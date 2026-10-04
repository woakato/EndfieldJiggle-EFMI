#include <windows.h>
#include <d3d11_1.h>
#include <wrl/client.h>
#include <filesystem>
#include <fstream>
#include <iostream>
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
    if (!file) throw std::runtime_error("Cannot read shader.");
    return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}

int wmain(int argc, wchar_t** argv) {
    if (argc < 3) {
        std::cerr << "loader_host <isolated d3d11.dll> <original shader> ...\n";
        return 2;
    }
    try {
        const auto modulePath = std::filesystem::absolute(argv[1]);
        std::filesystem::current_path(modulePath.parent_path());
        WNDCLASSW wc{};
        wc.lpfnWndProc = DefWindowProcW;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"EndfieldJiggleOfflineHost";
        if (!RegisterClassW(&wc)) throw std::runtime_error("RegisterClass.");
        HWND window = CreateWindowW(wc.lpszClassName, L"EFMI isolated test",
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
        ComPtr<ID3D11Texture2D> backbuffer;
        ComPtr<ID3D11RenderTargetView> target;
        check(swap->GetBuffer(0, IID_PPV_ARGS(&backbuffer)), "backbuffer");
        check(device->CreateRenderTargetView(backbuffer.Get(), nullptr, &target), "rtv");
        auto rtv = target.Get();
        context->OMSetRenderTargets(1, &rtv, nullptr);
        D3D11_VIEWPORT viewport{0, 0, 320, 180, 0, 1};
        context->RSSetViewports(1, &viewport);
        float sentinelData[16]{};
        D3D11_BUFFER_DESC sentinelDesc{};
        sentinelDesc.ByteWidth = sizeof(sentinelData);
        sentinelDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        D3D11_SUBRESOURCE_DATA initial{sentinelData, 0, 0};
        ComPtr<ID3D11Buffer> sentinelBuffer;
        check(device->CreateBuffer(&sentinelDesc, &initial, &sentinelBuffer), "sentinel buffer");
        D3D11_SHADER_RESOURCE_VIEW_DESC viewDesc{};
        viewDesc.Format = DXGI_FORMAT_R32G32B32A32_FLOAT;
        viewDesc.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
        viewDesc.Buffer.NumElements = 4;
        ComPtr<ID3D11ShaderResourceView> sentinelView;
        check(device->CreateShaderResourceView(sentinelBuffer.Get(), &viewDesc, &sentinelView), "sentinel view");
        unsigned short indices[3]{};
        D3D11_BUFFER_DESC ibDesc{};
        ibDesc.ByteWidth = sizeof(indices);
        ibDesc.BindFlags = D3D11_BIND_INDEX_BUFFER;
        D3D11_SUBRESOURCE_DATA ibInitial{indices, 0, 0};
        ComPtr<ID3D11Buffer> ib;
        check(device->CreateBuffer(&ibDesc, &ibInitial, &ib), "index buffer");
        context->IASetIndexBuffer(ib.Get(), DXGI_FORMAT_R16_UINT, 0);
        const bool eligibilityTest = std::wstring(argv[2]) == L"--eligibility";
        const int firstShader = eligibilityTest ? 3 : 2;
        ComPtr<ID3D11DeviceContext1> context1;
        ComPtr<ID3D11Buffer> cameraBuffer, instanceBuffer, materialBuffer;
        if (eligibilityTest) {
            check(context.As(&context1), "ranged context");
            auto makeConstantBuffer = [&](UINT bytes, ComPtr<ID3D11Buffer>& buffer) {
                D3D11_BUFFER_DESC description{};
                description.ByteWidth = bytes;
                description.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
                std::vector<float> zeros(bytes / sizeof(float));
                D3D11_SUBRESOURCE_DATA data{zeros.data(), 0, 0};
                check(device->CreateBuffer(&description, &data, &buffer), "constant buffer");
            };
            makeConstantBuffer(96 * 16, cameraBuffer);
            makeConstantBuffer(4096 * 16, instanceBuffer);
            makeConstantBuffer(32 * 16, materialBuffer);
        }
        for (int i = firstShader; i < argc; ++i) {
            auto bytes = read(std::filesystem::absolute(argv[i]));
            ComPtr<ID3D11VertexShader> shader;
            check(device->CreateVertexShader(bytes.data(), bytes.size(), nullptr, &shader), "shader");
            context->VSSetShader(shader.Get(), nullptr, 0);
            context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            auto before = sentinelView.Get();
            context->VSSetShaderResources(119, 1, &before);
            if (eligibilityTest) {
                ID3D11Buffer* constantBuffers[] = {
                    cameraBuffer.Get(), instanceBuffer.Get(), instanceBuffer.Get(), materialBuffer.Get()
                };
                context->VSSetConstantBuffers(0, 4, constantBuffers);
                context->DrawIndexed(3, 0, 0);
                context->DrawIndexedInstanced(3, 1, 0, 0, 0);
                context->DrawIndexedInstanced(3, 2, 0, 0, 0);
                ID3D11Buffer* shortCamera = materialBuffer.Get();
                const UINT firstConstant = 0;
                const UINT constantCount = 32;
                context1->VSSetConstantBuffers1(0, 1, &shortCamera, &firstConstant, &constantCount);
                context->DrawIndexedInstanced(3, 1, 0, 0, 0);
            } else {
                context->DrawIndexed(0, 0, 0);
            }
            ComPtr<ID3D11ShaderResourceView> restored;
            context->VSGetShaderResources(119, 1, &restored);
            if (!restored) throw std::runtime_error("Foreign t119 binding was cleared.");
            ComPtr<ID3D11Resource> beforeResource, afterResource;
            before->GetResource(&beforeResource);
            restored->GetResource(&afterResource);
            D3D11_SHADER_RESOURCE_VIEW_DESC beforeDesc{}, afterDesc{};
            before->GetDesc(&beforeDesc);
            restored->GetDesc(&afterDesc);
            // XXMI may recreate the SRV; resource identity and view range must survive.
            if (beforeResource.Get() != afterResource.Get() ||
                beforeDesc.Format != afterDesc.Format ||
                beforeDesc.ViewDimension != afterDesc.ViewDimension ||
                beforeDesc.Buffer.FirstElement != afterDesc.Buffer.FirstElement ||
                beforeDesc.Buffer.NumElements != afterDesc.Buffer.NumElements) {
                throw std::runtime_error("Foreign t119 resource or view range was not restored.");
            }
            std::wcout << L"loaded " << argv[i] << L'\n';
            std::cout << "srv-restored\n";
        }
        check(swap->Present(0, 0), "present");
        context->ClearState();
        context->Flush();
        DestroyWindow(window);
        // The proxy's worker hooks remain loaded until this isolated process exits.
        std::cout << "isolated-loader-ok\n";
        return 0;
    } catch (const std::exception& e) {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
