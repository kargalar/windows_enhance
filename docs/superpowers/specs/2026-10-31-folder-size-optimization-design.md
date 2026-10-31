# Explorer Folder Size Optimization Design

## Intent

Make folder sizes appear sooner in Explorer and reduce repeat filesystem work while keeping displayed totals exact. The feature remains an opt-in WinForge Explorer enhancement for local fixed drives.

## Goals

- Start scans requested by a Size sort before ordinary visible-size requests, then favor the most recently requested folders.
- Reuse a completed parent scan for requested descendant folders.
- Share recursive directory watchers across cached sibling or nested folders where one watcher covers them.
- Use change paths to rescan only the smallest affected directory subtree and apply its byte delta to cached ancestors. Fall back to a full scan when the snapshot is unavailable, incomplete, or a change cannot be reconciled safely.
- Expose bounded diagnostics for scan work, priority scheduling, snapshot reuse, incremental updates, and watcher load.

## Non-goals

- Follow reparse points, scan network/removable drives, or display partial totals.
- Keep folder-size results on disk across Explorer sessions.
- Maintain per-file metadata for every file or depend on the NTFS USN journal.
- Increase scan concurrency without measurements.

## Constraints

- Preserve the current callback scope, background thread, low I/O thread priority, 25 ms scanner slices, and existing scan/cache time limits.
- Keep incomplete or unreadable totals blank.
- Bound directory snapshots and watcher buffers; use the existing full-scan path if an optimization cannot preserve correctness.
- Update the shared-memory protocol version and both native and managed readers together.
- Preserve the existing user modification in `todo.md`.

## Approaches

1. **Bounded directory snapshots with subtree reconciliation (selected).** Record totals and direct-file totals per directory up to explicit memory limits. Reuse this snapshot for descendant queries. For a change notification, rescan its containing directory subtree, replace that subtree in each affected shared snapshot, and propagate the byte delta to cached ancestors. This avoids per-file state and retains an exact full-scan fallback.
2. **Per-file size manifests or volume journal deltas.** These can update individual file changes without enumerating their parent directory, but require more persistent per-file state and substantially more rename, journal-wrap, and memory handling.
3. **Keep full rescans and only tune queue/cache settings.** This is simpler, but leaves duplicate subtree work and repeated whole-tree scans after common file changes.

Approach 1 is chosen because it captures the repeat-work and event-locality gains while keeping storage bounded and preserving exact results through a full-scan fallback.

## Implementation decisions

- A scanner snapshot contains each visited directory's total bytes and direct-file bytes. It is discarded if its directory-count or path-storage limit is exceeded.
- Snapshot trees are shared by cached entries. A descendant query can return a cached descendant total immediately.
- A watcher is rooted at a cached directory; sibling/ancestor relationships can promote or reuse a shared recursive watcher. Events are filtered against the cache entry paths they can affect.
- Duplicate changes to the same parent directory are coalesced. A complete subtree rescan updates the snapshot and all cached entry totals that reference it. Overflow, an incomplete subtree scan, or missing snapshot data schedules safe full rescans.
- Add protocol counters for priority scan starts, descendant cache reuse, incremental updates, full scans, watcher events/overflows, active watcher count, and elapsed scan microseconds.

## Verification

- Native scanner checks verify exact root and descendant totals and correct behavior for empty, missing, linked, and large files.
- The owned Explorer view checks priority scheduling, descendant snapshot reuse, shared watcher count, and that a nested-file change updates displayed totals through an incremental subtree scan without increasing the full-scan count.
- Build the focused native and managed folder-size components and run the repository folder-size verification.
