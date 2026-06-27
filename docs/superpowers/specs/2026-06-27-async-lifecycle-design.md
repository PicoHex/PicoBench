# Async Lifecycle & Benchmark Design

**Date:** 2026-06-27  
**Status:** Approved  
**Context:** PicoBench currently only supports synchronous initialization/cleanup and benchmark methods. This design adds full async support across the entire pipeline.

---

## Summary of Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | Attribute auto-detection (void / Task / ValueTask) | Cleaner UX, no new attribute names to learn. `async void` gets an analyzer warning. |
| 2 | Unified `IBenchmarkClass` returning `ValueTask<BenchmarkSuite>` | Single interface, zero heap allocation for pure sync classes (`ValueTask.FromResult`). |
| 3 | `Benchmark.RunAsync` returns `Task<BenchmarkResult>` (distinct from sync `Run` → `BenchmarkResult`) | `Task` is the mainstream return type; distinct method names eliminate lambda ambiguity; true async, no blocking. |
| 4 | `CpuOnly` timing via `Process.TotalProcessorTime` | OS-level CPU accounting, implementation is trivial. GC collection is skipped in this mode. |
| 5 | `CancellationToken` in `BenchmarkConfig` | Natural fit — config already holds "how this run behaves". |
| 6 | `RunAsync<T>()` is the primary entry; `Run<T>()` is a sync shortcut (docs warn about SynchronizationContext). | Avoids two-interface complexity while keeping a familiar API. |

---

## 1. Analyzer Changes

### 1.1 Lifecycle Method Validation (PBGEN004)

**Before:**
```csharp
private static bool IsValidLifecycleMethod(IMethodSymbol method)
{
    return method is { IsStatic: false, IsGenericMethod: false,
                       Parameters.Length: 0, ReturnsVoid: true };
}
```

**After:** Relax to accept `Task` and `ValueTask` return types:
```csharp
private static bool IsValidLifecycleMethod(IMethodSymbol method)
{
    return method is { IsStatic: false, IsGenericMethod: false, Parameters.Length: 0 } &&
           (method.ReturnsVoid || IsTaskLike(method.ReturnType));
}

private static bool IsTaskLike(ITypeSymbol type) =>
    type is INamedTypeSymbol named &&
    (named.IsTask() || named.IsValueTask());
```

### 1.2 New Warning: `async void` Lifecycle Methods (PBGEN009)

```csharp
public static readonly DiagnosticDescriptor AsyncVoidLifecycleMethod = new(
    id: "PBGEN009",
    title: "Async void lifecycle method",
    messageFormat: "{0} method '{1}' is async void. It will not be awaited. Use Task or ValueTask.",
    category: "PicoBench.Generators",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true
);
```

Triggered when a method has `[GlobalSetup]`/`[GlobalCleanup]`/`[IterationSetup]`/`[IterationCleanup]` and both `IsAsync: true` and `ReturnsVoid: true`.

### 1.3 Benchmark Method Validation (PBGEN003)

**Before:** only `void` methods.

**After:** also accept `Task` and `ValueTask` returns:
```csharp
private static bool IsValidBenchmarkMethod(IMethodSymbol method)
{
    return method is { IsStatic: false, IsGenericMethod: false, Parameters.Length: 0 } &&
           (method.ReturnsVoid || IsTaskLike(method.ReturnType));
}
```

When registering a valid benchmark method, set `IsAsync = !method.ReturnsVoid` on the `BenchmarkMethodModel`.

### 1.4 Lifecycle Method Registration

When registering a lifecycle method, store both name and async flag:
```csharp
target = new LifecycleMethodInfo
{
    Name = method.Name,
    IsAsync = !method.ReturnsVoid
};
```

Set `model.IsAsync = true` when any lifecycle or benchmark method has `IsAsync == true`.

---

## 2. Model Changes

### 2.1 `BenchmarkClassModel`

Lifecycle method fields change from `string?` to a new record type:

```csharp
internal sealed class LifecycleMethodInfo : IEquatable<LifecycleMethodInfo>
{
    public string Name { get; init; } = "";
    public bool IsAsync { get; init; }   // true when returns Task/ValueTask
}
```

Updated fields:
```csharp
public LifecycleMethodInfo? GlobalSetupMethod { get; init; }
public LifecycleMethodInfo? GlobalCleanupMethod { get; init; }
public LifecycleMethodInfo? IterationSetupMethod { get; init; }
public LifecycleMethodInfo? IterationCleanupMethod { get; init; }
```

Class-level helper:
```csharp
public bool IsAsync { get; init; }   // true if any element (lifecycle or benchmark) is async
```

`IsAsync` is set to `true` when:
- Any lifecycle method returns `Task`/`ValueTask`
- Any benchmark method returns `Task`/`ValueTask`

### 2.2 `BenchmarkMethodModel`

Add `IsAsync` flag:
```csharp
internal sealed class BenchmarkMethodModel
{
    public string Name { get; init; } = "";
    public bool IsAsync { get; init; }
    public bool IsBaseline { get; init; }
    public string? Description { get; init; }
}
```

### 2.3 Model Equality/HashCode

Update all `Equals` and `GetHashCode` implementations to include `IsAsync` and the new `LifecycleMethodInfo` fields.

---

## 3. Interface Change

**Old:**
```csharp
public interface IBenchmarkClass
{
    BenchmarkSuite RunBenchmarks(BenchmarkConfig? config = null);
}
```

**New:**
```csharp
public interface IBenchmarkClass
{
    ValueTask<BenchmarkSuite> RunBenchmarksAsync(BenchmarkConfig? config = null);
}
```

Breaking change: existing implementations of `IBenchmarkClass` need to recompile (rename method, change return type). Source-generated code is unaffected; test fakes need updating.

---

## 4. Emitter Changes

### 4.1 Sync class (IsAsync = false)

```csharp
ValueTask<BenchmarkSuite> IBenchmarkClass.RunBenchmarksAsync(...)
{
    config ??= BenchmarkConfig.Default;
    // ... same sync logic as before ...
    return ValueTask.FromResult(suite);  // zero allocation
}
```

### 4.2 Async class (IsAsync = true)

The emitter uses `LifecycleMethodInfo.IsAsync` and `BenchmarkMethodModel.IsAsync` to decide per-method dispatch.

```csharp
async ValueTask<BenchmarkSuite> IBenchmarkClass.RunBenchmarksAsync(...)
{
    config ??= BenchmarkConfig.Default;

    foreach (var __p_N in new int[] { 10, 100, 1000 })
    {
        this.N = __p_N;

        // GlobalSetup: await if async, direct call if sync void
        @AWAIT@ this.GlobalSetup();

        // Benchmark methods wrapper and dispatch
        // Sync class:       Benchmark.Run(name, () => this.Method(), config)
        // Async class:      await Benchmark.RunAsync(name, async () => { this.Method(); }, ...)
        //                   (always Func<Task> wrapper, even if method is sync void)
        var __r_Method = @AWAIT@Benchmark.@RUN_OR_RUNASYNC@(
            nameExpr,
            @WRAP_BENCHMARK@,
            warmup: @WRAP_BENCHMARK@,
            config: config,
            setup: @WRAP_ITER@,
            teardown: @WRAP_ITER@);

        // GlobalCleanup: await if async, direct call if sync void
        @AWAIT@ this.GlobalCleanup();
    }

    return suite;
}
```

Pseudo-template key:
- `@AWAIT@` → `await ` if `IsAsync` (class-level), else empty
- `@RUN_OR_RUNASYNC@` → `RunAsync` if `IsAsync`, else `Run`
- `@WRAP_BENCHMARK@` → `async () => { @AWAIT_BM@ this.Method(); }` if `IsAsync`, else `() => this.Method()`
  - `@AWAIT_BM@` → `await ` if this benchmark method's `IsAsync`, else empty
  - Rationale: async class always needs `Func<Task>`, sync class needs `Action`
- `@WRAP_ITER@` → `(Func<Task>?)null` if no iteration method. Otherwise always produces a `Func<Task>` lambda:
  - Sync iter in async class: `async () => { this.IterSetup(); }`
  - Async iter in async class: `async () => { await this.IterSetupAsync(); }`
  - Sync iter in sync class: `() => this.IterSetup()` (as `Action`)

When both iteration setup and cleanup are `null`, the emitter uses the simple `Benchmark.Run(name, action, config)` (sync class) or `await Benchmark.RunAsync(name, asyncAction, config)` (async class) — fewer parameters, fewer allocations. When either exists, it uses the full overload with `setup`/`teardown` parameters.

---

## 5. BenchmarkConfig Changes

```csharp
public sealed class BenchmarkConfig
{
    // ... existing fields unchanged ...

    /// <summary>Timing strategy for async benchmarks. Sync benchmarks use WallClock by default.</summary>
    public AsyncTimingMode TimingMode { get; init; } = AsyncTimingMode.WallClock;

    /// <summary>CancellationToken to allow early termination of a benchmark run.</summary>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
}

public enum AsyncTimingMode
{
    WallClock = 0,  // Stopwatch full duration, including await suspension time
    CpuOnly = 1     // Process.TotalProcessorTime delta, excludes I/O wait
}
```

---

## 6. Runner Changes

### 6.1 Existing sync `Time` methods — unchanged

### 6.2 New async wall-clock method

GC baseline is captured before the first iteration. Note that due to `await` yielding the thread, GC events from other work may be counted in the delta. GC info is marked with an `IsApproximate` flag in this mode rather than reported as precise.

```csharp
public static async Task<TimingSample> TimeAsync(
    int iterations,
    Func<Task> action,
    Func<Task>? setup = null,
    Func<Task>? teardown = null)
{
    ValidateIterations(iterations);
    if (action == null) throw new ArgumentNullException(nameof(action));

    var gcBaseline = GetGcBaselineCounts();

    if (setup != null) await setup();

    var cycleStart = GetCpuCycles();
    var watch = Stopwatch.StartNew();

    for (int i = 0; i < iterations; i++)
        await action();

    watch.Stop();
    var cycleEnd = GetCpuCycles();

    if (teardown != null) await teardown();

    return CreateSample(watch, cycleStart, cycleEnd, gcBaseline, isGcApproximate: true);
}
```

### 6.3 New async CpuOnly method

```csharp
public static async Task<TimingSample> TimeCpuAsync(
    int iterations,
    Func<Task> action,
    Func<Task>? setup = null,
    Func<Task>? teardown = null)
{
    ValidateIterations(iterations);
    if (action == null) throw new ArgumentNullException(nameof(action));

    if (setup != null) await setup();

    var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
    var cycleStart = GetCpuCycles();

    for (int i = 0; i < iterations; i++)
        await action();

    var cycleEnd = GetCpuCycles();
    var cpuDelta = Process.GetCurrentProcess().TotalProcessorTime - cpuBefore;

    if (teardown != null) await teardown();

    // GC info is not collected in CpuOnly mode (inaccurate across await points)
    return new TimingSample
    {
        ElapsedNanoseconds = cpuDelta.TotalNanoseconds,
        ElapsedMilliseconds = cpuDelta.TotalMilliseconds,
        ElapsedTicks = cpuDelta.Ticks,
        CpuCycles = cycleEnd - cycleStart,
        GcInfo = null  // not meaningful in async CpuOnly mode
    };
}
```

### 6.4 Dispatch

The async `RunAsync` / `CollectAndBuildAsync` methods on `Benchmark` dispatch to `Runner.TimeAsync` (WallClock) or `Runner.TimeCpuAsync` (CpuOnly) based on `config.TimingMode`. Sync `Run` continues to use `Runner.Time`.

---

## 7. Benchmark.Run Async Overloads

Async overloads return `Task<BenchmarkResult>` — true async, no internal blocking. All new overloads use `Func<Task>`, not `Func<ValueTask>`. Names use `RunAsync` to avoid ambiguity with the sync `Action` overloads:

```csharp
// Simple
public static Task<BenchmarkResult> RunAsync(string name, Func<Task> action,
    BenchmarkConfig? config = null)

// Full with warmup/setup/teardown
public static Task<BenchmarkResult> RunAsync(string name, Func<Task> action,
    Func<Task>? warmup, BenchmarkConfig? config = null,
    Func<Task>? setup = null, Func<Task>? teardown = null)

// Stateful
public static Task<BenchmarkResult> RunAsync<TState>(string name, TState state,
    Func<TState, Task> action, Func<TState, Task>? warmup = null,
    BenchmarkConfig? config = null)
```

The emitter code uses `await Benchmark.RunAsync(...)` when the class is async. Sync classes continue to call `Benchmark.Run(...)` (the existing `Action` overloads returning `BenchmarkResult`).

`RunScoped` async variant is deferred — scope disposal semantics interact poorly with async. Users who need async scopes use attribute-based classes.

Internal `CollectAndBuildAsync`:
```csharp
private static async Task<BenchmarkResult> CollectAndBuildAsync(
    string name, BenchmarkConfig config,
    Func<int, Task<TimingSample>> sampleFuncAsync)
```

Warmup loop becomes:
```csharp
for (int i = 0; i < config.WarmupIterations; i++)
    await warmup();
```

CancellationToken is checked at sample boundaries:
```csharp
config.CancellationToken.ThrowIfCancellationRequested();
```

Note: sync `Run(...)` and async `RunAsync(...)` are named differently to eliminate lambda return-type ambiguity (`() => { }` matches `Action` but not `Func<Task>`). This also communicates intent at the call site.

---

## 8. BenchmarkRunner API

```csharp
public static class BenchmarkRunner
{
    // Primary async entry
    public static Task<BenchmarkSuite> RunAsync<T>(BenchmarkConfig? config = null)
        where T : IBenchmarkClass, new()
    {
        return new T().RunBenchmarksAsync(config).AsTask();
    }

    // Sync shortcut (Console apps only — may deadlock on UI/SynchronizationContext threads)
    public static BenchmarkSuite Run<T>(BenchmarkConfig? config = null)
        where T : IBenchmarkClass, new()
    {
        return new T().RunBenchmarksAsync(config).GetAwaiter().GetResult();
    }

    // Instance overloads
    public static Task<BenchmarkSuite> RunAsync<T>(T instance,
        BenchmarkConfig? config = null) where T : IBenchmarkClass
    {
        return instance.RunBenchmarksAsync(config).AsTask();
    }

    public static BenchmarkSuite Run<T>(T instance,
        BenchmarkConfig? config = null) where T : IBenchmarkClass
    {
        return instance.RunBenchmarksAsync(config).GetAwaiter().GetResult();
    }
}
```

---

## 9. Files Affected

| File | Change |
|---|---|
| `src/PicoBench/Attributes.cs` | No change (auto-detection eliminates need for new attributes) |
| `src/PicoBench.Generators/BenchmarkClassAnalyzer.cs` | Relax lifecycle + benchmark validation, add `async void` warning, detect `IsAsync` |
| `src/PicoBench.Generators/Models.cs` | Add `IsAsync` to `BenchmarkClassModel`, update equality |
| `src/PicoBench.Generators/Emitter.cs` | Branch on `IsAsync`, emit `ValueTask`/`async ValueTask` method, async warmup/cleanup |
| `src/PicoBench.Generators/DiagnosticDescriptors.cs` | Add PBGEN009 |
| `src/PicoBench/IBenchmarkClass.cs` | Change to `ValueTask<BenchmarkSuite> RunBenchmarksAsync(...)` |
| `src/PicoBench/BenchmarkConfig.cs` | Add `TimingMode`, `CancellationToken` |
| `src/PicoBench/Runner.cs` | Add `TimeAsync`, `TimeCpuAsync` |
| `src/PicoBench/Benchmark.cs` | Add `RunAsync` overloads (return `Task<BenchmarkResult>`), async `CollectAndBuildAsync` |
| `src/PicoBench/BenchmarkRunner.cs` | Add `RunAsync<T>()`, update `Run<T>()` |
| Tests | Update all existing tests for new interface; add async lifecycle + benchmark tests |

---

## 10. Test Plan

### 10.1 Analyzer Tests
- `async Task Setup()` with `[GlobalSetup]` → no diagnostic
- `async ValueTask Setup()` with `[GlobalSetup]` → no diagnostic
- `async void Setup()` with `[GlobalSetup]` → PBGEN009 warning
- `int Setup()` with `[GlobalSetup]` → PBGEN004 error

### 10.2 Generator Tests
- Pure sync class → generates `ValueTask.FromResult`
- Mixed class (async setup, sync benchmark) → generates `async ValueTask`
- Full async class → generates `async ValueTask` with awaits

### 10.3 Runtime Tests
- `Benchmark.RunAsync` with `Func<Task>` → produces valid `Task<BenchmarkResult>`, awaitable
- `Benchmark.RunAsync` with `CpuOnly` timing mode → GC info is null in `TimingSample`
- Setup/teardown lambdas executed correct number of times
- `CancellationToken` aborts run mid-sample
- `BenchmarkRunner.RunAsync<T>()` returns valid suite

### 10.4 Integration Tests
- End-to-end attribute-based async benchmark class
- Mixed sync/async lifecycle method execution order

---

## 11. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| GC info inaccurate in all async modes (W.C. and CpuOnly) due to await yielding | Mark `GcInfo` with an `IsApproximate` flag; document limitation. CpuOnly skips GC entirely. |
| `Process.TotalProcessorTime` resolution (~15ms Windows) | Auto-calibration naturally increases iterations until measurable delta |
| `ValueTask` boxing overhead for async classes | Acceptable — at most one allocation per `RunBenchmarksAsync` call (only if the async method actually suspends; synchronous completions avoid allocation). Not per-iteration. |
| Breaking change to `IBenchmarkClass` | Source gen users are unaffected; manual implementations (tests fakes) need a trivial rename |
| Deadlock from sync `Run<T>()` on UI threads | XML doc on `Run<T>()` warns; `SynchronizationContext` detection can be added later |
| User writes `async () => ...` which returns `Task`, not `Func<ValueTask>` | No issue — API takes `Func<Task>` |
