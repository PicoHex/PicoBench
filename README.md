# Pico.Bench

A lightweight, zero-dependency, cross-platform benchmarking library for .NET. Provides precise measurements, statistical analysis, and multiple output formats with nanosecond precision.

## Features

- **Zero Dependencies**: Pure .NET Standard 2.0 library
- **Cross-Platform**: Windows, Linux, macOS (x64, ARM64)
- **High Precision**: Nanosecond timing with CPU cycle counting
- **GC Tracking**: Per-generation garbage collection monitoring
- **Statistical Analysis**: Mean, percentiles (P50, P90, P95, P99), standard deviation
- **Multiple Output Formats**: Console, Markdown, HTML, CSV
- **Comparison Engine**: Speedup ratios and percentage improvements
- **Flexible Configuration**: Built-in presets (Quick, Default, Precise)

## Quick Start

```csharp
using Pico.Bench;

// Simple benchmark
var result = Benchmark.Run("MyOperation", () => 
{
    // Code to benchmark
    Thread.Sleep(1);
});

Console.WriteLine($"Average: {result.Statistics.Avg:F2} ns");
```

## Installation

Add the NuGet package (coming soon) or reference the project directly:

```xml
<PackageReference Include="Pico.Bench" Version="1.0.0" />
```

## Usage

### Basic Benchmarking

```csharp
using Pico.Bench;

// Run with default configuration
var result = Benchmark.Run("String.Concat", () => string.Concat("a", "b", "c"));

// Run with custom configuration
var config = new BenchmarkConfig
{
    WarmupIterations = 1000,
    SampleCount = 100,
    IterationsPerSample = 10000
};
var result = Benchmark.Run("Custom", () => { /* code */ }, config);

// Run with state (avoids closure allocation)
var data = new int[1000];
var result = Benchmark.Run("ArraySum", data, static arr => 
{
    var sum = 0;
    foreach (var item in arr) sum += item;
});
```

### Comparing Implementations

```csharp
// Compare two approaches
var comparison = Benchmark.Compare(
    name: "StringBuilder vs String.Concat",
    baselineName: "String.Concat",
    baselineAction: () => string.Concat("a", "b", "c"),
    candidateName: "StringBuilder",
    candidateAction: () => new StringBuilder().Append("a").Append("b").Append("c").ToString()
);

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x");
Console.WriteLine($"Improvement: {comparison.ImprovementPercent:F1}%");
```

### Output Formats

```csharp
using Pico.Bench.Formatters;

var result = Benchmark.Run("Test", () => { /* code */ });

// Console output
Console.WriteLine(new ConsoleFormatter().Format(result));

// Save to files
var markdown = new MarkdownFormatter();
var html = new HtmlFormatter();
var csv = new CsvFormatter();

File.WriteAllText("results.md", markdown.Format(result));
File.WriteAllText("results.html", html.Format(result));
File.WriteAllText("results.csv", csv.Format(result));
```

## Configuration Presets

| Preset | Warmup Iterations | Sample Count | Iterations per Sample | Use Case |
|--------|-------------------|--------------|----------------------|----------|
| `BenchmarkConfig.Quick` | 100 | 10 | 1,000 | Fast iteration during development |
| `BenchmarkConfig.Default` | 1,000 | 100 | 10,000 | Balanced accuracy and speed |
| `BenchmarkConfig.Precise` | 5,000 | 200 | 50,000 | Final measurements for publication |

## Sample Output

### Console Formatter
```
╔══════════════════════════════════════════════════════════════════════════════════╗
║                          String vs StringBuilder Benchmark                       ║
╚══════════════════════════════════════════════════════════════════════════════════╝

Environment: .NET 8.0.0 | Linux 6.5.0-21-generic | X64 | JIT | Release

┌─────────────────────────────────────────────┬─────────┬──────┬──────┬──────┬──────┐
│ Test Case                                   │ Avg (ns)│ P50  │ P90  │ P95  │ P99  │
├─────────────────────────────────────────────┼─────────┼──────┼──────┼──────┼──────┤
│ String (10 appends)                         │   125.4 │ 124.0│ 128.1│ 129.5│ 131.2│
│ StringBuilder (10 appends)                  │    45.2 │  44.8│  46.1│  46.8│  47.9│
│ StringBuilder+Capacity (10 appends)         │    38.7 │  38.3│  39.4│  39.9│  40.8│
└─────────────────────────────────────────────┴─────────┴──────┴──────┴──────┴──────┘

Comparison: String vs StringBuilder (10 appends)
  Speedup: 2.77x (177% faster)
  GC: 0/0/0 vs 0/0/0
```

## Architecture

### Core Components

- **`Benchmark`**: High-level orchestrator with multiple overloads
- **`BenchmarkConfig`**: Configuration for benchmark execution
- **`Runner`**: Low-level timing with platform-specific CPU cycle counting
- **`StatisticsCalculator`**: Statistical computation engine
- **`Formatters`**: Output formatting (Console, Markdown, HTML, CSV)
- **`Models`**: Data structures (BenchmarkResult, Statistics, ComparisonResult)

### Data Flow
```
Action → Runner.Time() → TimingSample → 
Multiple Samples → StatisticsCalculator → Statistics → 
BenchmarkResult → Formatter → Console/File Output
```

## Platform-Specific Features

| Platform | CPU Cycle Counting | Notes |
|----------|-------------------|-------|
| Windows | `QueryThreadCycleTime` | Thread-specific cycle counting |
| Linux | `perf_event_open` syscall | Requires `perf_event_paranoid ≤ 1` |
| macOS | `mach_absolute_time` | Mach absolute time (not CPU cycles) |
| Other | 0 | Timing only (no CPU cycles) |

## Comparison with BenchmarkDotNet

| Feature | Pico.Bench | BenchmarkDotNet |
|---------|------------|-----------------|
| Dependencies | **Zero** | Multiple NuGet packages |
| Setup | Instant | Complex configuration |
| Output Formats | 4+ built-in | Limited built-in |
| CPU Cycles | Cross-platform | Windows-only |
| Binary Size | Tiny (~50KB) | Large (>10MB) |
| Startup Time | Milliseconds | Seconds |

**Pico.Bench is ideal for**: Quick iteration, embedded scenarios, CI/CD pipelines, and educational use where simplicity and zero dependencies are prioritized.

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make changes with tests
4. Submit a pull request

## Running Samples

```bash
cd samples/StringVsStringBuilder
dotnet run -c Release
```

Results will be saved to the `results/` directory in multiple formats.