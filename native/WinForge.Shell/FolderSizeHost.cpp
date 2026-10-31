#include "FolderSizeProtocol.h"
#include "FolderSizeScanner.h"
#include <xamlom.h>
#include <cstdio>

int wmain(int argc, wchar_t** argv) {
    try {
        if (argc == 5 && wcscmp(argv[1], L"attach") == 0) {
            wchar_t* end{};
            auto processId = wcstoul(argv[2], &end, 10);
            if (!processId || *end || !std::filesystem::is_regular_file(argv[3])) return 2;
            auto module = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
            if (!module) throw std::runtime_error("Windows XAML diagnostics is unavailable.");
            auto initialize = reinterpret_cast<decltype(&InitializeXamlDiagnosticsEx)>(GetProcAddress(module, "InitializeXamlDiagnosticsEx"));
            if (!initialize) throw std::runtime_error("Windows XAML initialization is unavailable.");
            HRESULT result = E_FAIL;
            for (unsigned int i = 1; i <= 16; ++i) {
                auto endpoint = L"VisualDiagConnection" + std::to_wstring(i);
                result = initialize(endpoint.c_str(), processId, nullptr, argv[3], winforge::folders::ClassId, argv[4]);
                if (SUCCEEDED(result)) break;
            }
            std::printf("{\"hresult\":%ld}\n", result);
            return SUCCEEDED(result) ? 0 : 1;
        }
        if (argc == 3 && wcscmp(argv[1], L"scan") == 0) {
            winforge::folders::FolderScan scan(argv[2], 30);
            while (scan.Step([] { return true; }) == winforge::folders::ScanStatus::Pending) { }
            std::printf("{\"status\":%d,\"bytes\":%llu}\n", static_cast<int>(scan.Status()), scan.Bytes());
            return scan.Status() == winforge::folders::ScanStatus::Complete ? 0 : 1;
        }
        if (!((argc == 3 && wcscmp(argv[1], L"probe") == 0) || (argc == 4 && wcscmp(argv[1], L"prepare") == 0))) return 2;
        auto symbols = winforge::folders::ResolveStorageSymbols(argv[2]);
        if (argc == 4) {
            winforge::folders::Session session;
            if (!session.Open(argv[3]) || session.Get()->attached) throw std::runtime_error("Folder-size session is invalid or already connected.");
            session.Get()->symbols = symbols;
        }
        std::printf("{\"sizeRva\":%lu,\"collectionRva\":%lu,\"compareRva\":%lu}\n", symbols.size.rva, symbols.collection.rva, symbols.compare.rva);
        return 0;
    } catch (std::exception const& error) { std::fprintf(stderr, "%s\n", error.what()); return 1; }
}
