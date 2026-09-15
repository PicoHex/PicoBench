# 소스 프로젝트

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

이 디렉터리에는 PicoBench를 구성하는 두 라이브러리 프로젝트가 있습니다.

## PicoBench

**netstandard2.0**을 대상으로 하는 메인 벤치마킹 라이브러리입니다. 유일한 NuGet 참조는 netstandard2.0에 `ValueTask<T>`를 백포트하는 `System.Threading.Tasks.Extensions` BCL 폴리필입니다.

### 주요 파일

| 파일 | 용도 |
|------|---------|
| `Benchmark.cs` | 명령형 API - `Run()`, `RunAsync()`, `Run<TState>()`, `RunAsync<TState>()`, `RunScoped<TScope>()`, `RunScopedAsync<TScope>()`, `Compare()` |
| `BenchmarkRunner.cs` | 속성 기반 진입점 - `Run<T>()` |
| `Attributes.cs` | 7개 특성: `[BenchmarkClass]`, `[Benchmark]`, `[Params]`, `[GlobalSetup]`, `[GlobalCleanup]`, `[IterationSetup]`, `[IterationCleanup]` |
| `IBenchmarkClass.cs` | 데코레이션된 클래스에 소스 생성기가 구현하는 인터페이스 |
| `BenchmarkConfig.cs` | Quick / Default / Precise 프리셋과 선택적 자동 보정을 제공하는 구성 |
| `Runner.cs` | 저수준 타이밍 흐름과 샘플 생성 |
| `Runner.Gc.cs` | GC 기준선과 델타 추적 |
| `Runner.Cpu.cs` | 플랫폼별 CPU 카운터 구현 |
| `StatisticsCalculator.cs` | 백분위수 및 통계 계산 |
| `Models.cs` | `Statistics` 정밀도 필드와 `EnvironmentInfo`의 CPU 카운터 메타데이터를 포함한 결과 형식 |
| `Formatters/` | 4개의 `IFormatter` 구현(Console, Markdown, HTML, CSV)과 `SummaryFormatter` |

### 패키징

프로젝트는 소비자가 소스 생성기를 자동으로 사용할 수 있도록 `PicoBench.Generators`를 분석기로 번들합니다:

```bash
# Add the project reference
dotnet add reference ../PicoBench.Generators/PicoBench.Generators.csproj

# Then manually add the following attributes to the <ProjectReference> element in your .csproj file:
# PrivateAssets="all"
# ReferenceOutputAssembly="false"  
# OutputItemType="Analyzer"
```

## PicoBench.Generators

`[BenchmarkClass]`로 데코레이션된 partial 클래스를 컴파일 시점에 완전한 `IBenchmarkClass` 구현으로 바꾸는 **증분 소스 생성기**(`IIncrementalGenerator`)입니다.

- **대상**: netstandard2.0
- **의존성**: Microsoft.CodeAnalysis.CSharp 5.9.0
- **출력**: `global::` 정규화 호출을 사용하고 리플렉션이 없는 AOT 호환 C#

### 주요 파일

| 파일 | 용도 |
|------|---------|
| `BenchmarkGenerator.cs` | `ForAttributeWithMetadataName`을 사용하는 생성기 진입점 |
| `BenchmarkClassAnalyzer.cs` | 코드 생성 전 Roslyn 분석 및 진단 |
| `CSharpLiteralFormatter.cs` | 생성되는 `[Params]` 값의 C# 리터럴 서식 지정 |
| `DiagnosticDescriptors.cs` | 잘못된 벤치마크 선언에 대한 중앙 집중식 생성기 진단 |
| `Emitter.cs` | C# 코드 생성기 - 매개변수 반복, 설정/해제 훅, 비교 로직을 포함한 `RunBenchmarks()` 생성 |
| `Models.cs` | Roslyn 분석 모델: `BenchmarkClassModel`, `BenchmarkMethodModel`, `ParamsPropertyModel`(모두 캐싱을 위해 `IEquatable<T>` 구현) |

이제 생성기는 코드 생성 전에 흔한 실수를 검증하고, 잘못된 벤치마크 메서드, 수명 주기 메서드, 중복 기준선, 잘못된 `[Params]` 대상, 호환되지 않는 매개변수 값에 대한 진단을 보고합니다.

### 생성된 코드

다음과 같은 클래스가 있으면:

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

생성기는 다음과 같은 작업을 수행하는 `RunBenchmarks()` 메서드를 가진 `partial class MyBench : IBenchmarkClass`를 생성합니다:

1. 각 `[Params]` 값을 반복합니다(속성이 여러 개면 카테시안 곱)
2. 속성을 설정하고 `[GlobalSetup]`을 호출합니다
3. `[IterationSetup]`/`[IterationCleanup]`을 setup/teardown으로 사용해 `Benchmark.Run()`으로 각 `[Benchmark]` 메서드를 실행합니다
4. 후보를 기준선과 비교합니다
5. `[GlobalCleanup]`을 호출합니다
6. 모든 결과와 비교를 담은 `BenchmarkSuite`를 반환합니다
