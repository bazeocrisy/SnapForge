# Final product inventory (repository forensic discovery)

Baseline established 27 Nov 2026 before release-candidate work.

## Branches and pull requests

| Branch | Head at discovery | Content | Disposition |
|---|---|---|---|
| `main` | `fd18847` | Empty initial commit only | Untouched (merge of the RC needs owner approval) |
| `claude/pensive-euler-ulw6m4` (PR #1) | `0b42260` | Milestone 3: capture panel, widget, drag-and-drop, Library, editor MVP | Fully contained in `claude/video-v1` (no commits missing) — preserved |
| `claude/video-v1` | `33f7a53` | M3 + V1 screen recording (35 commits) | Base of the RC — preserved |
| `claude/website` (PR #2) | `0462dee` | Website sources, release verifier, publishing | Merged into the RC (website/ only) — preserved |
| `gh-pages` | `155879a` | Built website | Publishing target; updated only with validated output |
| `release/v1.0-candidate` | created from `claude/video-v1` + merge of `claude/website` (`fc0a2e9`) | 1.0 integration | New; integration PR to `main` for review (not merged) |

## Functionality at baseline

| Area | State at baseline |
|---|---|
| Screenshot capture (region/window/display/full screen, multi-monitor, delay, cursor, presets, hotkeys) | Complete, CI-tested |
| Capture panel, desktop widget, tray, notification area | Complete, CI-tested |
| Editor: arrow, line, rectangle, ellipse, highlight, text, steps, redact, crop, quick styles, undo | Complete |
| Redaction + sanitization | Complete with tests |
| Library (search, tags, folders, favourites, trash, backup/restore, retention, location) | Complete |
| Native drag-and-drop (images + MP4) | Complete; 2-minute temp deletion (defect DEF-RC-01) |
| Screen recording (MP4, audio, toolbar, recovery, CFR) | Complete; physical-PC tests pending |
| Website + verifier + gh-pages | Complete, not yet enabled |
| Missing vs 1.0 directive | Active window, repeat last, clipboard capture, magnifier, scrolling capture, OCR, rotate/flip/resize/canvas, freehand, callouts, stamps, blur/pixelate, borders/shadows, watermark, combine, print, copy/paste annotations, BMP/TIFF, video editing (trim/cut/frames/GIF), video import, first-launch welcome, settings reset, version metadata/About, official icon, installer, SBOM, signing preparation, 1.0 documentation, hi-res website gallery |

## Duplicates, conflicts, technical debt found

- Two acceptance documents (M3 `ACCEPTANCE-TESTING.md`, V1 `VIDEO-ACCEPTANCE.md`) — kept; 1.0 adds
  `MANUAL-ACCEPTANCE-1.0.md` that references both.
- README described Windows 10 and 11 and a "working name"; owner decisions (Windows 11 x64, SnapForge official)
  supersede — README updated in the RC.
- Website screenshots were 720-px JPEG previews (blurry) — replaced with full-resolution PNGs from the packaged build.
- CI did not run on `release/**` (DEF-RC-02).
- Version `0.1.0-dev` with no build number/channel — replaced by 1.0.0-rc.1 metadata.

## Dependencies and licensing concerns

No third-party runtime packages; .NET and WPF runtimes (MIT) bundled; Windows codecs/OCR used, not shipped. Open:
H.264/AAC distribution question, name clearance, signing — see LICENSING-REVIEW.md and SIGNING.md.

## Unverified claims at baseline

Physical-PC behaviour (audio, GPUs, multi-monitor, HDR, real drag targets, accessibility with screen readers) — still
pending and tracked in the traceability matrix.
