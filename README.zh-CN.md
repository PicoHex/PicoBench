# Pico.Bench

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Русский](README.ru.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Português (Brasil)](README.pt-BR.md)

一个轻量级、零依赖的 .NET 基准测试库，提供 **两种互补的 API**：命令式流式 API 和基于属性、源生成的 API，完全 **AOT 兼容**。

## 特性

- **零依赖** - 纯 .NET 实现，无需外部包
- **两种 API** - 命令式 (`Benchmark.Run`) 用于临时测试；基于属性 (`[Benchmark]` + 源生成器) 用于结构化测试套件
- **AOT 兼容的源生成器** - 增量生成器在运行时生成直接方法调用，零反射
- **跨平台** - 完全支持 Windows、Linux 和 macOS
- **高精度计时** - 使用 `Stopwatch`，纳秒级精度
- **GC 跟踪** - 监控基准测试期间的 Gen0/Gen1/Gen2 回收计数
- **CPU 周期计数** - 硬件级周期计数（Windows 通过 `QueryThreadCycleTime`，Linux 通过 `perf_event`，macOS 通过 `mach_absolute_time`）
- **统计分析** - 均值、中位数、P90、P95、P99、最小值、最大值、标准差
- **多种输出格式** - 控制台、Markdown、HTML、CSV 和程序化摘要
- **参数化基准测试** - `[Params]` 属性，支持自动笛卡尔积迭代
- **比较支持** - 基准 vs 候选实现，带加速比计算
- **可配置** - Quick、Default 和 Precise 预设或完全自定义配置
- **netstandard2.0** - 兼容 .NET Framework 4.6.1+、.NET Core 2.0+、.NET 5+

## 安装

引用 **Pico.Bench** NuGet 包。源生成器 (`Pico.Bench.Generators`) 作为分析器自动捆绑 - 无需额外引用。

```bash
dotnet add package Pico.Bench
```

## 快速开始

### 命令式 API

```csharp
using Pico.Bench;

var result = Benchmark.Run("My Benchmark", () =>
{
    Thread.SpinWait(100);
});

Console.WriteLine($"Average: {result.Statistics.Avg:F1} ns/op");
```

### 基于属性的 API（源生成）

```csharp
using Pico.Bench;

var suite = BenchmarkRunner.Run<MyBenchmarks>();
Console.WriteLine(new Pico.Bench.Formatters.ConsoleFormatter().Format(suite));

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

---

## 命令式 API 参考

### 基本基准测试

```csharp
using Pico.Bench;
using Pico.Bench.Formatters;

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

### 比较两种实现

```csharp
var comparison = Benchmark.Compare(
    "String vs StringBuilder",
    "String Concat",  () => { var s = ""; for (int i = 0; i < 100; i++) s += "a"; },
    "StringBuilder",  () => { var sb = new StringBuilder(); for (int i = 0; i < 100; i++) sb.Append('a'); _ = sb.ToString(); }
);

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%)");
```

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

### 完整示例

```csharp
using Pico.Bench;

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

---

## 配置

### 预设

| 预设 | 热身迭代 | 样本数 | 每次样本迭代数 | 使用场景 |
|--------|--------|---------|--------------|----------|
| `Quick` | 100 | 10 | 1,000 | 快速迭代 / CI |
| `Default` | 1,000 | 100 | 10,000 | 通用基准测试 |
| `Precise` | 5,000 | 200 | 50,000 | 最终测量 |

### 自定义配置

```csharp
var config = new BenchmarkConfig
{
    WarmupIterations    = 500,
    SampleCount         = 50,
    IterationsPerSample = 5000,
    RetainSamples       = true   // 保留原始 TimingSample 数据
};

var result = Benchmark.Run("Test", action, config);
```

---

## 输出格式化器

五个内置格式化器实现 `IFormatter`：

```csharp
using Pico.Bench.Formatters;

var console  = new ConsoleFormatter();     // 盒绘制控制台表格
var markdown = new MarkdownFormatter();    // GitHub 友好的 Markdown
var html     = new HtmlFormatter();        // 样式化的 HTML 报告
var csv      = new CsvFormatter();         // CSV 用于数据分析

// 比较摘要的静态辅助方法：
Console.WriteLine(SummaryFormatter.Format(suite.Comparisons));
```

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

---

## 结果模型

| 类型 | 描述 |
|------|-------------|
| `BenchmarkResult` | 名称、统计、样本、每次样本迭代数、样本数、标签、类别 |
| `ComparisonResult` | 基准、候选、加速比、是否更快、改进百分比 |
| `BenchmarkSuite` | 名称、描述、结果、比较、环境、持续时间 |
| `Statistics` | 平均值、P50、P90、P95、P99、最小值、最大值、标准差、每操作 CPU 周期、GC 信息 |
| `TimingSample` | 纳秒耗时、毫秒耗时、计时器滴答数、CPU 周期、GC 信息 |
| `GcInfo` | Gen0、Gen1、Gen2、总计、是否为零 |
| `EnvironmentInfo` | 操作系统、架构、运行时版本、处理器数量、配置 |

---

## 架构

```
src/
+-- Pico.Bench/                        # 主库 (netstandard2.0)
|   +-- Benchmark.cs                   # 命令式 API (Run, Compare, RunScoped)
|   +-- BenchmarkRunner.cs             # 基于属性的入口点 (Run<T>)
|   +-- BenchmarkConfig.cs             # 配置和预设
|   +-- Attributes.cs                  # 7 个基准测试属性
|   +-- IBenchmarkClass.cs             # 生成器实现的接口
|   +-- Runner.cs                      # 底层计时引擎
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
+-- Pico.Bench.Generators/            # 源生成器 (netstandard2.0)
    +-- BenchmarkGenerator.cs          # IIncrementalGenerator 入口点
    +-- Emitter.cs                     # C# 代码发射器 (AOT 安全)
    +-- Models.cs                      # Roslyn 分析模型
```

---

## 平台特定功能

| 功能 | Windows | Linux | macOS |
|---------|---------|-------|-------|
| 高精度计时 | Stopwatch | Stopwatch | Stopwatch |
| GC 跟踪 (Gen0/1/2) | 是 | 是 | 是 |
| CPU 周期计数 | `QueryThreadCycleTime` | `perf_event_open` | `mach_absolute_time` |
| 进程优先级提升 | 是 | 是 | 是 |

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

| 功能 | Pico.Bench | BenchmarkDotNet |
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

## 贡献

1. Fork 仓库
2. 创建特性分支
3. 进行更改并附带测试
4. 提交拉取请求