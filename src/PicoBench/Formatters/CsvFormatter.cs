namespace PicoBench.Formatters;

/// <summary>
/// Formats benchmark results as CSV for data analysis.
/// </summary>
public sealed class CsvFormatter(FormatterOptions? options = null) : FormatterBase(options)
{
    private const string Separator = ",";

    /// <inheritdoc />
    protected override string FormatCore(IEnumerable<BenchmarkResult> results)
    {
        var sb = new StringBuilder();
        var list = results.ToList();

        if (list.Count == 0)
            return string.Empty;

        // Contract first, then the header row and the data rows.
        AppendSchema(sb, "results", ResultsHeader());

        // Header row
        AppendResultsHeader(sb);

        // Data rows
        foreach (var result in list)
        {
            AppendResultRow(sb, result);
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    protected override string FormatCore(IEnumerable<ComparisonResult> comparisons)
    {
        var sb = new StringBuilder();
        var list = comparisons.ToList();

        if (list.Count == 0)
            return string.Empty;

        AppendSchema(sb, "comparisons", ComparisonsHeader());

        // Header row
        AppendComparisonsHeader(sb);

        // Data rows
        foreach (var comparison in list)
        {
            AppendComparisonRow(sb, comparison);
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    protected override string FormatCore(BenchmarkSuite suite)
    {
        var sb = new StringBuilder();

        // The contract belongs to the first line of the file: a consumer has to be
        // able to parse the sections before it reads them.
        if (suite.Results.Count > 0)
            AppendSchema(sb, "results", ResultsHeader());

        // Suite metadata as comment header
        if (Options.IncludeEnvironment)
        {
            sb.AppendLine($"# Suite: {Escape(suite.Name)}");
            if (!string.IsNullOrEmpty(suite.Description))
                sb.AppendLine($"# Description: {Escape(suite.Description!)}");
            sb.AppendLine($"# Environment: {Escape(suite.Environment.ToString())}");
            sb.AppendLine(
                $"# CpuCounter: {Escape(EnvironmentInfo.DescribeCpuCycleMeasurement(suite.Environment.CpuCycleMeasurement))}"
            );

            if (!suite.Environment.CpuCyclesAvailable)
                sb.AppendLine("# CpuCounterNote: unavailable");
            else if (!suite.Environment.CpuCyclesAreMeaningful)
                sb.AppendLine("# CpuCounterNote: proxy-only");
        }

        if (Options.IncludeGcInfo && AnyGcNotCollected(suite))
            sb.AppendLine($"# GcInfoNote: unavailable ({FormatterBase.NotCollected})");

        if (Options.IncludeTimestamp)
        {
            sb.AppendLine($"# Timestamp: {FormatTimestamp(suite.Timestamp)} UTC");
            sb.AppendLine($"# Duration: {FormatDuration(suite.Duration)}");
        }

        sb.AppendLine();

        // Results section
        if (suite.Results.Count > 0)
        {
            sb.AppendLine("# Results");
            AppendResultsHeader(sb);
            foreach (var result in suite.Results)
            {
                AppendResultRow(sb, result);
            }

            AppendNoiseNote(sb, suite.Results);
        }

        // Comparisons section
        if (!(suite.Comparisons?.Count > 0))
            return sb.ToString();
        sb.AppendLine();
        AppendSchema(sb, "comparisons", ComparisonsHeader());
        sb.AppendLine("# Comparisons");
        AppendComparisonsHeader(sb);
        foreach (var comparison in suite.Comparisons)
        {
            AppendComparisonRow(sb, comparison);
        }

        return sb.ToString();
    }

    #region Results

    private void AppendResultsHeader(StringBuilder sb)
    {
        var columns = new List<string> { "Name", "Category", "Avg_ns", "P50_ns" };

        if (Options.IncludePercentiles)
        {
            columns.AddRange([
                "P90_ns",
                "P95_ns",
                "P99_ns",
                "Min_ns",
                "Max_ns",
                "StdDev_ns",
                "StdErr_ns",
                "RSD_pct",
            ]);
        }

        if (Options.IncludeCpuCycles)
        {
            columns.Add("CpuCycles");
        }

        if (Options.IncludeGcInfo)
        {
            columns.AddRange(["GC0", "GC1", "GC2", GcApproximateColumn]);
        }

        columns.AddRange(["SampleCount", "IterationsPerSample"]);

        if (Options.IncludeTimestamp)
        {
            columns.Add("Timestamp");
        }

        sb.AppendLine(string.Join(Separator, columns));
    }

    private void AppendResultRow(StringBuilder sb, BenchmarkResult result)
    {
        var s = result.Statistics;
        var values = new List<string>
        {
            Escape(result.Name),
            Escape(result.Category ?? ""),
            FormatPerOp(s, s.Avg),
            FormatPerOp(s, s.P50),
        };

        if (Options.IncludePercentiles)
        {
            values.AddRange([
                FormatPerOp(s, s.P90),
                FormatPerOp(s, s.P95),
                FormatPerOp(s, s.P99),
                FormatPerOp(s, s.Min),
                FormatPerOp(s, s.Max),
                FormatPerOp(s, s.StdDev),
                FormatPerOp(s, s.StandardError),
                FormatNumber(s.RelativeStdDevPercent),
            ]);
        }

        if (Options.IncludeCpuCycles)
        {
            values.Add(FormatCycles(s));
        }

        if (Options.IncludeGcInfo)
        {
            values.AddRange(FormatGcCounts(s.GcInfo));
            values.Add(FormatGcApproximate(s.GcInfo));
        }

        values.Add(result.SampleCount.ToString(CultureInfo.InvariantCulture));
        values.Add(result.IterationsPerSample.ToString(CultureInfo.InvariantCulture));

        if (Options.IncludeTimestamp)
        {
            values.Add(result.Timestamp.ToString("o", CultureInfo.InvariantCulture));
        }

        sb.AppendLine(string.Join(Separator, values));
    }

    #endregion

    #region Comparisons

    private void AppendComparisonsHeader(StringBuilder sb)
    {
        var columns = new List<string> { "TestCase", "Provider", "Avg_ns", "Speedup" };

        if (Options.IncludePercentiles)
        {
            columns.AddRange(["P50_ns", "P90_ns", "P99_ns", "StdErr_ns", "RSD_pct"]);
        }

        if (Options.IncludeCpuCycles)
        {
            columns.Add("CpuCycles");
        }

        if (Options.IncludeGcInfo)
        {
            columns.AddRange(["GC0", "GC1", "GC2", GcApproximateColumn]);
        }

        sb.AppendLine(string.Join(Separator, columns));
    }

    private void AppendComparisonRow(StringBuilder sb, ComparisonResult c)
    {
        // Output two rows: one for candidate and one for baseline
        AppendComparisonSingleRow(
            sb,
            c.Name,
            Options.CandidateLabel,
            c.Candidate.Statistics,
            c.Speedup
        );
        AppendComparisonSingleRow(sb, c.Name, Options.BaselineLabel, c.Baseline.Statistics, null);
    }

    private void AppendComparisonSingleRow(
        StringBuilder sb,
        string testCase,
        string provider,
        Statistics stats,
        double? speedup
    )
    {
        // The per-operation columns of this section are the same kind of cell as the
        // results section's, and the section's own `# Schema:` line says so: a
        // comparison row therefore goes through the very same helpers
        // (FormatPerOp / FormatCycles) instead of the plain number formatter, so a
        // sub-floor or unmeasured run cannot print a number here and `<limit` / N/A
        // there (G8 · C1).
        var values = new List<string>
        {
            Escape($"{provider} * {testCase}"),
            Escape(provider),
            FormatPerOp(stats, stats.Avg),
            speedup.HasValue ? FormatNumber(speedup.Value) : "",
        };

        if (Options.IncludePercentiles)
        {
            values.AddRange([
                FormatPerOp(stats, stats.P50),
                FormatPerOp(stats, stats.P90),
                FormatPerOp(stats, stats.P99),
                FormatPerOp(stats, stats.StandardError),
                FormatNumber(stats.RelativeStdDevPercent),
            ]);
        }

        if (Options.IncludeCpuCycles)
        {
            values.Add(FormatCycles(stats));
        }

        if (Options.IncludeGcInfo)
        {
            values.AddRange(FormatGcCounts(stats.GcInfo));
            values.Add(FormatGcApproximate(stats.GcInfo));
        }

        sb.AppendLine(string.Join(Separator, values));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Writes the schema line of one section: the exact column order that follows,
    /// the shapes a cell can take, the meaning of the GC columns and the schema
    /// version.
    /// <para>
    /// Batch 0 gave the per-operation cells a third state (an upper bound,
    /// <c>&lt;limit</c>, when the value sits at the harness floor) and added the
    /// <c>GCApproximate</c> column, so the old contract "a numeric column holds a
    /// number or N/A" no longer describes the file (G4 section 7). The column list is
    /// derived from the header that is really written, so the schema cannot drift
    /// from the columns and never names a column the options left out.
    /// </para>
    /// </summary>
    private static void AppendSchema(StringBuilder sb, string section, string header)
    {
        var columns = header.Split(new[] { Separator }, StringSplitOptions.None);
        var perOp = columns
            .Where(static column => column.EndsWith("_ns", StringComparison.Ordinal))
            .ToArray();

        var schema = "# Schema: v2; section=" + section;
        schema += "; columns=" + string.Join("|", columns);
        if (perOp.Length > 0)
        {
            schema +=
                "; per-op cells ("
                + string.Join(",", perOp)
                + ") = number (nanoseconds)"
                + " | \"<limit\" (upper bound: the value is at the harness floor)"
                + " | \""
                + FormatterBase.NotCollected
                + "\" (not measured)";
        }

        if (Array.IndexOf(columns, "RSD_pct") >= 0)
            schema += "; RSD_pct = percent";

        if (Array.IndexOf(columns, "CpuCycles") >= 0)
            schema += "; CpuCycles = count | \"~count\" (not a thread-scoped cycle count)";

        if (Array.IndexOf(columns, GcApproximateColumn) >= 0)
        {
            schema +=
                "; GC counters = count | \""
                + FormatterBase.NotCollected
                + "\" (not collected); "
                + GcApproximateColumn
                + " = 1 (approximate counters) | 0 (exact) | \""
                + FormatterBase.NotCollected
                + "\" (not collected)";
        }

        sb.AppendLine(schema);
    }

    /// <summary>
    /// Writes the "high variance" note of the results section. A run whose relative
    /// standard deviation is at least 10% cannot support a per-operation conclusion
    /// even though every cell holds a number, so the suite table has to say it
    /// (Batch 0 · 0.5, third clause).
    /// </summary>
    private static void AppendNoiseNote(StringBuilder sb, IReadOnlyList<BenchmarkResult> results)
    {
        var noisy = results.Where(static r => r.Statistics.HasHighVariance).ToList();
        if (noisy.Count == 0)
            return;

        sb.AppendLine(
            "# NoiseNote: high variance (RSD >= 10%): "
                + string.Join(
                    ", ",
                    noisy.Select(static r =>
                        r.Name + "=" + r.Statistics.RelativeStdDevPercent.ToString(
                            "F1",
                            CultureInfo.InvariantCulture
                        ) + "%"
                    )
                )
                + "; treat the difference as unresolved"
        );
    }

    /// <summary>The header line of the results section, exactly as it is written.</summary>
    private string ResultsHeader()
    {
        var header = new StringBuilder();
        AppendResultsHeader(header);
        return header.ToString().TrimEnd((char)13, (char)10);
    }

    /// <summary>The header line of the comparisons section, exactly as it is written.</summary>
    private string ComparisonsHeader()
    {
        var header = new StringBuilder();
        AppendComparisonsHeader(header);
        return header.ToString().TrimEnd((char)13, (char)10);
    }

    /// <summary>
    /// Renders a per-operation cell. A value the run could not resolve above the
    /// harness floor is written as an upper bound (<c>&lt;limit</c>) instead of a
    /// number a consumer would mistake for a real measurement, and a run whose
    /// clock never advanced is written as <c>N/A</c> (not measured, exactly like a
    /// GC count that was not collected).
    /// </summary>
    private static string FormatPerOp(Statistics statistics, double nanoseconds) =>
        statistics.Unmeasured
            ? "N/A"
            : statistics.ResolutionLimited
                ? "<" + FormatNumber(statistics.ResolutionLimitNanoseconds)
                : FormatNumber(nanoseconds);

    /// <summary>
    /// Renders the cycle cell. <c>~</c> marks a process-wide counter or a
    /// monotonic-clock proxy rather than a thread-scoped CPU cycle count.
    /// </summary>
    private static string FormatCycles(Statistics statistics) =>
        (statistics.CpuCyclesAreApproximate ? "~" : "") + FormatNumber(statistics.CpuCyclesPerOp);

    /// <summary>
    /// Header of the boolean column that carries <see cref="GcInfo.IsApproximate"/>,
    /// which the three GC counter columns cannot express.
    /// </summary>
    private const string GcApproximateColumn = "GCApproximate";

    /// <summary>
    /// Render the three GC counters. A missing <see cref="GcInfo"/> means "not
    /// collected" and becomes <see cref="FormatterBase.NotCollected"/> rather
    /// than <c>0</c>, so a CSV consumer cannot mistake it for "zero collections".
    /// </summary>
    private static string[] FormatGcCounts(GcInfo? gc)
    {
        if (gc is null)
            return [
                FormatterBase.NotCollected,
                FormatterBase.NotCollected,
                FormatterBase.NotCollected,
            ];

        return [
            gc.Gen0.ToString(CultureInfo.InvariantCulture),
            gc.Gen1.ToString(CultureInfo.InvariantCulture),
            gc.Gen2.ToString(CultureInfo.InvariantCulture),
        ];
    }

    /// <summary>
    /// Render the <c>GCApproximate</c> flag: <c>1</c> when the counters are an
    /// approximation, <c>0</c> when they are exact, "not collected" otherwise.
    /// </summary>
    private static string FormatGcApproximate(GcInfo? gc) =>
        gc is null ? FormatterBase.NotCollected : gc.IsApproximate ? "1" : "0";

    /// <summary>True when any statistics in the suite has no GC counters.</summary>
    private static bool AnyGcNotCollected(BenchmarkSuite suite)
    {
        if (suite.Results.Any(static r => r.Statistics.GcInfo is null))
            return true;

        return suite.Comparisons?.Any(
            static c => c.Candidate.Statistics.GcInfo is null || c.Baseline.Statistics.GcInfo is null
        ) ?? false;
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // Prevent CSV formula injection on the raw content first: cells
        // starting with these characters would be interpreted as formulas by
        // spreadsheet applications. Prefixing before quoting keeps the
        // apostrophe inside the field once the quotes are stripped.
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;

        // If contains comma, quote, or newline, wrap in quotes and escape internal quotes
        if (
            value.Contains(',')
            || value.Contains('"')
            || value.Contains('\n')
            || value.Contains('\r')
        )
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("G", CultureInfo.InvariantCulture);
    }

    #endregion

    #region Static Helpers

    /// <summary>
    /// Write CSV to a file, creating directory if needed.
    /// </summary>
    public static void WriteToFile(
        string filePath,
        BenchmarkResult result,
        FormatterOptions? options = null
    )
    {
        var formatter = new CsvFormatter(options);
        WriteToFileInternal(formatter.Options.ResolvePath(filePath), formatter.Format(result));
    }

    /// <summary>
    /// Write CSV to a file, creating directory if needed.
    /// </summary>
    public static void WriteToFile(
        string filePath,
        IEnumerable<BenchmarkResult> results,
        FormatterOptions? options = null
    )
    {
        var formatter = new CsvFormatter(options);
        WriteToFileInternal(formatter.Options.ResolvePath(filePath), formatter.Format(results));
    }

    /// <summary>
    /// Write CSV to a file, creating directory if needed.
    /// </summary>
    public static void WriteToFile(
        string filePath,
        IEnumerable<ComparisonResult> comparisons,
        FormatterOptions? options = null
    )
    {
        var formatter = new CsvFormatter(options);
        WriteToFileInternal(formatter.Options.ResolvePath(filePath), formatter.Format(comparisons));
    }

    /// <summary>
    /// Write CSV to a file, creating directory if needed.
    /// </summary>
    public static void WriteToFile(
        string filePath,
        BenchmarkSuite suite,
        FormatterOptions? options = null
    )
    {
        var formatter = new CsvFormatter(options);
        WriteToFileInternal(formatter.Options.ResolvePath(filePath), formatter.Format(suite));
    }

    /// <summary>
    /// Append CSV to a file (without header if file exists), creating directory if needed.
    /// </summary>
    public static void AppendToFile(
        string filePath,
        BenchmarkResult result,
        FormatterOptions? options = null
    )
    {
        var formatter = new CsvFormatter(options);
        var resolvedPath = formatter.Options.ResolvePath(filePath);
        var content = formatter.Format(result);

        if (File.Exists(resolvedPath))
        {
            // Skip the header and append the rest byte-for-byte so embedded
            // newlines inside quoted values are not re-normalized.
            File.AppendAllText(resolvedPath, SkipHeaderLine(content));
        }
        else
        {
            WriteToFileInternal(resolvedPath, content);
        }
    }

    /// <summary>
    /// Returns everything that follows the file's header block: the leading
    /// comment lines (the <c># Schema:</c> contract line and any note) and the
    /// column header, so an appended write adds data rows only.
    /// <para>
    /// The scan is quote-aware: a naive <c>Split((char)10)</c> would cut through a
    /// quoted value that carries a newline.
    /// </para>
    /// </summary>
    private static string SkipHeaderLine(string csv)
    {
        var lineStart = 0;
        var seenColumnHeader = false;
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    i++; // escaped quote inside a quoted field
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (c != (char)10 || inQuotes)
                continue;

            var isComment = i > lineStart && csv[lineStart] == '#';
            if (!isComment)
            {
                // The first comment-free line is the column header; the next one is
                // the first data row and must be kept.
                if (seenColumnHeader)
                    return csv.Substring(lineStart);

                seenColumnHeader = true;
            }

            lineStart = i + 1;
        }

        return string.Empty;
    }

    #endregion
}
