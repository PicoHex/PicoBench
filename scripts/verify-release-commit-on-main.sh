#!/usr/bin/env bash
# Refuses to release a commit that is not on origin/main (review item B5-M2).
#
# `docs/releasing.md` promises that a release artifact is always built from
# main, but the only enforcement was a hand-run helper: pushing a `v*` tag on a
# side branch went straight to the pack job. This check closes that path.
#
# Inputs (environment): RELEASE_SHA (defaults to HEAD), MAIN_REF (defaults to
# refs/remotes/origin/main).
set -euo pipefail

sha="${RELEASE_SHA:-$(git rev-parse HEAD)}"
main_ref="${MAIN_REF:-refs/remotes/origin/main}"

git fetch --no-tags origin "+refs/heads/main:$main_ref" >/dev/null 2>&1 || true

if ! git rev-parse --verify --quiet "$main_ref" >/dev/null; then
    echo "::error::cannot resolve $main_ref; fetch origin/main before releasing" >&2
    exit 1
fi

if ! git merge-base --is-ancestor "$sha" "$main_ref"; then
    echo "::error::release commit $sha is not an ancestor of $main_ref - releases are cut from main only" >&2
    exit 1
fi

echo "release commit $sha is on $main_ref ($(git rev-parse --short "$main_ref"))"
