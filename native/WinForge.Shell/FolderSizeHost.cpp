#include "FolderSizeProtocol.h"
#include "FolderSizeEngine.h"
#include <xamlom.h>
#include <cstring>
#include <cstdio>

int wmain(int argc, wchar_t** argv) {
    try {
        if (argc == 2 && wcscmp(argv[1], L"verify-notification-parser") == 0) {
            constexpr auto headerBytes = offsetof(FILE_NOTIFY_INFORMATION, FileName);
            constexpr wchar_t name[] = L"nested\\file.bin";
            constexpr DWORD nameBytes = static_cast<DWORD>((std::size(name) - 1) * sizeof(wchar_t));
            constexpr wchar_t secondName[] = L"nested\\second.bin";
            constexpr DWORD secondNameBytes = static_cast<DWORD>((std::size(secondName) - 1) * sizeof(wchar_t));
            constexpr DWORD secondOffset = (headerBytes + nameBytes + alignof(DWORD) - 1) & ~(alignof(DWORD) - 1);
            std::array<BYTE, 128> valid{};
            auto* validRecord = reinterpret_cast<FILE_NOTIFY_INFORMATION*>(valid.data());
            validRecord->NextEntryOffset = secondOffset;
            validRecord->Action = FILE_ACTION_MODIFIED;
            validRecord->FileNameLength = nameBytes;
            std::memcpy(valid.data() + headerBytes, name, nameBytes);
            auto* secondRecord = reinterpret_cast<FILE_NOTIFY_INFORMATION*>(valid.data() + secondOffset);
            secondRecord->NextEntryOffset = 0;
            secondRecord->Action = FILE_ACTION_ADDED;
            secondRecord->FileNameLength = secondNameBytes;
            std::memcpy(valid.data() + secondOffset + headerBytes, secondName, secondNameBytes);
            auto decoded = winforge::folders::DecodeDirectoryChangeBatch(L"C:\\root", valid.data(),
                secondOffset + headerBytes + secondNameBytes);

            std::array<BYTE, 32> malformed{};
            auto* malformedRecord = reinterpret_cast<FILE_NOTIFY_INFORMATION*>(malformed.data());
            malformedRecord->NextEntryOffset = 0;
            malformedRecord->Action = FILE_ACTION_MODIFIED;
            malformedRecord->FileNameLength = 128;
            auto malformedBatch = winforge::folders::DecodeDirectoryChangeBatch(L"C:\\root", malformed.data(),
                static_cast<DWORD>(headerBytes + sizeof(wchar_t)));
            auto const expected = std::wstring(L"C:\\root\\nested\\file.bin");
            auto validPath = !decoded.overflow && decoded.paths.size() == 2 && decoded.paths.front() == expected;
            std::printf("{\"validPath\":\"%s\",\"recordCount\":%zu,\"malformedAccepted\":%s,\"malformedOverflow\":%s}\n",
                validPath ? "C:\\\\root\\\\nested\\\\file.bin" : "", decoded.paths.size(),
                malformedBatch.overflow ? "false" : "true", malformedBatch.overflow ? "true" : "false");
            return 0;
        }
        if (argc == 2 && wcscmp(argv[1], L"verify-scan-priority") == 0) {
            auto priorityBeforeOlder = winforge::folders::ScanRequestPrecedes(true, 1, false, 2);
            auto recentBeforeOld = winforge::folders::ScanRequestPrecedes(false, 2, false, 1);
            std::printf("{\"priorityBeforeOlder\":%s,\"recentBeforeOld\":%s}\n",
                priorityBeforeOlder ? "true" : "false", recentBeforeOld ? "true" : "false");
            return 0;
        }
        if (argc == 5 && wcscmp(argv[1], L"scan-snapshot") == 0) {
            wchar_t* directoryEnd{};
            wchar_t* pathCharactersEnd{};
            auto maximumDirectories = wcstoull(argv[3], &directoryEnd, 10);
            auto maximumPathCharacters = wcstoull(argv[4], &pathCharactersEnd, 10);
            if (!directoryEnd || *directoryEnd || !pathCharactersEnd || *pathCharactersEnd ||
                maximumDirectories > SIZE_MAX || maximumPathCharacters > SIZE_MAX) return 2;
            auto root = winforge::folders::NormalizePath(argv[2]);
            winforge::folders::FolderScan scan(argv[2], 30, {
                static_cast<std::size_t>(maximumDirectories), static_cast<std::size_t>(maximumPathCharacters)});
            while (scan.Step([] { return true; }) == winforge::folders::ScanStatus::Pending) { }
            auto snapshot = scan.TakeSnapshot();
            std::printf("{\"status\":%d,\"bytes\":%llu,\"snapshotComplete\":%s,\"directories\":[",
                static_cast<int>(scan.Status()), static_cast<unsigned long long>(scan.Bytes()), snapshot ? "true" : "false");
            if (snapshot) {
                bool first = true;
                for (auto const& [path, summary] : *snapshot) {
                    auto depth = static_cast<int>(std::count(path.begin() + std::min(path.size(), root.size()), path.end(), L'\\'));
                    std::printf("%s{\"depth\":%d,\"bytes\":%llu,\"directFileBytes\":%llu}", first ? "" : ",",
                        depth, static_cast<unsigned long long>(summary.bytes), static_cast<unsigned long long>(summary.directFileBytes));
                    first = false;
                }
            }
            std::puts("]}");
            return scan.Status() == winforge::folders::ScanStatus::Complete ? 0 : 1;
        }
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
