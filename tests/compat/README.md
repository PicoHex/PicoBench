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

## Known requirement this project uncovered: C# 8 or later

The source generator emits `#nullable enable` and `config ??= ...`, so the
generated code needs **C# 8**. The SDK derives **C# 7.3** for .NET Framework
targets, which means a default `net48` consumer project fails with

```
error CS8370: Feature 'nullable reference types' is not available in C# 7.3.
```

before it sees a single PicoBench diagnostic - the error names neither PicoBench
nor the generator. `PicoBench.Net4xSmoke` therefore pins `<LangVersion>8.0</LangVersion>`,
which is the floor the generated code actually needs. Making the generator emit
7.3-compatible code (avoid `#nullable`, avoid `??=`) or reporting the requirement
as a diagnostic is tracked separately; until then every .NET Framework consumer
needs `<LangVersion>8.0</LangVersion>` or later.
