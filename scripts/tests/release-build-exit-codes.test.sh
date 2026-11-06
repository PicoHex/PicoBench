#!/usr/bin/env bash
# Verifies the exit-code contract of tools/release-build.sh, the "only supported
# way to build a release" (review items B5 §4, B5-M2; Batch 3.7).
#
#   0  artifact built and the working tree is still clean
#   2  not on main (detached HEAD counts)
#   3  working tree is dirty
#   4  OUT resolves inside the repository
#   5  build or pack failed
#   6  the build left residue in the working tree
#
# The script hard-codes `PicoBench.slnx` and `src/PicoBench/PicoBench.csproj`, so
# the fixture mirrors that layout in a throwaway repository: the contract is
# tested against the real script, without building the real package six times.
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
script="$root/tools/release-build.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0
cases=0

case_() {
    cases=$((cases + 1))
    printf '== %s\n' "$1"
}

expect_exit() {
    local expected="$1" description="$2" repo="$3"
    shift 3
    local status
    ( cd "$repo" && "$@" ) >"$work/last.log" 2>&1
    status=$?
    if [ "$status" -eq "$expected" ]; then
        printf '   ok: %s (exit %s)\n' "$description" "$status"
    else
        printf '   FAIL: %s: expected exit %s, got %s\n' "$description" "$expected" "$status"
        sed 's/^/      /' "$work/last.log"
        failures=$((failures + 1))
    fi
}

new_fixture() {
    local dir="$1"
    rm -rf "$dir"
    mkdir -p "$dir/src/PicoBench"
    cat > "$dir/PicoBench.slnx" <<'SLNX'
<Solution>
  <Project Path="src/PicoBench/PicoBench.csproj" />
</Solution>
SLNX
    cat > "$dir/src/PicoBench/PicoBench.csproj" <<'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <AssemblyName>PicoBench</AssemblyName>
    <PackageId>PicoBench</PackageId>
    <IsPackable>true</IsPackable>
    <Description>release-build fixture</Description>
  </PropertyGroup>
</Project>
CSPROJ
    cat > "$dir/src/PicoBench/Source.cs" <<'SRC'
namespace PicoBench
{
    public static class Fixture
    {
        public static int Value => 1;
    }
}
SRC
    cat > "$dir/.gitignore" <<'IGNORE'
bin/
obj/
IGNORE
    (
        cd "$dir" || exit 1
        git init -q -b main
        git config user.email "release-test@example.invalid"
        git config user.name "Release Test"
        git add -A
        git commit -q -m "fixture"
    ) || { echo "error: could not create the fixture repository" >&2; exit 1; }
}

fixture="$work/repo"
new_fixture "$fixture"
out="$work/out"

# --------------------------------------------------------------------- exit 0
case_ "0 - a clean main with the artifact written outside the repository"
expect_exit 0 "clean build and pack" "$fixture" env OUT="$out" bash "$script"
if ls "$out"/*.nupkg >/dev/null 2>&1; then
    printf '   ok: the .nupkg was written to OUT\n'
else
    printf '   FAIL: no .nupkg in OUT\n'
    failures=$((failures + 1))
fi

# --------------------------------------------------------------------- exit 2
case_ "2 - not on main"
f="$work/repo-branch"
new_fixture "$f"
( cd "$f" && git checkout -q -b feature/b3 )
expect_exit 2 "a side branch is refused" "$f" env OUT="$work/out2" bash "$script"
( cd "$f" && git checkout -q main && git checkout -q --detach )
expect_exit 2 "a detached HEAD is refused" "$f" env OUT="$work/out2" bash "$script"

# --------------------------------------------------------------------- exit 3
case_ "3 - dirty working tree"
f="$work/repo-dirty"
new_fixture "$f"
( cd "$f" && echo "// dirty" >> src/PicoBench/Source.cs )
expect_exit 3 "uncommitted changes are refused" "$f" env OUT="$work/out3" bash "$script"

# --------------------------------------------------------------------- exit 4
case_ "4 - OUT inside the repository"
f="$work/repo-out"
new_fixture "$f"
expect_exit 4 "an absolute OUT inside the repo is refused" "$f" env OUT="$f/artifacts" bash "$script"
expect_exit 4 "a relative OUT is resolved from the repo root and refused" "$f" env OUT=artifacts bash "$script"
if [ -d "$f/artifacts" ]; then
    printf '   FAIL: a refused OUT left a directory behind\n'
    failures=$((failures + 1))
else
    printf '   ok: a refused OUT leaves no directory behind\n'
fi

# --------------------------------------------------------------------- exit 5
case_ "5 - build failure"
f="$work/repo-broken"
new_fixture "$f"
echo "this is not C#" >> "$f/src/PicoBench/Source.cs"
( cd "$f" && git commit -q -am "break the build" )
expect_exit 5 "a compiler error is reported as a build failure" "$f" env OUT="$work/out5" bash "$script"

# --------------------------------------------------------------------- exit 6
case_ "6 - the build leaves residue in the working tree"
f="$work/repo-residue"
new_fixture "$f"
cat > "$f/src/PicoBench/PicoBench.csproj" <<'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <AssemblyName>PicoBench</AssemblyName>
    <PackageId>PicoBench</PackageId>
    <IsPackable>true</IsPackable>
    <Description>release-build fixture</Description>
  </PropertyGroup>
  <Target Name="LeaveResidue" AfterTargets="Build">
    <WriteLinesToFile File="$(MSBuildThisFileDirectory)../../residue.txt" Lines="x" Overwrite="true" />
  </Target>
</Project>
CSPROJ
( cd "$f" && git commit -q -am "a target that writes into the tree" )
expect_exit 6 "residue is reported" "$f" env OUT="$work/out6" bash "$script"

printf '\n'
if [ "$failures" -eq 0 ]; then
    printf 'release-build.sh exit-code contract: %d cases, all ok\n' "$cases"
    exit 0
fi
printf 'release-build.sh exit-code contract: %d failure(s) across %d cases\n' "$failures" "$cases"
exit 1
