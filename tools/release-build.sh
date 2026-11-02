#!/usr/bin/env bash
# tools/release-build.sh - the only supported way to build a PicoBench release.
#
# main is the only release branch (see docs/releasing.md). This script refuses to
# build anywhere else, refuses a dirty tree, and keeps every artifact outside the
# repository so "released from a side branch" is not something you can do by hand.
#
# Exit codes (callers may branch on them):
#   0  artifact built and the working tree is still clean
#   2  not on main (current branch printed; detached HEAD counts)
#   3  working tree is dirty
#   4  OUT resolves inside the repository (artifacts must go outside)
#   5  build or pack failed
#   6  the build left residue in the working tree
set -euo pipefail

REPO="$(git rev-parse --show-toplevel)"
cd "$REPO"

# --- R1: refuse anything that is not main -----------------------------------
BRANCH="$(git symbolic-ref --short -q HEAD || true)"
if [ -z "$BRANCH" ]; then
  echo "refusing to build a release: HEAD is detached at $(git rev-parse --short HEAD), not on main" >&2
  echo "releases are cut from the 'main' branch only" >&2
  exit 2
fi
if [ "$BRANCH" != "main" ]; then
  echo "refusing to build a release: current branch is '$BRANCH', not 'main'" >&2
  echo "releases are cut from the 'main' branch only" >&2
  exit 2
fi

# --- R2: refuse a dirty tree -------------------------------------------------
STATUS="$(git status --porcelain)"
if [ -n "$STATUS" ]; then
  echo "refusing to build a release: working tree is dirty (commit or stash first):" >&2
  echo "$STATUS" | head -n 3 | sed 's/^/  /' >&2
  COUNT="$(echo "$STATUS" | wc -l | tr -d ' ')"
  LEFT=$((COUNT - 3))
  if [ "$LEFT" -gt 0 ]; then
    echo "  ... and $LEFT more path(s)" >&2
  fi
  exit 3
fi

# --- R3: print the identity of what is being released ------------------------
SHA="$(git rev-parse HEAD)"
echo "release identity"
echo "  commit  : $SHA"
echo "  branch  : $BRANCH"
echo "  time    : $(date -u +%FT%TZ)"

# The release workflow takes the version from the tag. Mirror that when HEAD is
# exactly a v* tag; otherwise the repo default (0.0.0-dev, src/Directory.Build.props)
# applies and we say so instead of inventing a version.
TAG="$(git describe --tags --exact-match 2>/dev/null || true)"
VERSION=""
case "$TAG" in
  v[0-9]*) VERSION="${TAG#v}" ;;
esac
if [ -n "$VERSION" ]; then
  echo "  version : $VERSION (from tag $TAG)"
else
  echo "  version : repository default (HEAD is not a release tag; see docs/releasing.md)"
fi

# --- R4: artifacts live outside the repository -------------------------------
TMP_ROOT="${TMPDIR:-/tmp}"
OUT="${OUT:-$TMP_ROOT/rel-$(git rev-parse --short HEAD)}"
REPO_ABS="$(cd "$REPO" && pwd -P)"

# Validate the requested path before creating anything, so a refused OUT leaves
# no directory behind. A relative OUT is relative to the repository root.
case "$OUT" in
  /*|[A-Za-z]:*) OUT_ABS="$OUT" ;;
  *) OUT_ABS="$REPO_ABS/$OUT" ;;
esac
refuse_inside() {
  echo "refusing to build a release: OUT ($1) is inside the repository" >&2
  echo "artifacts must be written outside $REPO_ABS; pass OUT=<path outside the repo>" >&2
  exit 4
}
case "$OUT_ABS/" in
  "$REPO_ABS"/*) refuse_inside "$OUT_ABS" ;;
esac

mkdir -p "$OUT_ABS"
# Resolve once more: a symlink under an allowed prefix could still point into the repo.
OUT_REAL="$(cd "$OUT_ABS" && pwd -P)"
case "$OUT_REAL/" in
  "$REPO_ABS"/*)
    rmdir "$OUT_REAL" 2>/dev/null || true
    refuse_inside "$OUT_REAL"
    ;;
esac
OUT_ABS="$OUT_REAL"
echo "  output  : $OUT_ABS"

# --- R6: build the solution and pack the package; a failure is a failure -----
# This repository ships one package: src/PicoBench (IsPackable=true), with the
# source generator embedded into it. There is no deployable app, so the release
# build is build + pack; nothing is published to a folder.
BUILD_ARGS=(-c Release --nologo)
PACK_ARGS=(-c Release --nologo -o "$OUT_ABS")
if [ -n "$VERSION" ]; then
  BUILD_ARGS+=("-p:Version=$VERSION")
  PACK_ARGS+=("-p:Version=$VERSION")
fi

if ! dotnet build PicoBench.slnx "${BUILD_ARGS[@]}"; then
  echo "release build FAILED: dotnet build PicoBench.slnx -c Release" >&2
  exit 5
fi

if ! dotnet pack src/PicoBench/PicoBench.csproj "${PACK_ARGS[@]}" -p:ContinuousIntegrationBuild=true; then
  echo "release pack FAILED: dotnet pack src/PicoBench/PicoBench.csproj -c Release" >&2
  exit 5
fi

# --- R5: fingerprint the artifact -------------------------------------------
ARTIFACT="$(find "$OUT_ABS" -maxdepth 1 -name '*.nupkg' -print -quit 2>/dev/null || true)"
echo "release artifacts"
if [ -n "$ARTIFACT" ]; then
  if command -v sha256sum >/dev/null 2>&1; then
    SUM="$(sha256sum "$ARTIFACT" | awk '{print $1}')"
  else
    SUM="$(shasum -a 256 "$ARTIFACT" | awk '{print $1}')"
  fi
  echo "  artifact: $ARTIFACT"
  echo "  sha256  : $SUM"
  echo "  size    : $(wc -c < "$ARTIFACT" | tr -d ' ') bytes"
else
  echo "  no .nupkg produced; contents of $OUT_ABS:"
  ls -l "$OUT_ABS" | sed 's/^/  /'
  echo "  total   : $(du -sh "$OUT_ABS" | awk '{print $1}')"
fi

# --- R4 (again): the build must leave nothing behind -------------------------
RESIDUE="$(git status --porcelain)"
if [ -n "$RESIDUE" ]; then
  echo "the release build left residue in the working tree (this must be zero):" >&2
  echo "$RESIDUE" | sed 's/^/  /' >&2
  exit 6
fi
echo "  repo    : clean (git status --porcelain is empty)"
