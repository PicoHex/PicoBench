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
                sb.AppendLine($"Timestamp: {FormatTimestamp(suite.Timestamp)} UTC");
                sb.AppendLine($"Duration: {FormatDuration(suite.Duration)}");
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
        // One unit for the whole single-result view, chosen from the figures it
        // prints (Batch 2 · 2.4): a five-second benchmark reads "5.0 s" here as it
        // does in the table, not "5000000000.0 ns" in one view and "5.0" in the other.
        var unit = TableUnit(
            [
                result.Statistics.Avg,
                result.Statistics.P50,
                result.Statistics.P90,
                result.Statistics.P95,
                result.Statistics.P99,
                result.Statistics.StandardError,
            ]
        );

        sb.AppendLine(
            $"Benchmark: {TextSanitizer.ForTerminal(result.Name)}{DescribeMeasurementLimit(result.Statistics)}"
        );
        sb.AppendLine($"  Avg: {FormatPerOpLine(result.Statistics, result.Statistics.Avg, unit)}");
        sb.AppendLine($"  P50: {FormatPerOpLine(result.Statistics, result.Statistics.P50, unit)}");

        if (result.Statistics.Unmeasured)
        {
            sb.AppendLine(
                $"  Unmeasured: the clock did not advance for {result.Statistics.UnmeasuredSampleCount} "
                    + $"sample(s); no per-operation value is reported (clock granularity "
                    + $"~{FormatMetric(result.Statistics.ResolutionLimitNanoseconds, 0)} ns/op)"
            );
        }
        else if (result.Statistics.ResolutionLimited)
        {
            sb.AppendLine(
                $"  Resolution: below the measurement floor (resolution limit "
                    + $"~{FormatMetric(result.Statistics.ResolutionLimitNanoseconds, 3)} ns/op, "
                    + $"harness overhead {FormatMetric(result.Statistics.OverheadNs, 3)} ns/op deducted)"
            );
        }
        else if (result.Statistics.OverheadNs > 0)
        {
            sb.AppendLine(
                $"  Overhead: {FormatMetric(result.Statistics.OverheadNs, 3)} ns/op deducted (delegate/loop floor)"
            );
        }

        if (Options.IncludePercentiles)
        {
            sb.AppendLine($"  P90: {FormatPerOpLine(result.Statistics, result.Statistics.P90, unit)}");
            sb.AppendLine($"  P95: {FormatPerOpLine(result.Statistics, result.Statistics.P95, unit)}");
            sb.AppendLine($"  P99: {FormatPerOpLine(result.Statistics, result.Statistics.P99, unit)}");
        }

        if (Options.IncludeCpuCycles)
        {
            sb.AppendLine($"  CPU Cycles: {FormatCycles(result.Statistics)}");
        }

        if (Options.IncludeGcInfo)
        {
            sb.AppendLine($"  GC (0/1/2): {FormatGcInfo(result.Statistics.GcInfo)}");
            sb.AppendLine($"  GC Allocated: {FormatGcBytes(result.Statistics.GcInfo)}");
        }

        // Per-operation figures like the ones above: the CSV/Markdown/HTML tables
        // print the same cells through the same rule, so an unmeasured run is "N/A"
        // in all four formats instead of a number in one of them.
        sb.AppendLine($"  StdDev: {FormatPerOpLine(result.Statistics, result.Statistics.StdDev, unit)}");
        sb.AppendLine($"  StdErr: {FormatPerOpLine(result.Statistics, result.Statistics.StandardError, unit)}");

        if (result.Statistics.HasHighVariance)
        {
            sb.AppendLine(
                $"  Noise: High variance ({FormatMetric(result.Statistics.RelativeStdDevPercent, 1)}% RSD)"
            );
        }
    }

    #endregion

    #region Results Table

    private void AppendResultsTable(StringBuilder sb, List<BenchmarkResult> results)
    {
        // One unit for the whole table (Batch 2 · 2.4, B2-M6): the header says which
        // unit the column is in, so every cell of that column is a plain number in
        // that unit — a per-cell rescale would mix 2.5 and 0.0 down one column.
        var unit = TableUnit(PerOpValues(results));

        var avgHeader = $"Avg ({unit.Symbol})";
        var p50Header = $"P50 ({unit.Symbol})";
        var p90Header = $"P90 ({unit.Symbol})";
        var p95Header = $"P95 ({unit.Symbol})";
        var p99Header = $"P99 ({unit.Symbol})";
        var stdErrHeader = $"StdErr ({unit.Symbol})";

        // Name widths are counted in display cells, not chars: a CJK name is two
        // cells wide but one char, and a control character must not survive into the
        // cell at all (Batch 2 · 2.5, B2-L4).
        var nameWidth = Math.Max(
            "Name".Length,
            results.Max(r => DisplayWidth.Of(TextSanitizer.ForTerminal(r.Name)))
        );
        var avgWidth = Math.Max(
            avgHeader.Length,
            results.Max(r => FormatPerOp(r.Statistics, r.Statistics.Avg, unit).Length)
        );
        var p50Width = Math.Max(
            p50Header.Length,
            results.Max(r => FormatPerOp(r.Statistics, r.Statistics.P50, unit).Length)
        );
        var p90Width = Options.IncludePercentiles
            ? Math.Max(
                p90Header.Length,
                results.Max(r => FormatPerOp(r.Statistics, r.Statistics.P90, unit).Length)
            )
            : 0;
        var p95Width = Options.IncludePercentiles
            ? Math.Max(
                p95Header.Length,
                results.Max(r => FormatPerOp(r.Statistics, r.Statistics.P95, unit).Length)
            )
            : 0;
        var p99Width = Options.IncludePercentiles
            ? Math.Max(
                p99Header.Length,
                results.Max(r => FormatPerOp(r.Statistics, r.Statistics.P99, unit).Length)
            )
            : 0;
        var stdErrWidth = Options.IncludePercentiles
            ? Math.Max(
                stdErrHeader.Length,
                results.Max(r => FormatPerOp(r.Statistics, r.Statistics.StandardError, unit).Length)
            )
            : 0;
        var rsdWidth = Options.IncludePercentiles
            ? Math.Max(
                RsdHeader.Length,
                results.Max(r => FormatMetric(r.Statistics.RelativeStdDevPercent, 1).Length)
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
        var gcBytesWidth = Options.IncludeGcInfo
            ? Math.Max(
                FormatterBase.GcBytesHeader.Length,
                results.Max(r => FormatGcBytes(r.Statistics.GcInfo).Length)
            )
            : 0;

        // Build column definitions
        var columns = new List<TableColumn<BenchmarkResult>>
        {
            new("Name", nameWidth, r => TextSanitizer.ForTerminal(r.Name), leftAlign: true),
            new(avgHeader, avgWidth, r => FormatPerOp(r.Statistics, r.Statistics.Avg, unit)),
            new(p50Header, p50Width, r => FormatPerOp(r.Statistics, r.Statistics.P50, unit))
        };

        if (Options.IncludePercentiles)
        {
            columns.Add(
                new(p90Header, p90Width, r => FormatPerOp(r.Statistics, r.Statistics.P90, unit))
            );
            columns.Add(
                new(p95Header, p95Width, r => FormatPerOp(r.Statistics, r.Statistics.P95, unit))
            );
            columns.Add(
                new(p99Header, p99Width, r => FormatPerOp(r.Statistics, r.Statistics.P99, unit))
            );

            // Batch 2 · 2.4 (B2-M5): the Console suite table — the one every sample
            // and the README recommend — carried neither StdErr nor RSD, while
            // Markdown and HTML carried both, so the noise signal was invisible in
            // the default rendering. Both columns now sit where the other two
            // formats put them (after the percentiles).
            columns.Add(
                new(
                    stdErrHeader,
                    stdErrWidth,
                    r => FormatPerOp(r.Statistics, r.Statistics.StandardError, unit)
                )
            );
            columns.Add(
                new(RsdHeader, rsdWidth, r => FormatMetric(r.Statistics.RelativeStdDevPercent, 1))
            );
        }

        if (Options.IncludeCpuCycles)
        {
            columns.Add(new("CPU Cycles", cpuWidth, r => FormatCycles(r.Statistics)));
        }

        if (Options.IncludeGcInfo)
        {
            columns.Add(new("GC (0/1/2)", gcWidth, r => FormatGcInfo(r.Statistics.GcInfo)));
            columns.Add(
                new(FormatterBase.GcBytesHeader, gcBytesWidth, r => FormatGcBytes(r.Statistics.GcInfo))
            );
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
                            $"{TextSanitizer.ForTerminal(r.Name)} {FormatMetric(r.Statistics.RelativeStdDevPercent, 1)}% RSD"
                        )
                    )
                    + "; treat the difference as unresolved"
            );
        }
    }

    /// <summary>Header of the RSD column: a percentage, never scaled.</summary>
    private const string RsdHeader = "RSD (%)";

    /// <summary>
    /// Every per-operation figure the results table holds. The table's unit is
    /// chosen from the largest of them, so no column can be scaled differently
    /// from its neighbours.
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
    /// resolve above the harness floor is printed as an upper bound
    /// (<c>&lt;limit</c>) rather than as a number that would look like a real
    /// measurement, and a run whose clock never advanced is printed as "not
    /// measured".
    /// </summary>
    private string FormatPerOp(Statistics statistics, double nanoseconds, TimeUnit unit) =>
        statistics.Unmeasured
            ? NotCollected
            : statistics.ResolutionLimited
                ? BoundCell(unit, statistics.ResolutionLimitNanoseconds)
                : MetricCell(unit, nanoseconds);

    /// <summary>
    /// A per-operation value with its unit, or the "not measured" note when the
    /// timing source never advanced during the run.
    /// </summary>
    private string FormatPerOpLine(Statistics statistics, double nanoseconds, TimeUnit unit) =>
        statistics.Unmeasured
            ? NotCollected + " (unmeasured)"
            : $"{FormatPerOp(statistics, nanoseconds, unit)} {unit.Symbol}";

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
            ? "~" + FormatMetric(statistics.CpuCyclesPerOp, 0)
            : FormatMetric(statistics.CpuCyclesPerOp, 0);

    #endregion

    #region Comparison Result

    private void AppendComparisonResult(StringBuilder sb, ComparisonResult comparison)
    {
        var indicator = GetSpeedupIndicator(comparison.Speedup);

        // Same unit for both sides of the comparison and for every figure printed
        // here, chosen from the two averages (Batch 2 · 2.4): a ratio whose two
        // numbers are written in two different units is not a comparison a reader
        // can check by eye.
        var unit = TableUnit(
            [
                comparison.Baseline.Statistics.Avg,
                comparison.Baseline.Statistics.P50,
                comparison.Candidate.Statistics.Avg,
                comparison.Candidate.Statistics.P50,
            ]
        );

        sb.AppendLine($"Comparison: {TextSanitizer.ForTerminal(comparison.Name)}");
        sb.AppendLine(
            $"  Baseline ({TextSanitizer.ForTerminal(comparison.Baseline.Name)}): {ComparisonValue(comparison.Baseline.Statistics, unit)}"
        );
        sb.AppendLine(
            $"  Candidate ({TextSanitizer.ForTerminal(comparison.Candidate.Name)}): {ComparisonValue(comparison.Candidate.Statistics, unit)}"
        );
        sb.AppendLine($"  Speedup: {FormatSpeedup(comparison.Speedup)} {indicator}");
        if (comparison.Interval is { } interval)
        {
            sb.AppendLine($"  Interval: {interval} (95% CI of the paired-round median)");
        }

        sb.AppendLine($"  Winner: {TextSanitizer.ForTerminal(DescribeWinner(comparison))}");
    }

    /// <summary>One side of a comparison, per-operation value and unit.</summary>
    private string ComparisonValue(Statistics statistics, TimeUnit unit) =>
        statistics.Unmeasured
            ? NotCollected + " (unmeasured)"
            : $"{FormatPerOp(statistics, statistics.Avg, unit)} {unit.Symbol}";

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
                $"none (within the {FormatMetric(ComparisonResult.DeadBand * 100, 0)}% dead band — indistinguishable)",
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

        // Summary. Non-finite ratios are excluded from the aggregate and counted,
        // so one scenario with a zero (or NaN) candidate cannot swallow the rest
        // (Batch 2 · 2.2, B2-M2).
        var wins = comparisons.Count(c => c.IsFaster);
        var ratios = SpeedupAggregate.From(comparisons);
        var note = ratios.Excluded == 0 ? "" : " | " + ratios.DescribeExcluded();

        sb.AppendLine();
        sb.AppendLine(
            $"Summary: Candidate wins {wins}/{comparisons.Count} | Avg: {FormatSpeedupValue(ratios.Average, Options.SpeedupDecimalPlaces)} | Max: {FormatSpeedupValue(ratios.Maximum, Options.SpeedupDecimalPlaces)}{note}"
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
        // Batch 2 · 2.4 (B2-M5): every per-operation column of the comparison table
        // now names its unit — only `Avg` used to say `(ns)`, so `P50`, `P90` and
        // `P99` were bare numbers whose unit a reader had to guess. StdErr and RSD
        // joined the set as well, because Markdown and HTML already had them: the
        // four formats must show the same metrics for the same data.
        var unit = TableUnit(ComparisonPerOpValues(rows));

        var avgHeader = $"Avg ({unit.Symbol})";
        var p50Header = $"P50 ({unit.Symbol})";
        var p90Header = $"P90 ({unit.Symbol})";
        var p99Header = $"P99 ({unit.Symbol})";
        var stdErrHeader = $"StdErr ({unit.Symbol})";

        var nameWidth = Math.Max(
            "Test Case".Length,
            rows.Max(r => DisplayWidth.Of(TextSanitizer.ForTerminal($"{r.Provider} * {r.Name}")))
        );
        var avgWidth = Math.Max(
            avgHeader.Length,
            rows.Max(r => ComparisonPerOp(r.Result.Statistics, r.Result.Statistics.Avg, unit).Length)
        );
        var speedupWidth = Math.Max("Speedup".Length, rows.Max(r => r.Speedup.Length));

        var columns = new List<TableColumn<ComparisonRow>>
        {
            new(
                "Test Case",
                nameWidth,
                r => TextSanitizer.ForTerminal($"{r.Provider} * {r.Name}"),
                leftAlign: true
            ),
            new(
                avgHeader,
                avgWidth,
                r => ComparisonPerOp(r.Result.Statistics, r.Result.Statistics.Avg, unit)
            ),
            new("Speedup", speedupWidth, r => r.Speedup)
        };

        if (Options.IncludePercentiles)
        {
            var p50Width = Math.Max(
                p50Header.Length,
                rows.Max(r => ComparisonPerOp(r.Result.Statistics, r.Result.Statistics.P50, unit).Length)
            );
            var p90Width = Math.Max(
                p90Header.Length,
                rows.Max(r => ComparisonPerOp(r.Result.Statistics, r.Result.Statistics.P90, unit).Length)
            );
            var p99Width = Math.Max(
                p99Header.Length,
                rows.Max(r => ComparisonPerOp(r.Result.Statistics, r.Result.Statistics.P99, unit).Length)
            );
            var stdErrWidth = Math.Max(
                stdErrHeader.Length,
                rows.Max(r =>
                    ComparisonPerOp(r.Result.Statistics, r.Result.Statistics.StandardError, unit).Length
                )
            );
            var rsdWidth = Math.Max(
                RsdHeader.Length,
                rows.Max(r => FormatMetric(r.Result.Statistics.RelativeStdDevPercent, 1).Length)
            );

            columns.Add(
                new(
                    p50Header,
                    p50Width,
                    r => ComparisonPerOp(r.Result.Statistics, r.Result.Statistics.P50, unit)
                )
            );
            columns.Add(
                new(
                    p90Header,
                    p90Width,
                    r => ComparisonPerOp(r.Result.Statistics, r.Result.Statistics.P90, unit)
                )
            );
            columns.Add(
                new(
                    p99Header,
                    p99Width,
                    r => ComparisonPerOp(r.Result.Statistics, r.Result.Statistics.P99, unit)
                )
            );
            columns.Add(
                new(
                    stdErrHeader,
                    stdErrWidth,
                    r =>
                        ComparisonPerOp(
                            r.Result.Statistics,
                            r.Result.Statistics.StandardError,
                            unit
                        )
                )
            );
            columns.Add(
                new(
                    RsdHeader,
                    rsdWidth,
                    r => FormatMetric(r.Result.Statistics.RelativeStdDevPercent, 1)
                )
            );
        }

        if (Options.IncludeCpuCycles)
        {
            var cpuWidth = Math.Max(
                "CPU".Length,
                rows.Max(r => FormatMetric(r.Result.Statistics.CpuCyclesPerOp, 0).Length)
            );
            columns.Add(
                new TableColumn<ComparisonRow>(
                    "CPU",
                    cpuWidth,
                    r => FormatMetric(r.Result.Statistics.CpuCyclesPerOp, 0)
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

            var gcBytesWidth = Math.Max(
                FormatterBase.GcBytesHeader.Length,
                rows.Max(r => FormatGcBytes(r.Result.Statistics.GcInfo).Length)
            );
            columns.Add(
                new TableColumn<ComparisonRow>(
                    FormatterBase.GcBytesHeader,
                    gcBytesWidth,
                    r => FormatGcBytes(r.Result.Statistics.GcInfo)
                )
            );
        }

        return columns;
    }

    /// <summary>
    /// A per-operation cell of the comparison table: the same three states as the
    /// results table (<c>N/A</c>, <c>&lt;limit</c>, number) in the table's unit.
    /// </summary>
    private string ComparisonPerOp(Statistics statistics, double nanoseconds, TimeUnit unit) =>
        FormatPerOp(statistics, nanoseconds, unit);

    /// <summary>Every per-operation figure a comparison table row holds.</summary>
    private static IEnumerable<double> ComparisonPerOpValues(List<ComparisonRow> rows)
    {
        foreach (var row in rows)
        {
            var s = row.Result.Statistics;
            yield return s.Avg;
            yield return s.P50;
            yield return s.P90;
            yield return s.P99;
            yield return s.StandardError;
        }
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
            // A description can be several lines. Each one is its own framed row, so
            // a line break inside it cannot split the box (Batch 2 · 2.5).
            foreach (var line in SplitLines(description!))
                sb.AppendLine($"║{CenterText(line, width - 2)}║");
        }

        sb.AppendLine($"╚{border}╝");
    }

    /// <summary>
    /// Splits user text into the lines it will occupy, with control characters
    /// already replaced (so a stray carriage return cannot rewind the terminal) and
    /// empty lines preserved.
    /// </summary>
    private static IEnumerable<string> SplitLines(string text)
    {
        var current = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (TextSanitizer.IsLineBreak(c))
            {
                // CR LF is one line break, not two: without this skip the box grows an
                // empty row for every Windows line ending.
                if (c == (char)13 && i + 1 < text.Length && text[i + 1] == (char)10)
                    i++;

                yield return current.ToString();
                current.Clear();
                continue;
            }

            current.Append(TextSanitizer.ForTerminal(c.ToString()));
        }

        yield return current.ToString();
    }

    /// <summary>
    /// Centres text in a fixed-width frame, measured in display cells: a CJK title
    /// is two cells wide per char, and truncating a 20-char CJK title at 78
    /// <em>chars</em> would overflow the box by 20 cells (Batch 2 · 2.5, B2-L4).
    /// </summary>
    private static string CenterText(string text, int width)
    {
        var sanitized = TextSanitizer.ForTerminal(text);
        if (DisplayWidth.Of(sanitized) >= width)
            return DisplayWidth.Truncate(sanitized, width);

        var padding = (width - DisplayWidth.Of(sanitized)) / 2;
        return DisplayWidth.PadRight(
            DisplayWidth.PadLeft(sanitized, padding + DisplayWidth.Of(sanitized)),
            width
        );
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
            sb.Append(DisplayWidth.PadRight(TextSanitizer.ForTerminal(title), totalWidth - 4));
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
            // Padding is applied in display cells, so the frame closes whatever the
            // script of the name (Batch 2 · 2.5, B2-L4).
            var text = column.LeftAlign
                ? DisplayWidth.PadRight($" {value}", column.Width)
                : DisplayWidth.PadLeft(value, column.Width - 1) + " ";
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
        var text = TextSanitizer.ForTerminal(title);
        var textWidth = DisplayWidth.Of(text);
        var centeredTitle = DisplayWidth.PadRight(
            DisplayWidth.PadLeft(text, (width - 2 - textWidth) / 2 + textWidth),
            width - 2
        );

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
