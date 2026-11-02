# Releasing PicoBench

## The rule

**`main` is the only release branch.** Every release artifact - the `.nupkg`
that goes to NuGet.org, the one a sibling PicoHex repo consumes, the one a user
downloads - is built from a commit on `main`. Not from a feature branch, not
from a `ci/*` branch, not from a detached checkout, not from someone's working
copy.

The rule is enforced in code, not in a wiki: `tools/release-build.sh` refuses to
produce artifacts unless `HEAD` is `main` and the tree is clean.

## Build with `tools/release-build.sh`

```bash
# artifacts go to $TMPDIR/rel-<short-sha>, never into the repository
bash tools/release-build.sh

# or choose the output directory (must be outside the repository)
OUT=/tmp/picobench-release bash tools/release-build.sh
```

The script prints the commit sha, the branch, a UTC timestamp, the version, the
artifact path and its sha256. Exit codes:

| code | meaning |
|-----:|---------|
| 0 | artifact built, working tree still clean |
| 2 | not on `main` (the current branch is printed; detached `HEAD` counts) |
| 3 | working tree is dirty |
| 4 | `OUT` is inside the repository |
| 5 | build or pack failed |
| 6 | the build left residue in the working tree |

It builds `PicoBench.slnx` in Release and packs `src/PicoBench/PicoBench.csproj`
(the only packable project; the source generator is embedded in it). Nothing is
`publish`ed - this repository has no deployable application. When `HEAD` is
exactly a `v*` tag the version comes from that tag, mirroring the release
workflow; otherwise the repo default `0.0.0-dev` is used and the script says so.

## Before you release: three self-checks

1. **You are on `main` and it is clean.** `git rev-parse --abbrev-ref HEAD`
   prints `main`; `git status --porcelain` prints nothing. The script refuses
   otherwise, so a release cannot be cut from a side branch by accident.
2. **`main` is what you think it is.** `git fetch origin && git status -sb`
   shows no divergence, and `git log --oneline -1` is the commit you intend to
   ship. The tag must be created on `main` (`scripts/release.ps1`), because the
   release workflow packs the commit the tag points at.
3. **You can name the artifact.** Record the artifact sha256 next to the commit
   sha the script prints. A file timestamp proves nothing; that pair says which
   artifact came from which commit. NuGet packing is not bit-reproducible, so a
   re-pack has a different hash - compare the commit sha across builds, and
   compare the hash only against the file you actually ship.

## Why this rule exists

Someone cut a release from a side branch. The build worked, the file was
written, and the artifact looked brand new: its timestamp was the newest thing
on disk. But the branch it came from was **69 commits behind `main`**, so the
package carried stale code - the fixes everyone thought they had shipped were
not in it. Nothing in the pipeline compared the artifact to `main`; the only
signal anyone looked at, the timestamp, pointed the wrong way.

A timestamp says *when* something was built. It says nothing about *what* was
built. So the rule is now structural: the build will not run off `main`, and it
prints both the commit it was built from and the sha256 of the artifact it
produced, so what you ship can be checked against what is on `main`.
