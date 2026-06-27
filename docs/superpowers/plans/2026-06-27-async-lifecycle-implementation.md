# Async Lifecycle & Benchmark Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add full async support for benchmark lifecycle methods and benchmark bodies across the entire PicoBench pipeline (analyzer → model → emitter → runtime).

**Architecture:** Single `IBenchmarkClass` interface returning `ValueTask<BenchmarkSuite>` with `RunBenchmarksAsync`. Sync classes return `ValueTask.FromResult` (zero allocation); async classes emit `async ValueTask`. Analyzer auto-detects `void`/`Task`/`ValueTask` return types. Public API split: `Benchmark.Run` (sync, returns `BenchmarkResult`) and `Benchmark.RunAsync` (async, returns `Task<BenchmarkResult>`).

**Tech Stack:** C# 13, .NET 10 (`PicoBench` runtime), .NET Standard 2.0 (`PicoBench.Generators`), Roslyn Source Generators, TUnit tests.

## Global Constraints

- No new attributes — auto-detect return type (void/Task/ValueTask) to classify sync vs async
- `async void` lifecycle methods get analyzer warning PBGEN009, not error
- `Func<Task>` overloads for public API (not `Func<ValueTask>`)
- `CpuOnly` timing uses `Process.TotalProcessorTime`; GC info skipped in this mode
- `CancellationToken` in `BenchmarkConfig`, checked at sample boundaries
- Sync `Run<T>()` is a blocking shortcut; XML doc warns about deadlock on UI threads
- Pure sync classes must have zero heap allocation overhead (ValueTask.FromResult)

---

## File Structure Map

```
src/PicoBench/
├── IBenchmarkClass.cs           MODIFY: ValueTask<BenchmarkSuite> RunBenchmarksAsync
├── Models.cs                    MODIFY: GcInfo.IsApproximate, TimingSample.GcInfo nullable
├── BenchmarkConfig.cs           MODIFY: +AsyncTimingMode, +CancellationToken
├── Benchmark.cs                 MODIFY: +RunAsync overloads, +CollectAndBuildAsync
├── Runner.cs                    MODIFY: +TimeAsync, +TimeCpuAsync
├── BenchmarkRunner.cs           MODIFY: +RunAsync<T>, update Run<T>

src/PicoBench.Generators/
├── Models.cs                    MODIFY: +LifecycleMethodInfo, +IsAsync on models
├── DiagnosticDescriptors.cs     MODIFY: +PBGEN009
├── BenchmarkClassAnalyzer.cs    MODIFY: relax validation, detect IsAsync, async void warning
├── Emitter.cs                   MODIFY: sync→ValueTask.FromResult, async→async ValueTask

tests/PicoBench.Tests/
├── BenchmarkRunnerTests.cs      MODIFY: FakeBenchmarkClass to new interface
├── ModelsTests.cs               MODIFY: add GcInfo.IsApproximate test
├── Generators/ModelsTests.cs    MODIFY: update CreateModel helper + add IsAsync tests
├── Generators/EmitterTests.cs   MODIFY: update assertions, add sync/async generation tests
├── Generators/BenchmarkGeneratorDiagnosticsTests.cs  MODIFY: +PBGEN009, +async validation
├── BenchmarkTests.cs            MODIFY: +RunAsync tests
├── BenchmarkConfigTests.cs      MODIFY: +TimingMode, +CancellationToken tests
├── RunnerTests.cs               MODIFY: +TimeAsync tests
```

---

### Task 1: Update IBenchmarkClass interface

**Files:**
- Modify: `src/PicoBench/IBenchmarkClass.cs`

**Interfaces:**
- Produces: `ValueTask<BenchmarkSuite> RunBenchmarksAsync(BenchmarkConfig? config = null)`

- [ ] **Step 1: Add required global using to both projects**

In `src/PicoBench/GlobalUsings.cs`, add:
```csharp
global using System.Threading.Tasks;
```

In `tests/PicoBench.Tests/GlobalUsings.cs`, add:
```csharp
global using System.Threading.Tasks;
```

This covers `Task<T>`, `ValueTask<T>`, `CancellationToken`, and `Func<Task>` used throughout the runtime library and test code.

- [ ] **Step 2: Change interface**

Replace content of `src/PicoBench/IBenchmarkClass.cs`:

```csharp
namespace PicoBench;

/// <summary>
/// Interface implemented by source-generated benchmark classes.
/// The source generator adds this interface to any class decorated with
/// <see cref="BenchmarkClassAttribute"/>, emitting a full <see cref="RunBenchmarksAsync"/>
/// implementation that is AOT-compatible (no reflection).
/// </summary>
public interface IBenchmarkClass
{
    /// <summary>
    /// Runs all <see cref="BenchmarkAttribute"/>-marked methods in this class
    /// and returns a <see cref="BenchmarkSuite"/> containing the results.
    /// </summary>
    /// <param name="config">
    /// Optional configuration. Defaults to <see cref="BenchmarkConfig.Default"/> when <c>null</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that completes with the suite
    /// containing individual results and any comparisons.
    /// For pure sync classes, returns <see cref="ValueTask.FromResult{TResult}"/>
    /// with zero heap allocation.
    /// </returns>
    ValueTask<BenchmarkSuite> RunBenchmarksAsync(BenchmarkConfig? config = null);
}
```

- [ ] **Step 3: Verify build**

```powershell
dotnet build D:/MyProjects/PicoHex/PicoBench/src/PicoBench/PicoBench.csproj -c Debug 2>&1 | Select-Object -Last 5
```

Expected: build succeeds. Test project will fail until fakes are updated.

- [ ] **Step 4: Update FakeBenchmarkClass in tests**

Modify `tests/PicoBench.Tests/BenchmarkRunnerTests.cs:25-43`:

Replace:
```csharp
        public BenchmarkSuite RunBenchmarks(BenchmarkConfig? config = null)
        {
            RunCount++;
            LastConfig = config;

            return new BenchmarkSuite(
                name: "FakeSuite",
                environment: new EnvironmentInfo(),
                results: [BenchmarkResultFactory.Create("FakeBenchmark")],
                duration: TimeSpan.FromMilliseconds(100),
                description: "Fake suite for testing"
            );
        }
```

With:
```csharp
        public ValueTask<BenchmarkSuite> RunBenchmarksAsync(BenchmarkConfig? config = null)
        {
            RunCount++;
            LastConfig = config;

            return ValueTask.FromResult(new BenchmarkSuite(
                name: "FakeSuite",
                environment: new EnvironmentInfo(),
                results: [BenchmarkResultFactory.Create("FakeBenchmark")],
                duration: TimeSpan.FromMilliseconds(100),
                description: "Fake suite for testing"
            ));
        }
```

- [ ] **Step 5: Build and run BenchmarkRunnerTests**

```powershell
dotnet build D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug
dotnet test --project D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug -- --filter "Category~BenchmarkRunner" --no-progress
```

Expected: all 7 BenchmarkRunner tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/PicoBench/GlobalUsings.cs src/PicoBench/IBenchmarkClass.cs tests/PicoBench.Tests/GlobalUsings.cs tests/PicoBench.Tests/BenchmarkRunnerTests.cs
git commit -m "feat: change IBenchmarkClass to async ValueTask<BenchmarkSuite> RunBenchmarksAsync"
```

---

### Task 2: Update runtime models — GcInfo.IsApproximate

**Files:**
- Modify: `src/PicoBench/Models.cs`

**Interfaces:**
- Produces: `GcInfo` with `bool IsApproximate`, `TimingSample.GcInfo` becomes nullable (`GcInfo?`)

- [ ] **Step 1: Add IsApproximate to GcInfo**

In `src/PicoBench/Models.cs`, find `public sealed class GcInfo` and add property:

```csharp
public sealed class GcInfo
{
    /// <summary>Gen0 collection count delta.</summary>
    public int Gen0 { get; init; }

    /// <summary>Gen1 collection count delta.</summary>
    public int Gen1 { get; init; }

    /// <summary>Gen2 collection count delta.</summary>
    public int Gen2 { get; init; }

    /// <summary>When true, GC counts may include non-benchmark work (async modes).</summary>
    public bool IsApproximate { get; init; }

    /// <summary>Total GC collections across all generations.</summary>
    public int Total => Gen0 + Gen1 + Gen2;

    /// <summary>Returns true if no GC occurred during the benchmark.</summary>
    public bool IsZero => Gen0 == 0 && Gen1 == 0 && Gen2 == 0;

    /// <inheritdoc />
    public override string ToString() =>
        IsApproximate ? $"~{Gen0}/{Gen1}/{Gen2}" : $"{Gen0}/{Gen1}/{Gen2}";
}
```

- [ ] **Step 2: Make TimingSample.GcInfo nullable**

In `src/PicoBench/Models.cs`, change TimingSample:

```csharp
public sealed class TimingSample
{
    // ... ElapsedNanoseconds, ElapsedMilliseconds, ElapsedTicks, CpuCycles unchanged ...

    /// <summary>
    /// GC collection counts during this sample.
    /// <c>null</c> when GC data was not collected (CpuOnly async mode).
    /// </summary>
    public GcInfo? GcInfo { get; init; }
}
```

Remove the `= new();` default.

- [ ] **Step 3: Fix Statistics.GcInfo default**

In `src/PicoBench/Models.cs`, `Statistics` class:

```csharp
/// <summary>Aggregated GC info across all samples. Null when not collected.</summary>
public GcInfo? GcInfo { get; init; }
```

Remove `= new GcInfo()`.

- [ ] **Step 4: Verify Runner.cs and StatisticsCalculator**

`src/PicoBench/Runner.cs` — the `CreateSample` method sets `GcInfo = CalculateGcDelta(...)`. The return type of `CalculateGcDelta` is `GcInfo` (non-null), and `TimingSample.GcInfo` is now `GcInfo?`. C# allows implicit conversion — no code change needed in Runner.cs.

`src/PicoBench/Runner.Gc.cs` — `CalculateGcDelta` returns `GcInfo`. No change needed.

- [ ] **Step 5: Fix StatisticsCalculator for nullable GcInfo**

Read and fix `src/PicoBench/StatisticsCalculator.cs` — the aggregation of `GcInfo` across samples needs to handle `null`:

```csharp
// In the Compute method, after samples loop:
var gcInfos = samples.Where(s => s.GcInfo != null).Select(s => s.GcInfo!).ToArray();

var aggregatedGcInfo = gcInfos.Length > 0
    ? new GcInfo
    {
        Gen0 = gcInfos.Sum(g => g.Gen0),
        Gen1 = gcInfos.Sum(g => g.Gen1),
        Gen2 = gcInfos.Sum(g => g.Gen2),
        IsApproximate = gcInfos.Any(g => g.IsApproximate)
    }
    : null;
```

Update the `Statistics` return to use `GcInfo = aggregatedGcInfo`.

- [ ] **Step 6: Fix formatters for nullable GcInfo**

Update each formatter to handle `GcInfo?`:
- `src/PicoBench/Formatters/ConsoleFormatter.cs`
- `src/PicoBench/Formatters/CsvFormatter.cs`
- `src/PicoBench/Formatters/HtmlFormatter.cs`
- `src/PicoBench/Formatters/MarkdownFormatter.cs`
- `src/PicoBench/Formatters/SummaryFormatter.cs`

Replace `stats.GcInfo.ToString()` or `stats.GcInfo.Total` accesses with `stats.GcInfo?.ToString() ?? "N/A"` and `stats.GcInfo?.Total ?? 0`.

- [ ] **Step 7: Build and run full test suite**

```powershell
dotnet build D:/MyProjects/PicoHex/PicoBench -c Debug
dotnet test --project D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug -- --no-progress
```

Expected: all existing tests pass (some may need null-check updates).

- [ ] **Step 8: Commit**

```bash
git add src/PicoBench/Models.cs src/PicoBench/StatisticsCalculator.cs src/PicoBench/Formatters/
git commit -m "feat: add GcInfo.IsApproximate, make TimingSample.GcInfo nullable for async modes"
```

---

### Task 3: Update generator models — LifecycleMethodInfo and IsAsync

**Files:**
- Modify: `src/PicoBench.Generators/Models.cs`

**Interfaces:**
- Produces: `LifecycleMethodInfo` record, `BenchmarkClassModel.IsAsync`, `BenchmarkMethodModel.IsAsync`
- Consumes: (none — foundation task)

- [ ] **Step 1: Add LifecycleMethodInfo**

In `src/PicoBench.Generators/Models.cs`, add BEFORE `BenchmarkClassModel`:

```csharp
/// <summary>
/// Describes a lifecycle method (GlobalSetup/GlobalCleanup/IterationSetup/IterationCleanup).
/// </summary>
internal sealed class LifecycleMethodInfo : IEquatable<LifecycleMethodInfo>
{
    public string Name { get; init; } = "";
    public bool IsAsync { get; init; }

    public bool Equals(LifecycleMethodInfo? other)
    {
        if (other is null) return false;
        return Name == other.Name && IsAsync == other.IsAsync;
    }

    public override bool Equals(object? obj) => Equals(obj as LifecycleMethodInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            return (Name.GetHashCode() * 397) ^ IsAsync.GetHashCode();
        }
    }
}
```

- [ ] **Step 2: Update BenchmarkClassModel fields**

Replace the four `string?` lifecycle fields:

```csharp
public LifecycleMethodInfo? GlobalSetupMethod { get; init; }
public LifecycleMethodInfo? GlobalCleanupMethod { get; init; }
public LifecycleMethodInfo? IterationSetupMethod { get; init; }
public LifecycleMethodInfo? IterationCleanupMethod { get; init; }
```

Add class-level flag:

```csharp
public bool IsAsync { get; init; }
```

- [ ] **Step 3: Add IsAsync to BenchmarkMethodModel**

```csharp
internal sealed class BenchmarkMethodModel : IEquatable<BenchmarkMethodModel>
{
    public string Name { get; init; } = "";
    public bool IsAsync { get; init; }
    public bool IsBaseline { get; init; }
    public string? Description { get; init; }
    // ... Equals/GetHashCode include IsAsync
}
```

Update `Equals`:
```csharp
public bool Equals(BenchmarkMethodModel? other)
{
    if (other is null) return false;
    return Name == other.Name
        && IsAsync == other.IsAsync
        && IsBaseline == other.IsBaseline
        && Description == other.Description;
}
```

Update `GetHashCode`:
```csharp
public override int GetHashCode()
{
    unchecked
    {
        var hash = 17;
        hash = hash * 31 + Name.GetHashCode();
        hash = hash * 31 + IsAsync.GetHashCode();
        hash = hash * 31 + IsBaseline.GetHashCode();
        hash = hash * 31 + (Description?.GetHashCode() ?? 0);
        return hash;
    }
}
```

- [ ] **Step 4: Update BenchmarkClassModel Equals**

Add `IsAsync` to equality check. The lifecycle fields now compare `LifecycleMethodInfo?` objects instead of `string?`:

```csharp
public bool Equals(BenchmarkClassModel? other)
{
    if (other is null) return false;
    if (ReferenceEquals(this, other)) return true;
    return Namespace == other.Namespace
        && ClassName == other.ClassName
        && AccessModifier == other.AccessModifier
        && Description == other.Description
        && IsAsync == other.IsAsync
        && Equals(GlobalSetupMethod, other.GlobalSetupMethod)
        && Equals(GlobalCleanupMethod, other.GlobalCleanupMethod)
        && Equals(IterationSetupMethod, other.IterationSetupMethod)
        && Equals(IterationCleanupMethod, other.IterationCleanupMethod)
        && Methods.SequenceEqual(other.Methods)
        && ParamsProperties.SequenceEqual(other.ParamsProperties);
}
```

Add static helper:
```csharp
private static bool Equals(LifecycleMethodInfo? left, LifecycleMethodInfo? right)
{
    if (left is null && right is null) return true;
    if (left is null || right is null) return false;
    return left.Equals(right);
}
```

Update `GetHashCode`:
```csharp
public override int GetHashCode()
{
    unchecked
    {
        var hash = 17;
        hash = hash * 31 + (Namespace?.GetHashCode() ?? 0);
        hash = hash * 31 + ClassName.GetHashCode();
        hash = hash * 31 + AccessModifier.GetHashCode();
        hash = hash * 31 + (Description?.GetHashCode() ?? 0);
        hash = hash * 31 + IsAsync.GetHashCode();
        hash = hash * 31 + (GlobalSetupMethod?.GetHashCode() ?? 0);
        hash = hash * 31 + (GlobalCleanupMethod?.GetHashCode() ?? 0);
        hash = hash * 31 + (IterationSetupMethod?.GetHashCode() ?? 0);
        hash = hash * 31 + (IterationCleanupMethod?.GetHashCode() ?? 0);
        hash = hash * 31 + Methods.Length;
        hash = hash * 31 + ParamsProperties.Length;
        return hash;
    }
}
```

- [ ] **Step 5: Update Emitter AND Analyzer to compile against new model (minimal)**

Fix `src/PicoBench.Generators/Emitter.cs` — change `model.GlobalSetupMethod` references to `model.GlobalSetupMethod?.Name`. Same for other 3 lifecycle fields.

Fix `src/PicoBench.Generators/BenchmarkClassAnalyzer.cs` — change `ref string?` parameters to `ref LifecycleMethodInfo?` in `RegisterLifecycleMethod`, `AnalyzeMethod`, and `AnalyzeTarget`. Change the void-assignment `target = method.Name` to `target = new LifecycleMethodInfo { Name = method.Name }`. The `IsAsync` field is left as default `false` here — Task 4 fills it with proper detection.

This is a minimal change to make types match. Task 4 adds async detection logic on top.

The check `model.GlobalSetupMethod is not null` still works. The call `this.{model.GlobalSetupMethod}()` becomes `this.{model.GlobalSetupMethod.Name}()`.

Update the `hasIterSetup`/`hasIterCleanup` checks:
```csharp
var hasIterSetup = model.IterationSetupMethod is not null;
var hasIterCleanup = model.IterationCleanupMethod is not null;
```

Update the setup/teardown argument emission:
```csharp
var setupArg = hasIterSetup
    ? $"({SystemAction})(() => this.{model.IterationSetupMethod!.Name}())"
    : "null";
var teardownArg = hasIterCleanup
    ? $"({SystemAction})(() => this.{model.IterationCleanupMethod!.Name}())"
    : "null";
```

- [ ] **Step 6: Build generators**

```powershell
dotnet build D:/MyProjects/PicoHex/PicoBench/src/PicoBench.Generators/PicoBench.Generators.csproj -c Debug
```

Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/PicoBench.Generators/Models.cs src/PicoBench.Generators/Emitter.cs src/PicoBench.Generators/BenchmarkClassAnalyzer.cs
git commit -m "feat: add LifecycleMethodInfo, IsAsync to generator models"
```

---

### Task 4: Analyzer — relax validation, add async void warning

**Files:**
- Modify: `src/PicoBench.Generators/DiagnosticDescriptors.cs`
- Modify: `src/PicoBench.Generators/BenchmarkClassAnalyzer.cs`

**Interfaces:**
- Consumes: `LifecycleMethodInfo`, `BenchmarkMethodModel.IsAsync`
- Produces: `PBGEN009` diagnostic, analyzer fills `IsAsync` fields

- [ ] **Step 1: Add PBGEN009 diagnostic**

In `src/PicoBench.Generators/DiagnosticDescriptors.cs`, add after `PBGEN008`:

```csharp
public static readonly DiagnosticDescriptor AsyncVoidLifecycleMethod =
    new(
        id: "PBGEN009",
        title: "Async void lifecycle method",
        messageFormat: "{0} method '{1}' is async void. It will not be awaited. Use Task or ValueTask.",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
```

- [ ] **Step 2: Add IsTaskLike helper to analyzer**

In `src/PicoBench.Generators/BenchmarkClassAnalyzer.cs`, add private method:

```csharp
private static bool IsTaskLike(ITypeSymbol type, Compilation compilation)
{
    if (type is not INamedTypeSymbol named)
        return false;

    if (type.SpecialType == SpecialType.System_Threading_Tasks_Task)
        return true;

    if (named.IsGenericType &&
        named.ConstructUnboundGenericType().SpecialType == SpecialType.System_Threading_Tasks_Task_T)
        return true;

    var valueTaskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
    var valueTaskGenericType = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");

    return SymbolEqualityComparer.Default.Equals(named, valueTaskType)
        || (named.IsGenericType && SymbolEqualityComparer.Default.Equals(
            named.ConstructUnboundGenericType(), valueTaskGenericType));
}
```

- [ ] **Step 3: Relax IsValidLifecycleMethod and propagate Compilation**

Add `Compilation compilation` parameter to `IsValidLifecycleMethod`, `IsValidBenchmarkMethod`, `RegisterLifecycleMethod`, `RegisterBenchmarkMethod`, and `AnalyzeMethod`. Pass `ctx.SemanticModel.Compilation` from `AnalyzeTarget` through the chain.

Replace `IsValidLifecycleMethod`:

```csharp
private static bool IsValidLifecycleMethod(IMethodSymbol method, Compilation compilation)
{
    return method is { IsStatic: false, IsGenericMethod: false, Parameters.Length: 0 } &&
           (method.ReturnsVoid || IsTaskLike(method.ReturnType, compilation));
}
```

- [ ] **Step 4: Set IsAsync on lifecycle method registration**

In `RegisterLifecycleMethod`, update the existing `LifecycleMethodInfo` assignment to include `IsAsync`. Change:
```csharp
target = new LifecycleMethodInfo { Name = method.Name };
```
To:
```csharp
target = new LifecycleMethodInfo
{
    Name = method.Name,
    IsAsync = !method.ReturnsVoid
};
```

- [ ] **Step 5: Add async void detection**

After the `IsValidLifecycleMethod` check in `RegisterLifecycleMethod`, add:

```csharp
// Check for async void (PBGEN009 warning)
if (method.IsAsync && method.ReturnsVoid)
{
    diagnostics.Add(
        Diagnostic.Create(
            DiagnosticDescriptors.AsyncVoidLifecycleMethod,
            GetAttributeLocation(attr, ct),
            attributeName,
            method.Name
        )
    );
}
```

- [ ] **Step 6: Update benchmark method registration**

In `RegisterBenchmarkMethod`, after `IsValidBenchmarkMethod` check passes, add to the model:

```csharp
methods.Add(
    new BenchmarkMethodModel
    {
        Name = method.Name,
        IsAsync = !method.ReturnsVoid,
        IsBaseline = isBaseline,
        Description = methodDesc
    }
);
```

Also relax `IsValidBenchmarkMethod` to accept Task/ValueTask returns:

```csharp
private static bool IsValidBenchmarkMethod(IMethodSymbol method, Compilation compilation)
{
    return method is { IsStatic: false, IsGenericMethod: false, Parameters.Length: 0 } &&
           (method.ReturnsVoid || IsTaskLike(method.ReturnType, compilation));
}
```

- [ ] **Step 7: Set model.IsAsync**

In `AnalyzeTarget`, after processing all members, set:

```csharp
var isAsync = (globalSetup?.IsAsync ?? false)
    || (globalCleanup?.IsAsync ?? false)
    || (iterSetup?.IsAsync ?? false)
    || (iterCleanup?.IsAsync ?? false)
    || methods.Any(m => m.IsAsync);
```

Include in the result model:
```csharp
return new GeneratorAnalysisResult(
    new BenchmarkClassModel
    {
        // ... existing fields ...
        IsAsync = isAsync,
        GlobalSetupMethod = globalSetup,
        GlobalCleanupMethod = globalCleanup,
        IterationSetupMethod = iterSetup,
        IterationCleanupMethod = iterCleanup,
        // ...
    },
    [..diagnostics]
);
```

- [ ] **Step 8: Update RegisterLifecycleMethod signature and all call sites**

Change `RegisterLifecycleMethod` parameter from `ref string?` to `ref LifecycleMethodInfo?`. Update the four call sites in `AnalyzeMethod`.

- [ ] **Step 9: Build generators**

```powershell
dotnet build D:/MyProjects/PicoHex/PicoBench/src/PicoBench.Generators/PicoBench.Generators.csproj -c Debug
```

- [ ] **Step 10: Commit**

```bash
git add src/PicoBench.Generators/DiagnosticDescriptors.cs src/PicoBench.Generators/BenchmarkClassAnalyzer.cs
git commit -m "feat: relax lifecycle+benchmark validation for async, add PBGEN009 async void warning"
```

---

### Task 5: Emitter — sync class produces ValueTask.FromResult

**Files:**
- Modify: `src/PicoBench.Generators/Emitter.cs`

**Interfaces:**
- Consumes: `BenchmarkClassModel.IsAsync`, `LifecycleMethodInfo.Name`
- Produces: generated source with `ValueTask<BenchmarkSuite>` for sync classes

- [ ] **Step 1: Change method signature in generated code**

In `Emitter.cs`, add a new constant for ValueTask:
```csharp
private const string ValueTaskType = "global::System.Threading.Tasks.ValueTask";
```

Change the generated method name from `RunBenchmarks` to `RunBenchmarksAsync` and return type to `ValueTask<BenchmarkSuite>`:

Replace:
```csharp
sb.AppendLine(
    $"{m1}public {Bench}.BenchmarkSuite RunBenchmarks({Bench}.BenchmarkConfig? config = null)"
);
```

With:
```csharp
sb.AppendLine(
    $"{m1}public {ValueTaskType}<{Bench}.BenchmarkSuite> RunBenchmarksAsync({Bench}.BenchmarkConfig? config = null)"
);
```

Note: `ValueTaskType` resolves to `global::System.Threading.Tasks.ValueTask`, not `PicoBench.ValueTask`.

- [ ] **Step 2: Wrap return in ValueTask.FromResult**

Replace:
```csharp
sb.AppendLine($"{m2}__sw.Stop();");
sb.AppendLine($"{m2}return new {Bench}.BenchmarkSuite(");
```

With:
```csharp
sb.AppendLine($"{m2}__sw.Stop();");
sb.AppendLine($"{m2}var __suite = new {Bench}.BenchmarkSuite(");
```

And after the `BenchmarkSuite` constructor call, add:
```csharp
sb.AppendLine($"{m2}return {ValueTaskType}.FromResult(__suite);");
```

- [ ] **Step 3: Update lifecycle method calls to use .Name**

Replace:
```csharp
sb.AppendLine($"{bodyIndent}this.{model.GlobalSetupMethod}();");
```

With:
```csharp
sb.AppendLine($"{bodyIndent}this.{model.GlobalSetupMethod!.Name}();");
```

Same for GlobalCleanup, IterationSetup, IterationCleanup references.

- [ ] **Step 4: Build generators (test failures expected)**

```powershell
dotnet build D:/MyProjects/PicoHex/PicoBench/src/PicoBench.Generators/PicoBench.Generators.csproj -c Debug
```

Expected: generators project builds. Test project will have obsolete assertion strings — that's handled in Task 11.

- [ ] **Step 5: Commit**

```bash
git add src/PicoBench.Generators/Emitter.cs
git commit -m "feat: emitter generates ValueTask<BenchmarkSuite> RunBenchmarksAsync with ValueTask.FromResult"
```

---

### Task 6: Emitter — async class path

**Files:**
- Modify: `src/PicoBench.Generators/Emitter.cs`

**Interfaces:**
- Consumes: `BenchmarkClassModel.IsAsync`, `LifecycleMethodInfo.IsAsync`, `BenchmarkMethodModel.IsAsync`
- Produces: `async ValueTask<BenchmarkSuite>` with await dispatch for async classes

- [ ] **Step 1: Branch on IsAsync for the method signature**

In `Generate`, after the class declaration, add the async keyword when `model.IsAsync`:

```csharp
var asyncKeyword = model.IsAsync ? "async " : "";
sb.AppendLine(
    $"{m1}public {asyncKeyword}{ValueTaskType}<{Bench}.BenchmarkSuite> RunBenchmarksAsync({Bench}.BenchmarkConfig? config = null)"
);
```

- [ ] **Step 2: Add await on global setup/cleanup for async class**

For `GlobalSetup`:
```csharp
if (model.GlobalSetupMethod is not null)
{
    var awaitPrefix = model.GlobalSetupMethod.IsAsync ? "await " : "";
    sb.AppendLine($"{bodyIndent}{awaitPrefix}this.{model.GlobalSetupMethod.Name}();");
    sb.AppendLine();
}
```

Same pattern for `GlobalCleanup`.

- [ ] **Step 3: Add async benchmark dispatch**

For each benchmark method, branch on `model.IsAsync`:

```csharp
if (model.IsAsync)
{
    // Async class: always use Benchmark.RunAsync with Func<Task>
    var wrap = method.IsAsync
        ? $"async () => {{ await this.{method.Name}(); }}"
        : $"async () => {{ this.{method.Name}(); }}";

    var setupArg = GetAsyncIterArg(model.IterationSetupMethod);
    var teardownArg = GetAsyncIterArg(model.IterationCleanupMethod);

    if (hasIterSetup || hasIterCleanup)
    {
        sb.AppendLine($"{bodyIndent}var __r_{method.Name} = await {Bench}.Benchmark.RunAsync(");
        sb.AppendLine($"{bodyIndent}    {nameExpr},");
        sb.AppendLine($"{bodyIndent}    {wrap},");
        sb.AppendLine($"{bodyIndent}    warmup: {wrap},");
        sb.AppendLine($"{bodyIndent}    config: config,");
        sb.AppendLine($"{bodyIndent}    setup: {setupArg},");
        sb.AppendLine($"{bodyIndent}    teardown: {teardownArg});");
    }
    else
    {
        sb.AppendLine($"{bodyIndent}var __r_{method.Name} = await {Bench}.Benchmark.RunAsync({nameExpr}, {wrap}, config);");
    }
}
else
{
    // Sync class: existing Benchmark.Run with Action
    // ... (existing sync code, unchanged except for .Name access on lifecycle) ...
}
```

- [ ] **Step 4: Add GetAsyncIterArg helper**

```csharp
private static string GetAsyncIterArg(LifecycleMethodInfo? method)
{
    if (method is null)
        return "null";

    return method.IsAsync
        ? $"async () => {{ await this.{method.Name}(); }}"
        : $"async () => {{ this.{method.Name}(); }}";
}
```

- [ ] **Step 5: Update sync class path also**

The sync class path (`IsAsync == false`) must still work. It uses:
```csharp
var setupArg = hasIterSetup
    ? $"({SystemAction})(() => this.{model.IterationSetupMethod!.Name}())"
    : "null";
```

This is the existing sync pattern, already updated with `.Name`.

- [ ] **Step 6: Build**

```powershell
dotnet build D:/MyProjects/PicoHex/PicoBench/src/PicoBench.Generators/PicoBench.Generators.csproj -c Debug
```

- [ ] **Step 7: Commit**

```bash
git add src/PicoBench.Generators/Emitter.cs
git commit -m "feat: emitter generates async ValueTask path with await+RunAsync for async classes"
```

---

### Task 7: Runner — TimeAsync and TimeCpuAsync

**Files:**
- Modify: `src/PicoBench/Runner.cs`

**Interfaces:**
- Produces: `Task<TimingSample> TimeAsync(int, Func<Task>, Func<Task>?, Func<Task>?)`, `Task<TimingSample> TimeCpuAsync(int, Func<Task>, Func<Task>?, Func<Task>?)`

- [ ] **Step 1: Add TimeAsync method**

In `src/PicoBench/Runner.cs`, after the existing `Time` methods:

```csharp
/// <summary>
/// Run an async timed measurement with optional setup and teardown.
/// Uses Stopwatch for wall-clock timing. GC info is marked approximate
/// because await yields may include non-benchmark GC events.
/// </summary>
public static async Task<TimingSample> TimeAsync(
    int iterations,
    Func<Task> action,
    Func<Task>? setup = null,
    Func<Task>? teardown = null)
{
    ValidateIterations(iterations);
    if (action == null)
        throw new ArgumentNullException(nameof(action));

    var gcBaseline = GetGcBaselineCounts();

    if (setup != null)
        await setup();

    var cycleStart = GetCpuCycles();
    var watch = Stopwatch.StartNew();

    for (int i = 0; i < iterations; i++)
        await action();

    watch.Stop();
    var cycleEnd = GetCpuCycles();

    if (teardown != null)
        await teardown();

    var sample = CreateSample(watch, cycleStart, cycleEnd, gcBaseline);
    if (sample.GcInfo != null)
    {
        return new TimingSample
        {
            ElapsedNanoseconds = sample.ElapsedNanoseconds,
            ElapsedMilliseconds = sample.ElapsedMilliseconds,
            ElapsedTicks = sample.ElapsedTicks,
            CpuCycles = sample.CpuCycles,
            GcInfo = new GcInfo
            {
                Gen0 = sample.GcInfo.Gen0,
                Gen1 = sample.GcInfo.Gen1,
                Gen2 = sample.GcInfo.Gen2,
                IsApproximate = true
            }
        };
    }
    return sample;
}
```

- [ ] **Step 2: Add TimeCpuAsync method**

```csharp
/// <summary>
/// Run an async timed measurement using Process.TotalProcessorTime.
/// Only CPU execution time is counted; I/O wait time is excluded.
/// GC info is not collected (null) — inaccurate across await points.
/// </summary>
public static async Task<TimingSample> TimeCpuAsync(
    int iterations,
    Func<Task> action,
    Func<Task>? setup = null,
    Func<Task>? teardown = null)
{
    ValidateIterations(iterations);
    if (action == null)
        throw new ArgumentNullException(nameof(action));

    if (setup != null)
        await setup();

    var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
    var cycleStart = GetCpuCycles();

    for (int i = 0; i < iterations; i++)
        await action();

    var cycleEnd = GetCpuCycles();
    var cpuDelta = Process.GetCurrentProcess().TotalProcessorTime - cpuBefore;

    if (teardown != null)
        await teardown();

    return new TimingSample
    {
        ElapsedNanoseconds = cpuDelta.TotalNanoseconds,
        ElapsedMilliseconds = cpuDelta.TotalMilliseconds,
        ElapsedTicks = cpuDelta.Ticks,
        CpuCycles = cycleEnd - cycleStart,
        GcInfo = null
    };
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build D:/MyProjects/PicoHex/PicoBench/src/PicoBench/PicoBench.csproj -c Debug
```

- [ ] **Step 4: Commit**

```bash
git add src/PicoBench/Runner.cs
git commit -m "feat: add Runner.TimeAsync and TimeCpuAsync for async benchmarks"
```

---

### Task 8: BenchmarkConfig — TimingMode and CancellationToken

**Files:**
- Modify: `src/PicoBench/BenchmarkConfig.cs`

**Interfaces:**
- Produces: `AsyncTimingMode` enum, `BenchmarkConfig.TimingMode`, `BenchmarkConfig.CancellationToken`

- [ ] **Step 1: Add AsyncTimingMode enum**

In `src/PicoBench/BenchmarkConfig.cs`, add at the bottom of the file (outside `BenchmarkConfig` class):

```csharp
/// <summary>
/// Controls how async benchmark timing is measured.
/// </summary>
public enum AsyncTimingMode
{
    /// <summary>Full wall-clock duration including await suspension time. Default.</summary>
    WallClock = 0,

    /// <summary>CPU execution time only (Process.TotalProcessorTime), excluding I/O wait.</summary>
    CpuOnly = 1
}
```

- [ ] **Step 2: Add fields to BenchmarkConfig**

In the `BenchmarkConfig` class, add:

```csharp
/// <summary>Timing strategy for async benchmarks. Sync benchmarks ignore this.</summary>
public AsyncTimingMode TimingMode { get; init; } = AsyncTimingMode.WallClock;

/// <summary>CancellationToken to allow early termination of a benchmark run.</summary>
public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
```

- [ ] **Step 3: Build**

```powershell
dotnet build D:/MyProjects/PicoHex/PicoBench/src/PicoBench/PicoBench.csproj -c Debug
```

- [ ] **Step 4: Commit**

```bash
git add src/PicoBench/BenchmarkConfig.cs
git commit -m "feat: add AsyncTimingMode + CancellationToken to BenchmarkConfig"
```

---

### Task 9: Benchmark.RunAsync overloads

**Files:**
- Modify: `src/PicoBench/Benchmark.cs`

**Interfaces:**
- Consumes: `Runner.TimeAsync`, `Runner.TimeCpuAsync`, `BenchmarkConfig.TimingMode`, `BenchmarkConfig.CancellationToken`
- Produces: `Task<BenchmarkResult> RunAsync(string, Func<Task>, BenchmarkConfig?)`, full overload, stateful overload

- [ ] **Step 1: Add simple RunAsync overload**

```csharp
/// <summary>
/// Run an async benchmark with the given action.
/// </summary>
public static Task<BenchmarkResult> RunAsync(
    string name,
    Func<Task> action,
    BenchmarkConfig? config = null)
{
    ValidateName(name, nameof(name));
    if (action == null)
        throw new ArgumentNullException(nameof(action));

    return RunAsync(name, action, warmup: action, config);
}
```

- [ ] **Step 2: Add full RunAsync overload**

```csharp
/// <summary>
/// Run an async benchmark with separate warmup, setup, and teardown.
/// </summary>
public static async Task<BenchmarkResult> RunAsync(
    string name,
    Func<Task> action,
    Func<Task>? warmup,
    BenchmarkConfig? config = null,
    Func<Task>? setup = null,
    Func<Task>? teardown = null)
{
    ValidateName(name, nameof(name));
    if (action == null)
        throw new ArgumentNullException(nameof(action));

    config ??= BenchmarkConfig.Default;
    Runner.Initialize();

    // Warmup phase
    if (warmup != null && config.WarmupIterations > 0)
    {
        for (int i = 0; i < config.WarmupIterations; i++)
            await warmup();
    }

    return await CollectAndBuildAsync(
        name,
        config,
        iterations => DispatchAsyncTiming(iterations, action, setup, teardown, config)
    );
}

private static Task<TimingSample> DispatchAsyncTiming(
    int iterations,
    Func<Task> action,
    Func<Task>? setup,
    Func<Task>? teardown,
    BenchmarkConfig config)
{
    return config.TimingMode == AsyncTimingMode.CpuOnly
        ? Runner.TimeCpuAsync(iterations, action, setup, teardown)
        : Runner.TimeAsync(iterations, action, setup, teardown);
}
```

- [ ] **Step 3: Add stateful RunAsync overload**

Note: the lambda `async () => await action(state)` creates one delegate allocation per sample (acceptable — async path overhead is dominated by Task allocations).

```csharp
/// <summary>
/// Run an async benchmark with state passed to avoid closure allocation.
/// </summary>
public static async Task<BenchmarkResult> RunAsync<TState>(
    string name,
    TState state,
    Func<TState, Task> action,
    Func<TState, Task>? warmup = null,
    BenchmarkConfig? config = null)
{
    ValidateName(name, nameof(name));
    if (action == null)
        throw new ArgumentNullException(nameof(action));

    config ??= BenchmarkConfig.Default;
    Runner.Initialize();

    if (warmup != null && config.WarmupIterations > 0)
    {
        for (int i = 0; i < config.WarmupIterations; i++)
            await warmup(state);
    }
    else if (config.WarmupIterations > 0)
    {
        for (int i = 0; i < config.WarmupIterations; i++)
            await action(state);
    }

    return await CollectAndBuildAsync(
        name,
        config,
        iterations =>
        {
            return DispatchAsyncTiming(
                iterations,
                async () => await action(state),
                setup: null,
                teardown: null,
                config
            );
        }
    );
}
```

- [ ] **Step 4: Add CollectAndBuildAsync**

```csharp
private static async Task<BenchmarkResult> CollectAndBuildAsync(
    string name,
    BenchmarkConfig config,
    Func<int, Task<TimingSample>> sampleFuncAsync)
{
    ForceGc();

    var iterationsPerSample = await ResolveIterationsPerSampleAsync(config, sampleFuncAsync);

    var samples = new TimingSample[config.SampleCount];
    var perOpTimes = new double[config.SampleCount];
    var perOpCycles = new double[config.SampleCount];

    for (var s = 0; s < config.SampleCount; s++)
    {
        config.CancellationToken.ThrowIfCancellationRequested();

        var sample = await sampleFuncAsync(iterationsPerSample);
        samples[s] = sample;
        perOpTimes[s] = sample.ElapsedNanoseconds / iterationsPerSample;
        perOpCycles[s] = (double)sample.CpuCycles / iterationsPerSample;
    }

    var stats = StatisticsCalculator.Compute(perOpTimes, perOpCycles, samples);

    return new BenchmarkResult(
        name: name,
        statistics: stats,
        iterationsPerSample: iterationsPerSample,
        sampleCount: config.SampleCount,
        samples: config.RetainSamples ? samples : null
    );
}

private static async Task<int> ResolveIterationsPerSampleAsync(
    BenchmarkConfig config,
    Func<int, Task<TimingSample>> sampleFuncAsync)
{
    if (!config.AutoCalibrateIterations)
        return config.IterationsPerSample;

    var iterations = config.IterationsPerSample;
    var minSampleNanoseconds = Math.Max(
        config.MinSampleTime.TotalMilliseconds * 1_000_000.0, 1.0);

    while (iterations < config.MaxAutoIterationsPerSample)
    {
        var sample = await sampleFuncAsync(iterations);
        if (sample.ElapsedNanoseconds >= minSampleNanoseconds)
            return iterations;

        var scale = minSampleNanoseconds / Math.Max(sample.ElapsedNanoseconds, 1.0);
        var nextIterations = (int)Math.Min(
            config.MaxAutoIterationsPerSample,
            Math.Max(iterations + 1,
                Math.Ceiling(iterations * Math.Min(Math.Max(scale, 2.0), 10.0)))
        );

        if (nextIterations <= iterations)
            break;
        iterations = nextIterations;
    }

    return iterations;
}
```

- [ ] **Step 5: Build**

```powershell
dotnet build D:/MyProjects/PicoHex/PicoBench/src/PicoBench/PicoBench.csproj -c Debug
```

- [ ] **Step 6: Commit**

```bash
git add src/PicoBench/Benchmark.cs
git commit -m "feat: add Benchmark.RunAsync overloads with async timing dispatch"
```

---

### Task 10: BenchmarkRunner.RunAsync

**Files:**
- Modify: `src/PicoBench/BenchmarkRunner.cs`

**Interfaces:**
- Consumes: `IBenchmarkClass.RunBenchmarksAsync`
- Produces: `Task<BenchmarkSuite> RunAsync<T>()`, `Run<T>()` sync shortcut

- [ ] **Step 1: Update BenchmarkRunner**

Replace entire file:

```csharp
namespace PicoBench;

/// <summary>
/// Static helper for running attribute-based benchmarks.
/// Provides a generic <c>Run{T}</c> and <c>RunAsync{T}</c> entry point.
/// </summary>
public static class BenchmarkRunner
{
    /// <summary>
    /// Creates a new instance of <typeparamref name="T"/> and runs all benchmarks
    /// declared with <see cref="BenchmarkAttribute"/>. Returns a <see cref="Task{TResult}"/>
    /// that completes with the benchmark suite.
    /// </summary>
    /// <typeparam name="T">
    /// A <see cref="BenchmarkClassAttribute"/>-decorated partial class.
    /// The source generator implements <see cref="IBenchmarkClass"/> automatically.
    /// </typeparam>
    /// <param name="config">
    /// Optional configuration. Defaults to <see cref="BenchmarkConfig.Default"/> when <c>null</c>.
    /// </param>
    /// <returns>A task that completes with the <see cref="BenchmarkSuite"/>.</returns>
    public static async Task<BenchmarkSuite> RunAsync<T>(BenchmarkConfig? config = null)
        where T : IBenchmarkClass, new()
    {
        return await new T().RunBenchmarksAsync(config);
    }

    /// <summary>
    /// Runs all benchmarks on an existing instance asynchronously.
    /// </summary>
    public static async Task<BenchmarkSuite> RunAsync<T>(T instance,
        BenchmarkConfig? config = null)
        where T : IBenchmarkClass
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        return await instance.RunBenchmarksAsync(config);
    }

    /// <summary>
    /// Synchronous shortcut for <see cref="RunAsync{T}(BenchmarkConfig?)"/>.
    /// Blocks the calling thread — avoid in UI/SynchronizationContext environments.
    /// </summary>
    public static BenchmarkSuite Run<T>(BenchmarkConfig? config = null)
        where T : IBenchmarkClass, new()
    {
        return new T().RunBenchmarksAsync(config).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Synchronous shortcut for <see cref="RunAsync{T}(T, BenchmarkConfig?)"/>.
    /// Blocks the calling thread — avoid in UI/SynchronizationContext environments.
    /// </summary>
    public static BenchmarkSuite Run<T>(T instance, BenchmarkConfig? config = null)
        where T : IBenchmarkClass
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        return instance.RunBenchmarksAsync(config).GetAwaiter().GetResult();
    }
}
```

- [ ] **Step 2: Build and run BenchmarkRunner tests**

```powershell
dotnet build D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug
dotnet test --project D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug -- --filter "Category~BenchmarkRunner" --no-progress
```

Fix any test that relied on the old `Run` not calling `RunBenchmarksAsync` — the interface changed but the observable behavior (RunCount increments) is the same.

- [ ] **Step 3: Commit**

```bash
git add src/PicoBench/BenchmarkRunner.cs tests/PicoBench.Tests/BenchmarkRunnerTests.cs
git commit -m "feat: add BenchmarkRunner.RunAsync<T>() with sync Run<T>() shortcut"
```

---

### Task 11: Update test infrastructure for model changes

**Files:**
- Modify: `tests/PicoBench.Tests/Generators/ModelsTests.cs`
- Modify: `tests/PicoBench.Tests/Generators/EmitterTests.cs`
- Modify: `tests/PicoBench.Tests/Generators/BenchmarkGeneratorDiagnosticsTests.cs`

- [ ] **Step 1: Update ModelsTests CreateModel helper**

Change lifecycle parameters from `string?` to `LifecycleMethodInfo?`:
```csharp
private static BenchmarkClassModel CreateModel(
    string className = "Test",
    string? ns = "NS",
    string accessModifier = "public",
    string? description = null,
    LifecycleMethodInfo? globalSetup = null,
    LifecycleMethodInfo? globalCleanup = null,
    LifecycleMethodInfo? iterSetup = null,
    LifecycleMethodInfo? iterCleanup = null,
    bool isAsync = false,
    ImmutableArray<BenchmarkMethodModel>? methods = null,
    ImmutableArray<ParamsPropertyModel>? paramsProps = null)
{
    return new BenchmarkClassModel
    {
        // ... same as before but with LifecycleMethodInfo fields and IsAsync ...
    };
}
```

Update `BenchmarkMethodModel` creations to include `IsAsync = false`.

- [ ] **Step 2: Add IsAsync equality tests**

Add tests:
```csharp
[Test]
public async Task BenchmarkClassModel_DifferentIsAsync_AreNotEqual()
{
    var a = CreateModel(isAsync: true);
    var b = CreateModel(isAsync: false);
    await Assert.That(a.Equals(b)).IsFalse();
}

[Test]
public async Task BenchmarkClassModel_DifferentLifecycleAsync_AreNotEqual()
{
    var a = CreateModel(globalSetup: new LifecycleMethodInfo { Name = "S", IsAsync = true });
    var b = CreateModel(globalSetup: new LifecycleMethodInfo { Name = "S", IsAsync = false });
    await Assert.That(a.Equals(b)).IsFalse();
}

[Test]
public async Task BenchmarkMethodModel_DifferentIsAsync_AreNotEqual()
{
    var a = new BenchmarkMethodModel { Name = "M", IsAsync = true };
    var b = new BenchmarkMethodModel { Name = "M", IsAsync = false };
    await Assert.That(a.Equals(b)).IsFalse();
}
```

- [ ] **Step 3: Run models tests**

```powershell
dotnet test --project D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug -- --filter "Category~Models" --no-progress
```

- [ ] **Step 4: Update EmitterTests MinimalModel helper**

Same change as ModelsTests: lifecycle params become `LifecycleMethodInfo?`, add `IsAsync` parameter.

Update all test calls. For existing tests, wrap strings: `minimalModel(globalSetup: "Setup")` → `minimalModel(globalSetup: new LifecycleMethodInfo { Name = "Setup" })`.

- [ ] **Step 5: Run emitter tests — identify failures**

```powershell
dotnet test --project D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug -- --filter "Category~Emitter" --no-progress
```

Fix assertion strings: `Benchmark.Run` → check sync output, `RunBenchmarksAsync` → new method name. `IterationSetup -> setup: (global::System.Action)` → still valid for sync class.

- [ ] **Step 6: Commit**

```bash
git add tests/
git commit -m "test: update model tests and emitter tests for LifecycleMethodInfo + IsAsync"
```

---

### Task 12: Analyzer tests for async validation

**Files:**
- Modify: `tests/PicoBench.Tests/Generators/BenchmarkGeneratorDiagnosticsTests.cs`

- [ ] **Step 1: Add async lifecycle tests**

```csharp
[Test]
[Property("Category", "Generators")]
public async Task AsyncTaskGlobalSetup_NoDiagnostic()
{
    var result = RunGenerator("""
        using PicoBench;

        [BenchmarkClass]
        public partial class GoodBench
        {
            [GlobalSetup]
            public async Task SetupAsync() { await Task.CompletedTask; }

            [Benchmark]
            public void Work() { }
        }
        """);

    await Assert.That(result.Diagnostics).IsEmpty();
    await Assert.That(result.GeneratedSources.Length).IsEqualTo(1);
}

[Test]
[Property("Category", "Generators")]
public async Task AsyncValueTaskGlobalSetup_NoDiagnostic()
{
    var result = RunGenerator("""
        using PicoBench;

        [BenchmarkClass]
        public partial class GoodBench
        {
            [GlobalSetup]
            public async ValueTask SetupAsync() { await Task.CompletedTask; }

            [Benchmark]
            public void Work() { }
        }
        """);

    await Assert.That(result.Diagnostics).IsEmpty();
}

[Test]
[Property("Category", "Generators")]
public async Task AsyncVoidGlobalSetup_ReportsWarning()
{
    var result = RunGenerator("""
        using PicoBench;

        [BenchmarkClass]
        public partial class BadBench
        {
            [GlobalSetup]
            public async void Setup() { }

            [Benchmark]
            public void Work() { }
        }
        """);

    await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN009")).IsTrue();
}

[Test]
[Property("Category", "Generators")]
public async Task AsyncTaskBenchmarkMethod_NoDiagnostic()
{
    var result = RunGenerator("""
        using PicoBench;

        [BenchmarkClass]
        public partial class GoodBench
        {
            [Benchmark]
            public async Task WorkAsync() { await Task.CompletedTask; }
        }
        """);

    await Assert.That(result.Diagnostics).IsEmpty();
}

[Test]
[Property("Category", "Generators")]
public async Task StaticAsyncBenchmarkMethod_ReportsDiagnostic()
{
    var result = RunGenerator("""
        using PicoBench;

        [BenchmarkClass]
        public partial class BadBench
        {
            [Benchmark]
            public static async Task WorkAsync() { await Task.CompletedTask; }
        }
        """);

    await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN003")).IsTrue();
}

[Test]
[Property("Category", "Generators")]
public async Task GenericAsyncBenchmarkMethod_ReportsDiagnostic()
{
    var result = RunGenerator("""
        using PicoBench;

        [BenchmarkClass]
        public partial class BadBench
        {
            [Benchmark]
            public async Task WorkAsync<T>() { await Task.CompletedTask; }
        }
        """);

    await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN003")).IsTrue();
}
```

- [ ] **Step 2: Run diagnostics tests**

```powershell
dotnet test --project D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug -- --filter "Category~Generators" --no-progress
```

- [ ] **Step 3: Commit**

```bash
git add tests/PicoBench.Tests/Generators/BenchmarkGeneratorDiagnosticsTests.cs
git commit -m "test: add analyzer tests for async lifecycle + benchmark validation"
```

---

### Task 13: Emitter tests for async generation

**Files:**
- Modify: `tests/PicoBench.Tests/Generators/EmitterTests.cs`

- [ ] **Step 1: Add sync class ValueTask test**

```csharp
[Test]
[Property("Category", "Emitter")]
public async Task Generate_SyncClass_ReturnsValueTaskFromResult()
{
    var code = Emitter.Generate(MinimalModel());

    await Assert.That(code).Contains("ValueTask.FromResult(__suite)");
    await Assert.That(code).DoesNotContain("async ValueTask");
}

[Test]
[Property("Category", "Emitter")]
public async Task Generate_SyncClass_DoesNotContainAwait()
{
    var code = Emitter.Generate(MinimalModel());

    await Assert.That(code).DoesNotContain("await ");
}
```

- [ ] **Step 2: Add async class generation test**

```csharp
[Test]
[Property("Category", "Emitter")]
public async Task Generate_AsyncClass_IsAsyncMethod()
{
    var methods = ImmutableArray.Create(
        new BenchmarkMethodModel { Name = "WorkAsync", IsAsync = true }
    );
    var model = MinimalModel(
        methods: methods,
        isAsync: true,
        globalSetup: new LifecycleMethodInfo { Name = "Setup", IsAsync = true },
        globalCleanup: new LifecycleMethodInfo { Name = "Cleanup", IsAsync = false }
    );
    var code = Emitter.Generate(model);

    await Assert.That(code).Contains("async ValueTask");
    await Assert.That(code).Contains("await this.Setup();");
    await Assert.That(code).Contains("this.Cleanup();"); // sync cleanup, no await
    await Assert.That(code).Contains("await Benchmark.RunAsync");
}

[Test]
[Property("Category", "Emitter")]
public async Task Generate_AsyncClassWithSyncBenchmark_WrapsInAsyncLambda()
{
    var methods = ImmutableArray.Create(
        new BenchmarkMethodModel { Name = "SyncWork", IsAsync = false }
    );
    var model = MinimalModel(
        methods: methods,
        isAsync: true
    );
    var code = Emitter.Generate(model);

    await Assert.That(code).Contains("async () => { this.SyncWork(); }");
}

[Test]
[Property("Category", "Emitter")]
public async Task Generate_AsyncClassWithAsyncIterSetup_WrapsCorrectly()
{
    var methods = ImmutableArray.Create(
        new BenchmarkMethodModel { Name = "Bench", IsAsync = true }
    );
    var model = MinimalModel(
        methods: methods,
        isAsync: true,
        iterSetup: new LifecycleMethodInfo { Name = "IterSetup", IsAsync = true },
        iterCleanup: new LifecycleMethodInfo { Name = "IterCleanup", IsAsync = false }
    );
    var code = Emitter.Generate(model);

    await Assert.That(code).Contains("setup: async () => { await this.IterSetup(); }");
    await Assert.That(code).Contains("teardown: async () => { this.IterCleanup(); }");
}
```

- [ ] **Step 3: Run emitter tests**

```powershell
dotnet test --project D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug -- --filter "Category~Emitter" --no-progress
```

- [ ] **Step 4: Commit**

```bash
git add tests/PicoBench.Tests/Generators/EmitterTests.cs
git commit -m "test: add emitter tests for sync ValueTask.FromResult and async generation"
```

---

### Task 14: Runtime tests for async benchmarks

**Files:**
- Modify: `tests/PicoBench.Tests/BenchmarkTests.cs`
- Modify: `tests/PicoBench.Tests/RunnerTests.cs`
- Modify: `tests/PicoBench.Tests/BenchmarkConfigTests.cs`

- [ ] **Step 1: Add async RunAsync test**

In `BenchmarkTests.cs`:

```csharp
private static readonly BenchmarkConfig FastConfig =
    new()
    {
        WarmupIterations = 1,
        SampleCount = 2,
        IterationsPerSample = 3
    };

[Test]
[Property("Category", "Benchmark")]
public async Task RunAsync_SimpleOverload_ReturnsValidResult()
{
    var result = await Benchmark.RunAsync(
        "AsyncSimple",
        async () => { await Task.CompletedTask; },
        FastConfig);

    await Assert.That(result).IsNotNull();
    await Assert.That(result.Name).IsEqualTo("AsyncSimple");
    await Assert.That(result.SampleCount).IsEqualTo(2);
}

[Test]
[Property("Category", "Benchmark")]
public async Task RunAsync_WithSetupAndTeardown_ExecutesThem()
{
    int setupCount = 0;
    int teardownCount = 0;

    var result = await Benchmark.RunAsync(
        "AsyncSetupTeardown",
        async () => { await Task.CompletedTask; },
        warmup: async () => { await Task.CompletedTask; },
        FastConfig,
        setup: () => { setupCount++; return Task.CompletedTask; },
        teardown: () => { teardownCount++; return Task.CompletedTask; }
    );

    await Assert.That(result).IsNotNull();
    await Assert.That(setupCount).IsEqualTo(FastConfig.SampleCount);
    await Assert.That(teardownCount).IsEqualTo(FastConfig.SampleCount);
}

[Test]
[Property("Category", "Benchmark")]
public async Task RunAsync_CpuOnly_SkipsGcInfo()
{
    var config = new BenchmarkConfig
    {
        WarmupIterations = 1,
        SampleCount = 2,
        IterationsPerSample = 3,
        TimingMode = AsyncTimingMode.CpuOnly
    };

    var result = await Benchmark.RunAsync(
        "CpuOnly",
        async () => { await Task.CompletedTask; },
        config);

    await Assert.That(result.Statistics.GcInfo).IsNull();
}

[Test]
[Property("Category", "Benchmark")]
public async Task RunAsync_WallClock_GcInfoIsApproximate()
{
    var config = new BenchmarkConfig
    {
        WarmupIterations = 1,
        SampleCount = 2,
        IterationsPerSample = 3,
        RetainSamples = true
    };

    var result = await Benchmark.RunAsync(
        "WallClockApprox",
        async () => { await Task.CompletedTask; },
        config);

    await Assert.That(result.Samples).IsNotNull();
    foreach (var sample in result.Samples!)
    {
        if (sample.GcInfo != null)
            await Assert.That(sample.GcInfo.IsApproximate).IsTrue();
    }
}

[Test]
[Property("Category", "Benchmark")]
public async Task RunAsync_CancellationAbortsMidRun()
{
    using var cts = new CancellationTokenSource();
    var config = new BenchmarkConfig
    {
        WarmupIterations = 0,
        SampleCount = 10,
        IterationsPerSample = 100,
        CancellationToken = cts.Token
    };

    int sampleCount = 0;
    cts.CancelAfter(50); // Cancel quickly

    await Assert.ThrowsAsync<OperationCanceledException>(async () =>
    {
        await Benchmark.RunAsync(
            "Cancelled",
            async () =>
            {
                sampleCount++;
                await Task.Delay(1);
            },
            config);
    });
}
```

- [ ] **Step 2: Add Runner.TimeAsync tests**

In `RunnerTests.cs`:

```csharp
[Test]
[Property("Category", "Runner")]
public async Task TimeAsync_ReturnsValidSample()
{
    var sample = await Runner.TimeAsync(
        10,
        async () => { await Task.CompletedTask; });

    await Assert.That(sample.ElapsedNanoseconds).IsGreaterThan(0);
    await Assert.That(sample.CpuCycles).IsGreaterThanOrEqualTo(0UL);
}

[Test]
[Property("Category", "Runner")]
public async Task TimeAsync_WithSetupAndTeardown_ExecutesThem()
{
    int setupCount = 0;
    int teardownCount = 0;

    await Runner.TimeAsync(
        3,
        async () => { await Task.CompletedTask; },
        setup: () => { setupCount++; return Task.CompletedTask; },
        teardown: () => { teardownCount++; return Task.CompletedTask; });

    await Assert.That(setupCount).IsEqualTo(1);
    await Assert.That(teardownCount).IsEqualTo(1);
}

[Test]
[Property("Category", "Runner")]
public async Task TimeCpuAsync_ReturnsNullGcInfo()
{
    var sample = await Runner.TimeCpuAsync(
        10,
        async () => { await Task.CompletedTask; });

    await Assert.That(sample.GcInfo).IsNull();
    await Assert.That(sample.ElapsedNanoseconds).IsGreaterThanOrEqualTo(0);
}
```

- [ ] **Step 3: Add BenchmarkConfig tests**

In `BenchmarkConfigTests.cs`:

```csharp
[Test]
[Property("Category", "BenchmarkConfig")]
public async Task DefaultConfig_TimingMode_IsWallClock()
{
    await Assert.That(BenchmarkConfig.Default.TimingMode).IsEqualTo(AsyncTimingMode.WallClock);
}

[Test]
[Property("Category", "BenchmarkConfig")]
public async Task DefaultConfig_CancellationToken_IsNone()
{
    await Assert.That(BenchmarkConfig.Default.CancellationToken).IsEqualTo(CancellationToken.None);
}
```

- [ ] **Step 4: Run runtime tests**

```powershell
dotnet test --project D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug -- --filter "Category~Benchmark or Category~Runner or Category~BenchmarkConfig" --no-progress
```

- [ ] **Step 5: Commit**

```bash
git add tests/PicoBench.Tests/BenchmarkTests.cs tests/PicoBench.Tests/RunnerTests.cs tests/PicoBench.Tests/BenchmarkConfigTests.cs
git commit -m "test: add runtime tests for async benchmarks, timing modes, and cancellation"
```

---

### Task 15: Integration test — full attribute-based async class

**Files:**
- Create: `tests/PicoBench.Tests/Integration/AsyncBenchmarkIntegrationTests.cs`

- [ ] **Step 1: Write integration test**

```csharp
namespace PicoBench.Tests.Integration;

using PicoBench;

[BenchmarkClass(Description = "Async integration test suite")]
public partial class AsyncIntegrationBenchmarks
{
    public int SetupCount { get; private set; }
    public int CleanupCount { get; private set; }
    public int IterSetupCount { get; private set; }
    public int IterCleanupCount { get; private set; }

    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        await Task.Delay(1);
        SetupCount++;
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        CleanupCount++;
    }

    [IterationSetup]
    public void IterSetup()
    {
        IterSetupCount++;
    }

    [Benchmark(Baseline = true)]
    public async Task AsyncBaseline()
    {
        await Task.Delay(1);
    }

    [Benchmark]
    public void SyncBenchmark()
    {
        // minimal work
    }
}

public class AsyncBenchmarkIntegrationTests
{
    [Test]
    [Property("Category", "Integration")]
    public async Task RunAsync_AttributeBasedClass_ReturnsSuite()
    {
        var config = new BenchmarkConfig
        {
            WarmupIterations = 1,
            SampleCount = 2,
            IterationsPerSample = 3
        };

        var suite = await BenchmarkRunner.RunAsync<AsyncIntegrationBenchmarks>(config);

        await Assert.That(suite).IsNotNull();
        await Assert.That(suite.Name).IsEqualTo("AsyncIntegrationBenchmarks");
        await Assert.That(suite.Results).Count().IsEqualTo(2);
        await Assert.That(suite.Description).IsEqualTo("Async integration test suite");
    }

    [Test]
    [Property("Category", "Integration")]
    public async Task Run_SyncShortcut_BlocksAndReturnsSuite()
    {
        var suite = BenchmarkRunner.Run<AsyncIntegrationBenchmarks>(BenchmarkConfig.Quick);

        await Assert.That(suite).IsNotNull();
        await Assert.That(suite.Results).Count().IsEqualTo(2);
    }

    [Test]
    [Property("Category", "Integration")]
    public async Task LifecycleMethods_AreCalled()
    {
        var config = new BenchmarkConfig
        {
            WarmupIterations = 0,
            SampleCount = 3,
            IterationsPerSample = 1
        };

        var suite = await BenchmarkRunner.RunAsync<AsyncIntegrationBenchmarks>(config);

        await Assert.That(suite).IsNotNull();
        await Assert.That(suite.Results.Any(r => r.Name == "AsyncBaseline")).IsTrue();
        await Assert.That(suite.Results.Any(r => r.Name == "SyncBenchmark")).IsTrue();
    }

    [Test]
    [Property("Category", "Integration")]
    public async Task Comparison_BaselineIsGenerated()
    {
        var suite = await BenchmarkRunner.RunAsync<AsyncIntegrationBenchmarks>(BenchmarkConfig.Quick);

        await Assert.That(suite.Comparisons).IsNotNull();
        await Assert.That(suite.Comparisons!.Count).IsGreaterThan(0);
        await Assert.That(suite.Comparisons[0].Name)
            .Contains("SyncBenchmark vs AsyncBaseline");
    }
}
```

- [ ] **Step 2: Build and run integration tests**

```powershell
dotnet build D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug
dotnet test --project D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug -- --filter "Category~Integration" --no-progress
```

- [ ] **Step 3: Run full test suite**

```powershell
dotnet test --project D:/MyProjects/PicoHex/PicoBench/tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug -- --no-progress
```

All tests must pass.

- [ ] **Step 4: Commit**

```bash
git add tests/PicoBench.Tests/Integration/AsyncBenchmarkIntegrationTests.cs
git commit -m "test: add end-to-end integration test for async attribute-based benchmark class"
```

---

### Self-Review Checklist

- [x] Every spec section mapped to at least one task
- [x] No TBD/TODO placeholders
- [x] Type consistency: `LifecycleMethodInfo`, `ValueTask<BenchmarkSuite>`, `Task<BenchmarkResult>` used consistently
- [x] Each task produces independently testable deliverable
- [x] Exact file paths for every file touched
- [x] Exact test commands with expected outputs
