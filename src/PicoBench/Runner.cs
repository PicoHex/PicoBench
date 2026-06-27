namespace PicoBench;

/// <summary>
/// Low-level timing utilities for benchmark measurements.
/// Provides high-precision timing with GC and CPU cycle tracking.
/// </summary>
public static partial class Runner
{
    private static readonly Lazy<bool> Initializer =
        new(InitializeCore, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Initialize the runner by setting process/thread priority and warming up timing APIs.
    /// Call this once at the start of your benchmark session.
    /// Thread-safe: uses <see cref="Lazy{T}"/> to guarantee single initialization.
    /// </summary>
    public static void Initialize()
    {
        _ = Initializer.Value;
    }

    private static bool InitializeCore()
    {
        try
        {
            // Set high priority on all platforms (may require elevated permissions)
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
        }
        catch
        {
            // Ignore if we can't set priority (e.g., insufficient permissions)
        }

        // Initialize Linux perf event for CPU cycle counting
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            InitializeLinuxPerf();
        }

        // Warm-up: touch Stopwatch/GC/cycle APIs once.
        Time(1, static () => { });

        return true;
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

        var gcBaseline = GetGcBaselineCounts();

        // Run setup (not timed)
        setup?.Invoke();

        // Start timing
        var cycleStart = GetCpuCycles();
        var watch = Stopwatch.StartNew();

        // Run the measured work
        for (var i = 0; i < iterations; i++)
            action();

        // Stop timing
        watch.Stop();
        var cycleEnd = GetCpuCycles();

        // Run teardown (not timed)
        teardown?.Invoke();

        return CreateSample(watch, cycleStart, cycleEnd, gcBaseline);
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

        var cycleStart = GetCpuCycles();
        var watch = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
            action(state);

        watch.Stop();
        var cycleEnd = GetCpuCycles();

        return CreateSample(watch, cycleStart, cycleEnd, gcBaseline);
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
        long[] gcBaseline,
        bool isGcApproximate = false
    )
    {
        var elapsedTicks = watch.ElapsedTicks;
        var elapsedNs = elapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency);

        var gcInfo = CalculateGcDelta(gcBaseline);
        if (isGcApproximate && gcInfo != null)
        {
            gcInfo = new GcInfo
            {
                Gen0 = gcInfo.Gen0,
                Gen1 = gcInfo.Gen1,
                Gen2 = gcInfo.Gen2,
                IsApproximate = true
            };
        }

        return new TimingSample
        {
            ElapsedNanoseconds = elapsedNs,
            ElapsedMilliseconds = elapsedNs / 1_000_000.0,
            ElapsedTicks = elapsedTicks,
            CpuCycles = cycleEnd - cycleStart,
            GcInfo = gcInfo
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

        var sample = CreateSample(watch, cycleStart, cycleEnd, gcBaseline, isGcApproximate: true);
        return sample;
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
            ElapsedNanoseconds = cpuDelta.Ticks * 100.0, // 1 tick = 100ns
            ElapsedMilliseconds = cpuDelta.TotalMilliseconds,
            ElapsedTicks = cpuDelta.Ticks,
            CpuCycles = cycleEnd - cycleStart,
            GcInfo = null
        };
    }
}
