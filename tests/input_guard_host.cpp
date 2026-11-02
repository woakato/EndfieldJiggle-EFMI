#include "InputGuard.h"
#include <iostream>
#include <stdexcept>
#include <cstring>
#include <filesystem>
#include <d3d11.h>
#include <wrl/client.h>

using namespace ej::input;
namespace {
int checks = 0;
void Check(bool condition, const char* name) {
    if (!condition) throw std::runtime_error(name);
    ++checks;
}

struct EfmiFixture {
    using Factory = HRESULT(WINAPI*)(IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT,
        const D3D_FEATURE_LEVEL*, UINT, UINT, const DXGI_SWAP_CHAIN_DESC*,
        IDXGISwapChain**, ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**);
    HMODULE library = nullptr;
    HWND window = nullptr;
    Microsoft::WRL::ComPtr<IDXGISwapChain> swap;
    Microsoft::WRL::ComPtr<ID3D11Device> device;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context;

    void Open(const wchar_t* path) {
        const auto absolute = std::filesystem::absolute(path);
        std::filesystem::current_path(absolute.parent_path());
        WNDCLASSW wc{};
        wc.lpfnWndProc = DefWindowProcW;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"EJInputGuardOfflineFixture";
        Check(RegisterClassW(&wc) != 0, "register isolated EFMI window");
        window = CreateWindowW(wc.lpszClassName, L"EJ input fixture", WS_OVERLAPPEDWINDOW,
            0, 0, 320, 180, nullptr, nullptr, wc.hInstance, nullptr);
        Check(window != nullptr, "create hidden isolated EFMI window");
        library = LoadLibraryExW(absolute.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        Check(library != nullptr, "load unchanged official EFMI DLL in fixture");
        const auto factory = reinterpret_cast<Factory>(GetProcAddress(library, "D3D11CreateDeviceAndSwapChain"));
        Check(factory != nullptr, "resolve official D3D11 factory");
        DXGI_SWAP_CHAIN_DESC desc{};
        desc.BufferDesc.Width = 320;
        desc.BufferDesc.Height = 180;
        desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.SampleDesc.Count = 1;
        desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        desc.BufferCount = 1;
        desc.OutputWindow = window;
        desc.Windowed = TRUE;
        D3D_FEATURE_LEVEL level = D3D_FEATURE_LEVEL_11_0;
        Check(SUCCEEDED(factory(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, &level, 1,
            D3D11_SDK_VERSION, &desc, swap.GetAddressOf(), device.GetAddressOf(),
            nullptr, context.GetAddressOf())), "create official EFMI WARP swap chain");
        Check(SUCCEEDED(swap->Present(0, 0)), "official EFMI present runs before input guard");
    }

    ~EfmiFixture() {
        context.Reset();
        device.Reset();
        swap.Reset();
        if (window) DestroyWindow(window);
        // The official loader owns process-wide hooks and remains loaded until exit.
    }
};

void TestPolicy() {
    Policy policy;
    auto sample = Sample{true, true, false, false, false, false, false};
    Check(!policy.Update(sample).motion, "startup passes camera input");
    sample.alt = true;
    Check(policy.Update(sample).motion, "Alt alone locks mouse motion");
    sample.buttons = true;
    Check(policy.Update(sample).buttons, "touch button remains isolated");
    sample.alt = false;
    auto released = policy.Update(sample);
    Check(!released.motion && released.buttons, "Alt release restores movement and drains a held button");
    sample.buttons = false;
    Check(!policy.Update(sample).buttons, "mouse release ends button draining");
    sample.buttons = true;
    sample.alt = true;
    Check(!policy.Update(sample).motion, "Alt does not steal a preexisting camera drag");
    sample.buttons = false;
    Check(policy.Update(sample).motion, "lock starts once the preexisting drag ends");
    sample.ctrl = true;
    Check(!policy.Update(sample).motion, "Ctrl chord bypasses guard");
    sample.ctrl = false;
    sample.shift = true;
    Check(!policy.Update(sample).motion, "Shift chord bypasses guard");
    sample.shift = false;
    sample.escape = true;
    Check(!policy.Update(sample).motion, "Escape unlocks while Alt is held");
    sample.escape = false;
    Check(!policy.Update(sample).motion, "Escape bypass persists until Alt release");
    sample.alt = false;
    policy.Update(sample);
    sample.alt = true;
    Check(policy.Update(sample).motion, "next Alt hold re-arms guard");
    sample.foreground = false;
    Check(!policy.Update(sample).motion, "focus loss unlocks");
    sample.foreground = true;
    sample.enabled = false;
    Check(!policy.Update(sample).motion, "companion loss unlocks");
    Control control{Protocol, 1, 0, 0, 10000};
    Check(IsLive(&control, 11000), "fresh heartbeat is live");
    Check(!IsLive(&control, 11500), "stale heartbeat fails open");
    Check(!IsLive(&control, 9999), "future heartbeat fails open");
    control.enabled = 0;
    Check(!IsLive(&control, 10000), "disabled companion fails open");
}

void TestPackets() {
    RAWMOUSE mouse{};
    mouse.lLastX = 120;
    mouse.lLastY = -42;
    mouse.usButtonFlags = RI_MOUSE_LEFT_BUTTON_DOWN | RI_MOUSE_WHEEL;
    mouse.usButtonData = WHEEL_DELTA;
    mouse.ulRawButtons = 1;
    mouse.ulExtraInformation = 42;
    auto original = mouse;
    AbsolutePosition absolute;
    FilterMouse(mouse, {}, absolute);
    Check(std::memcmp(&mouse, &original, sizeof(mouse)) == 0, "unlocked raw mouse is byte-identical");
    FilterMouse(mouse, {true, true}, absolute);
    Check(mouse.lLastX == 0 && mouse.lLastY == 0, "relative camera motion removed");
    Check(mouse.usButtonFlags == 0 && mouse.usButtonData == 0 && mouse.ulRawButtons == 0,
        "raw clicks and zoom removed");
    Check(mouse.ulExtraInformation == 42, "unrelated packet data retained");
    mouse = original;
    FilterMouse(mouse, {false, true}, absolute);
    Check(mouse.lLastX == 120 && mouse.usButtonFlags == 0, "button drain keeps camera movement");
    mouse.usFlags = MOUSE_MOVE_ABSOLUTE;
    mouse.lLastX = 30000;
    mouse.lLastY = 20000;
    FilterMouse(mouse, {}, absolute);
    mouse.lLastX = 40000;
    mouse.lLastY = 25000;
    FilterMouse(mouse, {true, true}, absolute);
    Check(mouse.lLastX == 30000 && mouse.lLastY == 20000, "absolute devices keep their last free position");
    for (UINT value : {WM_MOUSEMOVE, WM_MOUSEWHEEL, WM_MOUSEHWHEEL, WM_LBUTTONDOWN,
        WM_LBUTTONUP, WM_RBUTTONDBLCLK, WM_XBUTTONDOWN}) {
        MSG message{};
        message.message = value;
        FilterMessage(message, {true, true});
        Check(message.message == WM_NULL, "legacy camera message suppressed");
    }
    for (UINT value : {WM_TIMER, WM_KEYDOWN, WM_SYSKEYDOWN, WM_INPUT, WM_PAINT, WM_APP + 1}) {
        MSG message{};
        message.message = value;
        FilterMessage(message, {true, true});
        Check(message.message == value, "script, keyboard, rendering and raw-input cleanup messages retained");
    }
}

void TestActualHooks() {
    wchar_t self[MAX_PATH]{};
    GetModuleFileNameW(nullptr, self, MAX_PATH);
    HMODULE probe = LoadLibraryW((std::filesystem::path(self).parent_path() / L"input_cursor_probe.dll").c_str());
    Check(probe != nullptr, "load an external cursor-reader fixture");
    const auto readPicker = reinterpret_cast<BOOL(WINAPI*)(POINT*)>(GetProcAddress(probe, "ReadProbeCursor"));
    Check(readPicker != nullptr, "external cursor reader available");
    const auto cursorInfoApi = reinterpret_cast<const void*>(
        GetProcAddress(GetModuleHandleW(L"user32.dll"), "GetCursorInfo"));
    unsigned char cursorInfoCode[16]{};
    std::memcpy(cursorInfoCode, cursorInfoApi, sizeof(cursorInfoCode));
    Control control{Protocol, 0, 0, 0, 0};
    Check(Initialize(&control), "nine actual Win32 hooks installed");
    try {
        SetTestSample({true, true, true, false, false, false, false});
        POINT anchor{};
        Check(GetCursorPos(&anchor), "locked cursor position available");
        Check(SetCursorPos(anchor.x + 20, anchor.y + 20), "game recenter call succeeds without moving cursor");
        POINT after{};
        GetCursorPos(&after);
        Check(after.x == anchor.x && after.y == anchor.y, "game cursor delta remains zero");
        CURSORINFO cursor{sizeof(cursor)};
        Check(GetCursorInfo(&cursor), "EFMI cursor-info API remains available");
        Check(std::memcmp(cursorInfoCode, cursorInfoApi, sizeof(cursorInfoCode)) == 0,
            "EFMI GetCursorInfo code remains unmodified");
        POINT physical{};
        readPicker(&physical);
        Check(MovePhysicalCursorForTest(physical.x + 3, physical.y + 3), "physical cursor path can move");
        POINT picked{};
        readPicker(&picked);
        MovePhysicalCursorForTest(physical.x, physical.y);
        Check(picked.x != physical.x || picked.y != physical.y,
            "external picker module sees a moving cursor while game camera coordinates stay fixed");
        GetCursorPos(&after);
        Check(after.x == anchor.x && after.y == anchor.y, "physical movement does not move the game cursor");
        MSG message{};
        PeekMessageW(&message, nullptr, 0, 0, PM_NOREMOVE);
        Check(PostThreadMessageW(GetCurrentThreadId(), WM_MOUSEWHEEL, 120, 1), "post isolated wheel message");
        Check(PeekMessageW(&message, nullptr, WM_MOUSEWHEEL, WM_MOUSEWHEEL, PM_REMOVE) &&
            message.message == WM_NULL, "actual PeekMessageW removes camera zoom");
        Check(PostThreadMessageA(GetCurrentThreadId(), WM_LBUTTONDOWN, 1, 1), "post isolated button message");
        Check(GetMessageA(&message, nullptr, WM_LBUTTONDOWN, WM_LBUTTONDOWN) > 0 &&
            message.message == WM_NULL, "actual GetMessageA removes camera drag");
        const ULONGLONG started = GetTickCount64();
        int narrativeFrames = 0;
        while (narrativeFrames < 20) {
            PostThreadMessageW(GetCurrentThreadId(), WM_APP + 1, narrativeFrames, 0);
            Check(GetMessageW(&message, nullptr, WM_APP + 1, WM_APP + 1) > 0 &&
                message.message == WM_APP + 1, "narrative tick passes through during Alt hold");
            ++narrativeFrames;
            Sleep(1);
        }
        Check(GetTickCount64() > started, "system time keeps advancing during Alt hold");
        SetTestSample({true, true, false, false, false, false, false});
        PostThreadMessageA(GetCurrentThreadId(), WM_MOUSEWHEEL, 120, 1);
        Check(PeekMessageA(&message, nullptr, WM_MOUSEWHEEL, WM_MOUSEWHEEL, PM_REMOVE) &&
            message.message == WM_MOUSEWHEEL, "actual PeekMessageA restores zoom after Alt release");
        SetTestSample({true, true, true, false, false, true, false});
        PostThreadMessageW(GetCurrentThreadId(), WM_LBUTTONDOWN, 1, 1);
        Check(GetMessageW(&message, nullptr, WM_LBUTTONDOWN, WM_LBUTTONDOWN) > 0 &&
            message.message == WM_LBUTTONDOWN, "actual GetMessageW bypasses on Escape");
        StopForTest();
        FreeLibrary(probe);
    } catch (...) {
        StopForTest();
        FreeLibrary(probe);
        throw;
    }
}
}

int wmain(int argc, wchar_t** argv) {
    try {
        EfmiFixture efmi;
        if (argc == 3 && wcscmp(argv[1], L"--efmi") == 0) efmi.Open(argv[2]);
        else Check(argc == 1, "known test arguments");
        TestPolicy();
        TestPackets();
        TestActualHooks();
        if (efmi.swap) Check(SUCCEEDED(efmi.swap->Present(0, 0)), "official EFMI present runs after input guard");
        std::cout << "PASS: " << checks << " input policy, packet and actual Win32 hook checks.\n"
            "No game process was opened; no game camera or cutscene was tested.\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
