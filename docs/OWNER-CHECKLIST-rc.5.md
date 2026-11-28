# Owner test checklist: SnapForge 1.0.0-rc.5 (build 187, unsigned test build)

Windows CI already ran the full regression and the display-scaling audit on the installed app; see [QA-COVERAGE-rc.5.md](QA-COVERAGE-rc.5.md). **You do not need to QA the whole app.** This list covers only what needs your hardware: about 15 minutes, plus 10 more if you have a second monitor.

**Download:** use the **Download Windows test build** button on https://bazeocrisy.github.io/SnapForge/ (this needs a GitHub sign-in). Unzip the package to get `SnapForge-1.0.0-rc.5-win-x64-setup.exe`.

**Check the file first.** In PowerShell, run:

```
Get-FileHash .\SnapForge-1.0.0-rc.5-win-x64-setup.exe
```

It must show `2f2a90fd7e965ed1c42bddd40b6563e1c2743acad13e90411d44bd1f97058ea8`. Do not run a file whose checksum differs. Windows will then show SmartScreen's blue "Windows protected your PC" window. That is expected for this unsigned test build (REL-SIGN-01).

## 1. Upgrade from rc.4 with both apps open

1. [ ] Leave **SnapForge Capture** and **SnapForge Editor** open, then run the rc.5 Setup.
2. [ ] Setup closes both apps by itself and finishes.
3. [ ] Your captures, recordings, settings and Library location are still there.
4. [ ] About shows **1.0.0-rc.5 (build 187)** in both apps.

## 2. Readability at your usual scaling

5. [ ] Look at the **Editor** toolbar, the **Capture panel**, **QuickDock**, **Settings** and the **Library**. All text is comfortable to read and nothing is cut off.
6. [ ] Open **QuickDock → Record → More options**. The panel fits on your screen, the options scroll, and **Start recording** is visible.
7. [ ] Glance at **Help → Keyboard shortcuts** and the **tray icon's right-click menu**. Windows draws these, so CI does not check them.

## 3. Hardware (CI has none)

8. [ ] In the Record panel, turn **Camera** and **Mic** on. You see yourself and the level bar moves. Record about 20 seconds, then check the picture, the sound and the camera position.
9. [ ] Drag a capture from the Editor into a **File Explorer** folder with the mouse.

## 4. Optional: if you have a high-DPI or second monitor

10. [ ] **200 %:** CI's virtual screen stops at 175 %. On a 4K or high-DPI screen set to 200 %, repeat step 5 and take one region capture. Check that the size label and the magnifier are readable.
11. [ ] **Mixed scaling:** with two monitors at different scaling, drag the Editor, Settings and the Record panel between them. They stay sharp and fully on screen.

Report anything that fails, ideally with a photo, and which step it was.
