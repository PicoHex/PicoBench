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
    public static TimingSample Time(int iterations, Action action, Action? setup, Action? teardown)
    {
        ValidateIterations(iterations);
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        // Run setup (not timed)
        setup?.Invoke();

        // GC baseline is captured after setup so setup allocations are
        // not attributed to the benchmark.
        var gcBaseline = GetGcBaselineCounts();

        // Start timing
        var cycleStart = GetProcessCpuCycles();
        var watch = Stopwatch.StartNew();

        // Run the measured work
        for (var i = 0; i < iterations; i++)
            action();

        // Stop timing
        watch.Stop();
        var cycleEnd = GetProcessCpuCycles();

        // GC delta is computed before teardown so teardown allocations
        // are not attributed to the benchmark.
        var gcInfo = CalculateGcDelta(gcBaseline);

        // Run teardown (not timed)
        teardown?.Invoke();

        return CreateSample(watch, cycleStart, cycleEnd, gcInfo);
    }

    /// <summary>
    /// Run a timed measurement with state passed to the action (avoids closure allocation).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimingSample Time<TState>(int iterations, TState state, Action<TState> action)
    {
        ValidateIterations(iterations);
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var gcBaseline = GetGcBaselineCounts();

        var cycleStart = GetProcessCpuCycles();
        var watch = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
            action(state);

        watch.Stop();
        var cycleEnd = GetProcessCpuCycles();

        return CreateSample(watch, cycleStart, cycleEnd, CalculateGcDelta(gcBaseline));
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
        bool isGcApproximate = false
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
            };
        }

        return new TimingSample
        {
            ElapsedNanoseconds = elapsedNs,
            ElapsedMilliseconds = elapsedNs / 1_000_000.0,
            ElapsedTicks = elapsedTicks,
            CpuCycles = cycleEnd - cycleStart,
            GcInfo = gcInfo,
        };
    }

    /// <summary>
    /// Run an async timed measurement with optional setup and teardown.
    /// Uses Stopwatch for wall-clock timing. GC info is marked approximate.
    /// </summary>
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
        var gcBaseline = GetGcBaselineCounts();

        // Process-wide counters stay valid when an await resumes on a
        // different thread; per-thread counters do not.
        var cycleStart = GetProcessCpuCycles();
        var watch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
            await action();

        watch.Stop();
        var cycleEnd = GetProcessCpuCycles();

        // GC delta is computed before teardown so teardown allocations
        // are not attributed to the benchmark.
        var gcInfo = CalculateGcDelta(gcBaseline);

        if (teardown != null)
            await teardown();

        return CreateSample(watch, cycleStart, cycleEnd, gcInfo, isGcApproximate: true);
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

        var gcBaseline = GetGcBaselineCounts();

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
            CalculateGcDelta(gcBaseline),
            isGcApproximate: true
        );
    }

    /// <summary>
    /// Run an async timed measurement using Process.TotalProcessorTime.
    /// Only CPU execution time is counted; I/O wait time is excluded.
    /// GC info is not collected (null).
    /// </summary>
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

        for (int i = 0; i < iterations; i++)
            await action();

        var cycleEnd = GetProcessCpuCycles();
        var cpuDelta = Process.GetCurrentProcess().TotalProcessorTime - cpuBefore;

        if (teardown != null)
            await teardown();

        return new TimingSample
        {
            ElapsedNanoseconds = cpuDelta.Ticks * 100.0, // 1 tick = 100ns
            ElapsedMilliseconds = cpuDelta.TotalMilliseconds,
            ElapsedTicks = cpuDelta.Ticks,
            CpuCycles = cycleEnd - cycleStart,
            GcInfo = null,
        };
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
            GcInfo = null,
        };
    }
}
