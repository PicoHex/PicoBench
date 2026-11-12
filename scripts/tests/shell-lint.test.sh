#!/usr/bin/env bash
# Lint every shell script this repository ships (review item R5).
#
# Nothing compiles a shell script, so a typo in one is invisible until release
# day. Three checks, cheapest first:
#
#   1. `bash -n` on every script - a real syntax failure is always caught.
#   2. the R5 shape: an unquoted backslash-n where a line continuation was
#      meant. `bash -n` accepts it (a backslash before a plain `n` is a legal
#      escape: the backslash is dropped and the letter kept), so the list
#      silently iterates the wrong words - which is exactly how two tag cases
#      were lost. The scan is plain bash and needs no external tool.
#   3. `shellcheck`, when it happens to be installed. shellcheck is NOT vendored
#      and a build machine may have no network, so a missing binary is reported
#      as an explicit SKIP that names every unlinted file - never a silent pass.
#      Set SHELL_LINT_REQUIRE_SHELLCHECK=1 (ci.yml does) to turn a missing
#      binary into a failure, so CI cannot quietly lose the check.
#
# Note for editors: never start a comment line with the bare word shellcheck.
# ShellCheck reads `# shellcheck ...` as a directive, and a directive whose
# first key carries no `=` is SC1073 + SC1072 - error level, so the gate would
# fail on its own prose. Keep the word inside the sentence, as these lines do.
#
# Exit code: 0 when every executed check passes, 1 otherwise.
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

# Echoes "<file>:<line>: <text>" for every whitespace-delimited word that is
# exactly the two characters backslash and n. That is the R5 shape: it looks
# like a continuation, is not one, and bash turns it into a word of its own.
# The inside of a quoted string is never word-split, so the legitimate uses are
# safe by construction: in printf 'ok\n' the word is `ok\n`, never `\n`, and
# $'multi\nline' is one word too. A full-line comment is skipped explicitly,
# because a comment may legitimately name the two characters.
#
# Returns 0 when at least one hit was printed, 1 otherwise.
scan_literal_n() {
    local file="$1" line word stripped
    local -i number=0
    local -i found=1
    while IFS= read -r line || [ -n "$line" ]; do
        number+=1
        stripped="${line#"${line%%[![:space:]]*}"}"
        case "$stripped" in \#*) continue ;; esac
        read -r -a words <<< "$line"
        for word in "${words[@]}"; do
            if [ "$word" = '\n' ]; then
                printf '%s:%d: %s\n' "$file" "$number" "$line"
                found=0
                break
            fi
        done
    done < "$file"
    return "$found"
}

# Every shell script in the repository, minus the build output directories that
# are not source. `scripts/` and `tools/` are the two roots that ship scripts; a
# new root is a deliberate decision, not a directory that escapes the lint.
mapfile -t scripts < <(
    cd "$root" || exit 1
    find scripts tools -type f -name '*.sh' -not -path '*/artifacts/*' | LC_ALL=C sort
)
if [ "${#scripts[@]}" -eq 0 ]; then
    echo "error: no shell scripts found under scripts/ or tools/ - the find is broken" >&2
    exit 1
fi

# ---------------------------------------------------------------- 1. bash -n
case_ "every script parses (bash -n)"
for script in "${scripts[@]}"; do
    if output="$(bash -n "$root/$script" 2>&1)"; then
        ok "$script"
    else
        no "$script does not parse: $output"
    fi
done

# ------------------------- 2. an unquoted backslash-n standing in for a continuation
case_ "no line continuation is written as an unquoted backslash-n"
flagged=0
for script in "${scripts[@]}"; do
    if hits="$(scan_literal_n "$root/$script")"; then
        no "$script has an unquoted backslash-n where a line continuation was meant:"
        printf '%s\n' "$hits" | sed 's/^/      /'
        flagged=1
    fi
done
if [ "$flagged" -eq 0 ]; then
    ok "no unquoted backslash-n in ${#scripts[@]} script(s)"
fi

# The scan above is only worth anything if it can fail, so feed it the exact
# pre-0eb3071 shape of scripts/tests/release-tag-validation.test.sh: a for-list
# with real backslash continuations, one line carrying the two characters as a
# word, and a comment that names them and must not be reported. The fixture is
# written with printf rather than a heredoc so that this file does not itself
# carry the token being searched for.
backslash="\\"
{
    printf '%s\n' '#!/usr/bin/env bash'
    printf '%s\n' "# a comment may name ${backslash}n without being the bug"
    printf '%s\n' "for bad in ${backslash}"
    printf '%s\n' "    'v2026.4' ${backslash}"
    printf '%s\n' "    'v2026.4.0-beta' ${backslash}"
    printf '%s\n' "    'v 2026.4.0' ${backslash}n    'v2026.4.0 '; do"
    printf '%s\n' "    printf '   %s${backslash}n' \"\$bad\""
    printf '%s\n' 'done'
} > "$work/r5-shape.sh"
expected_line=6
hits="$(scan_literal_n "$work/r5-shape.sh")"
size="$(printf '%s\n' "$hits" | grep -c .)"
if [ "$size" -eq 1 ] && printf '%s' "$hits" | grep -q ":${expected_line}: "; then
    ok "the scan still flags the R5 shape (line $expected_line, nothing else)"
else
    no "the scan did not flag the R5 fixture exactly once at line $expected_line:"
    printf '%s\n' "$hits" | sed 's/^/      /'
fi

{
    printf '%s\n' '#!/usr/bin/env bash'
    printf '%s\n' "printf 'ok: a${backslash}n'"
    printf '%s\n' "printf \"ok: b${backslash}n\""
    printf '%s\n' "printf '%s' \$'multi${backslash}nline'"
    printf '%s\n' "word='${backslash}n'"
    printf '%s\n' "grep -q -e 'a${backslash}nb' \"\$1\""
    printf '%s\n' "for x in ${backslash}"
    printf '%s\n' "    a ${backslash}"
    printf '%s\n' "    b; do echo \"\$x\"; done"
} > "$work/legitimate.sh"
if hits="$(scan_literal_n "$work/legitimate.sh")"; then
    no "a legitimate script was flagged:"
    printf '%s\n' "$hits" | sed 's/^/      /'
else
    ok "quoted, escaped and continued forms are not flagged"
fi

# ------------------------------------------------------------ 3. shellcheck
case_ "shellcheck (external, optional)"
if command -v shellcheck >/dev/null 2>&1; then
    version="$(shellcheck --version 2>/dev/null | awk '/^version:/ {print $2}')"
    printf '   shellcheck %s\n' "${version:-unknown}"
    for script in "${scripts[@]}"; do
        if output="$(shellcheck --external-sources -- "$root/$script" 2>&1)"; then
            ok "shellcheck $script"
        else
            no "shellcheck $script"
            printf '%s\n' "$output" | sed 's/^/      /'
        fi
    done
else
    # Deliberately loud: these lines are the only evidence that the shellcheck
    # half of this file did not run.
    printf '   SKIP: shellcheck is not installed - %d file(s) NOT linted:\n' "${#scripts[@]}"
    for script in "${scripts[@]}"; do
        printf '         - %s\n' "$script"
    done
    # shellcheck disable=SC2016  # literal backticks in a message, not a substitution
    printf '   SKIP: install it (e.g. `apt-get install -y shellcheck`) and re-run this script\n'
    if [ "${SHELL_LINT_REQUIRE_SHELLCHECK:-0}" = "1" ]; then
        no "SHELL_LINT_REQUIRE_SHELLCHECK=1 but shellcheck is not installed"
    fi
fi

printf '\n'
if [ "$failures" -eq 0 ]; then
    printf 'shell lint: %d case(s), all ok\n' "$cases"
    exit 0
fi
printf 'shell lint: %d failure(s) across %d cases\n' "$failures" "$cases"
exit 1
