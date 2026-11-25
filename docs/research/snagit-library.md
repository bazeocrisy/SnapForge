<!-- Research notes compiled 2026-11-25 for Gate A. Evidence: search-engine extracts of official pages only; see docs/SOURCE-REGISTRY.md. -->
# Snagit (Windows) feature-parity research: Library, Tray, Documentation, Video, Export/Share, Enterprise, Release history

Research date / access date for all sources: 2026-11-25
Purpose: functional-behaviour facts only, for a feature-parity audit of an original open-source screenshot app. No code, assets, or branding copied.

## Method and access limitations (read first)

- Direct fetch was attempted and FAILED (DNS not resolvable from this sandbox, i.e. outside the allowed network) for:
  `support.techsmith.com`, `www.techsmith.com`, `assets.techsmith.com`, `download.techsmith.com`, `learn.microsoft.com`.
  To read the pages in full, those hosts must be allowed in the session's network settings.
- Therefore **every source below was accessed as "search extract only"**: titles, URLs, and short snippets returned by the WebSearch tool. **No page was read in full.** Short quotes are from search snippets. They are believed accurate, but they are not checked against the full page and may lack context.
- Evidence classes:
  - **DOC**: the statement is supported by a snippet from an official TechSmith or Microsoft page (search extract).
  - **INFER**: a reasonable deduction from DOC items. It is not stated outright.
  - **UNVERIFIED**: from a non-official source, from conflicting snippets, or from a summary that has no clear official quote.
- Several TechSmith tutorials say that they are written for one version (e.g. "Snagit 2024" or "Snagit 2025") and that "previous versions may look or function differently".

## Sources

All sources: access date 2026-11-25; access method = search extract only (direct fetch blocked).

| ID | URL | Page title | Version stated |
|---|---|---|---|
| S-LIB-01 | https://www.techsmith.com/learn/tutorials/snagit/recent-captures-tray/ | Recent Captures Tray | current tutorial (version not in snippet) |
| S-LIB-02 | https://www.techsmith.com/learn/tutorials/snagit/preferences/ | Snagit Preferences – Windows | current |
| S-LIB-03 | https://support.techsmith.com/hc/en-us/articles/115006435067 | Snagit Windows Version History (incl. 2024 entries) | 2024.1.2 item |
| S-LIB-04 | https://www.techsmith.com/learn/tutorials/snagit/snagit-library/ | Introduction to Snagit Library | current |
| S-LIB-05 | https://www.techsmith.com/learn/tutorials/snagit/snagit-library-location/ | Snagit Library Location | current |
| S-LIB-06 | https://www.techsmith.com/learn/tutorials/snagit/manage-library/ | Import a Library from a Previous Version (Manage the Library) | 2022+ |
| S-LIB-07 | https://support.techsmith.com/hc/en-us/articles/203731768 | Move Unsaved Captures to a Different Computer; Back-up & Restore Library in Snagit | 2021 and earlier |
| S-LIB-08 | https://support.techsmith.com/hc/en-us/articles/203732148-Manually-Backup-and-Restore-a-Snagit-Datastore | Manually Backup and Restore a Snagit Datastore | legacy |
| S-LIB-09 | https://support.techsmith.com/hc/en-us/articles/41301513334925-My-Snagit-Library-is-Behaving-Erratically | My Snagit Library is Behaving Erratically | current |
| S-LIB-10 | https://support.techsmith.com/hc/en-us/articles/18514512148365-Can-the-Snagit-Library-Be-Used-as-a-Storage-Location | Can the Snagit Library Be Used as a Storage Location? | n/s |
| S-LIB-11 | https://www.techsmith.com/learn/tutorials/snagit/file-save-tips/ | Best Practices for Saving Your Snagit Files | current |
| S-LIB-12 | https://support.techsmith.com/hc/en-us/articles/38927872972941-How-to-Default-to-a-File-Type-Other-Than-SNAGX | How to Default to a File Type Other Than SNAGX | current |
| S-LIB-13 | https://filext.com/file-extension/SNAGX | Opening SNAGX Files (NON-OFFICIAL) | n/a |
| S-LIB-14 | https://support.techsmith.com/hc/en-us/articles/36349926940941-What-Happens-to-My-Content-After-My-Subscription-Expires-in-Snagit | What Happens to My Content After My Subscription Expires in Snagit? | subscription era |
| S-LIB-15 | https://www.techsmith.com/learn/tutorials/snagit/batch-convert-images/ | Batch Convert Images | Snagit 2024 |
| S-LIB-16 | https://techsmith.com/tutorial-snagit-image-from-template.html (also /learn/tutorials/snagit/image-from-template/) | Create Visual Guides with Templates | Snagit 2025 |
| S-LIB-17 | https://www.techsmith.com/tutorial-snagit-combine-images.html | Combine Images | older |
| S-LIB-18 | https://www.techsmith.com/learn/tutorials/snagit/step-capture-outputs/ | Export and Share Your Step Capture Documentation | 2025+ |
| S-LIB-19 | https://www.techsmith.com/learn/tutorials/snagit/step-capture/ | Document a Process or Workflow Automatically (Step Capture) | 2025+ |
| S-LIB-20 | https://www.techsmith.com/learn/tutorials/snagit/video-from-images/ | 3 Ways to Turn Your Snagit Screenshots into a Video | Snagit 2025 |
| S-LIB-21 | https://www.techsmith.com/learn/tutorials/snagit/how-to-capture-video/ | how to capture video (Record a Video) | current |
| S-LIB-22 | https://www.techsmith.com/learn/tutorials/snagit/editing-video/ | Edit a Video | current |
| S-LIB-23 | https://www.techsmith.com/learn/tutorials/snagit/combine-videos/ | Combine Videos | Snagit 2024 |
| S-LIB-24 | https://www.techsmith.com/learn/tutorials/snagit/screen-draw/ | Screen Draw | current |
| S-LIB-25 | https://www.techsmith.com/learn/tutorials/snagit/animated-gif/ | Create an Animated GIF | Snagit 2024 |
| S-LIB-26 | https://support.techsmith.com/hc/en-us/articles/31970928001165-My-Snagit-GIF-is-Blurry | My Snagit GIF is Blurry | n/s |
| S-LIB-27 | https://support.techsmith.com/hc/en-us/articles/217552078 | Snagit Video Frame Rate | 2024.2.0+ |
| S-LIB-28 | https://support.techsmith.com/hc/en-us/articles/41688493993613-What-File-Formats-Does-Snagit-Support | What File Formats Does Snagit Support | snippet dated 5 Dec 2025 |
| S-LIB-29 | https://support.techsmith.com/hc/en-us/articles/44545405511821-Supported-File-Types-on-Camtasia-Snagit-for-Mac | Supported File Types on Camtasia Snagit for Mac | Mac, 2026 naming |
| S-LIB-30 | https://support.techsmith.com/hc/en-us/articles/43410048034061-Snagit-Windows-2022-Version-History | Snagit Windows 2022 Version History | 2022.1.1 |
| S-LIB-31 | https://www.techsmith.com/learn/tutorials/snagit/share/ (and /learn/tutorials/camtasia/share/ which hosts Snagit share text) | Share Captures | current |
| S-LIB-32 | https://support.techsmith.com/hc/en-us/articles/115002022732-Download-Snagit-Share-Destinations | Download Snagit Share Destinations | 13 / 2018 / 2019–2023 table |
| S-LIB-33 | https://support.techsmith.com/hc/en-us/articles/203731148-Automatically-Save-Images-to-a-Specific-Format-During-Capture | Automatically Save Images to a Specific Format During Capture | n/s |
| S-LIB-34 | https://www.techsmith.com/wp-content/uploads/2026/10/Snagit-MSI-Installation-Guide.pdf | Snagit MSI Installation Guide – Version 2026.4.0 | 2026.4.0 |
| S-LIB-35 | https://assets.techsmith.com/Docs/Snagit-2025-MSI-Installation-Guide.pdf (also /wp-content/uploads/2025/08/Snagit-MSI-Installation-Guide.pdf) | Snagit 2025 MSI Installation Guide | 2025 |
| S-LIB-36 | https://www.techsmith.com/wp-content/uploads/2026/02/Snagit-MSI-Installation-Guide.pdf | Snagit MSI Installation Guide – Version 2026.0.0 | 2026.0.0 |
| S-LIB-37 | https://www.techsmith.com/wp-content/uploads/2026/10/Snagit-Deployment-Tool-Guide_v2026_4_0.pdf | Deployment Tool Guide for Camtasia Snagit (v2026.4.0) | 2026.4.0 |
| S-LIB-38 | https://www.techsmith.com/wp-content/uploads/2025/08/Snagit-Deployment-Tool-Guide.pdf | Snagit 2025 Deployment Tool Guide | 2025 (tool v6.7.0) |
| S-LIB-39 | https://support.techsmith.com/hc/en-us/articles/43771074923021 | Supported technologies for deploying and managing Snagit, Camtasia, and Audiate in your enterprise environment | current |
| S-LIB-40 | https://support.techsmith.com/hc/en-us/articles/40742962847501-Deploying-TechSmith-Snagit-as-a-Win32-app-via-Microsoft-Intune | Deploying TechSmith Snagit as a Win32 app via Microsoft Intune | current |
| S-LIB-41 | https://assets.techsmith.com/Docs/Snagit-2025-Microsoft-Endpoint-Configuration-Manager.pdf | Snagit 2025 Microsoft Endpoint Configuration Manager | 2025 |
| S-LIB-42 | https://support.techsmith.com/hc/en-us/articles/43559688695693-Activate-Snagit-Offline | Activate Snagit Offline | subscription era |
| S-LIB-43 | https://support.techsmith.com/hc/en-us/articles/24282017627405 | Licensing & Activation (FAQ) | n/s |
| S-LIB-44 | https://support.techsmith.com/hc/en-us/articles/27009223314701 | TechSmith Transition to Annual Subscription Pricing Model in 2025 | 2025+ |
| S-LIB-45 | https://support.techsmith.com/hc/en-us/articles/31112245401229 | Maintenance Transitioning to a Legacy License Subscription FAQ | 2025+ |
| S-LIB-46 | https://support.techsmith.com/hc/en-us/articles/46075159178253-How-can-I-disable-AI-features-in-Snagit-and-Camtasia | How can I disable AI features in Snagit and Camtasia? | current |
| S-LIB-47 | https://assets.techsmith.com/docs/Snagit-2024-VPAT-2.5.pdf | Snagit 2024 VPAT 2.5 | 2024 (dated 14 Oct 2024) |
| S-LIB-48 | https://support.techsmith.com/hc/en-us/articles/25840273917581-TechSmith-Accessibility-VPAT-and-WCAG | TechSmith Accessibility (VPAT and WCAG) | n/s |
| S-LIB-49 | https://support.techsmith.com/hc/en-us/articles/42674936732685-Snagit-Windows-2026-Version-History | Snagit Windows 2026 Version History | 2026.x |
| S-LIB-50 | https://support.techsmith.com/hc/en-us/articles/39102293598349-Snagit-Windows-2025-Version-History | Snagit Windows 2025 Version History | 2025.x |
| S-LIB-51 | https://support.techsmith.com/hc/en-us/articles/45409168722445-Issues-With-Grab-Text-or-Other-OCR-Functions-Such-as-Edit-Text-or-Smart-Redact | Issues With Grab Text or Other OCR Functions… | 2026 |
| S-LIB-52 | https://www.capterra.com/p/209649/Snagit/reviews/?page=7 | Snagit Reviews 2026 (NON-OFFICIAL, user review) | n/a |
| S-LIB-53 | https://www.appdeploynews.com/app-tips/techsmith-snagit-2023-23-0-1-23930/ | Techsmith Snagit 2023 (NON-OFFICIAL packaging notes) | 2023 |
| S-LIB-54 | https://learn.microsoft.com/en-us/windows/win32/shell/clipboard | Shell Clipboard Formats (Microsoft) | n/a |
| S-LIB-55 | https://learn.microsoft.com/en-us/windows/win32/shell/datascenarios | Handling Shell Data Transfer Scenarios (Microsoft) | n/a |
| S-LIB-56 | https://devblogs.microsoft.com/oldnewthing/?p=23083 | What a drag: Dragging a virtual file (HGLOBAL edition) (Microsoft employee blog, semi-official) | n/a |
| S-LIB-57 | https://www.neowin.net/software/snagit-202640/ (and -202630, -202620, -202610) | Snagit 2026.x release posts (NON-OFFICIAL mirror of release notes) | 2026.x |
| S-LIB-58 | https://www.techsmith.com/snagit/features/ | Snagit Features | current (snippet only) |
| S-LIB-59 | https://www.techsmith.com/learn/tutorials/snagit/snagit-hotkeys/ | Snagit Hotkeys Guide | current |
| S-LIB-60 | https://support.techsmith.com/hc/en-us/articles/226262508 | Guidelines for 4K 60FPS Recording and Editing | n/s |

---

## 1. Recent Captures Tray

- **Pin/unpin.** Clicking the Pin icon "anchors a capture thumbnail to the left side of the tray". New captures are added to the right of pinned captures. Clicking Pin again unpins the capture. [S-LIB-01] DOC
- **Reorder.** You can drag a thumbnail to reorder it in the tray, whether or not it is pinned. [S-LIB-01] DOC
- **Close vs delete.** The Close icon removes the thumbnail from the tray. "Removing a capture from the tray doesn't delete the file" (snippet paraphrase). Delete is a separate right-click command ("Delete" on Windows, "Move to Trash" on Mac) that works in both the tray and the Library. [S-LIB-01] DOC
- **Bulk clear.** On Windows, a preference clears the tray when the Editor closes. Mac has "Close Other Captures". [S-LIB-01] DOC (the exact preference label was not captured)
- **Show/hide.** The Show Recent/Hide Recent button (Windows) toggles the tray. The Mac hotkey is Option+Command+T. No default Windows hotkey is listed. [S-LIB-01, S-LIB-59] DOC
- **Thumbnail size.** "right-click the thumbnail and select Thumbnail Size > Small, Medium, or Large (Windows only)". [S-LIB-01] DOC
- **Tray resize by dragging the divider.** This appears only in Snagit 10 help (2010). No current documentation was found. UNVERIFIED for current versions.
- **Multi-select.** Ctrl+click selects several items in the tray or Library. The selection is then used for Combine in Template, Combine Videos, Video from Images, etc. [S-LIB-16, S-LIB-20, S-LIB-23] DOC
- **Drag into the canvas.** Dragging a tray image onto the canvas combines it with the current image. [S-LIB-01] DOC
- **Drag OUT to other apps.** This is supported. Snagit 2024.1.2 (25 Apr 2024) "Added an option to set the default file type when dragging and dropping image files from the Recent Captures Tray or Snagit Library to other applications. This is available on the Advanced tab of Snagit Editor Preferences." [S-LIB-03] DOC. The Preferences page describes it as "Set the preferred file type when you drag SNAGX files from the Recent Captures Tray or Library to another app or file location." [S-LIB-02] DOC
  - INFER: the drag converts the .snagx (flattened) into the chosen raster format at drop time. The data-object mechanism Snagit uses (a temp file via CF_HDROP or a virtual file via FILEDESCRIPTOR/FILECONTENTS) is NOT documented. UNVERIFIED.
  - The Mac counterpart is "Default sharing format". On Mac this setting does not affect the Save As format. [S-LIB-01/02 Mac pref snippet] DOC (Mac)
- **Tooltips.** No documentation was found about tray thumbnail tooltips. UNVERIFIED.
- **2026.3.0.** The tray and Library show an image-count indicator on Step Capture thumbnails. [S-LIB-49 via search extract] DOC

## 2. Snagit Library

- **What it holds.** The Library holds the capture history: images, videos, and GIFs. [S-LIB-04] DOC
- **Search and filters.** "Search for captures by file name and/or filter by date, tags, application source, or website source." [S-LIB-04] DOC
- **Metadata.** Captures carry "additional details … which allows the Snagit Library to organize them based on date, application, web site". On Windows, right-click a capture and select **Details** to view it. When a web browser is captured, Snagit records the website URL in place of the browser application name. [S-LIB-04] DOC
- **Year/month/day browsing.** This was removed in early 2022 and "added back" in 2022.1.7. [S-LIB-04 / 22 update message, search extract] DOC
- **Tags.** Tags are assigned manually and you can filter by them. "Tags are only visible in Snagit" (they are not written into Windows file properties). [S-LIB-04] DOC
- **Favourites.** A Favorites filter exists **only on Mac** (captures you mark manually). [S-LIB-04] DOC. Windows has no flags or favourites: INFER from the "Mac only" wording.
- **Folders.** Snippets found no user folder structure inside the Library. There is a G2 user thread about folders disappearing in 2022. UNVERIFIED.
- **Full-text (OCR) search in the Library.** NOT documented. Library search is metadata-based. A 2026 user review asks for "automatic OCR for all screenshots with a robust search functionality". [S-LIB-04 DOC; S-LIB-52 UNVERIFIED, non-official]. OCR exists per capture: Grab Text / Edit Text. 2026.0.0 "improved Optical Character Recognition (OCR) processing to find & edit text in images". 2026.4.0 made Microsoft OCR the default engine. A third-party OCR licence expired on 1 May 2026, which broke Grab Text until TechSmith shipped updates. [S-LIB-49, S-LIB-51] DOC
- **Step Capture category** was added to the Library in 2026.3.0. [S-LIB-49] DOC
- **Delete and trash.** Right-click > Delete (Windows). Multi-select uses Shift-click. Snippets found no in-app trash or recovery bin on Windows. One TechSmith snippet says "Snagit cannot recover deleted files" (that was in a conversion-backup context). [S-LIB-04, S-LIB-07/08] DOC (limited). Whether files go to the Windows Recycle Bin: UNVERIFIED.
- **Retention / auto-delete.** No retention or auto-purge setting was found. Captures "are never deleted unless you choose to delete them", even after a subscription expires. After expiry, the software is deactivated and there is "a limited period of time to export captures using the Snagit Editor". [S-LIB-14] DOC. The length of the export window was not in the snippet.
- **Library location / moving.** Edit > Editor Preferences > Library tab > **Choose location**. If the target folder already holds a library, the two libraries merge. [S-LIB-05, S-LIB-06] DOC
  - OneDrive (Business and Personal) is supported if the client downloads all files ("Download all OneDrive files now"). TechSmith also says "storing your Snagit Library on OneDrive is the leading cause" of erratic Library behaviour. [S-LIB-05, S-LIB-09] DOC
  - Network drive: supported with limited function. "File counts, tags, and categories such as year, month, and date are not available"; "Searching by metadata other than file name is not supported." [S-LIB-05] DOC
  - Enterprise: if the data store is on a network location, the database file is cached into %LocalAppData% during the session (older enterprise datapath article 217369538). DOC (legacy)
- **Library as storage.** One support article exists: "Can the Snagit Library Be Used as a Storage Location?" [S-LIB-10]. Its body was not captured. UNVERIFIED.
- **Backup/restore.**
  - Snagit 2021 and earlier: backup to a `.snagarchive` file. "Restoring a library overwrites the captures in the current library. Merging two libraries is not supported at this time." [S-LIB-07] DOC (legacy)
  - Snagit 2023+: Editor Preferences > Library > **Import Backup** imports 2021-and-earlier backups. The merge/import path accepts only SNAG, MP4, and GIF, not arbitrary JPG/PNG. [S-LIB-06] DOC
  - Manual fallback: copy the DataStore folder (default `C:\Users\<user>\AppData\Local\TechSmith\Snagit`, legacy). [S-LIB-08] DOC (legacy)
  - The current 2022+ method is folder-based. INFER: because the Library is a folder of .snagx files, a backup is a copy of that folder plus "Choose location" on the new machine.
- **Importing arbitrary images.** No snippet documented a Windows command to import JPG/PNG into the Library. Opening an image in the Editor adds it to the tray. UNVERIFIED.
- **Batch export/convert.** The Batch Export wizard applies one change to a group of images: change the file format, apply effects, or rename. You can also save to a new location. Use **Add Files** in the wizard, then Next. The tutorial is for Snagit 2024. [S-LIB-15] DOC. The Explorer context menu "Snagit > Batch Convert Images" and the format list (png, bmp, jpg, gif, pdf, tif, ico, psd…) come from third-party sites. UNVERIFIED.
- **Uninstall data retention (MSI).** `TSC_DATA_STORE=0` keeps the Library on uninstall and `=1` removes it. [S-LIB-53] UNVERIFIED (non-official, Snagit 2023)

## 3. SNAGX / SNAG formats and Autosave

- **Autosave.** "Snagit automatically saves your image captures, including edits and annotations, as SNAGX files in the Snagit Library. The Library serves as a complete history of captures." [S-LIB-11] DOC
- **Turning autosave off (Windows only).** Edit > Editor Preferences > Library tab > deselect **"Automatically save new image captures in the Library"**. Captures then go straight to the chosen file type. [S-LIB-11, S-LIB-12] DOC
- **SNAGX replaces older formats.** "Snagit's SNAGX file format replaces the former SNAG format for Windows and SNAGPROJ format for Mac and is compatible with both platforms." [S-LIB-11] DOC. Introduced with Snagit 2022. INFER: based on the 2022 library rework and "Convert Your Previous Captures" tutorial titles.
- **Openness.** Other programs cannot open .snagx: "If you leave your captures in the .snagx or .snag format, they won't be able to be opened by other programs". [S-LIB-11] DOC. TechSmith publishes **no format specification** (none found). INFER.
- **Internal structure.** A non-official site says SNAGX is a ZIP-based container with entries such as index.json, metadata.json, and thumbnail.png, and calls the structure "proprietary". [S-LIB-13] UNVERIFIED. Snagit 2026.3.1 sends images to Camtasia Online "in the original .snagx format". [S-LIB-49] DOC
- **Transparency.** To keep transparency, save as "CUR, GIF, ICO, PNG, TIF, or SNAGX". [S-LIB-11] DOC
- **Mac flattening.** On Mac, a non-SNAGX file is flattened when you quit, and its annotations can no longer be edited. [S-LIB-11] DOC (Mac)

## 4. Documentation features

- **Combine in Template / Apply Template (Snagit 2025).** Ctrl+click images in the tray or Library, then right-click > **Combine in Template** (several images) or **Apply Template** (one image). A dialog lets you pick a template and edit "Created by". Next > reorder or edit > **Combine**. Snagit opens the result on the canvas and "scales the images to fit based on the template". [S-LIB-16] DOC
- **Image from Template.** Create > **Image from Template**: choose a template, then add images and text on the canvas. More templates come with a paid TechSmith Assets subscription. [S-LIB-16] DOC
- **Legacy Combine Images.** Ctrl+click in the tray > right-click > Combine Images (Snagit 2019: Image menu > Combine Images). [S-LIB-17] DOC (legacy)
- **Step Capture (new in 2025.0.0).** Takes a screenshot automatically on each click and builds a guide with numbered steps in the Editor. [S-LIB-19, S-LIB-50] DOC
  - Outputs: most share destinations get a flattened image ("Snagit sends the Step Capture as an uneditable image"). Editable export goes to Microsoft Word and PowerPoint (Windows/Mac), plus Keynote and Pages (Mac only). HTML or Markdown export and "File > Export > Step Capture images" batch export are **Mac only**. Limitation: "text font, text color, and step color are not preserved in editable exports". [S-LIB-18] DOC
  - Step Capture to Camtasia Online video requires 2026.3.0+. [S-LIB-18, S-LIB-49] DOC
  - The minimum interval-capture delay rose from 1 to 3 s in 2026.3.1. [S-LIB-49] DOC (it is unclear whether this applies to Step Capture or to interval capture in general)
- **Video from Images.** Select images in the tray or Library > Create > **Video from Images**. The Recording toolbar appears, and you record voice narration (with or without camera) while stepping through the images. You can draw on screen with the tools during recording. TechSmith says "up to 5 minutes of narration for each image". [S-LIB-20] DOC
  - A Nov 2024 changelog entry mentions an "Add Narration" button under the Move tool. DOC (search extract of techsmith.com changelog)
- **PDF output.** PDF is "export only" (Save As / output), not an import format. [S-LIB-28] DOC. A multi-page PDF from several images ("Combine images into PDF") has a techsmith.com page, but no snippet detail was retrieved. UNVERIFIED.
- **Slides.** Step Capture exports each step as an editable PowerPoint slide. [S-LIB-18] DOC. No other slides feature was found.

## 5. Video

- **Recording sources.** The Video tab of the Capture window records region, window, or full screen. INFER: these are the same selection modes as image capture. Not separately quoted.
- **Audio.** Microphone ("built-in microphone or … a connected USB microphone") and system audio are toggled separately. Auto mic gain is Mac only. A support article covers recording a microphone plus incoming Zoom/webinar audio. [S-LIB-21] DOC
- **Webcam.** Picture-in-picture or full screen, with shapes "Circle, Rectangle, Rounded Rectangle, Square, or Rounded Square". Mirror and blur are Mac only. [S-LIB-21] DOC
- **Pause/resume.** "click the Pause button or press Shift+F9 (Windows)". [S-LIB-21] DOC
- **Countdown.** Not documented in the snippets found. UNVERIFIED. Related setting: "Automatically start video recording" (Windows) starts recording right after the area is selected and is off by default. [S-LIB-02] DOC
- **Quality.** Capture Preferences > Capture > **Video Quality** dropdown. The default is Medium. "Very High" gives 4K/1080p up to 60 fps in 2024.2.0+. The frame rate is variable ("may vary throughout the video, up to 60 FPS"). True 4K60 needs a discrete GPU. [S-LIB-27, S-LIB-60] DOC
- **Cursor effects.** Capture cursor on/off. **Highlight Cursor** with a colour, and **Click Animation** with a colour. [S-LIB-02 / video preferences snippet] DOC
- **Drawing while recording (Screen Draw).** Start recording, then click Screen Draw or press **Ctrl+Shift+D**. Tools are Arrow, Square/Shape, and Step. Colours follow the active Editor theme. [S-LIB-24] DOC
- **Output.** MP4 opens in the Editor. 2026.1.0 replaced the "Preview in Editor" toggle with an **Open In** setting on the Video tab. 2026.4.0 can record directly to Camtasia Online. [S-LIB-21, S-LIB-49] DOC
- **Trim/cut.** Drag the timeline handles to select a segment, then cut it, save it as a new video, or save it as an animated GIF. A yellow line marks a cut. [S-LIB-22] DOC
- **Save frame as image.** Move the playhead and click **Save Frame as Image**. The still opens on the canvas. Frame-step buttons and arrow keys help find the frame. [S-LIB-22] DOC
- **Combine clips (Snagit 2024+).** Playback toolbar > **Add Videos** > select > drag to reorder > **Combine Videos**. You can also multi-select in the tray > right-click > Combine Videos. Clips with different sizes get black borders. [S-LIB-23] DOC
- **Animated GIF.** Use **Create Animated GIF** on the whole video or a selection. Custom options: Frame Rate, Dimensions, Dithering, **Looping** ("If looping is enabled, the Animated GIF plays back on a continuous loop"), and Fade to Black. The default preset favours small files. TechSmith suggests Custom at 30 fps, 100 % size, no dithering, looping on, and files of 1 MB or less. [S-LIB-25, S-LIB-26] DOC

## 6. Export / Save As / Share

- **Save As formats (Windows).** The support article lists "BMP, GIF, ICO, JPG, PNG, TIF, PSD, TGA, WMF, and others", with "CUR, PDF, and EPS" as export only (snippet dated 5 Dec 2025). [S-LIB-28] DOC (partial list; "and others" is not enumerated in the snippet)
- **WebP.** Added on Windows in **2022.1.1** (9 Aug 2022). [S-LIB-30] DOC
- **HEIF.** Listed only on the Mac supported-file-types page (PNG, JPG, HEIF, TIFF, GIF, BMP, WEBP; PDF export only). [S-LIB-29] DOC (Mac). HEIF on Windows: UNVERIFIED. **SVG: no evidence** of SVG export in any source. UNVERIFIED / likely unsupported.
- **Default format.** Share menu > **Save As Preferences** sets the default Save As type. [S-LIB-11] DOC
- **Auto-save on capture.** Capture preset > Share = **File** > gear icon: set the file type, name, and folder, then Save Preset. [S-LIB-33] DOC
- **Filenames.** By default, shared captures get descriptive names taken from the source context. Windows: Editor Preferences > Share tab can turn this off, falling back to capture date/time or a custom name. [S-LIB-02, S-LIB-31] DOC. The full token syntax for automatic file names was not documented in current snippets (Snagit 12 help had "Automatic File Name Options"). UNVERIFIED for current.
- **Clipboard and print.** Clipboard and Printer (Windows) share destinations exist. Google Drive uploads put a share link on the clipboard. [S-LIB-31] DOC
- **Share destinations (vary by version).**
  - Support table [S-LIB-32]: Camtasia, Screencast, SharePoint, Teams, Box, Slack, Google Drive, Dropbox, Word (several marked 2019–2023). DOC
  - 2025/2026 Deployment Tool Guides [S-LIB-37, S-LIB-38]: Word, Excel, PowerPoint, Screencast, Camtasia Editor, Camtasia Online, Google Drive, Slack, FTP, Evernote, Microsoft Teams, YouTube, Dropbox, OneDrive, OneNote, SharePoint, Panopto, Twitter/X. DOC (snippet summaries; per-version lists differ)
  - Changes: 2026.2.0 Email works with classic Outlook and "New Outlook". 2026.3.0 moved OneDrive, SharePoint, and Teams sign-in to built-in Windows Authentication. 2026.4.0 **removed the X share destination**. [S-LIB-49] DOC
  - Administrators can disable destinations at deployment. "How to Remove Snagit Outputs" (support 31718726464781) exists. DOC (title only)
- **Offline behaviour.** Not documented. INFER: local destinations (File, Clipboard, Printer, Office apps) need no network, and cloud destinations need sign-in. A licence check needs internet at least every 90 days (see §7).

## 7. Enterprise / security / licensing

- **Licence model.** Annual subscription only, starting with Snagit 2025 (released 12 Feb 2025). Maintenance holders moved to a "Legacy" subscription with discounted renewals "until at least 2030". Older perpetual versions stay usable. [S-LIB-44, S-LIB-45] DOC. It is "an annual subscription license of a locally installed, desktop version". DOC. 2026.2.2 lets Camtasia Editor subscribers activate Snagit when their subscription includes it. [S-LIB-49] DOC
- **Internet requirement.** "all TechSmith applications require access to the internet for license activation and at least every 90 days to verify current license status." [S-LIB-39/S-LIB-42 snippet] DOC
- **Offline activation.** Manual offline activation steps exist: generate a code offline, use the online activation tool on another device, then click "Offline Activation" in Snagit. [S-LIB-42] DOC. **CONFLICT:** a licensing FAQ says "There is no offline activation. You would need to refund the subscription and purchase perpetual". [S-LIB-43] DOC. Unresolved; it may differ for individual and business licences.
- **Account/sign-in.** MSI deployments: "user accounts are not required when deploying Snagit … using the MSI file". [S-LIB-39] DOC. Individual activation sign-in flow: "License Verification & Sign-In Issues" article exists (title only). The key is a site-wide licence key (`TSC_SOFTWARE_KEY`), validated online at launch. [S-LIB-34] DOC
- **MSI deployment.**
  - Silent install: `msiexec /i snagit.msi TSC_SOFTWARE_KEY=… /qn`, plus `/l*v` for logging. [S-LIB-34] DOC/INFER
  - Transforms: `TRANSFORMS=snagit.mst`. The Deployment Tool generates an **.mst plus a .bat**. [S-LIB-37, S-LIB-38] DOC
  - Prerequisites (e.g. WebView2) are not installed by the MSI, so deploy them first. Install WebView2 per machine. [S-LIB-38] DOC
  - Uninstall: `msiexec /x {ProductCode} TSC_DATA_STORE=0 /qn`. [S-LIB-53] UNVERIFIED (non-official, 2023)
- **Properties seen in snippets.**
  - `TSC_SOFTWARE_KEY` (licence) [S-LIB-34]
  - `TSC_START_AUTO` (run at Windows start; delete the row to disable) [S-LIB-36]
  - `TSC_ALLOW_TUDI` (anonymous usage data; also required for in-app notifications) [S-LIB-35]
  - `TSC_IN_PRODUCT_NOTIFICATIONS_ALLOW=0` [S-LIB-36]
  - Legacy `TSC_TUDI_OPTIN_VALUE=0`, `TSC_DESKTOP_LINK` (Snagit 10/11)
  - All DOC (search extracts). The guides also have "Advanced Customization: Edit the Registry Settings" and "Snagit Registry File Options" sections; their key names were not captured.
- **Telemetry.** Users can choose not to send usage data in Editor Preferences. Admins can turn it off in the Deployment Tool or with the TUDI property. A separate article covers disabling **AI features**. [S-LIB-46, S-LIB-35] DOC
- **Update control.** No MSI property for disabling update checks surfaced. UNVERIFIED.
- **Intune/SCCM/Patch My PC.** All supported. Intune uses a Win32 app (.intunewin made with the Content Prep Tool). The MECM guide installs "whether or not a user is logged on" with visibility Hidden and uses the Uninstaller Tool for prior versions. An RDS guide covers 2025+ with Deployment Tool 6.5.0+. App-V guides exist (legacy). A Microsoft Store install article exists. [S-LIB-39, S-LIB-40, S-LIB-41] DOC
- **Standard (non-admin) user.** No explicit statement was found. INFER: per-machine MSI deployed by the system context.
- **Accessibility.** Snagit 2024 VPAT 2.4 (for 2024.0.2, dated 8 Nov 2023) and VPAT 2.5 (dated 14 Oct 2024, WCAG 2.0/2.1). No 2025 or 2026 Snagit VPAT was found. [S-LIB-47, S-LIB-48] DOC
- **Encryption.** No documentation on encryption of the Library or SNAGX at rest. UNVERIFIED (INFER: none). 2026.3.0 "updated several security-related libraries". [S-LIB-49] DOC

## 8. Release history items (Windows) relevant to this scope

All entries come from search extracts of S-LIB-49, S-LIB-50, and S-LIB-03. Dates differ slightly from the Mac pages.

| Version | Date | Item | Class |
|---|---|---|---|
| 2022.1.1 | 9 Aug 2022 | WebP support [S-LIB-30] | DOC |
| 2022.1.7 | 2022 | Library browse by day/month/year restored | DOC |
| 2024.1.2 | 25 Apr 2024 | Default file type for drag-out from tray/Library (Advanced tab) [S-LIB-03] | DOC |
| 2024.2.0 | 2024 | Very High quality: 4K/1080p up to 60 fps [S-LIB-27] | DOC |
| 2024 | — | Combine Videos / Add Videos [S-LIB-23] | DOC |
| 2025.0.0 | 12 Feb 2025 | Step Capture; subscription-only licensing [S-LIB-50, S-LIB-44] | DOC |
| 2025.3.0 | 6 Aug 2025 | Step Capture fixes | DOC |
| 2025.4.0 | 23 Oct 2025 | Share images to Camtasia Online | DOC |
| 2025.4.3 | 11 Jun 2026 | Fix crash generating a Library thumbnail | DOC (date odd) |
| 2026.0.0 | 14 Jan 2026 | Improved OCR for find & edit text; rebrand "TechSmith Snagit" to "Camtasia Snagit" | DOC |
| 2026.1.0 | 13 Mar 2026 | "Open In" setting on Video tab replaces "Preview in Editor" | DOC (via S-LIB-57 mirror) |
| 2026.1.1 | 1 Apr 2026 | Performance of Step Capture and large thumbnails in the Library | DOC |
| 2026.2.0 | 30 Apr 2026 | Share video to Camtasia Online; Email with classic and New Outlook; theme follows Windows; Dropbox sign-in crash fix | DOC |
| 2026.2.2 | 28 May 2026 | Camtasia Editor subscribers can activate Snagit | DOC |
| 2026.3.0 | 16 Jul 2026 | Step Capture to Camtasia Online; OneDrive/SharePoint/Teams use Windows auth; Step Capture image-count badge in tray/Library; Step Capture Library category; security library updates | DOC |
| 2026.3.1 | 23 Jul 2026 | Images to Camtasia Online as .snagx; interval delay minimum 1 to 3 s; French Share-dialog text fix | DOC |
| 2026.4.0 | 6 Oct 2026 | Record to Camtasia Online; Microsoft OCR as default; X destination removed; fixes for presets not respecting the chosen microphone and for Step Capture images disappearing when grouped | DOC |

## 9. Background: Windows drag-and-drop data formats (Microsoft)

- Existing files should be offered as **CF_HDROP**, and drop targets that accept files must handle it. [S-LIB-55] DOC
- Virtual files (data not yet on disk) use **CFSTR_FILEDESCRIPTOR** (names and attributes) plus **CFSTR_FILECONTENTS** (one per file, selected by `FORMATETC.lindex`). The source can supply the contents as HGLOBAL or IStream. [S-LIB-54, S-LIB-56] DOC
- Raymond Chen's series shows a virtual-file drag landing in Explorer and as an Outlook attachment. [S-LIB-56] DOC (semi-official)
- Parity implication (INFER): to match Snagit's "preferred drag file type", the open-source app should render the chosen format to a temp file and offer CF_HDROP (best compatibility with Office, browsers, and Explorer). It can optionally add FILEDESCRIPTOR/FILECONTENTS and a CF_DIB/PNG bitmap for apps that accept image data.

## Unresolved questions

1. What exact data formats does Snagit place on the drag data object: CF_HDROP of a temp file, virtual file, or bitmap? Where are the temp files and how are they cleaned up? (Not documented.)
2. What is the full current Windows Save As list ("and others")? Is HEIF/HEIC supported on Windows? Is there any SVG export? (Evidence suggests no SVG.)
3. Does deleting from the Library go to the Windows Recycle Bin? Is there any in-app trash?
4. Is there any Windows equivalent of Mac Favorites (flags)? Are user-defined folders available in the 2022+ Library?
5. Is there a countdown before video recording on Windows, and is it configurable?
6. Offline activation for 2025+ subscriptions: S-LIB-42 and S-LIB-43 conflict.
7. What are the MSI/registry names for disabling update checks, sign-in prompts, and specific share destinations? (They are in the 2026 MSI guide registry section, which was not readable.)
8. How long is the export window after a subscription expires (S-LIB-14)?
9. Is there a current (2025/2026) Snagit VPAT?
10. What is the SNAGX internal structure? (Non-official ZIP claim only; no TechSmith spec.)
11. Can the tray be resized or collapsed by dragging in current versions? What are the tray tooltips?
12. What is the tray-clearing preference label, and what does Snagit do with unsaved (autosave-off) captures when the Editor closes?

To close most of these, allow `support.techsmith.com`, `www.techsmith.com`, `assets.techsmith.com`, `download.techsmith.com`, and `learn.microsoft.com` in the session network settings, then re-fetch S-LIB-01/02/04/28/32/34/37/49.
