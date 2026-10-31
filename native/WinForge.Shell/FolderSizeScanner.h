#pragma once
#include <windows.h>
#include <algorithm>
#include <chrono>
#include <functional>
#include <limits>
#include <map>
#include <optional>
#include <string>
#include <vector>

namespace winforge::folders {
enum class ScanStatus { Pending, Complete, Incomplete, Cancelled };

struct PathLess {
    bool operator()(std::wstring const& left, std::wstring const& right) const {
        return _wcsicmp(left.c_str(), right.c_str()) < 0;
    }
};

inline std::wstring NormalizePath(std::wstring path) {
    if (path.starts_with(L"\\\\?\\")) path.erase(0, 4);
    return path;
}

struct DirectorySummary {
    ULONGLONG bytes{};
    ULONGLONG directFileBytes{};
};

using DirectorySnapshot = std::map<std::wstring, DirectorySummary, PathLess>;

struct SnapshotLimits {
    std::size_t maximumDirectories{8192};
    std::size_t maximumPathCharacters{1'048'576};
};

// Enumeration stays resumable so a large tree cannot monopolize the single I/O worker.
class FolderScan {
    struct Frame {
        std::wstring path;
        std::wstring snapshotPath;
        HANDLE handle{INVALID_HANDLE_VALUE};
        WIN32_FIND_DATAW item{};
        bool first{true};
        ULONGLONG bytes{};
        ULONGLONG directFileBytes{};
    };

    std::vector<Frame> frames_;
    DirectorySnapshot directories_;
    SnapshotLimits snapshotLimits_;
    ULONGLONG started_{GetTickCount64()};
    ULONGLONG limit_;
    ULONGLONG bytes_{};
    std::size_t entries_{};
    std::size_t snapshotPathCharacters_{};
    ScanStatus status_{ScanStatus::Pending};
    bool snapshotComplete_{true};
    static constexpr std::size_t MaximumDepth = 256;
    static constexpr std::size_t MaximumEntries = 2000000;

    void DisableSnapshot() {
        snapshotComplete_ = false;
        directories_.clear();
        snapshotPathCharacters_ = 0;
    }

    void RecordDirectory(Frame const& frame) {
        if (!snapshotComplete_) return;
        if (directories_.size() >= snapshotLimits_.maximumDirectories ||
            frame.snapshotPath.size() > snapshotLimits_.maximumPathCharacters -
                std::min(snapshotPathCharacters_, snapshotLimits_.maximumPathCharacters)) {
            DisableSnapshot();
            return;
        }
        directories_.emplace(frame.snapshotPath, DirectorySummary{frame.bytes, frame.directFileBytes});
        snapshotPathCharacters_ += frame.snapshotPath.size();
    }

    void CompleteTopDirectory() {
        auto completed = std::move(frames_.back());
        if (completed.handle != INVALID_HANDLE_VALUE) FindClose(completed.handle);
        frames_.pop_back();
        RecordDirectory(completed);
        if (frames_.empty()) return;
        auto& parent = frames_.back();
        if (completed.bytes > std::numeric_limits<ULONGLONG>::max() - parent.bytes) {
            Finish(ScanStatus::Incomplete);
            return;
        }
        parent.bytes += completed.bytes;
    }

    void Finish(ScanStatus status) {
        status_ = status;
        if (status != ScanStatus::Complete) DisableSnapshot();
        for (auto& frame : frames_) if (frame.handle != INVALID_HANDLE_VALUE) FindClose(frame.handle);
        frames_.clear();
    }

public:
    explicit FolderScan(std::wstring path, DWORD seconds, SnapshotLimits snapshotLimits = {})
        : snapshotLimits_(snapshotLimits), limit_(static_cast<ULONGLONG>(seconds) * 1000) {
        auto attributes = GetFileAttributesW(path.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES || !(attributes & FILE_ATTRIBUTE_DIRECTORY) || attributes & FILE_ATTRIBUTE_REPARSE_POINT) {
            status_ = ScanStatus::Incomplete;
            DisableSnapshot();
            return;
        }
        auto snapshotPath = NormalizePath(path);
        frames_.push_back({std::move(path), std::move(snapshotPath)});
    }
    FolderScan(FolderScan const&) = delete;
    ~FolderScan() { Finish(status_); }
    ULONGLONG Bytes() const { return bytes_; }
    ScanStatus Status() const { return status_; }
    bool SnapshotComplete() const { return snapshotComplete_ && status_ == ScanStatus::Complete; }
    std::optional<DirectorySnapshot> TakeSnapshot() {
        if (!SnapshotComplete()) return std::nullopt;
        return std::move(directories_);
    }

    ScanStatus Step(std::function<bool()> const& alive, DWORD sliceMilliseconds = 25) {
        if (status_ != ScanStatus::Pending) return status_;
        auto slice = GetTickCount64();
        while (!frames_.empty()) {
            if (!alive()) { Finish(ScanStatus::Cancelled); break; }
            if (GetTickCount64() - started_ >= limit_ || entries_ >= MaximumEntries) { Finish(ScanStatus::Incomplete); break; }
            if (GetTickCount64() - slice >= sliceMilliseconds) break;
            auto& frame = frames_.back();
            if (frame.handle == INVALID_HANDLE_VALUE) {
                auto pattern = frame.path + L"\\*";
                frame.handle = FindFirstFileExW(pattern.c_str(), FindExInfoBasic, &frame.item, FindExSearchNameMatch, nullptr, FIND_FIRST_EX_LARGE_FETCH);
                if (frame.handle == INVALID_HANDLE_VALUE) {
                    if (GetLastError() == ERROR_FILE_NOT_FOUND) { CompleteTopDirectory(); continue; }
                    Finish(ScanStatus::Incomplete); break;
                }
            } else if (!frame.first && !FindNextFileW(frame.handle, &frame.item)) {
                if (GetLastError() != ERROR_NO_MORE_FILES) { Finish(ScanStatus::Incomplete); break; }
                CompleteTopDirectory();
                continue;
            }
            frame.first = false;
            auto item = frame.item;
            if (!wcscmp(item.cFileName, L".") || !wcscmp(item.cFileName, L"..")) continue;
            ++entries_;
            if (item.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
                // Junctions, symlinks and cloud directories are boundaries; never follow them.
                if (item.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) continue;
                if (frames_.size() >= MaximumDepth) { Finish(ScanStatus::Incomplete); break; }
                auto childPath = frame.path + L"\\" + item.cFileName;
                auto childSnapshotPath = frame.snapshotPath + L"\\" + item.cFileName;
                frames_.push_back({std::move(childPath), std::move(childSnapshotPath)});
            } else {
                auto size = (static_cast<ULONGLONG>(item.nFileSizeHigh) << 32) | item.nFileSizeLow;
                if (size > std::numeric_limits<ULONGLONG>::max() - bytes_ ||
                    size > std::numeric_limits<ULONGLONG>::max() - frame.bytes ||
                    size > std::numeric_limits<ULONGLONG>::max() - frame.directFileBytes) {
                    Finish(ScanStatus::Incomplete);
                    break;
                }
                bytes_ += size;
                frame.bytes += size;
                frame.directFileBytes += size;
            }
        }
        if (status_ == ScanStatus::Pending && frames_.empty()) Finish(ScanStatus::Complete);
        return status_;
    }
};
}
