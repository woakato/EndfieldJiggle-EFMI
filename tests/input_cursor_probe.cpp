#include <windows.h>

extern "C" __declspec(dllexport) BOOL ReadProbeCursor(POINT* point) {
    volatile BOOL result = GetCursorPos(point);
    return result;
}
