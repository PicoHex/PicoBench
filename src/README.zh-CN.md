# 源项目

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Русский](README.ru.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Português (Brasil)](README.pt-BR.md)

此目录包含构成 Pico.Bench 的两个库项目。

## Pico.Bench

主要的基准测试库，目标为 **netstandard2.0**，零外部依赖。

### 关键文件

| 文件 | 用途 |
|------|---------|
| `Benchmark.cs` | 命令式 API - `Run()`、`Run<TState>()`、`RunScoped<TScope>()`、`Compare()` |
| `BenchmarkRunner.cs` | 基于属性的入口点 - `Run<T>()` |
| `Attributes.cs` | 七个属性：`[BenchmarkClass]`、`[Benchmark]`、`[Params]`、`[GlobalSetup]`、`[GlobalCleanup]`、`[IterationSetup]`、`[IterationCleanup]` |
| `IBenchmarkClass.cs` | 由源生成器在装饰类上实现的接口 |
| `BenchmarkConfig.cs` | 包含 Quick / Default / Precise 预设的配置 |
| `Runner.cs` | 底层计时引擎，支持平台特定的 CPU 周期计数 |
| `StatisticsCalculator.cs` | 百分位数和统计计算 |
| `Models.cs` | 结果类型：`BenchmarkResult`、`ComparisonResult`、`BenchmarkSuite`、`Statistics`、`TimingSample`、`GcInfo`、`EnvironmentInfo` |
| `Formatters/` | 五个格式化器：Console、Markdown、HTML、CSV、Summary |

### 打包

项目将 `Pico.Bench.Generators` 捆绑为分析器，因此使用者自动获取源生成器：

```bash
# 添加项目引用
dotnet add reference ../Pico.Bench.Generators/Pico.Bench.Generators.csproj

# 然后在你的 .csproj 文件中的 <ProjectReference> 元素内手动添加以下属性：
# PrivateAssets="all"
# ReferenceOutputAssembly="false"
# OutputItemType="Analyzer"
```

## Pico.Bench.Generators

一个**增量源生成器** (`IIncrementalGenerator`)，将用 `[BenchmarkClass]` 装饰的 partial 类在编译时转换为完整的 `IBenchmarkClass` 实现。

- **目标**：netstandard2.0
- **依赖**：Microsoft.CodeAnalysis.CSharp 4.3.1
- **输出**：AOT 兼容的 C#，使用 `global::` 限定调用，无反射

### 关键文件

| 文件 | 用途 |
|------|---------|
| `BenchmarkGenerator.cs` | 生成器入口点，使用 `ForAttributeWithMetadataName` |
| `Emitter.cs` | C# 代码发射器 - 生成 `RunBenchmarks()`，包含参数迭代、设置/清理钩子和比较逻辑 |
| `Models.cs` | Roslyn 分析模型：`BenchmarkClassModel`、`BenchmarkMethodModel`、`ParamsPropertyModel`（全部实现 `IEquatable<T>` 以支持缓存） |

### 生成的代码

对于如下类：

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

生成器发射一个 `partial class MyBench : IBenchmarkClass`，其中包含 `RunBenchmarks()` 方法，该方法：

1. 迭代每个 `[Params]` 值（多个属性产生笛卡尔积）
2. 设置属性，调用 `[GlobalSetup]`
3. 通过 `Benchmark.Run()` 运行每个 `[Benchmark]` 方法，使用 `[IterationSetup]`/`[IterationCleanup]` 作为设置/清理
4. 将候选方法与基准进行比较
5. 调用 `[GlobalCleanup]`
6. 返回包含所有结果和比较的 `BenchmarkSuite`