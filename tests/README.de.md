# Tests

[English](README.md) | [中文](README.zh-CN.md) | [中文 (Traditional)](README.zh-TW.md) | [Español](README.es.md) | [Русский](README.ru.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Português (Brasil)](README.pt-BR.md)

Unit-Tests für **PicoBench** mit dem [TUnit](https://github.com/thomhurst/TUnit)-Testframework.

**Gesamt: 518 Tests**

## Ausführen

```bash
dotnet run --project tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug
```

## Testkategorien

### Formatters/ (249 Tests)

Tests für die vier auf `IFormatter` basierenden Ausgabeformatierer, `SummaryFormatter` und ihre unterstützende Infrastruktur.

| Datei | Tests | Beschreibung |
|------|-------|-------------|
| `ConsoleFormatterTests.cs` | 41 | Box-drawing-Tabellengenerierung, Ausrichtung, Kodierung |
| `MarkdownFormatterTests.cs` | 28 | GitHub-Markdown-Tabellenrendering |
| `HtmlFormatterTests.cs` | 36 | HTML-Berichtsgenerierung mit Stilen |
| `CsvFormatterTests.cs` | 31 | CSV-Export mit korrektem Escaping |
| `SummaryFormatterTests.cs` | 25 | Gewinn/Verlust-Zusammenfassungstext |
| `FormatterBaseTests.cs` | 37 | Template-Method-Basisklassenverhalten |
| `FormatterOptionsTests.cs` | 42 | Optionsstandards, Voreinstellungen, Pfadauflösung |
| `CrossPlatformTests.cs` | 9 | Zeilenende- und Kodierungskonsistenz |

### Formatters/Integration/ (6 Tests)

| Datei | Tests | Beschreibung |
|------|-------|-------------|
| `FormatterIntegrationTests.cs` | 6 | End-to-End-Formatierung vollständiger `BenchmarkSuite`-Objekte |

### Attributes/ (18 Tests)

| Datei | Tests | Beschreibung |
|------|-------|-------------|
| `AttributeTests.cs` | 18 | Alle sieben Attribute: Standardwerte, Eigenschafteneinstellung, `AttributeUsage`-Ziele, `[Params]`-Wertspeicherung |

### BenchmarkRunnerTests.cs (11 Tests)

| Datei | Tests | Beschreibung |
|------|-------|-------------|
| `BenchmarkRunnerTests.cs` | 11 | `BenchmarkRunner.Run<T>()` mit parameterloser / vorkonfigurierter Instanz, Nullprüfungen, Konfigurationspropagation |

### Generators/ (90 Tests)

| Datei | Tests | Beschreibung |
|------|-------|-------------|
| `EmitterTests.cs` | 38 | Quellgenerator-Codeemission: Klassenstruktur, Parameteriteration, Setup/Teardown-Hooks, Baseline-Vergleiche, `global::`-Qualifizierung |
| `ModelsTests.cs` | 30 | `BenchmarkClassModel`, `BenchmarkMethodModel`, `ParamsPropertyModel` Gleichheit, Hashcodes, Randfälle |
| `BenchmarkGeneratorDiagnosticsTests.cs` | 22 | End-to-End-Generator-Diagnosen für ungültige Signaturen, doppelte Baselines, ungültige `[Params]` und Enum-Parameter-Emission |

### Abdeckung des Kern-Runtimes

| Datei | Tests | Beschreibung |
|------|-------|-------------|
| `BenchmarkTests.cs` | 56 | Imperative API, Scoped-Ausführung, beibehaltene Samples, Vergleiche und Auto-Kalibrierungsverhalten |
| `StatisticsCalculatorTests.cs` | 12 | Statistikberechnung einschließlich Standardfehler, CPU-Zyklen und Randfällen |
| `ModelsTests.cs` | 38 | Validierung des Ergebnis-Modells, CPU-Zähler-Metadaten und Varianz-Helfer |

Formatter-Tests decken jetzt auch präzisionsorientierte Ausgaben wie Standardfehler, relative Standardabweichung und CPU-Zähler-Hinweise in Console, Markdown, HTML und CSV ab.

### TestData/

Factory-Klassen zum Erstellen konsistenter Test-Fixtures:

| Datei | Zweck |
|------|---------|
| `BenchmarkResultFactory.cs` | Erstellt `BenchmarkResult`-Instanzen mit sinnvollen Standardwerten |
| `BenchmarkSuiteFactory.cs` | Erstellt `BenchmarkSuite` mit Ergebnissen und Vergleichen |
| `ComparisonResultFactory.cs` | Erstellt `ComparisonResult`-Paare |
| `GcInfoFactory.cs` | Erstellt `GcInfo`-Records |
| `StatisticsFactory.cs` | Erstellt `Statistics` mit realistischen Verteilungen |

### Utilities/

| Datei | Zweck |
|------|---------|
| `FileSystemHelper.cs` | Temporäres Verzeichnismanagement für Dateiausgabe-Tests |
| `TestContextLogger.cs` | Logging-Helfer für TUnit-Testkontext |
