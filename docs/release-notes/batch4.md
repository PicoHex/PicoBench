# PicoBench release notes — Batch 4 (support baseline: SDK 10+ / Roslyn 5.9+)

These notes are for **package consumers**: they describe what a package built
from `main` after the baseline change does differently, and what you have to do
about it. They say nothing about how the change is implemented — the reference
manual is the README (*Requirements*).

The change closes a promise the repository no longer keeps. PicoBench used to
promise that its source generator loads in the **.NET SDK 8** compiler
(**Roslyn 4.8**), and a `tests/compat/ConsumerSdk8` leg existed to prove it. The
generator is now built against **Roslyn 5.9+**, so that promise is withdrawn:
the minimum is the **.NET SDK 10 or newer** (`net10.0` / `net11.0`).

## At a glance

| # | Change | Section |
|---|---|---|
| 1 | **Breaking:** the minimum build/host SDK is now 10 (`net10.0` / `net11.0`); the generator needs Roslyn 5.9+ | [1](#1-the-minimum-sdk-is-now-10) |
| 2 | Nothing else moved: the `netstandard2.0` *runtime* floor, the C# 7.3 emission and every public member are unchanged | [2](#2-what-did-not-change) |
| 3 | The old SDK 8 leg and the Roslyn 4.8 ceiling guard are gone; a floor guard replaces them | [3](#3-the-retired-legs-and-their-replacement) |

---

## 1. The minimum SDK is now 10

**Affected API / output**

- **Source generator:** `PicoBench.Generators` ships compiled against
  `Microsoft.CodeAnalysis.CSharp` **5.9.x**, the compiler band that ships with
  the .NET SDK 10. A host whose compiler is older cannot load it: the compiler
  skips the analyzer (a **`CS9057`** warning) and the generated
  `RunBenchmarksAsync` / typed entry points are then missing (`CS1061`-style
  errors in a file you never wrote).
- **Build and host floor:** **.NET SDK 10 or newer**. SDK 10 (`net10.0`) is the
  leg every CI job sets up; `global.json` accepts any installed SDK of major 10
  or newer (`rollForward: latestMajor`, `allowPrerelease: false`), which is what
  makes the `net11.0` half of the statement true without a file edit — and what
  keeps a floating preview SDK from ever being selected.

**Migration**

- **On SDK 8 or 9: upgrade the SDK before taking this package version.** There
  is no workaround inside PicoBench — the rule belongs to the host, not to the
  package. Pinning an older `Microsoft.CodeAnalysis.CSharp` in your own build has
  the same effect as an old host: the generator is skipped and the generated
  members are missing.
- A package reference alone does not raise your build floor for you. If your
  solution has a `global.json` pinning SDK 8 or 9, raise it as part of the
  upgrade; otherwise the next `dotnet build` is the first place the change
  surfaces.
- Everything that worked on SDK 10 before this batch keeps working unchanged.

**Why it is deliberate**

Roslyn 5.9 is the compiler the repository builds and tests against; keeping a
4.8 ceiling meant maintaining an emission path for a compiler that no supported
SDK ships any more, and a CI leg that existed only to prove a promise nobody was
asking for. The floor now matches the toolchain the package is actually
validated on.

## 2. What did *not* change

- **Runtime targets.** The package still targets `netstandard2.0`, so the
  compiled assembly still runs on **.NET Framework 4.7.2+**, **.NET Core 2.0+**
  and **.NET 5+**. Those are *runtime* floors; each of those consumers now
  builds with **SDK 10 or newer**. The READMEs say both, side by side, because a
  runtime floor read as a build floor is exactly the confusion this batch fixes.
- **Generated code language version.** The emission is still **C# 7.3-compatible**
  (`#nullable` and the nullable annotation appear only for a C# 8+ consumer), so a
  .NET Framework consumer still compiles with the language version the SDK
  derives for it.
- **Public API surface.** No member was removed and no signature changed, so the
  `api/` baseline is unchanged. The break is in the *platform a consumer builds
  on*, not in the API.

## 3. The retired legs and their replacement

Removed, on purpose:

| gone | was |
|---|---|
| `tests/compat/ConsumerSdk8/` | a `net8.0` consumer with its own `global.json` pinning SDK 8 |
| the `consumer-sdk8` CI job | built that consumer and ran it |
| the Roslyn 4.8 **ceiling** guard | failed if the generator asked for more than 4.8 |

Added instead — a **floor** guard, `tests/PicoBench.Tests/Batch4/SdkBaselineTests.cs`:

- it reads `Directory.Packages.props` and fails if `Microsoft.CodeAnalysis.CSharp`
  drops below **5.9**, reading a floating band (`5.9.*`), a fixed version
  (`5.9.0`), a range and a prerelease the same way — NuGet's floor is where the
  band starts, not what it resolved to on this machine;
- it scans every build file in the repository and fails if *any* of them pins the
  package below 5.9, because a downgrade does not have to happen in the central
  file to take effect;
- it fails if the English README stops naming the **SDK 10** baseline and the
  **Roslyn 5.9** floor, and if any of the **ten** READMEs promises an older SDK
  (`SDK 8`, `SDK 9`, `Roslyn 4.8`, `4.8+`, `8.0.100`).

The guard is red on the retracted state and green on the new one: setting the
central version back to `4.8` fails two of its tests, and deleting the "SDK 10"
wording from `README.md` fails the two README tests.

## Versioning note

This is a **deliberate incompatible change** for consumers still on the .NET SDK
8 or 9: the package no longer loads in that compiler. No public member moved, so
by the PicoHex version rule (the API baseline in `api/` decides whether the `x`
or the `y` segment increments) the next release takes the non-API segment — yet
the raise of a *build* floor is breaking all the same, which is why it ships with
this note rather than as a silent dependency bump. Read this file, not the API
delta, when deciding whether the upgrade is safe.
