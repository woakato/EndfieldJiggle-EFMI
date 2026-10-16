#include <windows.h>
#include <d3d11.h>
#include <wrl/client.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <vector>

using Microsoft::WRL::ComPtr;

static void check(HRESULT hr, const char* operation) {
    if (FAILED(hr)) throw std::runtime_error(operation);
}

struct Vertex {
    std::array<float, 3> position, normal, tangent;
};

struct Frame {
    std::array<float, 4> position;
    std::array<float, 3> normal, tangent;
};

class Fixture {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11VertexShader> shader;
    ComPtr<ID3D11GeometryShader> streamShader;
    ComPtr<ID3D11InputLayout> layout;
    ComPtr<ID3D11Buffer> vertices, camera, control, output, staging;
    ComPtr<ID3D11ShaderResourceView> controlView;

    ComPtr<ID3D11Buffer> buffer(UINT size, UINT bind, UINT usage = D3D11_USAGE_DEFAULT,
        UINT access = 0) {
        D3D11_BUFFER_DESC description{};
        description.ByteWidth = size;
        description.BindFlags = bind;
        description.Usage = static_cast<D3D11_USAGE>(usage);
        description.CPUAccessFlags = access;
        ComPtr<ID3D11Buffer> result;
        check(device->CreateBuffer(&description, nullptr, &result), "create buffer");
        return result;
    }

public:
    Fixture(const std::filesystem::path& path, UINT outputCount) {
        D3D_FEATURE_LEVEL level = D3D_FEATURE_LEVEL_11_0;
        check(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, &level, 1,
            D3D11_SDK_VERSION, &device, nullptr, &context), "create WARP device");
        std::ifstream input(path, std::ios::binary);
        if (!input) throw std::runtime_error("read shader");
        std::vector<char> bytes{std::istreambuf_iterator<char>(input),
            std::istreambuf_iterator<char>()};
        check(device->CreateVertexShader(bytes.data(), bytes.size(), nullptr, &shader),
            "create vertex shader");
        D3D11_INPUT_ELEMENT_DESC inputs[] = {
            {"POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, 0, D3D11_INPUT_PER_VERTEX_DATA, 0},
            {"NORMAL", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, 12, D3D11_INPUT_PER_VERTEX_DATA, 0},
            {"TANGENT", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, 24, D3D11_INPUT_PER_VERTEX_DATA, 0}
        };
        check(device->CreateInputLayout(inputs, 3, bytes.data(), bytes.size(), &layout),
            "create input layout");
        D3D11_SO_DECLARATION_ENTRY outputs[] = {
            {0, "SV_Position", 0, 0, 4, 0},
            {0, "TEXCOORD", 0, 0, 3, 0},
            {0, "TEXCOORD", 1, 0, 3, 0}
        };
        const UINT stride = sizeof(Frame);
        check(device->CreateGeometryShaderWithStreamOutput(bytes.data(), bytes.size(),
            outputs, outputCount, &stride, 1, D3D11_SO_NO_RASTERIZED_STREAM, nullptr, &streamShader),
            "create stream-output shader");
        vertices = buffer(sizeof(Vertex), D3D11_BIND_VERTEX_BUFFER);
        camera = buffer(82 * 16, D3D11_BIND_CONSTANT_BUFFER);
        control = buffer(4 * 16, D3D11_BIND_SHADER_RESOURCE);
        output = buffer(sizeof(Frame), D3D11_BIND_STREAM_OUTPUT);
        staging = buffer(sizeof(Frame), 0, D3D11_USAGE_STAGING, D3D11_CPU_ACCESS_READ);
        D3D11_SHADER_RESOURCE_VIEW_DESC view{};
        view.Format = DXGI_FORMAT_R32G32B32A32_FLOAT;
        view.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
        view.Buffer.NumElements = 4;
        check(device->CreateShaderResourceView(control.Get(), &view, &controlView),
            "create control view");
    }

    Frame run(const Vertex& vertex, std::array<float, 3> offset, bool active,
        std::array<float, 3> anchor = {}, std::array<float, 3> cameraPosition = {},
        std::array<float, 3> previousOffset = {}) {
        std::array<float, 82 * 4> cameraData{};
        std::copy(cameraPosition.begin(), cameraPosition.end(), cameraData.begin() + 44 * 4);
        std::copy(cameraPosition.begin(), cameraPosition.end(), cameraData.begin() + 81 * 4);
        std::array<float, 16> controlData{
            offset[0], offset[1], offset[2], active ? 1.f : 0.f,
            anchor[0], anchor[1], anchor[2], 0.12f,
            previousOffset[0], previousOffset[1], previousOffset[2], 2.f, 1.f, 0.f, 0.f, 0.f
        };
        context->UpdateSubresource(vertices.Get(), 0, nullptr, &vertex, 0, 0);
        context->UpdateSubresource(camera.Get(), 0, nullptr, cameraData.data(), 0, 0);
        context->UpdateSubresource(control.Get(), 0, nullptr, controlData.data(), 0, 0);
        auto vb = vertices.Get();
        const UINT stride = sizeof(Vertex), zero = 0;
        context->IASetVertexBuffers(0, 1, &vb, &stride, &zero);
        context->IASetInputLayout(layout.Get());
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_POINTLIST);
        context->VSSetShader(shader.Get(), nullptr, 0);
        auto cb = camera.Get();
        context->VSSetConstantBuffers(0, 1, &cb);
        auto srv = controlView.Get();
        context->VSSetShaderResources(119, 1, &srv);
        context->GSSetShader(streamShader.Get(), nullptr, 0);
        const Frame cleared{};
        context->UpdateSubresource(output.Get(), 0, nullptr, &cleared, 0, 0);
        auto destination = output.Get();
        context->SOSetTargets(1, &destination, &zero);
        context->Draw(1, 0);
        context->SOSetTargets(0, nullptr, nullptr);
        context->CopyResource(staging.Get(), output.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        check(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped), "read output");
        Frame result{};
        std::memcpy(&result, mapped.pData, sizeof(result));
        context->Unmap(staging.Get(), 0);
        return result;
    }
};

static bool same(const Frame& frame, const Vertex& vertex, bool normalOnly) {
    return std::memcmp(frame.normal.data(), vertex.normal.data(), 3 * sizeof(float)) == 0 &&
        (normalOnly || std::memcmp(frame.tangent.data(), vertex.tangent.data(), 3 * sizeof(float)) == 0);
}

static bool finite(const Frame& frame) {
    for (float component : frame.normal) if (!std::isfinite(component)) return false;
    for (float component : frame.tangent) if (!std::isfinite(component)) return false;
    return true;
}

static float distance(std::array<float, 3> a, std::array<float, 3> b) {
    float result = 0;
    for (size_t i = 0; i < a.size(); ++i) result += (a[i] - b[i]) * (a[i] - b[i]);
    return std::sqrt(result);
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 3) {
        std::cerr << "surface_frame_host <compiled VS> <frame|normal|current|previous>\n";
        return 2;
    }
    try {
        const std::wstring mode = argv[2];
        if (mode != L"frame" && mode != L"normal" && mode != L"current" && mode != L"previous")
            throw std::runtime_error("unknown test mode");
        const bool normalOnly = mode == L"normal";
        const bool positional = mode == L"current" || mode == L"previous";
        Fixture fixture(argv[1], positional ? 1 : normalOnly ? 2 : 3);
        unsigned failures = 0, checks = 0;
        auto expect = [&](bool condition, const char* label) {
            ++checks;
            if (!condition) ++failures;
            std::cout << (condition ? "PASS " : "FAIL ") << label << '\n';
        };
        // Skinned, independently normalized basis vectors need not be orthogonal.
        const Vertex vertex{{0.02f, 0.01f, 0.f}, {0.f, 0.f, 1.f}, {0.8f, 0.f, 0.6f}};
        if (positional) {
            const bool previous = mode == L"previous";
            auto run = [&](const Vertex& input, bool active, std::array<float, 3> camera = {}) {
                return fixture.run(input, previous ? std::array<float, 3>{} :
                    std::array<float, 3>{0.f, 0.f, 0.04f}, active, camera, camera,
                    previous ? std::array<float, 3>{0.f, 0.f, 0.04f} : std::array<float, 3>{});
            };
            auto unchanged = [&](const Frame& frame, const Vertex& input) {
                return std::memcmp(frame.position.data(), input.position.data(), 12) == 0 &&
                    frame.position[3] == 1.f;
            };
            expect(unchanged(run(vertex, false), vertex), "disabled position is bit-identical");
            expect(unchanged(fixture.run(vertex, {}, true), vertex), "zero offset is bit-identical");
            const auto deformed = run(vertex, true);
            expect(deformed.position[2] > 0.001f && deformed.position[2] <= 0.04f,
                "local positional response is visible and bounded");
            auto outside = vertex;
            outside.position = {1.f, 0.f, 0.f};
            expect(unchanged(run(outside, true), outside), "outside position is bit-identical");
            const auto translated = run(vertex, true, {32768.f, 32768.f, 32768.f});
            expect(std::memcmp(deformed.position.data(), translated.position.data(), 16) == 0,
                "camera translation preserves displacement");
            const auto foreignOffset = fixture.run(vertex,
                previous ? std::array<float, 3>{0.f, 0.f, 0.04f} : std::array<float, 3>{},
                true, {}, {}, previous ? std::array<float, 3>{} :
                std::array<float, 3>{0.f, 0.f, 0.04f});
            expect(unchanged(foreignOffset, vertex), "current and previous controls are independent");
            std::cout << "checks=" << checks << " failures=" << failures << '\n';
            return failures ? 1 : 0;
        }
        expect(same(fixture.run(vertex, {}, false), vertex, normalOnly), "disabled frame is bit-identical");
        expect(same(fixture.run(vertex, {}, true), vertex, normalOnly), "zero displacement is bit-identical");
        auto outside = vertex;
        outside.position = {1.f, 0.f, 0.f};
        expect(same(fixture.run(outside, {0.f, 0.f, 0.04f}, true), outside, normalOnly),
            "outside support is bit-identical");
        const auto deformed = fixture.run(vertex, {0.f, 0.f, 0.04f}, true);
        expect(finite(deformed), "deformed frame is finite");
        expect(distance(deformed.normal, vertex.normal) > 0.001f, "inside normal responds");
        for (const auto tangent : {std::array<float, 3>{}, std::array<float, 3>{0.f, 0.f, 1.f}}) {
            auto degenerate = vertex;
            degenerate.tangent = tangent;
            expect(finite(fixture.run(degenerate, {0.f, 0.f, 0.04f}, true)),
                "degenerate tangent stays finite");
        }
        auto translated = vertex;
        const auto nearFrame = fixture.run(translated, {0.f, 0.f, 0.04f}, true);
        const auto farFrame = fixture.run(translated, {0.f, 0.f, 0.04f}, true,
            {32768.f, 32768.f, 32768.f}, {32768.f, 32768.f, 32768.f});
        std::cout << "translation-normal-error=" << distance(nearFrame.normal, farFrame.normal) << '\n';
        expect(distance(nearFrame.normal, farFrame.normal) < 0.01f,
            "camera translation preserves local derivative");
        bool sweepFinite = true, sweepInvariant = true, sweepUntouched = true;
        float maximumError = 0.f;
        for (int index = 0; index < 32; ++index) {
            auto sample = vertex;
            sample.position = {-0.16f + index * 0.01f, 0.01f, 0.f};
            const auto reference = fixture.run(sample, {0.02f, -0.01f, 0.03f}, true);
            for (const float magnitude : {0.f, 256.f, 4096.f, 32768.f, -32768.f}) {
                const std::array<float, 3> translation{magnitude, -magnitude, magnitude};
                const auto actual = fixture.run(sample, {0.02f, -0.01f, 0.03f},
                    true, translation, translation);
                sweepFinite = sweepFinite && finite(actual);
                const float error = distance(reference.normal, actual.normal);
                maximumError = std::max(maximumError, error);
                sweepInvariant = sweepInvariant && error < 0.0001f;
                if (std::abs(sample.position[0]) >= 0.12f)
                    sweepUntouched = sweepUntouched && same(actual, sample, normalOnly);
            }
        }
        std::cout << "sweep-samples=160 max-normal-error=" << maximumError << '\n';
        expect(sweepFinite, "support-boundary sweep stays finite");
        expect(sweepInvariant, "support-boundary sweep is translation invariant");
        expect(sweepUntouched, "support-boundary sweep preserves untouched basis");
        std::cout << "checks=" << checks << " failures=" << failures << '\n';
        return failures ? 1 : 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 2;
    }
}
