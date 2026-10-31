#pragma once
#include "FolderSizeSymbols.h"
#include <cstdint>

namespace winforge::folders {
inline constexpr GUID ClassId{0xd029933c, 0xd28a, 0x40f0, {0x83, 0xa6, 0x2a, 0x55, 0x7a, 0x12, 0x62, 0xe9}};
inline constexpr DWORD ProtocolMagic = 0x57464653;
inline constexpr DWORD ProtocolVersion = 2;
inline constexpr DWORD SessionBytes = 4096;
inline constexpr ULONGLONG HeartbeatTimeout = 6000;
struct alignas(8) SessionData {
    DWORD magic;
    DWORD version;
    volatile ULONGLONG heartbeat;
    volatile LONG active;
    volatile LONG attached;
    volatile LONG closed;
    volatile LONG error;
    StorageSymbols symbols;
    DWORD scanSeconds;
    DWORD cacheSeconds;
    volatile LONG requests;
    volatile LONG hits;
    volatile LONG completed;
    volatile LONG incomplete;
    volatile LONG pending;
    volatile LONG callbacks;
    wchar_t message[256];
};
static_assert(sizeof(SessionData) <= SessionBytes);
static_assert(offsetof(SessionData, scanSeconds) == 100);
static_assert(offsetof(SessionData, requests) == 108);
static_assert(offsetof(SessionData, message) == 132);

class Session {
    HANDLE file_{INVALID_HANDLE_VALUE};
    HANDLE mapping_{};
    SessionData* data_{};
public:
    Session() = default;
    Session(Session const&) = delete;
    ~Session() {
        if (data_) UnmapViewOfFile(data_);
        if (mapping_) CloseHandle(mapping_);
        if (file_ != INVALID_HANDLE_VALUE) CloseHandle(file_);
    }
    bool Open(wchar_t const* path) {
        file_ = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file_ == INVALID_HANDLE_VALUE) return false;
        LARGE_INTEGER length{};
        if (!GetFileSizeEx(file_, &length) || length.QuadPart != SessionBytes) return false;
        mapping_ = CreateFileMappingW(file_, nullptr, PAGE_READWRITE, 0, 0, nullptr);
        if (!mapping_) return false;
        data_ = static_cast<SessionData*>(MapViewOfFile(mapping_, FILE_MAP_ALL_ACCESS, 0, 0, SessionBytes));
        return data_ && data_->magic == ProtocolMagic && data_->version == ProtocolVersion;
    }
    SessionData* Get() const { return data_; }
    bool Alive() const {
        return data_ && data_->active && data_->heartbeat && GetTickCount64() - data_->heartbeat <= HeartbeatTimeout;
    }
};
}
