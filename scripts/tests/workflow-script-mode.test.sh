#!/usr/bin/env bash
# Contract: a script a workflow runs as `./<path>` must carry the executable bit
# in the git index (mode 100755) - not merely on the disk of whoever wrote it.
#
# v2026.4.0 was tagged and pushed, and the release workflow then died on its
# third step with exit 126: `./scripts/verify-release-commit-on-main.sh:
# Permission denied`. The file was 100644 in the index - a working tree on
# Windows reports `rwxr-xr-x` no matter what, and `git add` therefore recorded
# no execute bit at all. Linux refuses to exec such a file, so Pack never ran,
# Publish was skipped, and not one artifact appeared. Nothing caught it: the
# only step that runs that script runs on release day.
#
# So this file pins the whole class instead of the one file:
#
#   every path a workflow file (*.yml / *.yaml under .github/workflows/) names
#   as `./scripts/...` or `./tools/...` must have index mode 100755.
#
# A path in the index but not executable is a failure, and so is a path that is
# not in the index at all - `./missing.sh` fails the same way at runtime.
#
# The scan is proven able to fail. A throwaway repository is built in the temp
# directory and driven red (644), green (100755), red again (644 restored), red
# (untracked), and red (no invocation to check at all - so an extraction that
# silently matches nothing cannot pass the gate vacuously). A comment that
# merely mentions a path must not be flagged.
#
# Exit code: 0 when the repository obeys the contract and the fixture behaves,
# 1 otherwise.
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0
cases=0

ok() { printf '   ok: %s\n' "$1"; }
no() {
    printf '   FAIL: %s\n' "$1"
    failures=$((failures + 1))
}
case_() {
    cases=$((cases + 1))
    printf '== %s\n' "$1"
}

# Echoes, one per line and sorted, every path a workflow invokes as `./<path>`,
# relative to the repository root. Full-line YAML comments are skipped: prose
# may name a script without invoking it, and this gate is about invocation.
# Only *.yml / *.yaml are read - an allowlist or a README sitting next to the
# workflows is not a workflow.
collect_invocations() {
    local root="$1" file
    while IFS= read -r file; do
        grep -v '^[[:space:]]*#' -- "$file" 2>/dev/null \
            | grep -oE '\./(scripts|tools)/[A-Za-z0-9._/-]+\.(sh|ps1)' \
            | sed 's|^\./||'
    done < <(
        find "$root/.github/workflows" -maxdepth 1 -type f \
            \( -name '*.yml' -o -name '*.yaml' \) | LC_ALL=C sort
    ) | LC_ALL=C sort -u
}

# Echoes the git index mode of a path ("untracked" when it is not in the index).
index_mode() {
    local root="$1" path="$2" line
    line="$(git -C "$root" ls-files -s -- "$path" 2>/dev/null | head -n 1)"
    if [ -z "$line" ]; then
        printf 'untracked'
        return 0
    fi
    printf '%s' "${line%% *}"
}

# Checks one repository root. Returns 0 when every `./`-invoked path is 100755
# and at least one was found; 1 otherwise. Echoes one line per checked path, and
# a FAIL line per offender, so a red run names the fix.
check_root() {
    local root="$1"
    local path mode
    local -i found=0
    local -i status=0
    while IFS= read -r path; do
        [ -n "$path" ] || continue
        found+=1
        mode="$(index_mode "$root" "$path")"
        if [ "$mode" = "100755" ]; then
            printf '   ok: %s is %s\n' "$path" "$mode"
        else
            status=1
            printf '   FAIL: %s is %s, but a workflow invokes it as ./%s\n' "$path" "$mode" "$path"
            printf '         fix: git update-index --chmod=+x %s\n' "$path"
        fi
    done < <(collect_invocations "$root")
    if [ "$found" -eq 0 ]; then
        printf '   FAIL: %s/.github/workflows invokes no ./scripts/... or ./tools/... script\n' "$root"
        printf '         the extraction is broken, or every invocation was rewritten\n'
        return 1
    fi
    return "$status"
}

# ------------------------------------------------- 1. this repository
case_ "every workflow-invoked script is 100755 in the git index"
if output="$(check_root "$root")"; then
    printf '%s\n' "$output"
else
    printf '%s\n' "$output"
    failures=$((failures + 1))
fi

# ------------------------------------------------- 2. the scan can fail
# A minimal repository: one workflow, one script. `git add` on Windows records
# 100644 whatever the working tree says, which is exactly the bug, so the mode
# is then forced explicitly - the fixture must not depend on the host.
fixture="$work/fixture"
mkdir -p "$fixture/.github/workflows" "$fixture/scripts"
git init -q "$fixture"
{
    printf '%s\n' 'name: fixture'
    printf '%s\n' 'jobs:'
    printf '%s\n' '  gate:'
    printf '%s\n' '    runs-on: ubuntu-latest'
    printf '%s\n' '    steps:'
    # A comment that names a path must not be treated as an invocation.
    printf '%s\n' '      # ./scripts/only-mentioned-in-prose.sh is not invoked here'
    printf '%s\n' '      - name: Gate'
    printf '%s\n' '        shell: bash'
    printf '%s\n' '        run: ./scripts/gate.sh'
} > "$fixture/.github/workflows/fixture.yml"
{
    printf '%s\n' '#!/usr/bin/env bash'
    printf '%s\n' 'exit 0'
} > "$fixture/scripts/gate.sh"
git -C "$fixture" add -A >/dev/null
git -C "$fixture" update-index --chmod=-x scripts/gate.sh

case_ "the comment is ignored and only the invoked script is checked"
invoked="$(collect_invocations "$fixture")"
if [ "$invoked" = "scripts/gate.sh" ]; then
    ok "collected 'scripts/gate.sh' alone"
else
    no "collected the wrong set: ${invoked//$'\n'/ }"
fi

case_ "a 100644 invocation is red"
if check_root "$fixture" > "$work/red.txt" 2>&1; then
    no "the 644 fixture passed - the gate cannot fail on the v2026.4.0 shape"
    sed 's/^/      /' "$work/red.txt"
else
    if grep -q 'scripts/gate.sh is 100644' "$work/red.txt"; then
        ok "rejected 100644 and named the path"
    else
        no "rejected the fixture but did not name the 100644 path:"
        sed 's/^/      /' "$work/red.txt"
    fi
fi

case_ "the same fixture is green at 100755"
git -C "$fixture" update-index --chmod=+x scripts/gate.sh
if check_root "$fixture" > "$work/green.txt" 2>&1; then
    ok "100755 passes"
else
    no "the 100755 fixture failed - the gate would be red on correct input:"
    sed 's/^/      /' "$work/green.txt"
fi

case_ "dropping the bit again is red again"
git -C "$fixture" update-index --chmod=-x scripts/gate.sh
if check_root "$fixture" > "$work/red-again.txt" 2>&1; then
    no "the gate went green after the bit was removed - it reads a stale state"
else
    ok "100644 is red again"
fi

case_ "a path absent from the index is red, not skipped"
rm -f "$fixture/scripts/gate.sh"
git -C "$fixture" rm --cached --quiet scripts/gate.sh
if check_root "$fixture" > "$work/untracked.txt" 2>&1; then
    no "an invoked script that is not in the index passed"
    sed 's/^/      /' "$work/untracked.txt"
else
    if grep -q 'scripts/gate.sh is untracked' "$work/untracked.txt"; then
        ok "rejected the missing path"
    else
        no "rejected it for the wrong reason:"
        sed 's/^/      /' "$work/untracked.txt"
    fi
fi

case_ "an extraction that finds nothing is red, not vacuously green"
{
    printf '%s\n' 'name: fixture'
    printf '%s\n' 'jobs:'
    printf '%s\n' '  gate:'
    printf '%s\n' '    runs-on: ubuntu-latest'
    printf '%s\n' '    steps:'
    printf '%s\n' '      - name: Gate'
    printf '%s\n' '        shell: bash'
    printf '%s\n' '        run: bash scripts/gate.sh'
} > "$fixture/.github/workflows/fixture.yml"
if check_root "$fixture" > "$work/empty.txt" 2>&1; then
    no "a workflow with no ./ invocation passed - a broken scan would go unnoticed"
else
    ok "an empty scan is a failure"
fi

printf '\n'
if [ "$failures" -eq 0 ]; then
    printf 'workflow script mode: %d case(s), all ok\n' "$cases"
    exit 0
fi
printf 'workflow script mode: %d failure(s) across %d cases\n' "$failures" "$cases"
exit 1
