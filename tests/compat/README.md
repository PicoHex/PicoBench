# Compatibility smoke tests

These projects are deliberately not part of `PicoBench.slnx`: they target
frameworks the rest of the repository cannot build on every OS.

| project | targets | why |
|---|---|---|
| `PicoBench.Net4xSmoke` | `net472`, `net48` | the README promises `netstandard2.0` = ".NET Framework **4.7.2+**"; this is the only place that promise is compiled *and* executed (`net4x-smoke` CI job, windows-latest) |
| `ConsumerSdk8` | `net8.0`, own `global.json` pinning SDK 8 | proves the generator runs inside the SDK 8 compiler (Roslyn 4.8). A generator built against a newer Roslyn is silently skipped there - CS9057 warning, then CS1061 in the consumer (`consumer-sdk8` job) |
| `ConsumerNetStandard20` | `netstandard2.0` library | proves another netstandard2.0 library can compile the *generated* code, not just the library itself (the `ValueTask.FromResult` regression was exactly this) (`consumer-netstandard20` job) |

`ConsumerSdk8` and `ConsumerNetStandard20` consume the package from a local feed,
so they take the same path a real consumer takes:
`dotnet restore <project> --source <feed> -p:PicoBenchVersion=<version>`.

Run locally (Windows, needs the .NET Framework 4.7.2/4.8 targeting packs):

```powershell
dotnet build tests/compat/PicoBench.Net4xSmoke/PicoBench.Net4xSmoke.csproj -c Release
dotnet run --project tests/compat/PicoBench.Net4xSmoke/PicoBench.Net4xSmoke.csproj -c Release -f net472
dotnet run --project tests/compat/PicoBench.Net4xSmoke/PicoBench.Net4xSmoke.csproj -c Release -f net48
```

Each run prints `Framework: <version>` and `Benchmarks: 2`, and exits non-zero
unless the generated benchmark class produced its two results.

## The C# 8 requirement this project uncovered is gone (G20)

The source generator used to emit `#nullable enable` and `config ??= ...`, so the
generated code needed **C# 8**. The SDK derives **C# 7.3** for .NET Framework and
netstandard2.0 targets, and a default `net48` consumer project therefore failed
with

```
error CS8370: Feature 'nullable reference types' is not available in C# 7.3.
```

before it saw a single PicoBench diagnostic - the error named neither PicoBench
nor the generator. `PicoBench.Net4xSmoke` worked around it with
`<LangVersion>8.0</LangVersion>`.

**The generated code is now C# 7.3-compatible**, so that pin is gone:
`PicoBench.Net4xSmoke` carries no `LangVersion` at all and therefore builds at
the SDK's derived C# 7.3 - which is the case a real .NET Framework consumer hits.
The three constructs were removed like this:

| was | is now |
|---|---|
| `#nullable enable` (always) | emitted only when the consumer compiles at C# 8+ |
| `BenchmarkConfig? config = null` (always) | `?` emitted only at C# 8+, annotation-free `BenchmarkConfig config = null` at 7.3 |
| `config ??= BenchmarkConfig.Default` | `config = config ?? BenchmarkConfig.Default` |

The first two are decided from the **consumer's compilation language version**
(`Compilation.LanguageVersion >= LanguageVersion.CSharp8`), not from a
preprocessor symbol: the SDK does not define a `CSHARP_8_OR_GREATER` symbol
(measured - a probe `#if CSHARP_8_OR_GREATER` stays inactive even on net10.0), so
a symbol guard would silently pin every consumer to the low branch. Deciding at
generation time keeps a C# 8+ consumer on exactly the code it had (nullable
context + annotated signature, so no `CS8632` / `CS8767` / `CS8625`) and gives a
7.3 consumer code that compiles.

`tests/PicoBench.Tests/Batch3/GeneratedCodeLanguageVersionTests.cs` is the
compile-level guard: it feeds the generated trees into a compilation at
`LanguageVersion.CSharp7_3` (`AddSyntaxTrees` + `GetDiagnostics`) and requires
zero errors, alongside positive cases at C# 8 / latest, a check that the emission
really follows the consumer's language version (no `#nullable` / no `?` at 7.3,
both present at C# 8), and a check that a nullable-enabled C# 8 consumer is free
of `CS8632` / `CS8767` / `CS8625`.

`ConsumerNetStandard20` still pins `<LangVersion>8.0</LangVersion>`, but now for
a different reason: **its own** `<Nullable>enable</Nullable>` cannot be combined
with the C# 7.3 the SDK derives for netstandard2.0 (`CS8630`, a compiler error
about the project's option, not about generated code). That project deliberately
stands in for a nullable-aware consumer.
