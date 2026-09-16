# PicoBench

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

一個輕量級、零依賴的 .NET 基準測試庫，提供 **兩種互補的 API**：命令式 API 和基於屬性、源生成的 API，完全 **AOT 相容**。

## 特性

- **零依賴** - 純 .NET 實現，無需外部套件
- **兩種 API** - 命令式 (`Benchmark.Run`) 用於臨時測試；基於屬性 (`[Benchmark]` + 源生成器) 用於結構化測試套件
- **AOT 相容的源生成器** - 增量生成器在執行時生成直接方法呼叫，零反射
- **跨平台** - 完全支援 Windows、Linux 和 macOS
- **高精度計時** - 使用 `Stopwatch` 並回報奈秒尺度的每操作耗時
- **GC 追蹤** - 監控基準測試期間的 Gen0/Gen1/Gen2 回收計數（不含 setup/teardown）
- **CPU 週期計數** - Windows / Linux 提供硬體週期計數，macOS 提供單調時鐘代理值（`mach_absolute_time`）
- **統計分析** - 均值、中位數、P90、P95、P99、最小值、最大值、標準差、標準誤和相對標準差
- **多種輸出格式** - 4 個內建 `IFormatter` 格式化器（Console、Markdown、HTML、CSV）以及程式化摘要輸出
- **參數化基準測試** - `[Params]` 屬性，支援自動笛卡爾積迭代
- **比較支援** - 基準 vs 候選實現，帶加速比計算
- **可配置** - Quick、Default 和 Precise 預設、自動校準或完全自訂配置
- **netstandard2.0** - 相容 .NET Framework 4.6.1+、.NET Core 2.0+、.NET 5+

## 安裝

引用 **PicoBench** NuGet 套件。源生成器 (`PicoBench.Generators`) 作為分析器自動捆綁 - 無需額外引用。

```bash
dotnet add package PicoBench
```

## 快速開始

### 命令式 API

```csharp
using PicoBench;

var result = Benchmark.Run("My Benchmark", () =>
{
    Thread.SpinWait(100);
});

Console.WriteLine($"Average: {result.Statistics.Avg:F1} ns/op");
```

### 基於屬性的 API（源生成）

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

> 類 **必須** 是 `partial`。源生成器在編譯時生成 `IBenchmarkClass` 實現 - 無反射，完全 AOT 安全。

> 常見錯誤用法現在會得到源生成器診斷，例如非 `partial` 類、重複 baseline、非法生命週期方法簽名，以及不相容的 `[Params]` 值。 產生器還會拒絕 `async void` 基準方法（PBGEN011）以及泛型/巢狀/record/不可實例化的基準類別（PBGEN012-015），並對空 `[Params]`（PBGEN016）與基底型別上的基準屬性（PBGEN017）發出警告。

---

## 命令式 API 參考

### 基本基準測試

```csharp
using PicoBench;
using PicoBench.Formatters;

var result = Benchmark.Run("SpinWait", () => Thread.SpinWait(100));
Console.WriteLine(new ConsoleFormatter().Format(result));
```

### 帶狀態的基準測試（避免閉包）

```csharp
var data = new byte[1024];
var result = Benchmark.Run("ArrayCopy", data, static d =>
{
    var copy = new byte[d.Length];
    Buffer.BlockCopy(d, 0, copy, 0, d.Length);
});
```

### 作用域基準測試（DI 友好）

```csharp
var result = Benchmark.RunScoped("DbQuery",
    () => new MyDbContext(),
    static ctx => ctx.Users.FirstOrDefault()
);
// 每個樣本創建新作用域；每個樣本後銷毀作用域。
```

也提供非同步變體:

```csharp
var result = await Benchmark.RunScopedAsync("DbQueryAsync",
    () => new MyDbContext(),
    static async ctx => await ctx.Users.FirstOrDefaultAsync()
);
// 非同步變體：每個樣本建立新作用域，每個樣本後銷毀。
```

### 比較兩種實現

```csharp
var comparison = Benchmark.Compare(
    "String vs StringBuilder",
    "String Concat",  () => { var s = ""; for (int i = 0; i < 100; i++) s += "a"; },
    "StringBuilder",  () => { var sb = new StringBuilder(); for (int i = 0; i < 100; i++) sb.Append('a'); _ = sb.ToString(); }
);

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%)");
```

### 高級：獨立的熱身、設置和清理

```csharp
var result = Benchmark.Run(
    name:     "Custom",
    action:   () => DoWork(),
    warmup:   () => DoWork(),      // null 跳過熱身
    config:   BenchmarkConfig.Precise,
    setup:    () => PrepareState(), // 每個樣本前呼叫（不計時）
    teardown: () => CleanUp()       // 每個樣本後呼叫（不計時）
);
```

---

## 基於屬性的 API 參考

用 `[BenchmarkClass]` 裝飾一個 **partial** 類，並用下面的屬性裝飾其方法/屬性。源生成器在編譯時生成所有連接代碼。

### 屬性

| 屬性 | 目標 | 描述 |
|-----------|--------|-------------|
| `[BenchmarkClass]` | 類 | 標記類進行代碼生成。可選的 `Description` 屬性。 |
| `[Benchmark]` | 方法 | 將無參數方法標記為基準測試。設置 `Baseline = true` 作為參考方法。可選的 `Description`。 |
| `[Params(values)]` | 屬性 / 欄位 | 迭代給定的編譯時常數值。多個 `[Params]` 屬性產生笛卡爾積。 |
| `[GlobalSetup]` | 方法 | 每個參數組合**呼叫一次**，在基準測試運行之前。 |
| `[GlobalCleanup]` | 方法 | 每個參數組合**呼叫一次**，在基準測試運行之後。 |
| `[IterationSetup]` | 方法 | 每個樣本**前呼叫**（不計時）。 |
| `[IterationCleanup]` | 方法 | 每個樣本**後呼叫**（不計時）。 |

`[Benchmark]` 與生命週期方法必須是實例、非泛型、無參數方法，可返回 `void`、`Task`、`ValueTask`、`Task<T>` 或 `ValueTask<T>`；非同步方法會被 await，返回值被捨棄。`[Benchmark]` 方法不得為 `async void`（PBGEN011）。`[Params]` 目標必須是可寫實例屬性或非唯讀實例欄位。基準類必須是非泛型、非巢狀、非抽象，並聲明公共無參建構子。

### 完整示例

```csharp
using PicoBench;

[BenchmarkClass(Description = "比較字符串連接策略")]
public partial class StringBenchmarks
{
    [Params(10, 100, 1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() { /* 為當前 N 準備數據 */ }

    [GlobalCleanup]
    public void Cleanup() { /* 釋放資源 */ }

    [IterationSetup]
    public void BeforeSample() { /* 每個樣本準備 */ }

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

### 運行

```csharp
// 內部創建實例：
var suite = BenchmarkRunner.Run<StringBenchmarks>(BenchmarkConfig.Quick);

// 或使用預配置的實例：
var instance = new StringBenchmarks();
var suite2 = BenchmarkRunner.Run(instance, BenchmarkConfig.Quick);
```

## 非同步基準測試

兩種 API 都支援非同步工作。非同步基準方法按迭代 await；同一類中可混用同步與非同步生命週期方法。

```csharp
var result = await Benchmark.RunAsync("Http call", async () =>
{
    using var response = await httpClient.GetAsync(url);
});

var scoped = await Benchmark.RunScopedAsync("Scoped",
    () => container.CreateScope(),
    async scope => await scope.Service.DoWorkAsync());
```

- **計時模式** —— `AsyncTimingMode.WallClock`（預設）統計包含 await 掛起的完整時長；`AsyncTimingMode.CpuOnly` 統計 `Process.TotalProcessorTime`，排除 I/O 等待。CPU 時鐘以作業系統定時器刻度推進（通常 10–16 ms），因此該模式下自動校準會把最小採樣預算提升到實測時鐘粒度。CpuOnly 模式不收集 GC 資料。
- **GC 歸因** —— 非同步 GC 計數標記為近似值（`GcInfo.IsApproximate`）。兩種模式都會排除 setup/teardown 的配置。
- **CPU 周期** —— 非同步基準使用進程級計數器（線程級計數器無法跨線程切換相減）。
- **取消** —— `BenchmarkConfig.CancellationToken` 僅在非同步基準的樣本邊界檢查；同步基準忽略它。
- **混合類** —— 即使類中存在其他非同步成員，同步 `[Benchmark]` 方法仍走直接同步測量路徑。
- **預熱** —— 預熱迭代只呼叫預熱委托；`setup`/`teardown` 與 `[IterationSetup]`/`[IterationCleanup]` 在預熱期間不會執行。預熱動作需自給自足。

---

## 測量保真度

PicoBench 採用進程內執行，CLR 設定會影響絕對值：

- 關閉分層 JIT：`DOTNET_TieredCompilation=0`、`DOTNET_TieredPGO=0`（或 `COMPlus_*`）。
- 單線程微基準建議使用工作站 GC：`DOTNET_gcServer=0`。
- 僅當作業系統允許非特權計數器時才可取得 CPU 周期（Windows `QueryThreadCycleTime`/`QueryProcessCycleTime`；Linux 在 `perf_event_paranoid` ≤ 2 時使用 `perf_event`；macOS 提供單調代理而非真實周期）。`EnvironmentInfo` 與所有格式化器都會報告所用來源。
- `BenchmarkConfig.BoostPriorities`（預設 `true`）僅在執行期間提升進程與線程優先級，結束後恢復原值。

---

---

## 配置

### 預設

| 預設 | 熱身迭代 | 樣本數 | 基礎每樣本迭代數 | 自動校準 | 使用場景 |
|--------|--------|---------|------------------|----------|----------|
| `Quick` | 100 | 10 | 1,000 | 是 | 快速迭代 / CI |
| `Default` | 1,000 | 100 | 10,000 | 否 | 通用基準測試 |
| `Precise` | 5,000 | 200 | 50,000 | 是 | 最終測量 |

### 自訂配置

```csharp
var config = new BenchmarkConfig
{
    WarmupIterations    = 500,
    SampleCount         = 50,
    IterationsPerSample = 5000,
    RetainSamples       = true,  // 保留原始 TimingSample 數據
    AutoCalibrateIterations = true,
    MinSampleTime       = TimeSpan.FromMilliseconds(0.5),
    MaxAutoIterationsPerSample = 1_000_000,
    ForceGcBeforeBenchmark = true,  // false skips the pre-benchmark full GC
};

var result = Benchmark.Run("Test", action, config);
```

啟用自動校準後，PicoBench 會自動增加 `IterationsPerSample`，直到達到最小樣本耗時預算，或命中 `MaxAutoIterationsPerSample` 上限。這對極快操作特別有用，可降低計時噪聲影響。 將 `ForceGcBeforeBenchmark` 設為 `false` 可跳過取樣階段之前的強制完整 GC——在執行大量基準與參數組合時很有用。

---

## 輸出格式化器

五個內置格式化器實現 `IFormatter`：

```csharp
using PicoBench.Formatters;

var console  = new ConsoleFormatter();     // 盒繪製控制台表格
var markdown = new MarkdownFormatter();    // GitHub 友好的 Markdown
var html     = new HtmlFormatter();        // 樣式化的 HTML 報告
var csv      = new CsvFormatter();         // CSV 用於數據分析

// 比較摘要的靜態輔助方法：
Console.WriteLine(SummaryFormatter.Format(suite.Comparisons));
```

控制台、Markdown、HTML 和 CSV 輸出會包含更偏精度分析的元資料，例如標準誤、相對標準差，以及 CPU 計數器語義說明。

### 格式化目標

```csharp
formatter.Format(result);               // 單個 BenchmarkResult
formatter.Format(results);              // IEnumerable<BenchmarkResult>
formatter.Format(comparison);           // 單個 ComparisonResult
formatter.Format(comparisons);          // IEnumerable<ComparisonResult>
formatter.Format(suite);                // 完整的 BenchmarkSuite
```

### 格式化器選項

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
// 也可用：FormatterOptions.Default, .Compact, .Minimal
```

### 保存結果

```csharp
var dir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(dir);

File.WriteAllText(Path.Combine(dir, "results.md"),   new MarkdownFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.html"), new HtmlFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.csv"),  new CsvFormatter().Format(suite));
```

---

## 結果模型

| 類型 | 描述 |
|------|-------------|
| `BenchmarkResult` | 名稱、類別、標籤、統計、樣本、每次樣本迭代數、樣本數、時間戳 |
| `ComparisonResult` | 名稱、類別、標籤、基準、候選、加速比、是否更快、改進百分比 |
| `BenchmarkSuite` | 名稱、描述、結果、比較、環境、持續時間、時間戳 |
| `Statistics` | 平均值、P50、P90、P95、P99、最小值、最大值、標準差、標準誤、相對標準差百分比、每操作 CPU 週期、GC 信息 |
| `TimingSample` | 奈秒耗時、毫秒耗時、計時器滴答數、CPU 週期、GC 信息 |
| `GcInfo` | Gen0、Gen1、Gen2、總計、是否為零 |
| `EnvironmentInfo` | 操作系統、架構、執行時版本、處理器數量、執行模式、配置、CPU 計數器類型 / 可用性 / 是否有意義、自訂標籤 |

---

## 架構

```
src/
+-- PicoBench/                        # 主庫 (netstandard2.0)
|   +-- Benchmark.cs                   # 命令式 API (Run, Compare, RunScoped)
|   +-- BenchmarkRunner.cs             # 基於屬性的入口點 (Run<T>)
|   +-- BenchmarkConfig.cs             # 配置和預設
|   +-- Attributes.cs                  # 7 個基準測試屬性
|   +-- IBenchmarkClass.cs             # 生成器實現的接口
|   +-- Runner.cs                      # 底層計時流程與樣本建立
|   +-- Runner.Gc.cs                   # GC 基線與增量追蹤
|   +-- Runner.Cpu.cs                  # 平台相關 CPU 計數器實作
|   +-- StatisticsCalculator.cs        # 百分位數 / 統計計算
|   +-- Models.cs                      # 結果類型
|   +-- Formatters/
|       +-- IFormatter.cs              # IFormatter, FormatterOptions & FormatterBase
|       +-- ConsoleFormatter.cs        # 盒繪製控制台表格
|       +-- MarkdownFormatter.cs       # GitHub Markdown 表格
|       +-- HtmlFormatter.cs           # 樣式化的 HTML 報告
|       +-- CsvFormatter.cs            # CSV 導出
|       +-- SummaryFormatter.cs        # 勝/負摘要
|
+-- PicoBench.Generators/            # 源生成器 (netstandard2.0)
    +-- BenchmarkGenerator.cs          # IIncrementalGenerator 入口點
    +-- BenchmarkClassAnalyzer.cs      # Roslyn 分析與診斷
    +-- CSharpLiteralFormatter.cs      # 為生成代碼格式化 C# 字面量
    +-- DiagnosticDescriptors.cs       # 生成器診斷定義
    +-- Emitter.cs                     # C# 代碼發射器 (AOT 安全)
    +-- Models.cs                      # Roslyn 分析模型
```

---

## 平台特定功能

| 功能 | Windows | Linux | macOS |
|---------|---------|-------|-------|
| 高精度計時 | Stopwatch | Stopwatch | Stopwatch |
| GC 追蹤 (Gen0/1/2) | 是 | 是 | 是 |
| CPU 週期計數 | `QueryThreadCycleTime` | `perf_event_open` | `mach_absolute_time`（代理值） |
| 進程優先級提升 | 是 | 是 | 是 |

在 macOS 上，導出的 CPU 計數器是高精度單調時鐘代理值，而不是真正的架構 CPU 週期。`EnvironmentInfo` 和格式化輸出會明確區分這一點。

---

## 示例

| 示例 | API 風格 | 描述 |
|--------|-----------|-------------|
| `StringVsStringBuilder` | 命令式 | 比較 `string +=`、`StringBuilder` 和帶容量的 `StringBuilder` |
| `AttributeBased` | 基於屬性 | 使用 `[Benchmark]`、`[Params]` 和源生成器的相同比較 |
| `CollectionBenchmarks` | 基於屬性 | List vs Dictionary vs HashSet 查找 - 展示所有屬性 |

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
dotnet run --project samples/AttributeBased -c Release
dotnet run --project samples/CollectionBenchmarks -c Release
```

---

## 與 BenchmarkDotNet 比較

| 功能 | PicoBench | BenchmarkDotNet |
|---------|-----------|----------------|
| 依賴 | 0 | 多 |
| 套件大小 | 小 | 大 |
| 目標框架 | netstandard2.0 | net6.0+ |
| AOT 支援 | 源生成器 | 基於反射 |
| 屬性 API | `[Benchmark]`、`[Params]` | `[Benchmark]`、`[Params]` |
| 設置時間 | 即時 | 秒級 |
| 輸出格式 | 5 | 10+ |
| 統計深度 | 良好 | 廣泛 |
| 使用場景 | 快速 A/B 測試、CI、AOT 應用 | 詳細分析、出版物 |

---

## 許可證

MIT 許可證 - 詳情見 [LICENSE](LICENSE) 文件。

## 建置與發佈

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

發佈遵循 PicoHex 版本規則 `<year>.<x>.<y>`：公開 API 變化時 **x** 加一，未變化時 **y** 加一。判定依據倉庫內的 API 基線 `api/PicoBench.public.txt`，它始終保存上一個發佈版本的公開表面。

```bash
# 查看 API 差異與下一個版本
pwsh ./scripts/release.ps1 -DryRun

# 發佈：刷新 api/、提交 chore(release): v<version>、打 tag、push
pwsh ./scripts/release.ps1 -Push
```

推送 tag 後，GitHub Actions 發佈流水線會執行測試、按 tag 版本打包並發佈到 [NuGet.org](https://www.nuget.org/packages/PicoBench)。為彌補兄弟倉庫的索引空窗，可將同一版本打包到 `NuGet.config` 宣告的本地來源：

```bash
dotnet pack src/PicoBench/PicoBench.csproj -c Release -o artifacts/nupkg -p:Version=<version>
```

## 貢獻

1. Fork 倉庫
2. 創建特性分支
3. 進行更改並附帶測試
4. 提交拉取請求
