#include "InputGuard.h"
#include <MinHook.h>
#include <intrin.h>
#include <array>
#include <unordered_map>

namespace ej::input {
namespace {

Control* shared = nullptr;
SRWLOCK stateLock = SRWLOCK_INIT;
Policy policy;
bool active = false;
POINT anchor{};
RECT restoreClip{};
bool clipped = false;
std::unordered_map<HANDLE, AbsolutePosition> absolutePositions;
std::array<void*, 9> targets{};
size_t targetCount = 0;
#ifdef EJ_INPUT_GUARD_TESTING
Sample testSample{};
#endif

decltype(&GetCursorPos) realGetCursorPos = nullptr;
decltype(&SetCursorPos) realSetCursorPos = nullptr;
decltype(&ClipCursor) realClipCursor = nullptr;
decltype(&GetRawInputData) realGetRawInputData = nullptr;
decltype(&GetRawInputBuffer) realGetRawInputBuffer = nullptr;
decltype(&PeekMessageW) realPeekMessageW = nullptr;
decltype(&PeekMessageA) realPeekMessageA = nullptr;
decltype(&GetMessageW) realGetMessageW = nullptr;
decltype(&GetMessageA) realGetMessageA = nullptr;

struct Exclusive {
    Exclusive() { AcquireSRWLockExclusive(&stateLock); }
    ~Exclusive() { ReleaseSRWLockExclusive(&stateLock); }
};

bool Held(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

Sample ReadSample() {
#ifdef EJ_INPUT_GUARD_TESTING
    return testSample;
#else
    DWORD foregroundPid = 0;
    GetWindowThreadProcessId(GetForegroundWindow(), &foregroundPid);
    return {IsLive(shared, GetTickCount64()), foregroundPid == GetCurrentProcessId(),
        Held(VK_MENU), Held(VK_CONTROL), Held(VK_SHIFT), Held(VK_ESCAPE),
        Held(VK_LBUTTON) || Held(VK_RBUTTON) || Held(VK_MBUTTON) ||
        Held(VK_XBUTTON1) || Held(VK_XBUTTON2)};
#endif
}

Decision Evaluate() {
    const DWORD error = GetLastError();
    Exclusive lock;
    const auto sample = ReadSample();
    auto decision = policy.Update(sample);
    if (decision.motion && !active) {
        realGetCursorPos(&anchor);
        clipped = GetClipCursor(&restoreClip) != FALSE;
        realClipCursor(nullptr);
    } else if (!decision.motion && active && sample.foreground && clipped) {
        realClipCursor(&restoreClip);
    }
    active = decision.motion;
    SetLastError(error);
    return decision;
}

BOOL WINAPI GuardGetCursorPos(LPPOINT point) {
    MEMORY_BASIC_INFORMATION caller{};
    const bool knownCaller = VirtualQuery(_ReturnAddress(), &caller, sizeof(caller)) != 0;
    const bool gameCaller = knownCaller && (caller.AllocationBase == GetModuleHandleW(nullptr) ||
        caller.AllocationBase == GetModuleHandleW(L"UnityPlayer.dll") ||
        caller.AllocationBase == GetModuleHandleW(L"GameAssembly.dll"));
    // EFMI and User32 must keep reading the physical cursor for picking.
    if (!point || !gameCaller) return realGetCursorPos(point);
    const auto decision = Evaluate();
    if (!decision.motion) return realGetCursorPos(point);
    Exclusive lock;
    *point = anchor;
    return TRUE;
}

BOOL WINAPI GuardSetCursorPos(int x, int y) {
    return Evaluate().motion ? TRUE : realSetCursorPos(x, y);
}

BOOL WINAPI GuardClipCursor(const RECT* rect) {
    if (!Evaluate().motion) return realClipCursor(rect);
    Exclusive lock;
    if (rect) {
        restoreClip = *rect;
    } else {
        restoreClip = {GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_XVIRTUALSCREEN) + GetSystemMetrics(SM_CXVIRTUALSCREEN),
            GetSystemMetrics(SM_YVIRTUALSCREEN) + GetSystemMetrics(SM_CYVIRTUALSCREEN)};
    }
    clipped = true;
    return TRUE;
}

void FilterRaw(RAWINPUT& raw, Decision decision) {
    if (raw.header.dwType != RIM_TYPEMOUSE) return;
    Exclusive lock;
    FilterMouse(raw.data.mouse, decision, absolutePositions[raw.header.hDevice]);
}

UINT WINAPI GuardGetRawInputData(HRAWINPUT raw, UINT command, LPVOID data,
    PUINT size, UINT headerSize) {
    // Evaluate before reading: a short Alt press must not leak its first packet.
    const auto decision = Evaluate();
    const UINT result = realGetRawInputData(raw, command, data, size, headerSize);
    const DWORD error = GetLastError();
    if (result != static_cast<UINT>(-1) && command == RID_INPUT && data &&
        result >= sizeof(RAWINPUTHEADER) + sizeof(RAWMOUSE)) {
        FilterRaw(*static_cast<RAWINPUT*>(data), decision);
    }
    SetLastError(error);
    return result;
}

UINT WINAPI GuardGetRawInputBuffer(PRAWINPUT data, PUINT size, UINT headerSize) {
    using QWORD = ULONGLONG; // The Windows SDK's NEXTRAWINPUTBLOCK macro expects this alias.
    const auto decision = Evaluate();
    const UINT capacity = data && size ? *size : 0;
    const UINT result = realGetRawInputBuffer(data, size, headerSize);
    const DWORD error = GetLastError();
    if (result != static_cast<UINT>(-1) && data) {
        const auto end = reinterpret_cast<const BYTE*>(data) + capacity;
        auto packet = data;
        for (UINT i = 0; i < result; ++i) {
            if (reinterpret_cast<const BYTE*>(packet) + sizeof(RAWINPUTHEADER) > end ||
                packet->header.dwSize < sizeof(RAWINPUTHEADER) ||
                packet->header.dwSize > static_cast<size_t>(
                    end - reinterpret_cast<const BYTE*>(packet))) break;
            if (packet->header.dwSize >= sizeof(RAWINPUTHEADER) + sizeof(RAWMOUSE)) {
                FilterRaw(*packet, decision);
            }
            packet = NEXTRAWINPUTBLOCK(packet);
        }
    }
    SetLastError(error);
    return result;
}

BOOL WINAPI GuardPeekMessageW(LPMSG message, HWND window, UINT first, UINT last, UINT flags) {
    const BOOL result = realPeekMessageW(message, window, first, last, flags);
    const DWORD error = GetLastError();
    if (result && message) FilterMessage(*message, Evaluate());
    SetLastError(error);
    return result;
}

BOOL WINAPI GuardPeekMessageA(LPMSG message, HWND window, UINT first, UINT last, UINT flags) {
    const BOOL result = realPeekMessageA(message, window, first, last, flags);
    const DWORD error = GetLastError();
    if (result && message) FilterMessage(*message, Evaluate());
    SetLastError(error);
    return result;
}

BOOL WINAPI GuardGetMessageW(LPMSG message, HWND window, UINT first, UINT last) {
    const BOOL result = realGetMessageW(message, window, first, last);
    const DWORD error = GetLastError();
    if (result > 0 && message) FilterMessage(*message, Evaluate());
    SetLastError(error);
    return result;
}

BOOL WINAPI GuardGetMessageA(LPMSG message, HWND window, UINT first, UINT last) {
    const BOOL result = realGetMessageA(message, window, first, last);
    const DWORD error = GetLastError();
    if (result > 0 && message) FilterMessage(*message, Evaluate());
    SetLastError(error);
    return result;
}

template<class Function>
bool Hook(const char* name, Function replacement, Function* original) {
    auto target = reinterpret_cast<void*>(GetProcAddress(GetModuleHandleW(L"user32.dll"), name));
    if (!target || MH_CreateHook(target, reinterpret_cast<void*>(replacement),
        reinterpret_cast<void**>(original)) != MH_OK) return false;
    targets[targetCount++] = target;
    return true;
}

void RemoveHooks() {
    for (size_t i = 0; i < targetCount; ++i) MH_DisableHook(targets[i]);
    for (size_t i = 0; i < targetCount; ++i) MH_RemoveHook(targets[i]);
    targetCount = 0;
    MH_Uninitialize();
}

}

bool Initialize(Control* control) {
    shared = control;
    if (!control || control->protocol != Protocol || MH_Initialize() != MH_OK) return false;
    const bool created =
        Hook("GetCursorPos", GuardGetCursorPos, &realGetCursorPos) &&
        Hook("SetCursorPos", GuardSetCursorPos, &realSetCursorPos) &&
        Hook("ClipCursor", GuardClipCursor, &realClipCursor) &&
        Hook("GetRawInputData", GuardGetRawInputData, &realGetRawInputData) &&
        Hook("GetRawInputBuffer", GuardGetRawInputBuffer, &realGetRawInputBuffer) &&
        Hook("PeekMessageW", GuardPeekMessageW, &realPeekMessageW) &&
        Hook("PeekMessageA", GuardPeekMessageA, &realPeekMessageA) &&
        Hook("GetMessageW", GuardGetMessageW, &realGetMessageW) &&
        Hook("GetMessageA", GuardGetMessageA, &realGetMessageA);
    if (!created) {
        RemoveHooks();
        return false;
    }
    for (size_t i = 0; i < targetCount; ++i) {
        if (MH_QueueEnableHook(targets[i]) != MH_OK) {
            RemoveHooks();
            return false;
        }
    }
    if (MH_ApplyQueued() != MH_OK) {
        RemoveHooks();
        return false;
    }
    InterlockedExchange(&control->status, 1);
    return true;
}

void Refresh() { Evaluate(); }

#ifdef EJ_INPUT_GUARD_TESTING
void SetTestSample(Sample sample) {
    {
        Exclusive lock;
        testSample = sample;
    }
    Refresh();
}

BOOL MovePhysicalCursorForTest(int x, int y) { return realSetCursorPos(x, y); }

void StopForTest() {
    SetTestSample({});
    RemoveHooks();
}
#endif

}

#ifndef EJ_INPUT_GUARD_TESTING
namespace {
DWORD WINAPI GuardWorker(void*) {
    const auto name = ej::input::ControlName(GetCurrentProcessId());
    const HANDLE mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, name.c_str());
    if (!mapping) return 1;
    auto control = static_cast<ej::input::Control*>(
        MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(ej::input::Control)));
    if (!control) {
        CloseHandle(mapping);
        return 1;
    }
    // Hooks stay resident but become passive when the companion stops heartbeating.
    // Keeping the module loaded avoids unloading code another game thread is using.
    HMODULE pinned = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&GuardWorker), &pinned);
    if (!ej::input::Initialize(control)) {
        InterlockedExchange(&control->status, -1);
        UnmapViewOfFile(control);
        CloseHandle(mapping);
        return 1;
    }
    for (;;) {
        ej::input::Refresh();
        Sleep(8);
    }
}
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
        wchar_t path[MAX_PATH]{};
        GetModuleFileNameW(nullptr, path, MAX_PATH);
        const wchar_t* name = wcsrchr(path, L'\\');
        if (name && _wcsicmp(name + 1, L"Endfield.exe") == 0) {
            if (HANDLE worker = CreateThread(nullptr, 0, GuardWorker, nullptr, 0, nullptr)) {
                CloseHandle(worker);
            }
        }
    }
    return TRUE;
}
#endif
