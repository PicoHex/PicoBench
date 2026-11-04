namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · G8 · C1 — the <c># Schema:</c> line of the <c>comparisons</c> section
/// promises the same three per-operation cell states as the <c>results</c> section
/// (<c>number</c> | <c>&lt;limit</c> | <c>N/A</c>) and the same <c>~</c> marker on
/// <c>CpuCycles</c>.
/// <para>
/// G8 §11.2 measured the mismatch on one and the same sub-floor lambda: the results
/// row printed <c>Avg_ns='&lt;2.34'</c> while the baseline row of the comparison
/// printed <c>Avg_ns='0.3625'</c> — a number a consumer would read as a measurement,
/// and a cell shape the contract line of that very section says cannot occur. The
/// comparison rows therefore go through <c>FormatPerOp</c>/<c>FormatCycles</c>, and
/// these tests pin the two sections together: the same <see cref="Statistics"/>
/// instance has to produce the same cell shape in both, in a suite file and in a
/// comparisons-only file.
/// </para>
/// </summary>
public class ComparisonCellContractTests
{
    /// <summary>The per-operation columns of the comparisons section (its own schema line).</summary>
    private static readonly string[] ComparisonPerOpColumns =
    [
        "Avg_ns",
        "P50_ns",
        "P90_ns",
        "P99_ns",
        "StdErr_ns",
    ];

    /// <summary>The per-operation columns of the results section (its own schema line).</summary>
    private static readonly string[] ResultsPerOpColumns =
    [
        "Avg_ns",
        "P50_ns",
        "P90_ns",
        "P95_ns",
        "P99_ns",
        "Min_ns",
        "Max_ns",
        "StdDev_ns",
        "StdErr_ns",
    ];

    /// <summary>
    /// G8 §11.2 — a run that could not resolve its workload above the harness floor
    /// publishes an <b>upper bound</b>. The comparison rows must publish the same
    /// bound as the results row of the same statistics, not <c>0.3625</c>.
    /// <para>Expected before the fix: <b>RED</b> — the two comparison rows carry the
    /// plain number <c>0.3625</c> in every per-operation column while the results row
    /// carries <c>&lt;2.34</c>.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "G8/C1")]
    public async Task SubFloorComparisonRows_PublishTheSameUpperBoundAsTheResultsRow()
    {
        var statistics = SubFloorStatistics();
        var result = BenchmarkResultFactory.Create("sub-floor", statistics: statistics);
        var comparison = ComparisonOf(statistics, "sub-floor-pair");

        var suiteCsv = new CsvFormatter().Format(Suite(result, comparison));
        var resultsCell = Cell(suiteCsv, "results", 0, "Avg_ns");
        Batch0Support.Log($"results Avg_ns = '{resultsCell}'");

        // The library's own answer for this workload: an upper bound, not a number.
        await Assert.That(resultsCell).IsEqualTo("<2.34");

        foreach (var section in new[] { "results", "comparisons" })
        {
            var (header, rows) = ReadSection(suiteCsv, section);
            var perOp = section == "results" ? ResultsPerOpColumns : ComparisonPerOpColumns;

            foreach (var column in perOp)
            {
                var index = Array.IndexOf(header, column);
                await Assert.That(index).IsGreaterThanOrEqualTo(0);
                for (var row = 0; row < rows.Count; row++)
                {
                    var cell = rows[row][index];
                    Batch0Support.Log($"{section}[{row}] {column} = '{cell}'");

                    await Assert
                        .That(cell)
                        .IsEqualTo("<2.34")
                        .Because(
                            $"the {section} section must publish the upper bound for a "
                                + $"resolution-limited run, not a number (cell '{cell}')"
                        );
                    await Assert.That(cell).DoesNotContain("0.3625");
                }
            }
        }

        // The comparisons-only document carries its own `# Schema:` line, so the same
        // promise has to hold there too.
        var comparisonsOnly = new CsvFormatter().Format(new[] { comparison });
        foreach (var cell in PerOpCells(comparisonsOnly, "comparisons", ComparisonPerOpColumns))
        {
            Batch0Support.Log($"comparisons-only cell = '{cell}'");
            await Assert.That(cell).IsEqualTo("<2.34");
        }
    }

    /// <summary>
    /// Same input, third cell state: a run whose timing source never advanced is
    /// <c>N/A</c> ("not measured") — in both sections, so the comparisons rows cannot
    /// print a timing quantum as if it were a measurement.
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "G8/C1")]
    public async Task UnmeasuredComparisonRows_PrintNotMeasuredLikeTheResultsRow()
    {
        var statistics = UnmeasuredStatistics();

        var result = BenchmarkResultFactory.Create("unmeasured", statistics: statistics);
        var comparison = ComparisonOf(statistics, "unmeasured-pair");
        var suiteCsv = new CsvFormatter().Format(Suite(result, comparison));

        await Assert.That(Cell(suiteCsv, "results", 0, "Avg_ns")).IsEqualTo("N/A");

        foreach (var cell in PerOpCells(suiteCsv, "comparisons", ComparisonPerOpColumns))
        {
            Batch0Support.Log($"comparisons cell = '{cell}'");
            await Assert.That(cell).IsEqualTo("N/A");
        }
    }

    /// <summary>
    /// The <c>CpuCycles</c> column says <c>count | "~count"</c> in both schema lines,
    /// so <see cref="Statistics.CpuCyclesAreApproximate"/> must prefix the comparisons
    /// rows exactly like the results row — and must not prefix them when the counter
    /// really is thread-scoped (the negative control that keeps the marker honest).
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "G8/C1")]
    public async Task ApproximateCycles_CarryTheTildeMarkerInBothSections()
    {
        var approximate = CycleStatistics(approximate: true);

        var suiteCsv = new CsvFormatter().Format(
            Suite(
                BenchmarkResultFactory.Create("proxy-counter", statistics: approximate),
                ComparisonOf(approximate, "proxy-counter-pair")
            )
        );

        var resultsCycles = Cell(suiteCsv, "results", 0, "CpuCycles");
        Batch0Support.Log($"results CpuCycles = '{resultsCycles}'");
        await Assert.That(resultsCycles).IsEqualTo("~42");

        foreach (var cell in Cells(suiteCsv, "comparisons", "CpuCycles"))
        {
            Batch0Support.Log($"comparisons CpuCycles = '{cell}'");
            await Assert.That(cell).IsEqualTo("~42");
        }

        // Negative control: a thread-scoped counter is a plain count in both sections.
        var exact = CycleStatistics(approximate: false);

        var exactCsv = new CsvFormatter().Format(
            Suite(
                BenchmarkResultFactory.Create("thread-counter", statistics: exact),
                ComparisonOf(exact, "thread-counter-pair")
            )
        );

        await Assert.That(Cell(exactCsv, "results", 0, "CpuCycles")).IsEqualTo("42");
        foreach (var cell in Cells(exactCsv, "comparisons", "CpuCycles"))
            await Assert.That(cell).IsEqualTo("42");
    }

    // ─── Fixtures ──────────────────────────────────────────────────

    /// <summary>
    /// The workload G8 measured: the residual of an empty lambda after the harness
    /// floor was deducted (0.3625 ns/op) with a disclosed limit of 2.34 ns/op, i.e.
    /// below the run's own resolution.
    /// </summary>
    private static Statistics SubFloorStatistics() =>
        new()
        {
            Avg = 0.3625,
            P50 = 0.3625,
            P90 = 0.3625,
            P95 = 0.3625,
            P99 = 0.3625,
            Min = 0.3625,
            Max = 0.3625,
            StdDev = 0.0,
            StandardError = 0.0,
            RelativeStdDevPercent = 0.0,
            CpuCyclesPerOp = 1.0,
            ResolutionLimitNanoseconds = 2.34,
            ResolutionLimited = true,
            GcInfo = GcInfoFactory.Create(),
        };

    /// <summary>
    /// A run whose timing source never advanced for two samples: its per-operation
    /// cells are "not measured" (<c>N/A</c>), not a timing quantum.
    /// </summary>
    private static Statistics UnmeasuredStatistics() =>
        new()
        {
            Avg = 2.34,
            P50 = 2.34,
            P90 = 2.34,
            P95 = 2.34,
            P99 = 2.34,
            Min = 2.34,
            Max = 2.34,
            StdDev = 0.0,
            StandardError = 0.0,
            RelativeStdDevPercent = 0.0,
            CpuCyclesPerOp = 1.0,
            ResolutionLimitNanoseconds = 2.34,
            ResolutionLimited = true,
            UnmeasuredSampleCount = 2,
            GcInfo = GcInfoFactory.Create(),
        };

    /// <summary>A resolved workload whose CPU cycle counter is (or is not) a proxy.</summary>
    private static Statistics CycleStatistics(bool approximate) =>
        new()
        {
            Avg = 100.0,
            P50 = 100.0,
            P90 = 100.0,
            P95 = 100.0,
            P99 = 100.0,
            Min = 100.0,
            Max = 100.0,
            StdDev = 1.0,
            StandardError = 0.5,
            RelativeStdDevPercent = 1.0,
            CpuCyclesPerOp = 42.0,
            CpuCyclesAreApproximate = approximate,
            GcInfo = GcInfoFactory.Create(),
        };

    /// <summary>
    /// A comparison whose candidate and baseline carry the <b>same</b> statistics, so
    /// any difference between the two sections comes from the formatter alone.
    /// </summary>
    private static ComparisonResult ComparisonOf(Statistics statistics, string name) =>
        new(
            name: name,
            baseline: BenchmarkResultFactory.Create(name + "-baseline", statistics: statistics),
            candidate: BenchmarkResultFactory.Create(name + "-candidate", statistics: statistics)
        );

    private static BenchmarkSuite Suite(BenchmarkResult result, ComparisonResult comparison) =>
        new(
            name: "cell contract suite",
            environment: new EnvironmentInfo(),
            results: [result],
            duration: TimeSpan.FromSeconds(1),
            comparisons: [comparison]
        );

    // ─── CSV reading ───────────────────────────────────────────────

    /// <summary>
    /// Splits one section out of a CSV document by its own <c># Schema:</c> marker:
    /// the first comment-free line is the header, the remaining comment-free lines are
    /// the rows. Reading by marker (instead of by line number) is what binds the
    /// assertion to the section the contract line belongs to.
    /// </summary>
    private static (string[] Header, List<string[]> Rows) ReadSection(string csv, string section)
    {
        var lines = csv.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(
            lines,
            line => line.StartsWith("# Schema: v2; section=" + section, StringComparison.Ordinal)
        );
        if (start < 0)
            throw new InvalidOperationException($"the CSV output has no '# Schema:' line for {section}");

        var index = start + 1;
        while (index < lines.Length && (lines[index].Length == 0 || lines[index][0] == '#'))
            index++;

        if (index >= lines.Length)
            throw new InvalidOperationException($"the CSV {section} section has no header row");

        var header = lines[index].Split(',');
        var rows = new List<string[]>();
        for (index++; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0 || line[0] == '#')
                break;

            rows.Add(line.Split(','));
        }

        return (header, rows);
    }

    /// <summary>Reads one cell of the section, addressed by column name.</summary>
    private static string Cell(string csv, string section, int row, string column)
    {
        var (header, rows) = ReadSection(csv, section);
        var index = Array.IndexOf(header, column);
        if (index < 0)
            throw new InvalidOperationException($"the CSV {section} header has no {column} column");

        if (row >= rows.Count)
            throw new InvalidOperationException($"the CSV {section} section has no row #{row}");

        return rows[row][index].Trim('"', '\'');
    }

    /// <summary>Every cell of one column of the section, row by row.</summary>
    private static IEnumerable<string> Cells(string csv, string section, string column)
    {
        var (header, rows) = ReadSection(csv, section);
        var index = Array.IndexOf(header, column);
        if (index < 0)
            throw new InvalidOperationException($"the CSV {section} header has no {column} column");

        foreach (var row in rows)
            yield return row[index].Trim('"', '\'');
    }

    /// <summary>Every per-operation cell of the section, row by row.</summary>
    private static IEnumerable<string> PerOpCells(string csv, string section, string[] columns)
    {
        foreach (var column in columns)
        {
            foreach (var cell in Cells(csv, section, column))
                yield return cell;
        }
    }
}
