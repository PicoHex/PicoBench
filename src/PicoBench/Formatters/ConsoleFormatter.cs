namespace PicoBench.Formatters;

/// <summary>
/// Formats benchmark results for console output with ASCII tables.
/// </summary>
public sealed class ConsoleFormatter(FormatterOptions? options = null) : FormatterBase(options)
{
    // Thread-local StringBuilder pool to reduce allocations
    [ThreadStatic]
    private static StringBuilder? _cachedStringBuilder;

    private static StringBuilder GetStringBuilder()
    {
        var sb = _cachedStringBuilder;
        if (sb is null)
            return new StringBuilder(1024); // Default capacity for typical output
        _cachedStringBuilder = null;
        sb.Clear();
        return sb;
    }

    private static void ReturnStringBuilder(StringBuilder sb)
    {
        if (sb.Capacity <= 4096) // Only cache reasonable sized builders
        {
            _cachedStringBuilder = sb;
        }
    }

    /// <inheritdoc />
    protected override string FormatCore(BenchmarkResult result)
    {
        var sb = GetStringBuilder();
        try
        {
            AppendSingleResult(sb, result);
            return sb.ToString();
        }
        finally
        {
            ReturnStringBuilder(sb);
        }
    }

    /// <inheritdoc />
    protected override string FormatCore(IEnumerable<BenchmarkResult> results)
    {
        var sb = GetStringBuilder();
        try
        {
            var list = results.ToList();

            if (list.Count == 0)
                return "No results.";

            AppendResultsTable(sb, list);
            return sb.ToString();
        }
        finally
        {
            ReturnStringBuilder(sb);
        }
    }

    /// <inheritdoc />
    protected override string FormatCore(ComparisonResult comparison)
    {
        var sb = GetStringBuilder();
        try
        {
            AppendComparisonResult(sb, comparison);
            return sb.ToString();
        }
        finally
        {
            ReturnStringBuilder(sb);
        }
    }

    /// <inheritdoc />
    protected override string FormatCore(IEnumerable<ComparisonResult> comparisons)
    {
        var sb = GetStringBuilder();
        try
        {
            var list = comparisons.ToList();

            if (list.Count == 0)
                return "No comparisons.";

            AppendComparisonsTable(sb, list);
            return sb.ToString();
        }
        finally
        {
            ReturnStringBuilder(sb);
        }
    }

    /// <inheritdoc />
    protected override string FormatCore(BenchmarkSuite suite)
    {
        var sb = GetStringBuilder();
        try
        {
            // Header
            AppendBoxHeader(sb, suite.Name, suite.Description);

            // Environment info
            if (Options.IncludeEnvironment)
            {
                sb.AppendLine();
                sb.AppendLine($"Environment: {suite.Environment}");
                sb.AppendLine(
                    $"CPU Counter: {EnvironmentInfo.DescribeCpuCycleMeasurement(suite.Environment.CpuCycleMeasurement)}"
                );

                if (!suite.Environment.CpuCyclesAvailable)
                {
                    sb.AppendLine("CPU Counter Note: unavailable on this platform/runtime");
                }
                else if (!suite.Environment.CpuCyclesAreMeaningful)
                {
                    sb.AppendLine("CPU Counter Note: proxy timing source, not true CPU cycles");
                }
            }

            if (Options.IncludeTimestamp)
            {
                sb.AppendLine($"Timestamp: {suite.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
                sb.AppendLine($"Duration: {suite.Duration.TotalSeconds:F2}s");
            }

            // Results
            if (suite.Results.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("═══ Results ═══");
                AppendResultsTable(sb, [.. suite.Results]);
            }

            // Comparisons
            if (!(suite.Comparisons?.Count > 0))
                return sb.ToString();
            sb.AppendLine();
            sb.AppendLine("═══ Comparisons ═══");
            AppendComparisonsTable(sb, [.. suite.Comparisons]);

            return sb.ToString();
        }
        finally
        {
            ReturnStringBuilder(sb);
        }
    }

    #region Single Result

    private void AppendSingleResult(StringBuilder sb, BenchmarkResult result)
    {
        sb.AppendLine($"Benchmark: {result.Name}{DescribeMeasurementLimit(result.Statistics)}");
        sb.AppendLine($"  Avg: {FormatPerOpLine(result.Statistics, result.Statistics.Avg)}");
        sb.AppendLine($"  P50: {FormatPerOpLine(result.Statistics, result.Statistics.P50)}");

        if (result.Statistics.Unmeasured)
        {
            sb.AppendLine(
                $"  Unmeasured: the clock did not advance for {result.Statistics.UnmeasuredSampleCount} "
                    + $"sample(s); no per-operation value is reported (clock granularity "
                    + $"~{result.Statistics.ResolutionLimitNanoseconds:F0} ns/op)"
            );
        }
        else if (result.Statistics.ResolutionLimited)
        {
            sb.AppendLine(
                $"  Resolution: below the measurement floor (resolution limit "
                    + $"~{result.Statistics.ResolutionLimitNanoseconds:F3} ns/op, "
                    + $"harness overhead {result.Statistics.OverheadNs:F3} ns/op deducted)"
            );
        }
        else if (result.Statistics.OverheadNs > 0)
        {
            sb.AppendLine(
                $"  Overhead: {result.Statistics.OverheadNs:F3} ns/op deducted (delegate/loop floor)"
            );
        }

        if (Options.IncludePercentiles)
        {
            sb.AppendLine($"  P90: {FormatPerOpLine(result.Statistics, result.Statistics.P90)}");
            sb.AppendLine($"  P95: {FormatPerOpLine(result.Statistics, result.Statistics.P95)}");
            sb.AppendLine($"  P99: {FormatPerOpLine(result.Statistics, result.Statistics.P99)}");
        }

        if (Options.IncludeCpuCycles)
        {
            sb.AppendLine($"  CPU Cycles: {FormatCycles(result.Statistics)}");
        }

        if (Options.IncludeGcInfo)
        {
            sb.AppendLine($"  GC (0/1/2): {FormatGcInfo(result.Statistics.GcInfo)}");
        }

        // Per-operation figures like the ones above: the CSV/Markdown/HTML tables
        // print the same cells through the same rule, so an unmeasured run is "N/A"
        // in all four formats instead of a number in one of them.
        sb.AppendLine($"  StdDev: {FormatPerOpLine(result.Statistics, result.Statistics.StdDev)}");
        sb.AppendLine($"  StdErr: {FormatPerOpLine(result.Statistics, result.Statistics.StandardError)}");

        if (result.Statistics.HasHighVariance)
        {
            sb.AppendLine(
                $"  Noise: High variance ({result.Statistics.RelativeStdDevPercent:F1}% RSD)"
            );
        }
    }

    #endregion

    #region Results Table

    private void AppendResultsTable(StringBuilder sb, List<BenchmarkResult> results)
    {
        // Calculate column widths based on content
        var nameWidth = Math.Max("Name".Length, results.Max(r => r.Name.Length));
        var avgWidth = Math.Max(
            "Avg (ns)".Length,
            results.Max(r => FormatPerOp(r.Statistics, r.Statistics.Avg).Length)
        );
        var p50Width = Math.Max(
            "P50 (ns)".Length,
            results.Max(r => FormatPerOp(r.Statistics, r.Statistics.P50).Length)
        );
        var p90Width = Options.IncludePercentiles
            ? Math.Max(
                "P90 (ns)".Length,
                results.Max(r => FormatPerOp(r.Statistics, r.Statistics.P90).Length)
            )
            : 0;
        var p95Width = Options.IncludePercentiles
            ? Math.Max(
                "P95 (ns)".Length,
                results.Max(r => FormatPerOp(r.Statistics, r.Statistics.P95).Length)
            )
            : 0;
        var p99Width = Options.IncludePercentiles
            ? Math.Max(
                "P99 (ns)".Length,
                results.Max(r => FormatPerOp(r.Statistics, r.Statistics.P99).Length)
            )
            : 0;
        var cpuWidth = Options.IncludeCpuCycles
            ? Math.Max("CPU Cycles".Length, results.Max(r => FormatCycles(r.Statistics).Length))
            : 0;
        var gcWidth = Options.IncludeGcInfo
            ? Math.Max(
                "GC (0/1/2)".Length,
                results.Max(r => FormatGcInfo(r.Statistics.GcInfo).Length)
            )
            : 0;

        // Build column definitions
        var columns = new List<TableColumn<BenchmarkResult>>
        {
            new("Name", nameWidth, r => r.Name, leftAlign: true),
            new("Avg (ns)", avgWidth, r => FormatPerOp(r.Statistics, r.Statistics.Avg)),
            new("P50 (ns)", p50Width, r => FormatPerOp(r.Statistics, r.Statistics.P50))
        };

        if (Options.IncludePercentiles)
        {
            columns.Add(
                new("P90 (ns)", p90Width, r => FormatPerOp(r.Statistics, r.Statistics.P90))
            );
            columns.Add(
                new("P95 (ns)", p95Width, r => FormatPerOp(r.Statistics, r.Statistics.P95))
            );
            columns.Add(
                new("P99 (ns)", p99Width, r => FormatPerOp(r.Statistics, r.Statistics.P99))
            );
        }

        if (Options.IncludeCpuCycles)
        {
            columns.Add(new("CPU Cycles", cpuWidth, r => FormatCycles(r.Statistics)));
        }

        if (Options.IncludeGcInfo)
        {
            columns.Add(new("GC (0/1/2)", gcWidth, r => FormatGcInfo(r.Statistics.GcInfo)));
        }

        AppendTable(sb, columns, results);

        if (results.Exists(static r => r.Statistics.Unmeasured))
        {
            sb.AppendLine();
            sb.AppendLine(
                "unmeasured: the clock did not advance for the whole sample — no per-operation value is shown"
            );
        }
        else if (results.Exists(static r => r.Statistics.ResolutionLimited))
        {
            sb.AppendLine();
            sb.AppendLine(
                "below resolution: the value is at the measurement harness' own floor, only an upper bound is shown"
            );
        }

        // Batch 0 - 0.5, third clause: a run whose spread is wide enough to question
        // the number itself has to say so in the main table, not only in the
        // single-result view. High variance is orthogonal to the two notes above:
        // every cell can hold a plain number and still be noise.
        var noisy = results.Where(static r => r.Statistics.HasHighVariance).ToList();
        if (noisy.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(
                "noise: high variance (RSD >= 10%) - "
                    + string.Join(
                        ", ",
                        noisy.Select(static r =>
                            $"{r.Name} {r.Statistics.RelativeStdDevPercent:F1}% RSD"
                        )
                    )
                    + "; treat the difference as unresolved"
            );
        }
    }

    /// <summary>
    /// Renders a per-operation cell. A value the run could not resolve above
    /// the harness floor is printed as an upper bound (<c>&lt;limit</c>) rather
    /// than as a number that would look like a real measurement, and a run whose
    /// clock never advanced is printed as "not measured".
    /// </summary>
    private string FormatPerOp(Statistics statistics, double nanoseconds) =>
        statistics.Unmeasured
            ? "N/A"
            : statistics.ResolutionLimited
                ? $"<{FormatTime(statistics.ResolutionLimitNanoseconds)}"
                : FormatTime(nanoseconds);

    /// <summary>
    /// A per-operation value with its unit, or the "not measured" note when the
    /// timing source never advanced during the run.
    /// </summary>
    private string FormatPerOpLine(Statistics statistics, double nanoseconds) =>
        statistics.Unmeasured ? "N/A (unmeasured)" : $"{FormatPerOp(statistics, nanoseconds)} ns";

    /// <summary>
    /// The measurement-quality marker printed next to a benchmark name:
    /// <c>[unmeasured]</c> when the clock never advanced, <c>[below resolution]</c>
    /// when the value sits at the harness floor, nothing otherwise.
    /// </summary>
    private static string DescribeMeasurementLimit(Statistics statistics) =>
        statistics.Unmeasured
            ? " [unmeasured]"
            : statistics.ResolutionLimited
                ? " [below resolution]"
                : "";

    /// <summary>
    /// Renders the cycle column. A process-wide counter or a monotonic-clock
    /// proxy is marked with <c>~</c>: it is not a thread-scoped cycle count.
    /// </summary>
    private static string FormatCycles(Statistics statistics) =>
        statistics.CpuCyclesAreApproximate
            ? $"~{statistics.CpuCyclesPerOp:F0}"
            : $"{statistics.CpuCyclesPerOp:F0}";

    #endregion

    #region Comparison Result

    private void AppendComparisonResult(StringBuilder sb, ComparisonResult comparison)
    {
        var indicator = GetSpeedupIndicator(comparison.Speedup);

        sb.AppendLine($"Comparison: {comparison.Name}");
        sb.AppendLine(
            $"  Baseline ({comparison.Baseline.Name}): {FormatTime(comparison.Baseline.Statistics.Avg)} ns"
        );
        sb.AppendLine(
            $"  Candidate ({comparison.Candidate.Name}): {FormatTime(comparison.Candidate.Statistics.Avg)} ns"
        );
        sb.AppendLine($"  Speedup: {FormatSpeedup(comparison.Speedup)} {indicator}");
        if (comparison.Interval is { } interval)
        {
            sb.AppendLine($"  Interval: {interval} (95% CI of the paired-round median)");
        }

        sb.AppendLine($"  Winner: {DescribeWinner(comparison)}");
    }

    /// <summary>
    /// Names the winner of a comparison, or says why there is none: a difference
    /// inside the dead band (or whose interval still contains parity) is not a win.
    /// </summary>
    private static string DescribeWinner(ComparisonResult comparison) =>
        comparison.Outcome switch
        {
            ComparisonOutcome.Faster => comparison.Candidate.Name,
            ComparisonOutcome.Slower => comparison.Baseline.Name,
            ComparisonOutcome.Inconclusive =>
                $"none (within the {ComparisonResult.DeadBand * 100:F0}% dead band — indistinguishable)",
            _ => "unknown (not measurable)",
        };

    #endregion

    #region Comparisons Table

    private sealed class ComparisonRow(
        string name,
        string provider,
        BenchmarkResult result,
        string speedup
    )
    {
        public string Name { get; } = name;
        public string Provider { get; } = provider;
        public BenchmarkResult Result { get; } = result;
        public string Speedup { get; } = speedup;
    }

    private void AppendComparisonsTable(StringBuilder sb, List<ComparisonResult> comparisons)
    {
        var rows = BuildComparisonRows(comparisons);
        var columns = BuildComparisonColumns(rows);

        AppendTable(sb, columns, rows);

        // Summary
        var wins = comparisons.Count(c => c.IsFaster);
        var avgSpeedup = comparisons.Average(c => c.Speedup);
        var maxSpeedup = comparisons.Max(c => c.Speedup);

        sb.AppendLine();
        sb.AppendLine(
            $"Summary: Candidate wins {wins}/{comparisons.Count} | Avg: {FormatSpeedup(avgSpeedup)} | Max: {FormatSpeedup(maxSpeedup)}"
        );
    }

    /// <summary>
    /// Flatten comparisons into individual rows for detailed view.
    /// </summary>
    private List<ComparisonRow> BuildComparisonRows(List<ComparisonResult> comparisons)
    {
        var rows = new List<ComparisonRow>();
        foreach (var c in comparisons)
        {
            var indicator = GetSpeedupIndicator(c.Speedup);
            rows.Add(
                new ComparisonRow(
                    c.Name,
                    Options.CandidateLabel,
                    c.Candidate,
                    $"{FormatSpeedup(c.Speedup)} {indicator}"
                )
            );
            rows.Add(new ComparisonRow(c.Name, Options.BaselineLabel, c.Baseline, ""));
        }
        return rows;
    }

    /// <summary>
    /// Build column definitions for comparison rows.
    /// </summary>
    private List<TableColumn<ComparisonRow>> BuildComparisonColumns(List<ComparisonRow> rows)
    {
        var nameWidth = Math.Max(
            "Test Case".Length,
            rows.Max(r => $"{r.Provider} * {r.Name}".Length)
        );
        var avgWidth = Math.Max(
            "Avg (ns)".Length,
            rows.Max(r => FormatTime(r.Result.Statistics.Avg).Length)
        );
        var speedupWidth = Math.Max("Speedup".Length, rows.Max(r => r.Speedup.Length));

        var columns = new List<TableColumn<ComparisonRow>>
        {
            new("Test Case", nameWidth, r => $"{r.Provider} * {r.Name}", leftAlign: true),
            new("Avg (ns)", avgWidth, r => FormatTime(r.Result.Statistics.Avg)),
            new("Speedup", speedupWidth, r => r.Speedup)
        };

        if (Options.IncludePercentiles)
        {
            var p50Width = Math.Max(
                "P50".Length,
                rows.Max(r => FormatTime(r.Result.Statistics.P50).Length)
            );
            var p90Width = Math.Max(
                "P90".Length,
                rows.Max(r => FormatTime(r.Result.Statistics.P90).Length)
            );
            var p99Width = Math.Max(
                "P99".Length,
                rows.Max(r => FormatTime(r.Result.Statistics.P99).Length)
            );
            columns.Add(new("P50", p50Width, r => FormatTime(r.Result.Statistics.P50)));
            columns.Add(new("P90", p90Width, r => FormatTime(r.Result.Statistics.P90)));
            columns.Add(new("P99", p99Width, r => FormatTime(r.Result.Statistics.P99)));
        }

        if (Options.IncludeCpuCycles)
        {
            var cpuWidth = Math.Max(
                "CPU".Length,
                rows.Max(r => $"{r.Result.Statistics.CpuCyclesPerOp:F0}".Length)
            );
            columns.Add(
                new TableColumn<ComparisonRow>(
                    "CPU",
                    cpuWidth,
                    r => $"{r.Result.Statistics.CpuCyclesPerOp:F0}"
                )
            );
        }

        if (Options.IncludeGcInfo)
        {
            var gcWidth = Math.Max(
                "GC".Length,
                rows.Max(r => FormatGcInfo(r.Result.Statistics.GcInfo).Length)
            );
            columns.Add(
                new TableColumn<ComparisonRow>(
                    "GC",
                    gcWidth,
                    r => FormatGcInfo(r.Result.Statistics.GcInfo)
                )
            );
        }

        return columns;
    }

    #endregion

    #region Box Header

    private static void AppendBoxHeader(StringBuilder sb, string title, string? description)
    {
        const int width = 80;
        var border = new string('═', width - 2);

        sb.AppendLine($"╔{border}╗");
        sb.AppendLine($"║{CenterText(title, width - 2)}║");

        if (!string.IsNullOrEmpty(description))
        {
            sb.AppendLine($"║{CenterText(description ?? string.Empty, width - 2)}║");
        }

        sb.AppendLine($"╚{border}╝");
    }

    private static string CenterText(string text, int width)
    {
        if (text.Length >= width)
            return text.Substring(0, width);

        var padding = (width - text.Length) / 2;
        return text.PadLeft(padding + text.Length).PadRight(width);
    }

    #endregion

    #region Table Builder Utilities

    private sealed class TableColumn<T>(
        string header,
        int width,
        Func<T, string> getValue,
        bool leftAlign = false
    )
    {
        public string Header { get; } = header;
        public int Width { get; set; } = width;
        public Func<T, string> GetValue { get; } = getValue;
        public bool LeftAlign { get; } = leftAlign;
    }

    private static void AppendTable<T>(
        StringBuilder sb,
        List<TableColumn<T>> columns,
        List<T> rows,
        string? title = null
    )
    {
        const int padding = 2;

        // Add padding to widths
        foreach (var column in columns)
        {
            column.Width += padding;
        }

        if (title != null)
        {
            var totalWidth = columns.Sum(c => c.Width) + columns.Count + 1;
            // Title top border
            sb.Append('┌');
            sb.Append(new string('─', totalWidth - 2));
            sb.AppendLine("┐");
            // Title text
            sb.Append("│ ");
            sb.Append(title.PadRight(totalWidth - 4));
            sb.AppendLine(" │");
            // Title-header separator with column dividers
            AppendTableBorder(sb, columns, '├', '┬', '┤');
        }
        else
        {
            AppendTableBorder(sb, columns, '┌', '┬', '┐');
        }

        AppendTableRow(sb, columns, columns.Select(c => c.Header).ToList());
        AppendTableBorder(sb, columns, '├', '┼', '┤');

        foreach (var row in rows)
        {
            var values = columns.Select(c => c.GetValue(row)).ToList();
            AppendTableRow(sb, columns, values);
        }

        AppendTableBorder(sb, columns, '└', '┴', '┘');
    }

    private static void AppendTableBorder<T>(
        StringBuilder sb,
        List<TableColumn<T>> columns,
        char left,
        char middle,
        char right
    )
    {
        sb.Append(left);
        for (var i = 0; i < columns.Count; i++)
        {
            sb.Append(new string('─', columns[i].Width));
            sb.Append(i < columns.Count - 1 ? middle : right);
        }
        sb.AppendLine();
    }

    private static void AppendTableRow<T>(
        StringBuilder sb,
        List<TableColumn<T>> columns,
        List<string> values
    )
    {
        sb.Append('│');
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var value = values[i];
            var text = column.LeftAlign
                ? $" {value}".PadRight(column.Width)
                : value.PadLeft(column.Width - 1) + " ";
            sb.Append(text);
            sb.Append('│');
        }
        sb.AppendLine();
    }

    #endregion

    #region Table With Title

    /// <summary>
    /// Formats comparisons as a table with a title header.
    /// </summary>
    public string FormatTableWithTitle(string title, IEnumerable<ComparisonResult> comparisons)
    {
        var sb = new StringBuilder();
        var list = comparisons.ToList();

        if (list.Count == 0)
            return "No comparisons.";

        AppendComparisonsTableWithTitle(sb, title, list);
        return sb.ToString();
    }

    private void AppendComparisonsTableWithTitle(
        StringBuilder sb,
        string title,
        List<ComparisonResult> comparisons
    )
    {
        var rows = BuildComparisonRows(comparisons);
        var columns = BuildComparisonColumns(rows);

        sb.AppendLine();
        AppendTable(sb, columns, rows, title);
    }

    #endregion

    #region Static Helpers

    /// <summary>
    /// Write formatted results directly to console.
    /// </summary>
    public static void Write(BenchmarkResult result, FormatterOptions? options = null)
    {
        var formatter = new ConsoleFormatter(options);
        Console.WriteLine(formatter.Format(result));
    }

    /// <summary>
    /// Write formatted results directly to console.
    /// </summary>
    public static void Write(IEnumerable<BenchmarkResult> results, FormatterOptions? options = null)
    {
        var formatter = new ConsoleFormatter(options);
        Console.WriteLine(formatter.Format(results));
    }

    /// <summary>
    /// Write formatted comparison directly to console.
    /// </summary>
    public static void Write(ComparisonResult comparison, FormatterOptions? options = null)
    {
        var formatter = new ConsoleFormatter(options);
        Console.WriteLine(formatter.Format(comparison));
    }

    /// <summary>
    /// Write formatted comparisons directly to console.
    /// </summary>
    public static void Write(
        IEnumerable<ComparisonResult> comparisons,
        FormatterOptions? options = null
    )
    {
        var formatter = new ConsoleFormatter(options);
        Console.WriteLine(formatter.Format(comparisons));
    }

    /// <summary>
    /// Write formatted suite directly to console.
    /// </summary>
    public static void Write(BenchmarkSuite suite, FormatterOptions? options = null)
    {
        var formatter = new ConsoleFormatter(options);
        Console.WriteLine(formatter.Format(suite));
    }

    /// <summary>
    /// Write comparisons table with title directly to console.
    /// </summary>
    public static void WriteTableWithTitle(
        string title,
        IEnumerable<ComparisonResult> comparisons,
        FormatterOptions? options = null
    )
    {
        var formatter = new ConsoleFormatter(options);
        Console.Write(formatter.FormatTableWithTitle(title, comparisons));
    }

    /// <summary>
    /// Write a header box to console.
    /// </summary>
    /// <param name="title">The main title.</param>
    /// <param name="width">The width of the box (default 111).</param>
    public static void WriteHeader(string title, int width = 111)
    {
        var border = new string('═', width - 2);
        var padding = (width - 2 - title.Length) / 2;
        var centeredTitle = title.PadLeft(padding + title.Length).PadRight(width - 2);

        Console.WriteLine();
        Console.WriteLine($"╔{border}╗");
        Console.WriteLine($"║{centeredTitle}║");
        Console.WriteLine($"╚{border}╝");
    }

    /// <summary>
    /// Write environment and config info to console.
    /// </summary>
    public static void WriteEnvironment(EnvironmentInfo env, BenchmarkConfig config)
    {
        Console.WriteLine();
        Console.WriteLine($"Environment: {env}");
        Console.WriteLine(
            $"CPU Counter: {EnvironmentInfo.DescribeCpuCycleMeasurement(env.CpuCycleMeasurement)}"
        );
        Console.WriteLine(
            $"Config: {config.SampleCount} samples × {config.IterationsPerSample} iterations"
        );
        Console.WriteLine();
    }

    /// <summary>
    /// Write file save message to console.
    /// </summary>
    public static void WriteFileSaved(string format, string path)
    {
        Console.WriteLine($"\n{format} results saved to: {path}");
    }

    #endregion
}
