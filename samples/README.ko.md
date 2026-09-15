# 샘플

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

네 개의 샘플 프로젝트가 PicoBench의 두 API를 보여 줍니다.

## StringVsStringBuilder (명령형 API)

명령형 `Benchmark.Run()` 및 `Benchmark.Compare()` API로 여러 크기의 문자열 연결 전략을 측정합니다.

**하이라이트:**

- 클로저와 `BenchmarkConfig.Quick`을 사용하는 `Benchmark.Run()`
- 클로저 할당을 피하기 위한 상태 기반 `Benchmark.Run<TState>()`
- 사용자 정의 그룹화를 위한 수동 `ComparisonResult` 생성
- 모든 결과와 비교를 담은 `BenchmarkSuite` 구성
- `FormatterOptions`(사용자 정의 레이블)를 통한 Console, Markdown, HTML, CSV 출력

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
```

## AttributeBased (속성 API + 소스 생성기)

동일한 문자열 벤치마크를 속성 기반 API로 다시 작성합니다.

**하이라이트:**

- `Description`이 있는 `[BenchmarkClass]`
- 매개변수화된 실행을 위한 `[Params(10, 100, 1000)]`
- 기준 메서드를 표시하는 `[Benchmark(Baseline = true)]`
- 매개변수 조합별 준비를 위한 `[GlobalSetup]`
- `BenchmarkRunner.Run<T>()`를 통한 한 줄 실행
- 빠른 승/패 개요를 위한 `SummaryFormatter`

```csharp
[BenchmarkClass(Description = "Comparing string concatenation strategies")]
public partial class StringBenchmarks
{
    [Params(10, 100, 1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() { }

    [Benchmark(Baseline = true)]
    public void StringConcat() { /* ... */ }

    [Benchmark]
    public void StringBuilder() { /* ... */ }

    [Benchmark]
    public void StringBuilderWithCapacity() { /* ... */ }
}
```

```bash
dotnet run --project samples/AttributeBased -c Release
```

## AsyncBenchmarks (비동기 API + 혼합 수명 주기)

완전 비동기 진입점과 동기/비동기 수명 주기 메서드의 혼합을 사용해 비동기 파일 I/O를 시뮬레이션합니다.

**하이라이트:**

- `BenchmarkRunner.RunAsync<T>()` — 완전 비동기 실행
- 임시 파일 픽스처를 공유하는 동기 메서드로서의 `[GlobalSetup]` / `[GlobalCleanup]`
- `async Task` 메서드로서의 `[IterationSetup]`
- `Task`를 반환하는 `[Benchmark(Baseline = true)]` — 순차 비동기 읽기
- 대조를 위한 `Task.WhenAll` 병렬 읽기 후보와 동기 읽기
- 빠른 승/패 개요를 위한 `SummaryFormatter`
- Console 및 Markdown 출력

```csharp
[BenchmarkClass(Description = "Simulating async I/O operations")]
public partial class FileSimulationBenchmarks
{
    private string? _tempPath;

    [GlobalSetup]
    public void SetupSync() { /* create temp-file fixture */ }

    [GlobalCleanup]
    public void CleanupSync() { /* delete temp file */ }

    [IterationSetup]
    public async Task PrepareAsync() => await Task.Delay(1);

    [Benchmark(Baseline = true)]
    public async Task SequentialReadsAsync() { /* three awaited reads */ }

    [Benchmark]
    public async Task ParallelReadsAsync() { /* Task.WhenAll */ }

    [Benchmark]
    public void SyncRead() { /* three sync reads */ }
}
```

```bash
dotnet run --project samples/AsyncBenchmarks -c Release
```

## CollectionBenchmarks (전체 속성 쇼케이스)

List, Dictionary, HashSet 조회 성능을 비교하며 **대부분의** 속성을 보여 줍니다.

**하이라이트:**

- `Description`이 있는 `[BenchmarkClass]`
- `[Params(100, 1_000, 10_000)]` - 세 가지 컬렉션 크기
- `[GlobalSetup]` - 무작위 데이터로 세 컬렉션을 채웁니다
- `[GlobalCleanup]` - 컬렉션을 해제합니다
- `[IterationSetup]` - 각 샘플 전에 조회 대상을 섞습니다
- `[Benchmark(Baseline = true, Description = "...")]` - 기준선으로 `List.Contains()`
- `[Benchmark(Description = "...")]` - `Dictionary.ContainsKey()` 및 `HashSet.Contains()`
- 다중 형식 출력: Console, Markdown, HTML, CSV

```csharp
[BenchmarkClass(Description = "Lookup performance: List vs Dictionary vs HashSet")]
public partial class LookupBenchmarks
{
    [Params(100, 1_000, 10_000)]
    public int N { get; set; }

    [GlobalSetup]   public void Setup()         { /* populate collections */ }
    [GlobalCleanup] public void Cleanup()       { /* release collections */ }
    [IterationSetup] public void ShuffleTarget() { /* vary lookup target */ }

    [Benchmark(Baseline = true, Description = "Linear scan O(n)")]
    public void ListContains() { _ = _list.Contains(_target); }

    [Benchmark(Description = "Hash lookup O(1)")]
    public void DictionaryContainsKey() { _ = _dictionary.ContainsKey(_target); }

    [Benchmark(Description = "Hash lookup O(1), set-optimised")]
    public void HashSetContains() { _ = _hashSet.Contains(_target); }
}
```

```bash
dotnet run --project samples/CollectionBenchmarks -c Release
```

## 출력

`StringVsStringBuilder`와 `CollectionBenchmarks`는 결과를 출력 폴더 아래 `results/` 하위 디렉터리에 Markdown, HTML, CSV 형식으로 저장합니다. `AttributeBased`와 `AsyncBenchmarks`는 현재 Markdown 출력만 저장합니다.

이 보고서에는 사용 가능한 경우 표준오차, 상대 표준편차, CPU 카운터 관련 참고 사항 등 정밀도 중심 메타데이터가 포함됩니다.
