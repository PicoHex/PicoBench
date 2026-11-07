namespace PicoBench;

/// <summary>
/// Configuration for benchmark execution.
/// </summary>
public sealed class BenchmarkConfig
{
    /// <summary>Number of warmup iterations before measurement.</summary>
    public int WarmupIterations
    {
        get;
        init =>
            field =
                value >= 0
                    ? value
                    : throw new ArgumentOutOfRangeException(
                        nameof(WarmupIterations),
                        "WarmupIterations must be non-negative."
                    );
    } = 1000;

    /// <summary>Number of samples to collect.</summary>
    public int SampleCount
    {
        get;
        init =>
            field =
                value > 0
                    ? value
                    : throw new ArgumentOutOfRangeException(
                        nameof(SampleCount),
                        "SampleCount must be positive."
                    );
    } = 100;

    /// <summary>Number of iterations per sample.</summary>
    public int IterationsPerSample
    {
        get;
        init =>
            field =
                value > 0
                    ? value
                    : throw new ArgumentOutOfRangeException(
                        nameof(IterationsPerSample),
                        "IterationsPerSample must be positive."
                    );
    } = 10000;

    /// <summary>Whether to retain raw samples in the result.</summary>
    public bool RetainSamples { get; init; } = false;

    /// <summary>
    /// When true (default), a full forced GC runs before each benchmark's
    /// collection phase to establish a clean heap baseline. Set to false to
    /// skip the forced collections — useful for suite runs with many
    /// benchmarks and parameter combinations where the cost adds up.
    /// </summary>
    public bool ForceGcBeforeBenchmark { get; init; } = true;

    /// <summary>
    /// When true (default), the process and the calling thread are raised to
    /// high priority for the duration of the run and restored to their previous
    /// values afterwards. Set to false to leave host scheduling untouched.
    /// </summary>
    public bool BoostPriorities { get; init; } = true;

    /// <summary>
    /// When enabled, PicoBench automatically increases iterations per sample until
    /// a minimum timing budget is reached for more stable measurements.
    /// </summary>
    public bool AutoCalibrateIterations { get; init; } = false;

    /// <summary>
    /// Minimum elapsed time per sample targeted by auto-calibration.
    /// </summary>
    public TimeSpan MinSampleTime { get; init; } = TimeSpan.FromMilliseconds(0.25);

    /// <summary>
    /// Upper bound for iterations per sample when auto-calibration is enabled.
    /// </summary>
    public int MaxAutoIterationsPerSample
    {
        get;
        init =>
            field =
                value > 0
                    ? value
                    : throw new ArgumentOutOfRangeException(
                        nameof(MaxAutoIterationsPerSample),
                        "MaxAutoIterationsPerSample must be positive."
                    );
    } = 1_000_000_000;

    /// <summary>Timing strategy for async benchmarks. Sync benchmarks ignore this.</summary>
    public AsyncTimingMode TimingMode { get; init; } = AsyncTimingMode.WallClock;

    /// <summary>
    /// CancellationToken to allow early termination of a benchmark run.
    /// Checked at sample boundaries by asynchronous benchmarks only;
    /// synchronous benchmarks ignore it.
    /// </summary>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    /// <summary>Default configuration suitable for most benchmarks.</summary>
    public static BenchmarkConfig Default { get; } = new();

    /// <summary>
    /// Quick configuration for faster iteration during development.
    /// <para>
    /// The preset pins a fixed <see cref="IterationsPerSample"/> and leaves
    /// <see cref="AutoCalibrateIterations"/> at its default (<c>false</c>): the
    /// resolved iteration count of a run is the preset's own constant, so two
    /// runs of one workload agree exactly. Auto-calibration is a multiplicative
    /// search that stops wherever one probe happens to overshoot
    /// <see cref="MinSampleTime"/>, which made the resolved count
    /// non-reproducible (2.0-2.2x spread over five runs of one workload). Opt in
    /// explicitly with <c>new BenchmarkConfig { AutoCalibrateIterations = true }</c>
    /// when a timing budget matters more than cross-run comparability.
    /// </para>
    /// </summary>
    public static BenchmarkConfig Quick { get; } =
        new()
        {
            WarmupIterations = 100,
            SampleCount = 10,
            IterationsPerSample = 1000,
        };

    /// <summary>
    /// Precise configuration for final measurements.
    /// <para>
    /// Like <see cref="Quick"/>, the preset is deterministic: it pins
    /// <see cref="IterationsPerSample"/> and does not auto-calibrate, so repeated
    /// runs of the same workload use the same iteration count.
    /// </para>
    /// </summary>
    public static BenchmarkConfig Precise { get; } =
        new()
        {
            WarmupIterations = 5000,
            SampleCount = 200,
            IterationsPerSample = 50000,
        };
}

/// <summary>
/// Controls how async benchmark timing is measured.
/// </summary>
public enum AsyncTimingMode
{
    /// <summary>Full wall-clock duration including await suspension time. Default.</summary>
    WallClock = 0,

    /// <summary>CPU execution time only (Process.TotalProcessorTime), excluding I/O wait.</summary>
    CpuOnly = 1,
}
