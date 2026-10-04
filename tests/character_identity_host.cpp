#include <windows.h>
#include <d3d11_1.h>
#include <wrl/client.h>
#include <climits>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <sstream>
#include <stdexcept>
#include <vector>

using Microsoft::WRL::ComPtr;
using CreateDevice = HRESULT(WINAPI*)(IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT,
    const D3D_FEATURE_LEVEL*, UINT, UINT, const DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**,
    ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**);

static void check(HRESULT result, const char* operation) {
    if (FAILED(result)) throw std::runtime_error(operation);
}

static std::vector<char> read(const std::filesystem::path& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file) throw std::runtime_error("Cannot read isolated input.");
    return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}

struct CapturedDraw {
    unsigned character, offset, count, indexBits;
    std::string indexPath, shaderPath;
};

int wmain(int argc, wchar_t** argv) {
    if (argc != 3) return 2;
    try {
        const auto modulePath = std::filesystem::absolute(argv[1]);
        std::filesystem::current_path(modulePath.parent_path());
        std::ifstream manifest(argv[2]);
        if (!manifest) throw std::runtime_error("Cannot read draw manifest.");
        std::vector<CapturedDraw> draws;
        std::string line;
        while (std::getline(manifest, line)) {
            std::istringstream row(line);
            CapturedDraw draw{};
            if (!(row >> draw.character >> draw.offset >> draw.count >> draw.indexBits >> draw.indexPath >> draw.shaderPath) ||
                (draw.indexBits != 16 && draw.indexBits != 32))
                throw std::runtime_error("Invalid captured draw row.");
            draws.push_back(draw);
        }
        WNDCLASSW wc{};
        wc.lpfnWndProc = DefWindowProcW;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"EFMIExactCharacterTest";
        if (!RegisterClassW(&wc)) throw std::runtime_error("RegisterClass.");
        HWND window = CreateWindowW(wc.lpszClassName, L"EFMI exact captured character test",
            WS_OVERLAPPEDWINDOW, 0, 0, 320, 180, nullptr, nullptr, wc.hInstance, nullptr);
        if (!window) throw std::runtime_error("CreateWindow.");
        HMODULE module = LoadLibraryExW(modulePath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (!module) throw std::runtime_error("LoadLibrary.");
        const auto create = reinterpret_cast<CreateDevice>(GetProcAddress(module, "D3D11CreateDeviceAndSwapChain"));
        if (!create) throw std::runtime_error("Missing D3D11 factory.");
        DXGI_SWAP_CHAIN_DESC desc{};
        desc.BufferDesc.Width = 320;
        desc.BufferDesc.Height = 180;
        desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.SampleDesc.Count = 1;
        desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        desc.BufferCount = 1;
        desc.OutputWindow = window;
        desc.Windowed = TRUE;
        desc.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
        D3D_FEATURE_LEVEL levels[] = {D3D_FEATURE_LEVEL_11_0};
        D3D_FEATURE_LEVEL level{};
        ComPtr<ID3D11Device> device;
        ComPtr<ID3D11DeviceContext> context;
        ComPtr<IDXGISwapChain> swap;
        check(create(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, levels, 1,
            D3D11_SDK_VERSION, &desc, &swap, &device, &level, &context), "create device");
        auto buffer = [&](const std::vector<char>& bytes, UINT flags) {
            if (bytes.empty() || bytes.size() > UINT_MAX) throw std::runtime_error("Invalid input buffer size.");
            D3D11_BUFFER_DESC description{};
            description.ByteWidth = static_cast<UINT>(bytes.size());
            description.BindFlags = flags;
            D3D11_SUBRESOURCE_DATA initial{bytes.data(), 0, 0};
            ComPtr<ID3D11Buffer> result;
            check(device->CreateBuffer(&description, &initial, &result), "create buffer");
            return result;
        };
        std::map<std::string, ComPtr<ID3D11Buffer>> indices;
        std::map<std::string, ComPtr<ID3D11VertexShader>> shaders;
        for (const auto& draw : draws) {
            if (!indices.count(draw.indexPath))
                indices.emplace(draw.indexPath, buffer(read(draw.indexPath), D3D11_BIND_INDEX_BUFFER));
            if (!shaders.count(draw.shaderPath)) {
                const auto code = read(draw.shaderPath);
                ComPtr<ID3D11VertexShader> shader;
                check(device->CreateVertexShader(code.data(), code.size(), nullptr, &shader), "create native shader");
                shaders.emplace(draw.shaderPath, shader);
            }
        }
        auto camera = buffer(std::vector<char>(96 * 16), D3D11_BIND_CONSTANT_BUFFER);
        auto instance = buffer(std::vector<char>(4096 * 16), D3D11_BIND_CONSTANT_BUFFER);
        auto material = buffer(std::vector<char>(32 * 16), D3D11_BIND_CONSTANT_BUFFER);
        ID3D11Buffer* constants[] = {camera.Get(), instance.Get(), instance.Get(), material.Get()};
        context->VSSetConstantBuffers(0, 4, constants);
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        check(swap->Present(0, 0), "present initial loader configuration");
        auto submit = [&](const CapturedDraw& draw, bool reject) {
            context->VSSetShader(shaders.at(draw.shaderPath).Get(), nullptr, 0);
            const auto format = draw.indexBits == 16 ? DXGI_FORMAT_R16_UINT : DXGI_FORMAT_R32_UINT;
            context->IASetIndexBuffer(indices.at(draw.indexPath).Get(), format, draw.offset);
            context->DrawIndexedInstanced(draw.count - (reject ? 1 : 0), 1, 0, 0, 0);
        };
        // Actual mesh hooks populate session state; no seen/character variables are assigned by the fixture.
        for (unsigned character = 1; character <= 3; ++character) {
            for (unsigned frame = 0; frame < 3; ++frame) {
                for (const auto& draw : draws) if (draw.character == character) submit(draw, false);
                check(swap->Present(0, 0), "present captured character");
            }
        }
        for (const auto& draw : draws) submit(draw, true);
        check(swap->Present(0, 0), "present rejected counts");
        for (const auto& draw : draws) submit(draw, false);
        check(swap->Present(0, 0), "present multiple characters");
        check(swap->Present(0, 0), "present absent character");
        context->ClearState();
        context->Flush();
        DestroyWindow(window);
        std::cout << "captured-character-draw-rows=" << draws.size() << '\n';
        std::cout << "captured-character-identity-ok\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
