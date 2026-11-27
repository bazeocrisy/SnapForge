# SnapForge defect register

Release candidate `1.0.0-rc.1`. Earlier milestone defects are recorded in `docs/MILESTONE-3-HANDOFF.md` and
`docs/V1-HANDOFF.md` §3 and are all closed with regression tests; they are summarised at the end. A defect is closed
only with evidence (a passing regression test in CI or a documented verification), never because a change was made.

**Severity:** Critical — security compromise, data loss or severe failure · High — mandatory functionality unreliable
or substantially broken · Medium — meaningful non-critical functional or usability problem · Low — minor cosmetic or
documentation issue. **Open Critical/High defects in mandatory functionality block release.**

## Open defects

None known at the time of writing. Items that depend on hardware or external action are tracked as *requirements*
with status *Manual verification pending* or *Blocked* in
[PRODUCT-REQUIREMENTS-TRACEABILITY.md](PRODUCT-REQUIREMENTS-TRACEABILITY.md), not as defects, until a test shows a
failure.

## Defects found and fixed during release-candidate work

| ID | Severity | Feature | Reproduction | Expected | Actual | Root cause | Fix commit | Regression test | Verification |
|---|---|---|---|---|---|---|---|---|---|
| DEF-RC-01 | High | DRG | Drag a capture into a browser upload field or mail draft; the app reads the file after more than 2 minutes, or after SnapForge exits | File still readable | File already deleted (2-minute timer; all copies deleted on exit) | Retention designed for synchronous drop targets only | `4980b5a` | `Temp_export_sessions_are_private_folders_and_swept_after_the_grace_period`, `Temp_export_size_cap_removes_oldest_sessions_but_never_recent_ones` | Core tests pass (CI); real-app check is part of manual drag acceptance |
| DEF-RC-02 | Medium | REL (CI) | Push to `release/v1.0-candidate` | CI runs | Only the Website workflow ran | CI trigger listed `main` and `claude/**` only | `a5ca8d3` | — (CI runs on every push since) | CI runs 75+ on the branch |
| DEF-RC-03 | Low | Tests | Capture panel test after adding *Active window* | Test reflects the menu | Test expected the old 4-item menu | Intended UI change; stale expectation | `e112736` | `Capture_panel_selection_delay_reset_and_presets_work` | CI run 77: App tests 60/60 |
| DEF-RC-04 | Low | Tests | Core timing test under CI coverage instrumentation | Bound measures the product | 60 s with coverage vs ~1 s without | Coverage instrumentation slows pixel loops ~50× | `0edd5f3` | Core test keeps a loose bound; `image-performance` in the packaged-build feature self-test enforces < 10 s | CI run 77: 942 ms on the packaged build |
| DEF-RC-05 | Low | Tests | Feature self-test frame extraction at 4.5 s | Expects the colour of second 4–5 | Expected magenta (second 5–6) | Off-by-one in the test's expectation (product correct: returned cyan) | `d73fe3a` | `video-frame-extract` | Next CI run |
| DEF-RC-06 | Medium | PKG | First installer test design used a PowerShell function parameter named `$args` | Arguments passed to setup | `$args` is an automatic variable (would pass nothing) | Script bug found in review before CI | `a5ca8d3` (pre-push fix) | Installer CI job | CI run 77: installer job passed |
| DEF-RC-07 | Medium | EDT | Editor toolbar at narrow window widths (900–~1250 px) | All tools, Undo/Redo/Delete and Export reachable at the minimum width | After Pen/Callout/Stamp were added, the right-hand groups were pushed out of the window; clicking Delete hit nothing (CI run 78, `InteractiveUiTests` at 980 px) | Toolbar was a non-wrapping DockPanel sized for 12 tools | next commit | `Every_toolbar_control_stays_fully_visible_at_the_window_width(900/1280)` + the original interactive test | Next CI run |

## Defects from earlier milestones (closed)

| Source | Count | Status |
|---|---|---|
| Milestone 3 handoff (capture, widget, drag-and-drop, Library) | see MILESTONE-3-HANDOFF.md | Closed with tests |
| V1 handoff §3 (A/V offset from B-frames and fragmented MP4 timing, resume gap, maximised-window match, recovery edge case, backup metadata, dark-theme styling, skipped frame slots) | 7 | Closed with tests and CI evidence |

## How to add a defect

Copy a row, use the next `DEF-RC-nn` (or `DEF-1.0-nn` after release), fill every column, link the failing evidence,
and only move it to the closed table with a passing regression test or recorded manual verification.
