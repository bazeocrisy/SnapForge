# Alternative product names — preliminary conflict research

Date: 2026-11-25. **No rename has been made.** "SnapForge" remains the working name until the owner approves a change.

## Why

"SnapForge" is already used by a screenshot editor (Chrome Web Store, snapforge.app) and an
AI screenshot tool on Product Hunt — the same product category (see [RESEARCH.md §4](RESEARCH.md#4-product-name-check-snapforge)).

## Method and its limits

One web search per candidate (exact quoted name plus "software"/"app"/"screenshot"), looking for
software products, apps and companies using the same or a confusingly similar name.
This is **not** a trademark clearance: no USPTO/EUIPO/UKIPO database search, domain registration check,
app-store-wide search or social-handle check was performed. A formal search by the owner (or counsel) is
required before adopting any name.

## Candidates (ranked, lowest apparent conflict first)

| # | Name | Exact-name hits found | Closest similar names found | Preliminary risk |
|---|------|-----------------------|-----------------------------|------------------|
| 1 | **Shotwright** | None | Shot Lister (film shot-list app), Straight Shooter (dry-fire training), Shooting Analyzer (sports) — different categories | Low |
| 2 | **Glintshot** | None | "Glint" apps: Glint – Video Effects Camera (iOS), Glint: Notification Light (Android), GlintGuard (device detector) | Low–medium (shared "Glint" prefix, one in photo/video) |
| 3 | **Pixelvane** | None | PixelVibe (AI image/design web app), Pixeluvo (photo editor for Windows/Linux), PixelOver (pixel-art tool) | Medium (imaging category, similar sound) |
| 4 | **Annotra** | None | Annotik (visual bug-reporting/QA tool), Annota (note-taking; also a medical transcription product), Annotare (iOS visual inspection logs) | Medium (Annotik is adjacent to screenshot markup) |
| 5 | **Clearmark** | None as a software product | ClearShot (Windows screenshot tool), CleanShot X (macOS screenshot app) | Medium–high (same category, similar sound) |

Also checked and set aside: **Snaplume** and **Capturo** (no hits, but crowded "Cap-/Snap-" screenshot naming
space: Capru, Capora, Snapture, SnapPro), **Stepmark** (StepMark: Pedometer Widget, iOS, April 2026).

## Search sources (as returned on 2026-11-25)

* Shotwright: [Shot Lister](https://apple.co/3I2Ueql), [Straight Shooter](https://apps.apple.com/app/id1587742987), [Shooting Analyzer](https://alternativeto.net/software/shooting-analyzer/about)
* Glintshot: [Glint – Video Effects Camera](https://apps.apple.com/us/app/-/id1589766090), [Glint: Notification Light](https://play.google.com/store/apps/details?id=ai.lazycode.glint), [GlintGuard](https://appgoblin.info/apps/com.glintguard.devicedetector)
* Pixelvane: [PixelVibe](https://sourceforge.net/app/pixelvibe/web-app/), [Pixelvibe review](https://www.softwareadvice.com.au/software/643128/Pixelvibe), [PixelOver](https://deakcor.itch.io/pixelover) (Pixeluvo appeared in the same results without a distinct URL)
* Annotra: [Annotik](https://www.capterra.com/p/10047727/Annotik/), [Annota (Product Hunt)](https://www.producthunt.com/products/annota/makers), [Annota (Software Advice)](https://www.softwareadvice.com.au/software/772914/Annota), [Annotare](https://apps.apple.com/us/app/annotare/id6748240456)
* Clearmark: [ClearShot alternatives](https://alternativeto.net/software/clearshot), [CleanShot X features](https://cleanshot.com/features?xs=1)
* Set aside: [Capru](https://apps.apple.com/app/id6792067380), [Capora](https://apps.apple.com/app/id6792840940), [Snapture](https://www.producthunt.com/products/snapture), [StepMark](https://appgoblin.info/apps/6759448480)

## Rename cost (when approved)

`SnapForge.Core/ProductInfo.cs`, `Directory.Build.props` (Product), `SnapForge.App.csproj` (AssemblyName),
`MainWindow.xaml` (title text), `SelectionOverlayWindow` title, icon, README/docs, LICENSE header unaffected.
