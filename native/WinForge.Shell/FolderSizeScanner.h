#pragma once
#include <windows.h>
#include <algorithm>
#include <chrono>
#include <functional>
#include <limits>
#include <string>
#include <vector>

namespace winforge::folders {
enum class ScanStatus { Pending, Complete, Incomplete, Cancelled };

// Enumeration stays resumable so a large tree cannot monopolize the single I/O worker.
class FolderScan {
    struct Frame { std::wstring path; HANDLE handle{INVALID_HANDLE_VALUE}; WIN32_FIND_DATAW item{}; bool first{true}; };
    std::vector<Frame> frames_;
    ULONGLONG started_{GetTickCount64()};
    ULONGLONG limit_;
    ULONGLONG bytes_{};
    std::size_t entries_{};
    ScanStatus status_{ScanStatus::Pending};
    static constexpr std::size_t MaximumDepth = 256;
    static constexpr std::size_t MaximumEntries = 2000000;

    void Finish(ScanStatus status) {
        status_ = status;
        for (auto& frame : frames_) if (frame.handle != INVALID_HANDLE_VALUE) FindClose(frame.handle);
        frames_.clear();
    }
public:
    explicit FolderScan(std::wstring path, DWORD seconds) : limit_(static_cast<ULONGLONG>(seconds) * 1000) {
        auto attributes = GetFileAttributesW(path.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES || !(attributes & FILE_ATTRIBUTE_DIRECTORY) || attributes & FILE_ATTRIBUTE_REPARSE_POINT) {
            status_ = ScanStatus::Incomplete;
            return;
        }
        frames_.push_back({std::move(path)});
    }
    FolderScan(FolderScan const&) = delete;
    ~FolderScan() { Finish(status_); }
    ULONGLONG Bytes() const { return bytes_; }
    ScanStatus Status() const { return status_; }

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
                    if (GetLastError() == ERROR_FILE_NOT_FOUND) { frames_.pop_back(); continue; }
                    Finish(ScanStatus::Incomplete); break;
                }
            } else if (!frame.first && !FindNextFileW(frame.handle, &frame.item)) {
                if (GetLastError() != ERROR_NO_MORE_FILES) { Finish(ScanStatus::Incomplete); break; }
                FindClose(frame.handle);
                frames_.pop_back();
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
                frames_.push_back({frame.path + L"\\" + item.cFileName});
            } else {
                auto size = (static_cast<ULONGLONG>(item.nFileSizeHigh) << 32) | item.nFileSizeLow;
                if (size > std::numeric_limits<ULONGLONG>::max() - bytes_) { Finish(ScanStatus::Incomplete); break; }
                bytes_ += size;
            }
        }
        if (status_ == ScanStatus::Pending && frames_.empty()) Finish(ScanStatus::Complete);
        return status_;
    }
};
}
