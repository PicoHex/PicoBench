# Releasing PicoBench

## The rule

**`main` is the only release branch.** Every release artifact - the `.nupkg`
that goes to NuGet.org, the one a sibling PicoHex repo consumes, the one a user
downloads - is built from a commit on `main`. Not from a feature branch, not
from a `ci/*` branch, not from a detached checkout, not from someone's working
copy.

The rule is enforced in code, not in a wiki, on both paths:

* `tools/release-build.sh` refuses to produce artifacts unless `HEAD` is `main`
  and the tree is clean.
* The release workflow's first step validates the tag
  (`scripts/validate-release-tag.sh`) before the checkout, and its third step
  runs `scripts/verify-release-commit-on-main.sh`, which refuses a tag whose
  commit is not an ancestor of `origin/main`. Pushing a `v*` tag on a side
  branch therefore fails the workflow instead of packing stale code.

Consumer-visible changes for the package you are about to ship are in
[release-notes/batch0.md](release-notes/batch0.md) (the measurement contract) and
[release-notes/batch2.md](release-notes/batch2.md) (output fidelity: invariant
numbers, `N/A` for a non-finite ratio, a legal CSV, the precision profile, escaped
user text and the percentile definition).

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

## Cut the release: `scripts/release.ps1`

```powershell
# inspect the decision - reads only, writes nothing, creates no tag
pwsh ./scripts/release.ps1 -DryRun

# cut it: refresh api/*.public.txt, commit, create the annotated tag
pwsh ./scripts/release.ps1
# ... then push the branch and the tag (the tag triggers the release workflow)
```

`-Push` also pushes for you; it is not required. Exit codes:

| code | meaning |
|-----:|---------|
| 0 | success (a dry run that reported the decision, or commit + tag completed) |
| 2 | precondition/usage error (no baselines, bad switch combination, tooling missing) |
| 3 | working tree is dirty |
| 4 | the public API lost members and `-AllowBreaking` was not passed |
| 5 | build or `api-surface` tool failure |

A release whose public API did not change (the normal `<year>.<x>.<y+1>`
release) leaves the baselines byte-identical, so the commit is **empty** - that
is expected and the tag is still created. Removing a public member is different:
it fails with exit 4 unless you pass `-AllowBreaking`. Read the diff the script
prints, decide whether the break is intended, document it in the release notes,
and only then re-run with `-AllowBreaking`; the refreshed baseline becomes the
new reference for the next release.

The same gate runs in CI (`api-guard` job: `release.ps1 -DryRun`), so a pull
request that deletes a public member fails before merge instead of being
absorbed by a baseline refresh at release time.

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

## Compatibility the release is expected to keep

`tests/compat/` holds the smoke projects that stand in for consumers; each one
is built by a CI job:

| job | proves |
|---|---|
| `api-guard` | no public member disappeared (or the break is explicit) |
| `aot-validation` | the library AOT-publishes with zero trim/AOT warnings **and the artifact runs** |
| `net4x-smoke` | `netstandard2.0` really compiles and runs on .NET Framework 4.7.2/4.8 |
| `consumer-sdk8` | the generator works inside the SDK 8 compiler (Roslyn 4.8) |
| `consumer-netstandard20` | another netstandard2.0 library can compile the generated code |
| `samples` | all four samples run on Windows, Linux and macOS |
| `release-scripting` | the tag gate, both release scripts' exit codes, the package shape and the workflows themselves |

Note for .NET Framework and netstandard2.0 consumers: the generated code needs
**C# 8** (`#nullable enable`, `??=`), so a project that relies on the SDK's
default C# 7.3 for those target frameworks must set `<LangVersion>8.0</LangVersion>`
or later. See `tests/compat/README.md`.

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
