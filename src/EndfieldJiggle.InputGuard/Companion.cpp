#include "InputGuard.h"
#include <tlhelp32.h>
#include <filesystem>
#include <iostream>
#include <vector>

namespace {
ej::input::Control* control = nullptr;
HANDLE mapping = nullptr;
HANDLE mutex = nullptr;
using Inject = int(__cdecl*)(DWORD, LPCWSTR, int);

BOOL WINAPI ConsoleControl(DWORD) {
    if (control) InterlockedExchange(&control->enabled, 0);
    return FALSE;
}

bool EqualPath(const std::filesystem::path& left, const std::filesystem::path& right) {
    return _wcsicmp(left.lexically_normal().c_str(), right.lexically_normal().c_str()) == 0;
}

std::vector<DWORD> FindGames() {
    std::vector<DWORD> result;
    const HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return result;
    PROCESSENTRY32W entry{sizeof(entry)};
    if (Process32FirstW(snapshot, &entry)) {
        do {
            if (_wcsicmp(entry.szExeFile, L"Endfield.exe") == 0) result.push_back(entry.th32ProcessID);
        } while (Process32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return result;
}

bool HasModule(DWORD pid, const std::filesystem::path& requested) {
    const HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, pid);
    if (snapshot == INVALID_HANDLE_VALUE) return false;
    MODULEENTRY32W entry{sizeof(entry)};
    bool found = false;
    if (Module32FirstW(snapshot, &entry)) {
        do {
            if (EqualPath(entry.szExePath, requested)) found = true;
        } while (Module32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return found;
}

int Run(int argc, wchar_t** argv) {
    const bool background = argc > 1 && wcscmp(argv[1], L"--background") == 0;
    const int first = background ? 2 : 1;
    if (argc > first + 1) {
        std::cerr << "Usage: EndfieldJiggleInputGuard.exe [--background] [EFMI directory]\n";
        return 2;
    }
    wchar_t self[MAX_PATH]{};
    GetModuleFileNameW(nullptr, self, MAX_PATH);
    auto directory = std::filesystem::path(self).parent_path();
    const auto guard = directory / L"EndfieldJiggleInputGuard.dll";
    std::filesystem::path efmi;
    if (argc == first + 1) {
        efmi = std::filesystem::absolute(argv[first]);
    } else {
        for (int depth = 0; depth < 24 && !directory.empty(); ++depth) {
            if (std::filesystem::is_regular_file(directory / L"d3dx.ini") &&
                std::filesystem::is_regular_file(directory / L"3dmloader.dll")) {
                efmi = directory;
                break;
            }
            if (directory == directory.parent_path()) break;
            directory = directory.parent_path();
        }
    }
    const auto loaderPath = efmi / L"3dmloader.dll";
    const auto d3dPath = efmi / L"d3d11.dll";
    if (efmi.empty() || !std::filesystem::is_regular_file(loaderPath) ||
        !std::filesystem::is_regular_file(d3dPath) ||
        !std::filesystem::is_regular_file(guard)) {
        std::cerr << "Missing official EFMI libraries or the adjacent input guard DLL.\n";
        return 3;
    }
    const auto games = FindGames();
    if (games.size() != 1 || !HasModule(games.front(), d3dPath)) {
        std::cerr << "Expected one Endfield.exe already using this EFMI directory.\n";
        return 4;
    }
    const DWORD pid = games.front();
    const auto mappingName = ej::input::ControlName(pid);
    mutex = CreateMutexW(nullptr, TRUE, (mappingName + L".Companion").c_str());
    if (!mutex || GetLastError() == ERROR_ALREADY_EXISTS) {
        std::cerr << "An input guard companion is already running for this game.\n";
        return 5;
    }
    mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0,
        sizeof(ej::input::Control), mappingName.c_str());
    if (!mapping) return 6;
    const bool existing = GetLastError() == ERROR_ALREADY_EXISTS;
    control = static_cast<ej::input::Control*>(
        MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(ej::input::Control)));
    if (!control || (existing && control->protocol != ej::input::Protocol)) return 6;
    control->protocol = ej::input::Protocol;
    control->owner = GetCurrentProcessId();
    InterlockedExchange(&control->enabled, 0);
    InterlockedExchange64(&control->heartbeat, static_cast<LONG64>(GetTickCount64()));
    SetConsoleCtrlHandler(ConsoleControl, TRUE);
    const bool alreadyLoaded = HasModule(pid, guard);
    if (!alreadyLoaded) {
        InterlockedExchange(&control->status, 0);
        const HMODULE loader = LoadLibraryExW(loaderPath.c_str(), nullptr,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!loader) return 7;
        const auto inject = reinterpret_cast<Inject>(GetProcAddress(loader, "Inject"));
        const int result = inject ? inject(pid, guard.c_str(), 10) : -1;
        FreeLibrary(loader);
        if (result != 0) {
            std::cerr << "Official loader returned " << result << ".\n";
            return 8;
        }
    }
    const auto deadline = GetTickCount64() + 5000;
    while (InterlockedCompareExchange(&control->status, 0, 0) == 0 &&
        GetTickCount64() < deadline) Sleep(20);
    if (InterlockedCompareExchange(&control->status, 0, 0) != 1) {
        std::cerr << "Input hooks did not become ready. Restart the game to retry.\n";
        return 9;
    }
    const HANDLE game = OpenProcess(SYNCHRONIZE, FALSE, pid);
    if (!game) return 10;
    std::cout << "EJ_INPUT_GUARD_READY\n" << std::flush;
    if (!background) std::cout << "ALT input guard ready. Hold Alt to isolate mouse input.\n"
        "Keep using Alt + left mouse to touch. Release Alt to restore camera input.\n"
        "Escape bypasses the guard until Alt is released. Ctrl+C stops this companion.\n"
        "Game focus, clocks, rendering, camera matrices and keyboard input are untouched.\n";
    InterlockedExchange(&control->enabled, 1);
    while (WaitForSingleObject(game, 100) == WAIT_TIMEOUT) {
        InterlockedExchange64(&control->heartbeat, static_cast<LONG64>(GetTickCount64()));
    }
    CloseHandle(game);
    return 0;
}
}

int wmain(int argc, wchar_t** argv) {
    int result = 1;
    try {
        result = Run(argc, argv);
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
    }
    if (control) {
        InterlockedExchange(&control->enabled, 0);
        UnmapViewOfFile(control);
    }
    if (mapping) CloseHandle(mapping);
    if (mutex) CloseHandle(mutex);
    if (result != 0 && argc == 1) {
        std::cerr << "Press Enter to close.\n";
        std::cin.get();
    }
    return result;
}
