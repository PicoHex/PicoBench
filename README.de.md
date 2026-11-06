# PicoBench

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

Eine leichte Benchmarking-Bibliothek für .NET mit **zwei komplementären APIs**: einer imperativen API und einer attributbasierten, quellgenerierten API, die vollständig **AOT-kompatibel** ist. Die einzige NuGet-Referenz ist ein BCL-Polyfill (`System.Threading.Tasks.Extensions`) für `ValueTask<T>` auf netstandard2.0.

## Funktionen

- **Keine Drittanbieter-Abhängigkeiten** — nur ein BCL-Polyfill (`System.Threading.Tasks.Extensions`) für `ValueTask<T>` auf netstandard2.0. Keine externen Pakete.
- **Zwei APIs** - Imperativ (`Benchmark.Run`) für Ad-hoc-Tests; attributbasiert (`[Benchmark]` + Quellgenerator) für strukturierte Suiten
- **AOT-kompatibler Quellgenerator** - Der inkrementelle Generator erzeugt direkte Methodenaufrufe ohne Reflection zur Laufzeit
- **Plattformübergreifend** - Vollständige Unterstützung für Windows, Linux und macOS
- **Sub-Mikrosekunden-Timing** - `Stopwatch`-basierte Zeitmessungen pro Vorgang, bei denen der Eigenaufwand der Harness pro Vorgang gemessen und abgezogen wird. Eine Arbeitslast unterhalb der Auflösungsgrenze des Laufs wird als obere Schranke (`<limit`) gemeldet und `ResolutionLimited` markiert — niemals als nanosekundengenauer Wert
- **Offengelegte Grenzen** - `OverheadNs`, `ResolutionLimitNanoseconds`, `ResolutionLimited` und `Unmeasured` sagen, was ein Lauf auflösen konnte und was nicht, sodass ein Ergebnis auf Rauschniveau nicht als schnelle Operation gelesen werden kann
- **GC-Tracking** - Überwacht Gen0/Gen1/Gen2-Sammlungszählungen während Benchmarks (ohne Setup/Teardown)
- **CPU-Zykluszählung** - Thread-lokale Zykluszahlen auf dem synchronen Pfad (Windows `QueryThreadCycleTime`; Linux `perf_event`, wenn das Betriebssystem unprivilegierten Zugriff erlaubt). Asynchrone Benchmarks lesen einen prozessweiten Zähler, weil ein Thread-Zähler über Thread-Wechsel hinweg nicht verfolgt werden kann, und sind mit `CpuCyclesAreApproximate` markiert (`~` in allen vier Formaten). macOS hat keinen Zykluszähler: `mach_absolute_time` ist ein monotoner *Zeit*-Proxy, keine Zyklen, und wird genauso markiert
- **Statistische Analyse** - `Avg`, `P50`, `P90`, `P95`, `P99`, `Min`, `Max`, `StdDev`, `StandardError` und relative Standardabweichung (`RSD`). Perzentile werden durch lineare Interpolation zwischen den beiden benachbarten Stichproben gebildet (die Methode, die Statistikpakete *Typ 7* nennen — Excel `PERCENTILE.INC`, NumPy-Standard), und die Varianz ist die Stichprobenvarianz (`N-1`); unter etwa 20 Stichproben liegt P99 nahe am Maximum, die Tail-Spalten zeigen also keinen stabilen Tail.
- **Mehrere Ausgabeformate** - Vier integrierte `IFormatter`-Formatter (Console, Markdown, HTML, CSV) plus programmatische Zusammenfassung
- **Parametrisierte Benchmarks** - `[Params]`-Attribut mit automatischer kartesischer Produktiteration
- **Vergleichsunterstützung** - Baseline und Kandidat in verschränkten Runden gemessen; das Urteil ist der Median der paarweisen Verhältnisse pro Runde und benötigt sowohl ein 5%-Totband als auch ein verteilungsfreies Intervall, das Gleichstand ausschließt, damit Runden-zu-Runden-Rauschen nicht als Verbesserung veröffentlicht wird
- **Konfigurierbar** - Quick-, Default- und Precise-Voreinstellungen, Auto-Kalibrierung oder vollständig benutzerdefinierte Konfiguration
- **netstandard2.0** - Kompatibel mit .NET Framework **4.7.2+** (`RuntimeInformation` wird bedingungslos verwendet, daher werden frühere .NET Framework-Versionen nicht unterstützt), .NET Core 2.0+, .NET 5+

## Installation

Verweisen Sie auf das **PicoBench** NuGet-Paket. Der Quellgenerator (`PicoBench.Generators`) wird automatisch als Analyzer gebündelt - kein zusätzlicher Verweis erforderlich.

```bash
dotnet add package PicoBench
```

## Schnellstart

### Imperative API

```csharp
using PicoBench;

var result = Benchmark.Run("My Benchmark", () =>
{
    Thread.SpinWait(100);
});

Console.WriteLine($"Average: {result.Statistics.Avg:F1} ns/op");
```

### Attributbasierte API (Quellgeneriert)

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

> Die Klasse **muss** `partial` sein. Der Quellgenerator erzeugt zur Kompilierzeit eine `IBenchmarkClass`-Implementierung - ohne Reflection, vollständig AOT-sicher.

> Ungültige Attributverwendung erzeugt jetzt Generator-Diagnosen für häufige Fehler wie nicht-`partial` Klassen, doppelte Baselines, ungültige Lifecycle-Signaturen und inkompatible `[Params]`-Werte. Der Generator lehnt außerdem `async void`-Benchmarks (PBGEN011) sowie generische, verschachtelte, Record- und nicht instanziierbare Benchmark-Klassen (PBGEN012-015) ab und warnt bei leeren `[Params]` (PBGEN016) und bei Benchmark-Attributen auf Basistypen (PBGEN017).

---

## Referenz der imperativen API

### Grundlegender Benchmark

```csharp
using PicoBench;
using PicoBench.Formatters;

var result = Benchmark.Run("SpinWait", () => Thread.SpinWait(100));
Console.WriteLine(new ConsoleFormatter().Format(result));
```

### Benchmark mit Zustand (Vermeidung von Closures)

```csharp
var data = new byte[1024];
var result = Benchmark.Run("ArrayCopy", data, static d =>
{
    var copy = new byte[d.Length];
    Buffer.BlockCopy(d, 0, copy, 0, d.Length);
});
```

### Bereichsbenchmarks (DI-freundlich)

```csharp
var result = Benchmark.RunScoped("DbQuery",
    () => new MyDbContext(),
    static ctx => ctx.Users.FirstOrDefault()
);
// Ein neuer Bereich wird pro Sample erstellt; der Bereich wird nach jedem Sample verworfen.
```

Eine asynchrone Variante ist ebenfalls verfügbar:

```csharp
var result = await Benchmark.RunScopedAsync("DbQueryAsync",
    () => new MyDbContext(),
    static async ctx => await ctx.Users.FirstOrDefaultAsync()
);
// Asynchrone Variante: pro Sample ein neuer Bereich, der nach jedem Sample verworfen wird.
```

### Zwei Implementierungen vergleichen

```csharp
var comparison = Benchmark.Compare(
    "String vs StringBuilder",
    "String Concat",  () => { var s = ""; for (int i = 0; i < 100; i++) s += "a"; },
    "StringBuilder",  () => { var sb = new StringBuilder(); for (int i = 0; i < 100; i++) sb.Append('a'); _ = sb.ToString(); }
);

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%) [{(comparison.Interval?.ToString() ?? "no interval")}] -> {comparison.Outcome}");
```

`Benchmark.Compare(name, baselineAction, candidateAction)` führt jede Spur **nicht** einmal aus. Es verschränkt die beiden Spuren über 10 Runden (welche Spur zuerst läuft, wechselt jede Runde, damit keine Seite einen wärmeren Prozess erbt) und meldet den **Median der paarweisen Verhältnisse pro Runde**. Was das für die Zahlen bedeutet:

- **`Speedup`** — Baseline-Zeit ÷ Kandidaten-Zeit; `> 1` bedeutet, der Kandidat ist schneller. Eine Runde, deren Kandidat nichts gemessen hat, trägt gar kein Verhältnis bei; liefert keine Runde ein brauchbares Verhältnis, meldet der Vergleich `NaN` (und `ImprovementPercent` ist dann ebenfalls `NaN`), statt ein Urteil zu erfinden. Ein `ComparisonResult`, das aus zwei unabhängigen `BenchmarkResult`s gebildet wurde, meldet weiterhin das Verhältnis der beiden Mittelwerte. Eine Runde mit 0/0 ist genau dieser Fall: `NaN`, nicht `1.0` (ein Vergleich aus zwei unabhängigen Null-Ergebnissen behält das alte `1.0`).
- **`Interval`** — verteilungsfreies 95%-Intervall dieses Medians (2./9. Ordnungsstatistik der 10 paarweisen Verhältnisse). `Interval.Contains(1.0)` bedeutet, dass Gleichstand noch plausibel ist. Ein aus zwei unabhängigen Ergebnissen gebildeter Vergleich hat **kein Intervall** (`Interval` ist `null`) — es gibt keine verschränkten Runden zum Paaren, also lässt sich über die Streuung nichts sagen.
- **`Outcome`** — das Urteil. `Faster` / `Slower` verlangen, dass das Verhältnis ein **5%-Totband** verlässt *und* (sofern ein Intervall existiert) das Intervall die 1 ausschließt. Alles darin ist `Inconclusive`; ein Verhältnis, das nicht berechnet werden konnte (nicht endlich oder nicht positiv), ist `Unknown`, und ebenso ein Vergleich mit weniger als sechs brauchbaren paarweisen Runden (darunter kann das Intervall den Median nicht mit 95% Konfidenz abdecken).
- **`IsFaster`** — `true` nur für `Outcome == Faster`, also **enger als zuvor**: eine 3%-Differenz liefert jetzt `false` (`Inconclusive`), und ein Vergleich ohne Runden muss dasselbe 5%-Totband überschreiten. Verwenden Sie `Outcome` für die dreiwertige Antwort und `IsFasterKnown`, um "kein messbarer Unterschied" von "konnte überhaupt nicht messen" zu unterscheiden.

Kosten: ein direkter Vergleich führt jede Spur 10-mal aus, also **20 `Run`s** — senken Sie `SampleCount` / `WarmupIterations` für A/B-Läufe, wenn das zu langsam ist.

### Fortgeschritten: Separate Aufwärm-, Setup- & Teardown-Phasen

```csharp
var result = Benchmark.Run(
    name:     "Custom",
    action:   () => DoWork(),
    warmup:   () => DoWork(),      // null zum Überspringen des Aufwärmens
    config:   BenchmarkConfig.Precise,
    setup:    () => PrepareState(), // vor jedem Sample aufgerufen (nicht gemessen)
    teardown: () => CleanUp()       // nach jedem Sample aufgerufen (nicht gemessen)
);
```

---

## Referenz der attributbasierten API

Dekorieren Sie eine **partial**-Klasse mit `[BenchmarkClass]` und ihre Methoden/Eigenschaften mit den unten stehenden Attributen. Der Quellgenerator erzeugt zur Kompilierzeit den gesamten Verbindungscode.

### Attribute

| Attribut | Ziel | Beschreibung |
|-----------|--------|-------------|
| `[BenchmarkClass]` | Klasse | Markiert die Klasse zur Codegenerierung. Optionale `Description`-Eigenschaft. |
| `[Benchmark]` | Methode | Markiert eine parameterlose Methode als Benchmark. Setzen Sie `Baseline = true` für die Referenzmethode. Optionale `Description`. |
| `[Params(values)]` | Eigenschaft / Feld | Iteriert die gegebenen Kompilierzeit-Konstantenwerte. Mehrere `[Params]`-Eigenschaften erzeugen ein kartesisches Produkt. |
| `[GlobalSetup]` | Methode | Wird **einmal** pro Parameterkombination aufgerufen, bevor Benchmarks laufen. |
| `[GlobalCleanup]` | Methode | Wird **einmal** pro Parameterkombination aufgerufen, nachdem Benchmarks laufen. |
| `[IterationSetup]` | Methode | Wird vor **jedem Sample** aufgerufen (nicht gemessen). |
| `[IterationCleanup]` | Methode | Wird nach **jedem Sample** aufgerufen (nicht gemessen). |

`[Benchmark]`- und Lifecycle-Methoden müssen Instanzmethoden, nicht generisch und parameterlos sein. Sie dürfen `void`, `Task`, `ValueTask`, `Task<T>` oder `ValueTask<T>` zurückgeben; asynchrone Methoden werden awaited und Rückgabewerte verworfen. `[Benchmark]`-Methoden dürfen nicht `async void` sein (PBGEN011). `[Params]`-Ziele müssen schreibbare Instanz-Eigenschaften oder nicht-readonly Instanz-Felder sein. Benchmark-Klassen müssen nicht generisch, nicht verschachtelt, nicht abstrakt sein und einen öffentlichen parameterlosen Konstruktor besitzen.

### Vollständiges Beispiel

```csharp
using PicoBench;

[BenchmarkClass(Description = "Vergleich von String-Verkettungsstrategien")]
public partial class StringBenchmarks
{
    [Params(10, 100, 1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() { /* Daten für aktuelles N vorbereiten */ }

    [GlobalCleanup]
    public void Cleanup() { /* Ressourcen freigeben */ }

    [IterationSetup]
    public void BeforeSample() { /* Vorbereitung pro Sample */ }

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

### Ausführung

```csharp
// Instanz intern erstellen:
var suite = BenchmarkRunner.Run<StringBenchmarks>(BenchmarkConfig.Quick);

// Oder mit einer vorkonfigurierten Instanz:
var instance = new StringBenchmarks();
var suite2 = BenchmarkRunner.Run(instance, BenchmarkConfig.Quick);
```

## Asynchrone Benchmarks

Beide APIs unterstützen asynchrone Arbeit. Asynchrone Benchmark-Methoden werden pro Iteration awaited; Lifecycle-Methoden dürfen im selben Klasse synchron und asynchron gemischt werden.

```csharp
// Imperativ
var result = await Benchmark.RunAsync("Http call", async () =>
{
    using var response = await httpClient.GetAsync(url);
});

// Asynchrones Setup/Teardown pro Sample
var result2 = await Benchmark.RunAsync(
    "With lifecycle",
    action: async () => await DoWorkAsync(),
    warmup: async () => await DoWorkAsync(),
    config: BenchmarkConfig.Quick,
    setup: async () => await ResetStateAsync(),
    teardown: async () => await DisposeStateAsync());

// Zustandsbehaftet asynchron (keine Closure-Allokation; keine Setup/Teardown-Überladung)
var result3 = await Benchmark.RunAsync("Stateful", state, async s => await ProcessAsync(s));

// Asynchrone Scopes (DI-freundlich)
var result4 = await Benchmark.RunScopedAsync("Scoped",
    () => container.CreateScope(),
    async scope => await scope.Service.DoWorkAsync());
```

Attributbasierte Klassen dürfen asynchrone Benchmarks und asynchrone Lifecycle-Methoden deklarieren:

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

- **Timing-Modi** – `AsyncTimingMode.WallClock` (Standard) misst die Gesamtdauer inklusive await-Unterbrechungen; `AsyncTimingMode.CpuOnly` misst `Process.TotalProcessorTime` und schließt I/O-Wartezeit aus. Die CPU-Uhr springt nur in Timer-Ticks (typisch 10–16 ms); die Auto-Kalibrierung hebt ihr Mindest-Sample-Budget daher auf die gemessene Uhr-Granularität an. Im CpuOnly-Modus entfallen GC-Daten.
- **GC-Zuordnung** – asynchrone GC-Zähler sind als ungefähr markiert (`GcInfo.IsApproximate`). Setup/Teardown-Allokationen werden in beiden Modi ausgeschlossen.
- **Nicht messbare Samples** – im `CpuOnly`-Modus liest sich eine Arbeitslast, die kürzer als ein Timer-Tick des Betriebssystems ist, als exakt null CPU-Zeit. Ein solches Sample wird in `Statistics.UnmeasuredSampleCount` gezählt (`Unmeasured` ist `true`), sein Wert pro Vorgang wird **ein Timing-Quantum — eine obere Schranke — und nicht `0.000`**, und die Formatierer geben die Zellen pro Vorgang als `N/A` aus. Statistiken eines solchen Laufs sind keine Messungen der Arbeit: Lesen Sie sie als "unterhalb der Auflösung der CPU-Uhr". Die Iterationszahlen werden **nicht** stillschweigend aufgebläht, um das Problem zu verbergen — nur `AutoCalibrateIterations = true` (die Voreinstellungen `Quick`/`Precise`) kalibriert; `Default` verwendet `IterationsPerSample` unverändert wie konfiguriert.
- **CPU-Zyklen** – der synchrone Pfad liest einen thread-lokalen Zähler (`QueryThreadCycleTime` unter Windows, ein für den messenden Thread geöffnetes `perf_event` unter Linux). Asynchrone Benchmarks lesen einen prozessweiten Zähler, weil ein Thread-Zähler über Thread-Wechsel hinweg nicht verfolgt werden kann: diese Zahl enthält Arbeit anderer Threads und ist mit `Statistics.CpuCyclesAreApproximate` markiert, in Console, Markdown, HTML und CSV mit einer `~` dargestellt. macOS hat keinen Zykluszähler — `mach_absolute_time` ist ein monotoner *Zeit*-Proxy und keine architektonischen Zyklen, und wird genauso markiert.
- **Abbruch** – `BenchmarkConfig.CancellationToken` wird nur von asynchronen Benchmarks an Sample-Grenzen geprüft; synchrone Benchmarks ignorieren es.
- **Gemischte Klassen** – eine synchrone `[Benchmark]`-Methode behält den direkten synchronen Messpfad, auch wenn die Klasse andere asynchrone Elemente enthält.
- **Warmup** – Warmup-Iterationen rufen nur den Warmup-Delegaten auf; `setup`/`teardown` und `[IterationSetup]`/`[IterationCleanup]` laufen während des Warmups nicht. Warmup-Aktionen müssen eigenständig funktionieren.

---

## Messgenauigkeit

PicoBench läuft in-process; CLR-Einstellungen beeinflussen daher die absoluten Werte:

- Tiered JIT deaktivieren: `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0` (oder `COMPlus_*`).
- Für Single-Thread-Mikrobenchmarks Workstation-GC verwenden: `DOTNET_gcServer=0`.
- CPU-Zykluszahlen gibt es nur dort, wo das Betriebssystem unprivilegierte Zähler erlaubt (`QueryThreadCycleTime` unter Windows; Linux `perf_event`, wenn `perf_event_paranoid` 2 oder kleiner ist). Synchrone Windows-Läufe sind thread-lokal; asynchrone Läufe und macOS verwenden einen prozessweiten Zähler oder, unter macOS, einen monotonen Uhr-Proxy anstelle echter Zyklen — beide sind mit `CpuCyclesAreApproximate` markiert. `EnvironmentInfo` und jeder Formatierer melden, welche Quelle verwendet wurde.
- `BenchmarkConfig.BoostPriorities` (Standard `true`) hebt Prozess- und Thread-Priorität nur für die Dauer eines Laufs an und stellt die vorherigen Werte danach wieder her.

### Was die Zahlen auflösen können und was nicht

Jeder Lauf misst den Aufwand, den die Harness selbst pro Vorgang hinzufügt, mit derselben Iterationszahl, zieht ihn ab und veröffentlicht dann den kleinsten Wert, den er noch auflösen kann:

| Feld | Bedeutung |
|---|---|
| `Statistics.OverheadNs` | pro Vorgang anfallender Aufwand der Harness (Delegaten-Dispatch, Schleifenverwaltung, Uhr- und Zählerablesungen), der gemessen und abgezogen wurde. `0` bei asynchronen Läufen, deren Overhead nicht abgezogen wird |
| `Statistics.ResolutionLimitNanoseconds` | kleinster Wert pro Vorgang, den dieser Lauf auflösen kann: das Maximum aus einem über die Iterationen verteilten Uhr-Tick, der Streuung des abgezogenen Overheads von Lauf zu Lauf und der Größe dieses Overheads pro Vorgang (seiner oberen Schranke) |
| `Statistics.ResolutionLimited` | `Avg` liegt auf oder unter dieser Grenze, oder mindestens ein Sample konnte überhaupt nicht gemessen werden. Formatierer geben die Zellen pro Vorgang als obere Schranke aus (`<limit` in allen vier Formaten; die Console ergänzt die Markierung `[below resolution]`, Markdown und HTML ergänzen `(below resolution)`) statt als Zahl |
| `Statistics.Unmeasured` / `UnmeasuredSampleCount` | wie viele Samples die Zeitquelle nicht voranbringen konnte (ein Null-Tick bei `CpuOnly`). Diese Zellen geben `N/A` aus, niemals `0.000` |

Die ehrliche Lesart eines Sub-Nanosekunden-Ergebnisses ist also **"unterhalb von `<limit` ns"** und nicht "0.02 ns": der Wert ist nicht gemessen, er ist beschränkt, und die Schranke deckt Uhrquantisierung und die Streuung des abgezogenen Overheads ab (sie ist das Maximum aus Uhr-Tick, Streuung des abgezogenen Overheads und der Größe dieses Overheads pro Vorgang; und weil der Rest bei null gekappt wird, gilt für jedes Sample wahr ≤ Rest + Overhead — die Schranke kann also nicht mehr unter die Grenze fallen, die derselbe Lauf abgezogen hat). Zwei weitere Asymmetrien sind erwähnenswert: der Abzug des Harness-Overheads ist nur für synchrone Benchmarks implementiert (asynchrone Werte pro Vorgang tragen weiterhin die Wrapper-Kosten pro Sample), und GC-Bytes werden überhaupt nicht gemessen — nur Sammlungszählungen, pro Prozess, auf dem asynchronen Pfad ungefähr.

---

---

## Konfiguration

### Voreinstellungen

| Voreinstellung | Aufwärmiterationen | Samples | Basis Iterationen/Sample | Auto-Kalibrierung | Anwendungsfall |
|--------|--------|---------|--------------------------|-------------------|----------|
| `Quick` | 100 | 10 | 1,000 | Ja | Schnelle Iteration / CI |
| `Default` | 1,000 | 100 | 10,000 | Nein | Allgemeines Benchmarking |
| `Precise` | 5,000 | 200 | 50,000 | Ja | Finale Messungen |

### Benutzerdefinierte Konfiguration

```csharp
var config = new BenchmarkConfig
{
    WarmupIterations    = 500,
    SampleCount         = 50,
    IterationsPerSample = 5000,
    RetainSamples       = true,  // Rohdaten von TimingSample behalten
    AutoCalibrateIterations = true,
    MinSampleTime       = TimeSpan.FromMilliseconds(0.5),
    MaxAutoIterationsPerSample = 1_000_000,
    ForceGcBeforeBenchmark = true,  // false überspringt die erzwungene volle GC vor dem Benchmark
    BoostPriorities     = true,   // hebt Prozess-/Thread-Priorität nur für den Lauf an und stellt sie danach wieder her
    TimingMode          = AsyncTimingMode.WallClock, // oder CpuOnly (nur asynchron)
    CancellationToken   = default,                   // nur asynchron; synchrone Benchmarks ignorieren ihn
};

var result = Benchmark.Run("Test", action, config);
```

Wenn die Auto-Kalibrierung aktiviert ist, erhöht PicoBench `IterationsPerSample`, bis ein minimales Zeitbudget pro Sample erreicht ist oder `MaxAutoIterationsPerSample` erreicht wird. Das ist besonders hilfreich bei extrem schnellen Operationen, die sonst vom Timerrauschen dominiert werden. Setzen Sie `ForceGcBeforeBenchmark = false`, um die erzwungene vollständige GC vor der Sammelphase zu überspringen — nützlich bei vielen Benchmarks und Parameterkombinationen.Die Auto-Kalibrierung ist **opt-in** (`AutoCalibrateIterations` ist standardmäßig `false`, und die Voreinstellung `Default` lässt die konfigurierte Iterationszahl unverändert): PicoBench bläht Iterationen nie stillschweigend auf, damit ein Ergebnis auflösbar aussieht — im `CpuOnly`-Modus ist das angehobene Budget die gemessene Granularität der CPU-Uhr, weil alles Kürzere als null gelesen wird.

---

## Ausgabeformatierer

Fünf eingebaute Formatierer implementieren `IFormatter`:

```csharp
using PicoBench.Formatters;

var console  = new ConsoleFormatter();     // Kastenzeichnungskonsolentabellen
var markdown = new MarkdownFormatter();    // GitHub-freundliches Markdown
var html     = new HtmlFormatter();        // Gestylte HTML-Berichte
var csv      = new CsvFormatter();         // CSV für Datenanalyse

// Statische Hilfsfunktion für Vergleichszusammenfassungen:
Console.WriteLine(SummaryFormatter.Format(suite.Comparisons));
```

Alle vier Formatierer zeigen denselben Satz an Metriken pro Vorgang — `Avg`, `P50`, `P90`, `P95`, `P99`, `StandardError` und die relative Standardabweichung (Spalte `RSD (%)`) — sowie, sofern verfügbar, die Hinweise zum CPU-Zähler. Die Console-Suite-Tabelle führt zusätzlich die Spalten `StdErr` und `RSD` (sie fehlten bisher, sodass das Rauschsignal in der Standardausgabe nicht sichtbar war); Console, Markdown und HTML verwenden dieselbe Tabelleneinheit für dieselben Daten, und CSV bleibt immer bei Nanosekunden (siehe unten).

### Präzisionsprofil: raw gegenüber display

Eine Zahl hat zwei Aufgaben, und die Formatierer halten sie auseinander, statt jeweils einen eigenen Kompromiss zu suchen: ein Wert ist entweder **raw** — Nanosekunden, genau wie gemessen — oder **display** — derselbe Wert, auf die Einheit der Tabelle skaliert.

- **Raw** (`MetricPrecision.Raw`): schlichte Nanosekunden, und die Spaltenüberschrift (oder der CSV-Spaltenname `Avg_ns`, `StdErr_ns`, …) sagt `ns`. `CsvFormatter` schreibt immer diese Form, welche Einheit den Daten auch lieber wäre, denn eine Datei, deren Einheit sich mit der Größenordnung der Daten ändert, ist nicht maschinenlesbar.
- **Display** (`MetricPrecision.Display`): die Tabelle wählt **eine** Einheit für alle ihre Spalten pro Vorgang — `ns` unter 1 000 ns, `µs` unter 1 000 µs, `ms` unter 1 000 ms, darüber Sekunden — und nennt sie in der Überschrift (`Avg (µs)`). Eine Einheit pro Tabelle, nicht pro Zelle, damit eine Spalte vergleichbar bleibt: ein Fünf-Sekunden-Benchmark steht als `5.0` unter `Avg (s)`, sein 150-ns-Nachbar als `0.0` unter derselben Überschrift, statt still umskaliert zu werden.

```csharp
var raw = new FormatterOptions { PrecisionProfile = MetricPrecision.Raw };
Console.WriteLine(new ConsoleFormatter(raw).Format(suite)); // 5000000000.0 unter "Avg (ns)"
```

Der angezeigte Text ist in Console, Markdown und HTML identisch (ein Test vergleicht ihn Zeichen für Zeichen), und die Rohzahl steht immer in der CSV-Datei: eine skalierte Anzeige wird also nie die einzige Kopie einer Messung.

### Formatierungsziele

```csharp
formatter.Format(result);               // Einzelnes BenchmarkResult
formatter.Format(results);              // IEnumerable<BenchmarkResult>
formatter.Format(comparison);           // Einzelnes ComparisonResult
formatter.Format(comparisons);          // IEnumerable<ComparisonResult>
formatter.Format(suite);                // Vollständiges BenchmarkSuite
```

### Formatiereroptionen

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
    PrecisionProfile      = MetricPrecision.Display, // oder .Raw; CSV schreibt immer raw
    OutputDirectory       = "results", // Used by WriteToFile methods
};

var formatter = new ConsoleFormatter(options);
// Auch verfügbar: FormatterOptions.Default, .Compact, .Minimal
```

### Ergebnisse speichern

```csharp
var dir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(dir);

File.WriteAllText(Path.Combine(dir, "results.md"),   new MarkdownFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.html"), new HtmlFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.csv"),  new CsvFormatter().Format(suite));
```

### CSV-Spaltenvertrag

`CsvFormatter` schreibt eine Kopfzeile und dann Datenzeilen; eine Suite fügt `#`-Kommentarzeilen hinzu (`# Schema:` Vertragszeile; `# key,value`-Metadatenzeilen: `Suite`, `Description`, `Environment`, `CpuCounter`, `CpuCounterNote`, `GcInfoNote`, `Timestamp`, `Duration`, `NoiseNote`) sowie zwei Abschnitte, `# Results` und `# Comparisons`, **jeweils mit eigener Kopfzeile**. Eine Datei hat daher mehr als eine Kopfzeile — `pandas.read_csv(path, comment='#')` überspringt die Kommentare, aber Sie müssen die beiden Abschnitte selbst trennen. Die erste Zeile einer Ergebnisdatei ist diese Vertragszeile `# Schema: v2; …`, und jede mit `#` beginnende Zeile ist ein Kommentar, den ein Parser überspringen muss.

Eine Metadatenzeile läuft nie in die nächste physische Zeile weiter: ein Zeilenumbruch im Wert wird als die zwei Zeichen `\n` geschrieben (CRLF, CR und LF gleichermaßen), und der Wert wird bei Bedarf nach CSV-Regeln in Anführungszeichen gesetzt. Genau das macht `comment='#'` für eine mehrzeilige Beschreibung sicher.

Ergebnisspalten, in dieser Reihenfolge (Standardoptionen; `IncludePercentiles`, `IncludeGcInfo`, `IncludeCpuCycles` und `IncludeTimestamp` entfernen ihre Spalten bei `false`):

| # | Spalte | Bedeutung |
|---|---|---|
| 1 | `Name` | Benchmark-Name (in Anführungszeichen, wenn er `,` `"` oder einen Zeilenumbruch enthält; ein führendes `=`/`+`/`-`/`@` wird mit `'` versehen) |
| 2 | `Category` | Kategorie, leer wenn nicht gesetzt |
| 3 | `Avg_ns` | Nanosekunden pro Vorgang — drei Zellzustände, siehe unten |
| 4 | `P50_ns` | mediane Nanosekunden pro Vorgang (dieselben drei Zustände) |
| 5-12 | `P90_ns`, `P95_ns`, `P99_ns`, `Min_ns`, `Max_ns`, `StdDev_ns`, `StdErr_ns`, `RSD_pct` | Perzentile und Streuung; `RSD_pct` ist ein einfacher Prozentwert, die anderen sieben verwenden jedoch die Zellzustände pro Vorgang |
| 13 | `CpuCycles` | Zyklen pro Vorgang; ein `~`-Präfix kennzeichnet einen prozessweiten Zähler oder einen Uhr-Proxy (`CpuCyclesAreApproximate`), keine Thread-Zyklen |
| 14-16 | `GC0`, `GC1`, `GC2` | Sammlungszählungen oder `N/A`, wenn GC nicht erfasst wurde |
| 17 | `GCApproximate` | `1`, wenn die Zähler ungefähr sind (asynchroner Pfad), `0`, wenn exakt, `N/A`, wenn nicht erfasst |
| 18 | `SampleCount` | Anzahl der Samples (Ganzzahl) |
| 19 | `IterationsPerSample` | Iterationen pro Sample (Ganzzahl) |
| 20 | `Timestamp` | ISO-8601-Roundtrip (`o`, invariante Kultur) |

Zellzustände einer Spalte pro Vorgang:

| Zelle | Bedeutung |
|---|---|
| `12.5` | ein gemessener Wert in Nanosekunden (invariante Kultur, Roundtrip-Format) |
| `<0.02` | **obere Schranke**: der Lauf konnte den Wert oberhalb von `Statistics.ResolutionLimitNanoseconds` nicht auflösen. Die Zahl nach `<` ist diese Grenze, und es ist kein gemessener Wert |
| `N/A` | nicht gemessen (`Statistics.Unmeasured`, ein Null-Uhr-Delta) oder nicht erfasst (GC-Spalten) |

Zwei Konsequenzen für Konsumenten: wegen `<0.02` ist eine Spalte pro Vorgang **nicht** garantiert numerisch, sodass Leser wie pandas `object` ableiten und Sie die Schrankenzeilen explizit behandeln müssen; und `GCApproximate` wurde an Position 17 eingefügt, zwischen `GC2` und `SampleCount`, sodass **positionelle Leser `SampleCount`/`IterationsPerSample` um eins verschieben müssen** (namensbasierte Leser sind nicht betroffen).

Vergleichsspalten: `TestCase`, `Provider`, `Avg_ns`, `Speedup`, `P50_ns`, `P90_ns`, `P99_ns`, `StdErr_ns`, `RSD_pct`, `CpuCycles`, `GC0`, `GC1`, `GC2`, `GCApproximate`, `SampleCount`, `IterationsPerSample`. Jeder Vergleich schreibt zwei Zeilen — zuerst den Kandidaten, dann die Baseline — und `Speedup` wird nur in der Kandidatenzeile gefüllt. Die Vergleichszellen für die Einzeloperation laufen durch denselben Formatierer wie die Ergebnistabelle, daher erscheinen `<limit` und `N/A` auch dort, und `CpuCycles` trägt dieselbe `~`-Markierung (`CpuCyclesAreApproximate`).

Die `# Schema:`-Zeile ist umgesetzt und steht **in der ersten Zeile der Datei**: eine Ergebnisdatei (und eine Suite-Datei mit Ergebnissen) beginnt mit dem Vertrag, der die darunter stehenden Spalten beschreibt, und ein Vergleichsabschnitt schreibt seine eigene Vertragszeile direkt vor `# Comparisons`:

```
# Schema: v2; section=results; columns=Name|Category|Avg_ns|P50_ns|P90_ns|P95_ns|P99_ns|Min_ns|Max_ns|StdDev_ns|StdErr_ns|RSD_pct|CpuCycles|GC0|GC1|GC2|GCApproximate|SampleCount|IterationsPerSample|Timestamp; per-op cells (Avg_ns,P50_ns,P90_ns,P95_ns,P99_ns,Min_ns,Max_ns,StdDev_ns,StdErr_ns) = number (nanoseconds) | "<limit" (upper bound: the value is at the harness floor) | "N/A" (not measured); RSD_pct = percent; SampleCount = samples taken | IterationsPerSample = iterations per sample; CpuCycles = count | "~count" (not a thread-scoped cycle count); GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected)
# Schema: v2; section=comparisons; columns=TestCase|Provider|Avg_ns|Speedup|P50_ns|P90_ns|P99_ns|StdErr_ns|RSD_pct|CpuCycles|GC0|GC1|GC2|GCApproximate|SampleCount|IterationsPerSample; per-op cells (Avg_ns,P50_ns,P90_ns,P99_ns,StdErr_ns) = number (nanoseconds) | "<limit" (upper bound: the value is at the harness floor) | "N/A" (not measured); RSD_pct = percent; Speedup = ratio (baseline avg / candidate avg, or the median of the per-round paired ratios for a direct comparison) | "N/A" (no finite ratio: a candidate average of zero is unbounded, a non-finite candidate average is unmeasurable, or too few usable paired rounds); SampleCount = samples taken | IterationsPerSample = iterations per sample; CpuCycles = count | "~count" (not a thread-scoped cycle count); GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected)
```

`v2` ist die Batch-0-Form — die drei Zellzustände pro Vorgang oben plus `GCApproximate`. Die Liste `columns=` wird aus der tatsächlich geschriebenen Kopfzeile abgeleitet, kann also nicht von den obigen Tabellen abweichen und nennt nie eine Spalte, die eine Option entfernt hat; `section=` trennt die beiden Vertragszeilen. Ein Parser muss jede Zeile überspringen, die mit `#` beginnt, und die Spaltenreihenfolge aus der `columns=`-Liste lesen, statt sie fest zu verdrahten.

---

## Ergebnis-Modell

| Typ | Beschreibung |
|------|-------------|
| `BenchmarkResult` | Name, Kategorie, Tags, Statistik, Samples, IterationenProSample, SampleAnzahl, Zeitstempel |
| `ComparisonResult` | Name, Kategorie, Tags, Baseline, Kandidat, Beschleunigung, VerbesserungsProzent, **Outcome**, **IsFasterKnown**, **Interval**, IstSchneller |
| `BenchmarkSuite` | Name, Beschreibung, Ergebnisse, Vergleiche, Umgebung, Dauer, Zeitstempel |
| `ComparisonOutcome` | Enum: `Unknown` (kein Verhältnis konnte berechnet werden), `Inconclusive` (innerhalb des 5%-Totbands oder das Intervall enthält noch Gleichstand), `Faster`, `Slower` |
| `ComparisonInterval` | Low, High, Contains(value), ToString() — verteilungsfreies 95%-Intervall des medianen paarweisen Verhältnisses |
| `Statistics` | `Avg`, `P50`, `P90`, `P95`, `P99`, `Min`, `Max`, `StdDev`, `StandardError`, `RelativeStdDevPercent`, `CpuCyclesPerOp`, **CpuCyclesAreApproximate**, **OverheadNs**, **ResolutionLimitNanoseconds**, **ResolutionLimited**, **Unmeasured**, **UnmeasuredSampleCount**, GcInfo, HasHighVariance |
| `TimingSample` | VerstricheneNanosekunden, VerstricheneMillisekunden, VerstricheneTicks, CpuZyklen, **CpuCyclesAreApproximate**, GcInfo |
| `GcInfo` | Gen0, Gen1, Gen2, Gesamt, IstNull, IsApproximate |
| `EnvironmentInfo` | Betriebssystem, Architektur, Laufzeitversion, Prozessoranzahl, Ausführungsmodus, Konfiguration, CPU-Zählerart / -verfügbarkeit / Aussagekraft, benutzerdefinierte Tags |

Semantik der durch die Messgenauigkeits-Arbeit hinzugefügten Member:

| Member | Vertrag |
|---|---|
| `ComparisonResult.Outcome` | Dreiwertiges Urteil; `Faster`/`Slower` benötigen das 5%-Totband **und** ein Intervall, das die 1 ausschließt |
| `ComparisonResult.IsFasterKnown` | `Outcome != Unknown` — "die beiden Spuren wurden verglichen" (sie können weiterhin ununterscheidbar sein) |
| `ComparisonResult.IsFaster` | Nur `Outcome == Faster`. Enger als das alte `Speedup > 1.0`: eine Differenz innerhalb des Totbands ist nicht mehr "schneller" |
| `ComparisonResult.Interval` | `ComparisonInterval?`; `null`, wenn der Vergleich aus zwei unabhängigen Ergebnissen statt aus verschränkten Runden gebildet wurde |
| `Statistics.ResolutionLimited` | Der Wert pro Vorgang ist eine obere Schranke (`<limit`), keine Messung |
| `Statistics.UnmeasuredSampleCount` / `Unmeasured` | Samples, bei denen die Zeitquelle nicht vorankam; ihre Zellen pro Vorgang sind `N/A` |
| `Statistics.CpuCyclesAreApproximate` | `CpuCyclesPerOp` ist prozessweit (asynchron) oder ein monotoner Uhr-Proxy (macOS), keine Thread-Zyklen: Formatierer stellen `~` voran |
| `TimingSample.CpuCyclesAreApproximate` | Dasselbe Flag auf Sample-Ebene; `Statistics` aggregiert es mit logischem ODER |

---

## Architektur

```
src/
+-- PicoBench/                        # Hauptbibliothek (netstandard2.0)
|   +-- Benchmark.cs                   # Imperative API (Run, Compare, RunScoped)
|   +-- BenchmarkRunner.cs             # Attributbasierter Einstiegspunkt (Run<T>)
|   +-- BenchmarkConfig.cs             # Konfiguration mit Voreinstellungen
|   +-- Attributes.cs                  # 7 Benchmark-Attribute
|   +-- IBenchmarkClass.cs             # Vom Generator erzeugtes Interface
|   +-- Runner.cs                      # Low-Level-Timing-Ablauf und Sample-Erzeugung
|   +-- Runner.Gc.cs                   # GC-Basislinie und Delta-Berechnung
|   +-- Runner.Cpu.cs                  # Plattformspezifische CPU-Zähler-Implementierung
|   +-- StatisticsCalculator.cs        # Perzentil- / Statistikberechnung
|   +-- Models.cs                      # Ergebnis-Typen
|   +-- Formatters/
|       +-- IFormatter.cs              # IFormatter, FormatterOptions & FormatterBase
|       +-- ConsoleFormatter.cs        # Kastenzeichnungskonsolentabellen
|       +-- MarkdownFormatter.cs       # GitHub-Markdown-Tabellen
|       +-- HtmlFormatter.cs           # Gestylte HTML-Berichte
|       +-- CsvFormatter.cs            # CSV-Export
|       +-- SummaryFormatter.cs        # Gewinn/Verlust-Zusammenfassung
|
+-- PicoBench.Generators/            # Quellgenerator (netstandard2.0)
    +-- BenchmarkGenerator.cs          # IIncrementalGenerator-Einstiegspunkt
    +-- BenchmarkClassAnalyzer.cs      # Roslyn-Analyse und Diagnostik
    +-- CSharpLiteralFormatter.cs      # C#-Literalformatierung für emittierte Parameter
    +-- DiagnosticDescriptors.cs       # Definitionen der Generator-Diagnostik
    +-- Emitter.cs                     # C#-Code-Emitter (AOT-sicher)
    +-- Models.cs                      # Roslyn-Analysemodelle
```

---

## Plattformspezifische Funktionen

| Funktion | Windows | Linux | macOS |
|---------|---------|-------|-------|
| Timing | Stopwatch | Stopwatch | Stopwatch |
| GC-Tracking (Gen0/1/2) | Ja | Ja | Ja |
| CPU-Zykluszählung, synchron | `QueryThreadCycleTime` (thread-lokal) | `perf_event_open` pro Thread (wenn `perf_event_paranoid` ≤ 2) | `mach_absolute_time`, ein **Zeit-Proxy** — markiert mit `~` |
| CPU-Zykluszählung, asynchron | `QueryProcessCycleTime` (prozessweit) — markiert mit `~` | prozessweites `perf_event` — markiert mit `~` | `mach_absolute_time` — markiert mit `~` |
| Prozessprioritäts-Erhöhung | Ja (begrenzt, wiederhergestellt) | Ja (begrenzt, wiederhergestellt) | Ja (begrenzt, wiederhergestellt) |

Unter macOS ist der exportierte CPU-Zähler ein hochauflösender monotoner *Zeit*-Proxy und keine architektonischen Zykluszahlen: der Wert schreitet mit der Wanduhrzeit voran, nicht mit ausgeführten Instruktionen, und ist daher mit `CpuCyclesAreApproximate` markiert und mit `~` ausgegeben. Dasselbe Flag wird für den asynchronen Pfad verwendet, dessen prozessweiter Zähler Arbeit anderer Threads einschließt. `EnvironmentInfo` und die Formatierer-Ausgabe machen beide Unterscheidungen explizit.

---

## Beispiele

| Beispiel | API-Stil | Beschreibung |
|--------|-----------|-------------|
| `StringVsStringBuilder` | Imperativ | Vergleicht `string +=`, `StringBuilder` und `StringBuilder` mit Kapazität |
| `AttributeBased` | Attributbasiert | Gleicher Vergleich mit `[Benchmark]`, `[Params]` und dem Quellgenerator |
| `CollectionBenchmarks` | Attributbasiert | List vs Dictionary vs HashSet-Suche - zeigt jedes Attribut |
| `AsyncBenchmarks` | Attributbasiert (asynchron) | Asynchroner Einstiegspunkt und gemischter Lifecycle: `BenchmarkRunner.RunAsync<T>()`, asynchrone `[GlobalSetup]`/`[IterationSetup]`, `Task`-rückgebende `[Benchmark]`, und `SummaryFormatter` |

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
dotnet run --project samples/AttributeBased -c Release
dotnet run --project samples/CollectionBenchmarks -c Release
dotnet run --project samples/AsyncBenchmarks -c Release
```

---

## Vergleich mit BenchmarkDotNet

| Funktion | PicoBench | BenchmarkDotNet |
|---------|-----------|----------------|
| Abhängigkeiten | 1 BCL-Polyfill | Viele |
| Paketgröße | Klein | Groß |
| Zielframework | netstandard2.0 | net6.0+ |
| AOT-Unterstützung | Quellgenerator | Reflection-basiert |
| Attribut-API | `[Benchmark]`, `[Params]` | `[Benchmark]`, `[Params]` |
| Einrichtungszeit | Sofort | Sekunden |
| Ausgabeformate | 5 | 10+ |
| Statistische Tiefe | Gut | Umfangreich |
| Anwendungsfall | Schnelle A/B-Tests, CI, AOT-Apps | Detaillierte Analysen, Publikationen |

---

## Lizenz

MIT-Lizenz - siehe [LICENSE](LICENSE)-Datei für Details.

## Erstellen und Veröffentlichen

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

Releases folgen der PicoHex-Versionsregel `<Jahr>.<x>.<y>`: **x** erhöht sich, wenn sich die öffentliche API geändert hat, **y**, wenn nicht. Die Entscheidung stammt aus der committeten API-Baseline `api/PicoBench.public.txt`, die immer die Oberfläche des letzten Releases enthält.

```bash
# API-Delta und nächste Version anzeigen
pwsh ./scripts/release.ps1 -DryRun

# Release schneiden: api/ aktualisieren, chore(release): v<version> committen, taggen, pushen
pwsh ./scripts/release.ps1 -Push
```

Der gepushte Tag startet die GitHub-Actions-Release-Pipeline: Tests, Pack aller Pakete mit der Tag-Version, Veröffentlichung auf [NuGet.org](https://www.nuget.org/packages/PicoBench). Um die Indexierungslücke für Schwester-Repositories zu überbrücken, kann dieselbe Version in den in `NuGet.config` deklarierten lokalen Feed gepackt werden:

```bash
dotnet pack src/PicoBench/PicoBench.csproj -c Release -o artifacts/nupkg -p:Version=<version>
```

## Beitragen

1. Repository forken
2. Feature-Branch erstellen
3. Änderungen mit Tests durchführen
4. Pull-Request einreichen
