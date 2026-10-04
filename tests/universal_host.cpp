#include <windows.h>
#include <d3d11_1.h>
#include <wrl/client.h>
#include <climits>
#include <cstring>
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
    unsigned fixtureGroup, offset, count, indexBits;
    std::string indexPath, shaderPath;
};
int wmain(int argc, wchar_t** argv) {
    if (argc != 6) return 2;
    try {
        auto modulePath = std::filesystem::absolute(argv[1]);
        std::filesystem::current_path(modulePath.parent_path());
        std::ifstream manifest(argv[2]);
        if (!manifest) throw std::runtime_error("Cannot read draw manifest.");
        std::vector<CapturedDraw> draws;
        std::string line;
        while (std::getline(manifest, line)) {
            std::istringstream row(line);
            CapturedDraw draw{};
            if (!(row >> draw.fixtureGroup >> draw.offset >> draw.count >> draw.indexBits >>
                draw.indexPath >> draw.shaderPath) || (draw.indexBits != 16 && draw.indexBits != 32))
                throw std::runtime_error("Invalid captured draw row.");
            draws.push_back(draw);
        }
        WNDCLASSW wc{};
        wc.lpfnWndProc = DefWindowProcW;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"EFMIUniversalNativeTest";
        if (!RegisterClassW(&wc)) throw std::runtime_error("RegisterClass.");
        HWND window = CreateWindowW(wc.lpszClassName, L"EFMI universal native test",
            WS_OVERLAPPEDWINDOW, 0, 0, 320, 180, nullptr, nullptr, wc.hInstance, nullptr);
        if (!window) throw std::runtime_error("CreateWindow.");
        HMODULE module = LoadLibraryExW(modulePath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (!module) throw std::runtime_error("LoadLibrary.");
        auto create = reinterpret_cast<CreateDevice>(GetProcAddress(module, "D3D11CreateDeviceAndSwapChain"));
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
        check(create(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, levels, 1, D3D11_SDK_VERSION,
            &desc, &swap, &device, &level, &context), "create device");
        ComPtr<ID3D11DeviceContext1> rangedContext;
        check(context.As(&rangedContext), "ranged native context");
        auto buffer = [&](const std::vector<char>& bytes, UINT flags) {
            if (bytes.empty() || bytes.size() > UINT_MAX) throw std::runtime_error("Invalid buffer.");
            D3D11_BUFFER_DESC description{};
            description.ByteWidth = static_cast<UINT>(bytes.size());
            description.BindFlags = flags;
            D3D11_SUBRESOURCE_DATA initial{bytes.data(), 0, 0};
            ComPtr<ID3D11Buffer> result;
            check(device->CreateBuffer(&description, &initial, &result), "create buffer");
            return result;
        };
        auto shader = [&](const std::filesystem::path& path) {
            auto code = read(path);
            ComPtr<ID3D11VertexShader> result;
            check(device->CreateVertexShader(code.data(), code.size(), nullptr, &result), "create native shader");
            return result;
        };
        auto colorA = shader(argv[3]);
        auto colorB = shader(argv[4]);
        auto auxiliary = shader(argv[5]);
        std::map<std::string, ComPtr<ID3D11Buffer>> indices;
        std::map<std::string, ComPtr<ID3D11VertexShader>> shaders;
        for (auto& draw : draws) {
            if (!indices.count(draw.indexPath))
                indices.emplace(draw.indexPath, buffer(read(draw.indexPath), D3D11_BIND_INDEX_BUFFER));
            if (!shaders.count(draw.shaderPath)) shaders.emplace(draw.shaderPath, shader(draw.shaderPath));
        }
        auto camera = buffer(std::vector<char>(96 * 16), D3D11_BIND_CONSTANT_BUFFER);
        auto instance = buffer(std::vector<char>(4096 * 16), D3D11_BIND_CONSTANT_BUFFER);
        auto material = buffer(std::vector<char>(32 * 16), D3D11_BIND_CONSTANT_BUFFER);
        auto shortBuffer = buffer(std::vector<char>(32 * 16), D3D11_BIND_CONSTANT_BUFFER);
        auto bindConstants = [&] {
            ID3D11Buffer* constants[] = {camera.Get(), instance.Get(), instance.Get(), material.Get()};
            context->VSSetConstantBuffers(0, 4, constants);
        };
        bindConstants();
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        std::vector<char> freshBytes(6 * sizeof(unsigned));
        unsigned freshIndices[] = {2, 1, 0, 0, 2, 1};
        memcpy(freshBytes.data(), freshIndices, sizeof(freshIndices));
        auto fresh = buffer(freshBytes, D3D11_BIND_INDEX_BUFFER);
        auto ordinary = [&](ID3D11VertexShader* program, UINT count = 3, UINT first = 0) {
            context->VSSetShader(program, nullptr, 0);
            context->IASetIndexBuffer(fresh.Get(), DXGI_FORMAT_R32_UINT, 0);
            context->DrawIndexed(count, first, 0);
        };
        auto instanced = [&](UINT count, UINT instances, UINT firstInstance = 0) {
            context->VSSetShader(colorA.Get(), nullptr, 0);
            context->IASetIndexBuffer(fresh.Get(), DXGI_FORMAT_R32_UINT, 0);
            context->DrawIndexedInstanced(count, instances, 0, 0, firstInstance);
        };
        auto present = [&](const char* name) {
            check(swap->Present(0, 0), "present scenario");
            std::cout << "scenario=" << name << '\n';
        };
        present("initial");
        ordinary(colorA.Get()); present("unknown-ib-color-a");
        ordinary(colorA.Get()); present("only-color-a-warms");
        ordinary(colorB.Get(), 3, 3); present("only-color-b-nonzero-first-index");
        instanced(3, 1); present("single-instance");
        ordinary(auxiliary.Get()); present("auxiliary-does-not-certify-color");
        ID3D11Buffer* shortValue = shortBuffer.Get();
        UINT firstConstant = 0;
        UINT shortCount = 32;
        rangedContext->VSSetConstantBuffers1(0, 1, &shortValue, &firstConstant, &shortCount);
        ordinary(colorA.Get()); present("short-camera-rejected");
        bindConstants();
        rangedContext->VSSetConstantBuffers1(2, 1, &shortValue, &firstConstant, &shortCount);
        ordinary(colorA.Get()); present("short-instance-rejected");
        bindConstants();
        instanced(3, 2); present("multiple-instances-rejected");
        instanced(3, 1, 1); present("nonzero-first-instance-rejected");
        context->VSSetShader(colorA.Get(), nullptr, 0);
        context->Draw(3, 0); present("nonindexed-rejected");
        ordinary(colorA.Get(), 2); present("short-index-range-rejected");
        ordinary(colorA.Get(), 0); present("empty-index-range-rejected");
        present("absence-clears-session");
        ordinary(colorA.Get(), 6); present("different-index-count");
        ordinary(colorA.Get(), 6); present("different-index-count-warms");
        // The isolated observer asserts UI navigation before this frame, then releases it.
        ordinary(colorA.Get()); present("ui-navigation-cancels");
        ordinary(colorB.Get()); present("after-navigation-warmup");
        ordinary(colorB.Get()); present("after-navigation-ready");
        for (unsigned group = 1; group <= 3; ++group) {
            for (auto& draw : draws) {
                if (draw.fixtureGroup != group) continue;
                context->VSSetShader(shaders.at(draw.shaderPath).Get(), nullptr, 0);
                context->IASetIndexBuffer(indices.at(draw.indexPath).Get(),
                    draw.indexBits == 16 ? DXGI_FORMAT_R16_UINT : DXGI_FORMAT_R32_UINT, draw.offset);
                context->DrawIndexedInstanced(draw.count, 1, 0, 0, 0);
            }
            present(group == 1 ? "captured-fixture-one" :
                group == 2 ? "captured-fixture-two" : "captured-fixture-three");
        }
        for (auto& draw : draws) {
            context->VSSetShader(shaders.at(draw.shaderPath).Get(), nullptr, 0);
            context->IASetIndexBuffer(indices.at(draw.indexPath).Get(),
                draw.indexBits == 16 ? DXGI_FORMAT_R16_UINT : DXGI_FORMAT_R32_UINT, draw.offset);
            context->DrawIndexedInstanced(draw.count - 1, 1, 0, 0, 0);
        }
        present("all-captured-counts-changed-no-whitelist");
        present("final-absence");
        context->ClearState();
        context->Flush();
        DestroyWindow(window);
        std::cout << "captured-draw-rows=" << draws.size() << '\n';
        std::cout << "universal-native-draw-protocol-ok\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
