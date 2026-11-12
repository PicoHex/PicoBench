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
- **서브마이크로초 타이밍** - `Stopwatch` 기반의 작업당 계측으로, 하네스 자체의 작업당 비용을 실측해서 차감합니다. 실행의 분해능 한계보다 짧은 워크로드는 상한값(`<limit`)으로 보고되고 `ResolutionLimited`로 플래그가 지정됩니다 — 나노초 정밀도의 수치로 보고되지 않습니다
- **공개된 한계** - `OverheadNs`, `ResolutionLimitNanoseconds`, `ResolutionLimited`, `Unmeasured`는 해당 실행이 무엇을 분해할 수 있었고 무엇을 분해할 수 없었는지를 알려주므로, 노이즈 수준의 결과를 빠른 작업으로 오독할 수 없습니다
- **GC 추적** - 벤치마크 중 Gen0/Gen1/Gen2 수집 횟수를 모니터링합니다(설정/해제 제외)
- **CPU 사이클 측정** - 동기 경로에서는 스레드 범위 사이클 수를 측정합니다(Windows `QueryThreadCycleTime`; Linux는 운영체제가 비특권 접근을 허용할 때 `perf_event`). 비동기 벤치마크는 프로세스 전역 카운터를 읽으며(스레드별 카운터는 스레드 전환을 넘어 추적할 수 없기 때문), `CpuCyclesAreApproximate`로 플래그가 지정됩니다(네 가지 형식 모두에서 `~`). macOS에는 사이클 카운터가 없습니다: `mach_absolute_time`은 사이클이 아닌 단조 *시간* 프록시이며 같은 방식으로 플래그가 지정됩니다
- **통계 분석** - `Avg`, `P50`, `P90`, `P95`, `P99`, `Min`, `Max`, `StdDev`, `StandardError`와 상대 표준 편차(`RSD`). 백분위수는 인접한 두 표본 사이의 선형 보간(통계 패키지가 *type 7*이라 부르는 방식, 즉 Excel `PERCENTILE.INC`와 NumPy 기본값)으로 계산하고 분산은 표본 분산(`N-1`)입니다. 표본 수가 약 20 미만이면 P99가 최댓값에 가까우므로 꼬리 열은 안정적인 꼬리를 뜻하지 않습니다.
- **다양한 출력 형식** - 4개의 내장 포맷터(Console, Markdown, HTML, CSV)와 프로그래밍 방식 요약 출력
- **매개변수화된 벤치마크** - 자동 카테시안 곱 반복을 지원하는 `[Params]` 특성
- **비교 지원** - 기준선과 후보를 교차 라운드로 측정하고, 판정은 라운드별 페어 비율의 중앙값으로 합니다. 5% 데드 밴드와 동등성을 배제하는 분포 비의존 구간이 모두 필요하므로, 라운드 간 노이즈가 개선으로 게시되지 않습니다
- **구성 가능** - Quick, Default, Precise 프리셋, 자동 보정 또는 완전 사용자 정의 구성
- **netstandard2.0** - .NET Framework **4.7.2+**(`RuntimeInformation`을 무조건 사용하므로 이전 .NET Framework 버전은 지원되지 않음), .NET Core 2.0+, .NET 5+ 호환

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

> 잘못된 특성 사용은 partial 누락, 중복 기준선, 잘못된 수명 주기 시그니처, 호환되지 않는 `[Params]` 값 등 일반적인 실수에 대해 생성기 진단을 발생시킵니다. 또한 `async void` 벤치마크(PBGEN011), 제네릭/중첩/레코드/인스턴스화 불가 벤치마크 클래스(PBGEN012-015)를 거부하고, 빈 `[Params]`(PBGEN016)와 기본 형식의 벤치마크 특성(PBGEN017)에 대해 경고합니다.

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

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%) [{(comparison.Interval?.ToString() ?? "no interval")}] -> {comparison.Outcome}");
```

`Benchmark.Compare(name, baselineAction, candidateAction)`는 각 레인을 한 번씩만 실행하지 **않습니다**. 두 레인을 10개 라운드에 걸쳐 교차 실행하고(매 라운드 어느 쪽이 먼저인지가 번갈아 바뀌므로 어느 쪽도 더 뜨거운 프로세스를 물려받지 않습니다) **라운드별 페어 비율의 중앙값**을 보고합니다. 이것이 수치에 대해 의미하는 바는 다음과 같습니다:

- **`Speedup`** — 기준선 시간 ÷ 후보 시간이며, `> 1`이면 후보가 더 빠릅니다. 후보가 아무것도 측정하지 못한 라운드는 비율에 전혀 기여하지 않습니다. 사용 가능한 비율이 하나도 없는 경우 비교는 판정을 만들어내지 않고 `NaN`을 보고합니다(`ImprovementPercent`도 함께 `NaN`이 됩니다). 두 개의 독립적인 `BenchmarkResult`로 구성된 `ComparisonResult`는 두 평균의 비율을 보고합니다. 0/0 라운드가 바로 이 경우이며 `1.0`이 아니라 `NaN`입니다(모두 0인 두 독립 결과로 만든 비교는 기존처럼 `1.0`을 유지합니다).
- **`Interval`** — 그 중앙값의 분포 비의존 95% 구간입니다(10개 페어 비율의 2번째/9번째 순서 통계량). `Interval.Contains(1.0)`은 동등성이 여전히 그럴듯하다는 뜻입니다. 두 개의 독립적인 결과로 구성된 비교에는 **구간이 없습니다**(`Interval`은 `null`) — 페어로 묶을 교차 라운드가 없으므로 산포에 대해 말할 수 있는 것이 없습니다.
- **`Outcome`** — 판정입니다. `Faster` / `Slower`는 비율이 **5% 데드 밴드**를 벗어나고 *그리고* (구간이 존재할 때) 구간이 1을 배제할 것을 요구합니다. 그 안에 있으면 `Inconclusive`, 계산할 수 없는 비율(비유한 또는 비양수)은 `Unknown`이며, 사용 가능한 페어 라운드가 6개 미만인 비교도 마찬가지입니다(그 미만에서는 구간이 중앙값을 95% 신뢰도로 포함할 수 없습니다).
- **`IsFaster`** — `Outcome == Faster`인 경우에만 `true`입니다. 즉 **이전보다 좁아졌습니다**: 3% 차이는 이제 `false`(`Inconclusive`)를 반환하고, 라운드가 없는 비교도 같은 5% 데드 밴드를 통과해야 합니다. 3방향 답에는 `Outcome`을, "측정 가능한 차이 없음"과 "전혀 측정할 수 없음"의 구분에는 `IsFasterKnown`을 사용하세요.

비용: 직접 비교는 각 레인을 10회씩, 즉 **20 `Run`**을 실행합니다 — 너무 느리면 A/B 실행에서 `SampleCount` / `WarmupIterations`를 낮추세요.

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
- **측정 불가 샘플** — `CpuOnly` 모드에서 운영체제 타이머 틱 하나보다 짧은 워크로드는 CPU 시간이 정확히 0으로 읽힙니다. 이런 샘플은 `Statistics.UnmeasuredSampleCount`에 집계되며(`Unmeasured`가 `true`), 작업당 값은 `0.000`이 아니라 **하나의 타이밍 양자 — 상한값**이 되고, 포맷터는 작업당 셀을 `N/A`로 표시합니다. 그런 실행의 통계는 작업에 대한 측정값이 아닙니다. "CPU 클록의 분해능 미만"으로 읽어야 합니다. 문제를 숨기기 위해 반복 횟수가 조용히 부풀려지지 않습니다 — 보정하는 것은 `AutoCalibrateIterations = true`(`Quick`/`Precise` 프리셋)인 경우뿐이며, `Default`는 구성된 `IterationsPerSample`을 그대로 사용합니다.
- **GC 귀속** — 비동기 GC 카운트는 근사치로 표시됩니다(`GcInfo.IsApproximate`). setup/teardown 할당은 두 모드 모두에서 제외됩니다.
- **CPU 사이클** — 동기 경로는 스레드 범위 카운터를 읽고(Windows의 `QueryThreadCycleTime`, Linux에서는 측정 스레드를 위해 연 `perf_event`), 비동기 벤치마크는 프로세스 전역 카운터를 읽습니다(스레드별 카운터는 스레드 전환을 넘어 추적할 수 없기 때문). 그 수치에는 다른 스레드가 한 작업이 포함되며 `Statistics.CpuCyclesAreApproximate`로 플래그가 지정되어 Console, Markdown, HTML, CSV에서 `~`와 함께 렌더링됩니다. macOS에는 사이클 카운터가 없으며, `mach_absolute_time`은 아키텍처 사이클이 아닌 단조 *시간* 프록시이고 같은 방식으로 플래그가 지정됩니다.
- **취소** — `BenchmarkConfig.CancellationToken`은 비동기 벤치마크의 샘플 경계에서만 확인됩니다. 동기 벤치마크는 이를 무시합니다.
- **혼합 클래스** — 클래스에 다른 비동기 멤버가 있어도 동기 `[Benchmark]` 메서드는 직접 동기 측정 경로를 유지하므로 비동기 래퍼 오버헤드를 부담하지 않습니다.
- **예열** — 예열 반복은 예열 델리게이트만 호출합니다. `setup`/`teardown`과 `[IterationSetup]`/`[IterationCleanup]`은 예열 중에 실행되지 않습니다. 예열 동작은 자체적으로 완결되어야 합니다.

---

## 측정 충실도

PicoBench는 빠른 시작을 위해 프로세스 내에서 실행되므로 CLR 설정이 절대값에 영향을 줍니다. 비교 가능한 결과를 얻으려면:

- 정상 상태 코드를 측정하도록 계층화 JIT를 비활성화하세요: `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0`(또는 `COMPlus_*`).
- 단일 스레드 마이크로 벤치마크에는 워크스테이션 GC를 사용해 백그라운드 서버 GC 스레드 노이즈를 피하세요: `DOTNET_gcServer=0`.
- CPU 사이클 수는 운영체제가 비특권 카운터를 허용하는 경우에만 사용할 수 있습니다(Windows `QueryThreadCycleTime`; Linux는 `perf_event_paranoid`가 2 이하일 때 `perf_event`). Windows의 동기 실행은 스레드 범위이고, 비동기 실행과 macOS는 프로세스 전역 카운터를, macOS에서는 실제 사이클 대신 단조 클록 프록시를 사용합니다 — 둘 다 `CpuCyclesAreApproximate`로 플래그가 지정됩니다. `EnvironmentInfo`와 모든 포맷터가 사용된 소스를 보고합니다.
- `BenchmarkConfig.BoostPriorities`(기본값 `true`)는 실행 중에만 프로세스와 스레드 우선순위를 높이고 종료 후 이전 값으로 복원합니다.

### 숫자가 분해할 수 있는 것과 없는 것

모든 실행은 동일한 반복 횟수로 하네스 자체가 작업당 추가하는 비용을 측정해 차감한 다음, 여전히 분해할 수 있는 가장 작은 값을 공개합니다:

| 필드 | 의미 |
|---|---|
| `Statistics.OverheadNs` | 측정해서 차감한 하네스의 작업당 비용(델리게이트 디스패치, 루프 관리, 클록 및 카운터 읽기). 비동기 실행에서는 오버헤드를 차감하지 않으므로 `0` |
| `Statistics.ResolutionLimitNanoseconds` | 이 실행이 분해할 수 있는 가장 작은 작업당 값: 클록 틱 하나를 반복 횟수로 나눈 값, 차감한 오버헤드의 실행 간 산포, 그 오버헤드의 작업당 크기(상한) 중 가장 큰 값 |
| `Statistics.ResolutionLimited` | `Avg`가 그 한계 이하이거나, 적어도 하나의 샘플을 전혀 측정하지 못했음을 나타냅니다. 포맷터는 작업당 셀을 숫자가 아닌 상한값(네 가지 형식 모두 `<limit`입니다. Console은 `[below resolution]`, Markdown과 HTML은 `(below resolution)`을 덧붙입니다)으로 출력합니다 |
| `Statistics.Unmeasured` / `UnmeasuredSampleCount` | 타이밍 소스가 전혀 진행되지 못한 샘플 수(`CpuOnly`의 제로 틱). 해당 셀은 `0.000`이 아니라 `N/A`로 출력됩니다 |

따라서 서브나노초 결과에 대한 정직한 해석은 "0.02 ns"가 아니라 **"`<limit` ns 미만"**입니다: 그 값은 측정된 것이 아니라 상한으로 제한된 것이며, 그 상한은 클록 양자화와 차감한 오버헤드의 산포를 포괄합니다(클록 틱, 차감한 오버헤드의 산포, 그 오버헤드의 작업당 크기 중 가장 큰 값이며, 잔차가 0으로 클램프되므로 모든 샘플에서 참값 ≤ 잔차 + 오버헤드가 성립합니다. 따라서 이 상한이 같은 실행에서 차감한 하한보다 낮아질 수는 없습니다). 두 가지 비대칭도 기억할 가치가 있습니다: 하네스 오버헤드 차감은 동기 벤치마크에만 구현되어 있고(비동기 작업당 값은 여전히 샘플별 래퍼 비용을 안고 있음), GC 바이트 수는 전혀 측정되지 않습니다 — 프로세스 단위의 수집 횟수만 있으며 비동기 경로에서는 근사값입니다.

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

자동 보정이 활성화되면 PicoBench는 최소 샘플 시간 예산에 도달하거나 `MaxAutoIterationsPerSample`에 도달할 때까지 `IterationsPerSample`을 늘립니다. 이는 타이머 노이즈에 지배될 수 있는 초고속 작업에 특히 유용합니다. 자동 보정은 **옵트인**입니다(`AutoCalibrateIterations`의 기본값은 `false`이며 `Default` 프리셋은 설정된 반복 횟수를 건드리지 않습니다): PicoBench는 결과가 측정 가능해 보이도록 반복 횟수를 조용히 부풀리지 않습니다 — `CpuOnly` 모드에서 올려진 예산은 측정된 CPU 클럭 입도이며, 그보다 짧은 값은 0으로 읽히기 때문입니다. `ForceGcBeforeBenchmark = false`로 설정하면 수집 단계 전의 강제 전체 GC를 건너뜁니다. 많은 벤치마크와 매개변수 조합을 실행할 때 유용합니다.

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

네 포맷터 모두 동일한 작업당 지표 — `Avg`, `P50`, `P90`, `P95`, `P99`, `StandardError`, 상대 표준 편차(`RSD (%)` 열) — 와 사용 가능한 경우의 CPU 카운터 설명을 보여 줍니다. Console 스위트 표에도 `StdErr`와 `RSD` 열이 있습니다(이전에는 빠져 있어 노이즈 신호가 기본 출력에 보이지 않았습니다). Console·Markdown·HTML은 같은 데이터에 같은 표 단위를 쓰고, CSV는 항상 나노초로 남습니다(아래 참조).

### 정밀도 프로파일: raw와 display

같은 숫자가 두 가지 역할을 하므로, 포맷터는 각자 타협하지 않고 둘을 분리합니다. 값은 **raw** — 측정된 그대로의 나노초 — 이거나 **display** — 표에 맞는 단위로 환산한 같은 값 — 입니다.

- **Raw** (`MetricPrecision.Raw`): 가공하지 않은 나노초이며, 열 머리글(또는 `Avg_ns`, `StdErr_ns` 같은 CSV 열 이름)이 `ns`라고 밝힙니다. 데이터에 어떤 단위가 어울리든 `CsvFormatter`는 항상 이 형태로 씁니다 — 단위가 데이터 크기에 따라 바뀌는 데이터 파일은 기계가 읽을 수 없습니다.
- **Display** (`MetricPrecision.Display`): 표의 모든 작업당 열에 **하나**의 단위를 골라 — 1 000 ns 미만은 `ns`, 1 000 µs 미만은 `µs`, 1 000 ms 미만은 `ms`, 그 이상은 초 — 머리글에 그 단위를 적습니다(`Avg (µs)`). 셀이 아니라 표마다 하나의 단위이므로 열은 계속 비교 가능합니다. 5초 벤치마크는 `Avg (s)` 아래에서 `5.0`으로, 같은 머리글 아래의 150 ns 이웃은 `0.0`으로 읽히며 조용히 단위가 바뀌지 않습니다.

```csharp
var raw = new FormatterOptions { PrecisionProfile = MetricPrecision.Raw };
Console.WriteLine(new ConsoleFormatter(raw).Format(suite)); // "Avg (ns)" 아래 5000000000.0
```

Console, Markdown, HTML에서 표시되는 문자열은 완전히 동일하고(테스트가 문자 단위로 단언합니다) 원래 숫자는 항상 CSV에 있으므로, 환산된 표시가 측정값의 유일한 사본이 되는 일은 없습니다.

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
    PrecisionProfile      = MetricPrecision.Display, // 또는 .Raw; CSV는 항상 raw
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

### CSV 열 계약

`CsvFormatter`는 헤더 행을 쓰고 이어서 데이터 행을 씁니다. 스위트는 `#` 주석 행(`# Schema:` 계약 행, `# key,value` 메타데이터 행: `Suite`, `Description`, `Environment`, `CpuCounter`, `CpuCounterNote`, `GcInfoNote`, `Timestamp`, `Duration`, `NoiseNote`)과 두 섹션 `# Results`, `# Comparisons`를 추가하며, **각 섹션은 자체 헤더 행**을 가집니다. 따라서 파일에는 헤더 행이 둘 이상 있으므로 `pandas.read_csv(path, comment='#')`는 주석을 건너뛰지만 두 섹션은 직접 분할해야 합니다. 결과 파일의 첫 행이 이 `# Schema: v2; …` 계약 행이며, `#`로 시작하는 모든 행은 파서가 건너뛰어야 하는 주석입니다.

메타데이터 행은 다음 물리적 행으로 이어지지 않습니다. 값 안의 줄바꿈은 두 문자 `\n`으로 기록되며(CRLF, CR, LF 모두 동일), 필요하면 CSV 규칙대로 인용됩니다. 덕분에 여러 줄 설명에서도 `comment='#'`가 안전합니다.

결과 열(이 순서, 기본 옵션. `IncludePercentiles`, `IncludeGcInfo`, `IncludeCpuCycles`, `IncludeTimestamp`를 `false`로 하면 해당 열이 사라집니다):

| # | 열 | 의미 |
|---|---|---|
| 1 | `Name` | 벤치마크 이름(`,` `"` 또는 줄바꿈을 포함하면 인용부호로 감싸고, 선행 `=`/`+`/`-`/`@`에는 `'`를 앞에 붙임) |
| 2 | `Category` | 카테고리. 설정되지 않으면 비어 있음 |
| 3 | `Avg_ns` | 작업당 나노초 — 세 가지 셀 상태가 있으며 아래 참조 |
| 4 | `P50_ns` | 작업당 중앙값 나노초(같은 세 가지 상태) |
| 5-12 | `P90_ns`, `P95_ns`, `P99_ns`, `Min_ns`, `Max_ns`, `StdDev_ns`, `StdErr_ns`, `RSD_pct` | 백분위와 산포. `RSD_pct`는 단순 백분율이지만 나머지 일곱 개는 작업당 셀 상태를 사용합니다 |
| 13 | `CpuCycles` | 작업당 사이클 수. `~` 접두사는 스레드 사이클이 아니라 프로세스 전역 카운터 또는 클록 프록시(`CpuCyclesAreApproximate`)를 표시합니다 |
| 14-16 | `GC0`, `GC1`, `GC2` | 수집 횟수. GC가 수집되지 않았으면 `N/A` |
| 17 | `GCApproximate` | 카운터가 근사값(비동기 경로)이면 `1`, 정확하면 `0`, 수집되지 않았으면 `N/A` |
| 18 | `SampleCount` | 샘플 수(정수) |
| 19 | `IterationsPerSample` | 샘플당 반복 횟수(정수) |
| 20 | `Timestamp` | ISO-8601 라운드트립(`o`, 인바리언트 컬처) |

작업당 열의 셀 상태:

| 셀 | 의미 |
|---|---|
| `12.5` | 측정된 나노초 값(인바리언트 컬처, 라운드트립 형식) |
| `<0.02` | **상한값**: 실행이 `Statistics.ResolutionLimitNanoseconds`를 넘는 값을 분해하지 못했습니다. `<` 뒤의 숫자가 그 한계이며 측정된 값이 아닙니다 |
| `N/A` | 측정되지 않았거나(`Statistics.Unmeasured`, 클록 델타가 0) 수집되지 않음(GC 열) |

소비자에게 두 가지 영향이 있습니다: `<0.02` 때문에 작업당 열은 **숫자임이 보장되지 않으므로** pandas 같은 리더는 `object`로 추론하며 상한 행을 명시적으로 처리해야 합니다. 또한 `GCApproximate`가 `GC2`와 `SampleCount` 사이인 17번 위치에 삽입되었으므로 **위치 기반 리더는 `SampleCount`/`IterationsPerSample`을 하나씩 밀어야 합니다**(이름 기반 리더는 영향을 받지 않음).

비교 열: `TestCase`, `Provider`, `Avg_ns`, `Speedup`, `P50_ns`, `P90_ns`, `P99_ns`, `StdErr_ns`, `RSD_pct`, `CpuCycles`, `GC0`, `GC1`, `GC2`, `GCApproximate`, `SampleCount`, `IterationsPerSample`. 각 비교는 두 행(후보가 먼저, 기준선이 다음)을 쓰고 `Speedup`은 후보 행에만 채워집니다. 비교의 작업당 셀은 결과 테이블과 동일한 포맷터를 거치므로 `<limit`와 `N/A`가 거기에도 나타나며, `CpuCycles`에는 동일한 `~` 표시(`CpuCyclesAreApproximate`)가 붙습니다.

`# Schema:` 행은 이미 반영되었고 **파일의 첫 행**입니다. 결과 파일(그리고 결과가 있는 스위트 파일)은 뒤따르는 열을 설명하는 계약 행으로 시작하며, 비교 섹션은 `# Comparisons` 바로 앞에 자체 계약 행을 씁니다:

```
# Schema: v2; section=results; columns=Name|Category|Avg_ns|P50_ns|P90_ns|P95_ns|P99_ns|Min_ns|Max_ns|StdDev_ns|StdErr_ns|RSD_pct|CpuCycles|GC0|GC1|GC2|GCApproximate|SampleCount|IterationsPerSample|Timestamp; per-op cells (Avg_ns,P50_ns,P90_ns,P95_ns,P99_ns,Min_ns,Max_ns,StdDev_ns,StdErr_ns) = number (nanoseconds) | "<limit" (upper bound: the value is at the harness floor) | "N/A" (not measured); RSD_pct = percent; SampleCount = samples taken | IterationsPerSample = iterations per sample; CpuCycles = count | "~count" (not a thread-scoped cycle count); GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected)
# Schema: v2; section=comparisons; columns=TestCase|Provider|Avg_ns|Speedup|P50_ns|P90_ns|P99_ns|StdErr_ns|RSD_pct|CpuCycles|GC0|GC1|GC2|GCApproximate|SampleCount|IterationsPerSample; per-op cells (Avg_ns,P50_ns,P90_ns,P99_ns,StdErr_ns) = number (nanoseconds) | "<limit" (upper bound: the value is at the harness floor) | "N/A" (not measured); RSD_pct = percent; Speedup = ratio (baseline avg / candidate avg, or the median of the per-round paired ratios for a direct comparison) | "N/A" (no finite ratio: a candidate average of zero is unbounded, a non-finite candidate average is unmeasurable, or too few usable paired rounds); SampleCount = samples taken | IterationsPerSample = iterations per sample; CpuCycles = count | "~count" (not a thread-scoped cycle count); GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected)
```

`v2`는 Batch 0의 형태 — 위의 작업당 세 가지 셀 상태에 `GCApproximate`를 더한 것입니다. `columns=` 목록은 실제로 쓰이는 헤더 행에서 파생되므로 위 표와 어긋날 수 없고, 옵션으로 빠진 열을 나열하지도 않습니다. `section=`이 두 계약 행을 구분합니다. 파서는 `#`로 시작하는 모든 행을 건너뛰고, 열 순서는 하드코딩하지 말고 `columns=` 목록에서 읽어야 합니다.

---

## 결과 모델

| 형식 | 설명 |
|------|-------------|
| `BenchmarkResult` | Name, Category, Tags, Statistics, Samples, IterationsPerSample, SampleCount, Timestamp |
| `ComparisonResult` | Name, Category, Tags, Baseline, Candidate, Speedup, ImprovementPercent, **Outcome**, **IsFasterKnown**, **Interval**, IsFaster |
| `BenchmarkSuite` | Name, Description, Results, Comparisons, Environment, Duration, Timestamp |
| `ComparisonOutcome` | 열거형: `Unknown`(비율을 계산할 수 없음), `Inconclusive`(5% 데드 밴드 내부이거나 구간이 여전히 동등성을 포함), `Faster`, `Slower` |
| `ComparisonInterval` | Low, High, Contains(value), ToString() — 중앙값 페어 비율의 분포 비의존 95% 구간 |
| `Statistics` | Avg, P50, P90, P95, P99, Min, Max, StdDev, StandardError, RelativeStdDevPercent, CpuCyclesPerOp, **CpuCyclesAreApproximate**, **OverheadNs**, **ResolutionLimitNanoseconds**, **ResolutionLimited**, **Unmeasured**, **UnmeasuredSampleCount**, GcInfo, HasHighVariance |
| `TimingSample` | ElapsedNanoseconds, ElapsedMilliseconds, ElapsedTicks, CpuCycles, **CpuCyclesAreApproximate**, GcInfo |
| `GcInfo` | Gen0, Gen1, Gen2, Total, IsZero, IsApproximate |
| `EnvironmentInfo` | Os, Architecture, RuntimeVersion, ProcessorCount, ExecutionMode, Configuration, CPU counter kind / availability / meaning, CustomTags |

측정 충실도 작업으로 추가된 멤버의 의미:

| 멤버 | 계약 |
|---|---|
| `ComparisonResult.Outcome` | 3방향 판정. `Faster`/`Slower`는 5% 데드 밴드**와** 1을 배제하는 구간이 모두 필요 |
| `ComparisonResult.IsFasterKnown` | `Outcome != Unknown` — "두 레인이 비교되었다"(여전히 구분되지 않을 수 있음) |
| `ComparisonResult.IsFaster` | `Outcome == Faster`인 경우에만. 기존 `Speedup > 1.0`보다 좁으며, 데드 밴드 내부의 차이는 더 이상 "더 빠름"이 아닙니다 |
| `ComparisonResult.Interval` | `ComparisonInterval?`. 교차 라운드가 아니라 두 개의 독립적인 결과로 비교를 구성한 경우 `null` |
| `Statistics.ResolutionLimited` | 작업당 값이 측정값이 아니라 상한값(`<limit`)임을 나타냅니다 |
| `Statistics.UnmeasuredSampleCount` / `Unmeasured` | 타이밍 소스가 진행되지 못한 샘플. 해당 작업당 셀은 `N/A` |
| `Statistics.CpuCyclesAreApproximate` | `CpuCyclesPerOp`가 스레드 사이클이 아니라 프로세스 전역(비동기) 또는 단조 클록 프록시(macOS)임을 나타냅니다. 포맷터는 `~`를 앞에 붙입니다 |
| `TimingSample.CpuCyclesAreApproximate` | 샘플 수준의 같은 플래그. `Statistics`는 논리합으로 집계합니다 |

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
| 타이밍 | Stopwatch | Stopwatch | Stopwatch |
| GC 추적 (Gen0/1/2) | 예 | 예 | 예 |
| CPU 사이클 측정, 동기 | `QueryThreadCycleTime`(스레드 범위) | 스레드당 `perf_event_open`(`perf_event_paranoid` ≤ 2일 때) | `mach_absolute_time`, **시간 프록시** — `~` 플래그 지정 |
| CPU 사이클 측정, 비동기 | `QueryProcessCycleTime`(프로세스 전역) — `~` 플래그 지정 | 프로세스 전역 `perf_event` — `~` 플래그 지정 | `mach_absolute_time` — `~` 플래그 지정 |
| 프로세스 우선순위 상향 | 예 (범위 한정, 복원됨) | 예 (범위 한정, 복원됨) | 예 (범위 한정, 복원됨) |

macOS에서 내보내는 CPU 카운터는 아키텍처 사이클 수가 아닌 고해상도 단조 *시간* 프록시입니다: 값은 실행된 명령 수가 아니라 월 클록과 함께 진행하므로 `CpuCyclesAreApproximate`로 플래그가 지정되고 `~`와 함께 출력됩니다. 같은 플래그가 비동기 경로에도 사용되며, 그 프로세스 전역 카운터에는 다른 스레드가 한 작업이 포함됩니다. `EnvironmentInfo`와 포맷터 출력은 두 구분을 모두 명시적으로 보여줍니다.

---

## 샘플

| 샘플 | API 스타일 | 설명 |
|--------|-----------|-------------|
| `StringVsStringBuilder` | 명령형 | `string +=`, `StringBuilder`, 용량을 지정한 `StringBuilder` 비교 |
| `AttributeBased` | 속성 | `[Benchmark]`, `[Params]`, 소스 생성기를 사용한 동일 비교 |
| `CollectionBenchmarks` | 속성 | List vs Dictionary vs HashSet 조회 - 모든 특성 시연 |
| `AsyncBenchmarks` | 특성(비동기) | 비동기 진입점과 혼합 수명 주기: `BenchmarkRunner.RunAsync<T>()`, 비동기 `[GlobalSetup]`/`[IterationSetup]`, `Task`를 반환하는 `[Benchmark]`, `SummaryFormatter` |

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
dotnet run --project samples/AttributeBased -c Release
dotnet run --project samples/CollectionBenchmarks -c Release
dotnet run --project samples/AsyncBenchmarks -c Release
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
```

릴리스는 PicoHex 버전 규칙 `<year>.<x>.<y>`를 따릅니다. 공개 API가 변경되면 **x**를, 변경되지 않으면 **y**를 올립니다. 판단은 저장소의 API 베이스라인 `api/PicoBench.public.txt`를 기준으로 하며, 이 파일은 항상 마지막 릴리스의 공개 표면을 담습니다. 베이스라인이 없으면 한 번 시드합니다: `pwsh ./scripts/release.ps1 -Bootstrap -FromTag <last-tag>`.

```bash
# API 델타와 다음 버전 확인
pwsh ./scripts/release.ps1 -DryRun

# 릴리스: api/ 갱신, chore(release): v<version> 커밋, 태그, push
pwsh ./scripts/release.ps1 -Push
```

푸시된 태그가 GitHub Actions 릴리스 파이프라인을 트리거하면 테스트 실행, 태그 버전으로 모든 패키지 pack, [NuGet.org](https://www.nuget.org/packages/PicoBench) 게시가 자동으로 수행됩니다. 형제 PicoHex 저장소의 인덱싱 공백을 메우려면 같은 버전을 `NuGet.config`에 선언된 로컬 피드로 pack할 수 있습니다:

```bash
dotnet pack src/PicoBench/PicoBench.csproj -c Release -o artifacts/nupkg -p:Version=<version>
```

## 기여

1. 저장소를 포크합니다
2. 기능 브랜치를 만듭니다
3. 테스트와 함께 변경합니다
4. 풀 리퀘스트를 제출합니다
