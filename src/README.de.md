# Quellprojekte

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Русский](README.ru.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Português (Brasil)](README.pt-BR.md)

Dieses Verzeichnis enthält die beiden Bibliotheksprojekte, die Pico.Bench ausmachen.

## Pico.Bench

Die Haupt-Benchmarking-Bibliothek mit Ziel **netstandard2.0** ohne externe Abhängigkeiten.

### Wichtige Dateien

| Datei | Zweck |
|------|---------|
| `Benchmark.cs` | Imperative API - `Run()`, `Run<TState>()`, `RunScoped<TScope>()`, `Compare()` |
| `BenchmarkRunner.cs` | Attributbasierter Einstiegspunkt - `Run<T>()` |
| `Attributes.cs` | Sieben Attribute: `[BenchmarkClass]`, `[Benchmark]`, `[Params]`, `[GlobalSetup]`, `[GlobalCleanup]`, `[IterationSetup]`, `[IterationCleanup]` |
| `IBenchmarkClass.cs` | Vom Quellgenerator auf dekorierte Klassen implementierte Schnittstelle |
| `BenchmarkConfig.cs` | Konfiguration mit Quick / Default / Precise Voreinstellungen |
| `Runner.cs` | Low-Level-Timing-Engine mit plattformspezifischer CPU-Zykluszählung |
| `StatisticsCalculator.cs` | Prozentil- und Statistikberechnung |
| `Models.cs` | Ergebnistypen: `BenchmarkResult`, `ComparisonResult`, `BenchmarkSuite`, `Statistics`, `TimingSample`, `GcInfo`, `EnvironmentInfo` |
| `Formatters/` | Fünf Formatierer: Console, Markdown, HTML, CSV, Summary |

### Paketierung

Das Projekt bindet `Pico.Bench.Generators` als Analyzer ein, sodass Verbraucher den Quellgenerator automatisch erhalten:

```bash
# Projektreferenz hinzufügen
dotnet add reference ../Pico.Bench.Generators/Pico.Bench.Generators.csproj

# Fügen Sie dann manuell die folgenden Attribute zum <ProjectReference>-Element in Ihrer .csproj-Datei hinzu:
# PrivateAssets="all"
# ReferenceOutputAssembly="false"
# OutputItemType="Analyzer"
```

## Pico.Bench.Generators

Ein **inkrementeller Quellgenerator** (`IIncrementalGenerator`), der mit `[BenchmarkClass]` dekorierte partielle Klassen zur Kompilierzeit in vollständige `IBenchmarkClass`-Implementierungen umwandelt.

- **Ziel**: netstandard2.0
- **Abhängigkeit**: Microsoft.CodeAnalysis.CSharp 4.3.1
- **Ausgabe**: AOT-kompatibles C# mit `global::`-qualifizierten Aufrufen und ohne Reflection

### Wichtige Dateien

| Datei | Zweck |
|------|---------|
| `BenchmarkGenerator.cs` | Generator-Einstiegspunkt mit `ForAttributeWithMetadataName` |
| `Emitter.cs` | C#-Code-Emitter – generiert `RunBenchmarks()` mit Parameteriteration, Setup/Teardown-Hooks und Vergleichslogik |
| `Models.cs` | Roslyn-Analysemodelle: `BenchmarkClassModel`, `BenchmarkMethodModel`, `ParamsPropertyModel` (alle `IEquatable<T>` für Caching) |

### Generierter Code

Für eine Klasse wie:

```csharp
[BenchmarkClass]
public partial class MyBench
{
    [Params(10, 100)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() { }

    [Benchmark(Baseline = true)]
    public void Baseline() { /* ... */ }

    [Benchmark]
    public void Fast() { /* ... */ }
}
```

Der Generator gibt eine `partial class MyBench : IBenchmarkClass` mit einer `RunBenchmarks()`-Methode aus, die:

1. Jeden `[Params]`-Wert iteriert (kartesisches Produkt für mehrere Eigenschaften)
2. Die Eigenschaft setzt, `[GlobalSetup]` aufruft
3. Jede `[Benchmark]`-Methode über `Benchmark.Run()` mit `[IterationSetup]`/`[IterationCleanup]` als Setup/Teardown ausführt
4. Kandidaten gegen den Baseline vergleicht
5. `[GlobalCleanup]` aufruft
6. Ein `BenchmarkSuite` mit allen Ergebnissen und Vergleichen zurückgibt