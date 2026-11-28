<!-- Research notes compiled 2026-11-25 for Gate A. Evidence: search-engine extracts of official pages only; see docs/SOURCE-REGISTRY.md. -->
# Snagit for Windows: Editor, Tool Properties, Effects, Shortcuts, Release History

Feature-parity research note. It records functional behaviour only, with no code, assets or branding.

- Research date: 2026-11-25
- Scope: Snagit Editor tools, tool properties and Quick Styles, effects, keyboard shortcuts and accessibility, and 2025/2026 release history, on Windows. Mac differences are noted only where the sources mention them.

## Access method and its limits (read first)

- **Direct fetch was blocked.** WebFetch and curl to `www.techsmith.com`, `support.techsmith.com`, `assets.techsmith.com` and `download.techsmith.com` all failed with DNS/ENOTFOUND or HTTP 000 because the hosts are outside the session's network allowlist.
- **No source page was opened directly.** Every finding below comes from WebSearch result extracts: the search tool's summaries of official TechSmith pages, restricted to TechSmith domains.
- **Quoted text is not verified verbatim.** Text in "quotes" is the wording the search extract presented as page text. Re-check it against the live page before using it as a verbatim citation.
- **To upgrade this evidence,** allow `www.techsmith.com` and `support.techsmith.com` in the session network settings and re-fetch.

### Evidence classes

- **DOC:** Stated by an official TechSmith page, as surfaced in a search extract. All DOC findings here are therefore "DOC (search extract)".
- **INFER:** A reasonable inference from DOC findings.
- **UNVERIFIED:** Conflicting, old-version-only, or not confirmed.

## Sources

All sources were accessed on 2026-11-25. **Access method for every source: search extract only.**

| ID | Title | URL | Version applies to (as stated) |
|---|---|---|---|
| S-ED-01 | Snagit Windows 2026 Version History | https://support.techsmith.com/hc/en-us/articles/42674936732685-Snagit-Windows-2026-Version-History | 2026.0.0 to 2026.4.0 |
| S-ED-02 | Snagit Windows 2025 Version History | https://support.techsmith.com/hc/en-us/articles/39102293598349-Snagit-Windows-2025-Version-History | 2025.0.0 to 2025.4.3 |
| S-ED-03 | Snagit Mac 2026 Version History | https://support.techsmith.com/hc/en-us/articles/41975263481613-Snagit-Mac-2026-Version-History | Mac 2026.x (used for contrast only) |
| S-ED-04 | What's new in Snagit? | https://www.techsmith.com/snagit/whats-new/ | Mixed; per-item version tags |
| S-ED-05 | How to Set and Save Default Tool Properties in Snagit | https://support.techsmith.com/hc/en-us/articles/45790491793165-How-to-Set-and-Save-Default-Tool-Properties-in-Snagit | Not stated in extract |
| S-ED-06 | Quickly Access Favorite Styles and Tools | https://www.techsmith.com/learn/tutorials/snagit/quick-styles-and-favorites/ | Current |
| S-ED-07 | Snagit Themes | https://www.techsmith.com/learn/tutorials/snagit/snagit-themes/ | Import requires Win 13.1+ |
| S-ED-08 | Blur Tool | https://www.techsmith.com/learn/tutorials/snagit/blur/ | Smart Redact: Win 2025.0.0+; properties panel: Win 2026.1.0+ |
| S-ED-09 | Hide Sensitive Information in an Image | https://www.techsmith.com/learn/tutorials/snagit/hide-sensitive-information/ | Current |
| S-ED-10 | Automatically Redact Data in Screenshots (Smart Redact feature page) | https://www.techsmith.com/snagit/features/smart-redact/ | Marketing page |
| S-ED-11 | Step Tool | https://www.techsmith.com/learn/tutorials/snagit/step/ | Select All: Win 2026.1.0+ |
| S-ED-12 | Grab & Edit Text | https://www.techsmith.com/learn/tutorials/snagit/edit-image-text/ | Current |
| S-ED-13 | Snagit Grab Text & Edit Text Best Practices | https://support.techsmith.com/hc/en-us/articles/115002031592-Snagit-Grab-Text-Edit-Text-Best-Practices | Older article (about 876 days per extract) |
| S-ED-14 | Issues With Grab Text or Other OCR Functions Such as Edit Text or Smart Redact | https://support.techsmith.com/hc/en-us/articles/45409168722445-Issues-With-Grab-Text-or-Other-OCR-Functions-Such-as-Edit-Text-or-Smart-Redact | 2026.1.1+, 2025.4.2+, 2024.3.7+ |
| S-ED-15 | Move Tool | https://www.techsmith.com/learn/tutorials/snagit/move/ | Current |
| S-ED-16 | Selection Tool | https://www.techsmith.com/learn/tutorials/snagit/selection-tools/ | Current |
| S-ED-17 | Magic Wand Tool | https://www.techsmith.com/learn/tutorials/snagit/magic-wand/ | Current |
| S-ED-18 | Remove a Background | https://www.techsmith.com/learn/tutorials/snagit/remove-background/ | Current |
| S-ED-19 | Background Remover (feature page) | https://www.techsmith.com/snagit/features/background-remover/ | Marketing page |
| S-ED-20 | Simplify Images | https://www.techsmith.com/learn/tutorials/snagit/simplify/ | Win 2026.1.0+ |
| S-ED-21 | Add Effects | https://www.techsmith.com/learn/tutorials/snagit/effects/ | Current |
| S-ED-22 | Snagit Hotkeys Guide | https://www.techsmith.com/learn/tutorials/snagit/snagit-hotkeys/ | Current (updated about 117 days ago per extract) |
| S-ED-23 | Snagit Shortcuts Guide, printable (Win), PDF | https://www.techsmith.com/learn/wp-content/uploads/2024/07/Snagit-Shortcuts-Guide-Printable-Win.pdf | 2024 |
| S-ED-24 | Customize the Snagit Toolbar | https://www.techsmith.com/learn/tutorials/snagit/customize-toolbar/ | Current |
| S-ED-25 | Arrow Tool | https://www.techsmith.com/learn/tutorials/snagit/arrow/ | Current |
| S-ED-26 | Line Tool (Windows) | https://www.techsmith.com/learn/tutorials/snagit/line/ | Win 2026.1.0+ for Select All |
| S-ED-27 | Callout Tool | https://www.techsmith.com/learn/tutorials/snagit/callout/ | Win 2026.1.0+ for Select All |
| S-ED-28 | Text Tool | https://www.techsmith.com/learn/tutorials/snagit/text/ | Win 2026.1.0+ for Select All |
| S-ED-29 | Shape Tool | https://www.techsmith.com/learn/tutorials/snagit/shape/ | Win 2026.1.0+ for Select All |
| S-ED-30 | Highlighter Tool | https://www.techsmith.com/learn/tutorials/snagit/highlighter/ | Win 2026.1.0+ |
| S-ED-31 | Magnify Tool | https://www.techsmith.com/learn/tutorials/snagit/magnify/ | Current |
| S-ED-32 | Spotlight Tool (Mac) | https://www.techsmith.com/learn/tutorials/snagit/spotlight/ | Mac; used for the Windows contrast |
| S-ED-33 | Crop Tool | https://www.techsmith.com/learn/tutorials/snagit/crop/ | Current |
| S-ED-34 | Cut Out Tool | https://www.techsmith.com/learn/tutorials/snagit/cut-out/ | Current |
| S-ED-35 | Resize, Rotate, or Flip an Image | https://www.techsmith.com/learn/tutorials/snagit/resize-rotate-flip-image/ | Current |
| S-ED-36 | Stamp Tool | https://www.techsmith.com/learn/tutorials/snagit/stamp/ | Current |
| S-ED-37 | Pen Tool | https://www.techsmith.com/learn/tutorials/snagit/pen/ | Current |
| S-ED-38 | Fill Tool | https://www.techsmith.com/learn/tutorials/snagit/fill/ | Current |
| S-ED-39 | Eraser Tool | https://www.techsmith.com/learn/tutorials/snagit/eraser/ | Current |
| S-ED-40 | Remove Parts of an Image | https://www.techsmith.com/learn/tutorials/snagit/remove-image-parts/ | Current |
| S-ED-41 | Guide for Using Snagit Accessibly | https://www.techsmith.com/learn/tutorials/academy/guide-for-using-snagit-accessibly/ | Current |
| S-ED-42 | Snagit 2025 VPAT 2.5 Int (PDF) | https://assets.techsmith.com/docs/Snagit_2025-VPAT-2.5-Int.pdf | Snagit 2025 |
| S-ED-43 | Snagit 2024 VPAT 2.5 (PDF) | https://assets.techsmith.com/docs/Snagit-2024-VPAT-2.5.pdf | Snagit 2024 |
| S-ED-44 | Save Capture Settings as Presets | https://www.techsmith.com/learn/tutorials/snagit/presets/ | Current |
| S-ED-45 | Snagit Help 2024 (PDF) | https://www.techsmith.com/learn/wp-content/uploads/2025/01/Snagit-Help-2024-EN.pdf | Snagit 2024 |
| S-ED-46 | Snagit Help 10.0 / 11.x (legacy PDFs) | https://assets.techsmith.com/docs/pdf-snagit/Snagit-10-Help.pdf ; https://assets.techsmith.com/docs/pdf-snagit/Snagit-11-Help.pdf | Snagit 10/11 (2010 to 2012), legacy only |
| S-ED-47 | Snagit Preferences - Windows | https://www.techsmith.com/learn/tutorials/snagit/preferences/ | Current |
| S-ED-48 | Camtasia Snagit Hidden Features (TechSmith blog) | https://www.techsmith.com/blog/snagit-hidden-features/ | Blog (official, not documentation) |
| S-ED-49 | Screentelligence (Trust Center) | https://www.techsmith.com/trust-center/screentelligence/ | Current |

Requested starting URLs that were searched but whose content did not surface in extracts:

- https://www.techsmith.com/snagit/features/
- https://www.techsmith.com/learn/tutorials/snagit/

The article ID for the default-tool-properties page differs. The URL given in the brief, `.../articles/45790530141069-...`, was unreachable. Search returned `.../articles/45790491793165-...` with the same title (S-ED-05).

## 1. Editor tools

### Toolbar, the More menu and customization

- Tools not on the main toolbar are found under the **More** dropdown. Tutorials repeatedly say "If it's hidden, check the More dropdown". Sources: S-ED-11, S-ED-20, S-ED-39. **DOC.**
- **Customize Toolbar:** "In the Editor toolbar, select More > Customize Toolbar". You drag items from the dialog onto the toolbar to add them and drag them off to remove them. You can drag a **Space** to separate items visually, and **Restore Defaults** resets the toolbar on Windows. Source: S-ED-24. **DOC.**
- **Items that can be placed on the toolbar** are not limited to tools. They include:
  - menu items such as Save, Duplicate and New from Clipboard;
  - share destinations such as Word, Microsoft Teams and Google Drive;
  - effects such as Border and Filters.

  Source: S-ED-24. **DOC.**
- **Toolbar labels:** Right-click the toolbar to switch between icons with text and icons only. Source: S-ED-24. **DOC.**
- **Layout change in 2025.3.0:** The Editor UI was updated "to show both Library and Editor options on the main toolbar". Source: S-ED-02. **DOC.**
- **Tool positions and shortcuts:** The first ten toolbar positions get Ctrl+1 to Ctrl+9 shortcuts, so the shortcut follows the position, not the tool. Source: S-ED-22. **DOC.**

### Move tool and Smart Move

- Smart Move is enabled "In the Tool Properties panel" on Windows (in the Actions panel on Mac). Once on, Snagit detects objects, including text, and makes them independently selectable and movable. Sources: S-ED-15, S-ED-40. **DOC.**
- A **Detail slider** adjusts "the granularity of objects Snagit identifies". Source: S-ED-15. **DOC.**
- **Editing detected text:** Double-click the detected text, type the new text, then click away. Source: S-ED-15. **DOC.**

### Selection tool

- **Shapes:** Rectangle, Ellipse, Polygon and Freehand.
  - Polygon: click to anchor each corner, then double-click to anchor the last corner.
  - Freehand: drag to draw an irregular shape.

  Source: S-ED-16. **DOC.**
- **Background Fill** is "an option to fill the area when the selection is moved or deleted". The options are:
  - **Transparent**, which reveals the canvas;
  - **Auto-Fill**, which "Automatically fills the area based on the content surrounding the selection";
  - on Windows only, two optimisation modes, including "Optimize for Speed" for solid-coloured content.

  Sources: S-ED-15, S-ED-16. **DOC.**
- **Ctrl+drag** a selection copies it instead of moving it. The copy flattens after it is moved, like a normal selection. Source: S-ED-46 (legacy). **UNVERIFIED** for the current version.
- **Right-click menu on a selection** includes **Edit text** and **Grab Text**. Sources: S-ED-12, S-ED-15. **DOC.**

### Magic Wand

- Selects by the colour at the click point. It works best on solid or slight-gradient backgrounds. Sources: S-ED-17, S-ED-18. **DOC.**
- A **Select slider** sets how much adjacent colour is included. Source: S-ED-16. **DOC.**
- **Global Color Selection** selects every instance of that colour in the image. Source: S-ED-16. **DOC.**
- **Background removal workflow:** set Background Fill to Transparent, click the colour, then press Delete. Source: S-ED-18. **DOC.**

### Fill tool

- Recolours the clicked pixel and neighbouring pixels of the same colour. Source: S-ED-38. **DOC.**
- **Eyedropper** samples a colour "from anywhere on the screen". **Transparent** fill reveals the canvas. Source: S-ED-38. **DOC.**
- **Tolerance slider:** "Set the Tolerance slider to 1 to replace pixels of the exact same color". Increase it to fill more area and decrease it to fill less. Source: S-ED-38. **DOC.**

### Background removal

- **Documented Windows methods:** the Fill tool with Transparent ("remove large areas of the background with just one click"), the Selection tool's Lasso/Freehand option, and the Magic Wand. Sources: S-ED-18, S-ED-19. **DOC.**
- **Mac has a right-click "Remove Background"** command. It handles non-solid backgrounds but "works best on images where there is a high level of contrast" and "may not preserve fine details … such as individual whiskers or hairs". Source: S-ED-18. **DOC (Mac).**
- **A Windows equivalent of the Mac command was not confirmed.** S-ED-04 lists an "AI Background Remover" in its AI feature list without a version tag. **UNVERIFIED** for Windows.
- **Transparency survives only in some formats:** CUR, GIF, ICO, PNG, TIF and SNAGX. Source: S-ED-18. **DOC.**

### Crop, Cut Out, Trim and Auto Trim

- **Crop** trims the outer edges. When you click the Crop button, Snagit deletes areas outside the highlighted region. Crop can also change the aspect ratio. Source: S-ED-33. **DOC.**
- **Cut Out** removes a vertical or horizontal band.
  - Straight-edge styles stitch the remaining sections together.
  - Other edge styles leave a gap of a fixed size that cannot be changed.
  - If the image already has edits, you must flatten first, which makes those edits permanent.

  Sources: S-ED-34, S-ED-40. **DOC.**
- **Cut Out on Mac** also has Quick Styles "Add Horizontal Space" and "Add Vertical Space". Source: S-ED-34. **DOC (Mac).**
- **Auto Trim:** The 2026.0.0 release notes mention a "renamed Auto Trim label". Source: S-ED-01. **DOC (search extract).** The only behaviour description found is legacy: "lets Snagit set the trim based on the solid color around the capture's edges". Source: S-ED-46. **UNVERIFIED** for the current version.

### Resize, rotate and flip

- **Resize Image:** open it from Image menu > Resize Image, or by clicking the image dimensions at the bottom of the canvas.
  - Units: percent, pixels, inches and centimetres.
  - An aspect-ratio lock is available.
  - On Windows, an **Advanced** section adds resolution and resampling options. The page warns that resampling can permanently degrade quality.
  - Vector objects must be flattened before resizing.

  Source: S-ED-35. **DOC.**
- **Resize Canvas** is separate from Resize Image. Content falling off the canvas is cropped. Dragging a canvas handle is also described. Source: S-ED-46. **UNVERIFIED** (legacy wording).
- **Rotate** is under Image > Rotate: clockwise, counter-clockwise, 180 degrees, or a custom angle. Source: S-ED-35. **DOC.**
- **Flip** is under Image > Arrange > Flip, horizontal or vertical. Flipping also flips text and annotations. Source: S-ED-35. **DOC.**
- **No Windows hotkeys** are listed for Trim, Resize or Rotate. Source: S-ED-22 (via the S-ED-35 search extract). **DOC.**
- **Rounded image corners:** drag the yellow diamond handle at the top-right of the image to round the corners of the whole capture. Source: S-ED-29 extract. **DOC.**

### Arrow and Line

- **Arrow properties:**
  - End Style dropdown for the tail or head style;
  - Start/End Size;
  - Line Style dropdown;
  - width, opacity and shadow.

  Source: S-ED-25. **DOC.**
- Windows has **"T"-shaped end styles**, and some end styles do not carry over between Windows and Mac. Source: S-ED-25. **DOC.**
- **Bezier Curve** checkbox (Windows): enable it, then drag the white handles. Mac's curve handles differ. Source: S-ED-25. **DOC.**
- **The Line tool is Windows-only;** its tutorial is titled "Line Tool (Windows)". Source: S-ED-26. **DOC.**
- **Dash patterns** are not described in the current documentation. Legacy Snagit 10 had a dash preset or custom option. Source: S-ED-46. **UNVERIFIED.**

### Shape

- Drag the **green handle** to rotate. Source: S-ED-29. **DOC.**
- **Properties:** line thickness, plus shadow colour, angle, distance, opacity and blur. Source: S-ED-29. **DOC.**
- **No corner-radius control** for the Shape tool appeared in the extracts. Source: S-ED-29. **UNVERIFIED.**

### Text and Callout

- **Text tool properties:**
  - Fill (letter interior colour);
  - Outline (colour around the letters; a line width of 0 removes the outline);
  - font, size and style.

  No shadow or background box appeared in the current extract. For a box with a background, border or arrow, use Callout. Source: S-ED-28. **DOC.**
- **Text shadow conflicts across versions.** The older 2024 help described a Text drop-shadow option. Source: S-ED-45. **UNVERIFIED** for the current version.
- **Callout tool:**
  - Choose a Quick Style, drag on the canvas, then type.
  - Shadow: a direction toggle plus advanced colour, angle (Windows only), distance, opacity and blur.
  - Font, font colour and size. Font style is Mac-only.
  - **Padding** and a **Shaded** option are Windows-only.
  - Rounded corners via a yellow radius handle are Mac-only.

  Source: S-ED-27. **DOC.**

### Step

- Click to place the first marker. Each further click adds the next label in the sequence. Source: S-ED-11. **DOC.**
- **Type** dropdown chooses the alphanumeric sequence style. Number (1, 2, 3) and letter (a, b, c) sequences are confirmed. Sources: S-ED-11, S-ED-48. **DOC.**
- **Restart Sequence** button restarts numbering or lettering for the next step. Source: S-ED-11. **DOC.**
- **Custom labels:** right-click a step and choose **Edit Value**. Source: S-ED-11. **DOC.**
- **Automatic renumbering is not documented.** Whether remaining steps renumber when one is deleted was not found. **UNVERIFIED.**

### Highlighter, Magnify and Spotlight

- **Highlighter** draws attention to an area "while keeping text or other objects visible underneath". Source: S-ED-30. **DOC.**
  - Shape options are rectangle, rounded corner rectangle, oval and polygon. The extracts conflict on platform: one tags them Mac-only, another states a Windows 2026.1.0+ requirement. **UNVERIFIED** for Windows.
- **Magnify tool:**
  - Drag to select the source area and resize it with the white handles.
  - Move the enlarged copy using the anchor point.
  - Properties: magnification percentage and outline Line Width.

  Source: S-ED-31. **DOC.**
- **Spotlight:**
  - On Mac it is a standalone tool with Color, Opacity and Blur. Source: S-ED-32. **DOC.**
  - On Windows it is an effect: select an area with the Selection tool, then Image > Effects > **Spotlight and Magnify** > Apply. Sources: S-ED-32, S-ED-21. **DOC.**
- **Highlight, Blur and Spotlight "only to text"** was added in Mac 2026.0.0. Source: S-ED-03. **DOC (Mac).** No Windows equivalent was found. **UNVERIFIED.**

### Blur, Pixelate and Redact (security limitation)

- **Blur tool types:** Blur, Pixelate and Solid shapes.
  - Pixelate "covers an area with large square pixels, and is used to mask faces or sensitive information".
  - Solid shapes "covers an area with a fully opaque shape, and this option is for redacting confidential information".

  Source: S-ED-08. **DOC.**
- **Limitation:** Blur and pixelation can be reversed by some third-party tools, so solid shapes are the safer choice. Sources: S-ED-08, S-ED-09. **DOC.**
- **Limitation:** Redactions can be removed unless the image is flattened. Right-click > **Flatten All**, or save to a flattened format (JPG, PNG, GIF or PDF). After flattening, "redactions and blurs cannot be edited further". Sources: S-ED-08, S-ED-09. **DOC.**

### Smart Redact (AI)

- **Location:** Blur tool, then **Smart Redact** in the Actions/Tool Properties area. Available in Windows 2025.0.0+. The current properties panel requires Windows 2026.1.0+. Sources: S-ED-08, S-ED-02. **DOC.**
- **Detected categories,** each toggleable: addresses, credit card numbers, dates, emails, faces, IP addresses, phone numbers, Social Security numbers and URLs. Sources: S-ED-10, S-ED-09. **DOC.**
- **Redaction style:** black solid shapes by default, switchable to blur or pixelate. Sources: S-ED-08, S-ED-09. **DOC.**
- **Scope and privacy:**
  - Still images only, not video. Source: S-ED-10. **DOC.**
  - Windows uses ABBYY and OpenCV. TechSmith states "neither the image nor the sensitive information in the image is uploaded to TechSmith or to any service provider". Source: S-ED-09/S-ED-10 (search extract). **DOC.**
  - INFER: it runs on-device and needs no internet connection for detection.
- **Option refinements:** 2025.1.0 "refined" Smart Redact and Blur tool options. 2026.3.0 improved Smart Redact accuracy when Optimized OCR is on. Sources: S-ED-02, S-ED-14. **DOC.**
- **Mac-only additions in 2026.0.0:** clicking text adds or removes redactions, and the right-click menu gained "Redact All Matches". What's New describes "automatically finding and removing similar content across your screenshots" (tagged 2026.0.0, November 2025). Sources: S-ED-03, S-ED-04. **DOC (Mac).** Windows availability is **UNVERIFIED**.

### Grab Text (OCR) and Edit Text

- **Grab Text** has two modes:
  - Whole image: right-click the image > Grab Text > **Copy All**.
  - Region: select an area with the Selection tool, right-click > Grab Text. A partial selection "can improve the accuracy".

  Source: S-ED-12. **DOC.**
- **Grab Text Results** dialog has a **Plain text** option on Windows that strips font and colour. Source: S-ED-12. **DOC.** Preservation of table or column layout is not documented. **UNVERIFIED.**
- **Edit Text:** Selection tool > drag tightly around the text > right-click > **Edit Text**, then type or delete. Font, size and colour can be changed in Tool Properties. Source: S-ED-12. **DOC.**
  - Tip: draw the selection tightly, because background objects inside the box can be distorted or blurred. Source: S-ED-12. **DOC.**
  - Font matching: TechSmith's blog says Snagit "will match the font as best as it can". This is a vendor claim. **UNVERIFIED** quality.
- **OCR engine history:**
  - Older articles: ABBYY OCR engine. Source: S-ED-13. **DOC (older).**
  - 2026.0.0 (dated 14 January 2026 in the extract): experimental "improved Optical Character Recognition (OCR) processing to find & edit text in images", exposed as **Optimized OCR**. Enable it via Edit > Editor Preferences > **Experiments** tab, after accepting the Experimental Feature Terms. Administrators can disable it. Sources: S-ED-01, S-ED-14, S-ED-47. **DOC.**
  - 2026.4.0 (6 October 2026): "Updated Snagit to use Microsoft Optical Character Recognition as the default OCR engine". Source: S-ED-01. **DOC.**
- **OCR languages:** The older support article lists English, German, French, Japanese and Spanish, "with the exception of Japanese Characters". Source: S-ED-13. **DOC (older; possibly stale after the Microsoft OCR switch).**
- **Limitation:** "The OCR library that is used in Snagit does not recognize text that is slanted." Source: S-ED-13. **DOC (older).**
- **Limitation (licensing):** "Grab Text, Edit Text, and Smart Redact functions within Snagit will not work without the update containing the refreshed license". The fix is to update to 2026.1.1+, 2025.4.2+ or 2024.3.7+, or to enable Optimized OCR. "Snagit 2023 and older will not be receiving updates." Source: S-ED-14. **DOC.**
  - INFER: OCR features depend on a time-limited third-party engine licence.

### Simplify (AI)

- Simplify tool (it may sit in More) > **Auto Simplify** in Tool Properties. It covers text and UI elements with simplified shapes. Requires Windows 2026.1.0+ for the documented steps. Source: S-ED-20. **DOC.**
- **Detail slider** sets how much is simplified. Changing it "resets all Auto Simplify objects on the canvas", so manual edits to those objects are lost. Source: S-ED-20. **DOC.**
- **Editing afterwards:** resize, move or delete objects, change palette colours, and add Quick Style shapes manually. Source: S-ED-20. **DOC.**
- **Older behaviour:** the 2021 help says objects are built from up to four colours in the image. Source: S-ED-46 family. **UNVERIFIED** for the current version.
- **Local vs. cloud is not documented.** S-ED-49 (Screentelligence) is ambiguous: one page says it analyses "metadata locally", another calls it SaaS. **UNVERIFIED.**

### Pen, Eraser and Stamp

- **Pen:**
  - Draws freehand lines. Shift constrains to horizontal or vertical.
  - "Create as vector" (Windows) must be enabled before drawing for the line to stay editable. "Lines drawn without this option cannot be edited."

  Source: S-ED-37. **DOC.**
- **Eraser:**
  - Drag to erase. Shift constrains the stroke to a straight line.
  - Properties: Width on both platforms; Opacity and Antialias on Windows only; Shape (circle or square) on Mac only.
  - Erased areas reveal the **canvas colour**, set via the Image menu. A transparent canvas shows a checkerboard.
  - For a content-aware fill, use Selection + Auto-Fill + Delete instead.

  Source: S-ED-39. **DOC.**
- **Stamp:**
  - Choose a category or search in Quick Styles, then click the canvas.
  - Stamps stay editable after placement.
  - More stamps can be downloaded from the Snagit Assets site.

  Source: S-ED-36. **DOC.**

## 2. Tool properties, Quick Styles and object management

### Quick Styles

- **Saving a Quick Style:** "1. In Snagit Editor, click to select a tool. 2. In the Tool Properties panel, edit the desired properties. 3. As you edit the properties, a temporary Quick Style appears below the existing styles. Click the Plus icon to save the new Quick Style." Sources: S-ED-05, S-ED-06. **DOC.**
- **No separate per-tool default setting:** saved Quick Styles act as the defaults or presets. Source: S-ED-05 (search extract summary). **DOC (search extract).** The exact article wording could not be read. **UNVERIFIED** as verbatim.
- **Deleting:** right-click a style > **Delete Quick Style**. Source: S-ED-06. **DOC.**
- **Favourites:** click the Favourite (star) icon on a Quick Style; it turns yellow when saved. The **Favorites** tool gathers favourited styles from different tools. Quick Effects favourites are Mac-only. Source: S-ED-06. **DOC.**

### Themes

- **A theme is** "A set of custom Quick Styles in Snagit based on a preselected color palette". You pick 4 to 8 colours (presets, palette, eyedropper, or HEX/RGB) and Snagit auto-generates Quick Styles. Source: S-ED-07. **DOC.**
- **Saving edits to a theme overwrites** the existing Quick Styles in that theme. Source: S-ED-07. **DOC.**
- **Export/import:** themes export as a **.snagtheme** file and are imported via the Theme menu (Windows). Importing requires Windows 13.1+. Source: S-ED-07. **DOC.**
- **Deployment limitation:** "Stamps, Quick Styles, and options in the toolbar are not stored in the registry file and cannot be deployed to multiple users". Source: Snagit 2018/2024 Deployment Tool Guide, via the S-ED-07 search extract. **DOC (deployment guide).**
- **Custom colours:** a theme's "My Color" palette stores custom colours (Windows 2018 help). Source: S-ED-46 family. **UNVERIFIED** for the current version.

### Colour controls

- The colour dropdown accepts typed values. A **Mode** button switches between HEX and RGB. An **Eyedropper** appears in the Fill, Highlighter and Callout properties. Sources: S-ED-38, S-ED-27, S-ED-30. **DOC.**
- **Gradients:** "My Gradients" (+ to save) is documented on a Mac-titled page ("Apply a Gradient (Mac)"). **UNVERIFIED** on Windows.

### Select All of a kind (2026.1.0)

- 2026.1.0 (13 March 2026): "Users can now select and customize all like objects, such as step numbers, text, arrows, etc, with a single Select All button in the Tool Properties panel". It is documented for Line, Step, Text, Highlighter, Callout, Arrow, Stamp and Shape. Sources: S-ED-01, S-ED-11, S-ED-25 to S-ED-29. **DOC.**

### Multi-select, grouping and layering

- **Multi-select:** Shift+click adds objects to the selection, and a right-click "Select All Objects" also exists. Source: S-ED-46. **UNVERIFIED** (legacy). Ctrl+A is Select All. Source: S-ED-22. **DOC.**
- **Grouping and layering shortcuts** (Windows): Ctrl+Shift+G Group, Ctrl+Shift+U Ungroup, Ctrl+Shift+F Bring Forward, Ctrl+Shift+B Send Backward. Source: S-ED-22. **DOC.**
- **Flatten:** "Right-click an object and select Flatten to permanently make an object part of the background … After you flatten an object, you cannot select and move or modify the object." Source: S-ED-22/S-ED-45 extract. **DOC.**
  - The flatten shortcut conflicts between sources: Ctrl+T in S-ED-22, while Ctrl+Shift+T is "Flatten All" in S-ED-23. INFER: Ctrl+T flattens the selected object and Ctrl+Shift+T flattens all. **UNVERIFIED.**
- **Annotation anchoring (2025.2.0):** annotations such as Callout and Arrow stay anchored to template sections when those sections are moved or deleted. Source: S-ED-02. **DOC.**

### Copy, paste, duplicate, undo and redo

- **Shortcuts:**
  - Ctrl+C, Ctrl+X, Ctrl+V;
  - **Copy All**, Ctrl+Shift+C, which copies the flattened image for pasting elsewhere;
  - Ctrl+Z Undo, Ctrl+Y Redo.

  Source: S-ED-22. **DOC.**
- **Duplicate** is available as a toolbar-customisation menu item. Source: S-ED-24. **DOC.**
- **Pasting objects between captures** while keeping them editable is not documented. **UNVERIFIED.**

### Vector vs. raster

- Objects stay editable until flattened. Pen strokes are raster unless "Create as vector" is on.
- These operations require flattening: Cut Out, Resize Image, and Perspective (legacy note).

Sources: S-ED-34, S-ED-35, S-ED-37, S-ED-46. **DOC/INFER.**

## 3. Effects

- **Effects panel types** (current "Add Effects" tutorial): Border, Edges, Capture Info, Page Curl (Windows), Shadow, Perspective, Reflection (Mac), Watermark (Windows), Color Adjustment, Filters (Windows), and Color Filters (Mac). Source: S-ED-21. **DOC.**
- **Border:** colour and width slider, then Apply. Source: S-ED-21. **DOC.**
- **Edges:**
  - Choose a style from the dropdown and set the size.
  - Click one or more edges to choose where the effect applies.
  - Edge shadow options are Windows-only.

  Source: S-ED-21. **DOC.** Legacy style names were drop shadow, torn, faded, wave, saw, shark tooth and beveled. Source: S-ED-46. **UNVERIFIED** for the current list.
- **Shadow:** colour, a position grid and size. Mac adds opacity, offset and blur sliders. Source: S-ED-21. **DOC.**
- **Filters** (Windows): Soften, Sharpen, Opacity, Monochrome, Grayscale and Invert Colors, applied to the whole image. Source: S-ED-21. **DOC.** Legacy filters such as Edge Detect and Pixelate were under Image > Modify. Source: S-ED-46. **UNVERIFIED.**
- **Perspective:** choose a style, then adjust the Horizontal/Rotation and Vertical/Tilt sliders. Source: S-ED-21. **DOC.** A legacy note says applying Perspective flattens the capture. **UNVERIFIED.**
- **Page Curl** (Windows): curls one corner. Choose a style, then Apply. Source: S-ED-21. **DOC.**
- **Watermark** (Windows): text, logo or copyright. You can import an image and set position and scale. Two modes: **Underlay** (embossed into the background) or **Overlay**. Source: S-ED-21. **DOC.**
- **Spotlight and Magnify** (Windows effect): applies to an area selected with the Selection tool. Sources: S-ED-21, S-ED-32. **DOC.**
- **Capture Info:** listed as an effect type, but its fields were not described in the extracts. Sources: S-ED-21, S-ED-45. **UNVERIFIED** detail.
- **Colour replacement and brightness/contrast:** "Color Adjustment" is listed, but its specific controls were not surfaced. Source: S-ED-21. **UNVERIFIED** detail.
- **Transparency:** an Opacity filter exists. Formats that keep transparency are listed under Background removal above. Sources: S-ED-21, S-ED-18. **DOC.**
- **Effects favourites:** "click the Favorite icon for Effects Quick Style to save the effect to Favorites". Source: S-ED-27/S-ED-21 extract. **DOC.** The page notes that the Quick Effects favourites option is Mac-only (S-ED-06), so Windows behaviour is **UNVERIFIED**.
- **Effects in capture presets:** presets can "automatically add effects such as borders or capture information". Effects apply to image captures only. Sources: S-ED-44, S-ED-45. **DOC.** The step-by-step for configuring this was not found.
- **Batch effects:** not found in the extracts. Possibly covered by Library batch conversion, which is in another agent's scope. **UNVERIFIED.**
- **Effect scope:** effects apply to the whole capture, not a selection, except Spotlight and Magnify. Source: S-ED-46 legacy. **UNVERIFIED/INFER.**

## 4. Keyboard shortcuts, context menus and accessibility

### Editor shortcuts (Windows, current Hotkeys Guide)

All rows are from S-ED-22 and are **DOC** unless marked otherwise.

| Action | Shortcut |
|---|---|
| Select the tool in toolbar position 1 to 9 | Ctrl+1 to Ctrl+9 |
| Undo / Redo | Ctrl+Z / Ctrl+Y |
| Cut / Copy / Paste | Ctrl+X / Ctrl+C / Ctrl+V |
| Copy All | Ctrl+Shift+C |
| Select All | Ctrl+A |
| Save / Print / New Image | Ctrl+S / Ctrl+P / Ctrl+N |
| Group / Ungroup | Ctrl+Shift+G / Ctrl+Shift+U |
| Bring Forward / Send Backward | Ctrl+Shift+F / Ctrl+Shift+B |
| Flatten | Ctrl+T (S-ED-22); Flatten All Ctrl+Shift+T (S-ED-23). The two sources conflict. |
| Zoom In / Zoom Out / Actual Size | Ctrl+Plus / Ctrl+Minus / Ctrl+0 |
| Constrain Pen or Eraser to horizontal/vertical | Hold Shift (S-ED-37, S-ED-39) |
| Screen Draw mode | Ctrl+Shift+D toggles it; A = arrow, S = square, 1 = step |

- The Screen Draw keys (last row) come from the search extract and may apply to the capture/screen-draw context, not the Editor. **UNVERIFIED** context.
- **Customisation:** "Hotkeys can be changed in Snagit's preferences, and Restore Defaults resets them". Source: S-ED-22. **DOC.** The legacy path File > Editor Options > Customize > Keyboard Shortcuts is **UNVERIFIED** for the current version.

### Context menus (documented items only)

- Right-click an image: **Grab Text**. Source: S-ED-12.
- Right-click a selection: **Edit Text** and **Grab Text**. Sources: S-ED-12, S-ED-15.
- Right-click an object: **Flatten**. Right-click the canvas: **Flatten All**. Sources: S-ED-08, S-ED-22.
- Right-click a step: **Edit Value**. Source: S-ED-11.
- Right-click a Quick Style: **Delete Quick Style**. Source: S-ED-06.
- Right-click the toolbar: icons with text vs. icons only. Source: S-ED-24.
- Right-click the title bar (2025.4.0): a system menu for window position and size. Source: S-ED-02.

All items above are **DOC**.

### Accessibility

- **Keyboard navigation:** Capture and Editor support Tab, Shift+Tab and the arrow keys, with visible focus. Source: S-ED-41. **DOC.**
- **2025 VPAT limitations:**
  - Keyboard-only users "cannot execute the functions of the selected toolbar items".
  - A keyboard trap exists between the drawing toolbar and properties.
  - Options in capture popups are not keyboard accessible.

  Source: S-ED-42. **DOC.**
- **2024 VPAT limitations:**
  - Canvas work "require[s] the use of the mouse".
  - Share menu selections are not screen-readable.

  Source: S-ED-43. **DOC.**
- **Accessibility release items:**
  - 2025.1.0: labels on tool-property controls for screen readers.
  - 2025.4.0: screen reader support for the Learning Center videos.
  - Tool-property sliders adjustable with the arrow keys (the version was not given in the extract).
  - 2026.x: Create Image from Template and other Editor dialogs updated for screen readers and keyboard.
  - 2026.3.0: interface colours and icons updated "for improved accessibility".

  Sources: S-ED-01, S-ED-02. **DOC.**

## 5. Release history (editor, effects and AI items)

### Windows 2026 (source: S-ED-01 unless noted)

| Version | Date (per extract) | Editor-relevant items | Class |
|---|---|---|---|
| 2026.0.0 | 14 Jan 2026 | Experimental improved OCR "to find & edit text in images" (Optimized OCR). Renamed Auto Trim label. Spell-checker entries for product names. Bug fixes. | DOC |
| 2026.1.0 | 13 Mar 2026 | **Select All** of like objects in Tool Properties. Step Capture guides export to PowerPoint with editable text. Faster thumbnails. | DOC |
| 2026.1.1 | ~1 Apr 2026 (the extract conflicts with a "May 1, 2026" licence date) | Refreshed OCR licence (S-ED-14) | DOC/UNVERIFIED date |
| 2026.2.0 | 30 Apr 2026 | System colour-theme option. Classic and new Outlook sharing. Capture preset to open recordings in Camtasia. | DOC |
| 2026.3.0 | 16 Jul 2026 | Capture and Editor interface colours and icons refreshed for accessibility. Email sharing from the Editor menu restored. Smart Redact accuracy improved with Optimized OCR. | DOC |
| 2026.3.1 | 23 Jul 2026 | Images sent to Camtasia Online as .snagx. OpenSSL update. Privilege-escalation fix. | DOC |
| 2026.4.0 | 6 Oct 2026 | **Microsoft OCR becomes the default OCR engine.** Record to Camtasia Online. Standard/Delayed update timing. | DOC |

- Snagit 2026.0.0+ requires Windows 11 24H2+ (from the extract of the Windows requirements page). **DOC (search extract).**

### Windows 2025 (source: S-ED-02)

| Version | Date (per extract) | Editor-relevant items | Class |
|---|---|---|---|
| 2025.0.0 | 12 Feb 2025 | **Smart Redact** (Blur tool) and **Step Capture** introduced. Background noise removal is for video audio only. | DOC |
| 2025.1.0 | Mar 2025 | Screen-reader labels on tool-property controls. Smart Redact and Blur options refined. | DOC |
| 2025.2.0 | Jun 2025 | Annotations stay anchored to template sections when those sections are moved or deleted. | DOC |
| 2025.3.0 | 6 Aug 2025 (Windows page; Mac notes say 21 Aug) | Main toolbar shows both Library and Editor options. | DOC |
| 2025.4.0 | 23 Oct 2025 | Title-bar system menu. Learning Center screen reader support. Crash fixes for large or multi-page images. | DOC |
| 2025.4.1 | 23 Mar 2026 | Editor crash fix on certain hardware. | DOC |
| 2025.4.2 | — | Refreshed OCR licence (S-ED-14) | DOC |
| 2025.4.3 | 11 Jun 2026 | Library and .snagx crash fixes. | DOC |

- **AI feature list** on What's New: Background Remover, Image Simplifier, Smart Redact and Step Capture. Per-version introduction is not tagged except Smart Redact (2025.0.0). Source: S-ED-04. **DOC.**
- **A 2027 version-history line may already exist.** The search extract said the support site lists 2027 history alongside 2026. **UNVERIFIED;** not examined.

## Documented limitations (summary)

1. **Blur and pixelate are reversible** by some third-party tools. Use solid shapes and flatten before sharing. Sources: S-ED-08, S-ED-09.
2. **Unflattened redactions can be removed** by the recipient of a .snagx file. INFER from S-ED-08.
3. **OCR does not read slanted text.** The older language list covers EN, DE, FR, JA (with exceptions) and ES. Source: S-ED-13.
4. **OCR-based features stop working without the licence-refresh update.** This affects Grab Text, Edit Text and Smart Redact. Source: S-ED-14.
5. **Smart Redact is still-images only.** TechSmith states the image is not uploaded. Source: S-ED-10.
6. **Simplify's Detail slider discards edits.** Changing it resets the Auto Simplify objects and loses manual changes. Source: S-ED-20.
7. **Cut Out, Resize Image and (legacy) Perspective require or cause flattening.** Sources: S-ED-34, S-ED-35, S-ED-46.
8. **Pen strokes are not editable** unless "Create as vector" was on. Source: S-ED-37.
9. **Experimental features can be disabled** by an administrator and "may radically change or disappear at any time". Source: S-ED-47.
10. **Many properties are platform-specific.**
    - Windows-only: Line tool, Bezier on Arrow, Callout padding and Shaded, Eraser opacity and antialias, Page Curl, Watermark, Filters.
    - Mac-only: Spotlight tool, Reflection, Callout corner radius, Remove Background command.

    Sources: S-ED-21, S-ED-25, S-ED-27, S-ED-32, S-ED-39.
11. **Accessibility gaps:** canvas editing is mouse-dependent, and there is a toolbar/properties keyboard trap. Sources: S-ED-42, S-ED-43.

No source documented whether any Windows AI feature requires an internet connection or a TechSmith account sign-in. See the unresolved questions.

## Unresolved questions

1. **Default tool properties wording.** Does S-ED-05 describe a real "set as default" action, or only Quick Styles? The brief's URL ID (…45790530141069) was unreachable; search returned …45790491793165.
2. **Windows AI background remover.** Does Windows have a one-click AI "Remove Background" equivalent to Mac's? In which version was it added?
3. **Mac 2026.0.0 Smart Redact features on Windows.** Is Smart Redact "Redact All Matches" / click-to-toggle available on Windows, and from which version? The same question applies to Highlight, Blur and Spotlight "text only".
4. **OCR languages after the switch.** What is the current OCR language list after the 2026.4.0 switch to Microsoft OCR? Does it follow the Windows OCR language packs (INFER)?
5. **Internet and sign-in requirements.** Do Simplify, Smart Redact, Optimized OCR or the Background Remover need internet or sign-in on Windows? Screentelligence sources conflict between local and SaaS processing.
6. **Exact flatten shortcuts:** Ctrl+T vs. Ctrl+Shift+T.
7. **Current Edges style list,** plus the controls inside Color Adjustment (brightness, contrast, colour replacement) and the fields of Capture Info.
8. **Batch-applying effects to several images** (Library batch convert): handled by the other agent.
9. **Undocumented object behaviour:** step auto-renumbering on delete, Shape corner radius, dash patterns, a full opacity list per tool, and copying editable objects between captures.
10. **Whether Highlighter shape options exist on Windows 2026.1.0+;** the extracts conflict.
11. **Contents of a 2027 Windows version history,** if one exists.
