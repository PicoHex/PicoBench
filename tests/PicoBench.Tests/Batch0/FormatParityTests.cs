namespace PicoBench.Tests.Batch0;

/// <summary>
/// T4 — the four formats have to say the same thing about the same run.
/// <para>
/// G8 · C1 (see <see cref="ComparisonCellContractTests"/>) pinned the CSV
/// <c>results</c> section to the CSV <c>comparisons</c> section. The three display
/// formats have the same two tables — a results table and a comparisons table —
/// and the same kind of cell in each, so the same three per-operation states
/// (<c>number</c> | <c>&lt;limit</c> | <c>N/A</c>) and the same <c>~</c> marker on
/// the cycle column have to hold there too. A reader of the terminal, of a
/// Markdown file and of an HTML page must not see <c>~42</c> in one table and a
/// bare <c>42</c> in the other of the same document.
/// </para>
/// <para>
/// macOS is where the cycle column gets its second meaning: there is no
/// architectural counter, <c>mach_absolute_time</c> is a monotonic clock, and an
/// asynchronous run reads a process-wide counter. The value is therefore
/// <b>kept</b> (it is a valid duration) and flagged with the one marker the
/// library defines (<c>~</c>) in all four formats, and the three display formats
/// say what the marker means instead of leaving it to the reader.
/// </para>
/// </summary>
public class FormatParityTests
{

    /// <summary>
    /// Console — a sub-floor run. The comparison cells must publish the same upper
    /// bound the results cell publishes, in the same unit, and the proxy cycle
    /// counter must carry the same <c>~</c> in both tables, under the same header,
    /// with the one sentence that explains the marker.
    /// <para>
    /// Expected before the fix: <b>RED</b> — the comparison table's per-operation
    /// cells did publish the upper bound (that half was already right), but its
    /// cycle column printed a bare <c>42</c> while the results table printed
    /// <c>~42</c>, its header said <c>CPU</c> where the results table said
    /// <c>CPU Cycles</c>, and the table explained neither the bound nor the marker.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "T4/console parity")]
    public async Task Console_ComparisonCells_MatchTheResultsTable()
    {
        var subFloor = SubFloorStatistics();
        var console = new ConsoleFormatter().Format(
            Suite(
                BenchmarkResultFactory.Create("sub-floor", statistics: subFloor),
                ComparisonOf(subFloor, "sub-floor-pair")
            )
        );

        Batch0Support.Log(console);

        // 0.3625 ns/op, unit = ns, disclosed limit = 2.34 ns ⇒ `<2.3` in both tables.
        await Assert.That(console).Contains("<2.3");
        await Assert.That(console).DoesNotContain("0.3625");

        // The proxy cycle cell is `~42` in the results table AND in the comparison
        // table (two rows, so at least three occurrences in a suite document).
        var marked = console.Split("~42").Length - 1;
        Batch0Support.Log($"'~42' occurrences = {marked}");
        await Assert
            .That(marked)
            .IsGreaterThanOrEqualTo(3)
            .Because("both tables must mark the same proxy counter with `~`");

        // One header for the one kind of column.
        await Assert.That(console).Contains("CPU Cycles");
        await Assert.That(console).Contains("monotonic clock proxy");
    }

    /// <summary>
    /// Markdown and HTML — the same fixture as the Console test. The comparison
    /// rows carry the same <c>(below resolution)</c> marker the results row carries,
    /// the cycle column keeps the <c>~</c> in both tables, and each document
    /// explains the marker once.
    /// <para>
    /// Expected before the fix: <b>RED</b> — the comparison rows wrote <c>&lt;2.3</c>
    /// with no <c>(below resolution)</c> suffix (Markdown) / no
    /// <c>note</c> span (HTML), the comparison cycle column printed a bare
    /// <c>42</c>, and neither comparison table carried the note the results table
    /// prints. The unit header itself was already correct.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "T4/markup parity")]
    public async Task MarkdownAndHtml_ComparisonCells_MatchTheResultsTable()
    {
        var subFloor = SubFloorStatistics();
        var suite = Suite(
            BenchmarkResultFactory.Create("sub-floor", statistics: subFloor),
            ComparisonOf(subFloor, "sub-floor-pair")
        );

        var markdown = new MarkdownFormatter().Format(suite);
        var html = new HtmlFormatter().Format(suite);
        Batch0Support.Log(markdown);
        Batch0Support.Log(html);

        foreach (var (name, document) in new[] { ("markdown", markdown), ("html", html) })
        {
            var marked = document.Split("~42").Length - 1;
            var notes = document.Split("below resolution").Length - 1;
            Batch0Support.Log($"{name}: ~42 x{marked}, below-resolution x{notes}");

            await Assert
                .That(marked)
                .IsGreaterThanOrEqualTo(3)
                .Because($"{name} must mark the proxy cycle cell in both tables and explain it");
            await Assert
                .That(document)
                .DoesNotContain("0.3625")
                .Because($"{name} must not publish a sub-floor residual as a number");
            await Assert
                .That(notes)
                .IsGreaterThanOrEqualTo(3)
                .Because($"{name} must mark its per-operation cells in both tables");
        }

        // The column is named in both tables of both documents.
        await Assert.That(html).Contains("CPU Cycles");
        await Assert.That(markdown).Contains("CPU Cycle");
    }

    /// <summary>
    /// macOS cycle semantics — the value is kept, marked with the one <c>~</c> in
    /// all four formats, and explained in the three display formats; CSV carries the
    /// same contract in its <c># Schema:</c> line. The negative control keeps the
    /// flag honest: an exact counter is a bare count with no explanation anywhere.
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "T4/macos cycles")]
    public async Task ProxyCycleCounter_IsMarkedAndExplained_InAllFourFormats()
    {
        var approximate = CycleStatistics(approximate: true);
        var suite = Suite(
            BenchmarkResultFactory.Create("proxy-counter", statistics: approximate),
            ComparisonOf(approximate, "proxy-counter-pair")
        );

        var console = new ConsoleFormatter().Format(suite);
        var markdown = new MarkdownFormatter().Format(suite);
        var html = new HtmlFormatter().Format(suite);
        var csv = new CsvFormatter().Format(suite);

        // The value stays in every format (a monotonic-clock proxy is still a
        // duration), with the one marker:
        //   console  = result row + 2 comparison rows = 3
        //   markdown/html = result row + 2 comparison rows = 3
        foreach (var (name, document) in new[]
        {
            ("console", console),
            ("markdown", markdown),
            ("html", html),
        })
        {
            var marked = document.Split("~42").Length - 1;
            Batch0Support.Log($"{name}: ~42 x{marked}");
            await Assert
                .That(marked)
                .IsGreaterThanOrEqualTo(3)
                .Because($"{name} must keep and mark the proxy value in both tables");
        }

        // CSV: the `~` marker in both sections, plus its own contract line.
        await Assert.That(ReadCsvCell(csv, "results", "proxy-counter", "CpuCycles")).IsEqualTo("~42");
        foreach (var cell in ReadCsvColumn(csv, "comparisons", "CpuCycles"))
            await Assert.That(cell).IsEqualTo("~42");
        await Assert.That(csv).Contains("CpuCycles = count | \"~count\"");

        // The three display formats say what the marker means.
        await Assert.That(console).Contains("monotonic clock proxy");
        await Assert.That(markdown).Contains("monotonic clock proxy");
        await Assert.That(html).Contains("monotonic clock proxy");

        // Negative control: a thread-scoped counter is a bare count and carries no
        // explanation in any format.
        var exact = CycleStatistics(approximate: false);
        var exactSuite = Suite(
            BenchmarkResultFactory.Create("thread-counter", statistics: exact),
            ComparisonOf(exact, "thread-counter-pair")
        );

        var exactConsole = new ConsoleFormatter().Format(exactSuite);
        var exactMarkdown = new MarkdownFormatter().Format(exactSuite);
        var exactHtml = new HtmlFormatter().Format(exactSuite);
        var exactCsv = new CsvFormatter().Format(exactSuite);

        foreach (var (name, document) in new[]
        {
            ("console", exactConsole),
            ("markdown", exactMarkdown),
            ("html", exactHtml),
        })
        {
            await Assert
                .That(document)
                .DoesNotContain("~42")
                .Because($"{name} must not mark a thread-scoped counter");
            await Assert
                .That(document)
                .DoesNotContain("monotonic clock proxy")
                .Because($"{name} must not explain a marker it does not print");
        }

        await Assert.That(ReadCsvCell(exactCsv, "results", "thread-counter", "CpuCycles")).IsEqualTo("42");
    }

    // ─── Fixtures ──────────────────────────────────────────────────

    /// <summary>
    /// G8 §11.2's workload: the residual of an empty lambda after the harness floor
    /// was deducted (0.3625 ns/op) under a disclosed limit of 2.34 ns/op — below the
    /// run's own resolution — with a proxy (approximate) cycle counter.
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
            CpuCyclesPerOp = 42.0,
            CpuCyclesAreApproximate = true,
            ResolutionLimitNanoseconds = 2.34,
            ResolutionLimited = true,
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
    /// any difference between two tables comes from the formatter alone.
    /// </summary>
    private static ComparisonResult ComparisonOf(Statistics statistics, string name) =>
        new(
            name: name,
            baseline: BenchmarkResultFactory.Create(name + "-baseline", statistics: statistics),
            candidate: BenchmarkResultFactory.Create(name + "-candidate", statistics: statistics)
        );

    private static BenchmarkSuite Suite(BenchmarkResult result, ComparisonResult comparison) =>
        new(
            name: "format parity suite",
            environment: new EnvironmentInfo(),
            results: [result],
            duration: TimeSpan.FromSeconds(1),
            comparisons: [comparison]
        );

    // ─── CSV reading ───────────────────────────────────────────────

    /// <summary>
    /// One cell of a CSV section, addressed by section, row name and column name
    /// (the same marker-based reading the G8 · C1 tests use).
    /// </summary>
    private static string ReadCsvCell(string csv, string section, string rowName, string column)
    {
        var (header, rows) = ReadCsvSection(csv, section);
        var columnIndex = Array.IndexOf(header, column);
        if (columnIndex < 0)
            throw new InvalidOperationException($"the CSV {section} header has no {column} column");

        foreach (var row in rows)
        {
            if (row.Any(cell => cell.Contains(rowName, StringComparison.Ordinal)))
                return row[columnIndex];
        }

        throw new InvalidOperationException($"the CSV {section} section has no row for {rowName}");
    }

    /// <summary>Every cell of one CSV section column, row by row.</summary>
    private static IEnumerable<string> ReadCsvColumn(string csv, string section, string column)
    {
        var (header, rows) = ReadCsvSection(csv, section);
        var columnIndex = Array.IndexOf(header, column);
        if (columnIndex < 0)
            throw new InvalidOperationException($"the CSV {section} header has no {column} column");

        foreach (var row in rows)
            yield return row[columnIndex];
    }

    /// <summary>
    /// Splits one CSV section out of the document by its own <c># Schema:</c>
    /// marker: the first comment-free line is the header, the remaining
    /// comment-free lines are the rows.
    /// </summary>
    private static (string[] Header, List<string[]> Rows) ReadCsvSection(string csv, string section)
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

            rows.Add(line.Split(',').Select(static cell => cell.Trim('"', '\'')).ToArray());
        }

        return (header, rows);
    }
}
