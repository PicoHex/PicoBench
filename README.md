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
- **High-Precision Timing** - Uses `Stopwatch` and reports nanosecond-scale per-operation timings
- **GC Tracking** - Monitors Gen0/Gen1/Gen2 collection counts during benchmarks (setup/teardown excluded)
- **CPU Cycle Counting** - Hardware cycle counts on Windows/Linux when the OS permits unprivileged counter access, plus a monotonic proxy on macOS (`mach_absolute_time`); async benchmarks read process-wide counters so they stay valid across thread hops
- **Statistical Analysis** - Mean, Median, P90, P95, P99, Min, Max, StdDev, StdErr, and relative standard deviation
- **Multiple Output Formats** - Four built-in formatters (Console, Markdown, HTML, CSV) plus programmatic summary output
- **Parameterised Benchmarks** - `[Params]` attribute with automatic Cartesian product iteration
- **Comparison Support** - Baseline vs candidate with speedup calculations
- **Configurable** - Quick, Default, and Precise presets, auto-calibration, or fully custom configuration
- **netstandard2.0** - Compatible with .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5+

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

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%)");
```

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
- **GC attribution** — asynchronous GC counts are marked approximate (`GcInfo.IsApproximate`), because await suspensions may run unrelated work. Setup/teardown allocations are excluded in both modes.
- **CPU cycles** — async benchmarks use process-wide cycle counters (per-thread counters cannot be subtracted across thread hops).
- **Cancellation** — `BenchmarkConfig.CancellationToken` is checked at sample boundaries by async benchmarks only; synchronous benchmarks ignore it.
- **Mixed classes** — a synchronous `[Benchmark]` method keeps the direct synchronous measurement path even when the class has other async members, so it does not pay async-wrapper overhead.
- **Warmup** — warmup iterations invoke only the warmup delegate; per-sample `setup`/`teardown` (and `[IterationSetup]`/`[IterationCleanup]`) do not run during warmup. Keep warmup actions self-sufficient.

---

## Measurement Fidelity

PicoBench runs in-process for fast startup, which means the CLR's own settings affect absolute numbers. For comparable results:

- Disable tiered JIT so steady-state code is measured: `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0` (or `COMPlus_*` equivalents).
- For single-threaded micro-benchmarks, workstation GC avoids background server-GC threads adding noise: `DOTNET_gcServer=0`.
- CPU cycle counts are only available where the OS allows unprivileged counters (Windows `QueryThreadCycleTime`/`QueryProcessCycleTime`; Linux `perf_event` when `perf_event_paranoid` is 2 or lower; macOS exposes a monotonic proxy, not true cycles). `EnvironmentInfo` and every formatter report which source was used.
- `BenchmarkConfig.BoostPriorities` (default `true`) raises process and thread priority only for the duration of a run and restores the previous values afterwards.

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

When auto-calibration is enabled, PicoBench increases `IterationsPerSample` until a minimum sample-time budget is reached or `MaxAutoIterationsPerSample` is hit. This is especially useful for ultra-fast operations that would otherwise be dominated by timer noise. Set `ForceGcBeforeBenchmark = false` to skip the forced full GC that precedes the collection phase — useful when running many benchmarks and parameter combinations.

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

---

## Result Model

| Type | Description |
|------|-------------|
| `BenchmarkResult` | Name, Category, Tags, Statistics, Samples, IterationsPerSample, SampleCount, Timestamp |
| `ComparisonResult` | Name, Category, Tags, Baseline, Candidate, Speedup, IsFaster, ImprovementPercent |
| `BenchmarkSuite` | Name, Description, Results, Comparisons, Environment, Duration, Timestamp |
| `Statistics` | Avg, P50, P90, P95, P99, Min, Max, StdDev, StandardError, RelativeStdDevPercent, CpuCyclesPerOp, GcInfo |
| `TimingSample` | ElapsedNanoseconds, ElapsedMilliseconds, ElapsedTicks, CpuCycles, GcInfo |
| `GcInfo` | Gen0, Gen1, Gen2, Total, IsZero |
| `EnvironmentInfo` | Os, Architecture, RuntimeVersion, ProcessorCount, ExecutionMode, Configuration, CPU counter kind / availability / meaning, CustomTags |

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
| High-precision timing | Stopwatch | Stopwatch | Stopwatch |
| GC tracking (Gen0/1/2) | Yes | Yes | Yes |
| CPU cycle counting | `QueryThreadCycleTime` / `QueryProcessCycleTime` | `perf_event_open` (when `perf_event_paranoid` ≤ 2) | `mach_absolute_time` (proxy) |
| Process priority boost | Yes (scoped, restored) | Yes (scoped, restored) | Yes (scoped, restored) |

On macOS the exported CPU counter is a high-resolution monotonic proxy rather than architectural cycle counts. `EnvironmentInfo` and formatter output expose this distinction explicitly.

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
