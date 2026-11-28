# Owner test checklist: SnapForge 1.0.0-rc.3 (unsigned test build)

Install with `SnapForge-1.0.0-rc.3-win-x64-setup.exe`.

The build is **unsigned**, so Windows SmartScreen may warn about it. Choose **More info → Run anyway** only if the file's SHA-256 matches the one in PR #3.

Installing over rc.2 keeps your captures and settings. If SnapForge is running, setup asks you to stop any recording and exit it first.

Tick each box; if something is wrong, write down what you saw.

## 1. Main walkthrough: recording

1. [ ] Close SnapForge Editor if it is open. Open **QuickDock** (or the SnapForge Capture icon next to the clock) and choose **Record**. A small **Record** panel opens.
2. [ ] Pick **Screen**, **Window** or **Area**. Turn **Camera** and **Mic** on (both start Off each time SnapForge starts).
3. [ ] **You can see yourself.** A floating camera preview appears near the bottom-right of the screen. You can drag it anywhere, and it stays on screen. Its caption says where you will appear in the recording.
4. [ ] **The mic hears you.** Talk. The **Mic level** bar in the panel moves.
5. [ ] Press **Start recording**, choose what to record, then talk for 20–30 seconds. Move the camera preview over the recorded area at least once.
   * A small control strip shows the time, the mic level, **Pause** and **Stop**.
   * Your camera preview stays visible.
6. [ ] Press **Stop**. A **Recording saved** message appears with **Play**, **Open in Editor** and **Show in folder**.
7. [ ] Press **Play** and check the recording:
   * [ ] small screen text is readable (compare with rc.2);
   * [ ] your camera appears **exactly once**, at the position the caption said;
   * [ ] the floating preview, the control strip and the Record panel are **not** in the video;
   * [ ] the sound is clear.
8. [ ] Press **Show in folder**, then drag the MP4 into another File Explorer folder and play that copy.

## 2. Two programs

9. [ ] The Start menu has **SnapForge Capture** and **SnapForge Editor**, and no old single "SnapForge" entry.
10. [ ] Restart the PC with "Launch when I sign in" on. Only the SnapForge Capture icon and QuickDock appear; no window opens.
11. [ ] Start a recording, then open and close **SnapForge Editor**. The recording continues, and the hotkeys still work.
12. [ ] With the Editor closed, take a screenshot. The Editor opens with it.
13. [ ] With the Editor open, take another screenshot. It opens in the same Editor window.
14. [ ] Exit Capture (tray icon → Exit), then open the Editor from the Start menu. Your Library is there and items open.
15. [ ] **About** (in both programs) shows version 1.0.0-rc.3 and a build number.

## 3. Recording controls and problems

16. [ ] While recording, right-click the tray icon. **Stop recording and save**, **Pause** and **Show recording controls** are there.
17. [ ] Hide the camera preview (its **Hide** button), then show it again from the tray menu. The recording keeps the camera throughout.
18. [ ] **More ▾ → Camera in the recording** turns the camera off and on in the saved video. The preview caption says so.
19. [ ] Unplug the webcam while recording. The recording continues and the preview says the camera was disconnected.
20. [ ] Open another app that uses the camera (for example, the Camera app), then turn Camera on in SnapForge. A clear "in use by another app" message appears.
21. [ ] **More options** has camera only, devices, frame rate, quality, countdown, the overlay position/shape/size, and "Open in the Editor after recording" (off by default).

## 4. Keeping what you had

22. [ ] After upgrading from rc.2, all your captures, recordings and editable projects are in the Library, and your settings (hotkeys, Library location) are unchanged.
23. [ ] Screenshots, annotations, OCR, redaction and backup/restore still work.
24. [ ] Drag an image and an MP4 from the Editor's Library into File Explorer and into another app.

## Not checked by automated tests (needs your PC)

These have no automated check: real webcams and microphones, interactive UAC prompts, a second real Windows user, physical-mouse drag-and-drop into File Explorer, and hardware video encoders.
