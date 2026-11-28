# Owner test checklist: SnapForge 1.0.0-rc.4 (build 177, unsigned test build)

**Download:** the **Download Windows test build** button on https://bazeocrisy.github.io/SnapForge/ (GitHub sign-in needed). Unzip the package to get `SnapForge-1.0.0-rc.4-win-x64-setup.exe`.

**Check the file first.** In PowerShell, run:

```
Get-FileHash .\SnapForge-1.0.0-rc.4-win-x64-setup.exe
```

It must show `9f42670a8299a4e9456a02ec51bcdc28ac1c31205a343ef7d1f35871287c3c4d`. Do not run a file whose checksum differs.

**About the blue window.** When you start Setup, Windows shows the blue Microsoft Defender SmartScreen window **"Windows protected your PC"**. This is expected: the test installer is **not code-signed**. Trusted signing is tracked separately as REL-SIGN-01 and needs your decision.

## 1. Reinstall with both apps open (main test)

1. [ ] Start **SnapForge Capture** and **SnapForge Editor** (Start menu) and leave them open and idle.
2. [ ] Run the rc.4 Setup over your rc.3 installation and approve the administrator prompt.
3. [ ] Setup closes **both** apps by itself and finishes. It must not ask you to exit SnapForge from the notification area.
4. [ ] Your captures, recordings, projects, settings and your Library location are all still there. Settings → Apps → Startup lists **one** SnapForge entry.
5. [ ] The Start menu has **SnapForge Capture** and **SnapForge Editor**, and both start.

## 2. Check the version in both apps

6. [ ] **SnapForge Capture:** right-click the icon next to the clock and choose **About SnapForge Capture**. The first line reads **SnapForge Capture 1.0.0-rc.4 (build 177)**.
7. [ ] **SnapForge Editor:** choose **Help → About SnapForge**. The first line reads **SnapForge Editor 1.0.0-rc.4 (build 177)**.
8. [ ] **Settings → Apps → Installed apps** shows **SnapForge 1.0.0-rc.4**.

## 3. Record with your webcam and microphone

9. [ ] Open QuickDock and choose **Record**. Pick Screen, Window or Area, and turn **Camera** and **Mic** on.
10. [ ] You see yourself in the floating preview, and the **Mic level** bar moves when you talk.
11. [ ] Record 20–30 seconds, then press **Stop**. A **Recording saved** message appears.
12. [ ] Play the recording and check that:
    - text is readable;
    - your camera appears **once**, where the preview caption said;
    - no SnapForge controls appear in the video;
    - the sound is clear.

## 4. Optional: Setup while you are busy

13. [ ] Start a recording, then run Setup. SnapForge asks whether to stop and save the recording.
    - Answer **No**: Setup says SnapForge is still open and offers **Retry / Cancel**.
    - Answer **Yes**: the recording is saved and Setup continues.
14. [ ] If another Windows account on this PC has SnapForge open, Setup names that session and stops without changing anything.

## Only your PC can check

* Real webcam and microphone.
* The interactive administrator (UAC) prompt.
* A second signed-in Windows account.
* SmartScreen behaviour.
* Dragging into File Explorer with a physical mouse.
* Hardware video encoders.
