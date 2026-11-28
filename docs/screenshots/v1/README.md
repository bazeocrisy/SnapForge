# Screenshots — Milestone V1 screen recording

**Real screen captures** made by `SnapForge.exe --screenshots` on the GitHub Actions Windows runner (single
1024×768 display, 100 %, software rendering), portable build with .NET hidden — commit `7cee382`, CI run
[45910436639](https://github.com/bazeocrisy/SnapForge/actions/runs/45910436639). Reduced JPEG previews (≤720 px);
full-size PNGs are in the CI artifact `screenshots-<commit>`. CI evidence, not a substitute for a real PC.

The demo video is a **real 4-second recording** made by the production recorder during the tour, of a synthetic test
pattern (so the screenshots do not depend on what else is on the runner's screen).

The recording toolbar and outline are excluded from screen capture in the app, so a screenshot cannot show them; for
these documentation images only they are shown **without** the exclusion. That the real toolbar and outline do not
appear in recordings is verified separately by the self-test's pixel check (`toolbar-not-recorded`).

| Surface | Dark | Light |
|---|---|---|
| Capture panel — Video mode (owner defaults: system audio on, microphone off, cursor on, countdown 3 s, 30 FPS, Standard; Record / Shift+F9) | ![](capture-panel-video-dark.jpg) | ![](capture-panel-video-light.jpg) |
| Capture panel — Image mode (unchanged) | ![](capture-panel-dark.jpg) | ![](capture-panel-light.jpg) |
| Recording: dashed outline around the recorded area and the floating toolbar | ![](recording-dark.jpg) | ![](recording-light.jpg) |
| Recording toolbar (elapsed time, size · FPS, Pause, Stop, Cancel, System audio / Microphone with level meters) | ![](recording-toolbar-dark.jpg) | ![](recording-toolbar-light.jpg) |
| Library — Videos view (play badge, duration) | ![](library-videos-dark.jpg) | ![](library-videos-light.jpg) |
| Library — all items (video tile among image captures) | ![](library-dark.jpg) | ![](library-light.jpg) |
| Player (playing; seek bar, time, mute, volume, info line) | ![](player-dark.jpg) | ![](player-light.jpg) |
| Settings → Recording (microphone, hotkeys, Library, disk space, privacy note) | ![](settings-recording-dark.jpg) | ![](settings-recording-light.jpg) |
| Widget expanded (Capture, **Record**, Editor, Library, Presets) | ![](widget-expanded-dark.jpg) | ![](widget-expanded-light.jpg) |
| Editor with Recent Captures tray (video in the tray) | ![](editor-dark.jpg) | ![](editor-light.jpg) |
| Widget collapsed / docked left | ![](widget-collapsed-dark.jpg) ![](widget-left-dark.jpg) | ![](widget-collapsed-light.jpg) ![](widget-left-light.jpg) |
| Settings (top) / widget and drag-and-drop | ![](settings-dark.jpg) ![](settings-widget-dragdrop-dark.jpg) | ![](settings-light.jpg) ![](settings-widget-dragdrop-light.jpg) |
