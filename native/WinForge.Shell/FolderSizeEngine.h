#pragma once
#include "FolderSizeProtocol.h"
#include "FolderSizeScanner.h"
#include <shlobj.h>
#include <atomic>
#include <condition_variable>
#include <map>
#include <memory>
#include <mutex>
#include <optional>

namespace winforge::folders {
class DirectoryWatch {
    HANDLE directory_{INVALID_HANDLE_VALUE};
    OVERLAPPED overlapped_{};
    std::array<BYTE, 8192> buffer_{};
    bool pending_{};
    void Arm() {
        ResetEvent(overlapped_.hEvent);
        pending_ = ReadDirectoryChangesW(directory_, buffer_.data(), static_cast<DWORD>(buffer_.size()), TRUE,
            FILE_NOTIFY_CHANGE_FILE_NAME | FILE_NOTIFY_CHANGE_DIR_NAME | FILE_NOTIFY_CHANGE_SIZE | FILE_NOTIFY_CHANGE_LAST_WRITE | FILE_NOTIFY_CHANGE_ATTRIBUTES,
            nullptr, &overlapped_, nullptr) != FALSE;
    }
public:
    explicit DirectoryWatch(std::wstring const& path) {
        overlapped_.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!overlapped_.hEvent) return;
        directory_ = CreateFileW(path.c_str(), FILE_LIST_DIRECTORY, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OVERLAPPED, nullptr);
        if (directory_ != INVALID_HANDLE_VALUE) Arm();
    }
    ~DirectoryWatch() {
        if (directory_ != INVALID_HANDLE_VALUE) {
            if (pending_) { CancelIoEx(directory_, &overlapped_); DWORD ignored{}; GetOverlappedResult(directory_, &overlapped_, &ignored, TRUE); }
            CloseHandle(directory_);
        }
        if (overlapped_.hEvent) CloseHandle(overlapped_.hEvent);
    }
    bool Changed() {
        if (!pending_ || WaitForSingleObject(overlapped_.hEvent, 0) != WAIT_OBJECT_0) return false;
        DWORD bytes{};
        GetOverlappedResult(directory_, &overlapped_, &bytes, FALSE);
        pending_ = false;
        Arm();
        // Errors and buffer overflow also invalidate the result.
        return true;
    }
};

struct PathLess {
    bool operator()(std::wstring const& left, std::wstring const& right) const { return _wcsicmp(left.c_str(), right.c_str()) < 0; }
};

class FolderSizeEngine {
    struct Entry {
        std::wstring path;
        ULONGLONG used{};
        ULONGLONG expires{};
        ULONGLONG retryAfter{};
        ULONGLONG bytes{};
        bool ready{};
        bool wanted{true};
        bool retired{};
        std::unique_ptr<FolderScan> scan;
        std::unique_ptr<DirectoryWatch> watch;
    };
    static constexpr std::size_t MaximumCachedFolders = 64;
    static constexpr std::size_t MaximumActiveScans = 4;
    std::map<std::wstring, std::shared_ptr<Entry>, PathLess> entries_;
    std::vector<std::shared_ptr<Entry>> retired_;
    std::mutex mutex_;
    std::condition_variable wake_;
    Session& session_;
    DWORD scanSeconds_;
    DWORD cacheSeconds_;

    static bool LocalFixedPath(std::wstring const& path) {
        auto offset = path.starts_with(L"\\\\?\\") ? 4U : 0U;
        if (path.size() < offset + 3 || path[offset + 1] != L':' || path[offset + 2] != L'\\') return false;
        wchar_t root[]{path[offset], L':', L'\\', L'\0'};
        return GetDriveTypeW(root) == DRIVE_FIXED;
    }
    static std::wstring ExtendedPath(std::wstring const& path) {
        return path.starts_with(L"\\\\?\\") ? path : L"\\\\?\\" + path;
    }
    static void Notify(std::wstring const& path) {
        SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, path.c_str(), nullptr);
    }
public:
    explicit FolderSizeEngine(Session& session) : session_(session),
        scanSeconds_(std::clamp(session.Get()->scanSeconds, 5UL, 120UL)),
        cacheSeconds_(std::clamp(session.Get()->cacheSeconds, 5UL, 300UL)) {}

    std::optional<ULONGLONG> Query(std::wstring const& path) {
        // Only bounded cache bookkeeping is synchronized; enumeration never holds this lock.
        std::lock_guard lock(mutex_);
        if (!session_.Alive()) return std::nullopt;
        auto now = GetTickCount64();
        InterlockedIncrement(&session_.Get()->requests);
        auto found = entries_.find(path);
        if (found != entries_.end()) {
            auto& entry = *found->second;
            entry.used = now;
            if (entry.ready && now < entry.expires) { InterlockedIncrement(&session_.Get()->hits); return entry.bytes; }
            if (now >= entry.retryAfter) { entry.wanted = true; wake_.notify_one(); }
            return std::nullopt;
        }
        if (entries_.size() >= MaximumCachedFolders) {
            if (retired_.size() >= MaximumCachedFolders) return std::nullopt;
            auto oldest = std::min_element(entries_.begin(), entries_.end(), [](auto const& a, auto const& b) { return a.second->used < b.second->used; });
            oldest->second->retired = true;
            retired_.push_back(oldest->second);
            entries_.erase(oldest);
        }
        auto entry = std::make_shared<Entry>();
        entry->path = path;
        entry->used = now;
        entries_.emplace(path, std::move(entry));
        wake_.notify_one();
        return std::nullopt;
    }

    void Run() {
        SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_BEGIN);
        while (session_.Alive()) {
            std::vector<std::shared_ptr<Entry>> entries;
            std::vector<std::shared_ptr<Entry>> retiredEntries;
            {
                std::lock_guard lock(mutex_);
                for (auto const& [path, entry] : entries_) { (void)path; entries.push_back(entry); }
                retiredEntries.swap(retired_);
            }
            retiredEntries.clear(); // Closing directory handles must happen on this worker, never in Explorer's callback.
            std::size_t active{};
            for (auto const& entry : entries) if (entry->scan) ++active;
            for (auto const& entry : entries) {
                if (!session_.Alive()) break;
                bool changed = entry->watch && entry->watch->Changed();
                bool wasReady{}, wanted{}, retired{};
                ULONGLONG retryAfter{};
                {
                    std::lock_guard lock(mutex_);
                    retired = entry->retired;
                    wasReady = entry->ready;
                    if (changed) { entry->ready = false; entry->wanted = true; entry->retryAfter = GetTickCount64() + 300; }
                    wanted = entry->wanted;
                    retryAfter = entry->retryAfter;
                }
                if (retired) continue;
                if (changed) {
                    if (entry->scan) { entry->scan.reset(); --active; }
                    if (wasReady) Notify(entry->path);
                }
                if (!wanted || GetTickCount64() < retryAfter) continue;
                if (!entry->scan && active < MaximumActiveScans) {
                    if (!LocalFixedPath(entry->path)) { Complete(*entry, false, 0); continue; }
                    auto path = ExtendedPath(entry->path);
                    if (!entry->watch) entry->watch = std::make_unique<DirectoryWatch>(path);
                    entry->scan = std::make_unique<FolderScan>(std::move(path), scanSeconds_);
                    ++active;
                }
                if (!entry->scan) continue;
                auto status = entry->scan->Step([this] { return session_.Alive(); });
                if (status == ScanStatus::Pending) continue;
                auto bytes = entry->scan->Bytes();
                entry->scan.reset();
                --active;
                if (entry->watch && entry->watch->Changed()) {
                    std::lock_guard lock(mutex_);
                    entry->ready = false;
                    entry->retryAfter = GetTickCount64() + 300;
                    continue;
                }
                Complete(*entry, status == ScanStatus::Complete, bytes);
            }
            {
                std::unique_lock lock(mutex_);
                LONG pending{};
                for (auto const& [path, entry] : entries_) { (void)path; if (entry->wanted) ++pending; }
                InterlockedExchange(&session_.Get()->pending, pending);
                wake_.wait_for(lock, std::chrono::milliseconds(active ? 1 : 100));
            }
        }
        SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_END);
    }

    void RefreshDisplayedItems() {
        std::vector<std::wstring> paths;
        { std::lock_guard lock(mutex_); for (auto const& [path, entry] : entries_) { (void)entry; paths.push_back(path); } }
        for (auto const& path : paths) Notify(path);
    }
    void ReleaseEntries() {
        std::map<std::wstring, std::shared_ptr<Entry>, PathLess> entries;
        std::vector<std::shared_ptr<Entry>> retired;
        {
            std::lock_guard lock(mutex_);
            for (auto const& [path, entry] : entries_) { (void)path; entry->retired = true; }
            entries.swap(entries_);
            retired.swap(retired_);
        }
        // Any final directory-watch or scanner handles are released on the monitor or I/O worker.
    }
private:
    void Complete(Entry& entry, bool complete, ULONGLONG bytes) {
        {
            std::lock_guard lock(mutex_);
            if (entry.retired) return;
            entry.wanted = false;
            entry.ready = complete;
            entry.bytes = bytes;
            entry.expires = GetTickCount64() + static_cast<ULONGLONG>(cacheSeconds_) * 1000;
            entry.retryAfter = complete ? 0 : entry.expires;
            InterlockedIncrement(complete ? &session_.Get()->completed : &session_.Get()->incomplete);
        }
        if (complete) Notify(entry.path);
    }
};
}
