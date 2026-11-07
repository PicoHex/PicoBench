using System.Reflection;

namespace PicoBench;

/// <summary>
/// GC generation counts captured during a benchmark run.
/// </summary>
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

/// <summary>
/// Raw timing data from a single sample run.
/// </summary>
public sealed class TimingSample
{
    /// <summary>Elapsed time in nanoseconds.</summary>
    public double ElapsedNanoseconds { get; init; }

    /// <summary>Elapsed time in milliseconds.</summary>
    public double ElapsedMilliseconds { get; init; }

    /// <summary>
    /// Elapsed ticks from the timing source: Stopwatch ticks in wall-clock
    /// modes, or 100-nanosecond CPU-time ticks in
    /// <see cref="AsyncTimingMode.CpuOnly"/> mode.
    /// </summary>
    public long ElapsedTicks { get; init; }

    /// <summary>
    /// CPU cycles consumed. Synchronous benchmarks read per-thread counters;
    /// asynchronous benchmarks read process-wide counters because the measured
    /// work may resume on a different thread. On macOS this is a monotonic
    /// timestamp and should not be compared across platforms. 0 when no cycle
    /// source is available.
    /// </summary>
    public ulong CpuCycles { get; init; }

    /// <summary>
    /// When true, <see cref="CpuCycles"/> is not a thread-scoped CPU cycle
    /// count: it comes either from a process-wide counter (asynchronous
    /// benchmarks, which may resume on another thread, so the value also counts
    /// work done by other threads) or from a monotonic clock proxy (macOS
    /// <c>mach_absolute_time</c>), which is not a cycle count at all.
    /// </summary>
    public bool CpuCyclesAreApproximate { get; init; }

    /// <summary>
    /// GC collection counts during this sample.
    /// <c>null</c> when GC data was not collected (CpuOnly async mode).
    /// </summary>
    public GcInfo? GcInfo { get; init; }
}

/// <summary>
/// Statistical summary of multiple timing samples.
/// </summary>
public sealed class Statistics
{
    /// <summary>Average time in nanoseconds per operation.</summary>
    public double Avg { get; init; }

    /// <summary>
    /// Median (50th percentile) in nanoseconds, by linear interpolation between the
    /// two nearest order statistics — the method statistics packages call type 7
    /// (Excel <c>PERCENTILE.INC</c>, NumPy's default). Below about twenty samples the
    /// high percentiles sit next to <see cref="Max"/>; the README's "Percentiles"
    /// note says how to read that.
    /// </summary>
    public double P50 { get; init; }

    /// <summary>90th percentile in nanoseconds (type 7, like <see cref="P50"/>).</summary>
    public double P90 { get; init; }

    /// <summary>95th percentile in nanoseconds (type 7, like <see cref="P50"/>).</summary>
    public double P95 { get; init; }

    /// <summary>
    /// 99th percentile in nanoseconds (type 7, like <see cref="P50"/>). With fewer
    /// than twenty samples this is close to <see cref="Max"/> by construction, so it
    /// does not describe a stable tail.
    /// </summary>
    public double P99 { get; init; }

    /// <summary>Minimum time in nanoseconds.</summary>
    public double Min { get; init; }

    /// <summary>Maximum time in nanoseconds.</summary>
    public double Max { get; init; }

    /// <summary>Standard deviation in nanoseconds.</summary>
    public double StdDev { get; init; }

    /// <summary>Standard error of the mean in nanoseconds.</summary>
    public double StandardError { get; init; }

    /// <summary>Relative standard deviation as a percentage of the mean.</summary>
    public double RelativeStdDevPercent { get; init; }

    /// <summary>
    /// Average CPU cycles per operation. 0 when no cycle source is available
    /// on the current platform/runtime.
    /// </summary>
    public double CpuCyclesPerOp { get; init; }

    /// <summary>
    /// When true, <see cref="CpuCyclesPerOp"/> may include work performed by
    /// other threads (process-wide counter) or is a monotonic-clock proxy rather
    /// than real CPU cycles, so it must not be read as the cycle cost of the
    /// measured operation.
    /// </summary>
    public bool CpuCyclesAreApproximate { get; init; }

    /// <summary>
    /// Fixed per-operation overhead of the measurement harness itself (delegate
    /// dispatch, loop bookkeeping, clock and counter reads) that was measured
    /// with the same iteration count and deducted from the per-operation values.
    /// Both the synchronous and the asynchronous paths measure and deduct it; 0
    /// only when the caller supplied no probe to measure it with, i.e. when
    /// nothing was deducted and the per-operation values carry the floor.
    /// </summary>
    public double OverheadNs { get; init; }

    /// <summary>
    /// Smallest per-operation value this run can resolve, i.e. the largest of one
    /// timing-clock tick spread over the iterations, the run-to-run spread of the
    /// deducted harness overhead and the per-operation <b>size</b> of that overhead
    /// (its upper bound): a per-operation value at or below this limit is
    /// indistinguishable from the harness floor and is published as the upper bound
    /// <c>&lt;limit</c> instead of a number. The bound never falls below the amount
    /// that was deducted, so "this costs less than the limit" cannot claim a
    /// precision below the floor the same run measured.
    /// </summary>
    public double ResolutionLimitNanoseconds { get; init; }

    /// <summary>
    /// True when <see cref="Avg"/> is at or below
    /// <see cref="ResolutionLimitNanoseconds"/>, i.e. the workload could not be
    /// distinguished from the measurement harness' own overhead (sub-nanosecond
    /// operations), or when the timing source could not advance at all for at
    /// least one sample (see <see cref="Unmeasured"/>). Formatters print such a
    /// value as an upper bound (<c>&lt;limit</c>) or as "below resolution"
    /// instead of a plain number.
    /// </summary>
    public bool ResolutionLimited { get; init; }

    /// <summary>
    /// Number of samples whose timing source did not advance at all, i.e.
    /// samples that carry no measurement of the measured work. In
    /// <see cref="AsyncTimingMode.CpuOnly"/> mode the process CPU clock only
    /// advances in whole timer ticks (typically 10-16 ms on Windows), so any
    /// workload shorter than one tick reads as exactly zero CPU time.
    /// </summary>
    public int UnmeasuredSampleCount { get; init; }

    /// <summary>
    /// True when at least one sample could not be measured at all
    /// (<see cref="UnmeasuredSampleCount"/> &gt; 0, a zero clock delta).
    /// <para>
    /// The per-operation values of such a run are <b>not</b> measurements of the
    /// work: a zero-delta sample is reported as one timing quantum (the smallest
    /// value the clock can express, so no quantile can claim a measured zero).
    /// Formatters therefore print the per-operation cells as "not measured"
    /// (<c>N/A</c>) instead of a number, and <see cref="ResolutionLimited"/> is
    /// true as well.
    /// </para>
    /// </summary>
    public bool Unmeasured => UnmeasuredSampleCount > 0;

    /// <summary>Aggregated GC info across all samples. Null when not collected.</summary>
    public GcInfo? GcInfo { get; init; }

    /// <summary>Returns true when sample variation is high enough to question result stability.</summary>
    public bool HasHighVariance => RelativeStdDevPercent >= 10.0;
}

/// <summary>
/// Describes how the CPU cycle metric is collected on the current platform.
/// </summary>
public enum CpuCycleMeasurementKind
{
    /// <summary>No cycle-like measurement is available.</summary>
    Unsupported,

    /// <summary>Native thread cycle counts are used.</summary>
    ThreadCycles,

    /// <summary>Linux perf hardware CPU cycle events are used.</summary>
    PerfEventCpuCycles,

    /// <summary>A monotonic clock proxy is used instead of true CPU cycles.</summary>
    MonotonicClockProxy,
}

/// <summary>
/// Describes the execution mode of the benchmark process.
/// </summary>
public enum RuntimeExecutionMode
{
    /// <summary>The execution mode could not be determined reliably.</summary>
    Unknown,

    /// <summary>The benchmark process is running with a JIT runtime.</summary>
    Jit,

    /// <summary>The benchmark process is running as a Native AOT binary.</summary>
    NativeAot,
}

/// <summary>
/// Complete benchmark result for a single test case.
/// </summary>
public sealed class BenchmarkResult
{
    /// <summary>Name/identifier of the benchmark.</summary>
    public string Name { get; }

    /// <summary>Optional category/group for organizing results.</summary>
    public string? Category { get; }

    /// <summary>Optional tags for filtering and grouping.</summary>
    public IReadOnlyDictionary<string, string>? Tags { get; }

    /// <summary>Statistical summary of the benchmark.</summary>
    public Statistics Statistics { get; }

    /// <summary>Raw timing samples (optional, for detailed analysis).</summary>
    public IReadOnlyList<TimingSample>? Samples { get; }

    /// <summary>Number of iterations per sample.</summary>
    public int IterationsPerSample { get; }

    /// <summary>Total number of samples collected.</summary>
    public int SampleCount { get; }

    /// <summary>When the benchmark was run.</summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a benchmark result.
    /// </summary>
    public BenchmarkResult(
        string name,
        Statistics statistics,
        int iterationsPerSample,
        int sampleCount,
        string? category = null,
        IReadOnlyDictionary<string, string>? tags = null,
        IReadOnlyList<TimingSample>? samples = null,
        DateTime? timestamp = null
    )
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));

        if (iterationsPerSample <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(iterationsPerSample),
                "IterationsPerSample must be positive."
            );
        IterationsPerSample = iterationsPerSample;

        if (sampleCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(sampleCount),
                "SampleCount must be positive."
            );
        SampleCount = sampleCount;

        Category = category;
        Tags = tags;
        Samples = samples;

        if (timestamp.HasValue)
            Timestamp = timestamp.Value;
    }
}

/// <summary>
/// Verdict of a comparison: whether the candidate is measurably faster or slower
/// than the baseline, or whether the two could not be told apart.
/// </summary>
public enum ComparisonOutcome
{
    /// <summary>
    /// The comparison could not be evaluated (no usable ratio, e.g. a
    /// non-finite or non-positive average), so no verdict can be stated.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Both lanes were measured, but the ratio stays inside the noise: within the
    /// 5% dead band, or an interval that still contains parity.
    /// </summary>
    Inconclusive = 1,

    /// <summary>
    /// The candidate is faster: the ratio is above the dead band and the interval
    /// excludes 1.
    /// </summary>
    Faster = 2,

    /// <summary>
    /// The candidate is slower: the ratio is below the dead band and the interval
    /// excludes 1.
    /// </summary>
    Slower = 3,
}

/// <summary>
/// A two-sided interval for a comparison ratio (baseline time / candidate time),
/// so a caller can see how much of the reported difference the measurement itself
/// could explain: <c>1.0</c> inside the interval means parity is still plausible.
/// </summary>
public readonly struct ComparisonInterval
{
    /// <summary>Creates an interval from its two ends.</summary>
    /// <param name="low">Lower bound of the ratio.</param>
    /// <param name="high">Upper bound of the ratio.</param>
    public ComparisonInterval(double low, double high)
    {
        Low = low;
        High = high;
    }

    /// <summary>Lower bound of the ratio.</summary>
    public double Low { get; }

    /// <summary>Upper bound of the ratio.</summary>
    public double High { get; }

    /// <summary>True when <paramref name="value"/> lies inside the interval.</summary>
    /// <param name="value">The value to test.</param>
    public bool Contains(double value) => value >= Low && value <= High;

    /// <inheritdoc />
    public override string ToString() =>
        "["
        + Low.ToString("F3", CultureInfo.InvariantCulture)
        + ", "
        + High.ToString("F3", CultureInfo.InvariantCulture)
        + "]";
}

/// <summary>
/// Comparison result between two benchmark runs.
/// </summary>
public sealed class ComparisonResult
{
    private readonly double? _measuredSpeedup;
    private readonly ComparisonInterval? _interval;

    /// <summary>
    /// Relative distance from parity (5%) a ratio has to exceed before it counts as
    /// a real difference. Closer than that it is indistinguishable from the
    /// round-to-round noise of an interleaved comparison.
    /// </summary>
    internal const double DeadBand = ComparisonMeasurement.DeadBand;

    /// <summary>
    /// Creates a comparison result from two independent benchmark results.
    /// Such a comparison carries no interval: it reports the ratio of the two
    /// averages and a verdict only when that ratio is outside the dead band.
    /// </summary>
    /// <param name="name">Name of the comparison.</param>
    /// <param name="baseline">Baseline benchmark result.</param>
    /// <param name="candidate">Candidate benchmark result (the one being compared).</param>
    /// <param name="category">Optional category/group for organizing results.</param>
    /// <param name="tags">Optional tags for filtering and grouping.</param>
    public ComparisonResult(
        string name,
        BenchmarkResult baseline,
        BenchmarkResult candidate,
        string? category = null,
        IReadOnlyDictionary<string, string>? tags = null
    )
        : this(name, baseline, candidate, category, tags, speedup: null, interval: null) { }

    /// <summary>
    /// Creates a comparison result that carries its own measured ratio and interval,
    /// as produced by
    /// <see cref="Benchmark.Compare(string, string, Action, string, Action, BenchmarkConfig?)"/>,
    /// which interleaves the two lanes and pairs the rounds.
    /// </summary>
    internal ComparisonResult(
        string name,
        BenchmarkResult baseline,
        BenchmarkResult candidate,
        string? category,
        IReadOnlyDictionary<string, string>? tags,
        double? speedup,
        ComparisonInterval? interval
    )
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        Category = category;
        Tags = tags;
        _measuredSpeedup = speedup;
        _interval = interval;
    }

    /// <summary>Name of the comparison.</summary>
    public string Name { get; }

    /// <summary>Optional category/group for organizing results.</summary>
    public string? Category { get; }

    /// <summary>Optional tags for filtering and grouping.</summary>
    public IReadOnlyDictionary<string, string>? Tags { get; }

    /// <summary>Baseline benchmark result.</summary>
    public BenchmarkResult Baseline { get; }

    /// <summary>Candidate benchmark result (the one being compared).</summary>
    public BenchmarkResult Candidate { get; }

    /// <summary>Speedup ratio (Baseline.Avg / Candidate.Avg). >1 means candidate is faster.</summary>
    /// <remarks>
    /// A direct action-versus-action comparison reports the <b>median of the
    /// per-round paired ratios</b> instead of the ratio of two arithmetic means, so
    /// one noisy round cannot swing it. The behaviour for a zero/near-zero average
    /// is unchanged: <c>1.0</c> when both averages are zero, positive infinity when
    /// only the candidate's is. A direct comparison reports <see cref="double.NaN"/>
    /// when fewer than 6 rounds produced a usable ratio, because the interval would
    /// not reach 95% confidence; see <see cref="Outcome"/>.
    /// </remarks>
    public double Speedup =>
        _measuredSpeedup ?? ComputeSpeedup(Baseline.Statistics.Avg, Candidate.Statistics.Avg);

    /// <summary>
    /// Interval of the speedup ratio, when the comparison measured one (only
    /// <see cref="Benchmark.Compare(string, string, Action, string, Action, BenchmarkConfig?)"/>
    /// does). Null for a comparison built from two independent results, which has no
    /// interleaved rounds to pair.
    /// </summary>
    public ComparisonInterval? Interval => _interval;

    /// <summary>
    /// Verdict of the comparison:
    /// <see cref="ComparisonOutcome.Inconclusive"/> when the ratio is inside the 5%
    /// dead band or its interval still contains parity, and
    /// <see cref="ComparisonOutcome.Unknown"/> when no ratio could be computed —
    /// including the case where fewer rounds produced a usable ratio than the sign
    /// test needs to reach 95% confidence (<c>6</c>), so no interval and no verdict
    /// is stated at all.
    /// </summary>
    public ComparisonOutcome Outcome => Classify(Speedup, _interval);

    /// <summary>
    /// True when a ratio could be measured at all, i.e. <see cref="Outcome"/> is not
    /// <see cref="ComparisonOutcome.Unknown"/> (the two lanes may still be
    /// indistinguishable).
    /// </summary>
    public bool IsFasterKnown => Outcome != ComparisonOutcome.Unknown;

    /// <summary>
    /// True only when the candidate is faster <b>and</b> that is known: the ratio is
    /// outside the 5% dead band and its interval excludes parity. Noise no longer
    /// reads as an improvement — a difference inside the dead band returns false
    /// (see <see cref="Outcome"/> and <see cref="Interval"/>).
    /// </summary>
    public bool IsFaster => Outcome == ComparisonOutcome.Faster;

    /// <summary>Percentage improvement. Positive means candidate is faster.</summary>
    public double ImprovementPercent => (Speedup - 1) * 100;

    /// <summary>
    /// Maps a ratio (and its interval, when one was measured) to a verdict. "Faster"
    /// and "slower" need both the dead band and — when an interval is known — an
    /// interval that excludes parity.
    /// </summary>
    internal static ComparisonOutcome Classify(double speedup, ComparisonInterval? interval)
    {
        if (double.IsNaN(speedup) || double.IsInfinity(speedup) || !(speedup > 0))
            return ComparisonOutcome.Unknown;

        var above = 1.0 + DeadBand;
        var below = 1.0 / (1.0 + DeadBand);

        if (speedup > above && (interval is not { } fasterInterval || fasterInterval.Low > 1.0))
            return ComparisonOutcome.Faster;

        if (speedup < below && (interval is not { } slowerInterval || slowerInterval.High < 1.0))
            return ComparisonOutcome.Slower;

        return ComparisonOutcome.Inconclusive;
    }

    private static double ComputeSpeedup(double baselineAvg, double candidateAvg)
    {
        // Handle near-zero candidate average
        if (!(Math.Abs(candidateAvg) < 1e-12))
            return baselineAvg / candidateAvg;
        // If both are near-zero, treat as equal (speedup = 1)
        return Math.Abs(baselineAvg) < 1e-12
            ? 1.0
            :
            // Candidate is extremely fast (near-zero time)
            double.PositiveInfinity;
    }
}

/// <summary>
/// A suite of benchmark results, typically from a single test run.
/// </summary>
public sealed class BenchmarkSuite
{
    /// <summary>Name of the benchmark suite.</summary>
    public string Name { get; }

    /// <summary>Optional description.</summary>
    public string? Description { get; }

    /// <summary>Environment information.</summary>
    public EnvironmentInfo Environment { get; }

    /// <summary>All benchmark results in this suite.</summary>
    public IReadOnlyList<BenchmarkResult> Results { get; }

    /// <summary>All comparison results in this suite.</summary>
    public IReadOnlyList<ComparisonResult>? Comparisons { get; }

    /// <summary>When the suite was run.</summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>Total duration of the benchmark run.</summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Creates a benchmark suite.
    /// </summary>
    public BenchmarkSuite(
        string name,
        EnvironmentInfo environment,
        IReadOnlyList<BenchmarkResult> results,
        TimeSpan duration,
        string? description = null,
        IReadOnlyList<ComparisonResult>? comparisons = null,
        DateTime? timestamp = null
    )
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        Results = results ?? throw new ArgumentNullException(nameof(results));
        Duration = duration;
        Description = description;
        Comparisons = comparisons;

        if (timestamp.HasValue)
            Timestamp = timestamp.Value;
    }
}

/// <summary>
/// Information about the benchmark execution environment.
/// </summary>
public sealed class EnvironmentInfo
{
    /// <summary>Operating system description.</summary>
    public string Os { get; init; } = RuntimeInformation.OSDescription;

    /// <summary>Process architecture.</summary>
    public string Architecture { get; init; } = RuntimeInformation.ProcessArchitecture.ToString();

    /// <summary>.NET runtime version.</summary>
    public string RuntimeVersion { get; init; } = RuntimeInformation.FrameworkDescription;

    /// <summary>Number of logical processors.</summary>
    public int ProcessorCount { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Describes the execution mode of the benchmark process.
    /// Defaults to <see cref="RuntimeExecutionMode.Unknown"/> unless the caller sets it explicitly.
    /// </summary>
    public RuntimeExecutionMode ExecutionMode { get; init; } = RuntimeExecutionMode.Unknown;

    /// <summary>
    /// Whether the benchmark process is running with Native AOT.
    /// This compatibility property only returns true for a positively identified Native AOT process.
    /// </summary>
    public bool IsNativeAot => ExecutionMode == RuntimeExecutionMode.NativeAot;

    /// <summary>Build configuration (Debug/Release).</summary>
    /// <remarks>
    /// Detected from the entry assembly's <see cref="DebuggableAttribute"/> so
    /// NuGet consumers report their own build configuration rather than the
    /// configuration the library itself was compiled with. Requires the entry
    /// assembly's attribute metadata; falls back to this assembly's
    /// compile-time configuration when unavailable (AOT/trimming).
    /// </remarks>
    public string Configuration { get; init; } = DetectConfiguration();

    /// <summary>How the CPU cycle metric is collected on this runtime.</summary>
    public CpuCycleMeasurementKind CpuCycleMeasurement { get; init; } =
        Runner.GetCpuCycleMeasurementKind();

    /// <summary>Whether a cycle-like metric is available at all.</summary>
    public bool CpuCyclesAvailable { get; init; } = Runner.AreCpuCyclesAvailable();

    /// <summary>Whether the cycle metric represents meaningful CPU cycles rather than a proxy timer.</summary>
    public bool CpuCyclesAreMeaningful { get; init; } = Runner.HasMeaningfulCpuCycles();

    /// <summary>Custom environment tags.</summary>
    public IReadOnlyDictionary<string, string>? CustomTags { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{RuntimeVersion} | {Os} | {Architecture} | {DescribeExecutionMode(ExecutionMode)} | {Configuration} | Cycles: {DescribeCpuCycleMeasurement(CpuCycleMeasurement)}";

    // The only runtime reflection performed by the library. Wrapped in
    // try/catch so trimming/AOT can fall back to this assembly's compile-time
    // configuration (see the Configuration property remarks).
    private static string DetectConfiguration()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(EnvironmentInfo).Assembly;
            return ClassifyConfiguration(
                assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITTrackingEnabled
            );
        }
        catch
        {
            // Attribute reflection can fail under aggressive trimming.
            return ClassifyConfiguration(null);
        }
    }

    /// <summary>
    /// Maps the presence of JIT tracking (DebuggableAttribute) to a
    /// configuration name. Null means the attribute was unavailable; the
    /// compile-time configuration of this library is used as a fallback.
    /// </summary>
    internal static string ClassifyConfiguration(bool? isJitTrackingEnabled)
    {
        if (isJitTrackingEnabled.HasValue)
            return isJitTrackingEnabled.Value ? "Debug" : "Release";

#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    internal static string DescribeExecutionMode(RuntimeExecutionMode mode)
    {
        return mode switch
        {
            RuntimeExecutionMode.Jit => "JIT",
            RuntimeExecutionMode.NativeAot => "Native AOT",
            _ => "Unknown",
        };
    }

    internal static string DescribeCpuCycleMeasurement(CpuCycleMeasurementKind kind)
    {
        return kind switch
        {
            CpuCycleMeasurementKind.ThreadCycles => "ThreadCycles",
            CpuCycleMeasurementKind.PerfEventCpuCycles => "PerfEventCpuCycles",
            CpuCycleMeasurementKind.MonotonicClockProxy => "MonotonicClockProxy",
            _ => "Unsupported",
        };
    }
}
