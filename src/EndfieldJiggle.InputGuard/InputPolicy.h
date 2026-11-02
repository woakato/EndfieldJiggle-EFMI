#pragma once

#include <windows.h>

namespace ej::input {

struct Sample {
    bool enabled, foreground, alt, ctrl, shift, escape, buttons;
};

struct Decision {
    bool motion, buttons;
};

class Policy {
public:
    Decision Update(const Sample& input) {
        if (!input.alt) escapeLatch = false;
        if (input.alt && input.escape) escapeLatch = true;
        const bool eligible = input.enabled && input.foreground && input.alt &&
            !input.ctrl && !input.shift && !escapeLatch;
        if (!eligible) {
            drainButtons = drainButtons || (active && input.buttons);
            active = false;
        } else if (!active && !input.buttons) {
            active = true;
        }
        if (!input.buttons || !input.foreground || !input.enabled) drainButtons = false;
        return {active, active || drainButtons};
    }

private:
    bool active = false;
    bool escapeLatch = false;
    bool drainButtons = false;
};

inline bool IsMouseMessage(UINT message) {
    return message >= WM_MOUSEFIRST && message <= WM_MOUSELAST;
}

inline bool IsMotionMessage(UINT message) {
    return message == WM_MOUSEMOVE;
}

inline void FilterMessage(MSG& message, Decision decision) {
    if (IsMouseMessage(message.message) &&
        (IsMotionMessage(message.message) ? decision.motion : decision.buttons)) {
        message.message = WM_NULL;
        message.wParam = 0;
        message.lParam = 0;
    }
}

struct AbsolutePosition {
    LONG x = 0, y = 0;
    bool known = false;
};

inline void FilterMouse(RAWMOUSE& mouse, Decision decision, AbsolutePosition& absolute) {
    if (mouse.usFlags & MOUSE_MOVE_ABSOLUTE) {
        if (!decision.motion || !absolute.known) {
            absolute = {mouse.lLastX, mouse.lLastY, true};
        }
        if (decision.motion) {
            mouse.lLastX = absolute.x;
            mouse.lLastY = absolute.y;
        }
    } else if (decision.motion) {
        mouse.lLastX = 0;
        mouse.lLastY = 0;
    }
    if (decision.buttons) {
        mouse.usButtonFlags = 0;
        mouse.usButtonData = 0;
        mouse.ulRawButtons = 0;
    }
}

}
