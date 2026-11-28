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
| DEF-RC-05 | Low | Tests | Feature self-test frame extraction at 4.5 s | Expects the colour of second 4–5 | Expected magenta (second 5–6) | Off-by-one in the test's expectation (product correct: returned cyan) | `d73fe3a` | `video-frame-extract` | CI runs 82, 83: pass |
| DEF-RC-06 | Medium | PKG | First installer test design used a PowerShell function parameter named `$args` | Arguments passed to setup | `$args` is an automatic variable (would pass nothing) | Script bug found in review before CI | `a5ca8d3` (pre-push fix) | Installer CI job | CI run 77: installer job passed |
| DEF-RC-07 | Medium | EDT | Editor toolbar at narrow window widths (900–~1250 px) | All tools, Undo/Redo/Delete and Export reachable at the minimum width | After Pen/Callout/Stamp were added, the right-hand groups were pushed out of the window; clicking Delete hit nothing (CI run 78, `InteractiveUiTests` at 980 px) | Toolbar was a non-wrapping DockPanel sized for 12 tools | `a6e399f`; test window width adjusted in the next commit (the wrapped toolbar is taller at 980 px, so the test image no longer showed at 1:1) | `Every_toolbar_control_stays_fully_visible_at_the_window_width(900/1280)` + the original interactive test | CI runs 82, 83: pass; 1920-px screenshot shows one toolbar row |
| DEF-RC-08 | Low | Tests | GIF encoder timing unit test under CI coverage instrumentation | 30 frames 640×360 < 3 s | 3.19 s in CI run 79 (coverage on) | Same cause as DEF-RC-04 | next commit | Core test bound 30 s; new `gif-encoder-performance` feature self-test enforces < 3 s on the packaged build | CI runs 82, 83: Core tests pass; self-test 282 / 295 ms |
| DEF-RC-09 | Medium | ACC | Dialog buttons with access keys (e.g. OCR window "_Copy text", scrolling capture "_Auto-scroll") | Label shows "Copy text" with Alt+C | Literal underscore shown, no Alt shortcut (found in the CI run 82 website screenshot) | Button templates' ContentPresenter lacked RecognizesAccessKey | `748eedb` | CI screenshot of the OCR window (visual); MA-ACC-01 keyboard check | CI run 83 screenshot shows "Copy text"; keyboard check pending (MA-ACC-01) |

## rc.2 — forensic audit findings and owner observations

The 24 audit findings (FA-001 … FA-024), the owner's observations (SF-001 … SF-015) and QA items are tracked one by
one, with the change, tests, reproduction class and status, in [FORENSIC-REMEDIATION.md](FORENSIC-REMEDIATION.md).

## rc.2 — defects found while integrating and testing

| ID | Severity | Area | Symptom | Cause | Fix | Evidence |
|---|---|---|---|---|---|---|
| DEF-RC2-01 | Medium | FA-003 | Work-area guard did not move test windows (CI runs 89, 90) | The `Loaded` class handler was not raised for plain `Window` instances in the test host; WPF also re-applied its own size | Guard also attaches through a 500 ms scan of the app's windows and drives WPF's own Left/Top/Width/Height (DIPs) before falling back to `SetWindowPos`; tests attach explicitly so other UI tests keep their placement | `WindowingTests` on the final CI run |
| DEF-RC2-02 | Low | FA-009 | Drag test found 2 files in the session folder | New active/dropped retention markers live in the session folder | Test ignores SnapForge's own markers and asserts they are never offered to the drop target | `Drag_data_offers_only_flattened_redacted_images…` |
| DEF-RC2-03 | Low | SF-005 | Redesigned capture panel 384 DIP tall vs. an estimated ≤ 380 | Estimate made without Windows rendering | Bound set to ≤ 420 DIP (still compact) | `Capture_panel_is_compact_and_marks_unavailable_modes` |
| DEF-RC2-04 | High | FA-007 | `MoveTo` no longer deletes the source (by design), so the old App flow would have left two Libraries silently | Core API change | New `RelocateLibraryAsync` flow with explicit, confirmed, verified cleanup | `FA007_…` App tests |
| DEF-RC2-05 | Medium | SF-005 | "Active window" chip label cut off ("Active windo") in the rc.2 screenshot | Capture-type column fixed at 236 DIP; widening the window did not help | Column 272 DIP (Image and Video tabs) | `Capture_panel_is_compact…` now measures every chip label (CI run 119 caught 119 > 103 DIP) |
| DEF-RC2-06 | Medium | Tests (DnD) | `Real_mouse_drag_of_a_library_multi_selection…` failed intermittently (CI runs 111, 119, 125, 129; passed 109, 113, 123, 127) | **Test-harness defect, not an application or OLE defect.** The drop target was placed 250 px right of the first tile; on the 1024×768 CI display it spanned x = 975–1195, so its centre (1093) was off screen. Windows clamps the pointer to x = 1023; there `WindowFromPoint` found the runner's console window (`CASCADIA_HOSTING_WINDOW_CLASS`), so OLE sent DragLeave to the target (after it had answered Copy) and no Drop — depending on where that console window lay on each runner. Ruled out with logged evidence (run 129): lazy file creation (never reached), CF_HDROP payload, target's DragOver answer (Copy), selection (3 items at drag start), tooltips/popups (none opened, also with a 1.5 s pause). A tooltip-suppression change made on that hypothesis was withdrawn unshipped. | `PlaceTarget` keeps every drop target inside the monitor work area; `SlowDrag` refuses off-screen gesture points; `AssertTargetIsUnder` checks that Windows finds the target at the drop and release points | `DEF_RC2_06_library_drags_with_a_pause_over_the_target_deliver_every_file_repeatedly` (1 and 3 items × 3 drags each, pause + adjustment) and the original multi-selection test |
| DEF-RC2-07 | Medium — **open (recurred in rc.3 CI run 155)** | Tests (screen-based self-tests) | `studio-screen-plus-camera-synthetic` / `studio-window-not-recorded` recorded none of the lime test background in CI runs 132, 133 and 138; `toolbar-not-recorded` showed the same symptom in run 141 (0 % even in its control run). Passed in all other runs since 113 | **Narrowed, not fully established.** Run 138: a direct GDI capture of the area just before recording showed 0 % lime — the *screen* did not show the test window (created on its own STA thread) although Windows reported it on top; recordings matched the screen, so the recording pipeline is faithful. Ruled out: a covering window (hit-test precondition); Studio blocking its UI thread (Loaded work 4–6 ms, camera listing 2–4 ms). Remaining: why a freshly shown test window was sometimes not painted on those runners | `a70ae88`, `616345d`, `6d46bd9`: both screen-based self-tests wait up to 5 s for their test window to be painted (14–30 ms measured, runs 139–144) and fail with the timing if it never is; new product check `studio-ui-not-blocked` (≤ 500 ms). No check loosened | Keep open until a failing occurrence with the new timings; MANUAL-ACCEPTANCE: Studio and toolbar never appear in recordings on a physical PC |

## Defects from earlier milestones (closed)

| Source | Count | Status |
|---|---|---|
| Milestone 3 handoff (capture, widget, drag-and-drop, Library) | see MILESTONE-3-HANDOFF.md | Closed with tests |
| V1 handoff §3 (A/V offset from B-frames and fragmented MP4 timing, resume gap, maximised-window match, recovery edge case, backup metadata, dark-theme styling, skipped frame slots) | 7 | Closed with tests and CI evidence |

## How to add a defect

Copy a row, use the next `DEF-RC-nn` (or `DEF-1.0-nn` after release), fill every column, link the failing evidence,
and only move it to the closed table with a passing regression test or recorded manual verification.

## rc.3 — defects found while integrating and testing

| ID | Severity | Area | Symptom | Cause | Fix | Evidence |
|---|---|---|---|---|---|---|
| DEF-RC3-01 | Low | Tests (installer CI) | rc.2 → rc.3 upgrade step stopped before upgrading (run 149) | Test harness: the runner profile had no `HKCU\…\CurrentVersion\Run` key, so seeding the rc.2 start-with-Windows entry failed | Create the key if missing (never `New-Item -Force`, which would erase existing values) | `dbbba29`; run 155 got past it |
| DEF-RC3-02 | Low | Tests (installer CI) | "Installed apps entry not upgraded in place" check crashed with `Cannot find path …\H` (run 155) | Test harness: a one-element PowerShell pipeline result is a string, so `$entries[0]` was its first character | Wrap the result in `@(...)` | `466b84b`; CI run 159 green |
| DEF-RC3-03 | Low | Tests (App) | 4 `StudioWindowTests` failed (run 155) | Intended UI change: the tests drove the rc.2 Studio form (Screen / Screen + camera / Camera segments, in-window preview) that the compact Record panel replaces | Tests rewritten for the Record panel with the same guarantees (camera never opened by opening the panel; Camera/Mic off each session; Camera on starts the live preview, off releases it; Start releases the camera and hands the preview over; failure explained, not retried; settings persist; every control named) plus new ones (live preview frames, caption names the corner) | `466b84b`; CI run 159 green |
| DEF-RC3-04 | Low | Tests (App, video quality) | `RC3_VQ_area_average_keeps_flat_colour_exact_mask_antialiased_and_mirror` failed (run 155) | Test expectation: on the circle's middle row the edge falls exactly on pixel boundaries, so no partially covered pixel exists there; the product mask is anti-aliased elsewhere | Check a row where the edge is diagonal | `466b84b`; CI run 159 green |
| DEF-RC3-05 | Low | Tests (App, video quality) | `RC3_VQ_quality_scene_is_deterministic…` failed (run 155) | Test expectation: two separately created scenes are not pixel-identical (WPF text rasterisation into a bitmap). The comparison builds both recordings and every reference from ONE scene instance, so its results are unaffected | Test checks the property the comparison relies on (one instance renders a frame identically every time) | `466b84b`; CI run 159 green |
| DEF-RC3-06 | Low — **open** | Docs (screenshot tour) | The documentation screenshot of the recording control strip shows only the background (runs 155 and 159), although the strip reports itself loaded, visible and 379×50 DIP | Not established. The product strip does paint: in run 159 the self-test's control pass (exclusion off) recorded the strip's pixels (0 % background where it was). Same family of symptom as DEF-RC2-07 (a freshly shown window not reaching the screen in the CI session) | None yet; no toolbar screenshot is claimed for rc.3 | Owner checklist item 5 (the strip is visible while recording on a physical PC) |

**DEF-RC2-07 update (runs 155, 159).** Run 159: all screen-based checks passed (`toolbar-not-recorded` test window painted after 18 / 16 ms, Studio 44 ms). `toolbar-not-recorded`: in the excluded pass the test window was **not painted within 5 s**
(the control pass painted in 16 ms), so the check failed on its precondition. The recorder, Studio/Record panel and
camera-preview checks in the same run passed (test window painted in 33 ms). This is the failing occurrence with the
new timings the entry was waiting for: a freshly shown test window on its own STA thread sometimes never reaches the
screen on the runner. Next diagnostic step (added, no check loosened): when the window is not painted, the failure now
records DWM cloaking, visibility, minimised and foreground state and the colour on screen. Stays **open**.

## rc.4

| ID | Severity | Area | Symptom | Cause | Status / fix | Evidence |
|---|---|---|---|---|---|---|
| DEF-RC3-07 | Medium | Installer (signing) | Owner sees a blue screen when testing RC3 | **Identified (owner, 28 Nov 2026): the Microsoft Defender SmartScreen window "Windows protected your PC"** shown when launching the **unsigned** test installer. Not a crash, stop error or app/recording defect | **Closed as identified; moved to release task REL-SIGN-01 (trusted code signing, open)**. Test builds are labelled "Not code-signed" on the website and in the PR; no bypass instructions are given; no self-signed substitute | `docs/BLUE-SCREEN-TRIAGE.md`; `Get-AuthenticodeSignature` → NotSigned for every CI build |
| REL-SIGN-01 | High for public release (not a test-build blocker) | Release | SmartScreen warns on every unsigned SnapForge installer | No trusted code-signing certificate yet | **Open — owner decision**: choose SignPath Foundation (free OSS, application) / Microsoft Artifact Signing (paid, identity verification) / OV certificate; then sign both programs and the installer in CI with timestamping and record the publisher on the website. See `docs/SIGNING.md` | — |
| DEF-RC4-01 | Medium | Installer | Reinstalling RC3 while SnapForge was open stopped and asked the owner to exit SnapForge manually | RC3 Setup used Inno Setup's AppMutex check, which can only refuse | Setup now closes SnapForge gracefully (see `docs/INSTALLATION.md`) | pending CI (installer job) |
| DEF-RC4-02 | Low | Installer build | rc.4 installer did not compile in CI (runs 163, 165): the CI's Inno Setup rejects the wizard progress-page types used for a "Closing SnapForge" page | Compiler/script incompatibility | Page removed; Setup waits on its standard "Preparing to install" page | `ff8bb06`; pending CI |

