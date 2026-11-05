# PicoBench

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

一個輕量級的 .NET 基準測試庫，提供 **兩種互補的 API**：命令式 API 和基於屬性、源生成的 API，完全 **AOT 相容**。除了一個 BCL polyfill（`System.Threading.Tasks.Extensions`，用於在 netstandard2.0 補回 `ValueTask<T>`）以外沒有任何 NuGet 引用。

## 特性

- **無第三方依賴** —— 只有 1 個 BCL polyfill（`System.Threading.Tasks.Extensions`），用於在 netstandard2.0 補回 `ValueTask<T>`；沒有其他外部套件
- **兩種 API** - 命令式 (`Benchmark.Run`) 用於臨時測試；基於屬性 (`[Benchmark]` + 源生成器) 用於結構化測試套件
- **AOT 相容的源生成器** - 增量生成器在執行時生成直接方法呼叫，零反射
- **跨平台** - 完全支援 Windows、Linux 和 macOS
- **亞微秒計時** - 基於 `Stopwatch` 的每操作計時，並實測並扣除測試框架自身的每操作開銷。低於本次執行解析度上限的工作負載僅以其上界回報（`<limit`）並標記 `ResolutionLimited` —— 絕不給出奈秒級精確的數值
- **顯式揭露的極限** - `OverheadNs`、`ResolutionLimitNanoseconds`、`ResolutionLimited` 與 `Unmeasured` 說明了本次執行能夠解析與無法解析什麼，因此雜訊級的結果不會被當作快速操作來讀
- **GC 追蹤** - 監控基準測試期間的 Gen0/Gen1/Gen2 回收計數（不含 setup/teardown）
- **CPU 週期計數** - 同步路徑使用執行緒級週期計數（Windows `QueryThreadCycleTime`；Linux 在作業系統允許非特權存取時使用 `perf_event`）。非同步基準讀取進程級計數器，因為執行緒級計數器無法跨執行緒切換追蹤，因此標記為 `CpuCyclesAreApproximate`（四種格式中均帶 `~`）。macOS 沒有週期計數器：`mach_absolute_time` 是單調的*時間*代理，而不是週期，同樣會被標記
- **統計分析** - `Avg`、`P50`、`P90`、`P95`、`P99`、`Min`、`Max`、`StdDev`、`StandardError`（相對標準差 `RSD`）。分位數採用最近兩個樣本之間的線性插值（統計軟體稱之為 *type 7*，即 Excel `PERCENTILE.INC` 與 NumPy 的預設口徑），變異數為樣本變異數（除以 `N-1`）；樣本數少於約 20 時 P99 接近最大值，因此尾部分位數並不代表穩定的尾部。
- **多種輸出格式** - 4 個內建 `IFormatter` 格式化器（Console、Markdown、HTML、CSV）以及程式化摘要輸出
- **參數化基準測試** - `[Params]` 屬性，支援自動笛卡爾積迭代
- **比較支援** - 基準與候選在交錯輪次中測量；結論取每輪配對比值的中位數，並且必須同時滿足 5% 死區與排除持平的分佈無關區間，因此輪次間的雜訊不會被當作效能提升發佈
- **可配置** - Quick、Default 和 Precise 預設、自動校準或完全自訂配置
- **netstandard2.0** - 相容 .NET Framework **4.7.2+**（`RuntimeInformation` 被無條件使用，因此不支援更早的 .NET Framework 版本）、.NET Core 2.0+、.NET 5+

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

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%) [{(comparison.Interval?.ToString() ?? "no interval")}] -> {comparison.Outcome}");
```

`Benchmark.Compare(name, baselineAction, candidateAction)` **不會**讓每條通道只執行一次。它會在 10 輪中交錯執行兩條通道（每輪交替哪條通道先跑，因此任何一方都不會繼承更熱的進程），並回報**每輪配對比值的中位數**。這意味著：

- **`Speedup`** —— 基準耗時 ÷ 候選耗時；`> 1` 表示候選更快。候選未測到任何耗時的輪次不產生比值；當沒有任何輪次給出可用比值時，比較會回報 `NaN`（`ImprovementPercent` 也隨之為 `NaN`），而不是憑空編造結論。由兩個獨立的 `BenchmarkResult` 建構的 `ComparisonResult` 仍然回報兩個平均值的比值。 0/0 的輪次正是這種情況：得到 `NaN` 而不是 `1.0`（由兩個全零的獨立結果建構的比較仍沿用舊的 `1.0`）。
- **`Interval`** —— 該中位數的分佈無關 95% 區間（10 個配對比值的第 2/第 9 順序統計量）。`Interval.Contains(1.0)` 表示持平仍然可信。由兩個獨立結果建構的比較**沒有區間**（`Interval` 為 `null`）—— 沒有可配對的交錯輪次，因此無法描述離散程度。
- **`Outcome`** —— 結論。`Faster` / `Slower` 要求比值離開 **5% 死區** *並且*（存在區間時）區間排除 1。落在其中即為 `Inconclusive`；無法計算的比值（非有限值或非正值）為 `Unknown`，可用配對輪次少於六輪的比較也是如此（低於該數量，區間無法以 95% 信心覆蓋中位數）。
- **`IsFaster`** —— 僅當 `Outcome == Faster` 時為 `true`，即**比以往更窄**：3% 的差異現在回傳 `false`（`Inconclusive`），而沒有輪次的比較也必須越過同樣的 5% 死區。用 `Outcome` 取得三態答案，用 `IsFasterKnown` 區分「無可測差異」與「根本無法測量」。

成本：直接比較會讓每條通道執行 10 次，即 **20 次 `Run`** —— 如果太慢，A/B 執行可降低 `SampleCount` / `WarmupIterations`。

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
// 命令式
var result = await Benchmark.RunAsync("Http call", async () =>
{
    using var response = await httpClient.GetAsync(url);
});

// 逐樣本非同步 setup/teardown
var result2 = await Benchmark.RunAsync(
    "With lifecycle",
    action: async () => await DoWorkAsync(),
    warmup: async () => await DoWorkAsync(),
    config: BenchmarkConfig.Quick,
    setup: async () => await ResetStateAsync(),
    teardown: async () => await DisposeStateAsync());

// 帶狀態的非同步（無閉包配置；沒有 setup/teardown 多載）
var result3 = await Benchmark.RunAsync("Stateful", state, async s => await ProcessAsync(s));

// 非同步作用域（對 DI 友善）
var result4 = await Benchmark.RunScopedAsync("Scoped",
    () => container.CreateScope(),
    async scope => await scope.Service.DoWorkAsync());
```

屬性式類別可以宣告非同步基準與非同步生命週期方法：

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

- **計時模式** —— `AsyncTimingMode.WallClock`（預設）統計包含 await 掛起的完整時長；`AsyncTimingMode.CpuOnly` 統計 `Process.TotalProcessorTime`，排除 I/O 等待。CPU 時鐘以作業系統定時器刻度推進（通常 10–16 ms），因此該模式下自動校準會把最小採樣預算提升到實測時鐘粒度。CpuOnly 模式不收集 GC 資料。
- **不可測樣本** —— 在 `CpuOnly` 模式下，短於一個作業系統定時器刻度的工作負載會被讀作恰好為零的 CPU 時間。此類樣本會計入 `Statistics.UnmeasuredSampleCount`（`Unmeasured` 為 `true`），其每操作值變成**一個計時量子 —— 一個上界 —— 而不是 `0.000`**，並且各格式化器會把每操作儲存格列印為 `N/A`。此類執行得到的統計不是對該工作的測量：應將其讀作「低於 CPU 時鐘的解析度」。迭代次數**不會**被默默放大來掩蓋問題 —— 只有 `AutoCalibrateIterations = true`（`Quick`/`Precise` 預設）才會校準；`Default` 原樣使用配置的 `IterationsPerSample`。
- **GC 歸因** —— 非同步 GC 計數標記為近似值（`GcInfo.IsApproximate`）。兩種模式都會排除 setup/teardown 的配置。
- **CPU 周期** —— 同步路徑讀取執行緒級計數器（Windows 上為 `QueryThreadCycleTime`，Linux 上為針對測量執行緒開啟的 `perf_event`）。非同步基準讀取進程級計數器，因為執行緒級計數器無法跨執行緒切換追蹤：該數值包含其他執行緒完成的工作，並被標記為 `Statistics.CpuCyclesAreApproximate`，在 Console、Markdown、HTML 和 CSV 中渲染為 `~`。macOS 沒有週期計數器 —— `mach_absolute_time` 是單調的*時間*代理而非架構週期，同樣會被標記。
- **取消** —— `BenchmarkConfig.CancellationToken` 僅在非同步基準的樣本邊界檢查；同步基準忽略它。
- **混合類** —— 即使類中存在其他非同步成員，同步 `[Benchmark]` 方法仍走直接同步測量路徑。
- **預熱** —— 預熱迭代只呼叫預熱委托；`setup`/`teardown` 與 `[IterationSetup]`/`[IterationCleanup]` 在預熱期間不會執行。預熱動作需自給自足。

---

## 測量保真度

PicoBench 採用進程內執行，CLR 設定會影響絕對值：

- 關閉分層 JIT：`DOTNET_TieredCompilation=0`、`DOTNET_TieredPGO=0`（或 `COMPlus_*`）。
- 單線程微基準建議使用工作站 GC：`DOTNET_gcServer=0`。
- 僅當作業系統允許非特權計數器時才可取得 CPU 週期計數（Windows `QueryThreadCycleTime`；Linux 在 `perf_event_paranoid` 為 2 或更低時使用 `perf_event`）。Windows 的同步執行是執行緒級的；非同步執行與 macOS 使用進程級計數器，或在 macOS 上使用單調時鐘代理而非真實週期 —— 兩者都會被標記 `CpuCyclesAreApproximate`。`EnvironmentInfo` 與每個格式化器都會報告所用來源。
- `BenchmarkConfig.BoostPriorities`（預設 `true`）僅在執行期間提升進程與線程優先級，結束後恢復原值。

### 這些數字能解析什麼、不能解析什麼

每次執行都會用相同的迭代次數實測並扣除測試框架自身每操作附加的開銷，然後公佈它仍能解析的最小值：

| 欄位 | 涵義 |
|---|---|
| `Statistics.OverheadNs` | 測試框架自身的每操作開銷（委派分發、迴圈簿記、時鐘與計數器讀取）中已實測並扣除的部分。非同步執行為 `0`，其開銷不做扣除 |
| `Statistics.ResolutionLimitNanoseconds` | 本次執行能解析的最小每操作取值：一個時鐘刻度分攤到各次迭代、所扣除開銷的逐次執行離散程度、以及該開銷的每操作規模（其上界），取三者中最大者 |
| `Statistics.ResolutionLimited` | `Avg` 等於或低於該上限，或至少有一個樣本完全無法測量。格式化器會把每操作儲存格列印為上界（四種格式均輸出 `<limit`；Console 另加 `[below resolution]` 標記，Markdown 與 HTML 加 `(below resolution)`），而不是一個數值 |
| `Statistics.Unmeasured` / `UnmeasuredSampleCount` | 計時源無法推進的樣本數（`CpuOnly` 的零刻度）。這些儲存格列印 `N/A`，絕不列印 `0.000` |

因此，對亞奈秒結果的誠實解讀是 **「低於 `<limit` ns」**，而不是「0.02 ns」：該值並未被測量，而是被界定，並且該界定涵蓋時鐘量化與所扣除開銷的離散程度（取時鐘刻度、所扣除開銷的離散程度與該開銷每操作規模三者中的最大者；由於殘差被鉗制在零，每個樣本都滿足真實值 ≤ 殘差 + 開銷 —— 因此該上界不會再低於同一次執行所扣除的下限）。還有兩點不對稱值得記住：測試框架開銷扣除僅對同步基準實作（非同步的每操作取值仍攜帶其每樣本包裝開銷），而 GC 位元組完全未被測量 —— 只有回收計數，且按進程，在非同步路徑上是近似的。

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
    ForceGcBeforeBenchmark = true,  // false 表示跳過基準前的強制完整 GC
    BoostPriorities     = true,   // 僅在本次執行期間提升進程/執行緒優先級，結束後還原
    TimingMode          = AsyncTimingMode.WallClock, // 或 CpuOnly（僅非同步）
    CancellationToken   = default,                   // 僅非同步；同步基準會忽略它
};

var result = Benchmark.Run("Test", action, config);
```

啟用自動校準後，PicoBench 會自動增加 `IterationsPerSample`，直到達到最小樣本耗時預算，或命中 `MaxAutoIterationsPerSample` 上限。這對極快操作特別有用，可降低計時噪聲影響。 將 `ForceGcBeforeBenchmark` 設為 `false` 可跳過取樣階段之前的強制完整 GC——在執行大量基準與參數組合時很有用。自動校準是**選擇性開啟**的（`AutoCalibrateIterations` 預設為 `false`，`Default` 預設也不會改動所配置的迭代數）：PicoBench 從不悄悄放大迭代數來讓結果看起來可解析 —— 在 `CpuOnly` 模式下，抬高後的預算就是實測的 CPU 時鐘粒度，因為更短的值一律被讀作零。

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

四種格式化器都會展示同一組每操作指標 —— `Avg`、`P50`、`P90`、`P95`、`P99`、`StandardError` 與相對標準差（`RSD (%)` 欄）—— 以及可用時的 CPU 計數器說明。Console 的套件表同樣帶有 `StdErr` 與 `RSD` 兩欄（先前缺漏，雜訊訊號在預設輸出裡根本看不到）。Console、Markdown 與 HTML 對同一份資料列印同一個表格單位，而 CSV 始終以奈秒給出（見下文）。

### 精度檔案：raw 與 display

同一個數值承擔兩項職責，格式化器把它們分開，而不是各自取一個折衷：一個取值不是 **raw** —— 按實測原樣的奈秒數 —— 就是 **display** —— 縮放到適合該表格的單位後的同一個值。

- **Raw** (`MetricPrecision.Raw`): 裸露的奈秒數，並且欄標題（或 CSV 欄名 `Avg_ns`、`StdErr_ns` 等）寫明 `ns`。無論資料更適合哪個單位，`CsvFormatter` 永遠寫這一檔 —— 單位隨資料量級變化的資料檔案不是機器可讀的。
- **Display** (`MetricPrecision.Display`): 整張表為其所有每操作欄選**一個**單位 —— 小於 1 000 ns 用 `ns`，小於 1 000 µs 用 `µs`，小於 1 000 ms 用 `ms`，更大用秒 —— 並把該單位寫進欄標題（`Avg (µs)`）。一個表一個單位、而不是一格一個單位，欄才繼續可比：5 秒的基準在 `Avg (s)` 下讀作 `5.0`，同一標題下 150 ns 的鄰居讀作 `0.0`，而不是被悄悄換算了單位。

```csharp
var raw = new FormatterOptions { PrecisionProfile = MetricPrecision.Raw };
Console.WriteLine(new ConsoleFormatter(raw).Format(suite)); // 在 "Avg (ns)" 下輸出 5000000000.0
```

Console、Markdown 與 HTML 顯示的文字完全相同（有測試逐字元斷言），而原始數字始終在 CSV 中，因此縮放後的顯示永遠不會成為某次測量的唯一副本。

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
    CandidateLabel        = "New",
    PrecisionProfile      = MetricPrecision.Display, // 或 .Raw；CSV 恆寫 raw
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

### CSV 欄契約

`CsvFormatter` 先寫一行表頭，再寫資料列；套件會額外寫入 `#` 註解行（`# Schema:` 契約行；`# key,value` 中繼資料列：`Suite`、`Description`、`Environment`、`CpuCounter`、`CpuCounterNote`、`GcInfoNote`、`Timestamp`、`Duration`、`NoiseNote`）以及兩個區段 `# Results` 與 `# Comparisons`，**每個區段都有自己的表頭行**。因此一個檔案中存在不止一行表頭 —— `pandas.read_csv(path, comment='#')` 會跳過註解行，但兩個區段需要你自己拆分。結果檔案的首行就是這條 `# Schema: v2; …` 契約行，任何以 `#` 開頭的行都是註解，解析器必須跳過。

中繼資料列永遠不會延續到下一個實體行：值中的換行會寫成兩個字元 `\n`（CRLF、CR、LF 同等處理），需要時再依 CSV 規則加引號。這正是 `comment='#'` 對跨行描述也安全的原因。

結果欄，按以下順序（預設選項；`IncludePercentiles`、`IncludeGcInfo`、`IncludeCpuCycles` 與 `IncludeTimestamp` 為 `false` 時會移除各自的欄）：

| # | 欄位 | 涵義 |
|---|---|---|
| 1 | `Name` | 基準名稱（包含 `,` `"` 或換行時加引號；開頭的 `=`/`+`/`-`/`@` 會加上 `'` 前綴） |
| 2 | `Category` | 類別，未設定時為空 |
| 3 | `Avg_ns` | 每操作奈秒數 —— 有三種儲存格狀態，見下文 |
| 4 | `P50_ns` | 每操作奈秒數中位數（同樣三種狀態） |
| 5-12 | `P90_ns`, `P95_ns`, `P99_ns`, `Min_ns`, `Max_ns`, `StdDev_ns`, `StdErr_ns`, `RSD_pct` | 百分位數與離散程度；`RSD_pct` 是普通百分比，其餘七個使用每操作儲存格狀態 |
| 13 | `CpuCycles` | 每操作週期數；`~` 前綴表示進程級計數器或時鐘代理（`CpuCyclesAreApproximate`），而非執行緒週期 |
| 14-16 | `GC0`, `GC1`, `GC2` | 回收計數，未收集 GC 時為 `N/A` |
| 17 | `GCApproximate` | 計數器為近似值時是 `1`（非同步路徑），精確時為 `0`，未收集時為 `N/A` |
| 18 | `SampleCount` | 樣本數（整數） |
| 19 | `IterationsPerSample` | 每樣本迭代數（整數） |
| 20 | `Timestamp` | ISO-8601 往返格式（`o`，固定不變文化） |

每操作欄的儲存格狀態：

| 儲存格 | 涵義 |
|---|---|
| `12.5` | 以奈秒為單位的實測值（固定不變文化，往返格式） |
| `<0.02` | **上界**：本次執行無法解析高於 `Statistics.ResolutionLimitNanoseconds` 的取值。`<` 之後的數字就是該上限，它不是實測值 |
| `N/A` | 未測量（`Statistics.Unmeasured`，時鐘增量為零）或未收集（GC 欄） |

對使用者而言有兩點後果：由於 `<0.02` 的存在，每操作欄**不保證是數值型**，因此 pandas 之類的讀取器會推斷為 `object`，你必須顯式處理上界列；而 `GCApproximate` 被插入到第 17 位，位於 `GC2` 與 `SampleCount` 之間，所以**按位置讀取的程式碼必須把 `SampleCount`/`IterationsPerSample` 後移一位**（按欄名讀取不受影響）。

比較欄：`TestCase`, `Provider`, `Avg_ns`, `Speedup`, `P50_ns`, `P90_ns`, `P99_ns`, `StdErr_ns`, `RSD_pct`, `CpuCycles`, `GC0`, `GC1`, `GC2`, `GCApproximate`, `SampleCount`, `IterationsPerSample`。每個比較會寫出兩列 —— 候選在前，基準在後 —— 並且 `Speedup` 只填在候選列。比較的每操作儲存格與結果表走同一個格式化器，因此 `<limit` 與 `N/A` 也會出現在那裡，`CpuCycles` 帶同樣的 `~` 標記（`CpuCyclesAreApproximate`）。

`# Schema:` 行已經落地，並且就是**檔案首行**：結果檔案（以及含結果的套件檔案）以這段描述後續欄位的契約行開頭，比較區段則在 `# Comparisons` 之前再寫一條自己的契約行：

```
# Schema: v2; section=results; columns=Name|Category|Avg_ns|P50_ns|P90_ns|P95_ns|P99_ns|Min_ns|Max_ns|StdDev_ns|StdErr_ns|RSD_pct|CpuCycles|GC0|GC1|GC2|GCApproximate|SampleCount|IterationsPerSample|Timestamp; per-op cells (Avg_ns,P50_ns,P90_ns,P95_ns,P99_ns,Min_ns,Max_ns,StdDev_ns,StdErr_ns) = number (nanoseconds) | "<limit" (upper bound: the value is at the harness floor) | "N/A" (not measured); RSD_pct = percent; SampleCount = samples taken | IterationsPerSample = iterations per sample; CpuCycles = count | "~count" (not a thread-scoped cycle count); GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected)
# Schema: v2; section=comparisons; columns=TestCase|Provider|Avg_ns|Speedup|P50_ns|P90_ns|P99_ns|StdErr_ns|RSD_pct|CpuCycles|GC0|GC1|GC2|GCApproximate|SampleCount|IterationsPerSample; per-op cells (Avg_ns,P50_ns,P90_ns,P99_ns,StdErr_ns) = number (nanoseconds) | "<limit" (upper bound: the value is at the harness floor) | "N/A" (not measured); RSD_pct = percent; Speedup = ratio (baseline avg / candidate avg) | "N/A" (no finite ratio: 0/0 or too few paired rounds); SampleCount = samples taken | IterationsPerSample = iterations per sample; CpuCycles = count | "~count" (not a thread-scoped cycle count); GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected)
```

`v2` 即 Batch 0 的形態 —— 上文的每操作三態加上 `GCApproximate`。`columns=` 清單由實際寫出的表頭行推導，因此不會與上面的表偏離，也不會列出被選項關掉的欄；`section=` 用來區分兩條契約行。解析器必須跳過所有以 `#` 開頭的行，並從 `columns=` 清單讀取欄序，而不是硬編碼。

---

## 結果模型

| 類型 | 描述 |
|------|-------------|
| `BenchmarkResult` | 名稱、類別、標籤、統計、樣本、每次樣本迭代數、樣本數、時間戳 |
| `ComparisonResult` | 名稱、類別、標籤、基準、候選、加速比、改進百分比、**Outcome**、**IsFasterKnown**、**Interval**、是否更快 |
| `BenchmarkSuite` | 名稱、描述、結果、比較、環境、持續時間、時間戳 |
| `ComparisonOutcome` | 列舉：`Unknown`（無法計算任何比值）、`Inconclusive`（落在 5% 死區內，或區間仍包含持平）、`Faster`、`Slower` |
| `ComparisonInterval` | Low, High, Contains(value), ToString() —— 中位配對比值的分佈無關 95% 區間 |
| `Statistics` | `Avg`、`P50`、`P90`、`P95`、`P99`、`Min`、`Max`、`StdDev`、`StandardError`、`RelativeStdDevPercent`、`CpuCyclesPerOp`、**CpuCyclesAreApproximate**、**OverheadNs**、**ResolutionLimitNanoseconds**、**ResolutionLimited**、**Unmeasured**、**UnmeasuredSampleCount**、`GcInfo`、HasHighVariance |
| `TimingSample` | 奈秒耗時、毫秒耗時、計時器滴答數、CPU 週期、**CpuCyclesAreApproximate**、GC 信息 |
| `GcInfo` | Gen0、Gen1、Gen2、總計、是否為零、IsApproximate |
| `EnvironmentInfo` | 操作系統、架構、執行時版本、處理器數量、執行模式、配置、CPU 計數器類型 / 可用性 / 是否有意義、自訂標籤 |

測量保真度工作中新增成員的涵義：

| 成員 | 契約 |
|---|---|
| `ComparisonResult.Outcome` | 三態結論；`Faster`/`Slower` 需要 5% 死區**並且**一個排除 1 的區間 |
| `ComparisonResult.IsFasterKnown` | `Outcome != Unknown` —— 「兩條通道已被比較過」（它們仍可能無法區分） |
| `ComparisonResult.IsFaster` | 僅 `Outcome == Faster`。比舊的 `Speedup > 1.0` 更窄：死區內的差異不再算「更快」 |
| `ComparisonResult.Interval` | `ComparisonInterval?`；當比較由兩個獨立結果而非交錯輪次建構時為 `null` |
| `Statistics.ResolutionLimited` | 每操作取值是一個上界（`<limit`），不是一次測量 |
| `Statistics.UnmeasuredSampleCount` / `Unmeasured` | 計時源無法推進的樣本；它們的每操作儲存格為 `N/A` |
| `Statistics.CpuCyclesAreApproximate` | `CpuCyclesPerOp` 是進程級（非同步）或單調時鐘代理（macOS），而非執行緒週期：格式化器會加上 `~` 前綴 |
| `TimingSample.CpuCyclesAreApproximate` | 樣本級同一標記；`Statistics` 以邏輯或聚合它 |

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
| 計時 | Stopwatch | Stopwatch | Stopwatch |
| GC 追蹤 (Gen0/1/2) | 是 | 是 | 是 |
| CPU 週期計數（同步） | `QueryThreadCycleTime`（執行緒級） | 每執行緒的 `perf_event_open`（當 `perf_event_paranoid` ≤ 2 時） | `mach_absolute_time`，一個**時間代理** —— 標記為 `~` |
| CPU 週期計數（非同步） | `QueryProcessCycleTime`（進程級）—— 標記為 `~` | 進程級 `perf_event` —— 標記為 `~` | `mach_absolute_time` —— 標記為 `~` |
| 進程優先級提升 | 是 | 是 | 是 |

在 macOS 上，導出的 CPU 計數器是高解析度單調的*時間*代理，而不是架構週期計數：該值隨時間推進，而不是隨已執行的指令推進，因此被標記 `CpuCyclesAreApproximate` 並以 `~` 列印。非同步路徑使用同一標記，其進程級計數器包含其他執行緒完成的工作。`EnvironmentInfo` 與格式化器輸出都會明確揭露這兩種區別。

---

## 示例

| 示例 | API 風格 | 描述 |
|--------|-----------|-------------|
| `StringVsStringBuilder` | 命令式 | 比較 `string +=`、`StringBuilder` 和帶容量的 `StringBuilder` |
| `AttributeBased` | 基於屬性 | 使用 `[Benchmark]`、`[Params]` 和源生成器的相同比較 |
| `CollectionBenchmarks` | 基於屬性 | List vs Dictionary vs HashSet 查找 - 展示所有屬性 |
| `AsyncBenchmarks` | 屬性（非同步） | 非同步入口與混合生命週期：`BenchmarkRunner.RunAsync<T>()`、非同步 `[GlobalSetup]`/`[IterationSetup]`、回傳 `Task` 的 `[Benchmark]`，以及 `SummaryFormatter` |

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
dotnet run --project samples/AttributeBased -c Release
dotnet run --project samples/CollectionBenchmarks -c Release
dotnet run --project samples/AsyncBenchmarks -c Release
```

---

## 與 BenchmarkDotNet 比較

| 功能 | PicoBench | BenchmarkDotNet |
|---------|-----------|----------------|
| 依賴 | 1 個 BCL polyfill | 多個 |
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
