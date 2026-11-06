# AOT negative fixture

The `aot-validation` job claims that "a trim or AOT warning fails the job" and
turns IL2xxx / IL3xxx into errors through three MSBuild properties. Until this
fixture existed, that claim had no falsification path: it was a static string in
`ci.yml`, so dropping a `-p:` flag or an SDK rename would have left the job green
forever (review item N6 / Batch 3.5).

## What is here

One project, `PicoBench.AotNegative.csproj`, that consumes `PicoBench` the same
way the job's temporary app does, with the program selected by `$(AotProgram)`:

| `AotProgram` | source | required publish result |
|---|---|---|
| `Control` | `Control.cs` | succeeds (the benign `Benchmarks: 2` program) |
| `Negative` | `Negative.cs` | **fails**, naming `IL2026` |

`Negative.cs` differs from `Control.cs` by one line: `Assembly.LoadFrom`, which
carries `RequiresUnreferencedCodeAttribute`. The control is what makes the
negative half meaningful - without it, a mistyped publish command would "pass" by
failing for the wrong reason.

`AOT_PUBLISH_FLAGS` lives on the command line in the harness, exactly where it
lives in `ci.yml`, and the harness additionally asserts those flags still appear
in `ci.yml`. The fixture therefore cannot mask the flags it is meant to test.

## Run it locally

```bash
# host RID (needs an AOT-capable SDK, the ILCompiler for the platform, and on
# Linux clang + zlib1g-dev):
bash scripts/tests/aot-negative-probe.test.sh

# a specific RID / wiring only / step-by-step output:
AOT_RID=linux-x64 bash scripts/tests/aot-negative-probe.test.sh
AOT_SKIP_PUBLISH=1 bash scripts/tests/aot-negative-probe.test.sh
VERBOSE=1 bash scripts/tests/aot-negative-probe.test.sh
```

The probe prints one line per check and exits non-zero on the first inconsistency.
`AOT_SKIP_PUBLISH=1` skips the two publishes and only verifies the wiring, which
is enough to catch a renamed flag or a deleted step.

## In CI

It is the last step of the `aot-validation` job, on the `win-x64` leg only: that
is the one runner whose RID matches its own matrix entry, so the local publish is
native. Running a foreign-RID AOT publish on all five legs would buy wall time,
not coverage.

The step itself has been executed locally (see the probe output); the GitHub
runner orchestration around it has **not** been executed from this environment.
