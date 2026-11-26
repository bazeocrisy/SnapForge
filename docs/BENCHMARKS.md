# Library scale benchmarks (milestone 3)

**Evidence class: CI** (GitHub-hosted `windows-latest` runner, Windows 10.0.26100 Server, 4 vCPU, .NET 10.0.15),
run 45722027176, commit `ddabbbc`, job "Library scale benchmarks (production durability)", artifact
`benchmarks-ddabbbc…` (11 JSON/MD files). Linux numbers below are from a local run in the development container.
These are **not** measurements on a user's PC; a real laptop disk (or a OneDrive-synced folder) can be slower.

## Method

* One library grows 100 → 1 000 → 5  000 → 10 000 → 25 000 captures. Every capture is written by the production code
  (`CaptureLibrary.Create`) with **production durability**: each file goes to a temp file, `FileStream.Flush(true)`
  (FlushFileBuffers), then an atomic rename; the manifest is written last. **No test-only bypass is used anywhere in
  the benchmarks** (the `SkipDiskFlush` hook is internal to the test assemblies and not reachable from the tool).
* Images are 800×500 synthetic UI-like screenshots (flat panels, text-like stripes) with two annotations each, so
  compression behaves like real screenshots (≈42 KB per capture on disk).
* After each size: core measurements (`tools/SnapForge.Benchmarks`) and then the real app is started over that
  library with `SnapForge.exe --benchmark-startup` (UI measurements: window, index, freezes, Library grid, search,
  memory). A size would be skipped only if seeding could not fit a 120-minute budget — **none was skipped**.
* "Longest UI freeze" is the longest gap between 15 ms input-priority ticks on the UI thread from the moment the
  window is shown until the index is complete and the Library grid and a search have been shown. It therefore
  includes the deliberate UI work of building the Library grid and running the search, not only indexing.

## Results — Windows (CI)

| Captures | Window shown (ms from process start) | Index complete in background (ms after start-up) | Longest UI freeze (ms) | Library grid shown (ms) | UI search (ms) | Core search (ms) | Thumbnail decode (ms each) | Autosave median / p95 (ms) | New capture save (ms) | Working set (MB) | Disk (MB) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 100 | 1 098 | 765 | 175 | 213 | 41 | 0.09 | 0.17 | 4.1 / 5.2 | 7.4 | 225 | 4 |
| 1 000 | 935 | 838 | 164 | 197 | 107 | 0.78 | 0.19 | 3.7 / 4.5 | 6.1 | 233 | 42 |
| 5 000 | 968 | 1 909 | 186 | 223 | 105 | 3.7 | 0.17 | 3.6 / 4.1 | 6.0 | 240 | 212 |
| 10 000 | 933 | 3 066 | 215 | 254 | 116 | 7.5 | 0.25 | 3.7 / 4.4 | 6.2 | 245 | 423 |
| 25 000 | 986 | 6 201 | 261 | 292 | 135 | 18.6 | 0.16 | 3.6 / 4.3 | 6.4 | 257 | 1 058 |

Core index alone (no UI, `CompleteOpen` on a warm cache): 23 ms (100), 190 ms (1 000), 971 ms (5 000), 1 943 ms
(10 000), 4 967 ms (25 000); index memory 7.4 MB at 25 000. Seeding 25 000 captures took 112 s of the 2.1-minute run.

## Results — Linux (development container, core only)

Ubuntu 24.04, 4 vCPU, warm page cache: index 11 / 50 / 251 / 533 / 1 349 ms for 100 / 1 000 / 5 000 / 10 000 /
25 000; autosave median 4.4 ms at 25 000; search 5 ms at 25 000.

## Findings

1. **Start-up no longer depends on library size** (DEF-02 fixed): the window appears in ≈0.9–1.1 s from process start
   at every size; before milestone 3 the library was indexed on the UI thread before the window was created.
2. **Indexing is linear** (≈0.25 ms per capture on this runner) and runs in the background; at 25 000 captures the
   Library shows "Loading the library…" for ≈6 s after start-up. A persisted index would remove this; not needed
   for M3, proposed as a later improvement.
3. **The UI stays responsive** but not perfectly smooth: the longest single UI-thread pause grows from 175 ms (100) to
   261 ms (25 000). It comes from building the Library grid and the search refresh on the UI thread, which scale
   with the number of captures. Above ≈100 ms a user can perceive a short hitch when opening the Library or typing a
   search in a 10 000+ library. Recorded as known limitation KL-PERF-1.
4. **UI search** (SetSearch → visible grid) is 104–135 ms from 5 000 captures; the underlying query is 4–19 ms, so
   the cost is the grid rebuild. Target PERF-04 (< 100 ms core search at 10 000) is met (7.5 ms); the UI path is not
   under 100 ms above 1 000 captures.
5. **Autosave and capture saves do not slow down** with library size (≈3.6 ms / ≈6 ms) with production flushing.
   Caveat: cloud VM disks acknowledge flushes quickly; a consumer SSD or HDD will be slower in absolute terms.
6. **Memory** grows modestly (225 → 257 MB working set for the full app; ≈7 MB of index data at 25 000).

## Reproduce

```
dotnet build tools/SnapForge.Benchmarks -c Release
tools/SnapForge.Benchmarks/bin/Release/net10.0/SnapForge.Benchmarks --root <empty folder> --sizes 100,1000,5000,10000,25000 --keep --out core.json
SnapForge.exe --benchmark-startup <same folder> ui.json
```
Use an empty folder: the tool adds captures to whatever library it is pointed at. Never point it at a real library.
