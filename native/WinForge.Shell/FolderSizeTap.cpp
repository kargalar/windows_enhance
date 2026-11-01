#include <windows.h>
#include <ocidl.h>
#include <xamlom.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <propkey.h>
#include <propsys.h>
#include <winrt/base.h>
#include <atomic>
#include <memory>
#include <thread>
#include <cstdio>
#include <cmath>
#include "FolderSizeCallback.h"
#include "FolderSizeEngine.h"

namespace winforge::folders {
using SizeGetter = HRESULT(WINAPI*)(IUnknown*, ITEMID_CHILD const*, void const*, PROPVARIANT*);
using FolderCompare = HRESULT(WINAPI*)(IUnknown*, LPARAM, ITEMIDLIST_RELATIVE const*, ITEMIDLIST_RELATIVE const*);
using SizeFormatter = HRESULT(STDMETHODCALLTYPE*)(IPropertyDescription*, REFPROPVARIANT, PROPDESC_FORMAT_FLAGS, LPWSTR*);
inline std::atomic<SizeGetter> Original;
inline std::atomic<FolderCompare> OriginalCompare;
inline std::atomic<SizeFormatter> OriginalFormat;
struct State {
    Session session;
    std::unique_ptr<FolderSizeEngine> engine;
    HMODULE storage{};
    StorageSymbols symbols{};
    bool otherSizeHook{};
    CallbackTablePatch sizeTablePatch;
    CallbackTablePatch sortTablePatch;
    winrt::com_ptr<IPropertyDescription> sizeDescription;
    CallbackTablePatch formatTablePatch;
};
inline std::atomic<std::shared_ptr<State>> Current;
inline std::mutex ConnectionGate;

std::wstring FormatFolderSize(ULONGLONG bytes) {
    constexpr wchar_t const* units[] = {L"B", L"KB", L"MB", L"GB", L"TB", L"PB", L"EB"};
    double size = static_cast<double>(bytes);
    std::size_t unit{};
    while (size >= 1024.0 && unit + 1 < std::size(units)) {
        size /= 1024.0;
        ++unit;
    }
    // Promote a rounded boundary too, so 1023.99 MB is displayed as 1 GB.
    size = std::round(size * 10.0) / 10.0;
    if (size >= 1024.0 && unit + 1 < std::size(units)) { size /= 1024.0; ++unit; }
    wchar_t number[32]{};
    swprintf_s(number, L"%.1f", size);
    std::wstring text(number);
    if (text.back() == L'0') text.resize(text.size() - 2);
    return text + L" " + units[unit];
}

HRESULT STDMETHODCALLTYPE FormatSize(IPropertyDescription* description, REFPROPVARIANT value,
    PROPDESC_FORMAT_FLAGS flags, LPWSTR* display) noexcept {
    auto state = Current.load();
    if (state && state->session.Alive() && value.vt == VT_UI8 && display) {
        PROPERTYKEY key{};
        if (SUCCEEDED(description->GetPropertyKey(&key)) && IsEqualPropertyKey(key, PKEY_Size)) {
            try {
                auto text = FormatFolderSize(value.uhVal.QuadPart);
                auto buffer = static_cast<wchar_t*>(CoTaskMemAlloc((text.size() + 1) * sizeof(wchar_t)));
                if (!buffer) return E_OUTOFMEMORY;
                memcpy(buffer, text.c_str(), (text.size() + 1) * sizeof(wchar_t));
                *display = buffer;
                return S_OK;
            } catch (...) { }
        }
    }
    return OriginalFormat.load()(description, value, flags, display);
}

std::optional<std::wstring> FolderPath(IShellFolder* folder, ITEMID_CHILD const* child) {
    STRRET display{};
    if (FAILED(folder->GetDisplayNameOf(child, SHGDN_FORPARSING, &display))) return std::nullopt;
    wchar_t* path{};
    if (FAILED(StrRetToStrW(&display, child, &path))) return std::nullopt;
    auto release = std::unique_ptr<wchar_t, decltype(&CoTaskMemFree)>(path, CoTaskMemFree);
    if (!path || wcslen(path) > 32760) return std::nullopt;
    return std::wstring(path);
}

bool IsFilesystemFolder(IShellFolder* folder, ITEMID_CHILD const* child) {
    SFGAOF attributes = SFGAO_FOLDER | SFGAO_FILESYSTEM;
    return SUCCEEDED(folder->GetAttributesOf(1, &child, &attributes)) &&
        (attributes & (SFGAO_FOLDER | SFGAO_FILESYSTEM)) == (SFGAO_FOLDER | SFGAO_FILESYSTEM);
}

HRESULT WINAPI CompareFolders(IUnknown* folder, LPARAM parameters, ITEMIDLIST_RELATIVE const* first,
    ITEMIDLIST_RELATIVE const* second) noexcept {
    auto original = OriginalCompare.load();
    auto state = Current.load();
    if (!state || !state->session.Alive() || !first || !second ||
        (parameters & (SHCIDS_ALLFIELDS | SHCIDS_CANONICALONLY))) return original(folder, parameters, first, second);
    try {
        winrt::com_ptr<IShellFolder2> shellFolder;
        if (FAILED(folder->QueryInterface(IID_PPV_ARGS(shellFolder.put())))) return original(folder, parameters, first, second);
        PROPERTYKEY key{};
        auto column = static_cast<UINT>(parameters & SHCIDS_COLUMNMASK);
        if (FAILED(shellFolder->MapColumnToSCID(column, &key)) || !IsEqualPropertyKey(key, PKEY_Size))
            return original(folder, parameters, first, second);
        auto firstChild = reinterpret_cast<ITEMID_CHILD const*>(first);
        auto secondChild = reinterpret_cast<ITEMID_CHILD const*>(second);
        if (!IsFilesystemFolder(shellFolder.get(), firstChild) || !IsFilesystemFolder(shellFolder.get(), secondChild))
            return original(folder, parameters, first, second);
        auto firstPath = FolderPath(shellFolder.get(), firstChild);
        auto secondPath = FolderPath(shellFolder.get(), secondChild);
        if (!firstPath || !secondPath) return original(folder, parameters, first, second);
        state->engine->RememberSizeSort(*firstPath);
        auto firstSize = state->engine->Query(*firstPath, true);
        auto secondSize = state->engine->Query(*secondPath, true);
        if (firstSize.has_value() != secondSize.has_value() || (firstSize && secondSize && *firstSize != *secondSize)) {
            auto order = !firstSize ? 1 : !secondSize ? -1 : *firstSize < *secondSize ? -1 : 1;
            return MAKE_HRESULT(SEVERITY_SUCCESS, 0, static_cast<USHORT>(static_cast<short>(order)));
        }
    } catch (...) { }
    return original(folder, parameters, first, second);
}

HRESULT WINAPI GetSize(IUnknown* folder, ITEMID_CHILD const* child, void const* data, PROPVARIANT* value) noexcept {
    auto state = Current.load();
    if (state && state->session.Alive()) InterlockedIncrement(&state->session.Get()->callbacks);
    auto result = Original.load()(folder, child, data, value);
    if (result != S_OK || !value || !state || !state->session.Alive() ||
        (value->vt != VT_EMPTY && !(state->otherSizeHook && value->vt == VT_UI8))) return result;
    try {
        if (!IsViewRequest(state->storage, state->symbols)) return result;
        // The owning filesystem folder supplies an absolute parsing name; no recursive shell operation is invoked.
        winrt::com_ptr<IShellFolder> shellFolder;
        if (FAILED(folder->QueryInterface(IID_PPV_ARGS(shellFolder.put())))) return result;
        if (value->vt == VT_UI8) {
            if (!IsFilesystemFolder(shellFolder.get(), child)) return result;
        }
        auto path = FolderPath(shellFolder.get(), child);
        if (!path) return result;
        auto bytes = state->engine->Query(*path);
        if (bytes) { value->vt = VT_UI8; value->uhVal.QuadPart = *bytes; }
    } catch (...) { /* A size enhancement cannot propagate exceptions into Explorer. */ }
    return result;
}

struct Tap : winrt::implements<Tap, IObjectWithSite> {
    winrt::com_ptr<IUnknown> site_;
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) noexcept override {
        if (!site) { site_ = nullptr; return S_OK; }
        try {
            std::lock_guard lock(ConnectionGate);
            if (Current.load()) return HRESULT_FROM_WIN32(ERROR_BUSY);
            site_.copy_from(site);
            auto diagnostics = site_.as<IXamlDiagnostics>();
            BSTR initialization{};
            winrt::check_hresult(diagnostics->GetInitializationData(&initialization));
            std::wstring path(initialization, SysStringLen(initialization));
            SysFreeString(initialization);
            auto state = std::make_shared<State>();
            if (!state->session.Open(path.c_str()) || !state->session.Alive()) return E_ACCESSDENIED;
            auto header = state->session.Get();
            try {
                state->storage = LoadLibraryExW(L"windows.storage.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
                if (!state->storage) throw std::runtime_error("Windows Storage is unavailable.");
                state->symbols = header->symbols;
                ValidateStorageIdentity(state->storage, state->symbols);
                state->symbols.collection = UnwindRange(state->storage, state->symbols.collection);
                state->symbols.prepare = UnwindRange(state->storage, state->symbols.prepare);
                state->symbols.operation = UnwindRange(state->storage, state->symbols.operation);
                if (!state->symbols.collection.bytes || !state->symbols.prepare.bytes || !state->symbols.operation.bytes)
                    throw std::runtime_error("Explorer view and file operation callers could not be verified.");
                // A callback can already have been fetched by another thread when it is restored.
                // Keep its code mapped until Explorer exits; inactive calls always delegate to Windows.
                HMODULE pinned{};
                if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                    reinterpret_cast<LPCWSTR>(&GetSize), &pinned)) throw std::runtime_error("Folder-size callback lifetime could not be protected.");
                Original.store(reinterpret_cast<SizeGetter>(reinterpret_cast<BYTE*>(state->storage) + header->symbols.size.rva));
                OriginalCompare.store(reinterpret_cast<FolderCompare>(reinterpret_cast<BYTE*>(state->storage) + header->symbols.compare.rva));
                auto entry = reinterpret_cast<BYTE*>(state->storage) + header->symbols.size.rva;
                state->otherSizeHook = entry[0] == 0xe9 || entry[0] == 0xeb || entry[0] == 0xff ||
                    entry[0] == 0xcc || (entry[0] == 0x48 && entry[1] == 0xb8);
                state->engine = std::make_unique<FolderSizeEngine>(state->session);
                winrt::check_hresult(PSGetPropertyDescription(PKEY_Size, IID_PPV_ARGS(state->sizeDescription.put())));
                // IPropertyDescription::FormatForDisplay is slot 22 of the public COM interface.
                auto formatSlot = *reinterpret_cast<void***>(state->sizeDescription.get()) + 22;
                OriginalFormat.store(reinterpret_cast<SizeFormatter>(*formatSlot));
                state->formatTablePatch.InstallSlot(formatSlot, reinterpret_cast<void*>(&FormatSize));
                state->sizeTablePatch.Install(state->storage, header->symbols, header->symbols.size.rva, reinterpret_cast<void*>(&GetSize));
                state->sortTablePatch.Install(state->storage, header->symbols, header->symbols.compare.rva, reinterpret_cast<void*>(&CompareFolders));
                Current.store(state);
                std::thread([state] {
                    auto initialized = SUCCEEDED(CoInitializeEx(nullptr, COINIT_MULTITHREADED));
                    try { state->engine->Run(); }
                    catch (...) {
                        InterlockedExchange(&state->session.Get()->error, E_FAIL);
                        InterlockedExchange(&state->session.Get()->active, 0);
                    }
                    if (initialized) CoUninitialize();
                }).detach();
                std::thread([state] {
                    while (state->session.Alive()) Sleep(100);
                    {
                        std::lock_guard guard(ConnectionGate);
                        Current.store(nullptr);
                        bool restored = state->sortTablePatch.Restore();
                        restored = state->formatTablePatch.Restore() && restored;
                        restored = state->sizeTablePatch.Restore() && restored;
                        if (!restored) InterlockedExchange(&state->session.Get()->error, E_FAIL);
                        InterlockedExchange(&state->session.Get()->closed, 1);
                    }
                    try { state->engine->RefreshDisplayedItems(); } catch (...) { }
                    try { state->engine->ReleaseEntries(); } catch (...) { }
                }).detach();
                InterlockedExchange(&header->attached, 1);
                return S_OK;
            } catch (std::exception const& error) {
                std::string text(error.what());
                std::wstring wide(text.begin(), text.end());
                wcsncpy_s(header->message, wide.c_str(), _TRUNCATE);
                InterlockedExchange(&header->error, E_FAIL);
                InterlockedExchange(&header->active, 0);
                Current.store(nullptr);
                state->sortTablePatch.Restore();
                state->formatTablePatch.Restore();
                state->sizeTablePatch.Restore();
                return E_FAIL;
            }
        } catch (...) { return winrt::to_hresult(); }
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid, void** result) noexcept override { return site_ ? site_->QueryInterface(iid, result) : E_NOINTERFACE; }
};
struct Factory : winrt::implements<Factory, IClassFactory> {
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** result) noexcept override {
        if (outer) return CLASS_E_NOAGGREGATION;
        try { return winrt::make_self<Tap>()->QueryInterface(iid, result); }
        catch (...) { return winrt::to_hresult(); }
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) noexcept override { return S_OK; }
};
}
STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** result) {
    if (clsid != winforge::folders::ClassId) return CLASS_E_CLASSNOTAVAILABLE;
    try { return winrt::make_self<winforge::folders::Factory>()->QueryInterface(iid, result); }
    catch (...) { return winrt::to_hresult(); }
}
STDAPI DllCanUnloadNow() { return S_FALSE; }
