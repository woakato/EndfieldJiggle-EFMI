#include <windows.h>
#include <filesystem>
#include <fstream>
#include <iostream>

int wmain(int argc, wchar_t** argv) {
    if (argc != 3 || wcscmp(argv[1], L"--background") != 0) return 2;
    const auto root = std::filesystem::path(argv[2]);
    std::ofstream(root / L"pid.txt") << GetCurrentProcessId();
    if (root.filename() == L"failed") {
        std::cerr << "Fixture rejected initialization.\n";
        return 3;
    }
    if (root.filename() == L"ready") std::cout << "EJ_INPUT_GUARD_READY\n" << std::flush;
    const auto deadline = GetTickCount64() + 30000;
    while (GetTickCount64() < deadline && !std::filesystem::exists(root / L"stop")) Sleep(10);
    return 0;
}
