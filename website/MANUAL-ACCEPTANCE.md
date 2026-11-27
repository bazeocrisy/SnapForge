# Manual accessibility acceptance (keyboard and screen readers)

Automated checks (axe-core, focus visibility, menu keyboard handling) catch only part of WCAG 2.2 AA. These manual
procedures must be run by a person before launch (`LAUNCH-CHECKLIST.md` A5). **Status: not yet run.**

Test against a local build (`python3 -m http.server 8000 --directory website/public`, open
`http://localhost:8000/`) or the deployed site. Record each result in the table at the end: pass / fail / notes,
with browser, screen reader and OS versions.

## 1. Keyboard only (Windows 11: Edge or Chrome, and Firefox)

Put the mouse away. On **every page**:

| ID | Step | Expected |
|---|---|---|
| K-01 | Load the page, press **Tab** once | "Skip to main content" appears at the top left, clearly visible |
| K-02 | Press **Enter** | Focus moves to the main content; the next Tab goes to the first link/button in the content, not the menu |
| K-03 | Reload; **Tab** through the whole page to the footer, then **Shift+Tab** back | Every link and button is reached, in reading order (left→right, top→bottom); nothing is skipped or trapped; a thick orange focus outline is always visible and never hidden behind the sticky header |
| K-04 | On the current page's menu item | It is announced/marked as current (orange underline) |
| K-05 | Activate links with **Enter** | They open; external links (GitHub) leave the site |
| K-06 | Home and Download: Tab to the disabled **Download for Windows** button | Either it is skipped (disabled) and the text "Not released yet…" next to it is readable, or it is focusable and announced as unavailable — never activatable |
| K-07 | Support page: Tab to each FAQ question, press **Enter** or **Space** | It expands/collapses; content is then reachable with Tab |
| K-08 | Guide page: use the "On this page" links | Focus/scroll moves to the section; the heading is not hidden under the header |
| K-09 | Window width ≤ 960 px (or zoom 200%): Tab to **Menu**, press **Enter** | Menu opens, focus can move through items; **Escape** closes it and returns focus to Menu |
| K-10 | Released configuration only (test build): **Copy** next to SHA-256 | Copies the checksum; "Copied" shown; screen readers announce it |

## 2. Zoom, reflow, text spacing, motion, contrast themes

| ID | Step | Expected |
|---|---|---|
| Z-01 | Browser zoom **200%** at 1280 px wide | All text readable; no overlapping; no horizontal scrolling (tables may scroll inside their own box) |
| Z-02 | Zoom **400%** (equivalent to 320 px width) | Single column; menu behind "Menu"; no content lost; no page-level horizontal scroll |
| Z-03 | Windows Settings → Accessibility → Text size 150% (Edge) | Layout holds |
| Z-04 | Text-spacing bookmarklet (line height 1.5, paragraph spacing 2×, letter spacing 0.12em, word spacing 0.16em) | No clipped or overlapping text |
| Z-05 | Windows Settings → Accessibility → Visual effects → **Animation effects off** | No smooth-scrolling or transitions |
| Z-06 | Windows **Contrast themes** (Aquatic, Desert, Dusk, Night sky) in Edge | All text, buttons, focus outlines and badges remain visible; buttons have borders |
| Z-07 | Disable JavaScript (Edge: Settings → Cookies and site permissions → JavaScript → Blocked) | Navigation is fully visible and usable; only the Copy button disappears |

## 3. Screen readers

Run at least **NVDA + Firefox** and **Narrator + Edge** on Windows 11. Run **VoiceOver + Safari** (iPhone) and
**TalkBack + Chrome** (Android) for the phone layout if devices are available; otherwise record "not tested".

| ID | Step | Expected |
|---|---|---|
| S-01 | Open each page | The title ("Download — SnapForge", etc.) is announced |
| S-02 | List landmarks (NVDA: **NVDA+F7** → Landmarks; Narrator: **Caps Lock+F5** to Landmarks; VoiceOver: rotor → Landmarks) | banner, navigation "Main", main, navigation "Footer", contentinfo — and the Guide's "On this page" navigation |
| S-03 | List headings (NVDA **H** / **NVDA+F7** → Headings; Narrator **H**; VoiceOver rotor → Headings) | One level-1 heading per page; levels never skip; headings describe their sections |
| S-04 | List links | Every link makes sense out of context; links to other sites say "(external site)" |
| S-05 | Images | Product screenshots have descriptive alt text; decorative icons and logos inside links are not announced separately; the brand link reads "SnapForge" |
| S-06 | Status badges ("Development build", "Experimental", "Planned") | Read as text next to the feature heading |
| S-07 | Disabled Download button | Announced as "Download for Windows, button, unavailable/dimmed" followed by (or with) the "Not released yet" description |
| S-08 | Tables (Home status labels; Guide shortcuts; Roadmap) | Table navigation (**Ctrl+Alt+arrows** in NVDA/Narrator) announces column headers; captions are read |
| S-09 | Keyboard shortcut cells | Keys are read in order ("Ctrl plus N") |
| S-10 | FAQ (Support) | Each question announced as a collapsed/expanded button-like summary; expanding reads the answer |
| S-11 | Mobile menu (phone or zoomed) | "Menu, button, collapsed"; after activation "expanded"; items reachable |
| S-12 | Notes ("Not released", "Check before you attach") | Read in place as part of the page |

## 4. Results

| ID range | Browser / AT / OS | Tester | Date | Result | Notes |
|---|---|---|---|---|---|
| K-01…K-10 | | | | | |
| Z-01…Z-07 | | | | | |
| S-01…S-12 (NVDA + Firefox) | | | | | |
| S-01…S-12 (Narrator + Edge) | | | | | |
| S-01…S-12 (VoiceOver + Safari, iOS) | | | | | |
| S-01…S-12 (TalkBack + Chrome, Android) | | | | | |
