#pragma once
#include "FolderSizeSymbols.h"

namespace winforge::folders {
inline void ValidateStorageIdentity(HMODULE module, StorageSymbols const& symbols) {
    auto base = reinterpret_cast<BYTE*>(module);
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + reinterpret_cast<IMAGE_DOS_HEADER*>(base)->e_lfanew);
    auto bytes = nt->OptionalHeader.SizeOfImage;
    if (nt->FileHeader.TimeDateStamp != symbols.timestamp || bytes != symbols.imageBytes || bytes < 16 ||
        !symbols.size.rva || symbols.size.rva >= bytes - 16 || !symbols.collection.rva || symbols.collection.rva >= bytes ||
        !symbols.prepare.rva || symbols.prepare.rva >= bytes || !symbols.operation.rva || symbols.operation.rva >= bytes)
        throw std::runtime_error("Windows Storage changed after symbol preparation.");
    GUID guid{};
    DWORD age{};
    std::wstring name;
    PdbIdentity(module, name, guid, age);
    if (age != symbols.pdbAge || memcmp(&guid, &symbols.pdbGuid, sizeof(guid)))
        throw std::runtime_error("Windows Storage symbol identity changed after preparation.");
}

class CallbackTablePatch {
    std::vector<void**> slots_;
    void* original_{};
    void* replacement_{};
    static bool Exchange(void** slot, void* expected, void* desired) {
        DWORD previous{};
        if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &previous)) return false;
        auto observed = InterlockedCompareExchangePointer(slot, desired, expected);
        DWORD ignored{};
        if (!VirtualProtect(slot, sizeof(void*), previous, &ignored)) {
            if (observed == expected) InterlockedCompareExchangePointer(slot, expected, desired);
            VirtualProtect(slot, sizeof(void*), previous, &ignored);
            return false;
        }
        return observed == expected;
    }
public:
    CallbackTablePatch() = default;
    CallbackTablePatch(CallbackTablePatch const&) = delete;
    ~CallbackTablePatch() { Restore(); }
    void Install(HMODULE module, StorageSymbols const& symbols, void* replacement) {
        auto base = reinterpret_cast<BYTE*>(module);
        auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + reinterpret_cast<IMAGE_DOS_HEADER*>(base)->e_lfanew);
        ValidateStorageIdentity(module, symbols);
        original_ = base + symbols.size.rva;
        replacement_ = replacement;
        std::vector<void**> found;
        auto section = IMAGE_FIRST_SECTION(nt);
        for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
            if (memcmp(section[i].Name, ".rdata", 6) || section[i].Characteristics & IMAGE_SCN_MEM_EXECUTE) continue;
            auto start = base + section[i].VirtualAddress;
            for (DWORD offset = 0; offset + sizeof(void*) <= section[i].Misc.VirtualSize; offset += sizeof(void*)) {
                auto slot = reinterpret_cast<void**>(start + offset);
                if (*slot == original_) found.push_back(slot);
            }
        }
        if (found.size() != 1) throw std::runtime_error("Explorer size callback table is unsupported.");
        slots_.reserve(found.size());
        for (auto slot : found) {
            if (!Exchange(slot, original_, replacement_)) throw std::runtime_error("Explorer size callback table is already in use.");
            slots_.push_back(slot);
        }
    }
    bool Restore() noexcept {
        bool restored = true;
        for (auto slot : slots_) restored = Exchange(slot, replacement_, original_) && restored;
        slots_.clear();
        return restored;
    }
};

inline SymbolRange UnwindRange(HMODULE module, SymbolRange symbol) {
    if (!symbol.rva) return {};
    DWORD64 base{};
    if (auto entry = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(module) + symbol.rva, &base, nullptr))
        return {entry->BeginAddress, entry->EndAddress - entry->BeginAddress};
    return symbol;
}
inline bool InRange(DWORD64 address, DWORD64 base, SymbolRange range) {
    return range.rva && range.bytes && address >= base + range.rva && address < base + range.rva + range.bytes;
}
inline bool IsViewRequest(HMODULE module, StorageSymbols const& symbols) {
    void* stack[32]{};
    auto count = CaptureStackBackTrace(1, static_cast<DWORD>(std::size(stack)), stack, nullptr);
    auto base = reinterpret_cast<DWORD64>(module);
    bool collection{};
    for (USHORT i = 0; i < count; ++i) {
        auto address = reinterpret_cast<DWORD64>(stack[i]);
        if (InRange(address, base, symbols.prepare) || InRange(address, base, symbols.operation)) return false;
        if (InRange(address, base, symbols.collection)) collection = true;
    }
    return collection;
}
}
