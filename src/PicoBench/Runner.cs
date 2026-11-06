namespace PicoBench;

/// <summary>
/// Low-level timing utilities for benchmark measurements.
/// Provides high-precision timing with GC and CPU cycle tracking.
/// </summary>
public static partial class Runner
{
    private static readonly Lazy<bool> PlatformInitializer = new(
        InitializePlatform,
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    /// <summary>
    /// <c>MethodImplOptions.AggressiveOptimization</c> (value 512) was introduced
    /// after .NET Standard 2.0, so the netstandard2.0 reference assembly this
    /// library compiles against does not declare it. The flag is honoured by the
    /// JIT of every supported runtime and ignored where unknown.
    /// </summary>
    private const MethodImplOptions AggressiveOptimization = (MethodImplOptions)512;

    private static readonly Lazy<bool> Initializer = new(
        InitializeCore,
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    /// <summary>
    /// Initialize the runner by preparing platform cycle counters and warming up
    /// the timing APIs. Call this once at the start of your benchmark session.
    /// Thread-safe: uses <see cref="Lazy{T}"/> to guarantee single initialization.
    /// </summary>
    public static void Initialize()
    {
        _ = Initializer.Value;
    }

    /// <summary>
    /// Ensures platform-specific cycle counter initialization has run.
    /// Called by initialization and by the environment metadata factory so
    /// that reported counter availability does not depend on whether a
    /// benchmark has already run (see <see cref="EnvironmentInfo"/> defaults).
    /// </summary>
    internal static void EnsurePlatformInitialized()
    {
        _ = PlatformInitializer.Value;
    }

    private static bool InitializePlatform()
    {
        // Initialize Linux perf events for CPU cycle counting.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            InitializeLinuxPerf();
        }

        return true;
    }

    private static bool InitializeCore()
    {
        EnsurePlatformInitialized();

        // Warm-up: touch Stopwatch/GC/cycle APIs once.
        Time(1, static () => { });

        return true;
    }

    // ─── Priority boost scope ────────────────────────────────────────

    private static readonly object PriorityGate = new();
    private static int _priorityBoostDepth;
    private static ProcessPriorityClass? _savedProcessPriority;
    private static Thread? _boostedThread;
    private static ThreadPriority? _savedThreadPriority;

    /// <summary>
    /// Enters a scoped process/thread priority boost. The previous priorities
    /// are restored when the returned scope is disposed, so hosts that run
    /// benchmarks keep their original scheduling behaviour.
    /// </summary>
    internal static PriorityBoostScope BoostPriorities(bool enabled) => new(enabled);

    internal readonly struct PriorityBoostScope : IDisposable
    {
        private readonly bool _active;

        internal PriorityBoostScope(bool enabled)
        {
            if (enabled && EnterPriorityBoost(out var processBoosted, out var threadBoosted))
            {
                _active = true;
                ProcessBoosted = processBoosted;
                ThreadBoosted = threadBoosted;
            }
        }

        internal bool IsActive => _active;

        internal bool ProcessBoosted { get; }

        internal bool ThreadBoosted { get; }

        public void Dispose()
        {
            if (_active)
                ExitPriorityBoost();
        }
    }

    private static bool EnterPriorityBoost(out bool processBoosted, out bool threadBoosted)
    {
        processBoosted = false;
        threadBoosted = false;

        lock (PriorityGate)
        {
            // Nested scope: the outermost scope owns the boost and restores it.
            if (_priorityBoostDepth > 0)
            {
                _priorityBoostDepth++;
                return true;
            }

            try
            {
                var process = Process.GetCurrentProcess();
                _savedProcessPriority = process.PriorityClass;
                process.PriorityClass = ProcessPriorityClass.High;
                processBoosted = true;
            }
            catch
            {
                _savedProcessPriority = null;
            }

            try
            {
                _boostedThread = Thread.CurrentThread;
                _savedThreadPriority = _boostedThread.Priority;
                _boostedThread.Priority = ThreadPriority.Highest;
                threadBoosted = true;
            }
            catch
            {
                _boostedThread = null;
                _savedThreadPriority = null;
            }

            if (!processBoosted && !threadBoosted)
                return false;

            _priorityBoostDepth = 1;
            return true;
        }
    }

    private static void ExitPriorityBoost()
    {
        lock (PriorityGate)
        {
            if (_priorityBoostDepth == 0)
                return;

            _priorityBoostDepth--;
            if (_priorityBoostDepth > 0)
                return;

            try
            {
                if (_savedProcessPriority is { } savedProcessPriority)
                    Process.GetCurrentProcess().PriorityClass = savedProcessPriority;
            }
            catch
            {
                // Ignore restore failures (permissions may have changed).
            }

            try
            {
                if (
                    _boostedThread is { } boostedThread
                    && _savedThreadPriority is { } savedThreadPriority
                )
                    boostedThread.Priority = savedThreadPriority;
            }
            catch
            {
                // Ignore restore failures (the thread may have exited).
            }

            _savedProcessPriority = null;
            _boostedThread = null;
            _savedThreadPriority = null;
        }
    }

    // ─── CPU clock granularity ───────────────────────────────────────

    private static readonly Lazy<TimeSpan> CpuClockGranularity = new(
        MeasureCpuClockGranularity,
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    /// <summary>
    /// Granularity of <see cref="Process.TotalProcessorTime"/> on this machine.
    /// Used as the minimum sample budget for <see cref="AsyncTimingMode.CpuOnly"/>,
    /// whose clock can only advance in full timer ticks (typically 10-16 ms).
    /// </summary>
    internal static TimeSpan GetCpuClockGranularity() => CpuClockGranularity.Value;

    private static TimeSpan MeasureCpuClockGranularity()
    {
        var fallback = TimeSpan.FromMilliseconds(15);
        const int maxMeasurementMilliseconds = 250;

        try
        {
            var process = Process.GetCurrentProcess();
            var before = process.TotalProcessorTime;
            var watch = Stopwatch.StartNew();

            while (process.TotalProcessorTime == before)
            {
                Thread.SpinWait(200);
                if (watch.Elapsed > TimeSpan.FromMilliseconds(maxMeasurementMilliseconds))
                    return fallback;
            }

            var measured = watch.Elapsed;
            return
                measured > TimeSpan.Zero
                && measured < TimeSpan.FromMilliseconds(maxMeasurementMilliseconds)
                ? measured
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Fixed per-sample cost of the timing harness itself: one empty delegate
    /// timed through exactly the same path (GC baseline, cycle-counter reads,
    /// stopwatch, loop bookkeeping) as a real sample. The minimum over several
    /// repetitions is the best estimate of the additive cost, because noise can
    /// only inflate a measurement; the maximum doubles as the run-to-run spread
    /// used to report how small a per-operation value is still resolvable.
    /// </summary>
    internal readonly struct HarnessOverhead(double minNanoseconds, double maxNanoseconds)
    {
        /// <summary>Smallest measured harness cost (nanoseconds per sample).</summary>
        internal double MinNanoseconds { get; } = minNanoseconds;

        /// <summary>Largest measured harness cost (nanoseconds per sample).</summary>
        internal double MaxNanoseconds { get; } = maxNanoseconds > minNanoseconds ? maxNanoseconds : minNanoseconds;
    }

    /// <summary>
    /// Measures the fixed per-sample overhead of the harness with
    /// <paramref name="iterations"/> iterations, i.e. the cost a caller would
    /// otherwise see attributed to the measured action (delegate dispatch plus
    /// loop bookkeeping plus the clock/counter reads around it).
    /// </summary>
    /// <param name="iterations">Iterations per sample used by the run being corrected.</param>
    /// <param name="repetitions">Number of probes to keep; the minimum is reported.</param>
    internal static HarnessOverhead MeasureHarnessOverhead(int iterations, int repetitions)
    {
        if (iterations <= 0 || repetitions <= 0)
            return new HarnessOverhead(0, 0);

        Action empty = static () => { };
        var min = double.MaxValue;
        var max = 0.0;

        // The first probe is discarded: it carries first-call warm-up (the
        // delegate, the counters and the loop are all cold) and would otherwise
        // widen the reported spread without telling anything about the steady
        // state this run is measured in.
        for (var r = 0; r <= repetitions; r++)
        {
            var sample = Time(iterations, empty);
            if (r == 0)
                continue;

            if (sample.ElapsedNanoseconds < min)
                min = sample.ElapsedNanoseconds;
            if (sample.ElapsedNanoseconds > max)
                max = sample.ElapsedNanoseconds;
        }

        return min is double.MaxValue
            ? new HarnessOverhead(0, 0)
            : new HarnessOverhead(min, max);
    }

    /// <summary>
    /// Run a timed measurement of the given action.
    /// </summary>
    /// <param name="iterations">Number of times to invoke the action.</param>
    /// <param name="action">The action to measure.</param>
    /// <returns>A <see cref="TimingSample"/> containing timing and GC data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimingSample Time(int iterations, Action action) =>
        Time(iterations, action, setup: null, teardown: null);

    /// <summary>
    /// Run a timed measurement with optional setup and teardown.
    /// </summary>
    /// <param name="iterations">Number of times to invoke the action.</param>
    /// <param name="action">The action to measure.</param>
    /// <param name="setup">Optional setup action (not timed).</param>
    /// <param name="teardown">Optional teardown action (not timed).</param>
    /// <returns>A <see cref="TimingSample"/> containing timing and GC data.</returns>
    /// <remarks>
    /// <para>
    /// <b>Cleanup contract</b> (B1-H3, Batch 0 · 0.8): <paramref name="teardown"/> is
    /// the counterpart of <paramref name="setup"/> and runs <b>exactly once</b> per
    /// sample — on success, when <paramref name="action"/> throws, and when the
    /// measured delegate cancels (it throws <see cref="OperationCanceledException"/>,
    /// which is just another exception here). The exception still propagates and the
    /// loop still stops at the throwing iteration, so a failing sample can no longer
    /// leak the resource the setup acquired.
    /// </para>
    /// <para>
    /// If <paramref name="setup"/> itself throws, <paramref name="teardown"/> is
    /// <b>not</b> invoked: there is no established state to tear down, and calling a
    /// teardown that dereferences a half-initialised field could mask the original
    /// setup exception. A setup that acquires resources before it can fail must roll
    /// its own partial state back.
    /// </para>
    /// <para>
    /// A teardown that throws replaces the exception in flight (normal .NET
    /// semantics). Teardown work is never timed and never lands in this sample's
    /// elapsed time, CPU cycles or GC delta.
    /// </para>
    /// </remarks>
    // The timed loop must run in fully optimised code. Left to tiered compilation
    // it executes in tier-0 for the first ~1000 iterations of *every* call (OSR
    // only partially recovers), which added a fixed 20-45 µs per sample — the
    // dominant part of the "+2 to +3 ns/op" floor below 5 ns and of the per-op
    // drift between 100 and 10000 iterations (Batch 0 · 0.1, B1-C2).
    [MethodImpl(AggressiveOptimization)]
    public static TimingSample Time(int iterations, Action action, Action? setup, Action? teardown)
    {
        ValidateIterations(iterations);
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        // Run setup (not timed). A setup that throws never reaches the timing block
        // below, so its teardown is not invoked (see the remarks above).
        setup?.Invoke();

        // GC baseline is captured after setup so setup allocations are
        // not attributed to the benchmark.
        var gcBaseline = GetGcBaseline();

        // Start timing. Synchronous benchmarks never hop threads, so the
        // thread-scoped counter is both valid and immune to work done by other
        // threads (a busy neighbour thread used to inflate cycles/op by ~2x).
        var cycleStart = GetCpuCycles();
        var watch = Stopwatch.StartNew();

        try
        {
            // Run the measured work
            for (var i = 0; i < iterations; i++)
                action();

            // Stop timing
            watch.Stop();
            var cycleEnd = GetCpuCycles();

            // GC delta is computed before teardown so teardown allocations
            // are not attributed to the benchmark.
            var gcInfo = CalculateGcDelta(gcBaseline, processWideAllocation: false);

            return CreateSample(
                watch,
                cycleStart,
                cycleEnd,
                gcInfo,
                areCpuCyclesApproximate: !HasMeaningfulCpuCycles()
            );
        }
        finally
        {
            // Unconditional cleanup: this is the only path to teardown, so it runs
            // once on success, once when an iteration throws, and once when the
            // delegate cancels (B1-H3). It runs after the sample was built, so a
            // teardown that allocates or forces a GC cannot change this sample.
            teardown?.Invoke();
        }
    }

    /// <summary>
    /// Run a timed measurement with state passed to the action (avoids closure allocation).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | AggressiveOptimization)]
    public static TimingSample Time<TState>(int iterations, TState state, Action<TState> action)
    {
        ValidateIterations(iterations);
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var gcBaseline = GetGcBaseline();

        // Thread-scoped counter: see Time(int, Action, Action?, Action?).
        var cycleStart = GetCpuCycles();
        var watch = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
            action(state);

        watch.Stop();
        var cycleEnd = GetCpuCycles();

        return CreateSample(
            watch,
            cycleStart,
            cycleEnd,
            CalculateGcDelta(gcBaseline, processWideAllocation: false),
            areCpuCyclesApproximate: !HasMeaningfulCpuCycles()
        );
    }

    /// <summary>
    /// Validate that the iterations parameter is positive.
    /// </summary>
    private static void ValidateIterations(int iterations)
    {
        if (iterations <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(iterations),
                "Iterations must be positive."
            );
    }

    /// <summary>
    /// Create a <see cref="TimingSample"/> from stopwatch and CPU cycle measurements.
    /// </summary>
    private static TimingSample CreateSample(
        Stopwatch watch,
        ulong cycleStart,
        ulong cycleEnd,
        GcInfo gcInfo,
        bool isGcApproximate = false,
        bool areCpuCyclesApproximate = false
    )
    {
        var elapsedTicks = watch.ElapsedTicks;
        var elapsedNs = elapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency);

        if (isGcApproximate)
        {
            gcInfo = new GcInfo
            {
                Gen0 = gcInfo.Gen0,
                Gen1 = gcInfo.Gen1,
                Gen2 = gcInfo.Gen2,
                IsApproximate = true,
                // The async path reads the process-wide allocation counter, so its
                // bytes carry the same "approximate" disclosure as its counts.
                AllocatedBytes = gcInfo.AllocatedBytes,
                AllocatedBytesAreApproximate = gcInfo.AllocatedBytesAreApproximate,
            };
        }

        return new TimingSample
        {
            ElapsedNanoseconds = elapsedNs,
            ElapsedMilliseconds = elapsedNs / 1_000_000.0,
            ElapsedTicks = elapsedTicks,
            CpuCycles = cycleEnd - cycleStart,
            CpuCyclesAreApproximate = areCpuCyclesApproximate,
            GcInfo = gcInfo,
        };
    }

    /// <summary>
    /// Run an async timed measurement with optional setup and teardown.
    /// Uses Stopwatch for wall-clock timing. GC info is marked approximate.
    /// </summary>
    /// <remarks>
    /// Cleanup contract, identical to
    /// <see cref="Time(int, Action, Action?, Action?)"/>: the awaited
    /// <paramref name="teardown"/> runs exactly once per sample — on success, when an
    /// iteration throws and when it cancels — while a throwing
    /// <paramref name="setup"/> skips it (nothing was established to tear down).
    /// </remarks>
    public static async Task<TimingSample> TimeAsync(
        int iterations,
        Func<Task> action,
        Func<Task>? setup = null,
        Func<Task>? teardown = null
    )
    {
        ValidateIterations(iterations);
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        if (setup != null)
            await setup();

        // GC baseline is captured after setup so setup allocations are
        // not attributed to the benchmark.
        var gcBaseline = GetGcBaseline();

        // Process-wide counters stay valid when an await resumes on a
        // different thread; per-thread counters do not.
        var cycleStart = GetProcessCpuCycles();
        var watch = Stopwatch.StartNew();

        try
        {
            for (int i = 0; i < iterations; i++)
                await action();

            watch.Stop();
            var cycleEnd = GetProcessCpuCycles();

            // GC delta is computed before teardown so teardown allocations
            // are not attributed to the benchmark.
            var gcInfo = CalculateGcDelta(gcBaseline, processWideAllocation: true);

            return CreateSample(
                watch,
                cycleStart,
                cycleEnd,
                gcInfo,
                isGcApproximate: true,
                areCpuCyclesApproximate: true
            );
        }
        finally
        {
            // Unconditional cleanup: a throwing or cancelled iteration must not
            // skip the teardown (B1-H3, Batch 0 · 0.8).
            if (teardown != null)
                await teardown();
        }
    }

    /// <summary>
    /// Run an async timed measurement with state passed (avoids closure allocation).
    /// Uses Stopwatch for wall-clock timing. GC info is marked approximate.
    /// </summary>
    public static async Task<TimingSample> TimeAsync<TState>(
        int iterations,
        TState state,
        Func<TState, Task> action
    )
    {
        ValidateIterations(iterations);
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var gcBaseline = GetGcBaseline();

        var cycleStart = GetProcessCpuCycles();
        var watch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
            await action(state);

        watch.Stop();
        var cycleEnd = GetProcessCpuCycles();

        return CreateSample(
            watch,
            cycleStart,
            cycleEnd,
            CalculateGcDelta(gcBaseline, processWideAllocation: true),
            isGcApproximate: true,
            areCpuCyclesApproximate: true
        );
    }

    /// <summary>
    /// Run an async timed measurement using Process.TotalProcessorTime.
    /// Only CPU execution time is counted; I/O wait time is excluded.
    /// GC info is not collected (null).
    /// </summary>
    /// <remarks>
    /// Cleanup contract, identical to
    /// <see cref="Time(int, Action, Action?, Action?)"/>: teardown runs exactly once
    /// per sample when the measured work throws or is cancelled, and is skipped when
    /// the setup itself throws.
    /// </remarks>
    public static async Task<TimingSample> TimeCpuAsync(
        int iterations,
        Func<Task> action,
        Func<Task>? setup = null,
        Func<Task>? teardown = null
    )
    {
        ValidateIterations(iterations);
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        if (setup != null)
            await setup();

        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var cycleStart = GetProcessCpuCycles();

        try
        {
            for (int i = 0; i < iterations; i++)
                await action();

            var cycleEnd = GetProcessCpuCycles();
            var cpuDelta = Process.GetCurrentProcess().TotalProcessorTime - cpuBefore;

            return new TimingSample
            {
                ElapsedNanoseconds = cpuDelta.Ticks * 100.0, // 1 tick = 100ns
                ElapsedMilliseconds = cpuDelta.TotalMilliseconds,
                ElapsedTicks = cpuDelta.Ticks,
                CpuCycles = cycleEnd - cycleStart,
                CpuCyclesAreApproximate = true,
                GcInfo = null,
            };
        }
        finally
        {
            // Unconditional cleanup: a throwing or cancelled iteration must not
            // skip the teardown (B1-H3, Batch 0 · 0.8).
            if (teardown != null)
                await teardown();
        }
    }

    /// <summary>
    /// Run an async CPU-only timed measurement with state passed (avoids closure allocation).
    /// GC info is not collected (null).
    /// </summary>
    public static async Task<TimingSample> TimeCpuAsync<TState>(
        int iterations,
        TState state,
        Func<TState, Task> action
    )
    {
        ValidateIterations(iterations);
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var cycleStart = GetProcessCpuCycles();

        for (int i = 0; i < iterations; i++)
            await action(state);

        var cycleEnd = GetProcessCpuCycles();
        var cpuDelta = Process.GetCurrentProcess().TotalProcessorTime - cpuBefore;

        return new TimingSample
        {
            ElapsedNanoseconds = cpuDelta.Ticks * 100.0,
            ElapsedMilliseconds = cpuDelta.TotalMilliseconds,
            ElapsedTicks = cpuDelta.Ticks,
            CpuCycles = cycleEnd - cycleStart,
            CpuCyclesAreApproximate = true,
            GcInfo = null,
        };
    }
}
