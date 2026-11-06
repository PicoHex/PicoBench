namespace PicoBench.Formatters;

/// <summary>
/// Configuration for summary output.
/// </summary>
public sealed class SummaryOptions
{
    /// <summary>Title for the summary box.</summary>
    public string Title { get; init; } = "SUMMARY";

    /// <summary>Width of the summary box (in characters, at least 2).</summary>
    public int BoxWidth
    {
        get;
        init =>
            field =
                value >= 2
                    ? value
                    : throw new ArgumentOutOfRangeException(
                        nameof(BoxWidth),
                        "BoxWidth must be at least 2."
                    );
    } = 111;

    /// <summary>Label for the candidate (faster) side in comparisons.</summary>
    public string CandidateLabel { get; init; } = "Candidate";

    /// <summary>Label for wins count.</summary>
    public string WinsLabel { get; init; } = "wins";

    /// <summary>Show grouping by category.</summary>
    public bool GroupByCategory { get; init; } = true;

    /// <summary>Show detailed comparison table.</summary>
    public bool ShowDetailedTable { get; init; } = true;

    /// <summary>Show duration in summary.</summary>
    public bool ShowDuration { get; init; } = true;

    /// <summary>Formatter options for the detailed table.</summary>
    public FormatterOptions? TableOptions { get; init; }

    /// <summary>Default summary options.</summary>
    public static SummaryOptions Default { get; } = new();
}

/// <summary>
/// Formats benchmark comparison summaries for console output.
/// Accepts an optional <see cref="IFormatter"/> to render the detailed table,
/// defaulting to <see cref="ConsoleFormatter"/> if none is provided.
/// </summary>
public static class SummaryFormatter
{
    /// <summary>
    /// Format a summary of comparison results.
    /// </summary>
    /// <param name="comparisons">Comparison results to summarize.</param>
    /// <param name="duration">Optional total benchmark duration.</param>
    /// <param name="options">Optional summary display options.</param>
    /// <param name="detailFormatter">Optional formatter for the detailed table. Defaults to <see cref="ConsoleFormatter"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="comparisons"/> is <c>null</c>. An empty sequence is not an
    /// error and returns a "no results" message instead.
    /// </exception>
    public static string Format(
        IEnumerable<ComparisonResult> comparisons,
        TimeSpan? duration = null,
        SummaryOptions? options = null,
        IFormatter? detailFormatter = null
    )
    {
        if (comparisons == null)
            throw new ArgumentNullException(nameof(comparisons));
        options ??= SummaryOptions.Default;
        var list = comparisons.ToList();
        var sb = new StringBuilder();

        if (list.Count == 0)
        {
            sb.AppendLine("No comparison results.");
            return sb.ToString();
        }

        // Header box
        AppendBoxLine(sb, options.BoxWidth, '╔', '═', '╗');
        AppendCenteredLine(sb, options.Title, options.BoxWidth);
        AppendBoxLine(sb, options.BoxWidth, '╚', '═', '╝');
        sb.AppendLine();

        // Group by category
        if (options.GroupByCategory)
        {
            var byCategory = list.GroupBy(c => c.Category ?? "Other").OrderBy(g => g.Key);

            foreach (var group in byCategory)
            {
                var groupRatios = SpeedupAggregate.From(group);
                var groupNote =
                    groupRatios.Excluded == 0 ? "" : " — " + groupRatios.DescribeExcluded();
                sb.AppendLine(
                    $"▶ {TextSanitizer.ForTerminal(group.Key)}: {FormatterBase.FormatSpeedupValue(groupRatios.Average, SummarySpeedupDecimals)} average speedup{groupNote}"
                );

                foreach (var c in group)
                {
                    var indicator = FormatterBase.GetSpeedupIndicator(c.Speedup);
                    sb.AppendLine(
                        $"   {DisplayWidth.PadRight(TextSanitizer.ForTerminal(c.Name), 35)}: {FormatterBase.FormatSpeedupValue(c.Speedup, SummarySpeedupDecimals)} {indicator}"
                    );
                }
                sb.AppendLine();
            }
        }

        // Overall stats
        var wins = list.Count(c => c.IsFaster);
        var total = list.Count;

        // Batch 2 · 2.2 (B2-M2): a single ∞ (or NaN) scenario used to turn the
        // whole box into ∞ and hide every other number. The aggregate only
        // averages the ratios that exist, and the box says how many it skipped.
        var ratios = SpeedupAggregate.From(list);

        AppendBoxLine(sb, options.BoxWidth, '╔', '═', '╗');
        AppendPaddedLine(
            sb,
            $"  {options.CandidateLabel} {options.WinsLabel}: {wins} / {total} scenarios",
            options.BoxWidth
        );
        AppendPaddedLine(
            sb,
            $"  Average speedup: {FormatterBase.FormatSpeedupValue(ratios.Average, SummarySpeedupDecimals)} faster",
            options.BoxWidth
        );
        AppendPaddedLine(
            sb,
            $"  Maximum speedup: {FormatterBase.FormatSpeedupValue(ratios.Maximum, SummarySpeedupDecimals)} faster",
            options.BoxWidth
        );

        if (ratios.Excluded > 0)
            AppendPaddedLine(sb, $"  Note: {ratios.DescribeExcluded()}", options.BoxWidth);

        if (options.ShowDuration && duration.HasValue)
        {
            AppendPaddedLine(
                sb,
                $"  Duration: {FormatterBase.FormatMetric(duration.Value.TotalSeconds, 2)}s",
                options.BoxWidth
            );
        }

        AppendBoxLine(sb, options.BoxWidth, '╚', '═', '╝');

        // Detailed table
        if (!options.ShowDetailedTable)
            return sb.ToString();
        sb.AppendLine();
        sb.AppendLine("▶ Detailed Results:");

        var formatter =
            detailFormatter
            ?? new ConsoleFormatter(options.TableOptions ?? FormatterOptions.Default);
        sb.Append(formatter.Format(list));

        return sb.ToString();
    }

    /// <summary>
    /// Write summary directly to console.
    /// </summary>
    public static void Write(
        IEnumerable<ComparisonResult> comparisons,
        TimeSpan? duration = null,
        SummaryOptions? options = null
    )
    {
        if (comparisons == null)
            throw new ArgumentNullException(nameof(comparisons));
        Console.Write(Format(comparisons, duration, options));
    }

    /// <summary>
    /// Write summary for a benchmark suite directly to console.
    /// </summary>
    public static void Write(BenchmarkSuite suite, SummaryOptions? options = null)
    {
        if (suite == null)
            throw new ArgumentNullException(nameof(suite));
        if (suite.Comparisons == null || suite.Comparisons.Count == 0)
        {
            Console.WriteLine("No comparison results in suite.");
            return;
        }

        Console.Write(Format(suite.Comparisons, suite.Duration, options));
    }

    #region Helpers

    /// <summary>Decimals of every speedup figure the summary box prints.</summary>
    private const int SummarySpeedupDecimals = 2;

    private static void AppendBoxLine(StringBuilder sb, int width, char left, char fill, char right)
    {
        sb.Append(left);
        sb.Append(fill, width - 2);
        sb.AppendLine(right.ToString());
    }

    private static void AppendCenteredLine(StringBuilder sb, string text, int width)
    {
        var innerWidth = width - 2;
        var fitted = Fit(text, innerWidth);
        var textWidth = DisplayWidth.Of(fitted);
        var padding = (innerWidth - textWidth) / 2;
        sb.Append('║');
        sb.Append(' ', padding);
        sb.Append(fitted);
        sb.Append(' ', innerWidth - padding - textWidth);
        sb.AppendLine("║");
    }

    private static void AppendPaddedLine(StringBuilder sb, string text, int width)
    {
        var innerWidth = width - 2;
        var fitted = Fit(text, innerWidth);
        sb.Append('║');
        sb.Append(fitted);
        sb.Append(' ', Math.Max(0, innerWidth - DisplayWidth.Of(fitted)));
        sb.AppendLine("║");
    }

    /// <summary>
    /// Fits user text into <paramref name="width"/> <b>display cells</b> and strips
    /// the control characters that could move the cursor out of the frame (Batch 2 ·
    /// 2.5, B2-H4/L4): the box is only a box if every line of it is the same number
    /// of cells wide.
    /// </summary>
    private static string Fit(string text, int width) =>
        DisplayWidth.Truncate(TextSanitizer.ForTerminal(text), width);

    #endregion
}
