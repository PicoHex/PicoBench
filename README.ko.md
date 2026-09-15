# PicoBench

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoBench/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoBench.svg)](https://www.nuget.org/packages/PicoBench)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**두 가지 상호 보완적 API**를 제공하는 .NET용 경량 벤치마킹 라이브러리입니다. 명령형 API와 완전한 **AOT 호환** 속성 기반 소스 생성 API를 갖추었습니다. 서드파티 의존성이 없으며, netstandard2.0에서 `ValueTask<T>`를 지원하기 위한 BCL 폴리필 하나만 NuGet으로 참조합니다.

## 기능

- **서드파티 의존성 없음** — netstandard2.0에 `ValueTask<T>`를 백포트하는 BCL 폴리필(`System.Threading.Tasks.Extensions`) 하나뿐입니다. 외부 패키지가 없습니다.
- **두 가지 API** - 즉석 테스트를 위한 명령형(`Benchmark.Run`), 구조화된 스위트를 위한 속성 기반(`[Benchmark]` + 소스 생성기)
- **AOT 호환 소스 생성기** - 증분 생성기가 런타임 리플렉션 없이 직접 메서드 호출을 생성합니다
- **크로스 플랫폼** - Windows, Linux, macOS 완벽 지원
- **고정밀 타이밍** - `Stopwatch`를 사용해 작업당 나노초 단위 시간을 보고합니다
- **GC 추적** - 벤치마크 중 Gen0/Gen1/Gen2 수집 횟수를 모니터링합니다(설정/해제 제외)
- **CPU 사이클 측정** - 운영체제가 비특권 카운터 접근을 허용할 때 Windows/Linux에서 하드웨어 사이클을 측정하고, macOS에서는 단조 프록시(`mach_absolute_time`)를 사용합니다. 비동기 벤치마크는 스레드 전환에도 유효하도록 프로세스 전역 카운터를 읽습니다
- **통계 분석** - 평균, 중앙값, P90, P95, P99, 최소, 최대, 표준편차, 표준오차, 상대 표준편차
- **다양한 출력 형식** - 4개의 내장 포맷터(Console, Markdown, HTML, CSV)와 프로그래밍 방식 요약 출력
- **매개변수화된 벤치마크** - 자동 카테시안 곱 반복을 지원하는 `[Params]` 특성
- **비교 지원** - 기준선 대 후보의 속도 향상 계산
- **구성 가능** - Quick, Default, Precise 프리셋, 자동 보정 또는 완전 사용자 정의 구성
- **netstandard2.0** - .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5+ 호환

## 설치

**PicoBench** NuGet 패키지를 참조하세요. 소스 생성기(`PicoBench.Generators`)는 분석기로 자동 번들되므로 추가 참조가 필요하지 않습니다.

```bash
dotnet add package PicoBench
```

## 빠른 시작

### 명령형 API

```csharp
using PicoBench;

var result = Benchmark.Run("My Benchmark", () =>
{
    Thread.SpinWait(100);
});

Console.WriteLine($"Average: {result.Statistics.Avg:F1} ns/op");
```

### 속성 기반 API (소스 생성)

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

> 클래스는 반드시 `partial`이어야 합니다. 소스 생성기가 컴파일 시점에 `IBenchmarkClass` 구현을 생성합니다. 리플렉션이 없으며 완전히 AOT 안전합니다.

> 잘못된 특성 사용은 partial 누락, 중복 기준선, 잘못된 수명 주기 시그니처, 호환되지 않는 `[Params]` 값 등 일반적인 실수에 대해 생성기 진단을 발생시킵니다.

---

## 명령형 API 참조

### 기본 벤치마크

```csharp
using PicoBench;
using PicoBench.Formatters;

var result = Benchmark.Run("SpinWait", () => Thread.SpinWait(100));
Console.WriteLine(new ConsoleFormatter().Format(result));
```

### 상태를 사용하는 벤치마크 (클로저 방지)

```csharp
var data = new byte[1024];
var result = Benchmark.Run("ArrayCopy", data, static d =>
{
    var copy = new byte[d.Length];
    Buffer.BlockCopy(d, 0, copy, 0, d.Length);
});
```

### 범위 벤치마크 (DI 친화적)

```csharp
var result = Benchmark.RunScoped("DbQuery",
    () => new MyDbContext(),
    static ctx => ctx.Users.FirstOrDefault()
);
// A new scope is created per sample; the scope is disposed after each sample.
```

비동기 변형도 사용할 수 있습니다:

```csharp
var result = await Benchmark.RunScopedAsync("DbQueryAsync",
    () => new MyDbContext(),
    static async ctx => await ctx.Users.FirstOrDefaultAsync()
);
// Async variant: a new scope per sample, disposed after each sample.
```

### 두 구현 비교

```csharp
var comparison = Benchmark.Compare(
    "String vs StringBuilder",
    "String Concat",  () => { var s = ""; for (int i = 0; i < 100; i++) s += "a"; },
    "StringBuilder",  () => { var sb = new StringBuilder(); for (int i = 0; i < 100; i++) sb.Append('a'); _ = sb.ToString(); }
);

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%)");
```

### 고급: 분리된 예열, 설정 및 해제

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

## 속성 기반 API 참조

**partial** 클래스에 `[BenchmarkClass]`를, 메서드/속성에 아래 특성을 붙이세요. 소스 생성기가 컴파일 시점에 모든 연결 코드를 생성합니다.

### 특성

| 특성 | 대상 | 설명 |
|-----------|--------|-------------|
| `[BenchmarkClass]` | 클래스 | 코드 생성을 위해 클래스를 표시합니다. 선택적 `Description` 속성. |
| `[Benchmark]` | 메서드 | 매개변수 없는 메서드를 벤치마크로 표시합니다. 기준 메서드에는 `Baseline = true`를 설정합니다. 선택적 `Description`. |
| `[Params(values)]` | 속성 / 필드 | 주어진 컴파일 타임 상수 값을 반복합니다. 여러 `[Params]` 속성은 카테시안 곱을 만듭니다. |
| `[GlobalSetup]` | 메서드 | 매개변수 조합마다 벤치마크 실행 전에 **한 번** 호출됩니다. |
| `[GlobalCleanup]` | 메서드 | 매개변수 조합마다 벤치마크 실행 후에 **한 번** 호출됩니다. |
| `[IterationSetup]` | 메서드 | **각 샘플** 전에 호출됩니다(시간에 포함되지 않음). |
| `[IterationCleanup]` | 메서드 | **각 샘플** 후에 호출됩니다(시간에 포함되지 않음). |

`[Benchmark]`와 수명 주기 메서드는 인스턴스, 비제네릭, 매개변수 없음이어야 합니다. 반환 형식은 `void`, `Task`, `ValueTask`, `Task<T>`, `ValueTask<T>` 중 하나일 수 있으며, 비동기 메서드는 await되고 반환 값은 버려집니다. `[Benchmark]` 메서드는 `async void`일 수 없습니다(PBGEN011). `[Params]` 대상은 쓰기 가능한 인스턴스 속성이거나 읽기 전용이 아닌 인스턴스 필드여야 합니다. 벤치마크 클래스는 비제네릭, 비중첩, 비추상이어야 하며 public 매개변수 없는 생성자를 선언해야 합니다.

### 전체 예제

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

### 실행

```csharp
// Create instance internally:
var suite = BenchmarkRunner.Run<StringBenchmarks>(BenchmarkConfig.Quick);

// Or with a pre-configured instance:
var instance = new StringBenchmarks();
var suite2 = BenchmarkRunner.Run(instance, BenchmarkConfig.Quick);
```

---

## 비동기 벤치마크

두 API 모두 비동기 작업을 지원합니다. 비동기 벤치마크 메서드는 반복마다 await되며, 수명 주기 메서드는 같은 클래스에서 동기와 비동기를 혼합할 수 있습니다.

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

속성 기반 클래스는 비동기 벤치마크와 비동기 수명 주기 메서드를 선언할 수 있습니다:

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

알아 두면 좋은 비동기 의미:

- **타이밍 모드** — `AsyncTimingMode.WallClock`(기본값)은 await 중단을 포함한 전체 시간을 측정합니다. `AsyncTimingMode.CpuOnly`는 `Process.TotalProcessorTime`을 측정하고 I/O 대기를 제외합니다. CPU 시계는 운영체제 타이머 틱(보통 10–16 ms) 단위로만 진행하므로, 이 모드의 자동 보정은 최소 샘플 예산을 측정된 시계 세분성까지 높입니다. CpuOnly 모드에서는 GC 데이터가 수집되지 않습니다.
- **GC 귀속** — 비동기 GC 카운트는 근사치로 표시됩니다(`GcInfo.IsApproximate`). setup/teardown 할당은 두 모드 모두에서 제외됩니다.
- **CPU 사이클** — 비동기 벤치마크는 프로세스 전역 사이클 카운터를 사용합니다(스레드별 카운터는 스레드 전환을 넘어 뺄 수 없음).
- **취소** — `BenchmarkConfig.CancellationToken`은 비동기 벤치마크의 샘플 경계에서만 확인됩니다. 동기 벤치마크는 이를 무시합니다.
- **혼합 클래스** — 클래스에 다른 비동기 멤버가 있어도 동기 `[Benchmark]` 메서드는 직접 동기 측정 경로를 유지하므로 비동기 래퍼 오버헤드를 부담하지 않습니다.
- **예열** — 예열 반복은 예열 델리게이트만 호출합니다. `setup`/`teardown`과 `[IterationSetup]`/`[IterationCleanup]`은 예열 중에 실행되지 않습니다. 예열 동작은 자체적으로 완결되어야 합니다.

---

## 측정 충실도

PicoBench는 빠른 시작을 위해 프로세스 내에서 실행되므로 CLR 설정이 절대값에 영향을 줍니다. 비교 가능한 결과를 얻으려면:

- 정상 상태 코드를 측정하도록 계층화 JIT를 비활성화하세요: `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0`(또는 `COMPlus_*`).
- 단일 스레드 마이크로 벤치마크에는 워크스테이션 GC를 사용해 백그라운드 서버 GC 스레드 노이즈를 피하세요: `DOTNET_gcServer=0`.
- CPU 사이클은 운영체제가 비특권 카운터를 허용하는 경우에만 사용할 수 있습니다(Windows `QueryThreadCycleTime`/`QueryProcessCycleTime`, Linux는 `perf_event_paranoid`가 2 이하일 때 `perf_event`, macOS는 실제 사이클이 아닌 단조 프록시). `EnvironmentInfo`와 모든 포맷터가 사용된 소스를 보고합니다.
- `BenchmarkConfig.BoostPriorities`(기본값 `true`)는 실행 중에만 프로세스와 스레드 우선순위를 높이고 종료 후 이전 값으로 복원합니다.

---

## 구성

### 프리셋

| 프리셋 | 예열 | 샘플 | 기본 반복/샘플 | 자동 보정 | 용도 |
|--------|--------|---------|-------------------|----------------|----------|
| `Quick` | 100 | 10 | 1,000 | 예 | 빠른 반복 / CI |
| `Default` | 1,000 | 100 | 10,000 | 아니요 | 일반 벤치마킹 |
| `Precise` | 5,000 | 200 | 50,000 | 예 | 최종 측정 |

### 사용자 정의 구성

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

자동 보정이 활성화되면 PicoBench는 최소 샘플 시간 예산에 도달하거나 `MaxAutoIterationsPerSample`에 도달할 때까지 `IterationsPerSample`을 늘립니다. 이는 타이머 노이즈에 지배될 수 있는 초고속 작업에 특히 유용합니다. `ForceGcBeforeBenchmark = false`로 설정하면 수집 단계 전의 강제 전체 GC를 건너뜁니다. 많은 벤치마크와 매개변수 조합을 실행할 때 유용합니다.

---

## 출력 포맷터

4개의 내장 포맷터가 `IFormatter`를 구현하며, `SummaryFormatter`는 별도의 요약 도우미를 제공합니다:

```csharp
using PicoBench.Formatters;

var console  = new ConsoleFormatter();     // Box-drawing console tables
var markdown = new MarkdownFormatter();    // GitHub-friendly Markdown
var html     = new HtmlFormatter();        // Styled HTML report
var csv      = new CsvFormatter();         // CSV for data analysis

// Static helper for comparison summaries:
Console.WriteLine(SummaryFormatter.Format(suite.Comparisons));
```

Console, Markdown, HTML, CSV 출력에는 표준오차, 상대 표준편차, 사용 가능한 경우 CPU 카운터 관련 참고 사항 등 정밀도 지향 메타데이터가 포함됩니다.

### 포맷팅 대상

```csharp
formatter.Format(result);               // Single BenchmarkResult
formatter.Format(results);              // IEnumerable<BenchmarkResult>
formatter.Format(comparison);           // Single ComparisonResult
formatter.Format(comparisons);          // IEnumerable<ComparisonResult>
formatter.Format(suite);                // Complete BenchmarkSuite
```

### 포맷터 옵션

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

### 결과 저장

```csharp
var dir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(dir);

File.WriteAllText(Path.Combine(dir, "results.md"),   new MarkdownFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.html"), new HtmlFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.csv"),  new CsvFormatter().Format(suite));
```

---

## 결과 모델

| 형식 | 설명 |
|------|-------------|
| `BenchmarkResult` | Name, Category, Tags, Statistics, Samples, IterationsPerSample, SampleCount, Timestamp |
| `ComparisonResult` | Name, Category, Tags, Baseline, Candidate, Speedup, IsFaster, ImprovementPercent |
| `BenchmarkSuite` | Name, Description, Results, Comparisons, Environment, Duration, Timestamp |
| `Statistics` | Avg, P50, P90, P95, P99, Min, Max, StdDev, StandardError, RelativeStdDevPercent, CpuCyclesPerOp, GcInfo |
| `TimingSample` | ElapsedNanoseconds, ElapsedMilliseconds, ElapsedTicks, CpuCycles, GcInfo |
| `GcInfo` | Gen0, Gen1, Gen2, Total, IsZero |
| `EnvironmentInfo` | Os, Architecture, RuntimeVersion, ProcessorCount, ExecutionMode, Configuration, CPU counter kind / availability / meaning, CustomTags |

---

## 아키텍처

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

## 플랫폼별 기능

| 기능 | Windows | Linux | macOS |
|---------|---------|-------|-------|
| 고정밀 타이밍 | Stopwatch | Stopwatch | Stopwatch |
| GC 추적 (Gen0/1/2) | 예 | 예 | 예 |
| CPU 사이클 측정 | `QueryThreadCycleTime` / `QueryProcessCycleTime` | `perf_event_open` (`perf_event_paranoid` ≤ 2일 때) | `mach_absolute_time` (프록시) |
| 프로세스 우선순위 상향 | 예 (범위 한정, 복원됨) | 예 (범위 한정, 복원됨) | 예 (범위 한정, 복원됨) |

macOS에서 내보내는 CPU 카운터는 아키텍처 사이클 수가 아닌 고해상도 단조 프록시입니다. `EnvironmentInfo`와 포맷터 출력이 이 차이를 명시적으로 드러냅니다.

---

## 샘플

| 샘플 | API 스타일 | 설명 |
|--------|-----------|-------------|
| `StringVsStringBuilder` | 명령형 | `string +=`, `StringBuilder`, 용량을 지정한 `StringBuilder` 비교 |
| `AttributeBased` | 속성 | `[Benchmark]`, `[Params]`, 소스 생성기를 사용한 동일 비교 |
| `CollectionBenchmarks` | 속성 | List vs Dictionary vs HashSet 조회 - 모든 특성 시연 |

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
dotnet run --project samples/AttributeBased -c Release
dotnet run --project samples/CollectionBenchmarks -c Release
```

---

## BenchmarkDotNet과의 비교

| 기능 | PicoBench | BenchmarkDotNet |
|---------|-----------|----------------|
| 의존성 | BCL 폴리필 1개 | 다수 |
| 패키지 크기 | 매우 작음 | 큼 |
| 대상 프레임워크 | netstandard2.0 | net6.0+ |
| AOT 지원 | 소스 생성기 | 리플렉션 기반 |
| 속성 API | `[Benchmark]`, `[Params]` | `[Benchmark]`, `[Params]` |
| 설정 시간 | 즉시 | 수 초 |
| 출력 형식 | 5 | 10+ |
| 통계 깊이 | 양호 | 광범위 |
| 용도 | 빠른 A/B 테스트, CI, AOT 앱 | 상세 분석, 논문 |

---

## 라이선스

MIT License - 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.

## 빌드 및 게시

```bash
dotnet build --configuration Release
dotnet test --configuration Release
dotnet pack src/PicoBench/PicoBench.csproj --configuration Release --include-symbols --output ./nupkg
```

릴리스는 **태그 기반**입니다. 버전 태그(예: `git tag v2026.2.0 && git push origin v2026.2.0`)를 푸시하면 GitHub Actions 파이프라인이 테스트, 패키징, [NuGet.org](https://www.nuget.org/packages/PicoBench) 게시를 자동으로 수행합니다.

## 기여

1. 저장소를 포크합니다
2. 기능 브랜치를 만듭니다
3. 테스트와 함께 변경합니다
4. 풀 리퀘스트를 제출합니다
