# 테스트

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

[TUnit](https://github.com/thomhurst/TUnit) 테스트 프레임워크를 사용하는 **PicoBench** 단위 테스트입니다.

**총계: 559개 테스트**

## 실행

```bash
dotnet run --project tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug
```

## 테스트 범주

### Formatters/ (256개)

네 개의 `IFormatter` 기반 출력 포맷터, `SummaryFormatter` 및 이를 지원하는 인프라에 대한 테스트입니다.

| 파일 | 테스트 | 설명 |
|------|-------|-------------|
| `ConsoleFormatterTests.cs` | 41 | 박스 드로잉 테이블 생성, 정렬, 인코딩 |
| `MarkdownFormatterTests.cs` | 28 | GitHub Markdown 테이블 렌더링 |
| `HtmlFormatterTests.cs` | 36 | 스타일이 포함된 HTML 보고서 생성 |
| `CsvFormatterTests.cs` | 35 | 올바른 이스케이프, 수식 주입 방지, 추가(append) 의미론을 포함한 CSV 내보내기 |
| `SummaryFormatterTests.cs` | 26 | 승/패 요약 텍스트 |
| `FormatterBaseTests.cs` | 37 | Template Method 기본 클래스 동작 |
| `FormatterOptionsTests.cs` | 44 | 옵션 기본값, 프리셋, 유효성 검사, 경로 확인 |
| `CrossPlatformTests.cs` | 9 | 줄 바꿈 및 인코딩 일관성 |

### Formatters/Integration/ (6개)

| 파일 | 테스트 | 설명 |
|------|-------|-------------|
| `FormatterIntegrationTests.cs` | 6 | 전체 `BenchmarkSuite` 개체의 엔드투엔드 서식 지정 |

### Attributes/ (18개)

| 파일 | 테스트 | 설명 |
|------|-------|-------------|
| `AttributeTests.cs` | 18 | 7개 특성 전체: 기본값, 속성 설정, `AttributeUsage` 대상, `[Params]` 값 저장 |

### BenchmarkRunnerTests.cs (11개)

| 파일 | 테스트 | 설명 |
|------|-------|-------------|
| `BenchmarkRunnerTests.cs` | 11 | 매개변수 없는 / 미리 구성된 인스턴스를 사용하는 `BenchmarkRunner.Run<T>()`, null 검사, 구성 전달 |

### Generators/ (102개)

| 파일 | 테스트 | 설명 |
|------|-------|-------------|
| `EmitterTests.cs` | 40 | 소스 생성기 코드 생성: 클래스 구조, 매개변수 반복, 설정/해제 훅, 동기/비동기 경로 선택, 기준선 비교, `global::` 정규화 |
| `ModelsTests.cs` (Generators) | 30 | `BenchmarkClassModel`, `BenchmarkMethodModel`, `ParamsPropertyModel` 동등성, 해시 코드, 경계 사례 |
| `BenchmarkGeneratorDiagnosticsTests.cs` | 32 | 잘못된 시그니처, 지원되지 않는 클래스 형태, async void 메서드, 빈 `[Params]`, 상속된 특성, 열거형 매개변수 생성에 대한 엔드투엔드 생성기 진단 |

### 핵심 런타임 커버리지

| 파일 | 테스트 | 설명 |
|------|-------|-------------|
| `BenchmarkTests.cs` | 62 | 명령형 API, 범위 실행, 샘플 보존, 비교, 비동기 의미론, 취소, 보정 하한, 우선순위 범위 |
| `RunnerTests.cs` | 38 | 저수준 타이밍: 유효성 검사, GC 귀속, 비동기 사이클 유효성, Linux perf 가드, 시계 세분성, 우선순위 범위 |
| `StatisticsCalculatorTests.cs` | 12 | 표준오차, CPU 사이클, 경계 사례를 포함한 통계 계산 |
| `ModelsTests.cs` | 39 | 결과 모델 유효성 검사, CPU 카운터 메타데이터, 분산 도우미 |
| `BenchmarkConfigTests.cs` | 15 | 프리셋 값, 불변성, 유효성 검사, 기본 우선순위 동작 |

포맷터 테스트는 Console, Markdown, HTML, CSV 전반에서 표준오차, 상대 표준편차, CPU 카운터 관련 참고 사항 등 정밀도 지향 출력도 다룹니다.

### TestData/

일관된 테스트 픽스처를 만들기 위한 팩터리 클래스:

| 파일 | 용도 |
|------|---------|
| `BenchmarkResultFactory.cs` | 합리적인 기본값으로 `BenchmarkResult` 인스턴스를 생성합니다 |
| `BenchmarkSuiteFactory.cs` | 결과와 비교를 포함한 `BenchmarkSuite`를 생성합니다 |
| `ComparisonResultFactory.cs` | `ComparisonResult` 쌍을 생성합니다 |
| `GcInfoFactory.cs` | `GcInfo` 레코드를 생성합니다 |
| `StatisticsFactory.cs` | 현실적인 분포로 `Statistics`를 생성합니다 |

### Utilities/

| 파일 | 용도 |
|------|---------|
| `FileSystemHelper.cs` | 파일 출력 테스트를 위한 임시 디렉터리 관리 |
| `TestContextLogger.cs` | TUnit 테스트 컨텍스트용 로깅 도우미 |
