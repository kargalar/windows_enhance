#pragma once
#include <windows.h>
#include <dbghelp.h>
#include <winhttp.h>
#include <array>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>
#include <stdexcept>

namespace winforge::folders {
struct SymbolRange { DWORD rva{}; DWORD bytes{}; };
struct StorageSymbols {
    DWORD timestamp{};
    DWORD imageBytes{};
    GUID pdbGuid{};
    DWORD pdbAge{};
    SymbolRange size;
    SymbolRange collection;
    SymbolRange prepare;
    SymbolRange operation;
    SymbolRange compare;
};

inline std::wstring PdbIdentity(HMODULE module, std::wstring& name, GUID& guid, DWORD& age) {
    auto base = reinterpret_cast<BYTE*>(module);
    auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    auto directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG];
    auto entries = reinterpret_cast<IMAGE_DEBUG_DIRECTORY*>(base + directory.VirtualAddress);
    for (DWORD i = 0; i < directory.Size / sizeof(IMAGE_DEBUG_DIRECTORY); ++i) {
        if (entries[i].Type != IMAGE_DEBUG_TYPE_CODEVIEW || entries[i].SizeOfData < 25) continue;
        auto record = base + entries[i].AddressOfRawData;
        if (memcmp(record, "RSDS", 4)) continue;
        memcpy(&guid, record + 4, sizeof(guid));
        memcpy(&age, record + 20, sizeof(age));
        auto path = reinterpret_cast<char const*>(record + 24);
        auto length = strnlen_s(path, entries[i].SizeOfData - 24);
        if (length == entries[i].SizeOfData - 24) break;
        name = std::filesystem::path(std::string(path, length)).filename().wstring();
        wchar_t key[64]{};
        swprintf_s(key, L"%08X%04X%04X%02X%02X%02X%02X%02X%02X%02X%02X%X",
            guid.Data1, guid.Data2, guid.Data3, guid.Data4[0], guid.Data4[1], guid.Data4[2], guid.Data4[3],
            guid.Data4[4], guid.Data4[5], guid.Data4[6], guid.Data4[7], age);
        return key;
    }
    throw std::runtime_error("Windows Storage has no supported CodeView identity.");
}

class InternetHandle {
    HINTERNET value_{};
public:
    explicit InternetHandle(HINTERNET value) : value_(value) { if (!value) throw std::runtime_error("Symbol server connection failed."); }
    ~InternetHandle() { WinHttpCloseHandle(value_); }
    operator HINTERNET() const { return value_; }
};

inline void DownloadSymbols(std::wstring const& path, std::filesystem::path const& destination) {
    InternetHandle session(WinHttpOpen(L"WinForge/1.0", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, nullptr, nullptr, 0));
    WinHttpSetTimeouts(session, 10000, 10000, 15000, 15000);
    InternetHandle connection(WinHttpConnect(session, L"msdl.microsoft.com", INTERNET_DEFAULT_HTTPS_PORT, 0));
    InternetHandle request(WinHttpOpenRequest(connection, L"GET", path.c_str(), nullptr, nullptr, nullptr, WINHTTP_FLAG_SECURE));
    if (!WinHttpSendRequest(request, nullptr, 0, nullptr, 0, 0, 0) || !WinHttpReceiveResponse(request, nullptr))
        throw std::runtime_error("Microsoft symbol download failed.");
    DWORD status{}, statusBytes = sizeof(status);
    if (!WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, nullptr, &status, &statusBytes, nullptr) || status != 200)
        throw std::runtime_error("Matching Windows symbols are not available from Microsoft.");
    auto temporary = destination;
    temporary += L"." + std::to_wstring(GetCurrentProcessId()) + L".tmp";
    try {
        std::ofstream output(temporary, std::ios::binary | std::ios::trunc);
        if (!output) throw std::runtime_error("Symbol cache is not writable.");
        std::array<char, 65536> buffer{};
        constexpr ULONGLONG MaximumPdbBytes = 512ULL * 1024 * 1024;
        ULONGLONG total{};
        DWORD read{};
        do {
            if (!WinHttpReadData(request, buffer.data(), static_cast<DWORD>(buffer.size()), &read))
                throw std::runtime_error("Symbol download was interrupted.");
            total += read;
            if (total > MaximumPdbBytes) throw std::runtime_error("Symbol file exceeds the cache limit.");
            output.write(buffer.data(), read);
            if (!output) throw std::runtime_error("Symbol cache write failed.");
        } while (read);
        if (total < 4096) throw std::runtime_error("Downloaded Windows symbols are incomplete.");
        output.close();
        if (!MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
            throw std::runtime_error("Symbol cache commit failed.");
    } catch (...) { std::error_code ignored; std::filesystem::remove(temporary, ignored); throw; }
}

inline BOOL CALLBACK CollectStorageSymbol(PSYMBOL_INFO symbol, ULONG, void* context) {
    auto result = static_cast<std::pair<DWORD64, StorageSymbols*>*>(context);
    std::string name(symbol->Name, symbol->NameLen);
    SymbolRange* range{};
    if (name == "CFSFolder::_GetSize") range = &result->second->size;
    else if (name == "CDefCollection::Item_GetValue") range = &result->second->collection;
    else if (name == "CRecursiveFolderOperation::Prepare") range = &result->second->prepare;
    else if (name == "CRecursiveFolderOperation::Do") range = &result->second->operation;
    else if (name == "CFSFolder::CompareIDs") range = &result->second->compare;
    if (range) *range = {static_cast<DWORD>(symbol->Address - result->first), symbol->Size};
    return TRUE;
}

inline StorageSymbols ResolveStorageSymbols(std::filesystem::path const& cache) {
    auto storage = LoadLibraryExW(L"windows.storage.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!storage) throw std::runtime_error("Windows Storage could not be loaded.");
    auto base = reinterpret_cast<BYTE*>(storage);
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + reinterpret_cast<IMAGE_DOS_HEADER*>(base)->e_lfanew);
    StorageSymbols result{};
    result.timestamp = nt->FileHeader.TimeDateStamp;
    result.imageBytes = nt->OptionalHeader.SizeOfImage;
    GUID guid{};
    DWORD age{};
    std::wstring name;
    auto identity = PdbIdentity(storage, name, guid, age);
    result.pdbGuid = guid;
    result.pdbAge = age;
    auto directory = cache / identity;
    std::filesystem::create_directories(directory);
    auto pdb = directory / name;
    if (!std::filesystem::is_regular_file(pdb) || std::filesystem::file_size(pdb) < 4096)
        DownloadSymbols(L"/download/symbols/" + name + L"/" + identity + L"/" + name, pdb);
    auto process = GetCurrentProcess();
    SymSetOptions(SYMOPT_UNDNAME | SYMOPT_FAIL_CRITICAL_ERRORS | SYMOPT_EXACT_SYMBOLS);
    if (!SymInitializeW(process, directory.c_str(), FALSE)) throw std::runtime_error("Windows symbol engine initialization failed.");
    try {
        std::array<wchar_t, 32768> image{};
        GetModuleFileNameW(storage, image.data(), static_cast<DWORD>(image.size()));
        auto loaded = SymLoadModuleExW(process, nullptr, image.data(), nullptr, reinterpret_cast<DWORD64>(storage), 0, nullptr, 0);
        if (!loaded) throw std::runtime_error("Matching Windows symbols could not be loaded.");
        IMAGEHLP_MODULEW64 information{};
        information.SizeOfStruct = sizeof(information);
        if (!SymGetModuleInfoW64(process, loaded, &information) || information.SymType != SymPdb ||
            information.PdbUnmatched || information.PdbAge != age || memcmp(&information.PdbSig70, &guid, sizeof(guid)))
            throw std::runtime_error("Windows symbol identity does not match the installed binary.");
        std::pair<DWORD64, StorageSymbols*> context{loaded, &result};
        if (!SymEnumSymbols(process, loaded, "*", CollectStorageSymbol, &context) || !result.size.rva || !result.collection.rva ||
            !result.prepare.rva || !result.operation.rva || !result.compare.rva)
            throw std::runtime_error("This Windows build does not expose the required folder-size callbacks.");
        SymCleanup(process);
        return result;
    } catch (...) { SymCleanup(process); throw; }
}
}
