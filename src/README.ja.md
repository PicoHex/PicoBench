# ソースプロジェクト

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Русский](README.ru.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Português (Brasil)](README.pt-BR.md)

このディレクトリには、PicoBenchを構成する2つのライブラリプロジェクトが含まれています。

## PicoBench

**netstandard2.0**をターゲットとする主要なベンチマークライブラリで、外部依存関係ゼロです。

### 主要ファイル

| ファイル | 目的 |
|------|---------|
| `Benchmark.cs` | 命令型API - `Run()`、`Run<TState>()`、`RunScoped<TScope>()`、`Compare()` |
| `BenchmarkRunner.cs` | 属性ベースエントリーポイント - `Run<T>()` |
| `Attributes.cs` | 7つの属性：`[BenchmarkClass]`、`[Benchmark]`、`[Params]`、`[GlobalSetup]`、`[GlobalCleanup]`、`[IterationSetup]`、`[IterationCleanup]` |
| `IBenchmarkClass.cs` | ソースジェネレーターが装飾クラスで実装するインターフェース |
| `BenchmarkConfig.cs` | Quick / Default / Preciseプリセット付き設定 |
| `Runner.cs` | プラットフォーム固有のCPUサイクルカウント付き低レベルタイミングエンジン |
| `StatisticsCalculator.cs` | パーセンタイルと統計計算 |
| `Models.cs` | 結果タイプ：`BenchmarkResult`、`ComparisonResult`、`BenchmarkSuite`、`Statistics`、`TimingSample`、`GcInfo`、`EnvironmentInfo` |
| `Formatters/` | 5つのフォーマッター：Console、Markdown、HTML、CSV、Summary |

### パッケージング

プロジェクトは`PicoBench.Generators`をアナライザーとしてバンドルするため、消費者は自動的にソースジェネレーターを取得します：

```bash
# プロジェクト参照を追加
dotnet add reference ../PicoBench.Generators/PicoBench.Generators.csproj

# その後、.csprojファイルの<ProjectReference>要素に以下の属性を手動で追加してください：
# PrivateAssets="all"
# ReferenceOutputAssembly="false"
# OutputItemType="Analyzer"
```

## PicoBench.Generators

`[BenchmarkClass]`で装飾されたpartialクラスを、コンパイル時に完全な`IBenchmarkClass`実装に変換する**インクリメンタルソースジェネレーター** (`IIncrementalGenerator`)です。

- **ターゲット**：netstandard2.0
- **依存関係**：Microsoft.CodeAnalysis.CSharp 4.3.1
- **出力**：AOT互換のC#、`global::`修飾呼び出し、リフレクションなし

### 主要ファイル

| ファイル | 目的 |
|------|---------|
| `BenchmarkGenerator.cs` | `ForAttributeWithMetadataName`を使用するジェネレーターエントリーポイント |
| `Emitter.cs` | C#コードエミッター - パラメータ反復、セットアップ/ティアダウンフック、比較ロジックを含む`RunBenchmarks()`を生成 |
| `Models.cs` | Roslyn分析モデル：`BenchmarkClassModel`、`BenchmarkMethodModel`、`ParamsPropertyModel`（すべてキャッシュ用に`IEquatable<T>`実装） |

### 生成されるコード

次のようなクラスの場合：

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

ジェネレーターは`partial class MyBench : IBenchmarkClass`を生成し、`RunBenchmarks()`メソッドを含みます。このメソッドは：

1. 各`[Params]`値を反復（複数プロパティの場合はデカルト積）
2. プロパティを設定し、`[GlobalSetup]`を呼び出し
3. `[IterationSetup]`/`[IterationCleanup]`をセットアップ/ティアダウンとして使用し、`Benchmark.Run()`経由で各`[Benchmark]`メソッドを実行
4. 候補をベースラインと比較
5. `[GlobalCleanup]`を呼び出し
6. すべての結果と比較を含む`BenchmarkSuite`を返す