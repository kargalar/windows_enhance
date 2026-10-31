# Explorer Folder Size Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce Explorer folder-size latency and repeat filesystem work while preserving exact totals and bounded resource use.

**Architecture:** Extend the resumable native scanner to produce a bounded directory snapshot with per-directory and direct-file totals. The native engine prioritizes Size-sort requests, shares snapshots/watchers, coalesces change events, and reconciles only affected subtrees with a full-scan fallback. Add bounded diagnostics through the versioned shared-memory protocol.

**Tech Stack:** C++20 Win32 shell/filesystem APIs, C# .NET 10 Windows, existing native fixture and folder-size verification executable.

**Spec:** `docs/superpowers/specs/2026-10-31-folder-size-optimization-design.md`

## Global Constraints

- Keep exact totals; never surface incomplete totals.
- Keep the existing local fixed-drive and reparse-point boundaries.
- Bound snapshot memory and preserve a full recursive-scan fallback.
- Preserve the background worker and its low I/O priority.
- Update the native protocol and its managed reader together.
- Preserve unrelated user changes.

## Review Focus

- Unreadable, missing, or over-limit subtrees must not produce partial totals; scanner tests retain these assertions.
- Added, removed, renamed, or modified paths must reconcile the containing subtree or fall back; the live fixture covers modification and rename/delete.
- Change-buffer overflow must invalidate affected snapshots and queue full rescans; cover overflow handling in the native notification parser/helper.
- Parent and child cache entries must observe the same changed snapshot; the live fixture queries a descendant after the parent scan.
- Size-sort requests must outrank older ordinary pending requests; assert the scheduling behavior in an engine fixture.

---

### Task 1: Bounded directory snapshots

**Files:**
- Modify: `native/WinForge.Shell/FolderSizeScanner.h`
- Modify: `native/WinForge.Shell/FolderSizeHost.cpp`
- Modify: `tests/WinForge.FolderSizeTests/Program.cs`

**Interfaces:**
- `FolderScan` records `{path, bytes, directFileBytes}` for each completed directory, exposes snapshot completeness, and transfers its completed directory map to the caller.
- Snapshot collection has fixed directory-count and path-storage caps. Exceeding either cap keeps the root byte total valid but marks the snapshot unusable for incremental reconciliation.

- [x] Add a host scan-snapshot assertion for root/nested totals and both caps; watch it fail because snapshot data is absent.
- [x] Add bounded per-frame totals and snapshot capture to `FolderScan`.
- [x] Emit snapshot summaries from the host test command and run the focused scanner checks.

### Task 2: Protocol diagnostics

**Files:**
- Modify: `native/WinForge.Shell/FolderSizeProtocol.h`
- Modify: `native/WinForge.Shell/FolderSizeEngine.h`
- Modify: `src/WinForge.Core/ExplorerFolderSizeSession.cs`
- Modify: `tests/WinForge.FolderSizeTests/Program.cs`

**Interfaces:**
- Protocol v3 exposes typed counters for priority scan starts, descendant cache reuse, incremental updates, full scans, watcher events/overflows, active watcher count, and elapsed scan microseconds.
- `ExplorerFolderSizeStatus` reads these fields from the matching shared-memory offsets.

- [x] Add an owned-view assertion that waits for a full-scan counter and verify it fails because the metric is absent.
- [x] Add the native protocol fields and managed status properties/offset reads.
- [x] Increment full-scan and elapsed-scan counters in the existing full-scan path.
- [x] Run scanner checks and the owned-view fixture.

### Task 3: Priority scheduling, snapshot reuse, and shared watchers

**Files:**
- Modify: `native/WinForge.Shell/FolderSizeEngine.h`
- Modify: `native/WinForge.Shell/FolderSizeTap.cpp`
- Modify: `native/WinForge.Shell/FolderSizeFixture.cpp`
- Modify: `tests/WinForge.FolderSizeTests/Program.cs`

**Interfaces:**
- `FolderSizeEngine::Query(path, sizeSort)` marks queued Size-sort work for priority scheduling.
- Completed directory snapshots are shared by cached descendants; a known descendant query returns immediately.
- `DirectoryWatch::ReadChanges()` returns normalized changed paths or an overflow indication. Related cached entries share recursive watchers.

- [x] Add owned-view checks for priority starts, descendant cache reuse, and watcher deduplication; run the fixture and verify they fail.
- [x] Order pending scans by Size-sort priority then most recent request without changing the active-scan bound.
- [x] Reuse ancestor snapshots for descendant queries.
- [x] Share/re-root recursive watchers for overlapping sibling and nested entries; update watcher counters.
- [x] Parse change records, filter them to affected entries, coalesce repeated parent paths, and handle overflow as invalidation.
- [x] Run scanner and owned-fixture checks.

### Task 4: Incremental subtree reconciliation and documentation

**Files:**
- Modify: `native/WinForge.Shell/FolderSizeEngine.h`
- Modify: `native/WinForge.Shell/FolderSizeScanner.h`
- Modify: `tests/WinForge.FolderSizeTests/Program.cs`
- Modify: `README.md`

**Interfaces:**
- A coalesced change request scans the changed path's containing directory subtree.
- A complete result replaces that subtree in each affected shared snapshot and propagates the exact byte delta to cached ancestors and descendants.
- Missing/incomplete snapshots or failed reconciliation mark affected entries stale and schedule a bounded full scan.

- [x] Require nested-file modification to increase the incremental-update counter without increasing the full-scan counter; verify the current implementation fails.
- [x] Replace the affected snapshot subtree and propagate deltas with overflow checks.
- [x] Fall back safely for missing snapshots, scan failures, buffer overflow, and directory rename/delete.
- [x] Verify modification and directory rename/delete update totals correctly in the owned fixture.
- [x] Document snapshot sharing, incremental subtree updates, bounds, and fallback in the Turkish README section.
- [x] Run `scripts/build-native.ps1`, build the focused project, run standard and owned-fixture modes, and run repository verification (the full script stopped at an unrelated FileTools offline-model output assertion after 1,396 regression checks passed; focused folder-size verification passed separately).
