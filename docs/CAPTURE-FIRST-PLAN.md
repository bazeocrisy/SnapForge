# Milestone 3.1 — "Capture-first": one program that behaves like two (proposed)

Status (2026-11-26): after discussion the owner agreed to build only the **small items** now — CF-07 (icon click opens
the capture panel), CP-01 (panel at start, default on), CP-03 (panel always-on-top option), CP-04 (show/hide panel
hotkey, unassigned by default). **Implemented.** The larger items (CF-01…CF-06, CP-02) are **on hold** in the
roadmap until there is a concrete need (e.g. a heavy video editor).

## Background

Snagit installs as two programs: a background **capture** program (hotkeys, capture window, widget, starts with
Windows) and an **editor** that opens when needed (DOC, search extract: TechSmith deployment guide mentions
`SnagitCapture.exe`, S-CAP-22/23). SnapForge is one process. Option A keeps one process — one build to sign, test and
update, one writer to the Library — but gives the user the two-program experience.

## Requirements

| ID | Requirement | Acceptance (summary) | Verification |
|---|---|---|---|
| CF-01 | **Capture-first start-up**: on launch (normal or with Windows) SnapForge shows only the notification-area icon, the widget and — if enabled — the capture panel. The Editor/Library window is **not created** at start. | After start, no Editor window exists; process memory lower than today (measured) | UI test + benchmark |
| CF-02 | **Editor on demand**: the Editor/Library window is created the first time it is needed — a capture with Preview in Editor, Open Editor / Open Library (panel, widget, notification menu), a tray/Library action, or a dropped image | Each entry point opens the Editor; capture with Preview off never creates it | UI tests |
| CF-03 | **Closing the Editor really closes it**: unsaved edits are saved first (as today; captures without history still ask), editor documents are released, the window is destroyed; capture keeps working from hotkeys, panel and widget | After closing, hotkey/panel/widget captures still work and save to the Library; memory drops back | UI test + measurement |
| CF-04 | **Captures without the Editor**: with Preview in Editor off, captures go to the Library and/or clipboard without creating the Editor | No Editor window after such captures; captures in Library/clipboard | UI test |
| CF-05 | **Two taskbar identities**: the capture panel and the Editor appear as separate taskbar items ("SnapForge Capture", "SnapForge Editor") with distinct icons, so they look and pin like two apps | Different AppUserModelIDs and icons per window | Test of window properties + manual check |
| CF-06 | **Fault isolation**: an unexpected error in the Editor is logged and reported, the Editor can be reopened, and capture keeps working | Injected Editor error leaves capture functional | UI test |
| CF-07 | **Notification icon**: left-click opens the capture panel (today: the Editor); the right-click menu keeps Capture, Capture panel, Show capture widget, Open Editor, Open Library, Settings, Exit | Click behaviour tested | UI test |
| CP-01 | **Capture panel at start** by default (setting "Show the capture panel when SnapForge starts", default **on**) | Fresh settings show the panel at start; setting off hides it | Test |
| CP-02 | **Slimmer panel**: File/Edit/View/Help move into the title bar (custom title bar keeping minimise/close, window snapping, dark/light/high-contrast, keyboard access with Alt) | Panel ≈30 px shorter; menus reachable with Alt | UI test + screenshot |
| CP-03 | **Always on top** option for the capture panel (default off) | Toggle persists and applies | Test |
| CP-04 | **Show/hide capture panel hotkey** (default: none assigned, to avoid conflicts; set in Settings → Hotkeys; conflicts reported) | Hotkey toggles the panel | Test |

Preserved (regression): every milestone-1–3 feature, Library format, settings (additive only), single-instance
check, drag and drop, widget, crash-safe saving. No video work, no paid dependencies, no merge or release.

## How it will be built (summary)

* `App` owns capture-side services (hotkeys, notification icon, widget, panel, capture controller) independently of
  the Editor window; the Editor window becomes a lazily created, disposable object (`EditorHost`).
* Capture results are routed through a small "capture intake" service: Library save / clipboard always; Editor only
  when Preview in Editor is on.
* Separate taskbar identity via per-window `System.AppUserModel.ID` (Windows property store) and window icons.
* Custom title bar for the panel using WPF `WindowChrome` (keeps native snap/resize behaviour).

## Risks

* Much code assumes the main window always exists (`App.MainShell`); each use must be rerouted. Mitigation: the full
  existing test suite plus new tests for every entry point.
* Taskbar grouping by AppUserModelID can behave differently for pinned shortcuts; verified manually.
* Custom title bars can hurt accessibility if done badly; WPF `WindowChrome` with standard caption buttons and Alt
  menu access keeps it accessible.

## Estimate

Medium (smaller than milestone 3). Deliverables: code, tests, updated docs and acceptance checklist, screenshots,
a new test build, and before/after memory numbers.

## Owner decisions needed

1. Approve CF-01…CF-07 and CP-01…CP-04 as written?
2. Capture panel shown at start **on** by default (CP-01) — yes/no?
3. Notification-icon left-click opens the **capture panel** (CF-07) — or keep opening the Editor?
4. A default show/hide-panel hotkey, or leave unassigned (recommended)?
