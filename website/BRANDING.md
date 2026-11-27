# SnapForge name and branding

## Owner decisions (27 November 2026)

| Decision | Status |
|---|---|
| **SnapForge is the official product name.** | Decided by the owner. Used on the website. |
| **The orange aperture logo is the official, target branding.** | Decided by the owner. Used on the website (`static/assets/img/logo-mark.svg`, `logo-mark.png`, `favicon.ico`, Open Graph image). |
| **Windows 11 x64 is the initial supported platform.** | Decided by the owner. Stated on the website. |

## Name: what has and has not been done

The owner's decision makes "SnapForge" the project's official name. That is a **project decision only**. It is not
a legal clearance:

* **No trademark has been registered** in any country, and none is claimed. The website does not use ™ or ®.
* **No trademark search or naming-clearance check has been completed.** The software repository's research
  (`docs/RESEARCH.md` §4 and `docs/NAMING.md` on the development branches) recorded possible conflicts with existing
  products and domains using similar names; those findings remain **unresolved**.
* Before wider promotion, a trademark search in the relevant markets (for example USPTO, EUIPO and UKIPO, plus app
  stores and domain names) is recommended, ideally reviewed by someone qualified. If it finds a conflict, the owner
  may need to reconsider the name.
* The website says: "SnapForge is the official name of this project … It is not a registered trademark, and no
  trademark registration or clearance is claimed."

The development branches still describe "SnapForge" as a *working name* (README, `docs/NAMING.md`). Those files are
not changed by this website PR; updating them to the owner's decision belongs in the software review branches.

## Logo

* **Target branding:** the orange aperture mark — orange corner brackets around an aperture (shutter) ring — on
  charcoal/navy, with the word mark "Snap" in white and "Forge" in orange. Source: the supplied website kit.
  Original artwork created for this project; not derived from any other product's assets.
* **Current app icon (unchanged):** an orange rounded square with white corner brackets and a dot
  (`design-references/current-app-icon.png`, from `src/SnapForge.App/Assets/SnapForge.ico` on the development
  branches).
* **App icon migration is a separate task** and is **not** part of this website PR. No software branch is modified
  here. When it is scheduled, it needs: a multi-size `.ico` (16, 20, 24, 32, 40, 48, 64, 256 px) redrawn for small
  sizes (the aperture lines need simplifying below 32 px), the notification-area icon, the widget/capture-panel
  glyphs, light/dark/high-contrast checks, and updated screenshots. Until then, website screenshots of the app show
  the current icon in the title bar; that is expected.

## Colours and type (website)

Charcoal/navy `#0b1220`, orange `#f97316` (text on white uses `#c2410c` for contrast), white, slate `#475569` /
`#e2e8f0`. Windows system fonts (Segoe UI Variable / Segoe UI) with system-ui fallbacks; no web fonts. All text
colour pairs are listed at the top of `static/assets/css/site.css` and checked for WCAG AA contrast by `build.py`.

## Supplied design references

`design-references/*.png` (concept board, hero, editor, recording toolbar, logo and icon references) are
generated visual references. They are **not** published on the site and must not be presented as real UI.
