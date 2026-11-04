# PicoBench

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

Une bibliothèque de benchmarking légère et sans dépendances pour .NET avec **deux API complémentaires** : une API impérative et une API basée sur des attributs, générée à partir du code source, entièrement **compatible AOT**.

## Caractéristiques

- **Zéro Dépendance** - Implémentation pure .NET, aucun package externe requis
- **Deux API** - Impérative (`Benchmark.Run`) pour les tests ad hoc ; basée sur les attributs (`[Benchmark]` + générateur de source) pour les suites structurées
- **Générateur de Source Compatible AOT** - Le générateur incrémental émet des appels de méthode directs sans réflexion à l'exécution
- **Multiplateforme** - Support complet pour Windows, Linux et macOS
- **Chronométrage Sub-Microseconde** - Chronométrages par opération basés sur `Stopwatch`, avec le coût par opération du harness mesuré et déduit. Une charge de travail en dessous de la limite de résolution du run est rapportée comme une borne supérieure (`<limit`) et signalée `ResolutionLimited` — jamais comme un nombre précis à la nanoseconde
- **Limites Déclarées** - `OverheadNs`, `ResolutionLimitNanoseconds`, `ResolutionLimited` et `Unmeasured` disent ce qu'un run a pu résoudre et ce qu'il n'a pas pu résoudre, afin qu'un résultat au niveau du bruit ne puisse pas être lu comme une opération rapide
- **Suivi GC** - Surveille les comptes de collection Gen0/Gen1/Gen2 pendant les benchmarks (setup/teardown exclus)
- **Comptage de Cycles CPU** - Cycles mesurés à l'échelle du thread sur le chemin synchrone (`QueryThreadCycleTime` sous Windows ; `perf_event` sous Linux lorsque l'OS autorise l'accès non privilégié). Les benchmarks asynchrones lisent un compteur à l'échelle du processus, car un compteur par thread ne peut pas être suivi lors des changements de thread, et sont signalés `CpuCyclesAreApproximate` (`~` dans les quatre formats). macOS n'a pas de compteur de cycles : `mach_absolute_time` est un proxy *temporel* monotone, pas des cycles, et il est signalé de la même manière
- **Analyse Statistique** - Moyenne, Médiane, P90, P95, P99, Minimum, Maximum, Écart Type, erreur standard et écart-type relatif
- **Formats de Sortie Multiples** - Quatre formateurs intégrés (`IFormatter`) (Console, Markdown, HTML, CSV) plus une sortie de résumé programmatique
- **Benchmarks Paramétrés** - Attribut `[Params]` avec itération automatique du produit cartésien
- **Support de Comparaison** - Base et candidat mesurés en rounds entrelacés ; le verdict est la médiane des ratios appariés par round et exige à la fois une bande morte de 5% et un intervalle sans hypothèse de distribution qui exclut la parité, afin que le bruit d'un round à l'autre ne soit pas publié comme une amélioration
- **Configurable** - Préconfigurations Quick, Default et Precise, auto-calibrage ou configuration entièrement personnalisée
- **netstandard2.0** - Compatible avec .NET Framework **4.7.2+** (`RuntimeInformation` est utilisé inconditionnellement, donc les versions antérieures de .NET Framework ne sont pas prises en charge), .NET Core 2.0+, .NET 5+

## Installation

Référencez le package NuGet **PicoBench**. Le générateur de source (`PicoBench.Generators`) est inclus automatiquement comme un analyseur - aucune référence supplémentaire n'est nécessaire.

```bash
dotnet add package PicoBench
```

## Démarrage Rapide

### API Impérative

```csharp
using PicoBench;

var result = Benchmark.Run("My Benchmark", () =>
{
    Thread.SpinWait(100);
});

Console.WriteLine($"Average: {result.Statistics.Avg:F1} ns/op");
```

### API Basée sur les Attributs (Générée par Source)

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

> La classe **doit** être `partial`. Le générateur de source émet une implémentation `IBenchmarkClass` au moment de la compilation - sans réflexion, entièrement sûr pour AOT.

> Une utilisation invalide des attributs produit désormais des diagnostics du générateur pour les erreurs courantes comme les classes non `partial`, les baselines dupliquées, les signatures de cycle de vie invalides et les valeurs `[Params]` incompatibles. Le générateur rejette aussi les benchmarks `async void` (PBGEN011) et les classes de benchmark génériques, imbriquées, record ou non instanciables (PBGEN012-015), et avertit pour les `[Params]` vides (PBGEN016) et les attributs de benchmark sur les types de base (PBGEN017).

---

## Référence de l'API Impérative

### Benchmark Basique

```csharp
using PicoBench;
using PicoBench.Formatters;

var result = Benchmark.Run("SpinWait", () => Thread.SpinWait(100));
Console.WriteLine(new ConsoleFormatter().Format(result));
```

### Benchmark avec État (Éviter les Clôtures)

```csharp
var data = new byte[1024];
var result = Benchmark.Run("ArrayCopy", data, static d =>
{
    var copy = new byte[d.Length];
    Buffer.BlockCopy(d, 0, copy, 0, d.Length);
});
```

### Benchmarks avec Portée (DI-Friendly)

```csharp
var result = Benchmark.RunScoped("DbQuery",
    () => new MyDbContext(),
    static ctx => ctx.Users.FirstOrDefault()
);
// Une nouvelle portée est créée par échantillon ; la portée est supprimée après chaque échantillon.
```

Une variante asynchrone est également disponible :

```csharp
var result = await Benchmark.RunScopedAsync("DbQueryAsync",
    () => new MyDbContext(),
    static async ctx => await ctx.Users.FirstOrDefaultAsync()
);
// Variante asynchrone : une nouvelle portée par échantillon, supprimée après chaque échantillon.
```

### Comparer Deux Implémentations

```csharp
var comparison = Benchmark.Compare(
    "String vs StringBuilder",
    "String Concat",  () => { var s = ""; for (int i = 0; i < 100; i++) s += "a"; },
    "StringBuilder",  () => { var sb = new StringBuilder(); for (int i = 0; i < 100; i++) sb.Append('a'); _ = sb.ToString(); }
);

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%) [{(comparison.Interval?.ToString() ?? "no interval")}] -> {comparison.Outcome}");
```

`Benchmark.Compare(name, baselineAction, candidateAction)` n'exécute **pas** chaque voie une seule fois. Il entrelace les deux voies sur 10 rounds (la voie qui passe en premier alterne à chaque round, afin qu'aucun côté n'hérite d'un processus plus chaud) et rapporte la **médiane des ratios appariés par round**. Ce que cela signifie pour les nombres :

- **`Speedup`** — temps de la base ÷ temps du candidat ; `> 1` signifie que le candidat est plus rapide. Un round dont le candidat n'a rien mesuré ne contribue aucun ratio ; lorsqu'aucun round ne fournit de ratio utilisable, la comparaison rapporte `NaN` (et `ImprovementPercent` vaut `NaN` avec lui) au lieu d'inventer un verdict. Un `ComparisonResult` construit à partir de deux `BenchmarkResult` indépendants rapporte toujours le ratio des deux moyennes. Un round à 0/0 est exactement ce cas : `NaN`, pas `1.0` (une comparaison construite à partir de deux résultats indépendants à zéro conserve le `1.0` historique).
- **`Interval`** — intervalle à 95% sans hypothèse de distribution de cette médiane (2e/9e statistique d'ordre des 10 ratios appariés). `Interval.Contains(1.0)` signifie que la parité reste plausible. Une comparaison construite à partir de deux résultats indépendants n'a **aucun intervalle** (`Interval` vaut `null`) — il n'y a pas de rounds entrelacés à apparier, donc rien ne peut être dit de la dispersion.
- **`Outcome`** — le verdict. `Faster` / `Slower` exigent que le ratio quitte une **bande morte de 5%** *et* (quand un intervalle existe) que l'intervalle exclue 1. Tout ce qui est à l'intérieur est `Inconclusive` ; un ratio qui n'a pas pu être calculé (non fini ou non positif) est `Unknown`, et il en va de même pour une comparaison avec moins de six rounds appariés utilisables (en dessous, l'intervalle ne peut pas couvrir la médiane avec 95% de confiance).
- **`IsFaster`** — `true` uniquement pour `Outcome == Faster`, c'est-à-dire **plus étroit qu'auparavant** : une différence de 3% retourne désormais `false` (`Inconclusive`), et une comparaison sans rounds doit franchir la même bande morte de 5%. Utilisez `Outcome` pour la réponse à trois valeurs et `IsFasterKnown` pour distinguer « aucune différence mesurable » de « impossible de mesurer du tout ».

Coût : une comparaison directe exécute chaque voie 10 fois, soit **20 `Run`s** — réduisez `SampleCount` / `WarmupIterations` pour les runs A/B si c'est trop lent.

### Avancé : Échauffement, Configuration et Nettoyage Séparés

```csharp
var result = Benchmark.Run(
    name:     "Custom",
    action:   () => DoWork(),
    warmup:   () => DoWork(),      // null pour ignorer l'échauffement
    config:   BenchmarkConfig.Precise,
    setup:    () => PrepareState(), // appelé avant chaque échantillon (non chronométré)
    teardown: () => CleanUp()       // appelé après chaque échantillon (non chronométré)
);
```

---

## Référence de l'API Basée sur les Attributs

Décorer une classe **partial** avec `[BenchmarkClass]` et ses méthodes/propriétés avec les attributs ci-dessous. Le générateur de source émet tout le code de connexion au moment de la compilation.

### Attributs

| Attribut | Cible | Description |
|-----------|--------|-------------|
| `[BenchmarkClass]` | Classe | Marque la classe pour la génération de code. Propriété optionnelle `Description`. |
| `[Benchmark]` | Méthode | Marque une méthode sans paramètres comme benchmark. Définir `Baseline = true` pour la méthode de référence. `Description` optionnelle. |
| `[Params(values)]` | Propriété / Champ | Itère les valeurs constantes à la compilation données. Plusieurs propriétés `[Params]` produisent un produit cartésien. |
| `[GlobalSetup]` | Méthode | Appelé **une fois** par combinaison de paramètres, avant l'exécution des benchmarks. |
| `[GlobalCleanup]` | Méthode | Appelé **une fois** par combinaison de paramètres, après l'exécution des benchmarks. |
| `[IterationSetup]` | Méthode | Appelé avant **chaque échantillon** (non chronométré). |
| `[IterationCleanup]` | Méthode | Appelé après **chaque échantillon** (non chronométré). |

Les méthodes `[Benchmark]` et de cycle de vie doivent être des méthodes d'instance, non génériques et sans paramètres. Elles peuvent retourner `void`, `Task`, `ValueTask`, `Task<T>` ou `ValueTask<T>` ; les méthodes asynchrones sont attendues (await) et les valeurs de retour ignorées. Les méthodes `[Benchmark]` ne peuvent pas être `async void` (PBGEN011). Les cibles `[Params]` doivent être des propriétés d'instance modifiables ou des champs d'instance non `readonly`. Les classes de benchmark doivent être non génériques, non imbriquées, non abstraites et déclarer un constructeur public sans paramètre.

### Exemple Complet

```csharp
using PicoBench;

[BenchmarkClass(Description = "Comparaison des stratégies de concaténation de chaînes")]
public partial class StringBenchmarks
{
    [Params(10, 100, 1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() { /* préparer les données pour N courant */ }

    [GlobalCleanup]
    public void Cleanup() { /* libérer les ressources */ }

    [IterationSetup]
    public void BeforeSample() { /* préparation par échantillon */ }

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

### Exécution

```csharp
// Créer l'instance en interne :
var suite = BenchmarkRunner.Run<StringBenchmarks>(BenchmarkConfig.Quick);

// Ou avec une instance préconfigurée :
var instance = new StringBenchmarks();
var suite2 = BenchmarkRunner.Run(instance, BenchmarkConfig.Quick);
```

## Benchmarks Asynchrones

Les deux APIs prennent en charge le travail asynchrone. Les méthodes de benchmark asynchrones sont attendues (await) à chaque itération ; les méthodes de cycle de vie peuvent mélanger synchrone et asynchrone dans la même classe.

```csharp
var result = await Benchmark.RunAsync("Http call", async () =>
{
    using var response = await httpClient.GetAsync(url);
});

var scoped = await Benchmark.RunScopedAsync("Scoped",
    () => container.CreateScope(),
    async scope => await scope.Service.DoWorkAsync());
```

- **Modes de mesure** – `AsyncTimingMode.WallClock` (par défaut) mesure la durée totale, suspensions await comprises ; `AsyncTimingMode.CpuOnly` mesure `Process.TotalProcessorTime` et exclut l'attente d'E/S. L'horloge CPU n'avance que par tics du minuteur (généralement 10–16 ms) ; l'auto-calibration relève donc son budget minimal d'échantillon à la granularité mesurée de l'horloge. Le mode CpuOnly ne collecte pas de données GC.
- **Attribution du GC** – les compteurs GC asynchrones sont marqués approximatifs (`GcInfo.IsApproximate`). Les allocations de setup/teardown sont exclues dans les deux modes.
- **Échantillons non mesurables** – en mode `CpuOnly`, une charge de travail plus courte qu'un tick du minuteur de l'OS se lit exactement comme zéro temps CPU. Un tel échantillon est compté dans `Statistics.UnmeasuredSampleCount` (`Unmeasured` vaut `true`), sa valeur par opération devient **un quantum de mesure — une borne supérieure — et non `0.000`**, et les formateurs impriment les cellules par opération comme `N/A`. Les statistiques d'un tel run ne sont pas des mesures du travail : lisez-les comme « en dessous de la résolution de l'horloge CPU ». Les nombres d'itérations ne sont **pas** gonflés en silence pour masquer le problème — seul `AutoCalibrateIterations = true` (les préconfigurations `Quick`/`Precise`) calibre ; `Default` utilise `IterationsPerSample` tel que configuré.
- **Cycles CPU** – le chemin synchrone lit un compteur à l'échelle du thread (`QueryThreadCycleTime` sous Windows, un `perf_event` ouvert pour le thread de mesure sous Linux). Les benchmarks asynchrones lisent un compteur à l'échelle du processus, car un compteur par thread ne peut pas être suivi lors des changements de thread : ce nombre inclut le travail effectué par d'autres threads et est signalé par `Statistics.CpuCyclesAreApproximate`, rendu avec un `~` en Console, Markdown, HTML et CSV. macOS n'a pas de compteur de cycles — `mach_absolute_time` est un proxy *temporel* monotone plutôt que des cycles architecturaux, et il est signalé de la même manière.
- **Annulation** – `BenchmarkConfig.CancellationToken` n'est vérifié qu'aux frontières d'échantillon des benchmarks asynchrones ; les benchmarks synchrones l'ignorent.
- **Classes mixtes** – une méthode `[Benchmark]` synchrone conserve le chemin de mesure synchrone direct même si la classe contient d'autres membres asynchrones.
- **Warmup** – les itérations de warmup n'appellent que le délégué de warmup ; `setup`/`teardown` et `[IterationSetup]`/`[IterationCleanup]` ne s'exécutent pas pendant le warmup. Les actions de warmup doivent être autonomes.

---

## Fidélité de Mesure

PicoBench s'exécute dans le processus ; les réglages du CLR influencent donc les valeurs absolues :

- Désactivez le JIT par niveaux : `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0` (ou `COMPlus_*`).
- Pour les micro-benchmarks mono-thread, utilisez le GC station de travail : `DOTNET_gcServer=0`.
- Les comptes de cycles CPU ne sont disponibles que là où l'OS autorise les compteurs non privilégiés (`QueryThreadCycleTime` sous Windows ; Linux `perf_event` lorsque `perf_event_paranoid` vaut 2 ou moins). Les runs synchrones Windows sont à l'échelle du thread ; les runs asynchrones et macOS utilisent un compteur à l'échelle du processus ou, sous macOS, un proxy d'horloge monotone au lieu de vrais cycles — les deux sont signalés `CpuCyclesAreApproximate`. `EnvironmentInfo` et chaque formateur rapportent quelle source a été utilisée.
- `BenchmarkConfig.BoostPriorities` (par défaut `true`) élève la priorité du processus et du thread uniquement pendant l'exécution, puis restaure les valeurs précédentes.

### Ce que les nombres peuvent et ne peuvent pas résoudre

Chaque run mesure le coût que le harness ajoute lui-même par opération, avec le même nombre d'itérations, le déduit, puis publie la plus petite valeur qu'il peut encore résoudre :

| Champ | Signification |
|---|---|
| `Statistics.OverheadNs` | coût par opération du harness (dispatch des délégués, gestion de la boucle, lectures d'horloge et de compteur) qui a été mesuré et déduit. `0` pour les runs asynchrones, dont le surcoût n'est pas déduit |
| `Statistics.ResolutionLimitNanoseconds` | plus petite valeur par opération que ce run peut résoudre : un tick d'horloge réparti sur les itérations, ou la dispersion d'un run à l'autre du surcoût déduit, selon la plus grande |
| `Statistics.ResolutionLimited` | `Avg` est égal ou inférieur à cette limite, ou au moins un échantillon n'a pas pu être mesuré du tout. Les formateurs impriment les cellules par opération comme une borne supérieure (`<limit` dans les quatre formats ; la console ajoute le marqueur `[below resolution]`, Markdown et HTML ajoutent `(below resolution)`) au lieu d'un nombre |
| `Statistics.Unmeasured` / `UnmeasuredSampleCount` | combien d'échantillons la source de mesure n'a pas pu faire avancer (un tick nul en `CpuOnly`). Ces cellules impriment `N/A`, jamais `0.000` |

Ainsi, la lecture honnête d'un résultat inférieur à la nanoseconde est **« en dessous de `<limit` ns »**, et non « 0.02 ns » : la valeur n'est pas mesurée, elle est bornée, et la borne couvre la quantification de l'horloge et la dispersion du surcoût déduit (le résidu est ramené à zéro, c'est donc un plancher de ce que le harness peut résoudre plutôt qu'une preuve que le coût réel est plus faible). Deux autres asymétries méritent d'être rappelées : la déduction du surcoût du harness n'est implémentée que pour les benchmarks synchrones (les valeurs par opération asynchrones portent toujours le coût de leur wrapper par échantillon), et les octets GC ne sont pas mesurés du tout — seulement les comptes de collection, par processus, et de façon approximative sur le chemin asynchrone.

---

---

## Configuration

### Préconfigurations

| Préconfiguration | Échauffement | Échantillons | Itérations de base/Échantillon | Auto-calibrage | Cas d'Utilisation |
|--------|--------|---------|--------------------------------|----------------|----------|
| `Quick` | 100 | 10 | 1,000 | Oui | Itération rapide / CI |
| `Default` | 1,000 | 100 | 10,000 | Non | Benchmarking général |
| `Precise` | 5,000 | 200 | 50,000 | Oui | Mesures finales |

### Configuration Personnalisée

```csharp
var config = new BenchmarkConfig
{
    WarmupIterations    = 500,
    SampleCount         = 50,
    IterationsPerSample = 5000,
    RetainSamples       = true,  // Conserver les données brutes TimingSample
    AutoCalibrateIterations = true,
    MinSampleTime       = TimeSpan.FromMilliseconds(0.5),
    MaxAutoIterationsPerSample = 1_000_000,
    ForceGcBeforeBenchmark = true,  // false skips the pre-benchmark full GC
};

var result = Benchmark.Run("Test", action, config);
```

Lorsque l'auto-calibrage est activé, PicoBench augmente `IterationsPerSample` jusqu'à atteindre un budget minimal de temps par échantillon ou `MaxAutoIterationsPerSample`. C'est particulièrement utile pour les opérations très rapides autrement dominées par le bruit du minuteur. Définissez `ForceGcBeforeBenchmark = false` pour ignorer le GC complet forcé qui précède la phase de collecte — utile avec de nombreux benchmarks et combinaisons de paramètres.

---

## Formateurs de Sortie

Cinq formateurs intégrés implémentent `IFormatter` :

```csharp
using PicoBench.Formatters;

var console  = new ConsoleFormatter();     // Tableaux console avec dessin de boîtes
var markdown = new MarkdownFormatter();    // Markdown compatible GitHub
var html     = new HtmlFormatter();        // Rapports HTML stylisés
var csv      = new CsvFormatter();         // CSV pour l'analyse de données

// Assistant statique pour les résumés de comparaison :
Console.WriteLine(SummaryFormatter.Format(suite.Comparisons));
```

Les sorties Console, Markdown, HTML et CSV incluent des métadonnées orientées précision comme l'erreur standard, l'écart-type relatif et des notes sur le compteur CPU lorsqu'elles sont disponibles.

### Cibles de Formatage

```csharp
formatter.Format(result);               // BenchmarkResult unique
formatter.Format(results);              // IEnumerable<BenchmarkResult>
formatter.Format(comparison);           // ComparisonResult unique
formatter.Format(comparisons);          // IEnumerable<ComparisonResult>
formatter.Format(suite);                // BenchmarkSuite complet
```

### Options du Formateur

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
    CandidateLabel        = "New"
    OutputDirectory       = "results", // Used by WriteToFile methods
};

var formatter = new ConsoleFormatter(options);
// Également disponibles : FormatterOptions.Default, .Compact, .Minimal
```

### Sauvegarde des Résultats

```csharp
var dir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(dir);

File.WriteAllText(Path.Combine(dir, "results.md"),   new MarkdownFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.html"), new HtmlFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.csv"),  new CsvFormatter().Format(suite));
```

### Contrat des Colonnes CSV

`CsvFormatter` écrit une ligne d'en-tête, puis des lignes de données ; une suite ajoute des lignes de commentaire `#` (`# Suite:`, `# Environment:`, `# CpuCounter:`, `# CpuCounterNote:`, `# GcInfoNote:`, `# Timestamp:`, `# Duration:`) et deux sections, `# Results` et `# Comparisons`, **chacune avec sa propre ligne d'en-tête**. Un fichier a donc plus d'une ligne d'en-tête — `pandas.read_csv(path, comment='#')` ignore les commentaires, mais vous devez découper vous-même les deux sections.

Colonnes de résultats, dans cet ordre (options par défaut ; `IncludePercentiles`, `IncludeGcInfo`, `IncludeCpuCycles` et `IncludeTimestamp` suppriment leurs colonnes quand ils valent `false`) :

| # | Colonne | Signification |
|---|---|---|
| 1 | `Name` | nom du benchmark (mis entre guillemets s'il contient `,` `"` ou un saut de ligne ; un `=`/`+`/`-`/`@` initial est préfixé par `'`) |
| 2 | `Category` | catégorie, vide si non définie |
| 3 | `Avg_ns` | nanosecondes par opération — trois états de cellule, voir ci-dessous |
| 4 | `P50_ns` | nanosecondes médianes par opération (mêmes trois états) |
| 5-12 | `P90_ns`, `P95_ns`, `P99_ns`, `Min_ns`, `Max_ns`, `StdDev_ns`, `StdErr_ns`, `RSD_pct` | percentiles et dispersion ; `RSD_pct` est un pourcentage simple, mais les sept autres utilisent les états de cellule par opération |
| 13 | `CpuCycles` | cycles par opération ; un préfixe `~` marque un compteur à l'échelle du processus ou un proxy d'horloge (`CpuCyclesAreApproximate`), pas des cycles de thread |
| 14-16 | `GC0`, `GC1`, `GC2` | comptes de collection, ou `N/A` quand le GC n'a pas été collecté |
| 17 | `GCApproximate` | `1` quand les compteurs sont approximatifs (chemin asynchrone), `0` quand ils sont exacts, `N/A` quand ils ne sont pas collectés |
| 18 | `SampleCount` | nombre d'échantillons (entier) |
| 19 | `IterationsPerSample` | itérations par échantillon (entier) |
| 20 | `Timestamp` | ISO-8601 aller-retour (`o`, culture invariante) |

États de cellule d'une colonne par opération :

| Cellule | Signification |
|---|---|
| `12.5` | une valeur mesurée en nanosecondes (culture invariante, format aller-retour) |
| `<0.02` | **borne supérieure** : le run n'a pas pu résoudre la valeur au-delà de `Statistics.ResolutionLimitNanoseconds`. Le nombre après `<` est cette limite, et ce n'est pas une valeur mesurée |
| `N/A` | non mesuré (`Statistics.Unmeasured`, un delta d'horloge nul) ou non collecté (colonnes GC) |

Deux conséquences pour les consommateurs : à cause de `<0.02`, une colonne par opération n'est **pas** garantie numérique, donc des lecteurs comme pandas infèrent `object` et vous devez traiter explicitement les lignes de borne ; et `GCApproximate` a été inséré en position 17, entre `GC2` et `SampleCount`, donc **les lecteurs positionnels doivent décaler `SampleCount`/`IterationsPerSample` d'une position** (les lecteurs par nom ne sont pas affectés).

Colonnes de comparaisons : `TestCase`, `Provider`, `Avg_ns`, `Speedup`, `P50_ns`, `P90_ns`, `P99_ns`, `StdErr_ns`, `RSD_pct`, `CpuCycles`, `GC0`, `GC1`, `GC2`, `GCApproximate`. Chaque comparaison écrit deux lignes — le candidat d'abord, puis la base — et `Speedup` n'est renseigné que sur la ligne du candidat. Les cellules de comparaison passent actuellement par le formateur de nombres simple, donc `N/A`/`<limit` du tableau de résultats n'y apparaissent pas.

> Une ligne d'en-tête `# Schema:` lisible par machine, déclarant ces états, est suivie comme une tâche de suivi Batch 0 distincte. Tant qu'elle n'est pas livrée, le tableau ci-dessus est le contrat, et un fichier de suite n'identifie pas encore la version de son schéma.

---

## Modèle de Résultats

| Type | Description |
|------|-------------|
| `BenchmarkResult` | Nom, Catégorie, Étiquettes, Statistiques, Échantillons, ItérationsParÉchantillon, NombreDÉchantillons, Horodatage |
| `ComparisonResult` | Nom, Catégorie, Étiquettes, Base, Candidat, Accélération, PourcentageAmélioration, **Outcome**, **IsFasterKnown**, **Interval**, IsFaster |
| `BenchmarkSuite` | Nom, Description, Résultats, Comparaisons, Environnement, Durée, Horodatage |
| `ComparisonOutcome` | Énumération : `Unknown` (aucun ratio n'a pu être calculé), `Inconclusive` (à l'intérieur de la bande morte de 5%, ou l'intervalle contient encore la parité), `Faster`, `Slower` |
| `ComparisonInterval` | Low, High, Contains(value), ToString() — intervalle à 95% sans hypothèse de distribution de la médiane des ratios appariés |
| `Statistics` | Moyenne, P50, P90, P95, P99, Minimum, Maximum, ÉcartType, StandardError, RelativeStdDevPercent, CyclesCpuParOp, **CpuCyclesAreApproximate**, **OverheadNs**, **ResolutionLimitNanoseconds**, **ResolutionLimited**, **Unmeasured**, **UnmeasuredSampleCount**, GcInfo, HasHighVariance |
| `TimingSample` | NanosecondesÉcoulées, MillisecondesÉcoulées, TicksÉcoulés, CyclesCpu, **CpuCyclesAreApproximate**, GcInfo |
| `GcInfo` | Gen0, Gen1, Gen2, Total, EstZéro, IsApproximate |
| `EnvironmentInfo` | OS, Architecture, VersionRuntime, NombreProcesseurs, Mode d'exécution, Configuration, type / disponibilité / signification du compteur CPU, étiquettes personnalisées |

Sémantique des membres ajoutés par le travail sur la fidélité de mesure :

| Membre | Contrat |
|---|---|
| `ComparisonResult.Outcome` | Verdict à trois valeurs ; `Faster`/`Slower` exigent la bande morte de 5% **et** un intervalle qui exclut 1 |
| `ComparisonResult.IsFasterKnown` | `Outcome != Unknown` — « les deux voies ont été comparées » (elles peuvent encore rester indiscernables) |
| `ComparisonResult.IsFaster` | `Outcome == Faster` uniquement. Plus étroit que l'ancien `Speedup > 1.0` : une différence à l'intérieur de la bande morte n'est plus « plus rapide » |
| `ComparisonResult.Interval` | `ComparisonInterval?` ; `null` quand la comparaison a été construite à partir de deux résultats indépendants plutôt qu'à partir de rounds entrelacés |
| `Statistics.ResolutionLimited` | La valeur par opération est une borne supérieure (`<limit`), pas une mesure |
| `Statistics.UnmeasuredSampleCount` / `Unmeasured` | Échantillons pour lesquels la source de mesure n'a pas pu avancer ; leurs cellules par opération sont `N/A` |
| `Statistics.CpuCyclesAreApproximate` | `CpuCyclesPerOp` est à l'échelle du processus (asynchrone) ou un proxy d'horloge monotone (macOS), et non des cycles de thread : les formateurs le préfixent par `~` |
| `TimingSample.CpuCyclesAreApproximate` | Même indicateur au niveau de l'échantillon ; `Statistics` l'agrège par OU logique |

---

## Architecture

```
src/
+-- PicoBench/                        # Bibliothèque principale (netstandard2.0)
|   +-- Benchmark.cs                   # API impérative (Run, Compare, RunScoped)
|   +-- BenchmarkRunner.cs             # Point d'entrée basé sur les attributs (Run<T>)
|   +-- BenchmarkConfig.cs             # Configuration avec préconfigurations
|   +-- Attributes.cs                  # 7 attributs de benchmark
|   +-- IBenchmarkClass.cs             # Interface émise par le générateur
|   +-- Runner.cs                      # Flux de chronométrage bas niveau et création d'échantillons
|   +-- Runner.Gc.cs                   # Ligne de base GC et calcul des deltas
|   +-- Runner.Cpu.cs                  # Implémentation du compteur CPU spécifique à la plateforme
|   +-- StatisticsCalculator.cs        # Calcul des percentiles / statistiques
|   +-- Models.cs                      # Types de résultats
|   +-- Formatters/
|       +-- IFormatter.cs              # IFormatter, FormatterOptions & FormatterBase
|       +-- ConsoleFormatter.cs        # Tableaux console avec dessin de boîtes
|       +-- MarkdownFormatter.cs       # Tableaux Markdown GitHub
|       +-- HtmlFormatter.cs           # Rapports HTML stylisés
|       +-- CsvFormatter.cs            # Export CSV
|       +-- SummaryFormatter.cs        # Résumé victoires/défaites
|
+-- PicoBench.Generators/            # Générateur de source (netstandard2.0)
    +-- BenchmarkGenerator.cs          # Point d'entrée IIncrementalGenerator
    +-- BenchmarkClassAnalyzer.cs      # Analyse et diagnostics Roslyn
    +-- CSharpLiteralFormatter.cs      # Formatage des littéraux C# pour les paramètres émis
    +-- DiagnosticDescriptors.cs       # Définitions des diagnostics du générateur
    +-- Emitter.cs                     # Émetteur de code C# (sécurisé AOT)
    +-- Models.cs                      # Modèles d'analyse Roslyn
```

---

## Caractéristiques Spécifiques à la Plateforme

| Caractéristique | Windows | Linux | macOS |
|---------|---------|-------|-------|
| Chronométrage | Stopwatch | Stopwatch | Stopwatch |
| Suivi GC (Gen0/1/2) | Oui | Oui | Oui |
| Comptage de cycles CPU, synchrone | `QueryThreadCycleTime` (à l'échelle du thread) | `perf_event_open` par thread (quand `perf_event_paranoid` ≤ 2) | `mach_absolute_time`, un **proxy temporel** — signalé `~` |
| Comptage de cycles CPU, asynchrone | `QueryProcessCycleTime` (à l'échelle du processus) — signalé `~` | `perf_event` à l'échelle du processus — signalé `~` | `mach_absolute_time` — signalé `~` |
| Augmentation de priorité de processus | Oui (avec portée, restaurée) | Oui (avec portée, restaurée) | Oui (avec portée, restaurée) |

Sous macOS, le compteur CPU exporté est un proxy *temporel* monotone à haute résolution et non des comptes de cycles architecturaux : la valeur avance avec le temps réel, pas avec les instructions exécutées, et est donc signalée `CpuCyclesAreApproximate` et imprimée avec `~`. Le même indicateur est utilisé pour le chemin asynchrone, dont le compteur à l'échelle du processus inclut le travail effectué par d'autres threads. `EnvironmentInfo` et la sortie des formateurs exposent explicitement les deux distinctions.

---

## Exemples

| Exemple | Style d'API | Description |
|--------|-----------|-------------|
| `StringVsStringBuilder` | Impérative | Compare `string +=`, `StringBuilder` et `StringBuilder` avec capacité |
| `AttributeBased` | Basée sur les attributs | Même comparaison en utilisant `[Benchmark]`, `[Params]` et le générateur de source |
| `CollectionBenchmarks` | Basée sur les attributs | Recherche List vs Dictionary vs HashSet - montre chaque attribut |

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
dotnet run --project samples/AttributeBased -c Release
dotnet run --project samples/CollectionBenchmarks -c Release
```

---

## Comparaison avec BenchmarkDotNet

| Caractéristique | PicoBench | BenchmarkDotNet |
|---------|-----------|----------------|
| Dépendances | 0 | Nombreuses |
| Taille du package | Petite | Grande |
| Framework cible | netstandard2.0 | net6.0+ |
| Support AOT | Générateur de source | Basé sur la réflexion |
| API d'attributs | `[Benchmark]`, `[Params]` | `[Benchmark]`, `[Params]` |
| Temps de configuration | Instantané | Secondes |
| Formats de sortie | 5 | 10+ |
| Profondeur statistique | Bonne | Étendue |
| Cas d'utilisation | Tests A/B rapides, CI, applications AOT | Analyse détaillée, publications |

---

## Licence

Licence MIT - voir le fichier [LICENSE](LICENSE) pour plus de détails.

## Compilation et Publication

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

Les versions suivent la règle PicoHex `<année>.<x>.<y>` : **x** augmente quand l'API publique change, **y** sinon. La décision vient de la baseline d'API commitée dans `api/PicoBench.public.txt`, qui contient toujours la surface du dernier release.

```bash
# afficher le delta d'API et la prochaine version
pwsh ./scripts/release.ps1 -DryRun

# couper le release : rafraîchir api/, commit chore(release): v<version>, taguer, push
pwsh ./scripts/release.ps1 -Push
```

Le tag poussé déclenche la pipeline de release GitHub Actions : tests, pack de chaque paquet à la version du tag, publication sur [NuGet.org](https://www.nuget.org/packages/PicoBench). Pour combler le délai d'indexation des dépôts frères, la même version peut être empaquetée dans le feed local déclaré dans `NuGet.config` :

```bash
dotnet pack src/PicoBench/PicoBench.csproj -c Release -o artifacts/nupkg -p:Version=<version>
```

## Contribution

1. Forkez le dépôt
2. Créez une branche de fonctionnalité
3. Effectuez des modifications avec des tests
4. Soumettez une pull request
