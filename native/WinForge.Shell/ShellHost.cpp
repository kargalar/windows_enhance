#include <windows.h>
#include <xamlom.h>
#include <cstdio>
#include <filesystem>
#include <tlhelp32.h>
#include "BridgeProtocol.h"

struct XamlRuntime {
    std::filesystem::path initializer;
    std::filesystem::path diagnostics;
};

static XamlRuntime FindWinUiRuntime(DWORD processId) {
    auto snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, processId);
    if (snapshot == INVALID_HANDLE_VALUE) return {};
    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    XamlRuntime result;
    if (Module32FirstW(snapshot, &entry)) do {
        if (_wcsicmp(entry.szModule, L"Microsoft.UI.Xaml.dll") != 0) continue;
        auto directory = std::filesystem::path(entry.szExePath).parent_path();
        for (auto name : {L"Microsoft.Internal.FrameworkUdk.dll", L"FrameworkUdk.dll"}) {
            auto candidate = directory / name;
            if (std::filesystem::is_regular_file(candidate)) { result = {candidate, entry.szExePath}; break; }
        }
        if (!result.initializer.empty()) break;
    } while (Module32NextW(snapshot, &entry));
    CloseHandle(snapshot);
    return result;
}

int wmain(int argc, wchar_t** argv) {
    if ((argc < 5 || argc > 7) || wcscmp(argv[1], L"attach") != 0) return 2;
    wchar_t* end{};
    auto pid = wcstoul(argv[2], &end, 10);
    if (!pid || *end || !std::filesystem::is_regular_file(argv[3])) return 2;
    auto winUi = argc == 7 && wcscmp(argv[6], L"winui") == 0;
    auto runtime = winUi ? FindWinUiRuntime(pid) : XamlRuntime{};
    if (winUi && runtime.initializer.empty()) { std::fwprintf(stderr, L"WinUI runtime not found (pid %lu).\n", pid); return 3; }
    auto module = winUi ? LoadLibraryExW(runtime.initializer.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS) :
        LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!module) { std::fwprintf(stderr, L"Initializer load failed: %lu %s\n", GetLastError(), runtime.initializer.c_str()); return 3; }
    using Initialize = decltype(&InitializeXamlDiagnosticsEx);
    auto initialize = reinterpret_cast<Initialize>(GetProcAddress(module, "InitializeXamlDiagnosticsEx"));
    if (!initialize) { std::fwprintf(stderr, L"Diagnostics export missing: %lu\n", GetLastError()); FreeLibrary(module); return 3; }
    HRESULT result = E_FAIL;
    auto requestedEndpoint = argc >= 6 ? wcstoul(argv[5], nullptr, 10) : 0UL;
    if (requestedEndpoint > 16) { FreeLibrary(module); return 2; }
    auto first = requestedEndpoint ? static_cast<unsigned int>(requestedEndpoint) : winUi ? 0U : 1U;
    auto last = requestedEndpoint ? first : 16U;
    for (unsigned int index = first; index <= last; ++index) {
        auto endpoint = std::wstring(winUi ? L"WinUIVisualDiagConnection" : L"VisualDiagConnection") + std::to_wstring(index);
        result = initialize(endpoint.c_str(), pid, winUi ? runtime.diagnostics.c_str() : nullptr,
            argv[3], winforge::shell::TapClassId, argv[4]);
        if (SUCCEEDED(result)) break;
    }
    std::wprintf(L"{\"processId\":%lu,\"hresult\":%ld}\n", pid, result);
    FreeLibrary(module);
    return SUCCEEDED(result) ? 0 : 1;
}
