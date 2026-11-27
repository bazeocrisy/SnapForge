#!/usr/bin/env bash
# Publish the website to the `gh-pages` branch, which GitHub Pages serves at https://bazeocrisy.github.io/SnapForge/
#
#   bash website/tools/publish-gh-pages.sh          build, check, commit to gh-pages (local only;
#                                                   extra arguments go to git commit, e.g. -m "note")
#   git push origin gh-pages                        then push — the site updates within a minute or two
#
# What it does: rebuilds website/public, runs the full check (`build.py --check` + unit tests), then replaces the
# entire contents of gh-pages with website/public (including .nojekyll) as one new commit. Nothing else is needed:
# no GitHub Actions, no variables. Run from any branch that contains website/ (normally claude/website).
set -euo pipefail
repo="$(git rev-parse --show-toplevel)"
cd "$repo"
if [ -n "$(git status --porcelain -- website)" ]; then
  echo "website/ has uncommitted changes — commit them first, so gh-pages matches a commit." >&2
  exit 1
fi
python3 -B website/build.py
python3 -B website/build.py --check
python3 -B -m unittest discover -s website/tests
if [ -n "$(git status --porcelain -- website/public)" ]; then
  echo "website/public changed after a rebuild — commit it on the source branch first." >&2
  exit 1
fi
src_commit="$(git rev-parse --short HEAD)"
src_branch="$(git rev-parse --abbrev-ref HEAD)"
work="$(mktemp -d)"
trap 'git worktree remove --force "$work" >/dev/null 2>&1 || true; rm -rf "$work"' EXIT
if git show-ref --verify --quiet refs/heads/gh-pages || git fetch -q origin gh-pages:gh-pages 2>/dev/null; then
  git worktree add -q "$work" gh-pages
  (cd "$work" && git rm -q -r --ignore-unmatch . )
else
  git worktree add -q --detach "$work"
  (cd "$work" && git checkout -q --orphan gh-pages && git rm -q -r --cached . && find . -mindepth 1 -maxdepth 1 ! -name .git -exec rm -rf {} +)
fi
cp -R website/public/. "$work/"
test -f "$work/.nojekyll"
cd "$work"
git add -A
if git diff --cached --quiet; then
  echo "gh-pages is already up to date with $src_branch@$src_commit."
  exit 0
fi
git commit -q -m "Publish website from $src_branch@$src_commit" "$@"
echo "Committed to gh-pages: $(git rev-parse --short HEAD) (from $src_branch@$src_commit)."
echo "Push it with:  git push origin gh-pages"
