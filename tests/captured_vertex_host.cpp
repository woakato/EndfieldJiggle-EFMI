#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <algorithm>
#include <array>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace fs = std::filesystem;

static void check(HRESULT result, const char* operation) {
    if (FAILED(result)) {
        std::cerr << operation << ": 0x" << std::hex << result << '\n';
        throw std::runtime_error(operation);
    }
}

static std::vector<char> read(const fs::path& path) {
    std::ifstream stream(path, std::ios::binary);
    if (!stream) throw std::runtime_error("Cannot read replay input");
    return {std::istreambuf_iterator<char>(stream), std::istreambuf_iterator<char>()};
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 5) {
        std::cerr << "captured_vertex_host <fixture directory> <vertex DXBC> <control buffer> <output buffer>\n";
        return 2;
    }
    try {
        const fs::path fixture = argv[1];
        std::ifstream metadata(fixture / "layout.txt");
        std::string version;
        UINT vertices = 0, inputCount = 0;
        metadata >> version >> vertices >> inputCount;
        if (version != "EJ_CAPTURE_V1" || !vertices || vertices > 1000000 || inputCount > 32)
            throw std::runtime_error("Invalid replay header");
        std::vector<std::string> inputNames(inputCount);
        std::vector<D3D11_INPUT_ELEMENT_DESC> inputs(inputCount);
        for (UINT i = 0; i < inputCount; ++i) {
            UINT format = 0;
            auto& input = inputs[i];
            metadata >> inputNames[i] >> input.SemanticIndex >> format
                >> input.InputSlot >> input.AlignedByteOffset;
            input.SemanticName = inputNames[i].c_str();
            input.Format = static_cast<DXGI_FORMAT>(format);
            input.InputSlotClass = D3D11_INPUT_PER_VERTEX_DATA;
            if (input.InputSlot > 15) throw std::runtime_error("Invalid vertex slot");
        }
        ComPtr<ID3D11Device> device;
        ComPtr<ID3D11DeviceContext> context;
        D3D_FEATURE_LEVEL level = D3D_FEATURE_LEVEL_11_0;
        check(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, &level, 1,
            D3D11_SDK_VERSION, &device, nullptr, &context), "Create WARP device");
        auto buffer = [&](const std::vector<char>& data, UINT bind, UINT stride = 0) {
            if (data.empty() || data.size() > 64 * 1024 * 1024)
                throw std::runtime_error("Invalid buffer size");
            D3D11_BUFFER_DESC description{};
            description.ByteWidth = static_cast<UINT>(data.size());
            description.BindFlags = bind;
            description.StructureByteStride = stride;
            if (stride) description.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
            D3D11_SUBRESOURCE_DATA initial{data.data(), 0, 0};
            ComPtr<ID3D11Buffer> result;
            check(device->CreateBuffer(&description, &initial, &result), "Create replay buffer");
            return result;
        };
        std::vector<ComPtr<ID3D11Buffer>> resources;
        UINT count = 0;
        metadata >> count;
        if (count > 16) throw std::runtime_error("Invalid vertex buffer count");
        for (UINT i = 0; i < count; ++i) {
            UINT slot = 0, stride = 0, offset = 0;
            metadata >> slot >> stride;
            if (slot > 15) throw std::runtime_error("Invalid vertex buffer slot");
            resources.push_back(buffer(read(fixture / ("vb" + std::to_string(slot) + ".bin")),
                D3D11_BIND_VERTEX_BUFFER));
            auto ptr = resources.back().Get();
            context->IASetVertexBuffers(slot, 1, &ptr, &stride, &offset);
        }
        metadata >> count;
        if (count > 14) throw std::runtime_error("Invalid constant buffer count");
        for (UINT i = 0; i < count; ++i) {
            UINT slot = 0;
            metadata >> slot;
            if (slot > 13) throw std::runtime_error("Invalid constant buffer slot");
            resources.push_back(buffer(read(fixture / ("cb" + std::to_string(slot) + ".bin")),
                D3D11_BIND_CONSTANT_BUFFER));
            auto ptr = resources.back().Get();
            context->VSSetConstantBuffers(slot, 1, &ptr);
        }
        if (!metadata) throw std::runtime_error("Truncated replay layout");
        auto boneBytes = read(fixture / "t0.bin");
        auto bones = buffer(boneBytes, D3D11_BIND_SHADER_RESOURCE, 16);
        D3D11_SHADER_RESOURCE_VIEW_DESC boneDescription{};
        boneDescription.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
        boneDescription.Format = DXGI_FORMAT_UNKNOWN;
        boneDescription.Buffer.NumElements = static_cast<UINT>(boneBytes.size() / 16);
        ComPtr<ID3D11ShaderResourceView> boneView;
        check(device->CreateShaderResourceView(bones.Get(), &boneDescription, &boneView), "Create bone view");
        auto bonePtr = boneView.Get();
        context->VSSetShaderResources(0, 1, &bonePtr);
        auto controlBytes = read(argv[3]);
        if (controlBytes.size() != 64) throw std::runtime_error("Invalid control buffer");
        auto control = buffer(controlBytes, D3D11_BIND_SHADER_RESOURCE);
        D3D11_SHADER_RESOURCE_VIEW_DESC controlDescription{};
        controlDescription.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
        controlDescription.Format = DXGI_FORMAT_R32G32B32A32_FLOAT;
        controlDescription.Buffer.NumElements = 4;
        ComPtr<ID3D11ShaderResourceView> controlView;
        check(device->CreateShaderResourceView(control.Get(), &controlDescription, &controlView), "Create control view");
        auto controlPtr = controlView.Get();
        context->VSSetShaderResources(119, 1, &controlPtr);
        auto shaderBytes = read(argv[2]);
        ComPtr<ID3D11VertexShader> shader;
        check(device->CreateVertexShader(shaderBytes.data(), shaderBytes.size(), nullptr, &shader), "Create vertex shader");
        ComPtr<ID3D11InputLayout> layout;
        check(device->CreateInputLayout(inputs.data(), inputCount, shaderBytes.data(), shaderBytes.size(), &layout),
            "Create captured input layout");
        ComPtr<ID3D11ShaderReflection> reflection;
        check(D3DReflect(shaderBytes.data(), shaderBytes.size(), IID_PPV_ARGS(&reflection)), "Reflect outputs");
        D3D11_SHADER_DESC shaderDescription{};
        check(reflection->GetDesc(&shaderDescription), "Describe outputs");
        std::vector<D3D11_SIGNATURE_PARAMETER_DESC> signatures(shaderDescription.OutputParameters);
        std::vector<D3D11_SO_DECLARATION_ENTRY> outputs;
        UINT stride = 0;
        for (UINT i = 0; i < shaderDescription.OutputParameters; ++i) {
            auto& signature = signatures[i];
            check(reflection->GetOutputParameterDesc(i, &signature), "Read output parameter");
            BYTE start = 0, components = 0;
            while (start < 4 && !(signature.Mask & (1 << start))) ++start;
            for (BYTE bit = start; bit < 4; ++bit) if (signature.Mask & (1 << bit)) ++components;
            if (start >= 4 || !components) throw std::runtime_error("Empty output signature");
            outputs.push_back({0, signature.SemanticName, signature.SemanticIndex, start, components, 0});
            stride += components * sizeof(float);
        }
        ComPtr<ID3D11GeometryShader> streamShader;
        check(device->CreateGeometryShaderWithStreamOutput(shaderBytes.data(), shaderBytes.size(),
            outputs.data(), static_cast<UINT>(outputs.size()), &stride, 1,
            D3D11_SO_NO_RASTERIZED_STREAM, nullptr, &streamShader), "Create output capture");
        std::vector<char> cleared(static_cast<size_t>(vertices) * stride);
        auto output = buffer(cleared, D3D11_BIND_STREAM_OUTPUT);
        D3D11_BUFFER_DESC stagingDescription{};
        stagingDescription.ByteWidth = static_cast<UINT>(cleared.size());
        stagingDescription.Usage = D3D11_USAGE_STAGING;
        stagingDescription.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Buffer> staging;
        check(device->CreateBuffer(&stagingDescription, nullptr, &staging), "Create output staging");
        D3D11_QUERY_DESC queryDescription{D3D11_QUERY_SO_STATISTICS, 0};
        ComPtr<ID3D11Query> query;
        check(device->CreateQuery(&queryDescription, &query), "Create output query");
        context->IASetInputLayout(layout.Get());
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_POINTLIST);
        context->VSSetShader(shader.Get(), nullptr, 0);
        context->GSSetShader(streamShader.Get(), nullptr, 0);
        auto outputPtr = output.Get();
        const UINT zero = 0;
        context->SOSetTargets(1, &outputPtr, &zero);
        context->Begin(query.Get());
        context->Draw(vertices, 0);
        context->End(query.Get());
        context->SOSetTargets(0, nullptr, nullptr);
        context->CopyResource(staging.Get(), output.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        check(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped), "Read captured vertices");
        std::ofstream destination(fs::path(argv[4]), std::ios::binary);
        destination.write(static_cast<const char*>(mapped.pData), cleared.size());
        context->Unmap(staging.Get(), 0);
        if (!destination) throw std::runtime_error("Write captured vertices");
        D3D11_QUERY_DATA_SO_STATISTICS statistics{};
        check(context->GetData(query.Get(), &statistics, sizeof(statistics), 0), "Read output statistics");
        if (statistics.NumPrimitivesWritten != vertices || statistics.PrimitivesStorageNeeded != vertices)
            throw std::runtime_error("Incomplete stream-output replay");
        std::cout << "{\"vertexCount\":" << vertices << ",\"stride\":" << stride
            << ",\"primitivesWritten\":" << statistics.NumPrimitivesWritten << ",\"outputs\":[";
        UINT byteOffset = 0;
        for (size_t i = 0; i < signatures.size(); ++i) {
            if (i) std::cout << ',';
            const auto& signature = signatures[i];
            std::cout << "{\"name\":\"" << signature.SemanticName << "\",\"index\":"
                << signature.SemanticIndex << ",\"components\":" << static_cast<UINT>(outputs[i].ComponentCount)
                << ",\"float\":" << (signature.ComponentType == D3D_REGISTER_COMPONENT_FLOAT32 ? "true" : "false")
                << ",\"byteOffset\":" << byteOffset << '}';
            byteOffset += outputs[i].ComponentCount * sizeof(float);
        }
        std::cout << "]}\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
