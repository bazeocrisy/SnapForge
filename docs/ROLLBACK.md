# Rollback procedures

Principle: **never roll back user data destructively.** Program files can be replaced; captures, recordings and
settings are only ever moved aside, never deleted, by a rollback.

## A defective software release

1. Mark the GitHub Release as a pre-release or edit its notes with a warning (do not delete it silently — people may
   need the checksum to identify what they installed).
2. Website: set `website/config.json → release.ownerApproved` to `false` (the Download button becomes disabled), or
   point it at the previous good release (re-run `verify-release.py` for it), rebuild, publish `gh-pages`.
3. Users: install the previous version over the current one (installer) or replace the portable folder. The Library
   format is forward-compatible within 1.x; an older build lists items it cannot read as *damaged* and leaves them
   untouched (it never rewrites them).
4. Fix forward with `1.0.(n+1)`.

## Website error

`gh-pages` is a normal branch: `git revert <bad publish commit>` on `gh-pages` and push, or re-run
`publish-gh-pages.sh` from a good source commit. Pages updates in a minute or two. To take the site offline:
Settings → Pages → Unpublish.

## Failed upgrade

The installer upgrades in place; settings and the Library are outside the program folder and are not touched. If
the new version does not start: uninstall (keep data) and install the previous installer, or run the previous portable
build. Report the issue with Help → About details.

## Corrupted settings

SnapForge moves an unreadable `settings.json` to `settings.json.damaged` and starts with defaults (automatic). To roll
back manually: exit SnapForge, rename `settings.json.damaged` back after fixing it, or delete `settings.json` to reset.
Settings → Reset all preferences does the same from inside the app (keeps the Library location).

## Failed Library migration / damaged items

- 1.0 has no destructive migration: new data (videos, new annotation types) is stored in new files/folders that older
  builds ignore or report as damaged without changing them.
- Damaged items are listed with the reason; other items keep working. Restore from a Library backup (.zip) into the
  Library (Settings → Library → Restore from backup) — restore adds items, it does not delete existing ones.
- Before any manual repair, copy the whole Library folder (`%LOCALAPPDATA%\SnapForge\Library` or your chosen
  location) somewhere safe.
