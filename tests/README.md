# Tests

[English](README.md) | [中文](README.zh-CN.md) | [中文 (Traditional)](README.zh-TW.md) | [Español](README.es.md) | [Русский](README.ru.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Português (Brasil)](README.pt-BR.md)

Unit tests for **PicoBench** using the [TUnit](https://github.com/thomhurst/TUnit) testing framework.

**Total: 518 tests**

## Running

```bash
dotnet run --project tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug
```

## Test Categories

### Formatters/ (249 tests)

Tests for the four `IFormatter`-based output formatters, `SummaryFormatter`, and their supporting infrastructure.

| File | Tests | Description |
|------|-------|-------------|
| `ConsoleFormatterTests.cs` | 41 | Box-drawing table generation, alignment, encoding |
| `MarkdownFormatterTests.cs` | 28 | GitHub Markdown table rendering |
| `HtmlFormatterTests.cs` | 36 | HTML report generation with styles |
| `CsvFormatterTests.cs` | 31 | CSV export with proper escaping |
| `SummaryFormatterTests.cs` | 25 | Win/loss summary text |
| `FormatterBaseTests.cs` | 37 | Template Method base class behaviour |
| `FormatterOptionsTests.cs` | 42 | Options defaults, presets, path resolution |
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

### Generators/ (90 tests)

| File | Tests | Description |
|------|-------|-------------|
| `EmitterTests.cs` | 38 | Source generator code emission: class structure, parameter iteration, setup/teardown hooks, baseline comparisons, `global::` qualification |
| `ModelsTests.cs` | 30 | `BenchmarkClassModel`, `BenchmarkMethodModel`, `ParamsPropertyModel` equality, hash codes, edge cases |
| `BenchmarkGeneratorDiagnosticsTests.cs` | 22 | End-to-end generator diagnostics for invalid signatures, duplicate baselines, invalid `[Params]`, and enum parameter emission |

### Core runtime coverage

| File | Tests | Description |
|------|-------|-------------|
| `BenchmarkTests.cs` | 56 | Imperative API, scoped execution, retained samples, comparisons, and auto-calibration behaviour |
| `StatisticsCalculatorTests.cs` | 12 | Statistical computation including standard error, CPU cycles, and edge cases |
| `ModelsTests.cs` | 38 | Result model validation, CPU counter metadata, and variance helpers |

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
