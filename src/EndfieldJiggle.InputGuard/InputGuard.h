#pragma once

#include "InputPolicy.h"
#include <string>

namespace ej::input {

constexpr LONG Protocol = 1;

struct alignas(8) Control {
    LONG protocol;
    volatile LONG enabled;
    volatile LONG status;
    LONG owner;
    volatile LONG64 heartbeat;
};

inline std::wstring ControlName(DWORD pid) {
    return L"Local\\EndfieldJiggle.InputGuard." + std::to_wstring(pid);
}

inline bool IsLive(Control* control, ULONGLONG now) {
    if (!control || control->protocol != Protocol ||
        InterlockedCompareExchange(&control->enabled, 0, 0) != 1) return false;
    const auto heartbeat = static_cast<ULONGLONG>(
        InterlockedCompareExchange64(&control->heartbeat, 0, 0));
    return heartbeat <= now && now - heartbeat < 1500;
}

bool Initialize(Control* control);
void Refresh();

#ifdef EJ_INPUT_GUARD_TESTING
void SetTestSample(Sample sample);
void StopForTest();
BOOL MovePhysicalCursorForTest(int x, int y);
#endif

}
