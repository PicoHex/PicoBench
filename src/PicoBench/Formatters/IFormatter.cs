namespace PicoBench.Formatters;

/// <summary>
/// Interface for formatting benchmark results to various output formats.
/// </summary>
public interface IFormatter
{
    /// <summary>
    /// Format a single benchmark result.
    /// </summary>
    string Format(BenchmarkResult result);

    /// <summary>
    /// Format multiple benchmark results.
    /// </summary>
    string Format(IEnumerable<BenchmarkResult> results);

    /// <summary>
    /// Format a comparison result.
    /// </summary>
    string Format(ComparisonResult comparison);

    /// <summary>
    /// Format multiple comparison results.
    /// </summary>
    string Format(IEnumerable<ComparisonResult> comparisons);

    /// <summary>
    /// Format a complete benchmark suite.
    /// </summary>
    string Format(BenchmarkSuite suite);
}

/// <summary>
/// Options for customizing formatter output.
/// </summary>
public sealed class FormatterOptions
{
    /// <summary>Output directory for WriteToFile methods. If null, uses the file path as-is.</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>Include environment information in output.</summary>
    public bool IncludeEnvironment { get; init; } = true;

    /// <summary>Include timestamp in output.</summary>
    public bool IncludeTimestamp { get; init; } = true;

    /// <summary>Include GC information in output.</summary>
    public bool IncludeGcInfo { get; init; } = true;

    /// <summary>Include CPU cycle information in output.</summary>
    public bool IncludeCpuCycles { get; init; } = true;

    /// <summary>Include percentile columns (P50, P90, P95, P99).</summary>
    public bool IncludePercentiles { get; init; } = true;

    /// <summary>Number of decimal places for time values.</summary>
    public int TimeDecimalPlaces
    {
        get;
        init =>
            field =
                value >= 0
                    ? value
                    : throw new ArgumentOutOfRangeException(
                        nameof(TimeDecimalPlaces),
                        "TimeDecimalPlaces must be non-negative."
                    );
    } = 1;

    /// <summary>Number of decimal places for speedup values.</summary>
    public int SpeedupDecimalPlaces
    {
        get;
        init =>
            field =
                value >= 0
                    ? value
                    : throw new ArgumentOutOfRangeException(
                        nameof(SpeedupDecimalPlaces),
                        "SpeedupDecimalPlaces must be non-negative."
                    );
    } = 2;

    /// <summary>Label for the baseline result in comparisons.</summary>
    public string BaselineLabel { get; init; } = "Baseline";

    /// <summary>Label for the candidate result in comparisons.</summary>
    public string CandidateLabel { get; init; } = "Candidate";

    /// <summary>Default options.</summary>
    public static FormatterOptions Default { get; } = new();

    /// <summary>Compact options with fewer columns.</summary>
    public static FormatterOptions Compact { get; } =
        new() { IncludePercentiles = false, IncludeCpuCycles = false };

    /// <summary>Minimal options for simple output.</summary>
    public static FormatterOptions Minimal { get; } =
        new()
        {
            IncludeEnvironment = false,
            IncludeTimestamp = false,
            IncludeGcInfo = false,
            IncludeCpuCycles = false,
            IncludePercentiles = false,
        };

    /// <summary>
    /// Resolves the full file path, combining OutputDirectory if set.
    /// </summary>
    public string ResolvePath(string fileName)
    {
        if (fileName == null)
            throw new ArgumentNullException(nameof(fileName));
        return string.IsNullOrEmpty(OutputDirectory)
            ? fileName
            : Path.Combine(OutputDirectory!, fileName);
    }
}

/// <summary>
/// Base class for formatters with common helper methods.
/// Uses the Template Method pattern: public <see cref="Format(BenchmarkResult)"/> methods validate
/// inputs and delegate to <c>FormatCore</c> overrides in derived classes.
/// </summary>
public abstract class FormatterBase(FormatterOptions? options = null) : IFormatter
{
    /// <summary>Resolved formatter options for this formatter instance.</summary>
    protected FormatterOptions Options { get; } = options ?? FormatterOptions.Default;

    #region IFormatter — public entry points with null validation

    /// <summary>
    /// Format a single benchmark result.
    /// </summary>
    public string Format(BenchmarkResult result)
    {
        return result == null
            ? throw new ArgumentNullException(nameof(result))
            : FormatCore(result);
    }

    /// <summary>
    /// Format a sequence of benchmark results.
    /// </summary>
    public string Format(IEnumerable<BenchmarkResult> results)
    {
        return results == null
            ? throw new ArgumentNullException(nameof(results))
            : FormatCore(results);
    }

    /// <summary>
    /// Format a single comparison result.
    /// </summary>
    public string Format(ComparisonResult comparison)
    {
        return comparison == null
            ? throw new ArgumentNullException(nameof(comparison))
            : FormatCore(comparison);
    }

    /// <summary>
    /// Format a sequence of comparison results.
    /// </summary>
    public string Format(IEnumerable<ComparisonResult> comparisons)
    {
        return comparisons == null
            ? throw new ArgumentNullException(nameof(comparisons))
            : FormatCore(comparisons);
    }

    /// <summary>
    /// Format a benchmark suite.
    /// </summary>
    public string Format(BenchmarkSuite suite)
    {
        return suite == null ? throw new ArgumentNullException(nameof(suite)) : FormatCore(suite);
    }

    #endregion

    #region Protected abstract/virtual — override in derived classes

    /// <summary>
    /// Format a single result. Default delegates to the collection overload.
    /// Override for custom single-item formatting (e.g., <see cref="ConsoleFormatter"/>).
    /// </summary>
    protected virtual string FormatCore(BenchmarkResult result) => FormatCore([result]);

    /// <summary>
    /// Format a sequence of benchmark results.
    /// </summary>
    protected abstract string FormatCore(IEnumerable<BenchmarkResult> results);

    /// <summary>
    /// Format a single comparison. Default delegates to the collection overload.
    /// Override for custom single-item formatting.
    /// </summary>
    protected virtual string FormatCore(ComparisonResult comparison) => FormatCore([comparison]);

    /// <summary>
    /// Format a sequence of comparison results.
    /// </summary>
    protected abstract string FormatCore(IEnumerable<ComparisonResult> comparisons);

    /// <summary>
    /// Format a full benchmark suite.
    /// </summary>
    protected abstract string FormatCore(BenchmarkSuite suite);

    #endregion

    /// <summary>
    /// Format a nanosecond value using the configured precision.
    /// </summary>
    protected string FormatTime(double nanoseconds) =>
        FormatMetric(nanoseconds, Options.TimeDecimalPlaces);

    /// <summary>
    /// Invariant fixed-point rendering of one metric: nanoseconds, RSD percent,
    /// CPU cycles or seconds.
    /// <para>
    /// Batch 2 · 2.1 (B2-H1): the library's "invariant culture" policy used to
    /// live in <see cref="FormatTime"/> and <see cref="FormatSpeedup"/> only,
    /// while the RSD/cycle/duration/speedup figures the four formatters
    /// <em>also</em> print went through <c>$"{x:F1}"</c>, i.e. through
    /// <see cref="CultureInfo.CurrentCulture"/>. A de-DE machine therefore wrote
    /// <c>12,3</c> into a column whose neighbours used <c>.</c>. Every numeric
    /// cell now goes through this one method, and no formatter may interpolate a
    /// metric with a format specifier again.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than <c>protected</c> because
    /// <see cref="SummaryFormatter"/> is a static helper outside the base class
    /// and prints the same speedup/duration figures the box of the other
    /// formatters does.
    /// </para>
    /// </summary>
    internal static string FormatMetric(double value, int decimals) =>
        value.ToString(
            "F" + decimals.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture
        );

    /// <summary>
    /// Timestamp of a run: Gregorian calendar, invariant culture, second
    /// precision. Interpolating a <see cref="DateTime"/> with
    /// <c>yyyy-MM-dd HH:mm:ss</c> formats it in
    /// <see cref="CultureInfo.CurrentCulture"/>'s <em>calendar</em>, which turns
    /// "2026-11-02 UTC" into "2569-11-02 UTC" on th-TH and "1448-05-22 UTC" on
    /// ar-SA — a wrong date wearing a UTC label (Batch 2 · 2.1).
    /// </summary>
    internal static string FormatTimestamp(DateTime timestamp) =>
        timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Duration as seconds with two decimals plus its unit (invariant).</summary>
    internal static string FormatDuration(TimeSpan duration) =>
        FormatMetric(duration.TotalSeconds, 2) + "s";

    /// <summary>
    /// Invariant speedup ratio with its <c>x</c> suffix. Shared so the four
    /// formatters cannot render the same ratio with two different precisions or
    /// two different decimal separators (Batch 2 · 2.1 / 2.2).
    /// </summary>
    internal static string FormatSpeedupValue(double? speedup, int decimals) =>
        speedup is { } value
            ? FormatMetric(value, decimals) + "x"
            : NotCollected;

    /// <summary>
    /// Format a speedup ratio using the configured precision.
    /// </summary>
    protected string FormatSpeedup(double speedup)
    {
        return speedup.ToString($"F{Options.SpeedupDecimalPlaces}", CultureInfo.InvariantCulture)
            + "x";
    }

    /// <summary>
    /// Cell text every formatter uses for "this value was not collected".
    /// Kept in one place so the four output formats cannot drift apart again:
    /// a <c>null</c> GC counter must never be rendered as <c>0</c>.
    /// </summary>
    internal const string NotCollected = "N/A";

    /// <summary>
    /// Format GC generation counts as <c>gen0/gen1/gen2</c>.
    /// <para>
    /// <c>null</c> (counters not collected, e.g. <c>CpuOnly</c>) renders as
    /// <see cref="NotCollected"/> instead of zeros, and an approximate count
    /// (async / process-wide counters) keeps the <c>~</c> marker that
    /// <see cref="GcInfo.ToString"/> already defines.
    /// </para>
    /// </summary>
    protected static string FormatGcInfo(GcInfo? gc)
    {
        if (gc is null)
            return NotCollected;

        var counts =
            $"{gc.Gen0.ToString(CultureInfo.InvariantCulture)}/"
            + $"{gc.Gen1.ToString(CultureInfo.InvariantCulture)}/"
            + gc.Gen2.ToString(CultureInfo.InvariantCulture);
        return gc.IsApproximate ? "~" + counts : counts;
    }

    /// <summary>
    /// Get a visual indicator for the speedup magnitude.
    /// </summary>
    public static string GetSpeedupIndicator(double speedup)
    {
        return speedup switch
        {
            >= 10 => "***",
            >= 5 => "**",
            >= 2 => "*",
            >= 1 => "",
            _ => "(!)",
        };
    }

    /// <summary>
    /// Write content to a file, creating the directory if it doesn't exist.
    /// </summary>
    protected static void WriteToFileInternal(string filePath, string content)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(filePath, content);
    }
}
