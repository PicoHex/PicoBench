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

    /// <summary>
    /// How per-operation figures are rendered, or <c>null</c> for the formatter's
    /// own default (Batch 2 · 2.4, B2-M5/M6).
    /// <para>
    /// <see cref="MetricPrecision.Display"/> scales a table to its readable unit
    /// and names that unit in the header; <see cref="MetricPrecision.Raw"/> stays
    /// in nanoseconds. The default is per format on purpose: Console, Markdown and
    /// HTML are read by a human (<c>Display</c>), while CSV is read by a program
    /// and its columns are named <c>_ns</c>, so <see cref="CsvFormatter"/> always
    /// writes raw nanoseconds and ignores this setting.
    /// </para>
    /// </summary>
    public MetricPrecision? PrecisionProfile { get; init; }

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
    /// Format a nanosecond value using the configured precision, <b>without</b> any
    /// unit scaling: the raw tier.
    /// <para>
    /// Batch 2 · 2.4 (B2-M6): the built-in display formatters no longer call this
    /// for a table cell — a cell goes through <see cref="MetricCell"/> under the
    /// table's <see cref="TimeUnit"/>, so a five-second benchmark reads
    /// <c>5.0</c> under an <c>(s)</c> header instead of <c>5000000000.0</c> under
    /// an <c>(ns)</c> one. It stays available (a custom formatter may want the raw
    /// nanoseconds in a fixed unit) and stays in nanoseconds on purpose.
    /// </para>
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
    /// A non-finite value (NaN, ±∞) is <b>not</b> a number this library can
    /// report: it renders as <see cref="NotCollected"/>. Batch 2 · 2.2 (B2-M2):
    /// <c>("F1")</c> on a double returns the word "Infinity" under the invariant
    /// culture and "∞"/"ليس رقم" under others, so a <c>+∞</c> ratio printed as the
    /// pseudo token <c>Infinityx</c> in one column and <c>∞x</c> in the next.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than <c>protected</c> because
    /// <see cref="SummaryFormatter"/> is a static helper outside the base class
    /// and prints the same speedup/duration figures the box of the other
    /// formatters does.
    /// </para>
    /// </summary>
    internal static string FormatMetric(double value, int decimals) =>
        IsFinite(value)
            ? value.ToString(
                "F" + decimals.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture
            )
            : NotCollected;

    /// <summary>
    /// True when <paramref name="value"/> is a real number. The library targets
    /// <c>netstandard2.0</c>, where <c>double.IsFinite</c> does not exist, so the
    /// two-part test lives here instead of being open-coded per formatter.
    /// </summary>
    internal static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

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

    /// <summary>
    /// The precision profile actually in effect: the caller's
    /// <see cref="FormatterOptions.PrecisionProfile"/>, or this format's own
    /// default. Batch 2 · 2.4 (B2-M5/M6).
    /// </summary>
    internal MetricPrecision Precision => Options.PrecisionProfile ?? DefaultPrecision;

    /// <summary>
    /// This format's own precision profile when the caller did not ask for one.
    /// <c>Display</c> for the three human-readable formats, <c>Raw</c> for
    /// <see cref="CsvFormatter"/> (whose columns are named <c>_ns</c>).
    /// <c>internal</c> rather than <c>protected</c> so that choosing a default is
    /// not a new public extension point.
    /// </summary>
    internal virtual MetricPrecision DefaultPrecision => MetricPrecision.Display;

    /// <summary>
    /// The unit one per-operation table is written in: nanoseconds under the
    /// <see cref="MetricPrecision.Raw"/> profile, otherwise the readable unit of
    /// the table's largest value. Every per-operation column of one table must use
    /// the same <see cref="TimeUnit"/>, so the unit is computed from the whole
    /// column set once instead of per cell.
    /// </summary>
    internal TimeUnit TableUnit(IEnumerable<double> perOpValues) =>
        Precision == MetricPrecision.Raw
            ? TimeUnit.Nanoseconds
            : TimeUnit.For(MaxFinite(perOpValues));

    /// <summary>The largest finite value of a sequence; 0 when there is none.</summary>
    internal static double MaxFinite(IEnumerable<double> values)
    {
        var max = 0.0;
        foreach (var value in values)
        {
            if (IsFinite(value) && value > max)
                max = value;
        }
        return max;
    }

    /// <summary>
    /// One per-operation cell under a table's unit: the metric with
    /// <see cref="FormatterOptions.TimeDecimalPlaces"/> decimals, or
    /// <see cref="NotCollected"/> when the value is not a number.
    /// </summary>
    internal string MetricCell(TimeUnit unit, double nanoseconds) =>
        FormatMetric(unit.Of(nanoseconds), Options.TimeDecimalPlaces);

    /// <summary>
    /// An upper bound (<c>&lt;limit</c>) under a table's unit. The bound is a floor,
    /// not a round number: under a coarse unit (a <c>0.345 ns</c> limit in a
    /// <c>µs</c> column) one decimal would print <c>&lt;0.0</c>, which claims the
    /// value is bounded by zero. The precision therefore widens until the first
    /// significant digit appears, and never narrows below the configured one.
    /// </summary>
    internal string BoundCell(TimeUnit unit, double nanoseconds) =>
        "<" + BoundCellValue(unit, nanoseconds);

    /// <summary>
    /// The number of an upper bound, without the <c>&lt;</c>: HTML has to write the
    /// sign as an entity, so each format adds its own marker to the same number.
    /// </summary>
    internal string BoundCellValue(TimeUnit unit, double nanoseconds)
    {
        var scaled = unit.Of(nanoseconds);
        var decimals = Options.TimeDecimalPlaces;
        while (decimals < 9 && scaled != 0 && Math.Abs(scaled) < Math.Pow(10, -decimals) / 2)
            decimals++;
        return FormatMetric(scaled, decimals);
    }

    /// <summary>Duration as seconds with two decimals plus its unit (invariant).</summary>
    internal static string FormatDuration(TimeSpan duration) =>
        FormatMetric(duration.TotalSeconds, 2) + "s";

    /// <summary>
    /// Invariant speedup ratio with its <c>x</c> suffix. Shared so the four
    /// formatters cannot render the same ratio with two different precisions or
    /// two different decimal separators (Batch 2 · 2.1 / 2.2).
    /// </summary>
    internal static string FormatSpeedupValue(double? speedup, int decimals) =>
        speedup is { } value && IsFinite(value)
            ? FormatMetric(value, decimals) + "x"
            : NotCollected;

    /// <summary>
    /// Format a speedup ratio using the configured precision.
    /// <para>
    /// A <c>NaN</c> ratio (0/0, or fewer paired rounds than the sign test needs)
    /// and a <c>+∞</c> ratio (the candidate cost is zero) both render as
    /// <see cref="NotCollected"/>: appending <c>x</c> to a non-finite number is
    /// what produced the token <c>Infinityx</c> (Batch 2 · 2.2, B2-M2). The
    /// comparison's verdict — <c>unknown (not measurable)</c> — carries the "why".
    /// </para>
    /// </summary>
    protected string FormatSpeedup(double speedup) =>
        FormatSpeedupValue(speedup, Options.SpeedupDecimalPlaces);

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
        // A ratio that does not exist cannot be indicted as "fast" or "slow":
        // no marker is attached to the N/A cell it renders as (Batch 2 · 2.2).
        if (!IsFinite(speedup))
            return "";

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
