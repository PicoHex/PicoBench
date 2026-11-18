#!/usr/bin/env bash
# Contract: a workflow step that is supposed to run a gate script must RUN it.
#
# H2-A8' of .review-2026-11/ablation-consolidated.md. Measured: workflow_lint.py
# judged the ancestry guard with `re.search(r"verify-release-commit-on-main\.sh",
# text)`, i.e. against the whole file, comments included. Rewriting that step's
# `run:` as `echo "skipped: ./scripts/verify-release-commit-on-main.sh"` - the
# check really disabled, the string left behind - kept the lint at exit 0 and the
# suite green, so a release could pack a side-branch commit and publish it to
# nuget.org while release.yml still appeared to call the script.
#
# The lint now parses the step structure and asks whether the path is the command
# word of some step's `run:` body (`step_runs` / `invoked_scripts`). This file
# drives that decision red and green on throwaway copies of the real workflows:
#
#   green  the repository's own workflows
#   red    `run: echo "skipped: ./scripts/verify-release-commit-on-main.sh"`
#   green  the invocation restored
#   red    a commented-out `run:` that still spells the path out
#   green  `run: bash scripts/verify-release-commit-on-main.sh` (same script)
#   red    the step gone entirely (the A8 ablation: no mention at all)
#   red    a CI job's gate script echoed instead of run
#   green  that CI gate restored
#
# Exit code: 0 when every case behaves, 1 otherwise.
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

# CI runs this lint as `python3`; this machine may only answer to `python`.
# Redirecting to /dev/null is not enough to detect the Windows App Execution
# Alias stub, which exits 0 and prints nothing, so the probe needs real output.
python_works() { "$1" -c 'print(2)' 2>/dev/null | grep -qx 2; }
py=""
for candidate in python3 python py; do
    if python_works "$candidate"; then
        py="$candidate"
        break
    fi
done
if [ -z "$py" ]; then
    printf 'FAIL: no working python interpreter (tried python3, python, py)\n'
    exit 1
fi

# lint() echoes the lint's own output and returns its exit code.
lint() { "$py" "$root/scripts/tests/workflow_lint.py" --workflows-dir "$1"; }

# swap_line() replaces exactly one line of a file, and fails (3) when the line is
# not there - so a fixture mutation can never pass by silently matching nothing.
swap_line() {
    local file="$1" old="$2" new="$3" tmp
    tmp="$work/swap.tmp"
    awk -v old="$old" -v new="$new" '
        { line = $0 }
        line == old { print new; found++; next }
        { print }
        END { if (found != 1) exit 3 }
    ' "$file" > "$tmp" || return 3
    mv "$tmp" "$file"
}

# The fixture is a copy of the real workflows, with CRLF folded to LF so the
# line-exact mutations below behave on a Windows checkout as well.
fixture="$work/workflows"
mkdir -p "$fixture"
cp -R "$root/.github/workflows/." "$fixture/"
for file in "$fixture"/*.yml "$fixture"/*.yaml; do
    [ -f "$file" ] || continue
    tr -d '\r' < "$file" > "$work/lf.tmp" && mv "$work/lf.tmp" "$file"
done
release="$fixture/release.yml"
ci="$fixture/ci.yml"

VERIFY_RUN='        run: ./scripts/verify-release-commit-on-main.sh'
VERIFY_ECHO='        run: echo "skipped: ./scripts/verify-release-commit-on-main.sh"'
VERIFY_COMMENTED='        run: # ./scripts/verify-release-commit-on-main.sh is disabled'
VERIFY_BASH='        run: bash scripts/verify-release-commit-on-main.sh'
VERIFY_GONE='        run: echo "nothing to see here"'
LINT_RUN='        run: python3 scripts/tests/workflow_lint.py'
LINT_ECHO='        run: echo "python3 scripts/tests/workflow_lint.py"'

# ------------------------------------------------- 1. the real workflows
case_ "the repository's own workflows pass"
if output="$(lint "$root/.github/workflows")"; then
    ok "exit 0"
else
    no "the lint is red on the checked-in workflows:"
    printf '%s\n' "$output" | sed 's/^/      /'
fi

# ---------------------------------------------- 2. the echo bypass (H2-A8')
case_ "an echo'd ancestry guard is red, not a passing mention"
if swap_line "$release" "$VERIFY_RUN" "$VERIFY_ECHO"; then
    if output="$(lint "$fixture")"; then
        no "the lint passed while the ancestry guard only echoes its own path:"
        printf '%s\n' "$output" | sed 's/^/      /'
    else
        if printf '%s' "$output" | grep -q 'mentioned but never executed'; then
            ok "rejected the echo and said the path is never executed"
        else
            no "rejected the echo for the wrong reason:"
            printf '%s\n' "$output" | sed 's/^/      /'
        fi
    fi
else
    no "fixture mutation did not apply (release.yml has no '$VERIFY_RUN')"
fi

# ------------------------------------------- 3. the invocation comes back
case_ "restoring the real invocation is green again"
if swap_line "$release" "$VERIFY_ECHO" "$VERIFY_RUN"; then
    if output="$(lint "$fixture")"; then
        ok "exit 0"
    else
        no "the lint stayed red after the invocation was restored:"
        printf '%s\n' "$output" | sed 's/^/      /'
    fi
else
    no "fixture mutation did not apply (the echo line is gone)"
fi

# ------------------------------------- 4. commented out, path still there
case_ "a commented-out run: that names the script is red"
if swap_line "$release" "$VERIFY_RUN" "$VERIFY_COMMENTED"; then
    if output="$(lint "$fixture")"; then
        no "a step with no runnable body passed:"
        printf '%s\n' "$output" | sed 's/^/      /'
    else
        ok "rejected the comment-only run"
    fi
    swap_line "$release" "$VERIFY_COMMENTED" "$VERIFY_RUN" \
        || no "could not restore the invocation after case 4"
else
    no "fixture mutation did not apply (case 4)"
fi

# ---------------------------------- 5. `bash scripts/...` is an invocation
case_ "the same script run through bash counts as an invocation"
if swap_line "$release" "$VERIFY_RUN" "$VERIFY_BASH"; then
    if output="$(lint "$fixture")"; then
        ok "exit 0"
    else
        no "an interpreter invocation was not recognised:"
        printf '%s\n' "$output" | sed 's/^/      /'
    fi
    swap_line "$release" "$VERIFY_BASH" "$VERIFY_RUN" \
        || no "could not restore the invocation after case 5"
else
    no "fixture mutation did not apply (case 5)"
fi

# ------------------------------------------- 6. the A8 ablation is still red
case_ "deleting the step entirely (A8) is still red"
if swap_line "$release" "$VERIFY_RUN" "$VERIFY_GONE"; then
    if output="$(lint "$fixture")"; then
        no "a workflow with no ancestry guard at all passed:"
        printf '%s\n' "$output" | sed 's/^/      /'
    else
        if printf '%s' "$output" | grep -q 'never calls scripts/verify-release-commit-on-main.sh'; then
            ok "rejected the missing step"
        else
            no "rejected it for the wrong reason:"
            printf '%s\n' "$output" | sed 's/^/      /'
        fi
    fi
    swap_line "$release" "$VERIFY_GONE" "$VERIFY_RUN" \
        || no "could not restore the invocation after case 6"
else
    no "fixture mutation did not apply (case 6)"
fi

# ------------------------------ 7. a CI job's gate script, echoed not run
case_ "a CI gate script that is echoed instead of executed is red"
if swap_line "$ci" "$LINT_RUN" "$LINT_ECHO"; then
    if output="$(lint "$fixture")"; then
        no "a job whose gate script is only echoed passed:"
        printf '%s\n' "$output" | sed 's/^/      /'
    else
        if printf '%s' "$output" | grep -q 'never runs scripts/tests/workflow_lint.py'; then
            ok "rejected the echoed gate and named the job and the script"
        else
            no "rejected the echoed gate for the wrong reason:"
            printf '%s\n' "$output" | sed 's/^/      /'
        fi
    fi
    swap_line "$ci" "$LINT_ECHO" "$LINT_RUN" \
        || no "could not restore the gate after case 7"
else
    no "fixture mutation did not apply (case 7)"
fi

case_ "the restored gate is green again"
if output="$(lint "$fixture")"; then
    ok "exit 0"
else
    no "the lint stayed red on the untouched copy of the workflows:"
    printf '%s\n' "$output" | sed 's/^/      /'
fi

printf '\n'
if [ "$failures" -eq 0 ]; then
    printf 'workflow lint structure: %d case(s), all ok\n' "$cases"
    exit 0
fi
printf 'workflow lint structure: %d failure(s) across %d cases\n' "$failures" "$cases"
exit 1
