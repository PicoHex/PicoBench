namespace PicoBench.Tests.Batch0;

/// <summary>
/// Shared helpers for the Batch 0 "known cost" regression net
/// (<c>.review-2026-11/00-FINAL-VERDICT.md</c> §5, item 0.0).
/// <para>
/// The helpers deliberately stay on the public API surface: they call
/// <see cref="Benchmark"/> / <see cref="Runner"/> the way a consumer would and
/// never re-implement a measurement formula, so a test can only pass when the
/// production code really reports the cost.
/// </para>
/// </summary>
internal static class Batch0Support
{
    /// <summary>
    /// The additive per-operation floor Batch 0 · 0.1 promises for an empty
    /// delegate: ≤0.5 ns/op, or an explicitly disclosed overhead.
    /// </summary>
    internal const double ResolutionFloorNanoseconds = 0.5;

    /// <summary>
    /// Configuration for short runs that are *not* auto-calibrated, so the
    /// requested iteration count reaches the timing loop unchanged. Warmup is
    /// pinned to 0 because <see cref="Benchmark.Run(string, Action, Action?, BenchmarkConfig?, Action?, Action?)"/>
    /// would otherwise warm up 1000 times.
    /// </summary>
    internal static BenchmarkConfig UncalibratedConfig(
        int sampleCount = 20,
        int iterationsPerSample = 5_000
    ) =>
        new()
        {
            WarmupIterations = 0,
            SampleCount = sampleCount,
            IterationsPerSample = iterationsPerSample,
            AutoCalibrateIterations = false,
            ForceGcBeforeBenchmark = false,
            RetainSamples = true,
        };

    /// <summary>Arithmetic median; used to damp single-run noise in timing tests.</summary>
    internal static double Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
            return double.NaN;

        var sorted = values.OrderBy(static v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>Median of <paramref name="count"/> repeated measurements of the same statistic.</summary>
    internal static double MedianOf(Func<double> measure, int count)
    {
        var values = new List<double>(count);
        for (var i = 0; i < count; i++)
            values.Add(measure());

        return Median(values);
    }

    // Property names a fix would plausibly use to disclose the measured
    // delegate/loop overhead (Batch 0 · 0.1: "Overhead 与'有效分辨率'进 Statistics").
    private static readonly string[] OverheadSignalNames =
    [
        "Overhead",
        "OverheadPerOp",
        "OverheadNanoseconds",
        "DelegateOverhead",
    ];

    // Property names a fix would plausibly use for "this number did not reach
    // the resolution floor / was not measured" (Batch 0 · 0.1 / 0.3).
    private static readonly string[] ResolutionSignalNames =
    [
        "ResolutionLimited",
        "IsResolutionLimited",
        "BelowResolution",
        "IsBelowResolution",
        "ResolutionLimitNanoseconds",
        "Unmeasured",
        "IsUnmeasured",
    ];

    /// <summary>
    /// True when <see cref="Statistics"/> exposes the measured empty-delegate
    /// overhead, i.e. the caller can subtract it themselves.
    /// </summary>
    internal static bool TryGetDisclosedOverhead(
        Statistics statistics,
        out double overheadNanoseconds,
        out string detail
    )
    {
        overheadNanoseconds = 0;
        var type = typeof(Statistics);

        foreach (var name in OverheadSignalNames)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !IsNumeric(property.PropertyType))
                continue;

            var raw = property.GetValue(statistics);
            if (raw is null)
                continue;

            overheadNanoseconds = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            detail =
                !double.IsNaN(overheadNanoseconds) && overheadNanoseconds > 0
                    ? $"Statistics.{name} = {overheadNanoseconds:F3} ns"
                    : $"Statistics.{name} present but {overheadNanoseconds:F3} ns (no usable overhead disclosed)";
            return !double.IsNaN(overheadNanoseconds) && overheadNanoseconds > 0;
        }

        detail = $"Statistics exposes none of [{string.Join(", ", OverheadSignalNames)}]";
        return false;
    }

    /// <summary>
    /// True when <see cref="Statistics"/> flags the value as
    /// resolution-limited / unmeasured rather than a genuine measurement.
    /// </summary>
    internal static bool HasResolutionLimitedSignal(Statistics statistics, out string detail)
    {
        var type = typeof(Statistics);

        foreach (var name in ResolutionSignalNames)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
                continue;

            var raw = property.GetValue(statistics);
            switch (raw)
            {
                case bool flag when flag:
                    detail = $"Statistics.{name} = true";
                    return true;
                case bool:
                    detail = $"Statistics.{name} = false";
                    return false;
                default:
                    if (raw is not null && IsNumeric(property.PropertyType))
                    {
                        var value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        detail = $"Statistics.{name} = {value:F3} ns";
                        return !double.IsNaN(value) && value > 0;
                    }

                    break;
            }
        }

        detail = $"Statistics exposes none of [{string.Join(", ", ResolutionSignalNames)}]";
        return false;
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(double)
        || type == typeof(float)
        || type == typeof(decimal)
        || type == typeof(int)
        || type == typeof(long)
        || type == typeof(TimeSpan);

    /// <summary>
    /// Writes a diagnostic line to the TUnit test output (falls back to the
    /// console when no test context is active, e.g. under a debugger).
    /// </summary>
    internal static void Log(string message)
    {
        var writer = TestContext.Current?.OutputWriter;
        if (writer is null)
            Console.WriteLine(message);
        else
            writer.WriteLine(message);
    }
}
