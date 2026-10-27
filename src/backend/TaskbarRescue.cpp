// language: C++17, file: TaskbarRescue.cpp, target: Windows 11 x64 / MSVC, independent taskbar recovery
#include <windows.h>
#include <shellapi.h>
#include <fstream>
#include <string>

static BOOL CALLBACK show_bar(HWND window, LPARAM interactive) {
    wchar_t name[128]{};
    GetClassNameW(window, name, 128);
    if (wcscmp(name, L"Shell_TrayWnd") == 0 || wcscmp(name, L"Shell_SecondaryTrayWnd") == 0)
        ShowWindow(window, SW_SHOWNA);
    if (interactive) {
        wchar_t title[128]{};
        GetWindowTextW(window, title, 128);
        if (wcscmp(title, L"Panneaux \u2014 barre des t\u00e2ches") == 0) {
            APPBARDATA custom{};
            custom.cbSize = sizeof(custom);
            custom.hWnd = window;
            SHAppBarMessage(ABM_REMOVE, &custom);
            ShowWindowAsync(window, SW_HIDE);
        }
    }
    return TRUE;
}

int WINAPI wWinMain(HINSTANCE,HINSTANCE,PWSTR,int) {
    int count = 0;
    auto args = CommandLineToArgvW(GetCommandLineW(), &count);
    const bool watch = count == 5 && wcscmp(args[1], L"--watch-parent") == 0;
    if (watch) {
        HANDLE parent = OpenProcess(SYNCHRONIZE, FALSE, wcstoul(args[2], nullptr, 10));
        HANDLE ready = OpenEventW(EVENT_MODIFY_STATE, FALSE, args[3]);
        HANDLE release = OpenEventW(SYNCHRONIZE, FALSE, args[4]);
        if (!parent || !ready || !release) {
            if (parent) CloseHandle(parent);
            if (ready) CloseHandle(ready);
            if (release) CloseHandle(release);
            LocalFree(args);
            return 2;
        }
        SetEvent(ready);
        HANDLE signals[]{parent, release};
        WaitForMultipleObjects(2, signals, FALSE, INFINITE);
        CloseHandle(parent);
        CloseHandle(ready);
        CloseHandle(release);
    } else if (HANDLE signal = OpenEventW(EVENT_MODIFY_STATE, FALSE, L"Local\\PanneauxGlassTaskbarRestore")) {
        SetEvent(signal);
        CloseHandle(signal);
    }
    if (args) LocalFree(args);
    wchar_t path[MAX_PATH]{};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    std::wstring folder(path);
    folder.resize(folder.find_last_of(L"\\/") + 1);
    std::ifstream file(folder + L"taskbar-session.json", std::ios::binary);
    const std::string json((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    int state = ABS_ALWAYSONTOP;
    auto key = json.find("\"State\"");
    if (key == std::string::npos) key = json.find("\"state\"");
    if (key != std::string::npos) {
        auto colon = json.find(':', key);
        auto digit = json.find_first_of("0123", colon);
        if (digit != std::string::npos) state = json[digit] - '0';
    }
    APPBARDATA bar{};
    bar.cbSize = sizeof(bar);
    bar.hWnd = FindWindowW(L"Shell_TrayWnd", nullptr);
    bar.lParam = state;
    SHAppBarMessage(ABM_SETSTATE, &bar);
    EnumWindows(show_bar, watch ? 0 : 1);
    return 0;
}
