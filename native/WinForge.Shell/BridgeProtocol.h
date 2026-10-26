#pragma once
#include <windows.h>
#include <cstdint>
#include <cstddef>
#include <string>

namespace winforge::shell {
inline constexpr GUID TapClassId{0x9ec43165, 0x4952, 0x4a0c, {0xb2, 0x18, 0x91, 0xde, 0xc7, 0x62, 0x1c, 0x6d}};
inline constexpr std::uint32_t ProtocolMagic = 0x57465348;
inline constexpr std::uint32_t ProtocolVersion = 2;
inline constexpr std::size_t MappingBytes = 1048576;
inline constexpr std::size_t PayloadOffset = 1024;
inline constexpr std::size_t SnapshotOffset = 65536;
inline constexpr ULONGLONG HeartbeatTimeout = 6000;

struct alignas(8) Header {
    std::uint32_t magic;
    std::uint32_t version;
    volatile LONG revision;
    std::uint32_t payloadCharacters;
    volatile ULONGLONG heartbeat;
    volatile LONG attached;
    volatile LONG elements;
    volatile LONG changed;
    volatile LONG errors;
    volatile LONG restored;
    DWORD processId;
    volatile LONG snapshotCharacters;
    volatile LONG snapshotRevision;
    volatile LONG appliedRevision;
    volatile LONG closed;
    volatile LONG lastError;
    wchar_t lastErrorMessage[256];
};
static_assert(sizeof(Header) <= PayloadOffset);

class SharedSession {
    HANDLE mapping_{};
    HANDLE file_{};
    void* view_{};
public:
    SharedSession() = default;
    SharedSession(SharedSession const&) = delete;
    SharedSession& operator=(SharedSession const&) = delete;
    ~SharedSession() { Close(); }
    void Close() {
        if (view_) UnmapViewOfFile(view_);
        if (mapping_) CloseHandle(mapping_);
        if (file_ && file_ != INVALID_HANDLE_VALUE) CloseHandle(file_);
        view_ = nullptr;
        mapping_ = nullptr;
        file_ = nullptr;
    }

    bool Open(std::wstring const& name) {
        file_ = CreateFileW(name.c_str(), GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file_ == INVALID_HANDLE_VALUE) return false;
        LARGE_INTEGER size{};
        if (!GetFileSizeEx(file_, &size) || size.QuadPart != MappingBytes) return false;
        mapping_ = CreateFileMappingW(file_, nullptr, PAGE_READWRITE, 0, 0, nullptr);
        if (!mapping_) return false;
        view_ = MapViewOfFile(mapping_, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, MappingBytes);
        if (!view_) return false;
        return Get()->magic == ProtocolMagic && Get()->version == ProtocolVersion;
    }
    Header* Get() const { return static_cast<Header*>(view_); }
    bool Alive() const {
        auto header = Get();
        return header && header->heartbeat != 0 && GetTickCount64() - header->heartbeat <= HeartbeatTimeout;
    }
    bool Read(std::wstring& json, LONG& revision) const {
        auto header = Get();
        if (!Alive()) return false;
        auto first = InterlockedCompareExchange(&header->revision, 0, 0);
        if ((first & 1) != 0) return false;
        auto length = header->payloadCharacters;
        if (length > (SnapshotOffset - PayloadOffset) / sizeof(wchar_t)) return false;
        auto source = reinterpret_cast<wchar_t const*>(static_cast<std::byte const*>(view_) + PayloadOffset);
        json.assign(source, length);
        MemoryBarrier();
        if (first != InterlockedCompareExchange(&header->revision, 0, 0)) return false;
        revision = first;
        return true;
    }
    void Snapshot(std::wstring const& json) const {
        auto header = Get();
        if (!header || json.size() >= (MappingBytes - SnapshotOffset) / sizeof(wchar_t)) return;
        InterlockedIncrement(&header->snapshotRevision);
        auto target = reinterpret_cast<wchar_t*>(static_cast<std::byte*>(view_) + SnapshotOffset);
        memcpy(target, json.data(), json.size() * sizeof(wchar_t));
        header->snapshotCharacters = static_cast<LONG>(json.size());
        MemoryBarrier();
        InterlockedIncrement(&header->snapshotRevision);
    }
};
}
