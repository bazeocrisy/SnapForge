# Redaction pipeline review

Date: 2026-11-25. Scope: annotations, clipboard, files, metadata, undo history, temporary files, memory.

## Two different things: editing vs. exported output

| | **While editing (reversible)** | **Exported image (irreversible)** |
|--|-------------------------------|-----------------------------------|
| What happens | Pixels in the redacted area are overwritten with black in the working image, and an opaque black block is placed above every annotation that existed at that moment | The PNG/JPEG file or clipboard image is produced from the redacted working image plus all annotations (including the black blocks) |
| Can it be undone? | **Yes** — Undo restores the original pixels and removes the block. The originals are held only in memory, inside the undo history | **No** — the file/clipboard image never contains the original pixels or the covered annotations; there is no hidden layer |
| When does it become permanent? | Never inside the editor | At the moment of Save or Copy |
| What is *not* changed | Anything you copied or saved **before** redacting | — |

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
8. **Discard** — when a new capture replaces the current one, or the app closes, the working image and every
   retained original-pixel block in the undo/redo history are zeroed and dropped.

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
| Discard wipes retained originals (undo and redo branches) | `EditorDocumentTests.Discard_*` |
| Redaction by real mouse drag covers pixels and earlier arrow | `InteractiveUiTests.Annotations_can_be_drawn_*` |

## Residual risks (outside SnapForge's control or not yet addressed)

* **Earlier exports**: a copy or file made before redacting keeps the original. Windows clipboard history
  (Win+V) and cloud clipboard sync may retain earlier clipboard items; the user must clear them.
* **Memory**: zeroing is best effort. WPF keeps its own copies of bitmaps shown on screen (unmanaged memory) and
  the .NET garbage collector may have moved/copied buffers; a memory dump taken while SnapForge runs could
  contain screen content.
* **Black box size reveals length** of the redacted text (inherent to box redaction).
* **Other screenshot tools** (Print Screen to OneDrive, Snipping Tool history) are outside SnapForge.
* Redaction color is fixed to black; blur/pixelate are intentionally not offered because they can be partially
  reversible.
