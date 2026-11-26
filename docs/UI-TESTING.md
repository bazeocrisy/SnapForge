# Automated UI testing — investigation and current approach

## Options considered

| Option | Fit for SnapForge | Notes |
|--------|------------------|-------|
| **Direct `SendInput` + in-process assertions (chosen)** | Drags on custom-drawn surfaces (overlay, editor canvas) are exactly what must be tested; assertions read the real document model | No new dependencies; tests run inside the test process on a shared WPF UI thread, so they can inspect `EditorDocument` directly |
| UI Automation via FlaUI (MIT) | Good for buttons, menus and text fields exposed through UI Automation | The overlay and editor canvas are custom-drawn and expose no meaningful automation tree; drags would still be raw mouse input. Adds packages. Worth adding later for dialogs (Save dialog, message boxes) |
| WinAppDriver / Appium Windows driver | Out-of-process black-box testing | WinAppDriver requires developer mode and a separate service; its maintenance status should be checked before adoption (not verified here). Heavier CI setup |
| Image-based (screenshot comparison) | Visual regressions | Brittle across fonts/DPI; useful later for rendering snapshots |

## What is automated now (`tests/SnapForge.App.Tests/InteractiveUiTests.cs`)

Real mouse input through the Windows input stack on the CI runner's interactive desktop:

* Region selection: drag completes with the exact rectangle (end-to-end regression for the MVP defect),
  reverse-direction drag, click-without-drag keeps the overlay open, right-click cancels.
* Window selection: click returns the front-most window rectangle (synthetic window list).
* Display selection: click returns the monitor bounds.
* Annotation manipulation: tool buttons clicked with the mouse; draw rectangle; select and move it; Undo/Redo
  buttons; arrow; two numbered steps (1, 2); redaction drag covers pixels and earlier annotations; select and
  delete via the Delete button.

The deterministic selection logic is additionally unit-tested in `SelectionTrackerTests` (including the exact
event order that caused the defect: capture lost before button-up).

## Limits (why manual acceptance testing is still required)

* The CI runner has one monitor at 100 % scale: **mixed-DPI and multi-monitor behaviour is not covered**.
  (Possible future: virtual display drivers on a self-hosted runner.)
* Keyboard shortcuts and text typing are not injected: Windows only delivers keyboard input to the foreground
  window, which a test process cannot reliably obtain. Text entry and shortcuts are manual-test items.
* The Save dialog, overwrite prompts and real pasting into other apps are not automated.
* Tests depend on an unlocked interactive desktop; they will fail (not silently pass) without one.
