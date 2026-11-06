#!/usr/bin/env bash
# Behavioural tests for the release gate (review items B5-H2, B5-M2).
#
# The release workflow used to strip `refs/tags/v` off the trigger and inline the
# result into `run:` blocks, so a tag like `v1.0.0$(id)` executed as shell. The
# fix moves the value into the environment and validates it with a strict
# pattern; these cases pin both halves of that fix - the malformed names must be
# rejected, and nothing they contain may ever reach a shell.
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
validate="$root/scripts/validate-release-tag.sh"
ancestry="$root/scripts/verify-release-commit-on-main.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The gate ships inline in release.yml (it has to run before the checkout), so
# the cases below execute the extracted body - the text that actually runs on
# GitHub - and fail if it drifts from the checked-in script.
gate="$work/gate.sh"
py=""
for candidate in python3 python; do
    command -v "$candidate" >/dev/null 2>&1 || continue
    if "$candidate" "$here/workflow_step.py" "$root/.github/workflows/release.yml" "Validate the release tag" > "$gate" 2>/dev/null && [ -s "$gate" ]; then
        py="$candidate"
        break
    fi
done
if [ -z "$py" ]; then
    echo "error: python is required to extract the inline release gate" >&2
    exit 1
fi

if [ ! -s "$gate" ]; then
    echo "error: could not extract the release tag validation step" >&2
    exit 1
fi

failures=0
cases=0

normalise() { grep -v -e '^[[:space:]]*#' -e '^[[:space:]]*$' "$1" | sed 's/[[:space:]]*$//'; }
if diff -u <(normalise "$validate") <(normalise "$gate") > "$work/drift.diff"; then
    printf 'ok: the inline gate is identical to scripts/validate-release-tag.sh
'
else
    printf 'FAIL: the inline release gate drifted from scripts/validate-release-tag.sh
'
    sed 's/^/      /' "$work/drift.diff"
    failures=$((failures + 1))
fi

case_() {
    cases=$((cases + 1))
    printf '== %s\n' "$1"
}

ok() { printf '   ok: %s\n' "$1"; }
no() {
    printf '   FAIL: %s\n' "$1"
    failures=$((failures + 1))
}

expect_exit() {
    local expected="$1" description="$2"
    shift 2
    local output status
    output="$("$@" 2>&1)"
    status=$?
    if [ "$status" -eq "$expected" ]; then
        ok "$description (exit $status)"
    else
        no "$description: expected exit $expected, got $status"
        printf '%s\n' "$output" | sed 's/^/      /'
    fi
}

# Runs validate-release-tag.sh with a clean output file and echoes the outputs.
run_validate() {
    : > "$work/out"
    GITHUB_OUTPUT="$work/out" "$@" >"$work/log" 2>&1
    local status=$?
    return $status
}

expect_output() {
    local expected="$1" description="$2"
    if grep -qxF "$expected" "$work/out"; then
        ok "$description ($expected)"
    else
        no "$description: '$expected' missing from the step outputs"
        sed 's/^/      /' "$work/out"
    fi
}

# ----------------------------------------------------------------- tag format
case_ "a well-formed tag push reaches the pack job"
run_validate env EVENT_NAME=push REF_TYPE=tag REF_NAME=v2026.4.0 bash "$gate"
expect_exit 0 "v2026.4.0 accepted" true
expect_output "version=2026.4.0" "version output"
expect_output "tag=v2026.4.0" "tag output"

case_ "malformed tags stop at the first step"
for bad in \
    'v2026.4' \
    'v2026.4.0.1' \
    'v' \
    'vfoo' \
    'v2026.4.0-beta' \
    '2026.4.0' \
    'v 2026.4.0' \
    'v2026.4.0 '; do
    expect_exit 1 "'$bad' rejected" \
        env EVENT_NAME=push REF_TYPE=tag REF_NAME="$bad" bash "$gate"
done

case_ "shell metacharacters in the tag name are refused and never executed"
marker="$work/pwned"
for bad in \
    "v1.0.0\$(touch $marker)" \
    "v1.0.0\"; touch $marker; \"" \
    "v1.0.0\`touch $marker\`" \
    "v1.0.0'; touch $marker; '"; do
    expect_exit 1 "'$bad' rejected" \
        env EVENT_NAME=push REF_TYPE=tag REF_NAME="$bad" bash "$gate"
done
if [ -e "$marker" ]; then
    no "a tag name executed as shell: $marker exists"
else
    ok "no tag name reached a shell"
fi

# ------------------------------------------------------------------ dispatch
case_ "workflow_dispatch must supply the version"
expect_exit 1 "missing version input rejected" \
    env EVENT_NAME=workflow_dispatch bash "$gate"

run_validate env EVENT_NAME=workflow_dispatch INPUT_VERSION=2026.4.0 bash "$gate"
expect_exit 0 "2026.4.0 input accepted" true
expect_output "tag=v2026.4.0" "tag reconstructed from the input"

case_ "the version is matched as a whole value, not line by line"
# `grep '^...$'` anchors a *line*, so a value carrying a second line - reachable
# through this free-text input - used to pass the gate (N1). The whole value must
# match, and nothing a rejected value carries may reach the step outputs.
for bad in \
    $'2026.4.0\nEVIL=1' \
    $'2026.4.0\n' \
    $'\n2026.4.0' \
    $'2026.4.0\nevil=1\nmore' \
    '2026.4.0 ' \
    ' 2026.4.0' \
    $'2026.4.0\t' \
    'x2026.4.0' \
    '2026.4.0x' \
    ''; do
    : > "$work/out"
    GITHUB_OUTPUT="$work/out" env EVENT_NAME=workflow_dispatch INPUT_VERSION="$bad" \
        bash "$gate" >"$work/log" 2>&1
    status=$?
    label="$(printf '%q' "$bad")"
    if [ "$status" -eq 1 ]; then
        ok "rejected: '$label'"
    else
        no "accepted (exit $status): '$label'"
        sed 's/^/      /' "$work/log"
    fi
    if [ -s "$work/out" ]; then
        no "a rejected version reached the step outputs: '$label'"
        sed 's/^/      /' "$work/out"
    fi
done

# The boundary is exact in both directions: the well-formed value still writes
# exactly the two outputs, no more.
run_validate env EVENT_NAME=workflow_dispatch INPUT_VERSION=2026.4.0 bash "$gate"
expect_exit 0 "a plain version is still accepted" true
if [ "$(wc -l < "$work/out")" -eq 2 ]; then
    ok "the accepted run writes exactly two outputs"
else
    no "expected two step outputs, got $(wc -l < "$work/out")"
    sed 's/^/      /' "$work/out"
fi


case_ "an unexpected trigger is refused"
expect_exit 1 "event=pull_request rejected" \
    env EVENT_NAME=pull_request bash "$gate"
expect_exit 1 "tag push with ref_type=branch rejected" \
    env EVENT_NAME=push REF_TYPE=branch REF_NAME=v2026.4.0 bash "$gate"

# ----------------------------------------------------------------- ancestry
case_ "the release commit must be on main"
repo="$work/repo"
mkdir -p "$repo"
(
    cd "$repo" || exit 1
    git init -q -b main
    git config user.email "release-test@example.invalid"
    git config user.name "Release Test"
    echo one > file.txt
    git add -A
    git commit -q -m "on main"
    git update-ref refs/remotes/origin/main HEAD
    git checkout -q -b side
    echo two >> file.txt
    git commit -q -am "off main"
) || no "could not build the ancestry fixture"

if [ -d "$repo" ]; then
    on_main="$(cd "$repo" && git rev-parse main)"
    off_main="$(cd "$repo" && git rev-parse side)"
    expect_exit 0 "a commit on main is accepted" \
        env RELEASE_SHA="$on_main" bash -c "cd '$repo' && bash '$ancestry'"
    expect_exit 1 "a commit that is not on main is refused" \
        env RELEASE_SHA="$off_main" bash -c "cd '$repo' && bash '$ancestry'"
fi

printf '\n'
if [ "$failures" -eq 0 ]; then
    printf 'release tag validation: %d cases, all ok\n' "$cases"
    exit 0
fi
printf 'release tag validation: %d failure(s) across %d cases\n' "$failures" "$cases"
exit 1
