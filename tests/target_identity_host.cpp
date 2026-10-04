#include <windows.h>
#include <d3d11_1.h>
#include <wrl/client.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;

using CreateDevice = HRESULT(WINAPI*)(
    IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT, const D3D_FEATURE_LEVEL*,
    UINT, UINT, const DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**,
    ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**);

static void check(HRESULT hr, const char* operation) {
    if (FAILED(hr)) {
        std::cerr << operation << " failed: 0x" << std::hex << hr << '\n';
        throw std::runtime_error(operation);
    }
}

static std::vector<char> read(const std::filesystem::path& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file) throw std::runtime_error("Cannot read input file.");
    return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}

static ComPtr<ID3D11Buffer> createIndexBuffer(
    ID3D11Device* device, const std::filesystem::path& path) {
    const auto bytes = read(path);
    constexpr size_t capturedBytes = 16u * 1024u * 1024u;
    if (bytes.size() != capturedBytes) {
        throw std::runtime_error("Captured index buffer must remain exactly 16 MiB.");
    }
    D3D11_BUFFER_DESC desc{};
    desc.ByteWidth = static_cast<UINT>(bytes.size());
    desc.BindFlags = D3D11_BIND_INDEX_BUFFER;
    D3D11_SUBRESOURCE_DATA initial{bytes.data(), 0, 0};
    ComPtr<ID3D11Buffer> buffer;
    check(device->CreateBuffer(&desc, &initial, &buffer), "CreateBuffer(index buffer)");
    return buffer;
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 13) {
        std::wcerr << L"target_identity_host <isolated d3d11.dll> <primary.buf> <primary offset> "
                      L"<follower.buf> <follower offset> <primary count> <follower count> "
                      L"<five original VS binaries>\n";
        return 2;
    }
    try {
        const auto modulePath = std::filesystem::absolute(argv[1]);
        std::filesystem::current_path(modulePath.parent_path());
        WNDCLASSW wc{};
        wc.lpfnWndProc = DefWindowProcW;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"EndfieldJiggleTargetIdentityHost";
        if (!RegisterClassW(&wc)) throw std::runtime_error("RegisterClass.");
        HWND window = CreateWindowW(wc.lpszClassName, L"EFMI target identity test",
            WS_OVERLAPPEDWINDOW, 0, 0, 320, 180, nullptr, nullptr, wc.hInstance, nullptr);
        if (!window) throw std::runtime_error("CreateWindow.");

        HMODULE module = LoadLibraryExW(modulePath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (!module) throw std::runtime_error("LoadLibrary.");
        auto create = reinterpret_cast<CreateDevice>(
            GetProcAddress(module, "D3D11CreateDeviceAndSwapChain"));
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
            D3D11_SDK_VERSION, &desc, &swap, &device, &level, &context), "create");

        const auto primary = createIndexBuffer(device.Get(), argv[2]);
        const auto follower = createIndexBuffer(device.Get(), argv[4]);
        const UINT primaryOffset = static_cast<UINT>(std::stoul(argv[3]));
        const UINT followerOffset = static_cast<UINT>(std::stoul(argv[5]));
        const UINT primaryCount = static_cast<UINT>(std::stoul(argv[6]));
        const UINT followerCount = static_cast<UINT>(std::stoul(argv[7]));
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

        for (int i = 8; i < 13; ++i) {
            const auto shaderBytes = read(std::filesystem::absolute(argv[i]));
            ComPtr<ID3D11VertexShader> shader;
            check(device->CreateVertexShader(shaderBytes.data(), shaderBytes.size(), nullptr, &shader),
                "CreateVertexShader");
            context->VSSetShader(shader.Get(), nullptr, 0);

            context->IASetIndexBuffer(primary.Get(), DXGI_FORMAT_R16_UINT, primaryOffset);
            context->DrawIndexed(primaryCount, 0, 0);
            context->DrawIndexedInstanced(primaryCount, 1, 0, 0, 0);

            context->IASetIndexBuffer(follower.Get(), DXGI_FORMAT_R16_UINT, followerOffset);
            context->DrawIndexedInstanced(followerCount, 1, 0, 0, 0);

            context->IASetIndexBuffer(primary.Get(), DXGI_FORMAT_R16_UINT, primaryOffset);
            context->DrawIndexed(primaryCount - 1, 0, 0);
            context->IASetIndexBuffer(follower.Get(), DXGI_FORMAT_R16_UINT, followerOffset);
            context->DrawIndexedInstanced(primaryCount, 1, 0, 0, 0);
            std::wcout << L"submitted captured identity cases for " << argv[i] << L'\n';
        }

        check(swap->Present(0, 0), "present");
        context->ClearState();
        context->Flush();
        DestroyWindow(window);
        std::cout << "target-identity-host-submitted\n";
        return 0;
    } catch (const std::exception& e) {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
