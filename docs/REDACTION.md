# Redaction pipeline review

Date: 2026-11-25. Scope: annotations, clipboard, files, metadata, undo history, temporary files, memory.

## Two different things: editing vs. exported output

| | **Editable copy (reversible)** — Editor, Library, `.sfcap`, backups | **Exported image (irreversible)** — PNG/JPEG, clipboard |
|--|-------------------------------|-----------------------------------|
| What happens | The visible image shows black in the redacted area and an opaque block covers every earlier annotation; the **base image keeps the original pixels** | The output is produced from the visible image plus all annotations (including the blocks) |
| Can it be undone? | **Yes**, while editing *and after restarting*: the Library stores the base image and the redaction as an object | **No** — the file/clipboard image never contains the original pixels or the covered annotations |
| Where do originals live? | In memory and **on disk in the Library** (`base.sfpx`), and in any `.sfcap` export or backup ZIP made before sanitizing | Nowhere in the output |
| How to make it permanent in the Library | **Sanitize** (File → Sanitize, Library → Sanitize, Settings → Sanitize all) | Already permanent |
| What is *not* changed by sanitizing | Exports, `.sfcap` packages and backups made earlier; Windows clipboard history | — |

The UI warns about the editable state: a banner in the Editor, an "Unsanitized redactions" badge on Library tiles, a
count in Settings → Privacy, and a confirmation before exporting an unsanitized `.sfcap`.

The status bar states this when the Redact tool is chosen, and warns when a redaction changes after the image
was already copied or saved.

## Pipeline, step by step

1. **Capture** — the whole desktop is copied into memory once. After the selection is cropped, the full-desktop
   buffer is zeroed (unless the result *is* the full desktop). Nothing is written to disk.
2. **Redact** (`RedactCommand`) — computes the covering pixel rectangle (partially covered edge pixels are
   included), overwrites it with opaque black in the working image, and inserts a `RedactionMaskAnnotation`
   on top of the annotation stack. The mask cannot be selected, moved, recolored or deleted.
3. **Annotations after a redaction** are drawn above the black block (e.g. an arrow pointing at it). Annotations
   created before it are covered. An earlier annotation can still be selected and moved out from under the
   block by the user, which is a deliberate edit of their own content.
4. **Export** (`ImageExporter.Flatten`) — renders all annotations to a transparent layer and blends it over the
   redacted working image. Untouched pixels stay bit-identical; redacted pixels are exactly the fill color.
5. **Encode** — PNG (lossless) or JPEG (quality 92) from raw pixels. No EXIF, XMP, text chunks, user name or
   location are written. JPEG compression cannot reintroduce original content because it never had it.
6. **Save** — written to a temporary file in the destination folder, then renamed; on any failure the
   temporary file is deleted. No other temporary files are created.
7. **Copy** — the same flattened image is placed on the clipboard as a bitmap and as PNG.
8. **Library autosave** — the base image (with originals), the annotations (including redaction regions) and a
   thumbnail of the *visible* image are written atomically. Thumbnails never contain redacted originals.
9. **Sanitize** — burns redactions into the base image, rasterizes annotations hidden under a redaction into the image
   and removes them as objects (so hidden text is not left in `annotations.json`), clears undo history, regenerates
   the thumbnail, and overwrites the old `base.sfpx` and `thumb.sfpx` with zeros before replacing them.
10. **Close / evict** — document buffers in memory are zeroed when a capture is closed or evicted from the session
    cache; the full-desktop capture frame is zeroed right after cropping.

## Evidence (automated tests)

| Behaviour | Test |
|-----------|------|
| Redacted pixels exactly black in PNG export | `ExportTests.Redacted_pixels_are_exactly_black_in_the_export` |
| Earlier annotations covered, later ones visible | `ExportTests.Redaction_covers_annotations_drawn_before_it_but_not_after` |
| JPEG export of a redacted block contains no original content | `ExportTests.Clipboard_and_file_exports_share_the_redacted_flattened_image` |
| No PNG text/EXIF metadata; no JPEG EXIF/XMP; no user name | `ExportTests.Png_contains_no_text_or_exif_metadata`, `ExportTests.Jpeg_contains_no_exif_or_xmp_metadata` |
| Clipboard receives bitmap + exact PNG | `UiSmokeTests.Clipboard_receives_bitmap_and_png` |
| Save leaves no temporary files; failure leaves nothing | `ExportTests.Save_*` |
| Mask placement, unselectable, undo/redo | `EditorDocumentTests.Redaction_places_unselectable_mask_*` |
| Undo restores originals in session; overlapping redactions | `EditorDocumentTests.Redaction_permanently_changes_pixels_*`, `Overlapping_redactions_undo_in_order` |
| Discard wipes in-memory buffers | `EditorDocumentTests.Discard_*` |
| Sanitize removes originals and hidden annotation text from stored files; crash-safe | `CaptureLibraryTests.Sanitize_*`, `Interrupted_sanitize_*`, `LibraryWorkflowTests.Sanitize_*` |
| Reopened capture re-applies redactions; editable copy keeps originals (documented) | `CaptureLibraryTests.Reopened_annotations_*`, `LibraryWorkflowTests.New_capture_*` |
| Redaction by real mouse drag covers pixels and earlier arrow | `InteractiveUiTests.Annotations_can_be_drawn_*` |

## Residual risks (outside SnapForge's control or not yet addressed)

* **Unsanitized library captures keep original pixels on disk** by design (reversible editing). Anyone with access
  to the user profile, a backup ZIP or an exported `.sfcap` can recover them. Sanitize before sharing editable files.
* **Overwrite-before-delete is best effort**: SSD wear-levelling, copy-on-write file systems, Volume Shadow Copies,
  File History, cloud-synced folders and earlier backups can keep old file contents.

* **Earlier exports**: a copy or file made before redacting keeps the original. Windows clipboard history
  (Win+V) and cloud clipboard sync may retain earlier clipboard items; the user must clear them.
* **Memory**: zeroing is best effort. WPF keeps its own copies of bitmaps shown on screen (unmanaged memory) and
  the .NET garbage collector may have moved/copied buffers; a memory dump taken while SnapForge runs could
  contain screen content.
* **Black box size reveals length** of the redacted text (inherent to box redaction).
* **Other screenshot tools** (Print Screen to OneDrive, Snipping Tool history) are outside SnapForge.
* Redaction color is fixed to black. Since 1.0 the Editor also offers **Blur** and **Pixelate** as visual effects;
  they are labelled "not redaction" because blurred or pixelated text can sometimes be partially recovered. Use
  Redact for anything secret.
