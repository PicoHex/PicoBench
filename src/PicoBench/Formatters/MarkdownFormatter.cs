namespace PicoBench.Formatters;

/// <summary>
/// Formats benchmark results as Markdown tables for documentation.
/// </summary>
public sealed class MarkdownFormatter(FormatterOptions? options = null) : FormatterBase(options)
{
    /// <inheritdoc />
    protected override string FormatCore(IEnumerable<BenchmarkResult> results)
    {
        var sb = new StringBuilder();
        var list = results.ToList();

        if (list.Count == 0)
            return "*No results.*";

        AppendResultsTable(sb, list);
        return sb.ToString();
    }

    /// <inheritdoc />
    protected override string FormatCore(IEnumerable<ComparisonResult> comparisons)
    {
        var sb = new StringBuilder();
        var list = comparisons.ToList();

        if (list.Count == 0)
            return "*No comparisons.*";

        AppendComparisonsTable(sb, list);
        return sb.ToString();
    }

    /// <inheritdoc />
    protected override string FormatCore(BenchmarkSuite suite)
    {
        var sb = new StringBuilder();

        // Title. Batch 2 · 2.5 (B2-H4): a name is data, not structure. A line break
        // collapses to a space and `#` is escaped, so "S + break + ## Injected" can
        // no longer become a real second-level heading of the generated document.
        sb.AppendLine($"# {TextSanitizer.ForMarkdownLine(suite.Name)}");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(suite.Description))
        {
            sb.AppendLine(TextSanitizer.ForMarkdownLine(suite.Description!));
            sb.AppendLine();
        }

        // Environment
        if (Options.IncludeEnvironment)
        {
            sb.AppendLine("## Environment");
            sb.AppendLine();
            sb.AppendLine($"**{suite.Environment}**");
            sb.AppendLine();
            sb.AppendLine(
                $"- CPU Counter: `{EnvironmentInfo.DescribeCpuCycleMeasurement(suite.Environment.CpuCycleMeasurement)}`"
            );

            if (!suite.Environment.CpuCyclesAvailable)
                sb.AppendLine("- CPU Counter Note: unavailable on this platform/runtime");
            else if (!suite.Environment.CpuCyclesAreMeaningful)
                sb.AppendLine("- CPU Counter Note: proxy timing source, not true CPU cycles");

            sb.AppendLine();
        }

        if (Options.IncludeTimestamp)
        {
            sb.AppendLine(
                $"> Benchmark run at {FormatTimestamp(suite.Timestamp)} UTC ({FormatDuration(suite.Duration)})"
            );
            sb.AppendLine();
        }

        // Results
        if (suite.Results.Count > 0)
        {
            sb.AppendLine("## Results");
            sb.AppendLine();
            AppendResultsTable(sb, suite.Results.ToList());
            sb.AppendLine();
        }

        // Comparisons
        if (!(suite.Comparisons?.Count > 0))
            return sb.ToString();
        sb.AppendLine("## Comparisons");
        sb.AppendLine();
        AppendComparisonsTable(sb, suite.Comparisons.ToList());

        // Summary
        var wins = suite.Comparisons.Count(c => c.IsFaster);
        var total = suite.Comparisons.Count;

        // Batch 2 · 2.2 (B2-M2): the aggregate ignores the ratios that do not
        // exist (NaN / ±∞) and the block says how many it ignored.
        var ratios = SpeedupAggregate.From(suite.Comparisons);

        sb.AppendLine();
        sb.AppendLine("### Summary");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine($"Candidate wins: {wins} / {total}");
        sb.AppendLine(
            $"Average speedup: {FormatSpeedupValue(ratios.Average, Options.SpeedupDecimalPlaces)}"
        );
        sb.AppendLine(
            $"Maximum speedup: {FormatSpeedupValue(ratios.Maximum, Options.SpeedupDecimalPlaces)}"
        );
        if (ratios.Excluded > 0)
            sb.AppendLine($"Ratios: {ratios.DescribeExcluded()}");
        sb.AppendLine("```");

        return sb.ToString();
    }

    #region Results Table

    private void AppendResultsTable(StringBuilder sb, List<BenchmarkResult> results)
    {
        // Batch 2 · 2.4 (B2-M5/M6): the same table unit as Console, chosen by the
        // same rule and printed in the same place in the header, so the two formats
        // cannot disagree about what a cell means.
        var unit = TableUnit(PerOpValues(results));

        // Header
        sb.Append($"| Name | Avg ({unit.Symbol}) | P50 ({unit.Symbol}) ");
        if (Options.IncludePercentiles)
            sb.Append(
                $"| P90 ({unit.Symbol}) | P95 ({unit.Symbol}) | P99 ({unit.Symbol}) | StdErr ({unit.Symbol}) | RSD (%) "
            );
        if (Options.IncludeCpuCycles)
            sb.Append("| " + CpuCyclesHeader + " ");
        if (Options.IncludeGcInfo)
            sb.Append("| GC (0/1/2) | GC B ");
        sb.AppendLine("|");

        // Separator
        sb.Append("|------|----------|----------");
        if (Options.IncludePercentiles)
            sb.Append("|----------|----------|----------|-------------|---------");
        if (Options.IncludeCpuCycles)
            sb.Append("|----------");
        if (Options.IncludeGcInfo)
            sb.Append("|------------|------");
        sb.AppendLine("|");

        // Rows
        foreach (var result in results)
        {
            var s = result.Statistics;
            var below = MeasurementLimitNote(s);
            sb.Append(
                $"| {Escape(result.Name)} | {FormatPerOp(s, s.Avg, unit)}{below} | {FormatPerOp(s, s.P50, unit)}{below} "
            );
            if (Options.IncludePercentiles)
                sb.Append(
                    $"| {FormatPerOp(s, s.P90, unit)}{below} | {FormatPerOp(s, s.P95, unit)}{below} | {FormatPerOp(s, s.P99, unit)}{below} | {FormatPerOp(s, s.StandardError, unit)}{below} | {FormatMetric(s.RelativeStdDevPercent, 1)} "
                );
            if (Options.IncludeCpuCycles)
                sb.Append($"| {FormatCycles(s)} ");
            if (Options.IncludeGcInfo)
                sb.Append($"| {FormatGcInfo(s.GcInfo)} | {FormatGcBytes(s.GcInfo)} ");
            sb.AppendLine("|");
        }

        AppendMeasurementLimitNote(sb, results.Select(static r => r.Statistics));

        var noisy = results.Where(static r => r.Statistics.HasHighVariance).ToList();
        if (noisy.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(
                "> **noise** - high variance (RSD >= 10%): "
                    + string.Join(
                        ", ",
                        noisy.Select(static r =>
                            $"{Escape(r.Name)} ({FormatMetric(r.Statistics.RelativeStdDevPercent, 1)}% RSD)"
                        )
                    )
                    + "; treat the difference as unresolved."
            );
        }
    }

    /// <summary>
    /// Every per-operation figure the results table holds; the table's unit is
    /// chosen from the largest of them (Batch 2 · 2.4).
    /// </summary>
    private static IEnumerable<double> PerOpValues(List<BenchmarkResult> results)
    {
        foreach (var result in results)
        {
            var s = result.Statistics;
            yield return s.Avg;
            yield return s.P50;
            yield return s.P90;
            yield return s.P95;
            yield return s.P99;
            yield return s.StandardError;
        }
    }

    /// <summary>
    /// Renders a per-operation cell in the table's unit. A value the run could not
    /// resolve above the harness floor is written as an upper bound
    /// (<c>&lt;limit</c>), and a run whose clock never advanced is written as "not
    /// measured".
    /// </summary>
    private string FormatPerOp(Statistics statistics, double nanoseconds, TimeUnit unit) =>
        statistics.Unmeasured
            ? NotCollected
            : statistics.ResolutionLimited
                ? BoundCell(unit, statistics.ResolutionLimitNanoseconds)
                : MetricCell(unit, nanoseconds);

    /// <summary>
    /// Renders the cycle cell. <c>~</c> marks a process-wide counter or a
    /// monotonic-clock proxy rather than a thread-scoped CPU cycle count.
    /// </summary>
    private static string FormatCycles(Statistics statistics) =>
        statistics.CpuCyclesAreApproximate
            ? "~" + FormatMetric(statistics.CpuCyclesPerOp, 0)
            : FormatMetric(statistics.CpuCyclesPerOp, 0);

    /// <summary>
    /// Header of the CPU-cycle column, spelled the same way in the results table
    /// and in the comparison table (T4 · format parity): both tables hold the same
    /// kind of cell — a <c>~</c>-marked proxy or a plain count — so they must name
    /// it the same way.
    /// </summary>
    private const string CpuCyclesHeader = "CPU Cycle";

    #endregion

    #region Comparisons Table

    private void AppendComparisonsTable(StringBuilder sb, List<ComparisonResult> comparisons)
    {
        // Flatten to rows with Provider column
        var rows = new List<(string TestCase, string Provider, Statistics Stats, string Speedup)>();
        foreach (var c in comparisons)
        {
            var indicator = GetSpeedupIndicator(c.Speedup);
            rows.Add(
                (
                    c.Name,
                    Options.CandidateLabel,
                    c.Candidate.Statistics,
                    $"**{FormatSpeedup(c.Speedup)}** {indicator}"
                )
            );
            rows.Add((c.Name, Options.BaselineLabel, c.Baseline.Statistics, ""));
        }

        // Batch 2 · 2.4 (B2-M5): `P50 | P90 | P99 | StdErr` used to be bare headers
        // in this table while Console wrote `Avg (ns)` — a reader had to guess the
        // unit, and the four formats have to agree on it. One unit for the table,
        // named in every per-operation header.
        var unit = TableUnit(rows.Select(r => r.Stats).SelectMany(PerOpValues));

        // Header
        sb.Append($"| Test Case | Avg ({unit.Symbol}) | Speedup |");
        if (Options.IncludePercentiles)
            sb.Append(
                $" P50 ({unit.Symbol}) | P90 ({unit.Symbol}) | P99 ({unit.Symbol}) | StdErr ({unit.Symbol}) | RSD (%) |"
            );
        if (Options.IncludeCpuCycles)
            sb.Append(" " + CpuCyclesHeader + " |");
        if (Options.IncludeGcInfo)
            sb.Append(" GC | GC B |");
        sb.AppendLine();

        // Separator
        sb.Append("|-----------|----------|---------|");
        if (Options.IncludePercentiles)
            sb.Append("-----|-----|-----|--------|---------|");
        if (Options.IncludeCpuCycles)
            sb.Append("-----|");
        if (Options.IncludeGcInfo)
            sb.Append("-----|------|");
        sb.AppendLine();

        // Rows. T4 · format parity: a comparison row marks its per-operation cells
        // exactly the way the results table marks them — the `(unmeasured)` /
        // `(below resolution)` suffix the results row carries — so one and the same
        // `Statistics` reads the same in both tables of the document.
        foreach (var row in rows)
        {
            var stats = row.Stats;
            var below = MeasurementLimitNote(stats);
            var testCase = $"{row.Provider} * {row.TestCase}";
            sb.Append($"| {Escape(testCase)} ");
            sb.Append($"| {PerOpCell(stats, stats.Avg, unit)}{below} ");
            sb.Append($"| {row.Speedup} |");

            if (Options.IncludePercentiles)
            {
                sb.Append($" {PerOpCell(stats, stats.P50, unit)}{below} |");
                sb.Append($" {PerOpCell(stats, stats.P90, unit)}{below} |");
                sb.Append($" {PerOpCell(stats, stats.P99, unit)}{below} |");
                sb.Append($" {PerOpCell(stats, stats.StandardError, unit)}{below} |");
                sb.Append($" {FormatMetric(stats.RelativeStdDevPercent, 1)} |");
            }

            if (Options.IncludeCpuCycles)
            {
                // The same cell as the results row: a proxy counter keeps its `~`.
                sb.Append($" {FormatCycles(stats)} |");
            }

            if (Options.IncludeGcInfo)
            {
                // Two columns, one alias: T3 added `GC B` to this table and T4
                // reads the row through `stats`. Both cells name the same
                // `Statistics`, and each keeps its own tri-state (IFormatter):
                // `N/A` when the counter was not collected, `~` when the
                // reading is approximate.
                sb.Append($" {FormatGcInfo(stats.GcInfo)} ");
                sb.Append($"| {FormatGcBytes(stats.GcInfo)} |");
            }

            sb.AppendLine();
        }

        AppendMeasurementLimitNote(sb, rows.Select(static r => r.Stats));
    }

    /// <summary>
    /// The marker a per-operation cell carries next to its value:
    /// <c>(unmeasured)</c> when the clock never advanced, <c>(below resolution)</c>
    /// when the value sits at the harness floor. One helper for both tables, so the
    /// results table and the comparison table cannot mark the same run differently.
    /// </summary>
    private static string MeasurementLimitNote(Statistics statistics) =>
        statistics.Unmeasured
            ? " (unmeasured)"
            : statistics.ResolutionLimited
                ? " (below resolution)"
                : "";

    /// <summary>
    /// The note that explains those markers, printed under a table whose cells use
    /// them. Both tables call it, and a table whose cells are all plain numbers
    /// prints none.
    /// </summary>
    private void AppendMeasurementLimitNote(StringBuilder sb, IEnumerable<Statistics> statistics)
    {
        var list = statistics.ToList();
        if (list.Exists(static s => s.Unmeasured))
        {
            sb.AppendLine();
            sb.AppendLine(
                "> **unmeasured** — the clock did not advance for the whole sample, so no per-operation value is shown (the timing source's granularity is coarser than the work)."
            );
        }
        else if (list.Exists(static s => s.ResolutionLimited))
        {
            sb.AppendLine();
            sb.AppendLine(
                "> **below resolution** — the value is at the measurement harness' own floor, so only an upper bound is shown."
            );
        }

        // R-T4 · B7: a table with no cycle column prints no `~`, so it explains none.
        if (Options.IncludeCpuCycles && list.Exists(static s => s.CpuCyclesAreApproximate))
        {
            sb.AppendLine();
            sb.AppendLine(ApproximateCycles.MarkdownPrefix + ApproximateCycles.Sentence);
        }
    }

    // The `~` sentence is shared with the other two display formats
    // (<see cref="ApproximateCycles"/>); this format adds only its own bold lead-in (R-T4 · B5).

    /// <summary>Every per-operation figure a statistics object contributes.</summary>
    private static IEnumerable<double> PerOpValues(Statistics statistics)
    {
        yield return statistics.Avg;
        yield return statistics.P50;
        yield return statistics.P90;
        yield return statistics.P99;
        yield return statistics.StandardError;
    }

    /// <summary>
    /// A comparison cell: per-operation value in the table's unit, with the same
    /// <c>N/A</c> / <c>&lt;limit</c> states as the results table.
    /// </summary>
    private string PerOpCell(Statistics statistics, double nanoseconds, TimeUnit unit) =>
        FormatPerOp(statistics, nanoseconds, unit);

    #endregion

    #region Helpers

    /// <summary>
    /// User text inside one table cell (Batch 2 · 2.5, B2-H4): structure characters
    /// escaped, control characters replaced, a line break as <c>&lt;br&gt;</c> so the
    /// row cannot be split. See <see cref="TextSanitizer"/> for the rules and for why
    /// each character is on the list.
    /// </summary>
    private static string Escape(string value) => TextSanitizer.ForMarkdownCell(value);

    #endregion

    #region Static Helpers

    /// <summary>
    /// Write Markdown to a file, creating directory if needed.
    /// </summary>
    public static void WriteToFile(
        string filePath,
        BenchmarkResult result,
        FormatterOptions? options = null
    )
    {
        var formatter = new MarkdownFormatter(options);
        WriteToFileInternal(formatter.Options.ResolvePath(filePath), formatter.Format(result));
    }

    /// <summary>
    /// Write Markdown to a file, creating directory if needed.
    /// </summary>
    public static void WriteToFile(
        string filePath,
        IEnumerable<BenchmarkResult> results,
        FormatterOptions? options = null
    )
    {
        var formatter = new MarkdownFormatter(options);
        WriteToFileInternal(formatter.Options.ResolvePath(filePath), formatter.Format(results));
    }

    /// <summary>
    /// Write Markdown to a file, creating directory if needed.
    /// </summary>
    public static void WriteToFile(
        string filePath,
        IEnumerable<ComparisonResult> comparisons,
        FormatterOptions? options = null
    )
    {
        var formatter = new MarkdownFormatter(options);
        WriteToFileInternal(formatter.Options.ResolvePath(filePath), formatter.Format(comparisons));
    }

    /// <summary>
    /// Write Markdown to a file, creating directory if needed.
    /// </summary>
    public static void WriteToFile(
        string filePath,
        BenchmarkSuite suite,
        FormatterOptions? options = null
    )
    {
        var formatter = new MarkdownFormatter(options);
        WriteToFileInternal(formatter.Options.ResolvePath(filePath), formatter.Format(suite));
    }

    /// <summary>
    /// Generate Markdown string for comparisons grouped by category.
    /// </summary>
    public static string FormatGroupedComparisons(
        IEnumerable<ComparisonResult> comparisons,
        Func<ComparisonResult, string> groupBy,
        FormatterOptions? options = null
    )
    {
        var formatter = new MarkdownFormatter(options);
        var sb = new StringBuilder();

        var groups = comparisons.GroupBy(groupBy).OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            sb.AppendLine($"### {TextSanitizer.ForMarkdownLine(group.Key)}");
            sb.AppendLine();
            sb.AppendLine(formatter.Format(group.ToList()));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    #endregion
}
