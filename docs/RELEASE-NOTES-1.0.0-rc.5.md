# SnapForge 1.0.0-rc.5: release notes (owner test build, build 187)

**Status:** not a public release and **not code-signed**. Public downloads stay disabled. SmartScreen's blue "Windows protected your PC" window is expected; trusted signing is tracked as REL-SIGN-01.

rc.5 is a **quality release**. It adds no new features. A full QA audit of the installed app at Windows display scaling 100 %, 125 %, 150 % and 175 % found the problems below, and rc.5 fixes them. The full checklist, with what passed and what is blocked, is in [QA-COVERAGE-rc.5.md](QA-COVERAGE-rc.5.md).

**Easier to read**

* **No more tiny text.** Every label in SnapForge is now at least 12 (Windows scales it up from there). Before, Editor tool names, badges, headings, Library details, QuickDock labels and the recording windows used 9–11.5.
* Capture titles that are too long for their thumbnail show the full title when you point at them.

**Fits the screen at high scaling**

* **Record panel:** with More options open, the panel ran off the bottom of the screen. At 125–150 % this **hid the Cancel and Start recording buttons**. The panel now stays on screen, its options scroll, and the two buttons are always visible.
* **Any window that grows after opening** is moved back onto the screen.
* **QuickDock:** the collapsed pill is no longer cut off at the bottom.

**Keyboard and screen readers**

* **Capture panel:** pressing Tab could move focus onto an invisible "Undo" link, so the focus seemed to disappear and Tab no longer reached Capture, Library or Editor. Fixed.
* **Editor toolbar:** when it wraps onto two rows (175 % and narrow windows), the arrow keys now reach every tool.
* The video editor's quality list now has a name screen readers read out.

**Website**

* The download page no longer scrolls sideways on phones and tablets.

**Upgrades**

rc.5 installs over **rc.4 (build 177)** with SnapForge Capture and SnapForge Editor both open. It also installs over rc.1, rc.2 and rc.3. Captures, recordings, projects, settings and your Library location are kept. All of these were tested in CI with real older builds.

**Verified in Windows CI:** run 187, https://github.com/bazeocrisy/SnapForge/actions/runs/46178250395.

**Still needs your PC:** see [OWNER-CHECKLIST-rc.5.md](OWNER-CHECKLIST-rc.5.md). It is grouped, short, and limited to what a virtual machine cannot do.
