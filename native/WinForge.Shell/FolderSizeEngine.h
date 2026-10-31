#pragma once
#include "FolderSizeProtocol.h"
#include "FolderSizeScanner.h"
#include <shlobj.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <limits>
#include <map>
#include <memory>
#include <mutex>
#include <optional>
#include <set>
#include <string>
#include <vector>

namespace winforge::folders {
struct DirectoryChangeBatch {
    bool changed{};
    bool overflow{};
    std::vector<std::wstring> paths;
};

inline bool DecodeDirectoryNotifications(std::wstring const& inputRoot, BYTE const* data, DWORD bytes,
    std::vector<std::wstring>& paths) {
    constexpr auto headerBytes = offsetof(FILE_NOTIFY_INFORMATION, FileName);
    if (!data || bytes < headerBytes) return false;
    auto root = NormalizePath(inputRoot);
    DWORD offset{};
    while (true) {
        auto remaining = bytes - offset;
        if (remaining < headerBytes) return false;
        auto const* notification = reinterpret_cast<FILE_NOTIFY_INFORMATION const*>(data + offset);
        auto nameBytes = notification->FileNameLength;
        if (notification->Action < FILE_ACTION_ADDED || notification->Action > FILE_ACTION_RENAMED_NEW_NAME ||
            nameBytes == 0 || nameBytes % sizeof(wchar_t) != 0 || nameBytes > remaining - headerBytes) return false;
        std::wstring path = root;
        if (!path.empty() && path.back() != L'\\') path.push_back(L'\\');
        path.append(notification->FileName, nameBytes / sizeof(wchar_t));
        paths.push_back(std::move(path));
        if (notification->NextEntryOffset == 0) return true;
        if (notification->NextEntryOffset % alignof(DWORD) != 0 ||
            notification->NextEntryOffset < headerBytes + nameBytes || notification->NextEntryOffset > remaining) return false;
        offset += notification->NextEntryOffset;
    }
}

inline DirectoryChangeBatch DecodeDirectoryChangeBatch(std::wstring const& root, BYTE const* data, DWORD bytes,
    bool readSucceeded = true) {
    DirectoryChangeBatch result;
    result.changed = true;
    if (!readSucceeded || bytes == 0 || !DecodeDirectoryNotifications(root, data, bytes, result.paths)) {
        result.overflow = true;
        result.paths.clear();
    }
    return result;
}

inline bool ScanRequestPrecedes(bool leftPriority, ULONGLONG leftUsed, bool rightPriority, ULONGLONG rightUsed) {
    if (leftPriority != rightPriority) return leftPriority;
    return leftUsed > rightUsed;
}

class DirectoryWatch {
    static constexpr DWORD BufferBytes = 64 * 1024;
    HANDLE directory_{INVALID_HANDLE_VALUE};
    OVERLAPPED overlapped_{};
    alignas(FILE_NOTIFY_INFORMATION) std::array<BYTE, BufferBytes> buffer_{};
    std::wstring root_;
    std::atomic_bool pending_{};

    static std::wstring ExtendedPath(std::wstring const& path) {
        return path.starts_with(L"\\\\?\\") ? path : L"\\\\?\\" + path;
    }

    void Arm() {
        if (directory_ == INVALID_HANDLE_VALUE) return;
        ResetEvent(overlapped_.hEvent);
        pending_ = ReadDirectoryChangesW(directory_, buffer_.data(), static_cast<DWORD>(buffer_.size()), TRUE,
            FILE_NOTIFY_CHANGE_FILE_NAME | FILE_NOTIFY_CHANGE_DIR_NAME | FILE_NOTIFY_CHANGE_SIZE |
                FILE_NOTIFY_CHANGE_LAST_WRITE | FILE_NOTIFY_CHANGE_ATTRIBUTES,
            nullptr, &overlapped_, nullptr) != FALSE;
    }

public:
    explicit DirectoryWatch(std::wstring path) : root_(NormalizePath(std::move(path))) {
        overlapped_.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!overlapped_.hEvent) return;
        auto ioPath = ExtendedPath(root_);
        directory_ = CreateFileW(ioPath.c_str(), FILE_LIST_DIRECTORY, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OVERLAPPED, nullptr);
        if (directory_ != INVALID_HANDLE_VALUE) Arm();
    }

    ~DirectoryWatch() {
        if (directory_ != INVALID_HANDLE_VALUE) {
            if (pending_) {
                CancelIoEx(directory_, &overlapped_);
                DWORD ignored{};
                GetOverlappedResult(directory_, &overlapped_, &ignored, TRUE);
            }
            CloseHandle(directory_);
        }
        if (overlapped_.hEvent) CloseHandle(overlapped_.hEvent);
    }

    std::wstring const& Root() const { return root_; }
    bool Valid() const { return directory_ != INVALID_HANDLE_VALUE && pending_.load(std::memory_order_acquire); }

    DirectoryChangeBatch ReadChanges() {
        DirectoryChangeBatch result;
        if (!pending_ || WaitForSingleObject(overlapped_.hEvent, 0) != WAIT_OBJECT_0) return result;
        DWORD bytes{};
        auto succeeded = GetOverlappedResult(directory_, &overlapped_, &bytes, FALSE) != FALSE;
        result = DecodeDirectoryChangeBatch(root_, buffer_.data(), bytes, succeeded);
        pending_ = false;
        Arm();
        if (!pending_) result.overflow = true;
        return result;
    }
};

class FolderSizeEngine {
    struct Entry {
        std::wstring path;
        ULONGLONG used{};
        ULONGLONG expires{};
        ULONGLONG retryAfter{};
        ULONGLONG watchRetryAfter{};
        ULONGLONG bytes{};
        std::chrono::steady_clock::time_point scanStarted{};
        bool ready{};
        bool wanted{true};
        bool retired{};
        bool sizeSortPriority{};
        bool incrementalPending{};
        std::unique_ptr<FolderScan> scan;
        std::shared_ptr<DirectorySnapshot> snapshot;
        std::wstring snapshotRoot;
        std::shared_ptr<DirectoryWatch> watch;
    };
    struct SortedParent { ULONGLONG used{}; ULONGLONG due{}; ULONGLONG deadline{}; bool pending{}; };
    struct SnapshotReference { std::weak_ptr<DirectorySnapshot> snapshot; };
    struct SnapshotMatch { std::shared_ptr<Entry> entry; DirectorySummary summary; };
    struct ScanCandidate { std::shared_ptr<Entry> entry; bool sizeSortPriority{}; ULONGLONG used{}; };
    struct SnapshotTarget { std::shared_ptr<DirectorySnapshot> snapshot; std::wstring root; };
    struct PendingChange {
        std::wstring path;
        ULONGLONG due{};
        std::chrono::steady_clock::time_point started{};
        std::unique_ptr<FolderScan> scan;
        std::vector<SnapshotTarget> targets;
    };
    struct SnapshotChangeGroup {
        std::shared_ptr<DirectorySnapshot> snapshot;
        std::wstring root;
        std::vector<std::wstring> paths;
        bool fallback{};
    };
    struct ReconciledSnapshot { std::shared_ptr<DirectorySnapshot> target; DirectorySnapshot contents; };
    struct ChangeHandling {
        std::size_t cancelledScans{};
        std::size_t cancelledIncrementalScans{};
        bool currentAffected{};
    };

    static constexpr std::size_t MaximumCachedFolders = 256;
    static constexpr std::size_t MaximumSortedParents = 64;
    static constexpr std::size_t MaximumActiveScans = 4;
    static constexpr std::size_t MaximumSnapshotBytes = 32 * 1024 * 1024;
    static constexpr std::size_t MaximumSnapshotDirectories = 8192;
    static constexpr std::size_t MaximumSnapshotPathCharacters = 1'048'576;
    static constexpr std::size_t SnapshotNodeAllowance = 128;
    static constexpr std::size_t MaximumPendingChanges = 128;
    static constexpr ULONGLONG SortRefreshDelayMilliseconds = 400;
    static constexpr ULONGLONG SortRefreshMaximumDelayMilliseconds = 2000;
    static constexpr ULONGLONG ChangeRetryDelayMilliseconds = 300;
    static constexpr ULONGLONG WatcherRetryDelayMilliseconds = 1000;
    std::map<std::wstring, std::shared_ptr<Entry>, PathLess> entries_;
    std::map<std::wstring, SortedParent, PathLess> sortedParents_;
    std::vector<std::shared_ptr<Entry>> retired_;
    std::vector<SnapshotReference> snapshots_;
    std::map<std::wstring, PendingChange, PathLess> pendingChanges_;
    std::mutex mutex_;
    std::condition_variable wake_;
    Session& session_;
    DWORD scanSeconds_;
    DWORD cacheSeconds_;

    static std::wstring ExtendedPath(std::wstring const& path) {
        return path.starts_with(L"\\\\?\\") ? path : L"\\\\?\\" + path;
    }

    static bool IsPathWithin(std::wstring const& root, std::wstring const& path) {
        if (root.empty() || path.size() < root.size() || _wcsnicmp(root.c_str(), path.c_str(), root.size()) != 0) return false;
        if (path.size() == root.size() || root.back() == L'\\') return true;
        return path[root.size()] == L'\\';
    }

    static bool SamePath(std::wstring const& left, std::wstring const& right) {
        return _wcsicmp(left.c_str(), right.c_str()) == 0;
    }

    static bool IsDriveRoot(std::wstring const& path) {
        return path.size() == 3 && path[1] == L':' && path[2] == L'\\';
    }

    static bool LocalFixedPath(std::wstring const& input) {
        auto path = NormalizePath(input);
        if (path.size() < 3 || path[1] != L':' || path[2] != L'\\') return false;
        wchar_t root[]{path[0], L':', L'\\', L'\0'};
        return GetDriveTypeW(root) == DRIVE_FIXED;
    }

    static void Notify(std::wstring const& path) {
        SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, path.c_str(), nullptr);
    }

    static std::wstring ParentPath(std::wstring const& path) {
        auto last = path.find_last_of(L"\\/");
        if (last == std::wstring::npos) return {};
        if (last == 2 && path[1] == L':') ++last;
        return path.substr(0, last);
    }

    static bool EventAffectsEntry(std::wstring const& entryPath, std::wstring const& changedPath) {
        return IsPathWithin(entryPath, changedPath) || IsPathWithin(changedPath, entryPath);
    }

    static std::size_t SnapshotMemoryBytes(DirectorySnapshot const& snapshot) {
        std::size_t bytes{};
        for (auto const& [path, summary] : snapshot) {
            (void)summary;
            if (path.size() > std::numeric_limits<std::size_t>::max() / sizeof(wchar_t))
                return std::numeric_limits<std::size_t>::max();
            auto pathBytes = path.size() * sizeof(wchar_t);
            if (bytes > std::numeric_limits<std::size_t>::max() - SnapshotNodeAllowance ||
                pathBytes > std::numeric_limits<std::size_t>::max() - SnapshotNodeAllowance - bytes)
                return std::numeric_limits<std::size_t>::max();
            bytes += pathBytes + SnapshotNodeAllowance;
        }
        return bytes;
    }

    std::size_t RetainedSnapshotBytes(std::set<DirectorySnapshot*> const& excluded = {}) {
        std::size_t used{};
        snapshots_.erase(std::remove_if(snapshots_.begin(), snapshots_.end(), [](SnapshotReference const& reference) {
            return reference.snapshot.expired();
        }), snapshots_.end());
        for (auto const& reference : snapshots_) {
            auto snapshot = reference.snapshot.lock();
            if (!snapshot || excluded.contains(snapshot.get())) continue;
            auto bytes = SnapshotMemoryBytes(*snapshot);
            if (bytes > std::numeric_limits<std::size_t>::max() - used) return std::numeric_limits<std::size_t>::max();
            used += bytes;
        }
        return used;
    }

    std::optional<SnapshotMatch> FindSnapshotAncestor(std::wstring const& path, ULONGLONG now) const {
        std::shared_ptr<Entry> best;
        for (auto const& [candidatePath, candidate] : entries_) {
            if (!candidate->ready || now >= candidate->expires || !candidate->snapshot ||
                (candidate->watch && !candidate->watch->Valid()) ||
                candidatePath.size() >= path.size() || !IsPathWithin(candidatePath, path)) continue;
            auto found = candidate->snapshot->find(path);
            if (found == candidate->snapshot->end()) continue;
            if (!best || candidatePath.size() > best->path.size()) best = candidate;
        }
        if (!best) return std::nullopt;
        auto summary = best->snapshot->find(path)->second;
        return SnapshotMatch{std::move(best), summary};
    }

    bool MakeRoom() {
        if (entries_.size() < MaximumCachedFolders) return true;
        if (retired_.size() >= MaximumCachedFolders) return false;
        auto oldest = std::min_element(entries_.begin(), entries_.end(), [](auto const& left, auto const& right) {
            return left.second->used < right.second->used;
        });
        oldest->second->retired = true;
        retired_.push_back(oldest->second);
        entries_.erase(oldest);
        return true;
    }

    void ReuseSnapshot(Entry& destination, Entry const& source, DirectorySummary const& summary, ULONGLONG now) {
        destination.bytes = summary.bytes;
        destination.expires = source.expires;
        destination.retryAfter = 0;
        destination.ready = true;
        destination.wanted = false;
        destination.incrementalPending = false;
        destination.snapshot = source.snapshot;
        destination.snapshotRoot = source.snapshotRoot;
        destination.watch = source.watch;
        destination.sizeSortPriority = false;
        destination.used = now;
    }

    std::shared_ptr<DirectoryWatch> AcquireWatcher(std::wstring const& path) {
        std::lock_guard lock(mutex_);
        std::shared_ptr<DirectoryWatch> covering;
        for (auto const& [candidatePath, entry] : entries_) {
            (void)candidatePath;
            if (entry->watch && entry->watch->Valid() && IsPathWithin(entry->watch->Root(), path) &&
                (!covering || entry->watch->Root().size() > covering->Root().size())) covering = entry->watch;
        }
        if (covering) return covering;

        auto parent = ParentPath(path);
        bool hasSiblingWatcher{};
        if (!parent.empty() && !IsDriveRoot(parent)) {
            for (auto const& [candidatePath, entry] : entries_) {
                if (!SamePath(candidatePath, path) && entry->watch && entry->watch->Valid() &&
                    SamePath(ParentPath(candidatePath), parent)) {
                    hasSiblingWatcher = true;
                    break;
                }
            }
        }
        bool hasDescendantWatcher{};
        for (auto const& [candidatePath, entry] : entries_) {
            (void)candidatePath;
            if (entry->watch && entry->watch->Valid() && IsPathWithin(path, entry->watch->Root())) {
                hasDescendantWatcher = true;
                break;
            }
        }
        auto root = hasSiblingWatcher ? parent : path;
        auto watcher = std::make_shared<DirectoryWatch>(root);
        if (!watcher->Valid()) return nullptr;
        for (auto const& [candidatePath, entry] : entries_) {
            if (entry->retired || !IsPathWithin(root, candidatePath)) continue;
            if (candidatePath == path || hasSiblingWatcher || hasDescendantWatcher || !entry->watch)
                entry->watch = watcher;
        }
        return watcher;
    }

    static bool IsDirectoryPath(std::wstring const& path) {
        auto attributes = GetFileAttributesW(ExtendedPath(path).c_str());
        return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0 &&
            (attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0;
    }

    static std::wstring NearestExistingDirectory(std::wstring path) {
        path = NormalizePath(std::move(path));
        while (!path.empty() && !IsDirectoryPath(path)) {
            auto parent = ParentPath(path);
            if (parent.empty() || SamePath(parent, path)) return {};
            path = std::move(parent);
        }
        return path;
    }

    static void AddCoalescedPath(std::vector<std::wstring>& paths, std::wstring path) {
        for (auto const& existing : paths)
            if (IsPathWithin(existing, path)) return;
        paths.erase(std::remove_if(paths.begin(), paths.end(), [&](std::wstring const& existing) {
            return IsPathWithin(path, existing);
        }), paths.end());
        paths.push_back(std::move(path));
    }

    static bool HasTarget(PendingChange const& change, DirectorySnapshot* snapshot) {
        return std::any_of(change.targets.begin(), change.targets.end(), [&](SnapshotTarget const& target) {
            return target.snapshot.get() == snapshot;
        });
    }

    bool QueueIncremental(std::wstring const& path, SnapshotTarget target, ULONGLONG now) {
        PendingChange merged;
        merged.path = path;
        merged.due = now + ChangeRetryDelayMilliseconds;
        merged.targets.push_back(std::move(target));
        for (auto it = pendingChanges_.begin(); it != pendingChanges_.end();) {
            auto overlaps = IsPathWithin(it->second.path, path) || IsPathWithin(path, it->second.path);
            auto samePath = SamePath(it->second.path, path);
            auto sharesSnapshot = std::any_of(merged.targets.begin(), merged.targets.end(), [&](SnapshotTarget const& current) {
                return HasTarget(it->second, current.snapshot.get());
            });
            if (!samePath && !(overlaps && sharesSnapshot)) { ++it; continue; }

            if (IsPathWithin(it->second.path, merged.path)) merged.path = it->second.path;
            for (auto const& existingTarget : it->second.targets) {
                auto duplicate = std::any_of(merged.targets.begin(), merged.targets.end(), [&](SnapshotTarget const& current) {
                    return current.snapshot == existingTarget.snapshot;
                });
                if (!duplicate) merged.targets.push_back(existingTarget);
            }
            it = pendingChanges_.erase(it); // Restart overlapping work so its result includes the latest filesystem state.
        }
        if (pendingChanges_.size() >= MaximumPendingChanges) return false;
        auto key = merged.path;
        pendingChanges_.insert_or_assign(std::move(key), std::move(merged));
        return true;
    }

    std::size_t RemovePendingTargets(DirectorySnapshot* snapshot) {
        std::size_t cancelled{};
        for (auto it = pendingChanges_.begin(); it != pendingChanges_.end();) {
            auto& change = it->second;
            auto oldSize = change.targets.size();
            change.targets.erase(std::remove_if(change.targets.begin(), change.targets.end(), [&](SnapshotTarget const& target) {
                return target.snapshot.get() == snapshot;
            }), change.targets.end());
            if (change.targets.size() == oldSize) { ++it; continue; }
            if (change.targets.empty()) {
                if (change.scan) ++cancelled;
                it = pendingChanges_.erase(it);
            } else ++it;
        }
        return cancelled;
    }

    void InvalidateEntryLocked(Entry& entry, ULONGLONG now, std::vector<std::wstring>& refresh,
        ChangeHandling& handling) {
        if (entry.scan) {
            entry.scan.reset();
            entry.scanStarted = {};
            ++handling.cancelledScans;
        }
        if (entry.ready) refresh.push_back(entry.path);
        entry.ready = false;
        entry.wanted = true;
        entry.incrementalPending = false;
        entry.snapshot.reset();
        entry.snapshotRoot.clear();
        entry.retryAfter = now + ChangeRetryDelayMilliseconds;
    }

    ChangeHandling FallbackSnapshot(DirectorySnapshot* snapshot) {
        ChangeHandling handling;
        handling.cancelledIncrementalScans += RemovePendingTargets(snapshot);
        std::vector<std::wstring> refresh;
        auto now = GetTickCount64();
        {
            std::lock_guard lock(mutex_);
            for (auto const& [path, entry] : entries_) {
                (void)path;
                if (entry->snapshot.get() == snapshot) InvalidateEntryLocked(*entry, now, refresh, handling);
            }
        }
        for (auto const& path : refresh) Notify(path);
        return handling;
    }

    ChangeHandling FallbackEntry(std::shared_ptr<Entry> const& entry) {
        ChangeHandling handling;
        std::vector<std::wstring> refresh;
        auto now = GetTickCount64();
        {
            std::lock_guard lock(mutex_);
            if (!entry->retired) InvalidateEntryLocked(*entry, now, refresh, handling);
        }
        for (auto const& path : refresh) Notify(path);
        return handling;
    }

    ChangeHandling HandleWatcherFailure(std::shared_ptr<DirectoryWatch> const& watcher) {
        ChangeHandling handling;
        InterlockedIncrement(&session_.Get()->watcherOverflows);
        std::vector<std::shared_ptr<DirectorySnapshot>> snapshots;
        std::vector<std::shared_ptr<Entry>> entriesWithoutSnapshot;
        std::set<DirectorySnapshot*> seen;
        {
            std::lock_guard lock(mutex_);
            for (auto const& [path, entry] : entries_) {
                (void)path;
                if (entry->retired || entry->watch != watcher) continue;
                entry->watch.reset();
                if (entry->snapshot) {
                    if (seen.insert(entry->snapshot.get()).second) snapshots.push_back(entry->snapshot);
                } else entriesWithoutSnapshot.push_back(entry);
            }
        }
        for (auto const& snapshot : snapshots) {
            auto result = FallbackSnapshot(snapshot.get());
            handling.cancelledScans += result.cancelledScans;
            handling.cancelledIncrementalScans += result.cancelledIncrementalScans;
        }
        for (auto const& entry : entriesWithoutSnapshot) {
            auto result = FallbackEntry(entry);
            handling.cancelledScans += result.cancelledScans;
        }
        return handling;
    }

    void RetryUnmonitoredEntries() {
        std::vector<std::shared_ptr<Entry>> candidates;
        auto now = GetTickCount64();
        {
            std::lock_guard lock(mutex_);
            for (auto const& [path, entry] : entries_) {
                (void)path;
                if (!entry->retired && !entry->watch && (entry->ready || entry->wanted) &&
                    now >= entry->watchRetryAfter) {
                    entry->watchRetryAfter = now + WatcherRetryDelayMilliseconds;
                    candidates.push_back(entry);
                }
            }
        }
        for (auto const& entry : candidates) {
            auto watcher = AcquireWatcher(entry->path);
            if (!watcher) continue;
            std::shared_ptr<DirectorySnapshot> snapshot;
            bool ready{};
            {
                std::lock_guard lock(mutex_);
                if (entry->retired) continue;
                if (!entry->watch) entry->watch = watcher;
                else watcher = entry->watch;
                if (!watcher || !watcher->Valid()) continue;
                entry->watchRetryAfter = 0;
                snapshot = entry->snapshot;
                ready = entry->ready;
            }
            if (snapshot) FallbackSnapshot(snapshot.get());
            else if (ready) FallbackEntry(entry);
        }
    }

    void MarkSnapshotPending(DirectorySnapshot* snapshot) {
        std::vector<std::wstring> refresh;
        {
            std::lock_guard lock(mutex_);
            for (auto const& [path, entry] : entries_) {
                (void)path;
                if (entry->retired || entry->snapshot.get() != snapshot) continue;
                if (entry->ready) refresh.push_back(entry->path);
                entry->ready = false;
                entry->wanted = false;
                entry->incrementalPending = true;
                entry->retryAfter = 0;
            }
        }
        for (auto const& path : refresh) Notify(path);
    }

    ChangeHandling HandleChanges(std::shared_ptr<DirectoryWatch> const& watcher, DirectoryChangeBatch const& batch,
        Entry* current = nullptr) {
        ChangeHandling handling;
        if (!batch.changed) return handling;
        auto eventCount = batch.paths.empty() ? 1 : static_cast<LONG>(std::min<std::size_t>(batch.paths.size(), LONG_MAX));
        InterlockedAdd(&session_.Get()->watchEvents, eventCount);
        if (batch.overflow && !watcher->Valid()) {
            if (current) {
                std::lock_guard lock(mutex_);
                for (auto const& [path, entry] : entries_) {
                    (void)path;
                    if (entry.get() == current && entry->watch == watcher) { handling.currentAffected = true; break; }
                }
            }
            return HandleWatcherFailure(watcher);
        }
        if (batch.overflow) InterlockedIncrement(&session_.Get()->watcherOverflows);
        std::vector<std::shared_ptr<Entry>> fallbackEntries;
        std::map<DirectorySnapshot*, SnapshotChangeGroup> groups;
        auto now = GetTickCount64();
        {
            std::lock_guard lock(mutex_);
            for (auto const& [path, entry] : entries_) {
                (void)path;
                if (entry->watch != watcher || entry->retired) continue;
                auto affected = batch.overflow || std::any_of(batch.paths.begin(), batch.paths.end(), [&](std::wstring const& changed) {
                    return EventAffectsEntry(entry->path, changed);
                });
                if (!affected) continue;
                if (entry.get() == current) handling.currentAffected = true;
                if (entry->scan) {
                    entry->scan.reset();
                    entry->scanStarted = {};
                    ++handling.cancelledScans;
                }
                if (batch.overflow || !entry->snapshot) {
                    fallbackEntries.push_back(entry);
                    continue;
                }
                auto [groupIt, inserted] = groups.try_emplace(entry->snapshot.get());
                auto& group = groupIt->second;
                if (inserted) {
                    group.snapshot = entry->snapshot;
                    group.root = entry->snapshotRoot;
                } else if (!SamePath(group.root, entry->snapshotRoot)) {
                    group.fallback = true;
                }
                if (group.root.empty() || group.snapshot->find(group.root) == group.snapshot->end()) group.fallback = true;
                for (auto const& changed : batch.paths) {
                    if (!EventAffectsEntry(entry->path, changed)) continue;
                    if (!IsPathWithin(group.root, changed)) { group.fallback = true; continue; }
                    auto scanPath = NearestExistingDirectory(changed);
                    if (scanPath.empty() || !IsPathWithin(group.root, scanPath)) { group.fallback = true; continue; }
                    AddCoalescedPath(group.paths, std::move(scanPath));
                }
            }
        }

        for (auto const& entry : fallbackEntries) {
            if (entry->snapshot) {
                auto result = FallbackSnapshot(entry->snapshot.get());
                handling.cancelledScans += result.cancelledScans;
                handling.cancelledIncrementalScans += result.cancelledIncrementalScans;
            } else {
                auto result = FallbackEntry(entry);
                handling.cancelledScans += result.cancelledScans;
            }
        }
        for (auto& [snapshot, group] : groups) {
            if (group.fallback || group.paths.empty()) {
                auto result = FallbackSnapshot(snapshot);
                handling.cancelledScans += result.cancelledScans;
                handling.cancelledIncrementalScans += result.cancelledIncrementalScans;
                continue;
            }
            bool queued = true;
            for (auto const& path : group.paths) {
                if (!QueueIncremental(path, SnapshotTarget{group.snapshot, group.root}, now)) { queued = false; break; }
            }
            if (!queued) {
                auto result = FallbackSnapshot(snapshot);
                handling.cancelledScans += result.cancelledScans;
                handling.cancelledIncrementalScans += result.cancelledIncrementalScans;
            } else MarkSnapshotPending(snapshot);
        }
        return handling;
    }

    void ScheduleParentRefresh(std::wstring const& path, ULONGLONG now) {
        auto parentPath = ParentPath(path);
        if (parentPath.empty()) return;
        auto found = sortedParents_.find(parentPath);
        if (found == sortedParents_.end()) {
            if (sortedParents_.size() >= MaximumSortedParents) {
                auto oldest = std::min_element(sortedParents_.begin(), sortedParents_.end(),
                    [](auto const& left, auto const& right) { return left.second.used < right.second.used; });
                sortedParents_.erase(oldest);
            }
            found = sortedParents_.try_emplace(std::move(parentPath)).first;
        }
        auto& refresh = found->second;
        if (!refresh.pending) { refresh.pending = true; refresh.deadline = now + SortRefreshMaximumDelayMilliseconds; }
        refresh.due = std::min(now + SortRefreshDelayMilliseconds, refresh.deadline);
    }

    static bool WithinSnapshotLimits(DirectorySnapshot const& snapshot) {
        if (snapshot.size() > MaximumSnapshotDirectories) return false;
        std::size_t characters{};
        for (auto const& [path, summary] : snapshot) {
            (void)summary;
            if (path.size() > MaximumSnapshotPathCharacters - std::min(characters, MaximumSnapshotPathCharacters)) return false;
            characters += path.size();
        }
        return true;
    }

    static bool AddSnapshotEntrySize(std::wstring const& path, std::size_t& count,
        std::size_t& characters, std::size_t& memoryBytes) {
        if (count >= MaximumSnapshotDirectories ||
            path.size() > MaximumSnapshotPathCharacters - std::min(characters, MaximumSnapshotPathCharacters) ||
            path.size() > std::numeric_limits<std::size_t>::max() / sizeof(wchar_t)) return false;
        auto pathBytes = path.size() * sizeof(wchar_t);
        if (memoryBytes > std::numeric_limits<std::size_t>::max() - SnapshotNodeAllowance ||
            pathBytes > std::numeric_limits<std::size_t>::max() - SnapshotNodeAllowance - memoryBytes) return false;
        ++count;
        characters += path.size();
        memoryBytes += pathBytes + SnapshotNodeAllowance;
        return true;
    }

    static bool ApplyByteDelta(ULONGLONG& value, ULONGLONG before, ULONGLONG after) {
        if (after >= before) {
            auto delta = after - before;
            if (delta > std::numeric_limits<ULONGLONG>::max() - value) return false;
            value += delta;
        } else {
            auto delta = before - after;
            if (delta > value) return false;
            value -= delta;
        }
        return true;
    }

    static bool EstimateReconciledSnapshotBytes(SnapshotTarget const& target, std::wstring const& scanPath,
        DirectorySnapshot const& scanned, std::wstring& replacementRoot, std::size_t& memoryBytes) {
        auto scanCoversTarget = IsPathWithin(scanPath, target.root);
        auto targetCoversScan = IsPathWithin(target.root, scanPath);
        if (!scanCoversTarget && !targetCoversScan) return false;
        replacementRoot = scanCoversTarget ? target.root : scanPath;
        auto scannedRoot = scanned.find(replacementRoot);
        if (scannedRoot == scanned.end()) return false;
        auto oldRoot = target.snapshot->find(replacementRoot);
        if (replacementRoot == target.root && oldRoot == target.snapshot->end()) return false;
        std::size_t count{};
        std::size_t characters{};
        memoryBytes = 0;
        for (auto const& [path, summary] : *target.snapshot) {
            (void)summary;
            if (!IsPathWithin(replacementRoot, path) && !AddSnapshotEntrySize(path, count, characters, memoryBytes)) return false;
        }
        for (auto const& [path, summary] : scanned) {
            (void)summary;
            if (IsPathWithin(replacementRoot, path) && !AddSnapshotEntrySize(path, count, characters, memoryBytes)) return false;
        }
        return true;
    }

    static bool ReconcileSnapshot(SnapshotTarget const& target, std::wstring const& replacementRoot,
        DirectorySnapshot const& scanned, DirectorySnapshot& result) {
        auto scannedRoot = scanned.find(replacementRoot);
        if (scannedRoot == scanned.end()) return false;
        auto oldRoot = target.snapshot->find(replacementRoot);
        if (replacementRoot == target.root && oldRoot == target.snapshot->end()) return false;
        auto oldBytes = oldRoot == target.snapshot->end() ? 0 : oldRoot->second.bytes;
        auto newBytes = scannedRoot->second.bytes;

        result = *target.snapshot;
        for (auto it = result.begin(); it != result.end();) {
            if (IsPathWithin(replacementRoot, it->first)) it = result.erase(it);
            else ++it;
        }
        for (auto const& [path, summary] : scanned) {
            if (IsPathWithin(replacementRoot, path)) result.insert_or_assign(path, summary);
        }
        auto updatedRoot = result.find(replacementRoot);
        if (updatedRoot == result.end() || updatedRoot->second.bytes != newBytes) return false;
        for (auto& [path, summary] : result) {
            if (SamePath(path, replacementRoot) || !IsPathWithin(path, replacementRoot)) continue;
            if (!ApplyByteDelta(summary.bytes, oldBytes, newBytes)) return false;
        }
        return WithinSnapshotLimits(result);
    }

    bool HasPendingSnapshot(DirectorySnapshot* snapshot) const {
        return std::any_of(pendingChanges_.begin(), pendingChanges_.end(), [&](auto const& item) {
            return HasTarget(item.second, snapshot);
        });
    }

    void FallbackChange(PendingChange const& change) {
        std::set<DirectorySnapshot*> visited;
        for (auto const& target : change.targets) {
            if (!target.snapshot || !visited.insert(target.snapshot.get()).second) continue;
            FallbackSnapshot(target.snapshot.get());
        }
    }

    bool CommitIncrementalChange(PendingChange const& change, DirectorySnapshot const& scanned) {
        struct ReplacementPlan {
            SnapshotTarget target;
            std::wstring root;
            std::size_t memoryBytes{};
        };
        std::vector<ReplacementPlan> plans;
        std::vector<ReconciledSnapshot> replacements;
        std::set<DirectorySnapshot*> updated;
        std::vector<std::wstring> refresh;
        auto now = GetTickCount64();
        {
            std::lock_guard lock(mutex_);
            for (auto const& target : change.targets) {
                if (!target.snapshot || !updated.insert(target.snapshot.get()).second) continue;
                ReplacementPlan plan{target, {}, 0};
                if (!EstimateReconciledSnapshotBytes(target, change.path, scanned, plan.root, plan.memoryBytes)) return false;
                plans.push_back(std::move(plan));
            }
            std::size_t replacementBytes{};
            for (auto const& plan : plans) {
                if (plan.memoryBytes > MaximumSnapshotBytes - std::min(replacementBytes, MaximumSnapshotBytes)) return false;
                replacementBytes += plan.memoryBytes;
            }
            auto retained = RetainedSnapshotBytes(updated);
            if (retained > MaximumSnapshotBytes - replacementBytes) return false;

            replacements.reserve(plans.size());
            for (auto const& plan : plans) {
                ReconciledSnapshot replacement{plan.target.snapshot, {}};
                if (!ReconcileSnapshot(plan.target, plan.root, scanned, replacement.contents)) return false;
                replacements.push_back(std::move(replacement));
            }

            for (auto& replacement : replacements) *replacement.target = std::move(replacement.contents);
            for (auto const& [path, entry] : entries_) {
                (void)path;
                if (!entry->snapshot || !updated.contains(entry->snapshot.get()) || entry->retired) continue;
                auto summary = entry->snapshot->find(entry->path);
                entry->incrementalPending = HasPendingSnapshot(entry->snapshot.get());
                entry->wanted = false;
                if (summary == entry->snapshot->end()) {
                    entry->ready = false;
                    entry->retryAfter = now + static_cast<ULONGLONG>(cacheSeconds_) * 1000;
                    continue;
                }
                entry->bytes = summary->second.bytes;
                entry->expires = now + static_cast<ULONGLONG>(cacheSeconds_) * 1000;
                entry->retryAfter = 0;
                entry->ready = !entry->incrementalPending;
                if (entry->ready) {
                    ScheduleParentRefresh(entry->path, now);
                    refresh.push_back(entry->path);
                }
            }
        }
        InterlockedAdd(&session_.Get()->incrementalUpdates, static_cast<LONG>(replacements.size()));
        for (auto const& path : refresh) Notify(path);
        return true;
    }

    void StepPendingChanges(std::size_t& active) {
        auto now = GetTickCount64();
        for (auto it = pendingChanges_.begin(); it != pendingChanges_.end();) {
            if (!session_.Alive()) return;
            auto& change = it->second;
            if (!change.scan && now >= change.due && active < MaximumActiveScans) {
                change.scan = std::make_unique<FolderScan>(ExtendedPath(change.path), scanSeconds_,
                    SnapshotLimits{MaximumSnapshotDirectories, MaximumSnapshotPathCharacters});
                change.started = std::chrono::steady_clock::now();
                ++active;
            }
            if (!change.scan) { ++it; continue; }
            auto status = change.scan->Step([this] { return session_.Alive(); });
            if (status == ScanStatus::Pending) { ++it; continue; }
            auto scanned = status == ScanStatus::Complete ? change.scan->TakeSnapshot() : std::nullopt;
            auto elapsed = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now() - change.started).count();
            InterlockedAdd64(&session_.Get()->scanMicroseconds, static_cast<LONG64>(elapsed));
            auto finished = std::move(change);
            it = pendingChanges_.erase(it);
            --active;
            if (!scanned || !CommitIncrementalChange(finished, *scanned)) FallbackChange(finished);
        }
    }

public:
    explicit FolderSizeEngine(Session& session) : session_(session),
        scanSeconds_(std::clamp(session.Get()->scanSeconds, 5UL, 120UL)),
        cacheSeconds_(std::clamp(session.Get()->cacheSeconds, 5UL, 300UL)) {}

    void RememberSizeSort(std::wstring const& path) {
        auto parent = ParentPath(NormalizePath(path));
        if (parent.empty()) return;
        std::lock_guard lock(mutex_);
        if (!session_.Alive()) return;
        auto found = sortedParents_.find(parent);
        if (found == sortedParents_.end()) {
            if (sortedParents_.size() >= MaximumSortedParents) {
                auto oldest = std::min_element(sortedParents_.begin(), sortedParents_.end(),
                    [](auto const& left, auto const& right) { return left.second.used < right.second.used; });
                sortedParents_.erase(oldest);
            }
            found = sortedParents_.try_emplace(std::move(parent)).first;
        }
        found->second.used = GetTickCount64();
    }

    std::optional<ULONGLONG> Query(std::wstring const& inputPath, bool sizeSort = false) {
        auto path = NormalizePath(inputPath);
        std::lock_guard lock(mutex_);
        if (!session_.Alive()) return std::nullopt;
        auto now = GetTickCount64();
        InterlockedIncrement(&session_.Get()->requests);
        auto found = entries_.find(path);
        if (found != entries_.end()) {
            auto& entry = *found->second;
            entry.used = now;
            if (entry.incrementalPending) return std::nullopt;
            if (entry.watch && !entry.watch->Valid()) {
                entry.ready = false;
                entry.wanted = true;
                entry.incrementalPending = false;
                entry.retryAfter = now + ChangeRetryDelayMilliseconds;
                wake_.notify_one();
                return std::nullopt;
            }
            if (entry.ready && now < entry.expires) {
                InterlockedIncrement(&session_.Get()->hits);
                return entry.bytes;
            }
            if (auto ancestor = FindSnapshotAncestor(path, now)) {
                ReuseSnapshot(entry, *ancestor->entry, ancestor->summary, now);
                InterlockedIncrement(&session_.Get()->hits);
                InterlockedIncrement(&session_.Get()->subtreeCacheHits);
                return entry.bytes;
            }
            if (sizeSort) entry.sizeSortPriority = true;
            if (now >= entry.retryAfter) { entry.wanted = true; wake_.notify_one(); }
            return std::nullopt;
        }

        auto ancestor = FindSnapshotAncestor(path, now);
        if (!MakeRoom()) return std::nullopt;
        auto entry = std::make_shared<Entry>();
        entry->path = path;
        entry->used = now;
        entry->sizeSortPriority = sizeSort;
        if (ancestor) {
            ReuseSnapshot(*entry, *ancestor->entry, ancestor->summary, now);
            auto bytes = entry->bytes;
            entries_.emplace(path, std::move(entry));
            InterlockedIncrement(&session_.Get()->hits);
            InterlockedIncrement(&session_.Get()->subtreeCacheHits);
            return bytes;
        }
        entries_.emplace(path, std::move(entry));
        wake_.notify_one();
        return std::nullopt;
    }

    void Run() {
        SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_BEGIN);
        while (session_.Alive()) {
            std::vector<std::shared_ptr<Entry>> entries;
            std::vector<std::shared_ptr<Entry>> retiredEntries;
            std::vector<std::shared_ptr<DirectoryWatch>> watchers;
            std::set<DirectoryWatch*> seenWatchers;
            {
                std::lock_guard lock(mutex_);
                for (auto const& [path, entry] : entries_) {
                    (void)path;
                    entries.push_back(entry);
                    if (entry->watch && seenWatchers.insert(entry->watch.get()).second) watchers.push_back(entry->watch);
                }
                retiredEntries.swap(retired_);
            }
            retiredEntries.clear(); // Watch handles are closed on this worker, never in Explorer's callback.

            for (auto const& watcher : watchers) {
                if (!watcher->Valid()) { HandleWatcherFailure(watcher); continue; }
                auto changes = watcher->ReadChanges();
                if (changes.changed) HandleChanges(watcher, changes);
            }
            RetryUnmonitoredEntries();

            std::vector<ScanCandidate> candidates;
            {
                std::lock_guard lock(mutex_);
                candidates.reserve(entries_.size());
                for (auto const& [path, entry] : entries_) {
                    (void)path;
                    candidates.push_back({entry, entry->sizeSortPriority, entry->used});
                }
            }
            std::sort(candidates.begin(), candidates.end(), [](ScanCandidate const& left, ScanCandidate const& right) {
                return ScanRequestPrecedes(left.sizeSortPriority, left.used, right.sizeSortPriority, right.used);
            });

            std::size_t active{};
            for (auto const& candidate : candidates) if (candidate.entry->scan) ++active;
            StepPendingChanges(active);
            for (auto const& candidate : candidates) {
                auto const& entry = candidate.entry;
                if (!session_.Alive()) break;
                bool retired{}, ready{}, wanted{}, priority{};
                ULONGLONG retryAfter{};
                {
                    std::lock_guard lock(mutex_);
                    retired = entry->retired;
                    ready = entry->ready;
                    wanted = entry->wanted;
                    retryAfter = entry->retryAfter;
                    priority = entry->sizeSortPriority;
                }
                if (retired) continue;
                if (entry->scan && ready) { entry->scan.reset(); entry->scanStarted = {}; --active; }
                if (!wanted || GetTickCount64() < retryAfter) continue;
                if (!entry->scan && active < MaximumActiveScans) {
                    if (!LocalFixedPath(entry->path)) { Complete(*entry, false, 0); continue; }
                    std::shared_ptr<DirectoryWatch> watcher;
                    { std::lock_guard lock(mutex_); watcher = entry->watch; }
                    if (!watcher) watcher = AcquireWatcher(entry->path);
                    {
                        std::lock_guard lock(mutex_);
                        if (!entry->watch) {
                            entry->watch = watcher;
                            entry->watchRetryAfter = watcher ? 0 : GetTickCount64() + WatcherRetryDelayMilliseconds;
                        }
                        else watcher = entry->watch;
                    }
                    auto path = ExtendedPath(entry->path);
                    entry->scan = std::make_unique<FolderScan>(std::move(path), scanSeconds_);
                    entry->scanStarted = std::chrono::steady_clock::now();
                    if (priority) InterlockedIncrement(&session_.Get()->priorityScans);
                    ++active;
                }
                if (!entry->scan) continue;
                auto status = entry->scan->Step([this] { return session_.Alive(); });
                if (status == ScanStatus::Pending) continue;
                auto bytes = entry->scan->Bytes();
                auto snapshot = status == ScanStatus::Complete ? entry->scan->TakeSnapshot() : std::nullopt;
                entry->scan.reset();
                --active;

                std::shared_ptr<DirectoryWatch> watcher;
                { std::lock_guard lock(mutex_); watcher = entry->watch; }
                if (watcher) {
                    auto changes = watcher->ReadChanges();
                    if (changes.changed) {
                        auto handling = HandleChanges(watcher, changes, entry.get());
                        auto cancelled = handling.cancelledScans + handling.cancelledIncrementalScans;
                        active -= std::min(active, cancelled);
                        if (handling.currentAffected) continue;
                    }
                }
                Complete(*entry, status == ScanStatus::Complete, bytes, std::move(snapshot));
            }

            std::vector<std::wstring> refreshParents;
            {
                std::unique_lock lock(mutex_);
                LONG pending{};
                std::set<DirectoryWatch*> activeWatchers;
                for (auto const& [path, entry] : entries_) {
                    (void)path;
                    if (entry->wanted || entry->incrementalPending) ++pending;
                    if (entry->watch && entry->watch->Valid()) activeWatchers.insert(entry->watch.get());
                }
                InterlockedExchange(&session_.Get()->pending, pending);
                InterlockedExchange(&session_.Get()->activeWatchers, static_cast<LONG>(activeWatchers.size()));
                auto now = GetTickCount64();
                for (auto& [path, parent] : sortedParents_) {
                    if (parent.pending && now >= parent.due) { parent.pending = false; refreshParents.push_back(path); }
                }
                wake_.wait_for(lock, std::chrono::milliseconds(active ? 1 : 100));
            }
            for (auto const& parent : refreshParents)
                SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, parent.c_str(), nullptr);
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
            sortedParents_.clear();
            snapshots_.clear();
        }
        // Final watcher and scanner handles are released on the monitor or I/O worker.
    }

private:
    void Complete(Entry& entry, bool complete, ULONGLONG bytes, std::optional<DirectorySnapshot> snapshot = std::nullopt) {
        if (entry.scanStarted != std::chrono::steady_clock::time_point{}) {
            InterlockedIncrement(&session_.Get()->fullScans);
            auto elapsed = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now() - entry.scanStarted).count();
            InterlockedAdd64(&session_.Get()->scanMicroseconds, static_cast<LONG64>(elapsed));
            entry.scanStarted = {};
        }
        std::shared_ptr<DirectorySnapshot> retainedSnapshot;
        std::vector<std::wstring> refresh;
        {
            std::lock_guard lock(mutex_);
            if (entry.retired) return;
            if (complete && snapshot) {
                auto candidate = std::make_shared<DirectorySnapshot>(std::move(*snapshot));
                auto bytesUsed = SnapshotMemoryBytes(*candidate);
                auto retained = RetainedSnapshotBytes();
                if (bytesUsed <= MaximumSnapshotBytes && retained <= MaximumSnapshotBytes - bytesUsed) {
                    retainedSnapshot = std::move(candidate);
                    snapshots_.push_back({retainedSnapshot});
                }
            }
            entry.wanted = false;
            entry.ready = complete;
            entry.incrementalPending = false;
            entry.bytes = bytes;
            entry.expires = GetTickCount64() + static_cast<ULONGLONG>(cacheSeconds_) * 1000;
            entry.retryAfter = complete ? 0 : entry.expires;
            entry.sizeSortPriority = false;
            entry.snapshot = retainedSnapshot;
            entry.snapshotRoot = retainedSnapshot ? entry.path : std::wstring{};
            InterlockedIncrement(complete ? &session_.Get()->completed : &session_.Get()->incomplete);
            if (complete) {
                ScheduleParentRefresh(entry.path, GetTickCount64());
                if (retainedSnapshot) {
                    for (auto const& [path, child] : entries_) {
                        if (child.get() == &entry || child->retired || !IsPathWithin(entry.path, path)) continue;
                        auto found = retainedSnapshot->find(path);
                        if (found == retainedSnapshot->end()) continue;
                        child->bytes = found->second.bytes;
                        child->ready = true;
                        child->wanted = false;
                        child->incrementalPending = false;
                        child->retryAfter = 0;
                        child->expires = entry.expires;
                        child->snapshot = retainedSnapshot;
                        child->snapshotRoot = entry.path;
                        child->watch = entry.watch;
                        child->sizeSortPriority = false;
                        ScheduleParentRefresh(path, GetTickCount64());
                        refresh.push_back(path);
                        InterlockedIncrement(&session_.Get()->subtreeCacheHits);
                    }
                }
            }
        }
        if (complete) Notify(entry.path);
        for (auto const& path : refresh) Notify(path);
    }
};
}
