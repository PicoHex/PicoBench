# Tests

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

Unit tests for **PicoBench** using the [TUnit](https://github.com/thomhurst/TUnit) testing framework.

**Total: 559 tests**

## Running

```bash
dotnet run --project tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug
```

## Test Categories

### Formatters/ (256 tests)

Tests for the four `IFormatter`-based output formatters, `SummaryFormatter`, and their supporting infrastructure.

| File | Tests | Description |
|------|-------|-------------|
| `ConsoleFormatterTests.cs` | 41 | Box-drawing table generation, alignment, encoding |
| `MarkdownFormatterTests.cs` | 28 | GitHub Markdown table rendering |
| `HtmlFormatterTests.cs` | 36 | HTML report generation with styles |
| `CsvFormatterTests.cs` | 35 | CSV export with proper escaping, formula-injection guard, append semantics |
| `SummaryFormatterTests.cs` | 26 | Win/loss summary text |
| `FormatterBaseTests.cs` | 37 | Template Method base class behaviour |
| `FormatterOptionsTests.cs` | 44 | Options defaults, presets, validation, path resolution |
| `CrossPlatformTests.cs` | 9 | Line-ending and encoding consistency |

### Formatters/Integration/ (6 tests)

| File | Tests | Description |
|------|-------|-------------|
| `FormatterIntegrationTests.cs` | 6 | End-to-end formatting of full `BenchmarkSuite` objects |

### Attributes/ (18 tests)

| File | Tests | Description |
|------|-------|-------------|
| `AttributeTests.cs` | 18 | All seven attributes: default values, property setting, `AttributeUsage` targets, `[Params]` value storage |

### BenchmarkRunnerTests.cs (11 tests)

| File | Tests | Description |
|------|-------|-------------|
| `BenchmarkRunnerTests.cs` | 11 | `BenchmarkRunner.Run<T>()` with parameterless / pre-configured instance, null checks, config propagation |

### Generators/ (102 tests)

| File | Tests | Description |
|------|-------|-------------|
| `EmitterTests.cs` | 40 | Source generator code emission: class structure, parameter iteration, setup/teardown hooks, sync/async path selection, baseline comparisons, `global::` qualification |
| `ModelsTests.cs` (Generators) | 30 | `BenchmarkClassModel`, `BenchmarkMethodModel`, `ParamsPropertyModel` equality, hash codes, edge cases |
| `BenchmarkGeneratorDiagnosticsTests.cs` | 32 | End-to-end generator diagnostics for invalid signatures, unsupported class shapes, async-void methods, empty `[Params]`, inherited attributes, and enum parameter emission |

### Core runtime coverage

| File | Tests | Description |
|------|-------|-------------|
| `BenchmarkTests.cs` | 62 | Imperative API, scoped execution, retained samples, comparisons, async semantics, cancellation, calibration floor, and priority scoping |
| `RunnerTests.cs` | 38 | Low-level timing: validation, GC attribution, async cycle validity, Linux perf guard, clock granularity, priority scopes |
| `StatisticsCalculatorTests.cs` | 12 | Statistical computation including standard error, CPU cycles, and edge cases |
| `ModelsTests.cs` | 39 | Result model validation, CPU counter metadata, and variance helpers |
| `BenchmarkConfigTests.cs` | 15 | Preset values, immutability, validation, and default priority behaviour |

Formatter tests also cover precision-oriented output such as standard error, relative standard deviation, and CPU counter notes across Console, Markdown, HTML, and CSV.

### TestData/

Factory classes for building consistent test fixtures:

| File | Purpose |
|------|---------|
| `BenchmarkResultFactory.cs` | Creates `BenchmarkResult` instances with sensible defaults |
| `BenchmarkSuiteFactory.cs` | Creates `BenchmarkSuite` with results and comparisons |
| `ComparisonResultFactory.cs` | Creates `ComparisonResult` pairs |
| `GcInfoFactory.cs` | Creates `GcInfo` records |
| `StatisticsFactory.cs` | Creates `Statistics` with realistic distributions |

### Utilities/

| File | Purpose |
|------|---------|
| `FileSystemHelper.cs` | Temp directory management for file-output tests |
| `TestContextLogger.cs` | Logging helper for TUnit test context |
