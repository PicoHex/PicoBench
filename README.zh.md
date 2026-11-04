# PicoBench

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoBench/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoBench.svg)](https://www.nuget.org/packages/PicoBench)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

一个轻量级、零依赖的 .NET 基准测试库，提供 **两种互补的 API**：命令式 API 和基于属性、源生成的 API，完全 **AOT 兼容**。

## 特性

- **零依赖** - 纯 .NET 实现，无需外部包
- **两种 API** - 命令式 (`Benchmark.Run`) 用于临时测试；基于属性 (`[Benchmark]` + 源生成器) 用于结构化测试套件
- **AOT 兼容的源生成器** - 增量生成器在运行时生成直接方法调用，零反射
- **跨平台** - 完全支持 Windows、Linux 和 macOS
- **亚微秒计时** - 基于 `Stopwatch` 的每操作计时，并实测并扣除测试框架自身的每操作开销。低于本次运行分辨率上限的工作负载仅以其上界报告（`<limit`）并标记 `ResolutionLimited` —— 绝不给出纳秒级精确的数值
- **显式披露的极限** - `OverheadNs`、`ResolutionLimitNanoseconds`、`ResolutionLimited` 与 `Unmeasured` 说明了本次运行能够分辨与无法分辨什么，因此噪声级的结果不会被当作快速操作来读
- **GC 跟踪** - 监控基准测试期间的 Gen0/Gen1/Gen2 回收计数（不含 setup/teardown）
- **CPU 周期计数** - 同步路径使用线程级周期计数（Windows `QueryThreadCycleTime`；Linux 在操作系统允许非特权访问时使用 `perf_event`）。异步基准读取进程级计数器，因为线程级计数器无法跨线程切换跟踪，因此标记为 `CpuCyclesAreApproximate`（四种格式中均带 `~`）。macOS 没有周期计数器：`mach_absolute_time` 是单调的*时间*代理，而不是周期，同样会被标记
- **统计分析** - 均值、中位数、P90、P95、P99、最小值、最大值、标准差、标准误和相对标准差
- **多种输出格式** - 4 个内置 `IFormatter` 格式化器（Console、Markdown、HTML、CSV）以及程序化摘要输出
- **参数化基准测试** - `[Params]` 属性，支持自动笛卡尔积迭代
- **比较支持** - 基准与候选在交错轮次中测量；结论取每轮配对比值的中位数，并且必须同时满足 5% 死区与排除持平的无分布区间，因此轮次间的噪声不会被当作性能提升发布
- **可配置** - Quick、Default 和 Precise 预设、自动校准或完全自定义配置
- **netstandard2.0** - 兼容 .NET Framework **4.7.2+**（`RuntimeInformation` 被无条件使用，因此不支持更早的 .NET Framework 版本）、.NET Core 2.0+、.NET 5+

## 安装

引用 **PicoBench** NuGet 包。源生成器 (`PicoBench.Generators`) 作为分析器自动捆绑 - 无需额外引用。

```bash
dotnet add package PicoBench
```

## 快速开始

### 命令式 API

```csharp
using PicoBench;

var result = Benchmark.Run("My Benchmark", () =>
{
    Thread.SpinWait(100);
});

Console.WriteLine($"Average: {result.Statistics.Avg:F1} ns/op");
```

### 基于属性的 API（源生成）

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

> 类 **必须** 是 `partial`。源生成器在编译时生成 `IBenchmarkClass` 实现 - 无反射，完全 AOT 安全。

> 常见错误用法现在会得到源生成器诊断，例如非 `partial` 类、重复 baseline、非法生命周期方法签名，以及不兼容的 `[Params]` 值。 生成器还会拒绝 `async void` 基准方法（PBGEN011）以及泛型/嵌套/record/不可实例化的基准类（PBGEN012-015），并对空 `[Params]`（PBGEN016）和基类型上的基准属性（PBGEN017）发出警告。

---

## 命令式 API 参考

### 基本基准测试

```csharp
using PicoBench;
using PicoBench.Formatters;

var result = Benchmark.Run("SpinWait", () => Thread.SpinWait(100));
Console.WriteLine(new ConsoleFormatter().Format(result));
```

### 带状态的基准测试（避免闭包）

```csharp
var data = new byte[1024];
var result = Benchmark.Run("ArrayCopy", data, static d =>
{
    var copy = new byte[d.Length];
    Buffer.BlockCopy(d, 0, copy, 0, d.Length);
});
```

### 作用域基准测试（DI 友好）

```csharp
var result = Benchmark.RunScoped("DbQuery",
    () => new MyDbContext(),
    static ctx => ctx.Users.FirstOrDefault()
);
// 每个样本创建新作用域；每个样本后销毁作用域。
```

也提供异步变体:

```csharp
var result = await Benchmark.RunScopedAsync("DbQueryAsync",
    () => new MyDbContext(),
    static async ctx => await ctx.Users.FirstOrDefaultAsync()
);
// 异步变体：每个样本创建新作用域，每个样本后销毁。
```

### 比较两种实现

```csharp
var comparison = Benchmark.Compare(
    "String vs StringBuilder",
    "String Concat",  () => { var s = ""; for (int i = 0; i < 100; i++) s += "a"; },
    "StringBuilder",  () => { var sb = new StringBuilder(); for (int i = 0; i < 100; i++) sb.Append('a'); _ = sb.ToString(); }
);

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%) [{(comparison.Interval?.ToString() ?? "no interval")}] -> {comparison.Outcome}");
```

`Benchmark.Compare(name, baselineAction, candidateAction)` **不会**让每条通道只运行一次。它会在 10 轮中交错运行两条通道（每轮交替哪条通道先跑，因此任何一方都不会继承更热的进程），并报告**每轮配对比值的中位数**。这意味着：

- **`Speedup`** —— 基准耗时 ÷ 候选耗时；`> 1` 表示候选更快。候选未测到任何耗时的轮次不产生比值；当没有任何轮次给出可用比值时，比较会报告 `NaN`（`ImprovementPercent` 也随之是 `NaN`），而不是凭空编造结论。由两个独立的 `BenchmarkResult` 构造的 `ComparisonResult` 仍然报告两个平均值的比值。
- **`Interval`** —— 该中位数的无分布 95% 区间（10 个配对比值的第 2/第 9 顺序统计量）。`Interval.Contains(1.0)` 表示持平仍然可信。由两个独立结果构造的比较**没有区间**（`Interval` 为 `null`）—— 没有可配对的交错轮次，因此无法描述离散程度。
- **`Outcome`** —— 结论。`Faster` / `Slower` 要求比值离开 **5% 死区** *并且*（存在区间时）区间排除 1。落在其中即为 `Inconclusive`；无法计算的比值（非有限值或非正值）为 `Unknown`，可用配对轮次少于六轮的比较也是如此（低于该数量，区间无法以 95% 置信度覆盖中位数）。
- **`IsFaster`** —— 仅当 `Outcome == Faster` 时为 `true`，即**比以往更窄**：3% 的差异现在返回 `false`（`Inconclusive`），而没有轮次的比较也必须越过同样的 5% 死区。用 `Outcome` 获得三态答案，用 `IsFasterKnown` 区分“无可测差异”与“根本无法测量”。

成本：直接比较会让每条通道运行 10 次，即 **20 次 `Run`** —— 如果太慢，A/B 运行可降低 `SampleCount` / `WarmupIterations`。

### 高级：独立的热身、设置和清理

```csharp
var result = Benchmark.Run(
    name:     "Custom",
    action:   () => DoWork(),
    warmup:   () => DoWork(),      // null 跳过热身
    config:   BenchmarkConfig.Precise,
    setup:    () => PrepareState(), // 每个样本前调用（不计时）
    teardown: () => CleanUp()       // 每个样本后调用（不计时）
);
```

---

## 基于属性的 API 参考

用 `[BenchmarkClass]` 装饰一个 **partial** 类，并用下面的属性装饰其方法/属性。源生成器在编译时生成所有连接代码。

### 属性

| 属性 | 目标 | 描述 |
|-----------|--------|-------------|
| `[BenchmarkClass]` | 类 | 标记类进行代码生成。可选的 `Description` 属性。 |
| `[Benchmark]` | 方法 | 将无参数方法标记为基准测试。设置 `Baseline = true` 作为参考方法。可选的 `Description`。 |
| `[Params(values)]` | 属性 / 字段 | 迭代给定的编译时常量值。多个 `[Params]` 属性产生笛卡尔积。 |
| `[GlobalSetup]` | 方法 | 每个参数组合**调用一次**，在基准测试运行之前。 |
| `[GlobalCleanup]` | 方法 | 每个参数组合**调用一次**，在基准测试运行之后。 |
| `[IterationSetup]` | 方法 | 每个样本**前调用**（不计时）。 |
| `[IterationCleanup]` | 方法 | 每个样本**后调用**（不计时）。 |

`[Benchmark]` 与生命周期方法必须是实例、非泛型、无参数方法，可返回 `void`、`Task`、`ValueTask`、`Task<T>` 或 `ValueTask<T>`；异步方法会被 await，返回值被丢弃。`[Benchmark]` 方法不得为 `async void`（PBGEN011）。`[Params]` 目标必须是可写实例属性或非只读实例字段。基准类必须是非泛型、非嵌套、非抽象，并声明公共无参构造函数。

### 完整示例

```csharp
using PicoBench;

[BenchmarkClass(Description = "比较字符串连接策略")]
public partial class StringBenchmarks
{
    [Params(10, 100, 1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() { /* 为当前 N 准备数据 */ }

    [GlobalCleanup]
    public void Cleanup() { /* 释放资源 */ }

    [IterationSetup]
    public void BeforeSample() { /* 每个样本准备 */ }

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

### 运行

```csharp
// 内部创建实例：
var suite = BenchmarkRunner.Run<StringBenchmarks>(BenchmarkConfig.Quick);

// 或使用预配置的实例：
var instance = new StringBenchmarks();
var suite2 = BenchmarkRunner.Run(instance, BenchmarkConfig.Quick);
```

## 异步基准测试

两种 API 都支持异步工作。异步基准方法按迭代 await；同一类中可混用同步与异步生命周期方法。

```csharp
var result = await Benchmark.RunAsync("Http call", async () =>
{
    using var response = await httpClient.GetAsync(url);
});

var scoped = await Benchmark.RunScopedAsync("Scoped",
    () => container.CreateScope(),
    async scope => await scope.Service.DoWorkAsync());
```

- **计时模式** —— `AsyncTimingMode.WallClock`（默认）统计包含 await 挂起的完整时长；`AsyncTimingMode.CpuOnly` 统计 `Process.TotalProcessorTime`，排除 I/O 等待。CPU 时钟以操作系统定时器刻度推进（通常 10–16 ms），因此该模式下自动校准会把最小采样预算提升到实测时钟粒度。CpuOnly 模式不收集 GC 数据。
- **不可测样本** —— 在 `CpuOnly` 模式下，短于一个操作系统定时器刻度的工作负载会被读作恰好为零的 CPU 时间。此类样本会计入 `Statistics.UnmeasuredSampleCount`（`Unmeasured` 为 `true`），其每操作值变为**一个计时量子 —— 一个上界 —— 而不是 `0.000`**，并且各格式化器会把每操作单元格打印为 `N/A`。此类运行得到的统计不是对该工作的测量：应将其读作“低于 CPU 时钟的分辨率”。迭代次数**不会**被悄悄放大来掩盖问题 —— 只有 `AutoCalibrateIterations = true`（`Quick`/`Precise` 预设）才会校准；`Default` 原样使用配置的 `IterationsPerSample`。
- **GC 归因** —— 异步 GC 计数标记为近似值（`GcInfo.IsApproximate`）。两种模式都会排除 setup/teardown 的分配。
- **CPU 周期** —— 同步路径读取线程级计数器（Windows 上为 `QueryThreadCycleTime`，Linux 上为针对测量线程打开的 `perf_event`）。异步基准读取进程级计数器，因为线程级计数器无法跨线程切换跟踪：该数值包含其他线程完成的工作，并被标记为 `Statistics.CpuCyclesAreApproximate`，在 Console、Markdown、HTML 和 CSV 中渲染为 `~`。macOS 没有周期计数器 —— `mach_absolute_time` 是单调的*时间*代理而非架构周期，同样会被标记。
- **取消** —— `BenchmarkConfig.CancellationToken` 仅在异步基准的样本边界检查；同步基准忽略它。
- **混合类** —— 即使类中存在其他异步成员，同步 `[Benchmark]` 方法仍走直接同步测量路径。
- **预热** —— 预热迭代只调用预热委托；`setup`/`teardown` 与 `[IterationSetup]`/`[IterationCleanup]` 在预热期间不会执行。预热动作需自给自足。

---

## 测量保真度

PicoBench 采用进程内运行，CLR 配置会影响绝对值：

- 关闭分层 JIT：`DOTNET_TieredCompilation=0`、`DOTNET_TieredPGO=0`（或 `COMPlus_*`）。
- 单线程微基准建议使用工作站 GC：`DOTNET_gcServer=0`。
- 仅当操作系统允许非特权计数器时才可获取 CPU 周期计数（Windows `QueryThreadCycleTime`；Linux 在 `perf_event_paranoid` 为 2 或更低时使用 `perf_event`）。Windows 的同步运行是线程级的；异步运行与 macOS 使用进程级计数器，或在 macOS 上使用单调时钟代理而非真实周期 —— 两者都会被标记 `CpuCyclesAreApproximate`。`EnvironmentInfo` 与每个格式化器都会报告所用来源。
- `BenchmarkConfig.BoostPriorities`（默认 `true`）仅在运行期间提升进程与线程优先级，结束后恢复原值。

### 这些数字能分辨什么、不能分辨什么

每次运行都会用相同的迭代次数实测并扣除测试框架自身每操作附加的开销，然后公布它仍能分辨的最小值：

| 字段 | 含义 |
|---|---|
| `Statistics.OverheadNs` | 测试框架自身的每操作开销（委托分发、循环簿记、时钟与计数器读取）中已实测并扣除的部分。异步运行为 `0`，其开销不做扣除 |
| `Statistics.ResolutionLimitNanoseconds` | 本次运行能分辨的最小每操作取值：一个时钟刻度分摊到各次迭代，或所扣除开销的逐次运行离散程度，取二者中较大者 |
| `Statistics.ResolutionLimited` | `Avg` 等于或低于该上限，或至少有一个样本完全无法测量。格式化器会把每操作单元格打印为上界（Console 与 CSV 中为 `<limit`，Markdown 与 HTML 中为 `[below resolution]`），而不是一个数值 |
| `Statistics.Unmeasured` / `UnmeasuredSampleCount` | 计时源无法推进的样本数（`CpuOnly` 的零刻度）。这些单元格打印 `N/A`，绝不打印 `0.000` |

因此，对亚纳秒结果的诚实解读是 **“低于 `<limit` ns”**，而不是“0.02 ns”：该值并未被测量，而是被界定，并且该界定涵盖时钟量化与所扣除开销的离散程度（残差被钳制在零，因此它是测试框架能分辨的下限，而不是真实开销更小的证明）。还有两点不对称值得记住：测试框架开销扣除仅对同步基准实现（异步的每操作取值仍携带其每样本包装开销），而 GC 字节完全未被测量 —— 只有回收计数，且按进程，在异步路径上是近似的。

---

---

## 配置

### 预设

| 预设 | 热身迭代 | 样本数 | 基础每样本迭代数 | 自动校准 | 使用场景 |
|--------|--------|---------|------------------|----------|----------|
| `Quick` | 100 | 10 | 1,000 | 是 | 快速迭代 / CI |
| `Default` | 1,000 | 100 | 10,000 | 否 | 通用基准测试 |
| `Precise` | 5,000 | 200 | 50,000 | 是 | 最终测量 |

### 自定义配置

```csharp
var config = new BenchmarkConfig
{
    WarmupIterations    = 500,
    SampleCount         = 50,
    IterationsPerSample = 5000,
    RetainSamples       = true,  // 保留原始 TimingSample 数据
    AutoCalibrateIterations = true,
    MinSampleTime       = TimeSpan.FromMilliseconds(0.5),
    MaxAutoIterationsPerSample = 1_000_000,
    ForceGcBeforeBenchmark = true,  // false skips the pre-benchmark full GC
};

var result = Benchmark.Run("Test", action, config);
```

启用自动校准后，PicoBench 会自动增大 `IterationsPerSample`，直到达到最小样本耗时预算，或命中 `MaxAutoIterationsPerSample` 上限。这对极快操作特别有用，可减少计时噪声影响。 将 `ForceGcBeforeBenchmark` 设为 `false` 可跳过采样阶段之前的强制完整 GC——在运行大量基准与参数组合时很有用。

---

## 输出格式化器

四个内置格式化器实现 `IFormatter`，`SummaryFormatter` 则提供单独的摘要辅助：

```csharp
using PicoBench.Formatters;

var console  = new ConsoleFormatter();     // 盒绘制控制台表格
var markdown = new MarkdownFormatter();    // GitHub 友好的 Markdown
var html     = new HtmlFormatter();        // 样式化的 HTML 报告
var csv      = new CsvFormatter();         // CSV 用于数据分析

// 比较摘要的静态辅助方法：
Console.WriteLine(SummaryFormatter.Format(suite.Comparisons));
```

控制台、Markdown、HTML 和 CSV 输出会包含更偏精度分析的元数据，例如标准误、相对标准差，以及 CPU 计数器语义说明。

### 格式化目标

```csharp
formatter.Format(result);               // 单个 BenchmarkResult
formatter.Format(results);              // IEnumerable<BenchmarkResult>
formatter.Format(comparison);           // 单个 ComparisonResult
formatter.Format(comparisons);          // IEnumerable<ComparisonResult>
formatter.Format(suite);                // 完整的 BenchmarkSuite
```

### 格式化器选项

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

### 保存结果

```csharp
var dir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(dir);

File.WriteAllText(Path.Combine(dir, "results.md"),   new MarkdownFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.html"), new HtmlFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.csv"),  new CsvFormatter().Format(suite));
```

### CSV 列契约

`CsvFormatter` 先写一行表头，再写数据行；套件会额外写入 `#` 注释行（`# Suite:`, `# Environment:`, `# CpuCounter:`, `# CpuCounterNote:`, `# GcInfoNote:`, `# Timestamp:`, `# Duration:`）以及两个区段 `# Results` 与 `# Comparisons`，**每个区段都有自己的表头行**。因此一个文件中存在不止一行表头 —— `pandas.read_csv(path, comment='#')` 会跳过注释行，但两个区段需要你自己拆分。

结果列，按以下顺序（默认选项；`IncludePercentiles`、`IncludeGcInfo`、`IncludeCpuCycles` 与 `IncludeTimestamp` 为 `false` 时会移除各自的列）：

| # | 列 | 含义 |
|---|---|---|
| 1 | `Name` | 基准名称（包含 `,` `"` 或换行时加引号；开头的 `=`/`+`/`-`/`@` 会加上 `'` 前缀） |
| 2 | `Category` | 类别，未设置时为空 |
| 3 | `Avg_ns` | 每操作纳秒数 —— 有三种单元格状态，见下文 |
| 4 | `P50_ns` | 每操作纳秒数中位数（同样三种状态） |
| 5-12 | `P90_ns`, `P95_ns`, `P99_ns`, `Min_ns`, `Max_ns`, `StdDev_ns`, `StdErr_ns`, `RSD_pct` | 百分位数与离散程度；`RSD_pct` 是普通百分比，其余七个使用每操作单元格状态 |
| 13 | `CpuCycles` | 每操作周期数；`~` 前缀表示进程级计数器或时钟代理（`CpuCyclesAreApproximate`），而非线程周期 |
| 14-16 | `GC0`, `GC1`, `GC2` | 回收计数，未收集 GC 时为 `N/A` |
| 17 | `GCApproximate` | 计数器为近似值时是 `1`（异步路径），精确时为 `0`，未收集时为 `N/A` |
| 18 | `SampleCount` | 样本数（整数） |
| 19 | `IterationsPerSample` | 每样本迭代数（整数） |
| 20 | `Timestamp` | ISO-8601 往返格式（`o`，固定区域性） |

每操作列的单元格状态：

| 单元格 | 含义 |
|---|---|
| `12.5` | 以纳秒为单位的实测值（固定区域性，往返格式） |
| `<0.02` | **上界**：本次运行无法分辨高于 `Statistics.ResolutionLimitNanoseconds` 的取值。`<` 之后的数字就是该上限，它不是实测值 |
| `N/A` | 未测量（`Statistics.Unmeasured`，时钟增量为零）或未收集（GC 列） |

对使用者而言有两点后果：由于 `<0.02` 的存在，每操作列**不保证是数值型**，因此 pandas 之类的读取器会推断为 `object`，你必须显式处理上界行；而 `GCApproximate` 被插入到第 17 位，位于 `GC2` 与 `SampleCount` 之间，所以**按位置读取的代码必须把 `SampleCount`/`IterationsPerSample` 后移一位**（按列名读取不受影响）。

比较列：`TestCase`, `Provider`, `Avg_ns`, `Speedup`, `P50_ns`, `P90_ns`, `P99_ns`, `StdErr_ns`, `RSD_pct`, `CpuCycles`, `GC0`, `GC1`, `GC2`, `GCApproximate`。每个比较会写出两行 —— 候选在前，基准在后 —— 并且 `Speedup` 只填在候选行。比较的单元格目前走普通数字格式化器，因此结果表中的 `N/A`/`<limit` 不会出现在那里。

> 声明这些状态、机器可读的 `# Schema:` 表头行被作为单独的 Batch 0 后续事项跟踪。在该项落地之前，上表就是契约，并且套件文件还无法标识自己的 schema 版本。

---

## 结果模型

| 类型 | 描述 |
|------|-------------|
| `BenchmarkResult` | 名称、类别、标签、统计、样本、每次样本迭代数、样本数、时间戳 |
| `ComparisonResult` | 名称、类别、标签、基准、候选、加速比、改进百分比、**Outcome**、**IsFasterKnown**、**Interval**、是否更快 |
| `BenchmarkSuite` | 名称、描述、结果、比较、环境、持续时间、时间戳 |
| `ComparisonOutcome` | 枚举：`Unknown`（无法计算任何比值）、`Inconclusive`（落在 5% 死区内，或区间仍包含持平）、`Faster`、`Slower` |
| `ComparisonInterval` | Low, High, Contains(value), ToString() —— 中位配对比值的无分布 95% 区间 |
| `Statistics` | 平均值、P50、P90、P95、P99、最小值、最大值、标准差、标准误、相对标准差百分比、每操作 CPU 周期、**CpuCyclesAreApproximate**、**OverheadNs**、**ResolutionLimitNanoseconds**、**ResolutionLimited**、**Unmeasured**、**UnmeasuredSampleCount**、GC 信息、HasHighVariance |
| `TimingSample` | 纳秒耗时、毫秒耗时、计时器滴答数、CPU 周期、**CpuCyclesAreApproximate**、GC 信息 |
| `GcInfo` | Gen0、Gen1、Gen2、总计、是否为零、IsApproximate |
| `EnvironmentInfo` | 操作系统、架构、运行时版本、处理器数量、执行模式、配置、CPU 计数器类型 / 可用性 / 是否有意义、自定义标签 |

测量保真度工作中新增成员的含义：

| 成员 | 契约 |
|---|---|
| `ComparisonResult.Outcome` | 三态结论；`Faster`/`Slower` 需要 5% 死区**并且**一个排除 1 的区间 |
| `ComparisonResult.IsFasterKnown` | `Outcome != Unknown` —— “两条通道已被比较过”（它们仍可能无法区分） |
| `ComparisonResult.IsFaster` | 仅 `Outcome == Faster`。比旧的 `Speedup > 1.0` 更窄：死区内的差异不再算“更快” |
| `ComparisonResult.Interval` | `ComparisonInterval?`；当比较由两个独立结果而非交错轮次构造时为 `null` |
| `Statistics.ResolutionLimited` | 每操作取值是一个上界（`<limit`），不是一次测量 |
| `Statistics.UnmeasuredSampleCount` / `Unmeasured` | 计时源无法推进的样本；它们的每操作单元格为 `N/A` |
| `Statistics.CpuCyclesAreApproximate` | `CpuCyclesPerOp` 是进程级（异步）或单调时钟代理（macOS），而非线程周期：格式化器会加上 `~` 前缀 |
| `TimingSample.CpuCyclesAreApproximate` | 样本级同一标记；`Statistics` 以逻辑或聚合它 |

---

## 架构

```
src/
+-- PicoBench/                        # 主库 (netstandard2.0)
|   +-- Benchmark.cs                   # 命令式 API (Run, Compare, RunScoped)
|   +-- BenchmarkRunner.cs             # 基于属性的入口点 (Run<T>)
|   +-- BenchmarkConfig.cs             # 配置和预设
|   +-- Attributes.cs                  # 7 个基准测试属性
|   +-- IBenchmarkClass.cs             # 生成器实现的接口
|   +-- Runner.cs                      # 底层计时流程与样本构造
|   +-- Runner.Gc.cs                   # GC 基线与增量跟踪
|   +-- Runner.Cpu.cs                  # 平台相关 CPU 计数器实现
|   +-- StatisticsCalculator.cs        # 百分位数 / 统计计算
|   +-- Models.cs                      # 结果类型
|   +-- Formatters/
|       +-- IFormatter.cs              # IFormatter, FormatterOptions & FormatterBase
|       +-- ConsoleFormatter.cs        # 盒绘制控制台表格
|       +-- MarkdownFormatter.cs       # GitHub Markdown 表格
|       +-- HtmlFormatter.cs           # 样式化的 HTML 报告
|       +-- CsvFormatter.cs            # CSV 导出
|       +-- SummaryFormatter.cs        # 胜/负摘要
|
+-- PicoBench.Generators/            # 源生成器 (netstandard2.0)
    +-- BenchmarkGenerator.cs          # IIncrementalGenerator 入口点
    +-- BenchmarkClassAnalyzer.cs      # Roslyn 分析与诊断
    +-- CSharpLiteralFormatter.cs      # 为生成代码格式化 C# 字面量
    +-- DiagnosticDescriptors.cs       # 生成器诊断定义
    +-- Emitter.cs                     # C# 代码发射器 (AOT 安全)
    +-- Models.cs                      # Roslyn 分析模型
```

---

## 平台特定功能

| 功能 | Windows | Linux | macOS |
|---------|---------|-------|-------|
| 计时 | Stopwatch | Stopwatch | Stopwatch |
| GC 跟踪 (Gen0/1/2) | 是 | 是 | 是 |
| CPU 周期计数（同步） | `QueryThreadCycleTime`（线程级） | 每线程的 `perf_event_open`（当 `perf_event_paranoid` ≤ 2 时） | `mach_absolute_time`，一个**时间代理** —— 标记为 `~` |
| CPU 周期计数（异步） | `QueryProcessCycleTime`（进程级）—— 标记为 `~` | 进程级 `perf_event` —— 标记为 `~` | `mach_absolute_time` —— 标记为 `~` |
| 进程优先级提升 | 是 | 是 | 是 |

在 macOS 上，导出的 CPU 计数器是高分辨率单调的*时间*代理，而不是架构周期计数：该值随时间推进，而不是随已执行的指令推进，因此被标记 `CpuCyclesAreApproximate` 并以 `~` 打印。异步路径使用同一标记，其进程级计数器包含其他线程完成的工作。`EnvironmentInfo` 与格式化器输出都会显式暴露这两种区别。

---

## 示例

| 示例 | API 风格 | 描述 |
|--------|-----------|-------------|
| `StringVsStringBuilder` | 命令式 | 比较 `string +=`、`StringBuilder` 和带容量的 `StringBuilder` |
| `AttributeBased` | 基于属性 | 使用 `[Benchmark]`、`[Params]` 和源生成器的相同比较 |
| `CollectionBenchmarks` | 基于属性 | List vs Dictionary vs HashSet 查找 - 展示所有属性 |

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
dotnet run --project samples/AttributeBased -c Release
dotnet run --project samples/CollectionBenchmarks -c Release
```

---

## 与 BenchmarkDotNet 比较

| 功能 | PicoBench | BenchmarkDotNet |
|---------|-----------|----------------|
| 依赖 | 0 | 多 |
| 包大小 | 小 | 大 |
| 目标框架 | netstandard2.0 | net6.0+ |
| AOT 支持 | 源生成器 | 基于反射 |
| 属性 API | `[Benchmark]`、`[Params]` | `[Benchmark]`、`[Params]` |
| 设置时间 | 即时 | 秒级 |
| 输出格式 | 5 | 10+ |
| 统计深度 | 良好 | 广泛 |
| 使用场景 | 快速 A/B 测试、CI、AOT 应用 | 详细分析、出版物 |

---

## 许可证

MIT 许可证 - 详情见 [LICENSE](LICENSE) 文件。

## 构建与发布

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

发布遵循 PicoHex 版本规则 `<year>.<x>.<y>`：公共 API 发生变化时 **x** 加一，未变化时 **y** 加一。判定依据仓库内的 API 基线 `api/PicoBench.public.txt`，它始终保存上一个发布版本的公共表面。

```bash
# 查看 API 差异与下一个版本
pwsh ./scripts/release.ps1 -DryRun

# 发布：刷新 api/、提交 chore(release): v<version>、打 tag、push
pwsh ./scripts/release.ps1 -Push
```

推送 tag 后，GitHub Actions 发布流水线会运行测试、按 tag 版本打包并发布到 [NuGet.org](https://www.nuget.org/packages/PicoBench)。为弥补兄弟仓库的索引空窗，可将同一版本打包到 `NuGet.config` 声明的本地源：

```bash
dotnet pack src/PicoBench/PicoBench.csproj -c Release -o artifacts/nupkg -p:Version=<version>
```

## 贡献

1. Fork 仓库
2. 创建特性分支
3. 进行更改并附带测试
4. 提交拉取请求
