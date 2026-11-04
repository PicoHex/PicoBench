# PicoBench

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoBench/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoBench.svg)](https://www.nuget.org/packages/PicoBench)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A lightweight benchmarking library for .NET with **two complementary APIs**: an imperative API and an attribute-based, source-generated API that is fully **AOT-compatible**. No third-party dependencies — the only NuGet reference is a .NET BCL polyfill for `ValueTask<T>` on netstandard2.0.

## Features

- **Zero Third-Party Dependencies** — Only a single BCL polyfill (`System.Threading.Tasks.Extensions`) to backport `ValueTask<T>` to netstandard2.0. No external packages.
- **Two APIs** - Imperative (`Benchmark.Run`) for ad-hoc tests; attribute-based (`[Benchmark]` + source generator) for structured suites
- **AOT-Compatible Source Generator** - The incremental generator emits direct method calls with zero reflection at runtime
- **Cross-Platform** - Full support for Windows, Linux, and macOS
- **Sub-Microsecond Timing** - `Stopwatch`-based per-operation timings with the harness' own per-operation cost measured and deducted. A workload below the run's resolution limit is reported as an upper bound (`<limit`) and flagged `ResolutionLimited` — never as a nanosecond-accurate number
- **Disclosed Limits** - `OverheadNs`, `ResolutionLimitNanoseconds`, `ResolutionLimited` and `Unmeasured` say what a run could and could not resolve, so a noise-level result cannot be read as a fast operation
- **GC Tracking** - Monitors Gen0/Gen1/Gen2 collection counts during benchmarks (setup/teardown excluded)
- **CPU Cycle Counting** - Thread-scoped cycle counts on the synchronous path (Windows `QueryThreadCycleTime`; Linux `perf_event` when the OS permits unprivileged access). Asynchronous benchmarks read a process-wide counter, because a per-thread counter cannot be followed across thread hops, and are flagged `CpuCyclesAreApproximate` (`~` in all four formats). macOS has no cycle counter: `mach_absolute_time` is a monotonic *time* proxy, not cycles, and is flagged the same way
- **Statistical Analysis** - Mean, Median, P90, P95, P99, Min, Max, StdDev, StdErr, and relative standard deviation
- **Multiple Output Formats** - Four built-in formatters (Console, Markdown, HTML, CSV) plus programmatic summary output
- **Parameterised Benchmarks** - `[Params]` attribute with automatic Cartesian product iteration
- **Comparison Support** - Baseline vs candidate measured in interleaved rounds; the verdict is the median of the paired per-round ratios and needs both a 5% dead band and a distribution-free interval that excludes parity, so round-to-round noise is not published as an improvement
- **Configurable** - Quick, Default, and Precise presets, auto-calibration, or fully custom configuration
- **netstandard2.0** - Compatible with .NET Framework **4.7.2+** (`RuntimeInformation` is used unconditionally, so earlier .NET Framework versions are not supported), .NET Core 2.0+, .NET 5+

## Installation

Reference the **PicoBench** NuGet package. The source generator (`PicoBench.Generators`) is bundled automatically as an analyzer - no extra reference needed.

```bash
dotnet add package PicoBench
```

## Quick Start

### Imperative API

```csharp
using PicoBench;

var result = Benchmark.Run("My Benchmark", () =>
{
    Thread.SpinWait(100);
});

Console.WriteLine($"Average: {result.Statistics.Avg:F1} ns/op");
```

### Attribute-Based API (Source-Generated)

```csharp
using PicoBench;

var suite = BenchmarkRunner.Run<MyBenchmarks>();
Console.WriteLine(new PicoBench.Formatters.ConsoleFormatter().Format(suite));

[BenchmarkClass]
public partial class MyBenchmarks
{
    [Benchmark(Baseline = true)]
    public void Baseline() { /* ... */ }

    [Benchmark]
    public void Candidate() { /* ... */ }
}
```

> The class **must** be `partial`. The source generator emits an `IBenchmarkClass` implementation at compile time - no reflection, fully AOT-safe.

> Invalid attribute usage now produces generator diagnostics for common mistakes such as non-`partial` classes, duplicate baselines, invalid lifecycle signatures, and incompatible `[Params]` values. The generator also rejects `async void` benchmarks (PBGEN011) and generic, nested, record, or non-instantiable benchmark classes (PBGEN012-015), and warns about empty `[Params]` (PBGEN016) and benchmark attributes on base types (PBGEN017).

---

## Imperative API Reference

### Basic Benchmark

```csharp
using PicoBench;
using PicoBench.Formatters;

var result = Benchmark.Run("SpinWait", () => Thread.SpinWait(100));
Console.WriteLine(new ConsoleFormatter().Format(result));
```

### Benchmark with State (Avoid Closures)

```csharp
var data = new byte[1024];
var result = Benchmark.Run("ArrayCopy", data, static d =>
{
    var copy = new byte[d.Length];
    Buffer.BlockCopy(d, 0, copy, 0, d.Length);
});
```

### Scoped Benchmarks (DI-Friendly)

```csharp
var result = Benchmark.RunScoped("DbQuery",
    () => new MyDbContext(),
    static ctx => ctx.Users.FirstOrDefault()
);
// A new scope is created per sample; the scope is disposed after each sample.
```

An async variant is also available:

```csharp
var result = await Benchmark.RunScopedAsync("DbQueryAsync",
    () => new MyDbContext(),
    static async ctx => await ctx.Users.FirstOrDefaultAsync()
);
// Async variant: a new scope per sample, disposed after each sample.
```

### Comparing Two Implementations

```csharp
var comparison = Benchmark.Compare(
    "String vs StringBuilder",
    "String Concat",  () => { var s = ""; for (int i = 0; i < 100; i++) s += "a"; },
    "StringBuilder",  () => { var sb = new StringBuilder(); for (int i = 0; i < 100; i++) sb.Append('a'); _ = sb.ToString(); }
);

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%) [{(comparison.Interval?.ToString() ?? "no interval")}] -> {comparison.Outcome}");
```

`Benchmark.Compare(name, baselineAction, candidateAction)` does **not** run each lane once. It interleaves the two lanes over 10 rounds (which lane goes first alternates every round, so neither side inherits a warmer process) and reports the **median of the per-round paired ratios**. What that means for the numbers:

- **`Speedup`** — baseline time ÷ candidate time; `> 1` means the candidate is faster. A round whose candidate measured nothing contributes no ratio at all; when no round yields a usable ratio the comparison reports `NaN` (and `ImprovementPercent` is `NaN` with it) instead of inventing a verdict. A `ComparisonResult` built from two independent `BenchmarkResult`s still reports the ratio of the two averages. A 0/0 round is exactly that case: `NaN`, not `1.0` (a comparison built from two all-zero independent results keeps the legacy `1.0`).
- **`Interval`** — distribution-free 95% interval of that median (2nd/9th order statistic of the 10 paired ratios). `Interval.Contains(1.0)` means parity is still plausible. A comparison built from two independent results has **no interval** (`Interval` is `null`) — there are no interleaved rounds to pair, so nothing can be said about the spread.
- **`Outcome`** — the verdict. `Faster` / `Slower` require the ratio to leave a **5% dead band** *and* (when an interval exists) the interval to exclude 1. Anything inside is `Inconclusive`; a ratio that could not be computed (non-finite or non-positive) is `Unknown`, and so is a comparison with fewer than six usable paired rounds (below that the interval cannot cover the median with 95% confidence).
- **`IsFaster`** — `true` only for `Outcome == Faster`, i.e. **narrower than before**: a 3% difference now returns `false` (`Inconclusive`), and a comparison without rounds must clear the same 5% dead band. Use `Outcome` for the three-way answer and `IsFasterKnown` to tell "no measurable difference" apart from "could not measure at all".

Cost: a direct comparison runs each lane 10 times, i.e. **20 `Run`s** — lower `SampleCount` / `WarmupIterations` for A/B runs if that is too slow.

### Advanced: Separate Warmup, Setup & Teardown

```csharp
var result = Benchmark.Run(
    name:     "Custom",
    action:   () => DoWork(),
    warmup:   () => DoWork(),      // null to skip warmup
    config:   BenchmarkConfig.Precise,
    setup:    () => PrepareState(), // called before each sample (not timed)
    teardown: () => CleanUp()       // called after each sample (not timed)
);
```

---

## Attribute-Based API Reference

Decorate a **partial** class with `[BenchmarkClass]` and its methods/properties with the attributes below. The source generator emits all wiring code at compile time.

### Attributes

| Attribute | Target | Description |
|-----------|--------|-------------|
| `[BenchmarkClass]` | Class | Marks the class for code generation. Optional `Description` property. |
| `[Benchmark]` | Method | Marks a parameterless method as a benchmark. Set `Baseline = true` for the reference method. Optional `Description`. |
| `[Params(values)]` | Property / Field | Iterates the given compile-time constant values. Multiple `[Params]` properties produce a Cartesian product. |
| `[GlobalSetup]` | Method | Called **once** per parameter combination, before benchmarks run. |
| `[GlobalCleanup]` | Method | Called **once** per parameter combination, after benchmarks run. |
| `[IterationSetup]` | Method | Called before **each sample** (not timed). |
| `[IterationCleanup]` | Method | Called after **each sample** (not timed). |

`[Benchmark]` and lifecycle methods must be instance, non-generic, and parameterless. They may return `void`, `Task`, `ValueTask`, `Task<T>`, or `ValueTask<T>`; async methods are awaited and returned values are discarded. `[Benchmark]` methods may not be `async void` (PBGEN011). `[Params]` targets must be writable instance properties or non-readonly instance fields. Benchmark classes must be non-generic, non-nested, non-abstract, and declare a public parameterless constructor.

### Full Example

```csharp
using PicoBench;

[BenchmarkClass(Description = "Comparing string concatenation strategies")]
public partial class StringBenchmarks
{
    [Params(10, 100, 1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() { /* prepare data for current N */ }

    [GlobalCleanup]
    public void Cleanup() { /* release resources */ }

    [IterationSetup]
    public void BeforeSample() { /* per-sample preparation */ }

    [Benchmark(Baseline = true)]
    public void StringConcat()
    {
        var s = string.Empty;
        for (var i = 0; i < N; i++) s += "a";
    }

    [Benchmark]
    public void StringBuilder()
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < N; i++) sb.Append('a');
        _ = sb.ToString();
    }
}
```

### Running

```csharp
// Create instance internally:
var suite = BenchmarkRunner.Run<StringBenchmarks>(BenchmarkConfig.Quick);

// Or with a pre-configured instance:
var instance = new StringBenchmarks();
var suite2 = BenchmarkRunner.Run(instance, BenchmarkConfig.Quick);
```

---

## Async Benchmarks

Both APIs support asynchronous work. Async benchmark methods are awaited per iteration; lifecycle methods may mix sync and async in the same class.

```csharp
// Imperative
var result = await Benchmark.RunAsync("Http call", async () =>
{
    using var response = await httpClient.GetAsync(url);
});

// Per-sample async setup/teardown
var result2 = await Benchmark.RunAsync(
    "With lifecycle",
    action: async () => await DoWorkAsync(),
    warmup: async () => await DoWorkAsync(),
    config: BenchmarkConfig.Quick,
    setup: async () => await ResetStateAsync(),
    teardown: async () => await DisposeStateAsync());

// Stateful async (no closure allocation; no setup/teardown overload)
var result3 = await Benchmark.RunAsync("Stateful", state, async s => await ProcessAsync(s));

// Async scopes (DI-friendly)
var result4 = await Benchmark.RunScopedAsync("Scoped",
    () => container.CreateScope(),
    async scope => await scope.Service.DoWorkAsync());
```

Attribute-based classes may declare async benchmarks and async lifecycle methods:

```csharp
[BenchmarkClass]
public partial class IoBenchmarks
{
    private string _path = "";

    [GlobalSetup]
    public async Task SetupAsync() => await File.WriteAllTextAsync(_path, "data");

    [IterationSetup]
    public async Task ResetAsync() => await Task.Yield();

    [Benchmark(Baseline = true)]
    public async Task ReadAsync() => await File.ReadAllTextAsync(_path);

    [Benchmark]
    public void ReadSync() => File.ReadAllText(_path);
}
```

Async semantics worth knowing:

- **Timing modes** — `AsyncTimingMode.WallClock` (default) measures full duration including await suspension; `AsyncTimingMode.CpuOnly` measures `Process.TotalProcessorTime` and excludes I/O wait. The CPU clock advances in OS timer ticks (typically 10-16 ms), so auto-calibration raises its minimum sample budget to the measured clock granularity in this mode. GC data is omitted in CpuOnly mode.
- **Unmeasurable samples** — in `CpuOnly` mode a workload shorter than one OS timer tick reads as exactly zero CPU time. Such a sample is counted in `Statistics.UnmeasuredSampleCount` (`Unmeasured` is `true`), its per-operation value becomes **one timing quantum — an upper bound — not `0.000`**, and formatters print the per-operation cells as `N/A`. Statistics from such a run are not measurements of the work: read them as "below the CPU clock's resolution". Iteration counts are **not** quietly inflated to hide the problem — only `AutoCalibrateIterations = true` (the `Quick`/`Precise` presets) calibrates; `Default` uses the configured `IterationsPerSample` unchanged.
- **GC attribution** — asynchronous GC counts are marked approximate (`GcInfo.IsApproximate`), because await suspensions may run unrelated work. Setup/teardown allocations are excluded in both modes.
- **CPU cycles** — the synchronous path reads a thread-scoped counter (`QueryThreadCycleTime` on Windows, a `perf_event` opened for the measuring thread on Linux). Asynchronous benchmarks read a process-wide counter, because a per-thread counter cannot be followed across thread hops: that number includes work done by other threads and is flagged `Statistics.CpuCyclesAreApproximate`, rendered with a `~` in Console, Markdown, HTML and CSV. macOS has no cycle counter — `mach_absolute_time` is a monotonic *time* proxy rather than architectural cycles, and is flagged the same way.
- **Cancellation** — `BenchmarkConfig.CancellationToken` is checked at sample boundaries by async benchmarks only; synchronous benchmarks ignore it.
- **Mixed classes** — a synchronous `[Benchmark]` method keeps the direct synchronous measurement path even when the class has other async members, so it does not pay async-wrapper overhead.
- **Warmup** — warmup iterations invoke only the warmup delegate; per-sample `setup`/`teardown` (and `[IterationSetup]`/`[IterationCleanup]`) do not run during warmup. Keep warmup actions self-sufficient.

---

## Measurement Fidelity

PicoBench runs in-process for fast startup, which means the CLR's own settings affect absolute numbers. For comparable results:

- Disable tiered JIT so steady-state code is measured: `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0` (or `COMPlus_*` equivalents).
- For single-threaded micro-benchmarks, workstation GC avoids background server-GC threads adding noise: `DOTNET_gcServer=0`.
- CPU cycle counts are only available where the OS allows unprivileged counters (Windows `QueryThreadCycleTime`; Linux `perf_event` when `perf_event_paranoid` is 2 or lower). Windows synchronous runs are thread-scoped; asynchronous runs and macOS use a process-wide counter or, on macOS, a monotonic clock proxy instead of true cycles — both are flagged `CpuCyclesAreApproximate`. `EnvironmentInfo` and every formatter report which source was used.
- `BenchmarkConfig.BoostPriorities` (default `true`) raises process and thread priority only for the duration of a run and restores the previous values afterwards.

### What the numbers can and cannot resolve

Every run measures the cost the harness itself adds per operation with the same iteration count and deducts it, then publishes the smallest value it can still resolve:

| Field | Meaning |
|---|---|
| `Statistics.OverheadNs` | per-operation cost of the harness (delegate dispatch, loop bookkeeping, clock and counter reads) that was measured and deducted. `0` for asynchronous runs, whose overhead is not deducted |
| `Statistics.ResolutionLimitNanoseconds` | smallest per-operation value this run can resolve: the largest of one clock tick spread over the iterations, the run-to-run spread of the deducted overhead and the per-operation size of that overhead (its upper bound) |
| `Statistics.ResolutionLimited` | `Avg` is at or below that limit, or at least one sample could not be measured at all. Formatters print the per-operation cells as an upper bound (`<limit` in all four formats; Console adds the marker `[below resolution]` and Markdown/HTML add `(below resolution)`) instead of a number |
| `Statistics.Unmeasured` / `UnmeasuredSampleCount` | how many samples the timing source could not advance for (a `CpuOnly` zero tick). Those cells print `N/A`, never `0.000` |

So the honest reading of a sub-nanosecond result is **"below `<limit` ns"**, not "0.02 ns": the value is not measured, it is bounded, and it is a real upper bound — the largest of the clock tick, the spread of the deducted overhead and the per-operation size of that overhead. Because the residual is clamped at zero, every sample satisfies `true ≤ residual + overhead`, so the bound can no longer understate the floor the same run deducted. Two further asymmetries are worth remembering: the harness overhead deduction is implemented for synchronous benchmarks only (asynchronous per-operation values still carry their per-sample wrapper cost), and GC bytes are not measured at all — only collection counts, per process, approximate on the asynchronous path.

---

## Configuration

### Presets

| Preset | Warmup | Samples | Base Iters/Sample | Auto-Calibrate | Use Case |
|--------|--------|---------|-------------------|----------------|----------|
| `Quick` | 100 | 10 | 1,000 | Yes | Fast iteration / CI |
| `Default` | 1,000 | 100 | 10,000 | No | General benchmarking |
| `Precise` | 5,000 | 200 | 50,000 | Yes | Final measurements |

### Custom Configuration

```csharp
var config = new BenchmarkConfig
{
    WarmupIterations    = 500,
    SampleCount         = 50,
    IterationsPerSample = 5000,
    RetainSamples       = true,  // Keep raw TimingSample data
    AutoCalibrateIterations = true,
    MinSampleTime       = TimeSpan.FromMilliseconds(0.5),
    MaxAutoIterationsPerSample = 1_000_000,
    ForceGcBeforeBenchmark = true,  // false skips the pre-benchmark full GC
    BoostPriorities     = true,   // raise process/thread priority for the run, then restore
    TimingMode          = AsyncTimingMode.WallClock, // or CpuOnly (async only)
    CancellationToken   = default,                   // async only; sync benchmarks ignore it
};

var result = Benchmark.Run("Test", action, config);
```

When auto-calibration is enabled, PicoBench increases `IterationsPerSample` until a minimum sample-time budget is reached or `MaxAutoIterationsPerSample` is hit. This is especially useful for ultra-fast operations that would otherwise be dominated by timer noise. Auto-calibration is **opt-in** (`AutoCalibrateIterations` defaults to `false`, and the `Default` preset leaves the configured iteration count alone): PicoBench never silently inflates iterations to make a result look resolvable — in `CpuOnly` mode the raised budget is the measured CPU clock granularity, because anything shorter reads as zero. Set `ForceGcBeforeBenchmark = false` to skip the forced full GC that precedes the collection phase — useful when running many benchmarks and parameter combinations.

---

## Output Formatters

Four built-in formatters implement `IFormatter`, and `SummaryFormatter` provides a separate summary helper:

```csharp
using PicoBench.Formatters;

var console  = new ConsoleFormatter();     // Box-drawing console tables
var markdown = new MarkdownFormatter();    // GitHub-friendly Markdown
var html     = new HtmlFormatter();        // Styled HTML report
var csv      = new CsvFormatter();         // CSV for data analysis

// Static helper for comparison summaries:
Console.WriteLine(SummaryFormatter.Format(suite.Comparisons));
```

Console, Markdown, HTML, and CSV outputs include precision-oriented metadata such as standard error, relative standard deviation, and CPU counter notes when available.

### Formatting Targets

```csharp
formatter.Format(result);               // Single BenchmarkResult
formatter.Format(results);              // IEnumerable<BenchmarkResult>
formatter.Format(comparison);           // Single ComparisonResult
formatter.Format(comparisons);          // IEnumerable<ComparisonResult>
formatter.Format(suite);                // Complete BenchmarkSuite
```

### Formatter Options

```csharp
var options = new FormatterOptions
{
    IncludeEnvironment   = true,
    IncludeTimestamp      = true,
    IncludeGcInfo         = true,
    IncludeCpuCycles      = true,
    IncludePercentiles    = true,
    TimeDecimalPlaces     = 1,
    SpeedupDecimalPlaces  = 2,
    BaselineLabel         = "Old",
    CandidateLabel        = "New",
    OutputDirectory       = "results", // Used by WriteToFile methods
};

var formatter = new ConsoleFormatter(options);
// Also available: FormatterOptions.Default, .Compact, .Minimal
```

### Saving Results

```csharp
var dir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(dir);

File.WriteAllText(Path.Combine(dir, "results.md"),   new MarkdownFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.html"), new HtmlFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.csv"),  new CsvFormatter().Format(suite));
```

### CSV Column Contract

`CsvFormatter` writes a header row, then data rows; a suite adds `#` comment lines (`# Suite:`, `# Environment:`, `# CpuCounter:`, `# CpuCounterNote:`, `# GcInfoNote:`, `# Timestamp:`, `# Duration:`) and two sections, `# Results` and `# Comparisons`, **each with its own header row**. A file therefore has more than one header line — `pandas.read_csv(path, comment='#')` skips the comments but you must split the two sections yourself.

Results columns, in this order (default options; `IncludePercentiles`, `IncludeGcInfo`, `IncludeCpuCycles` and `IncludeTimestamp` remove their columns when `false`):

| # | Column | Meaning |
|---|---|---|
| 1 | `Name` | benchmark name (quoted if it contains `,` `"` or a line break; a leading `=`/`+`/`-`/`@` is prefixed with `'`) |
| 2 | `Category` | category, empty when unset |
| 3 | `Avg_ns` | per-operation nanoseconds — three cell states, see below |
| 4 | `P50_ns` | median per-operation nanoseconds (same three states) |
| 5-12 | `P90_ns`, `P95_ns`, `P99_ns`, `Min_ns`, `Max_ns`, `StdDev_ns`, `StdErr_ns`, `RSD_pct` | percentiles and spread; `RSD_pct` is a plain percentage, but the other seven use the per-operation cell states |
| 13 | `CpuCycles` | cycles per operation; a `~` prefix marks a process-wide counter or a clock proxy (`CpuCyclesAreApproximate`), not thread cycles |
| 14-16 | `GC0`, `GC1`, `GC2` | collection counts, or `N/A` when GC was not collected |
| 17 | `GCApproximate` | `1` when the counters are approximate (async path), `0` when exact, `N/A` when not collected |
| 18 | `SampleCount` | number of samples (integer) |
| 19 | `IterationsPerSample` | iterations per sample (integer) |
| 20 | `Timestamp` | ISO-8601 round-trip (`o`, invariant culture) |

Cell states of a per-operation column:

| Cell | Meaning |
|---|---|
| `12.5` | a measured value in nanoseconds (invariant culture, round-trip format) |
| `<0.02` | **upper bound**: the run could not resolve the value above `Statistics.ResolutionLimitNanoseconds`. The number after `<` is that limit, and it is not a measured value |
| `N/A` | not measured (`Statistics.Unmeasured`, a zero clock delta) or not collected (GC columns) |

Two consequences for consumers: because of `<0.02` a per-operation column is **not** guaranteed to be numeric, so readers such as pandas infer `object` and you have to handle the bound rows explicitly; and `GCApproximate` was inserted at position 17, between `GC2` and `SampleCount`, so **positional readers must shift `SampleCount`/`IterationsPerSample` by one** (name-based readers are unaffected).

Comparisons columns: `TestCase`, `Provider`, `Avg_ns`, `Speedup`, `P50_ns`, `P90_ns`, `P99_ns`, `StdErr_ns`, `RSD_pct`, `CpuCycles`, `GC0`, `GC1`, `GC2`, `GCApproximate`. Each comparison writes two rows — candidate first, then baseline — and `Speedup` is filled only on the candidate row. The comparison cells currently go through the plain number formatter, so `N/A`/`<limit` from the results table do not appear there.

> A machine-readable `# Schema:` header line declaring these states is tracked as a separate Batch 0 follow-up. Until it lands, the table above is the contract, and a suite file does not yet identify its schema version.

---

## Result Model

| Type | Description |
|------|-------------|
| `BenchmarkResult` | Name, Category, Tags, Statistics, Samples, IterationsPerSample, SampleCount, Timestamp |
| `ComparisonResult` | Name, Category, Tags, Baseline, Candidate, Speedup, ImprovementPercent, **Outcome**, **IsFasterKnown**, **Interval**, IsFaster |
| `BenchmarkSuite` | Name, Description, Results, Comparisons, Environment, Duration, Timestamp |
| `ComparisonOutcome` | Enum: `Unknown` (no ratio could be computed), `Inconclusive` (inside the 5% dead band, or the interval still contains parity), `Faster`, `Slower` |
| `ComparisonInterval` | Low, High, Contains(value), ToString() — distribution-free 95% interval of the median paired ratio |
| `Statistics` | Avg, P50, P90, P95, P99, Min, Max, StdDev, StandardError, RelativeStdDevPercent, CpuCyclesPerOp, **CpuCyclesAreApproximate**, **OverheadNs**, **ResolutionLimitNanoseconds**, **ResolutionLimited**, **Unmeasured**, **UnmeasuredSampleCount**, GcInfo, HasHighVariance |
| `TimingSample` | ElapsedNanoseconds, ElapsedMilliseconds, ElapsedTicks, CpuCycles, **CpuCyclesAreApproximate**, GcInfo |
| `GcInfo` | Gen0, Gen1, Gen2, Total, IsZero, IsApproximate |
| `EnvironmentInfo` | Os, Architecture, RuntimeVersion, ProcessorCount, ExecutionMode, Configuration, CPU counter kind / availability / meaning, CustomTags |

Semantics of the members added by the measurement-fidelity work:

| Member | Contract |
|---|---|
| `ComparisonResult.Outcome` | Three-way verdict; `Faster`/`Slower` need the 5% dead band **and** an interval that excludes 1 |
| `ComparisonResult.IsFasterKnown` | `Outcome != Unknown` — "the two lanes were compared" (they may still be indistinguishable) |
| `ComparisonResult.IsFaster` | `Outcome == Faster` only. Narrower than the old `Speedup > 1.0`: a difference inside the dead band is no longer "faster" |
| `ComparisonResult.Interval` | `ComparisonInterval?`; `null` when the comparison was built from two independent results rather than interleaved rounds |
| `Statistics.ResolutionLimited` | The per-operation value is an upper bound (`<limit`), not a measurement |
| `Statistics.UnmeasuredSampleCount` / `Unmeasured` | Samples the timing source could not advance for; their per-operation cells are `N/A` |
| `Statistics.CpuCyclesAreApproximate` | `CpuCyclesPerOp` is process-wide (async) or a monotonic clock proxy (macOS), not thread cycles: formatters prefix it with `~` |
| `TimingSample.CpuCyclesAreApproximate` | Same flag at sample level; `Statistics` aggregates it with logical OR |

---

## Architecture

```
src/
+-- PicoBench/                        # Main library (netstandard2.0)
|   +-- Benchmark.cs                   # Imperative API (Run, Compare, RunScoped)
|   +-- BenchmarkRunner.cs             # Attribute-based entry point (Run<T>)
|   +-- BenchmarkConfig.cs             # Configuration with presets
|   +-- Attributes.cs                  # 7 benchmark attributes
|   +-- IBenchmarkClass.cs             # Interface emitted by the generator
|   +-- Runner.cs                      # Low-level timing flow and sample creation
|   +-- Runner.Gc.cs                   # GC baseline and delta tracking
|   +-- Runner.Cpu.cs                  # Platform-specific CPU counter implementation
|   +-- StatisticsCalculator.cs        # Percentile / stats computation
|   +-- Models.cs                      # Result types
|   +-- Formatters/
|       +-- IFormatter.cs              # IFormatter, FormatterOptions & FormatterBase
|       +-- ConsoleFormatter.cs        # Box-drawing console tables
|       +-- MarkdownFormatter.cs       # GitHub Markdown tables
|       +-- HtmlFormatter.cs           # Styled HTML reports
|       +-- CsvFormatter.cs            # CSV export
|       +-- SummaryFormatter.cs        # Win/loss summary
|
+-- PicoBench.Generators/            # Source generator (netstandard2.0)
    +-- BenchmarkGenerator.cs          # IIncrementalGenerator entry point
    +-- BenchmarkClassAnalyzer.cs      # Roslyn analysis and diagnostics
    +-- CSharpLiteralFormatter.cs      # C# literal formatting for emitted params
    +-- DiagnosticDescriptors.cs       # Generator diagnostic definitions
    +-- Emitter.cs                     # C# code emitter (AOT-safe)
    +-- Models.cs                      # Roslyn analysis models
```

---

## Platform-Specific Features

| Feature | Windows | Linux | macOS |
|---------|---------|-------|-------|
| Timing | Stopwatch | Stopwatch | Stopwatch |
| GC tracking (Gen0/1/2) | Yes | Yes | Yes |
| CPU cycle counting, sync | `QueryThreadCycleTime` (thread-scoped) | `perf_event_open` per thread (when `perf_event_paranoid` ≤ 2) | `mach_absolute_time`, a **time proxy** — flagged `~` |
| CPU cycle counting, async | `QueryProcessCycleTime` (process-wide) — flagged `~` | process-wide `perf_event` — flagged `~` | `mach_absolute_time` — flagged `~` |
| Process priority boost | Yes (scoped, restored) | Yes (scoped, restored) | Yes (scoped, restored) |

On macOS the exported CPU counter is a high-resolution monotonic *time* proxy rather than architectural cycle counts: the value advances with wall time, not with executed instructions, and is therefore flagged `CpuCyclesAreApproximate` and printed with `~`. The same flag is used for the asynchronous path, whose process-wide counter includes work done by other threads. `EnvironmentInfo` and formatter output expose both distinctions explicitly.

---

## Samples

| Sample | API Style | Description |
|--------|-----------|-------------|
| `StringVsStringBuilder` | Imperative | Compares `string +=`, `StringBuilder`, and `StringBuilder` with capacity |
| `AttributeBased` | Attribute | Same comparison using `[Benchmark]`, `[Params]`, and the source generator |
| `CollectionBenchmarks` | Attribute | List vs Dictionary vs HashSet lookup - showcases every attribute |

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
dotnet run --project samples/AttributeBased -c Release
dotnet run --project samples/CollectionBenchmarks -c Release
```

---

## Comparison with BenchmarkDotNet

| Feature | PicoBench | BenchmarkDotNet |
|---------|-----------|----------------|
| Dependencies | 1 BCL polyfill | Many |
| Package size | Tiny | Large |
| Target framework | netstandard2.0 | net6.0+ |
| AOT support | Source generator | Reflection-based |
| Attribute API | `[Benchmark]`, `[Params]` | `[Benchmark]`, `[Params]` |
| Setup time | Instant | Seconds |
| Output formats | 5 | 10+ |
| Statistical depth | Good | Extensive |
| Use case | Quick A/B tests, CI, AOT apps | Detailed analysis, publications |

---

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Building and Publishing

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

Releases follow the PicoHex version rule `<year>.<x>.<y>`: **x** increments when the public API changed, **y** when it did not. The decision is based on the committed API baseline `api/PicoBench.public.txt`, which always holds the surface of the last release. If the baseline is missing, seed it once with `pwsh ./scripts/release.ps1 -Bootstrap -FromTag <last-tag>`.

```bash
# inspect the API delta and the next version
pwsh ./scripts/release.ps1 -DryRun

# cut the release: refresh api/, commit chore(release): v<version>, tag, push
pwsh ./scripts/release.ps1 -Push
```

The pushed tag triggers the GitHub Actions release pipeline, which runs the tests, packs every package at the tag version, and publishes to [NuGet.org](https://www.nuget.org/packages/PicoBench) automatically. To bridge the indexing gap for sibling PicoHex repositories, pack the same version into the local feed declared in `NuGet.config`:

```bash
dotnet pack src/PicoBench/PicoBench.csproj -c Release -o artifacts/nupkg -p:Version=<version>
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make changes with tests
4. Submit a pull request
