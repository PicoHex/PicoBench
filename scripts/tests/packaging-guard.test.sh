#!/usr/bin/env bash
# Guards the shape of the shipped package (review items B5-M1, B5-L9 and the
# "nothing asserts the package contents" gap in B5 §4).
#
# The snupkg was never produced for years because a condition in
# Directory.Build.props could not see the <IsPackable> its project declared, and
# nothing noticed: the release workflow collected *.snupkg that never existed.
# These assertions fail when that regresses.
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
version="0.0.0-guard"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0
check() {
    local description="$1"
    shift
    if "$@" >/dev/null 2>&1; then
        printf '   ok: %s\n' "$description"
    else
        printf '   FAIL: %s\n' "$description"
        failures=$((failures + 1))
    fi
}

py=""
for candidate in python3 python; do
    command -v "$candidate" >/dev/null 2>&1 || continue
    if "$candidate" -c "import zipfile" >/dev/null 2>&1; then
        py="$candidate"
        break
    fi
done
[ -n "$py" ] || { echo "error: python with zipfile is required" >&2; exit 1; }

echo "== symbol package properties"
include_symbols="$(cd "$root" && dotnet msbuild src/PicoBench/PicoBench.csproj -getProperty:IncludeSymbols 2>/dev/null | tr -d '[:space:]')"
if [ "$include_symbols" = "true" ]; then
    printf '   ok: IncludeSymbols is true for the packed project\n'
else
    printf '   FAIL: IncludeSymbols is not true (%s)\n' "$include_symbols"
    failures=$((failures + 1))
fi

generator_symbols="$(cd "$root" && dotnet msbuild src/PicoBench.Generators/PicoBench.Generators.csproj -getProperty:IncludeSymbols 2>/dev/null | tr -d '[:space:]')"
if [ -z "$generator_symbols" ]; then
    printf '   ok: the embedded generator project stays unpacked\n'
else
    printf '   FAIL: the generator project picked up symbol settings (%s)\n' "$generator_symbols"
    failures=$((failures + 1))
fi

echo "== dotnet pack"
if (cd "$root" && dotnet pack src/PicoBench/PicoBench.csproj --configuration Release \
        --output "$work" -p:Version="$version" --nologo >"$work/pack.log" 2>&1); then
    printf '   ok: pack succeeded\n'
else
    printf '   FAIL: pack failed\n'
    sed 's/^/      /' "$work/pack.log"
    failures=$((failures + 1))
fi

nupkg="$work/PicoBench.$version.nupkg"
snupkg="$work/PicoBench.$version.snupkg"
check "the .nupkg exists" test -f "$nupkg"
check "the .snupkg exists" test -f "$snupkg"

if [ -f "$nupkg" ]; then
    echo "== package contents"
    entries="$("$py" - "$nupkg" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as archive:
    print("\n".join(sorted(archive.namelist())))
PY
)"
    for required in \
        "lib/netstandard2.0/PicoBench.dll" \
        "lib/netstandard2.0/PicoBench.xml" \
        "buildTransitive/PicoBench.props" \
        "buildTransitive/PicoBench.targets" \
        "buildTransitive/PicoBench.Generators/analyzers/dotnet/cs/PicoBench.Generators.dll" \
        "README.md" \
        "PicoBench.nuspec"; do
        if printf '%s\n' "$entries" | grep -qxF "$required"; then
            printf '   ok: nupkg contains %s\n' "$required"
        else
            printf '   FAIL: nupkg is missing %s\n' "$required"
            failures=$((failures + 1))
        fi
    done

    echo "== package dependencies"
    deps="$("$py" - "$nupkg" <<'PY'
import re, sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as archive:
    nuspec = archive.read("PicoBench.nuspec").decode("utf-8")
ids = sorted(set(re.findall(r'<dependency id="([^"]+)"', nuspec)))
print("\n".join(ids))
PY
)"
    expected="System.Threading.Tasks.Extensions"
    if [ "$deps" = "$expected" ]; then
        printf '   ok: the only declared dependency is %s\n' "$expected"
    else
        printf '   FAIL: declared dependencies changed:\n'
        printf '%s\n' "$deps" | sed 's/^/      /'
        failures=$((failures + 1))
    fi
fi

if [ -f "$snupkg" ]; then
    echo "== symbol package contents"
    pdbs="$("$py" - "$snupkg" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as archive:
    print(sum(1 for name in archive.namelist() if name.endswith(".pdb")))
PY
)"
    if [ "${pdbs:-0}" -ge 1 ]; then
        printf '   ok: the snupkg carries %s portable pdb file(s)\n' "$pdbs"
    else
        printf '   FAIL: the snupkg contains no .pdb\n'
        failures=$((failures + 1))
    fi
fi

printf '\n'
if [ "$failures" -eq 0 ]; then
    printf 'packaging guard: all checks ok\n'
    exit 0
fi
printf 'packaging guard: %d failure(s)\n' "$failures"
exit 1
