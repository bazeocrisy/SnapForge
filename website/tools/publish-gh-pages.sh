#!/usr/bin/env bash
# Publish the website to the `gh-pages` branch, which GitHub Pages serves at https://bazeocrisy.github.io/SnapForge/
#
#   bash website/tools/publish-gh-pages.sh                 check, build, test, commit to gh-pages (local only;
#                                                          extra arguments go to git commit, e.g. -m "note")
#   bash website/tools/publish-gh-pages.sh --preflight-only   only the source checks below, then stop
#   bash website/tools/publish-gh-pages.sh --force-older      allow publishing an OLDER source commit than the
#                                                          one gh-pages records (e.g. a deliberate rollback)
#   git push origin gh-pages                               then push — the site updates within a minute or two
#
# Branch-based GitHub Pages hosting only (Settings → Pages → Deploy from a branch → gh-pages, / (root)), with
# .nojekyll. No GitHub Actions deployment, no repository variables (owner requirement SF-013).
#
# FA-013 — one canonical source. The script refuses unless:
#   1. the current branch is CANONICAL_BRANCH (below);
#   2. the working tree has no changes at all (tracked or untracked);
#   3. after `git fetch`, the local branch is exactly origin/CANONICAL_BRANCH (not behind, not ahead/unpushed),
#      so the recorded source commit is public;
#   4. the source commit is not OLDER than the one recorded in gh-pages' build-info.json (it must contain that
#      commit); otherwise it refuses unless --force-older is given;
#   5. build.py --check and the unit tests pass, and website/public is unchanged by a rebuild.
# It then replaces gh-pages with website/public plus build-info.json (source branch, full commit, time), which is
# written only into gh-pages (website/public cannot contain the commit that contains it). It never pushes.
set -euo pipefail

# The one branch the live site may be built from. Change to "main" after the owner merges the candidate.
CANONICAL_BRANCH="release/v1.0-candidate"
REMOTE="origin"

force_older=0
preflight_only=0
commit_args=()
for arg in "$@"; do
  case "$arg" in
    --force-older) force_older=1 ;;
    --preflight-only) preflight_only=1 ;;
    *) commit_args+=("$arg") ;;
  esac
done

die() { echo "REFUSED: $*" >&2; exit 1; }

repo="$(git rev-parse --show-toplevel)"
cd "$repo"

branch="$(git rev-parse --abbrev-ref HEAD)"
[ "$branch" = "$CANONICAL_BRANCH" ] ||
  die "the site is published only from '$CANONICAL_BRANCH' (current branch: '$branch')."

[ -z "$(git status --porcelain --untracked-files=all)" ] ||
  die "the working tree has uncommitted or untracked changes — commit or remove them first."

git fetch -q "$REMOTE" "+refs/heads/$CANONICAL_BRANCH:refs/remotes/$REMOTE/$CANONICAL_BRANCH" ||
  die "could not fetch $REMOTE/$CANONICAL_BRANCH."
git fetch -q "$REMOTE" "+refs/heads/gh-pages:refs/remotes/$REMOTE/gh-pages" 2>/dev/null || true

head="$(git rev-parse HEAD)"
upstream="$(git rev-parse "refs/remotes/$REMOTE/$CANONICAL_BRANCH")"
if [ "$head" != "$upstream" ]; then
  if git merge-base --is-ancestor "$head" "$upstream"; then
    die "local '$CANONICAL_BRANCH' is behind $REMOTE — pull first."
  elif git merge-base --is-ancestor "$upstream" "$head"; then
    die "local '$CANONICAL_BRANCH' has commits not pushed to $REMOTE — push them first, so the published source is public."
  else
    die "local '$CANONICAL_BRANCH' and $REMOTE/$CANONICAL_BRANCH have diverged."
  fi
fi

# gh-pages to build on: the remote one when it exists; a local gh-pages must not be behind it.
pages_ref=""
if git show-ref --verify --quiet "refs/remotes/$REMOTE/gh-pages"; then
  pages_ref="refs/remotes/$REMOTE/gh-pages"
  if git show-ref --verify --quiet refs/heads/gh-pages; then
    if ! git merge-base --is-ancestor "$pages_ref" refs/heads/gh-pages; then
      die "local gh-pages is behind or diverged from $REMOTE/gh-pages — update it first (git branch -f gh-pages $REMOTE/gh-pages)."
    fi
    pages_ref="refs/heads/gh-pages"
  fi
elif git show-ref --verify --quiet refs/heads/gh-pages; then
  pages_ref="refs/heads/gh-pages"
fi

recorded=""
if [ -n "$pages_ref" ] && git cat-file -e "$pages_ref:build-info.json" 2>/dev/null; then
  recorded="$(git show "$pages_ref:build-info.json" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("sourceCommit") or "")')"
fi
if [ -n "$recorded" ] && [ "$recorded" != "$head" ]; then
  if ! git cat-file -e "$recorded^{commit}" 2>/dev/null; then
    [ "$force_older" = 1 ] || die "gh-pages was built from $recorded, which is not in this repository — cannot prove this source is newer (use --force-older to override)."
  elif ! git merge-base --is-ancestor "$recorded" "$head"; then
    [ "$force_older" = 1 ] || die "gh-pages was built from $recorded, which this commit ($head) does not contain — this source is older or unrelated (use --force-older to override)."
  fi
  if [ "$force_older" = 1 ]; then echo "WARNING: --force-older: publishing $head over the newer or unrelated $recorded." >&2; fi
fi

echo "Preflight OK: $CANONICAL_BRANCH@$head matches $REMOTE; gh-pages records ${recorded:-no source commit}."
[ "$preflight_only" = 1 ] && exit 0

python3 -B website/build.py
python3 -B website/build.py --check
python3 -B -m unittest discover -s website/tests
if [ -n "$(git status --porcelain --untracked-files=all)" ]; then
  die "the rebuild or tests changed the working tree (website/public stale?) — commit the result on $CANONICAL_BRANCH first."
fi

work="$(mktemp -d)"
trap 'cd "$repo"; git worktree remove --force "$work" >/dev/null 2>&1 || true; rm -rf "$work"' EXIT
if [ -n "$pages_ref" ]; then
  git worktree add -q --detach "$work" "$pages_ref"
  (cd "$work" && git checkout -q -B gh-pages && git rm -q -r --ignore-unmatch . )
else
  git worktree add -q --detach "$work"
  (cd "$work" && git checkout -q --orphan gh-pages && git rm -q -r --cached . && find . -mindepth 1 -maxdepth 1 ! -name .git -exec rm -rf {} +)
fi
cp -R website/public/. "$work/"
test -f "$work/.nojekyll"
python3 -c 'import json,sys,datetime; json.dump({"sourceBranch": sys.argv[1], "sourceCommit": sys.argv[2],
  "publishedAt": datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0).isoformat()},
  open(sys.argv[3], "w"), indent=2); open(sys.argv[3], "a").write("\n")' "$CANONICAL_BRANCH" "$head" "$work/build-info.json"
cd "$work"
git add -A
if git diff --cached --quiet -- . ':!build-info.json'; then
  echo "gh-pages already has the site built from $CANONICAL_BRANCH@$head — nothing to publish."
  exit 0
fi
git commit -q -m "Publish website from $CANONICAL_BRANCH@${head:0:12}" ${commit_args[@]+"${commit_args[@]}"}
echo "Committed to gh-pages: $(git rev-parse --short HEAD) (from $CANONICAL_BRANCH@${head:0:12})."
echo "Push it with:  git push $REMOTE gh-pages"
