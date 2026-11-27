# IP, provenance and design review (SF-015)

**Not legal advice. No trademark, copyright, design-right or trade-dress clearance is claimed.** This document records
where SnapForge's names, artwork, fonts and UI design come from, what the design intends to be, and a checklist for
a **qualified reviewer** (trademark attorney or equivalent) to complete before public promotion. Prepared
27 November 2026 for release candidate 2. Related: `docs/LICENSING-REVIEW.md`, `docs/NAMING.md`,
`website/BRANDING.md`.

## 1. Asset provenance

| Asset | Where | Origin | Licence / status | Open points |
|---|---|---|---|---|
| App and site logo (orange aperture mark: corner brackets + aperture ring on navy) | `branding/snapforge-icon.svg`, `branding/snapforge-icon-small.svg`; rendered by `branding/make-icons.js` → `branding/png/`, packed by `branding/pack-ico.py` → `src/SnapForge.App/Assets/SnapForge.ico`; `website/static/assets/img/logo-mark.svg/.png`, `favicon.ico` | Hand-written SVG geometry in this repository (paths, circle and strokes visible in the source), following the owner's chosen concept from the supplied website kit | Project artwork, MIT repository | The supplied concept images (`website/design-references/*.png`) are generated references from the owner's kit; reviewer to confirm the kit's terms and that the mark does not resemble another camera/screenshot brand (see §4) |
| Website feature icons | `website/static/assets/icons/*.svg` | Hand-written 24×24 stroke SVGs in this repository (simple rectangles/paths) | Project artwork | Generic shapes; no icon library used |
| App UI glyphs (Region, Window, Screen, …) | `src/SnapForge.App/Themes/Controls.xaml` (`Geometry x:Key="Icon…"`) | Path data written in this repository | Project artwork | No icon font (no Segoe MDL2/Fluent Icons, Material, Font Awesome) referenced in source |
| Annotation stamps and callouts | Drawn in code (`AnnotationRenderer`/editor) | Original | Project code | — |
| Open Graph image | `website/static/assets/img/og-image.png` from `website/tools/og-image.html` | Rendered once in Chromium from the logo SVG and text | Project artwork | Text is rasterised with the first available of "Segoe UI", "Noto Sans" or the system sans-serif on the rendering machine (likely Noto Sans, SIL OFL 1.1, on Linux). A raster image of glyphs is not a font file; reviewer may confirm |
| Screenshots | `website/static/assets/screenshots/*.png`, `docs/screenshots/` | Real captures of SnapForge made by the CI screenshot tour with **fictional demo data** | Project | Some show the earlier rc.1 design (captioned on the site) |
| Website design concept images | `website/design-references/` | Supplied by the owner (generated) | **Not published** on the site | Kept for reference only |

## 2. Fonts

| Use | Fonts named | Shipped? | Licence position |
|---|---|---|---|
| App UI (WPF) | `Segoe UI Variable Text`, `Segoe UI` (`Themes/Controls.xaml` `UiFont`; overlay text `Segoe UI`) | **No** — Windows system fonts used at run time | Licensed to the Windows user with Windows; SnapForge redistributes no font files |
| Website text | `Segoe UI Variable Text/Display`, `Segoe UI`, `system-ui`, `-apple-system`, `Helvetica Neue`, `Arial` | **No** — CSS font stack; the visitor's own system fonts | No web fonts, no Google Fonts, no font files in the site |
| Website code | `Cascadia Mono`, `Consolas`, `SFMono-Regular`, `Menlo`, `monospace` | **No** | Same |
| Annotation/OCR text in the app | Windows system fonts chosen at run time | **No** | Same |

Result: **no font files are redistributed** by the app, the installer or the website.

## 3. Original design direction (rc.2)

The capture UI is meant to be recognisably SnapForge's own and **not** to imitate the look of any other screen
capture product. Intended direction for **QuickDock** (provisional name for the desktop widget) and the **capture
panel**:

- A compact **charcoal/navy rounded pill** (fully rounded ends) floating at a screen edge, with SnapForge's **orange
  aperture mark** as its only brand element.
- **Horizontal navigation**: one row of equal, outlined line-icon buttons with text labels/tooltips (Capture, Record,
  Editor, Library, Presets); orange used only for focus/active state and the mark.
- **No large circular capture button** and no big red/orange round "record" disc as the dominant element; capture is
  one button among the row, and the capture panel uses a horizontal mode switch plus
  compact option toggles.
- SnapForge's own palette (navy `#0b1220`, orange `#f97316`, slate greys), its own icon geometry (§1) and system
  fonts (§2).
- **SnapForge Studio** (provisional name) uses the same visual language: a three-way mode selector (screen only /
  screen + camera / camera only), camera and microphone toggles that are **off by default**.

Designers and reviewers should compare the final rc.2 screens against this list; screenshots of the rc.1 design on
the website are labelled "Earlier release-candidate design".

## 4. Checklist for a qualified reviewer

Names — **SnapForge**, **QuickDock**, **SnapForge Studio** (the last two are provisional):

- [ ] Trademark knock-out and full searches in the markets of intended distribution — at least USPTO (TESS/
      successor search), EUIPO (eSearch plus TMview for EU national offices), UKIPO, and WIPO Global Brand Database.
- [ ] Classes: **Nice class 9** (downloadable software), **class 42** (software design/SaaS-like services) and,
      if any support/donation services are offered, **class 35/36** as relevant. Include phonetic and visual
      variants ("Snap Forge", "SnapForge", "Snapforge"; "Quick Dock", "QuickDock"; "Studio" combinations).
- [ ] Common-law use: app stores (Microsoft Store, Apple, Google Play), GitHub/SourceForge projects, Product Hunt,
      domain names, and well-known screen-capture products with similar names or "Studio"/"Dock" feature names.
- [ ] Earlier findings in `docs/RESEARCH.md` §4 and `docs/NAMING.md` (possible conflicts recorded there) resolved
      or accepted by the owner in writing.
- [ ] Decide whether "QuickDock" and "SnapForge Studio" are kept, renamed, or used only descriptively.

Trade dress / design:

- [ ] Side-by-side comparison of QuickDock, capture panel, recording toolbar, Studio and the Editor with the most
      similar commercial and free screen-capture tools (layout, button shape and size, colour scheme, iconography,
      animations, wording). Record differences and any element that should change.
- [ ] Logo comparison against camera/screenshot/photo brands (aperture and corner-bracket motifs are common; check
      the combination, colours and word mark).
- [ ] Confirm that no third-party screenshots, icons, sounds, fonts or texts are included (this review found none —
      §1–2).
- [ ] Confirm the terms under which the supplied (generated) concept images were produced allow their use as design
      references, and that nothing from them is published.

Copyright / licences:

- [ ] Review `docs/LICENSING-REVIEW.md` open questions (H.264/AAC position, Inno Setup licence wording, camera capture).
- [ ] Confirm `THIRD-PARTY-NOTICES.md` and the SBOM match the shipped packages.

Sign-off: reviewer name, qualification, date, scope and result to be recorded here by the owner. Until then the
website states that no trademark registration or clearance is claimed.
