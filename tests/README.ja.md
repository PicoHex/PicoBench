# テスト

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Русский](README.ru.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Português (Brasil)](README.pt-BR.md)

[TUnit](https://github.com/thomhurst/TUnit) テストフレームワークを使用した **PicoBench** の単体テストです。

**合計：518テスト**

## 実行

```bash
dotnet run --project tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug
```

## テストカテゴリー

### Formatters/ (249テスト)

4 つの `IFormatter` ベースの出力フォーマッター、`SummaryFormatter`、およびその周辺インフラのテスト。

| ファイル | テスト数 | 説明 |
|------|-------|-------------|
| `ConsoleFormatterTests.cs` | 41 | ボックス描画テーブル生成、整列、エンコーディング |
| `MarkdownFormatterTests.cs` | 28 | GitHub Markdownテーブルレンダリング |
| `HtmlFormatterTests.cs` | 36 | スタイル付きHTMLレポート生成 |
| `CsvFormatterTests.cs` | 31 | 適切なエスケープ付きCSVエクスポート |
| `SummaryFormatterTests.cs` | 25 | 勝敗概要テキスト |
| `FormatterBaseTests.cs` | 37 | Template Method基本クラスの動作 |
| `FormatterOptionsTests.cs` | 42 | オプションのデフォルト値、プリセット、パス解決 |
| `CrossPlatformTests.cs` | 9 | 行末とエンコーディングの一貫性 |

### Formatters/Integration/ (6テスト)

| ファイル | テスト数 | 説明 |
|------|-------|-------------|
| `FormatterIntegrationTests.cs` | 6 | 完全な`BenchmarkSuite`オブジェクトのエンドツーエンドフォーマット |

### Attributes/ (18テスト)

| ファイル | テスト数 | 説明 |
|------|-------|-------------|
| `AttributeTests.cs` | 18 | 7つの属性すべて：デフォルト値、プロパティ設定、`AttributeUsage`ターゲット、`[Params]`値ストレージ |

### BenchmarkRunnerTests.cs (11テスト)

| ファイル | テスト数 | 説明 |
|------|-------|-------------|
| `BenchmarkRunnerTests.cs` | 11 | パラメータなし/事前設定済みインスタンスでの`BenchmarkRunner.Run<T>()`、nullチェック、設定伝播 |

### Generators/ (90テスト)

| ファイル | テスト数 | 説明 |
|------|-------|-------------|
| `EmitterTests.cs` | 38 | ソースジェネレーターコード生成：クラス構造、パラメータ反復、セットアップ/ティアダウンフック、ベースライン比較、`global::`修飾 |
| `ModelsTests.cs` | 30 | `BenchmarkClassModel`、`BenchmarkMethodModel`、`ParamsPropertyModel`の等価性、ハッシュコード、エッジケース |
| `BenchmarkGeneratorDiagnosticsTests.cs` | 22 | 無効なシグネチャ、重複 baseline、無効な `[Params]`、enum パラメーター出力に対するエンドツーエンドのジェネレーター診断 |

### コアランタイムのカバレッジ

| ファイル | テスト数 | 説明 |
|------|-------|-------------|
| `BenchmarkTests.cs` | 56 | 命令型 API、scoped 実行、サンプル保持、比較、自動キャリブレーションの動作 |
| `StatisticsCalculatorTests.cs` | 12 | 標準誤差、CPU サイクル、境界ケースを含む統計計算 |
| `ModelsTests.cs` | 38 | 結果モデルの検証、CPU カウンターメタデータ、分散ヘルパー |

フォーマッターテストでは、Console、Markdown、HTML、CSV における標準誤差、相対標準偏差、CPU カウンターノートなどの精度重視の出力もカバーするようになりました。

### TestData/

一貫したテストフィクスチャを構築するためのファクトリークラス：

| ファイル | 目的 |
|------|---------|
| `BenchmarkResultFactory.cs` | 適切なデフォルト値を持つ`BenchmarkResult`インスタンスを作成 |
| `BenchmarkSuiteFactory.cs` | 結果と比較を含む`BenchmarkSuite`を作成 |
| `ComparisonResultFactory.cs` | `ComparisonResult`ペアを作成 |
| `GcInfoFactory.cs` | `GcInfo`レコードを作成 |
| `StatisticsFactory.cs` | 現実的な分布を持つ`Statistics`を作成 |

### Utilities/

| ファイル | 目的 |
|------|---------|
| `FileSystemHelper.cs` | ファイル出力テスト用の一時ディレクトリ管理 |
| `TestContextLogger.cs` | TUnitテストコンテキストのログヘルパー |
