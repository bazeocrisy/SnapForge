# SnapForge 1.0.0-rc.4: release notes (owner test build, build @BUILD@)

**Status:** not a public release and **not code-signed**. Public downloads stay disabled.

**What's new**

* **Setup closes SnapForge for you.** Install, reinstall, repair, upgrade and uninstall close SnapForge Capture and SnapForge Editor gracefully. This includes rc.1–rc.3, which understand the request. Setup never closes SnapForge by force:
  * **Recording:** SnapForge asks whether to stop and save the recording.
  * **Unsaved edits:** saved before the Editor closes.
  * **You keep something open:** Setup offers Retry or Cancel.
  * **Another Windows session:** if SnapForge runs there, Setup names that session and changes nothing.
  * **Other copies:** only the installation being updated is touched. A portable copy or an old per-user copy stays open.
* **The version is visible.** About in both programs shows "1.0.0-rc.4 (build @BUILD@)". This matches the installer file name, the Windows file version 1.0.0.@BUILD@ and Installed Apps.
* **Website.** A "Download Windows test build" button links to the exact CI package that passed. The listing shows the version, build, file name, SHA-256 and signing status. The public download button stays disabled.

**Blue screen**

The blue screen the owner reported is identified as **Microsoft Defender SmartScreen ("Windows protected your PC")** on the unsigned installer. It is not a SnapForge crash or a stop error. It is tracked as **REL-SIGN-01** (trusted code signing, open, owner decision). `tools/diagnostics/collect-snapforge-diagnostics.ps1` collects read-only diagnostics if another kind of blue screen ever appears.

**Unchanged from rc.3**

* the separate Capture and Editor programs;
* the compact Record panel with the live webcam preview;
* the sharper recording defaults.

**Verified in Windows CI** (run @RUN@): see PR #3. **Still needs your PC:** see [OWNER-CHECKLIST-rc.4.md](OWNER-CHECKLIST-rc.4.md).
