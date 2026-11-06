#!/usr/bin/env bash
# Falsification harness for the AOT job's "a trim/AOT warning is an error" claim
# (review item N6 / Batch 3.5).
#
# The `aot-validation` job turns IL2xxx / IL3xxx into errors with three MSBuild
# properties, but nothing exercised that: the claim rested on a static string in
# ci.yml, so dropping a `-p:` flag or an SDK rename would leave the job green
# forever. This probe publishes tests/aot-negative twice with the CI flag set:
#
#   * AotProgram=Control  - the benign `Benchmarks: 2` program: MUST publish;
#   * AotProgram=Negative - the same program plus `Assembly.LoadFrom`
#                           (RequiresUnreferencedCode -> IL2026): MUST fail, and
#                           must name IL2026.
#
# The control is what makes the negative half meaningful: without it, a mistyped
# publish command would "pass" by failing for the wrong reason. The probe also
# asserts the flag set it uses still appears in ci.yml, so it tests the command
# CI runs rather than one invented here.
#
# Local run:
#   bash scripts/tests/aot-negative-probe.test.sh            # RID = host RID
#   AOT_RID=linux-x64 bash scripts/tests/aot-negative-probe.test.sh
#   AOT_SKIP_PUBLISH=1 bash scripts/tests/aot-negative-probe.test.sh   # wiring only
#   VERBOSE=1 ... to stream the publish logs.
#
# In CI it is the last step of the `aot-validation` job, on the only leg whose
# matrix RID matches the runner (win-x64), which is where the flags are identical.
set -uo pipefail

# The two publishes run back to back over one project. A worker node left
# behind by the first can exit under the second and surface as MSB4166
# ("child node exited prematurely"), which would read as a fixture failure.
# Determinism matters more than reuse in a probe whose job is to be trusted.
export MSBUILDDISABLENODEREUSE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
fixture="$root/tests/aot-negative"
ci_yml="$root/.github/workflows/ci.yml"

failures=0
cases=0

case_() {
    cases=$((cases + 1))
    printf '== %s\n' "$1"
}

ok() { printf '   ok: %s\n' "$1"; }
no() {
    printf '   FAIL: %s\n' "$1"
    failures=$((failures + 1))
}

# The contract: the flags ci.yml must keep passing. Written out literally so that
# deleting a flag from AOT_PUBLISH_FLAGS below cannot silently shrink it.
EXPECTED_AOT_FLAGS=(
    -p:PublishAot=true
    -p:TreatWarningsAsErrors=true
    -p:ILLinkTreatWarningsAsErrors=true
    -p:IlcTreatWarningsAsErrors=true
)

# What the probe actually publishes with.
AOT_PUBLISH_FLAGS=(
    -p:PublishAot=true
    -p:TreatWarningsAsErrors=true
    -p:ILLinkTreatWarningsAsErrors=true
    -p:IlcTreatWarningsAsErrors=true
)

# ------------------------------------------------------------ the wiring under test
case_ "the probe publishes with exactly the flags CI passes"
if [ ! -f "$ci_yml" ]; then
    no "ci.yml not found at $ci_yml"
else
    for flag in "${EXPECTED_AOT_FLAGS[@]}"; do
        if grep -qF -- "$flag" "$ci_yml"; then
            ok "ci.yml still passes $flag"
        else
            no "ci.yml no longer passes $flag - the probe tests a command CI does not run"
        fi
        if printf '%s
' "${AOT_PUBLISH_FLAGS[@]}" | grep -qxF -- "$flag"; then
            ok "the probe publishes with $flag"
        else
            no "the probe dropped $flag - its publish no longer matches ci.yml"
        fi
    done
fi

case_ "the negative fixture exists and is not in the solution"
if [ -f "$fixture/PicoBench.AotNegative.csproj" ] && [ -f "$fixture/Negative.cs" ] && [ -f "$fixture/Control.cs" ]; then
    ok "tests/aot-negative is present (csproj + Control.cs + Negative.cs)"
else
    no "the negative fixture is incomplete under $fixture"
fi
if grep -qF "aot-negative" "$root/PicoBench.slnx" 2>/dev/null; then
    no "the fixture is referenced from PicoBench.slnx; it must stay out of the solution"
else
    ok "the fixture stays out of PicoBench.slnx"
fi

if [ "${AOT_SKIP_PUBLISH:-0}" = "1" ]; then
    printf '\n'
    if [ "$failures" -eq 0 ]; then
        printf 'aot negative probe: wiring only (AOT_SKIP_PUBLISH=1), publish checks not run\n'
        exit 0
    fi
    printf 'aot negative probe: %d failure(s) in the wiring\n' "$failures"
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    printf 'error: dotnet is required to publish the AOT fixtures\n' >&2
    exit 1
fi

rid="${AOT_RID:-}"
if [ -z "$rid" ]; then
    rid="$(dotnet --info 2>/dev/null | sed -n 's/^[[:space:]]*RID:[[:space:]]*//p' | head -n1)"
fi
if [ -z "$rid" ]; then
    printf 'error: could not determine a RID; set AOT_RID explicitly\n' >&2
    exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Publishes the fixture for $AotProgram and writes the combined log to $2.
publish() {
    local program="$1" log="$2"
    dotnet publish "$fixture/PicoBench.AotNegative.csproj" \
        --configuration Release --framework net10.0 --runtime "$rid" --self-contained \
        -p:AotProgram="$program" "${AOT_PUBLISH_FLAGS[@]}" >"$log" 2>&1
}

# ------------------------------------------------------------------- control half
case_ "the benign AOT program still publishes (the command itself works)"
if publish Control "$work/control.log"; then
    ok "Control published (exit 0)"
else
    no "Control FAILED to publish - the probe command is wrong, not the fixture"
    tail -n 20 "$work/control.log" | sed 's/^/      /'
fi
if [ -f "$work/control.log" ] && grep -qF -- 'IL2026' "$work/control.log"; then
    no "the benign control produced IL2026 - the fixture does not isolate the call"
else
    ok "the benign control carries no IL2026"
fi

# ------------------------------------------------------------------ negative half
case_ "the trim-hostile program must fail to publish with IL2026"
if publish Negative "$work/negative.log"; then
    no "Negative published successfully - IL2xxx is NOT being treated as an error"
    printf '      (an AOT publish calling Assembly.LoadFrom should have been rejected)\n'
else
    ok "Negative failed to publish (exit non-zero)"
fi
if grep -qF -- 'IL2026' "$work/negative.log"; then
    ok "the failure names IL2026"
else
    no "the failure did not mention IL2026 - it failed for some other reason"
    tail -n 20 "$work/negative.log" | sed 's/^/      /'
fi

if [ "${VERBOSE:-0}" = "1" ]; then
    printf '\n--- control log ---\n'
    cat "$work/control.log"
    printf '\n--- negative log ---\n'
    cat "$work/negative.log"
fi

printf '\n'
if [ "$failures" -eq 0 ]; then
    printf 'aot negative probe: %d checks, all ok (RID %s)\n' "$cases" "$rid"
    exit 0
fi
printf 'aot negative probe: %d failure(s) across %d checks\n' "$failures" "$cases"
exit 1
