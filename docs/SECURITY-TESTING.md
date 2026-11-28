# Security testing report — 1.0.0-rc.1

What was executed, where, and the result. Environments: **SIM** platform-independent tests (Linux CI + local),
**WCI** GitHub Windows runner, **PC** physical PC (pending). See [THREAT-MODEL.md](THREAT-MODEL.md) for the threats.
Exact CI runs are listed in [RELEASE-CANDIDATE-HANDOFF.md](RELEASE-CANDIDATE-HANDOFF.md).

| Area | Test(s) | Env | Result |
|---|---|---|---|
| Redaction in exports | `Redacted_pixels_are_exactly_black_in_the_export`, `Clipboard_and_file_exports_share_the_redacted_flattened_image` | SIM/WCI | Pass |
| Redaction in drag-and-drop files | `Drag_data_offers_only_flattened_redacted_images_named_by_title_and_time` (real OLE drag) | WCI | Pass |
| Redaction in thumbnails (adversarial secret colour) | `Redacted_secret_colour_appears_in_no_thumbnail_or_flattened_output` | SIM | Pass |
| Redaction under image edits | `Region_effect_keeps_redactions_intact_and_does_not_leak_hidden_pixels`, `Resize_redaction_cover_hides_every_pixel_derived_from_hidden_content`, `Shrinking_the_canvas_clips_or_drops_redaction_masks` | SIM | Pass |
| Sanitization | `Sanitize_removes_original_pixels_from_storage_and_hidden_annotations`, `Sanitize_removes_original_pixels_from_the_library_copy`, `Interrupted_sanitize_is_completed_on_next_open` | SIM/WCI | Pass |
| Malformed inputs | `Malformed_new_annotations_are_rejected`, `Corrupt_manifest_is_listed_as_damaged_and_other_captures_still_load`, `Modified_or_missing_video_file_is_reported_as_damaged`, `Rejects_non_fragmented_and_damaged_input_without_writing_garbage`, image size limits | SIM | Pass |
| Archive extraction | `Restore_rejects_path_traversal_and_foreign_zips` | SIM | Pass |
| Settings corruption | `Damaged_settings_fall_back_to_defaults_and_keep_the_bad_file` | SIM | Pass |
| Interrupted writes / disk full | `Disk_full_during_autosave_keeps_edits_open_reports_and_recovers`, atomic-file tests | WCI/SIM | Pass |
| Crash during recording | CI kill-and-recover step | WCI | Pass |
| Least privilege install | Installer job: per-user install, no admin; uninstall keeps data | WCI | Pass |
| Self-contained runtime (no machine .NET) | portable and installer jobs with .NET hidden | WCI | Pass |
| No network | Code review: no HTTP/socket APIs referenced in `src/` (`grep` for HttpClient/WebRequest/Socket/WebSocket returns nothing) | — | Pass |
| Dependency vulnerabilities | CI `dotnet list package --vulnerable` | WCI | Pass (no vulnerable packages) |
| Drag temp-file retention and cap | `Temp_export_*` tests | SIM | Pass |
| OCR data handling | Design review: text kept in the OCR window only | — | Pass (inspection) |

## Not tested (pending or out of scope)

- Fuzzing of image/video decoders (Windows components; out of scope).
- Signed-binary verification (no signature yet — see SIGNING.md).
- Physical-PC checks of privacy indicators (capture border, microphone indicator) — manual acceptance.
