namespace PicoBench.Tests.Batch0;

using System.Text.RegularExpressions;

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
/// <para>
/// <b>R-T4 · B1/B2 — the net these tests owe the reader.</b> The first version of
/// this file asserted on the <b>whole document</b> (<c>Contains("CPU Cycles")</c>, a
/// document-wide count of <c>~42</c> and of <c>below resolution</c>). R-T4 showed
/// that net was empty: deleting the comparisons table's <c>(below resolution)</c>
/// suffix, or renaming the cycle header back to <c>CPU</c>, still left the suite
/// green, because the results table — and the explanatory note under it, which
/// begins with the very same words — satisfied every document-wide count on its own.
/// Every assertion below is therefore <b>scoped to one table of one document</b>:
/// the document is sliced into its two table sections, each section is parsed into a
/// header row and data rows, and the <b>named</b> per-operation and cycle columns are
/// read cell by cell. A column that disappears is an
/// <see cref="InvalidOperationException"/> or a header mismatch, never a silently
/// skipped loop.
/// </para>
/// </summary>
public class FormatParityTests
{
    /// <summary>
    /// The per-operation columns the two tables share — the cells that carry one of
    /// the three states. Read by name, so a table that renames or drops one of them
    /// fails loudly instead of vacuously passing. The comparisons table has no
    /// <c>P95 (ns)</c> column; that is part of the contract asserted below, not an
    /// accident to work around.
    /// </summary>
    private static readonly string[] PerOpColumns =
    [
        "Avg (ns)",
        "P50 (ns)",
        "P90 (ns)",
        "P95 (ns)",
        "P99 (ns)",
        "StdErr (ns)",
    ];

    /// <summary>Which table of which document a cell expectation is written for.</summary>
    private enum Dialect
    {
        Console,
        Markdown,
        HtmlResults,
        HtmlComparisons,
    }

    // ─── Tests ─────────────────────────────────────────────────────

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

        var (results, resultsNotes) = ConsoleSectionTable(console, "Results");
        var (comparisons, comparisonNotes) = ConsoleSectionTable(console, "Comparisons");
        var fixture = StateFixture.Of("sub-floor");

        await Assert.That(results.Rows.Count).IsEqualTo(1).Because("the results table has one row");
        await Assert.That(comparisons.Rows.Count).IsEqualTo(2).Because("two comparison rows: candidate + baseline");

        // 0.3625 ns/op, unit = ns, disclosed limit = 2.34 ns ⇒ `<2.3` in every
        // per-operation cell of both tables, and the residual itself never printed.
        await AssertPerOpCells(results, "console results", fixture.Cells(Dialect.Console, comparisonTable: false));
        await AssertPerOpCells(
            comparisons,
            "console comparisons",
            fixture.Cells(Dialect.Console, comparisonTable: true)
        );
        await Assert.That(console).DoesNotContain("0.3625");

        // The proxy cycle cell is `~42` in the results table AND in the comparison
        // table — asserted per table, not as a document-wide occurrence count.
        await AssertCycleCells(results, "console results", "~42");
        await AssertCycleCells(comparisons, "console comparisons", "~42");

        // One header for the one kind of column, in both tables.
        await AssertCycleHeader(results, "console results", "CPU Cycles");
        await AssertCycleHeader(comparisons, "console comparisons", "CPU Cycles");

        // Each table explains the bound and the marker under itself, once.
        await AssertNoteOnce(resultsNotes, "console results", "below resolution");
        await AssertNoteOnce(comparisonNotes, "console comparisons", "below resolution");
        await AssertNoteOnce(resultsNotes, "console results", "monotonic clock proxy");
        await AssertNoteOnce(comparisonNotes, "console comparisons", "monotonic clock proxy");
    }

    /// <summary>
    /// Markdown and HTML — the same fixture as the Console test. The comparison rows
    /// carry the same <c>(below resolution)</c> marker the results row carries, the
    /// cycle column keeps the <c>~</c> in both tables, and each table of each
    /// document explains the marker once.
    /// <para>
    /// Expected before the fix: <b>RED</b> — the comparison rows wrote <c>&lt;2.3</c>
    /// with no <c>(below resolution)</c> suffix (Markdown) / no <c>note</c> span
    /// (HTML), the comparison cycle column printed a bare <c>42</c>, and neither
    /// comparison table carried the note the results table prints. The unit header
    /// itself was already correct.
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
        var fixture = StateFixture.Of("sub-floor");
        Batch0Support.Log(markdown);
        Batch0Support.Log(html);

        // ── Markdown: the marker is a suffix inside the cell, in both tables. ──
        var (markdownResults, markdownResultsNotes) = MarkdownSectionTable(markdown, "Results");
        var (markdownComparisons, markdownComparisonNotes) = MarkdownSectionTable(markdown, "Comparisons");
        await Assert.That(markdownResults.Rows.Count).IsEqualTo(1);
        await Assert.That(markdownComparisons.Rows.Count).IsEqualTo(2);
        await AssertPerOpCells(
            markdownResults,
            "markdown results",
            fixture.Cells(Dialect.Markdown, comparisonTable: false)
        );
        await AssertPerOpCells(
            markdownComparisons,
            "markdown comparisons",
            fixture.Cells(Dialect.Markdown, comparisonTable: true)
        );
        await AssertCycleCells(markdownResults, "markdown results", "~42");
        await AssertCycleCells(markdownComparisons, "markdown comparisons", "~42");
        await AssertCycleHeader(markdownResults, "markdown results", "CPU Cycle");
        await AssertCycleHeader(markdownComparisons, "markdown comparisons", "CPU Cycle");
        await AssertNoteOnce(markdownResultsNotes, "markdown results", "below resolution");
        await AssertNoteOnce(markdownComparisonNotes, "markdown comparisons", "below resolution");
        await AssertNoteOnce(markdownResultsNotes, "markdown results", "monotonic clock proxy");
        await AssertNoteOnce(markdownComparisonNotes, "markdown comparisons", "monotonic clock proxy");

        // ── HTML. R-T4 · B6: the two tables decorate a marked row differently, and
        // T4 freezes the results table's output, so the marker stays once in the
        // results row's Name cell while the comparison rows mark each per-operation
        // cell. The cells are asserted exactly as they are: the asymmetry is
        // recorded here on purpose instead of sitting untested.
        var (htmlResults, htmlResultsNotes) = HtmlSectionTable(html, "All Results");
        var (htmlComparisons, htmlComparisonNotes) = HtmlSectionTable(html, "Detailed Comparisons");
        await Assert.That(htmlResults.Rows.Count).IsEqualTo(1);
        await Assert.That(htmlComparisons.Rows.Count).IsEqualTo(2);
        await AssertPerOpCells(
            htmlResults,
            "html results",
            fixture.Cells(Dialect.HtmlResults, comparisonTable: false)
        );
        await AssertPerOpCells(
            htmlComparisons,
            "html comparisons",
            fixture.Cells(Dialect.HtmlComparisons, comparisonTable: true)
        );
        foreach (var name in htmlResults.ColumnCells("Name"))
            await Assert
                .That(name)
                .Contains("<span class=\"note\">(below resolution)</span>")
                .Because("html results marks the row once, in its Name cell (R-T4 · B6)");
        await AssertCycleCells(htmlResults, "html results", "~42");
        await AssertCycleCells(htmlComparisons, "html comparisons", "~42");
        await AssertCycleHeader(htmlResults, "html results", "CPU Cycles");
        await AssertCycleHeader(htmlComparisons, "html comparisons", "CPU Cycles");
        await AssertNoteOnce(htmlResultsNotes, "html results", "below resolution");
        await AssertNoteOnce(htmlComparisonNotes, "html comparisons", "below resolution");
        await AssertNoteOnce(htmlResultsNotes, "html results", "monotonic clock proxy");
        await AssertNoteOnce(htmlComparisonNotes, "html comparisons", "monotonic clock proxy");

        foreach (var (name, document) in new[] { ("markdown", markdown), ("html", html) })
            await Assert
                .That(document)
                .DoesNotContain("0.3625")
                .Because($"{name} must not publish a sub-floor residual as a number");
    }

    /// <summary>
    /// macOS cycle semantics — the value is kept, marked with the one <c>~</c> in
    /// all four formats, and explained in the three display formats; CSV carries the
    /// same contract in its <c># Schema:</c> line. The negative control keeps the
    /// flag honest: an exact counter is a bare count with no explanation anywhere.
    /// <para>
    /// The counts are taken <b>per table</b> (R-T4 · B1): a document-wide count of
    /// <c>~42</c> is satisfied by the results table alone, so it cannot prove the
    /// comparison table prints anything.
    /// </para>
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

        var (consoleResults, consoleResultsNotes) = ConsoleSectionTable(console, "Results");
        var (consoleComparisons, consoleComparisonNotes) = ConsoleSectionTable(console, "Comparisons");
        var (markdownResults, markdownResultsNotes) = MarkdownSectionTable(markdown, "Results");
        var (markdownComparisons, markdownComparisonNotes) = MarkdownSectionTable(markdown, "Comparisons");
        var (htmlResults, htmlResultsNotes) = HtmlSectionTable(html, "All Results");
        var (htmlComparisons, htmlComparisonNotes) = HtmlSectionTable(html, "Detailed Comparisons");

        var tables = new (string Name, ParsedTable Table, string Notes)[]
        {
            ("console results", consoleResults, consoleResultsNotes),
            ("console comparisons", consoleComparisons, consoleComparisonNotes),
            ("markdown results", markdownResults, markdownResultsNotes),
            ("markdown comparisons", markdownComparisons, markdownComparisonNotes),
            ("html results", htmlResults, htmlResultsNotes),
            ("html comparisons", htmlComparisons, htmlComparisonNotes),
        };

        // The value stays in every format (a monotonic-clock proxy is still a
        // duration), with the one marker, in each of the two tables.
        foreach (var (name, table, notes) in tables)
        {
            await AssertCycleCells(table, name, "~42");
            await AssertNoteOnce(notes, name, "monotonic clock proxy");
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

        var exactTables = new (string Name, ParsedTable Table, string Notes)[]
        {
            ("exact console results", ConsoleSectionTable(exactConsole, "Results").Table, ConsoleSectionTable(exactConsole, "Results").Notes),
            ("exact console comparisons", ConsoleSectionTable(exactConsole, "Comparisons").Table, ConsoleSectionTable(exactConsole, "Comparisons").Notes),
            ("exact markdown results", MarkdownSectionTable(exactMarkdown, "Results").Table, MarkdownSectionTable(exactMarkdown, "Results").Notes),
            ("exact markdown comparisons", MarkdownSectionTable(exactMarkdown, "Comparisons").Table, MarkdownSectionTable(exactMarkdown, "Comparisons").Notes),
            ("exact html results", HtmlSectionTable(exactHtml, "All Results").Table, HtmlSectionTable(exactHtml, "All Results").Notes),
            ("exact html comparisons", HtmlSectionTable(exactHtml, "Detailed Comparisons").Table, HtmlSectionTable(exactHtml, "Detailed Comparisons").Notes),
        };

        foreach (var (name, table, notes) in exactTables)
        {
            await AssertCycleCells(table, name, "42");
            await Assert
                .That(CountOf(notes, "monotonic clock proxy"))
                .IsEqualTo(0)
                .Because($"{name}: must not explain a marker it does not print");
        }

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

    /// <summary>
    /// R-T4 · B1 — the four per-operation states, one test case each, in <b>both</b>
    /// tables of all three display formats, cell by cell.
    /// <para>
    /// The <c>unmeasured</c> case is the one R-T4 found unprotected: before this test
    /// no fixture ever put an <c>N/A</c> run into a comparisons table, so the
    /// comparisons table's <c>N/A</c> rendering could be deleted outright with the
    /// whole suite still green.
    /// </para>
    /// </summary>
    [Test]
    [Arguments("sub-floor")]
    [Arguments("unmeasured")]
    [Arguments("proxy")]
    [Arguments("exact")]
    [Property("Category", "Batch0")]
    [Property("Batch0", "T4/state parity")]
    public async Task BothTables_MarkTheSamePerOperationState(string state)
    {
        var fixture = StateFixture.Of(state);
        var suite = Suite(
            BenchmarkResultFactory.Create("row-" + state, statistics: fixture.Statistics),
            ComparisonOf(fixture.Statistics, state + "-pair")
        );

        var console = new ConsoleFormatter().Format(suite);
        var markdown = new MarkdownFormatter().Format(suite);
        var html = new HtmlFormatter().Format(suite);
        Batch0Support.Log(console);
        Batch0Support.Log(markdown);
        Batch0Support.Log(html);

        // Console ─────────────────────────────────────────────────────
        var (consoleResults, consoleResultsNotes) = ConsoleSectionTable(console, "Results");
        var (consoleComparisons, consoleComparisonNotes) = ConsoleSectionTable(console, "Comparisons");
        await AssertPerOpCells(consoleResults, $"{state} · console results", fixture.Cells(Dialect.Console, false));
        await AssertPerOpCells(consoleComparisons, $"{state} · console comparisons", fixture.Cells(Dialect.Console, true));
        await AssertCycleCells(consoleResults, $"{state} · console results", fixture.CycleCell);
        await AssertCycleCells(consoleComparisons, $"{state} · console comparisons", fixture.CycleCell);
        await AssertFixtureNotes(fixture, $"{state} · console results", consoleResultsNotes);
        await AssertFixtureNotes(fixture, $"{state} · console comparisons", consoleComparisonNotes);

        // Markdown ────────────────────────────────────────────────────
        var (markdownResults, markdownResultsNotes) = MarkdownSectionTable(markdown, "Results");
        var (markdownComparisons, markdownComparisonNotes) = MarkdownSectionTable(markdown, "Comparisons");
        await AssertPerOpCells(markdownResults, $"{state} · markdown results", fixture.Cells(Dialect.Markdown, false));
        await AssertPerOpCells(markdownComparisons, $"{state} · markdown comparisons", fixture.Cells(Dialect.Markdown, true));
        await AssertCycleCells(markdownResults, $"{state} · markdown results", fixture.CycleCell);
        await AssertCycleCells(markdownComparisons, $"{state} · markdown comparisons", fixture.CycleCell);
        await AssertFixtureNotes(fixture, $"{state} · markdown results", markdownResultsNotes);
        await AssertFixtureNotes(fixture, $"{state} · markdown comparisons", markdownComparisonNotes);

        // HTML ────────────────────────────────────────────────────────
        var (htmlResults, htmlResultsNotes) = HtmlSectionTable(html, "All Results");
        var (htmlComparisons, htmlComparisonNotes) = HtmlSectionTable(html, "Detailed Comparisons");
        await AssertPerOpCells(htmlResults, $"{state} · html results", fixture.Cells(Dialect.HtmlResults, false));
        await AssertPerOpCells(htmlComparisons, $"{state} · html comparisons", fixture.Cells(Dialect.HtmlComparisons, true));
        await AssertCycleCells(htmlResults, $"{state} · html results", fixture.CycleCell);
        await AssertCycleCells(htmlComparisons, $"{state} · html comparisons", fixture.CycleCell);
        await AssertFixtureNotes(fixture, $"{state} · html results", htmlResultsNotes);
        await AssertFixtureNotes(fixture, $"{state} · html comparisons", htmlComparisonNotes);

        // CSV carries the same state in both of its sections.
        var csv = new CsvFormatter().Format(suite);
        await Assert.That(ReadCsvCell(csv, "results", "row-" + state, "Avg_ns")).IsEqualTo(fixture.CsvAvg);
        foreach (var cell in ReadCsvColumn(csv, "comparisons", "Avg_ns"))
            await Assert
                .That(cell)
                .IsEqualTo(fixture.CsvAvg)
                .Because($"csv comparisons must publish the same {state} state as csv results");
        await Assert.That(ReadCsvCell(csv, "results", "row-" + state, "CpuCycles")).IsEqualTo(fixture.CycleCell);
        foreach (var cell in ReadCsvColumn(csv, "comparisons", "CpuCycles"))
            await Assert.That(cell).IsEqualTo(fixture.CycleCell);
    }

    /// <summary>
    /// R-T4 · B2 — "the two tables name the one column the same way" is asserted on
    /// the <b>header row</b> of each table, never on the document, because the note
    /// under a table (<c>~ CPU Cycles: …</c>) begins with the very same words: a
    /// document-wide <c>Contains("CPU Cycles")</c> passes even when both headers say
    /// <c>CPU</c>.
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "T4/header parity")]
    public async Task BothTables_NameTheCycleColumnTheSameWay()
    {
        var approximate = CycleStatistics(approximate: true);
        var suite = Suite(
            BenchmarkResultFactory.Create("proxy-counter", statistics: approximate),
            ComparisonOf(approximate, "proxy-counter-pair")
        );

        var console = new ConsoleFormatter().Format(suite);
        var markdown = new MarkdownFormatter().Format(suite);
        var html = new HtmlFormatter().Format(suite);

        var (consoleResults, consoleResultsNotes) = ConsoleSectionTable(console, "Results");
        var (consoleComparisons, consoleComparisonNotes) = ConsoleSectionTable(console, "Comparisons");
        var (markdownResults, markdownResultsNotes) = MarkdownSectionTable(markdown, "Results");
        var (markdownComparisons, markdownComparisonNotes) = MarkdownSectionTable(markdown, "Comparisons");
        var (htmlResults, htmlResultsNotes) = HtmlSectionTable(html, "All Results");
        var (htmlComparisons, htmlComparisonNotes) = HtmlSectionTable(html, "Detailed Comparisons");

        await AssertCycleHeader(consoleResults, "console results", "CPU Cycles");
        await AssertCycleHeader(consoleComparisons, "console comparisons", "CPU Cycles");
        await AssertCycleHeader(markdownResults, "markdown results", "CPU Cycle");
        await AssertCycleHeader(markdownComparisons, "markdown comparisons", "CPU Cycle");
        await AssertCycleHeader(htmlResults, "html results", "CPU Cycles");
        await AssertCycleHeader(htmlComparisons, "html comparisons", "CPU Cycles");

        // The note is still printed under every table — and it is outside the header
        // row, so it cannot satisfy a header assertion (R-T4 · B2's exact failure).
        foreach (
            var (name, notes) in new[]
            {
                ("console results", consoleResultsNotes),
                ("console comparisons", consoleComparisonNotes),
                ("markdown results", markdownResultsNotes),
                ("markdown comparisons", markdownComparisonNotes),
                ("html results", htmlResultsNotes),
                ("html comparisons", htmlComparisonNotes),
            }
        )
            await Assert
                .That(notes)
                .Contains("monotonic clock proxy")
                .Because($"{name} still explains the `~` marker it prints");
    }

    /// <summary>
    /// R-T4 · B7 — the negative control the tests already hold the exact counter to,
    /// applied to the column switch: a formatter that prints no cycle column prints no
    /// <c>~</c>, so it must not explain a <c>~</c> either. Before this test the note was
    /// printed twice per document with <see cref="FormatterOptions.IncludeCpuCycles"/>
    /// off, once under each table — explaining a marker that was not on the page.
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "T4/negative control")]
    public async Task CycleNoteIsNotPrintedWhenTheCycleColumnIsNot()
    {
        var approximate = CycleStatistics(approximate: true);
        var suite = Suite(
            BenchmarkResultFactory.Create("proxy-counter", statistics: approximate),
            ComparisonOf(approximate, "proxy-counter-pair")
        );
        var compact = new FormatterOptions { IncludeCpuCycles = false };

        foreach (
            var (name, document) in new[]
            {
                ("console", new ConsoleFormatter(compact).Format(suite)),
                ("markdown", new MarkdownFormatter(compact).Format(suite)),
                ("html", new HtmlFormatter(compact).Format(suite)),
            }
        )
        {
            await Assert
                .That(document)
                .DoesNotContain("CPU Cycle")
                .Because($"{name}: the cycle column is switched off");
            await Assert
                .That(document)
                .DoesNotContain("~42")
                .Because($"{name}: must not print a marker for a column it does not print");
            await Assert
                .That(document)
                .DoesNotContain("monotonic clock proxy")
                .Because($"{name}: must not explain a marker it does not print");
        }

        // Positive control: the same fixture with the column on prints both, so the
        // assertions above cannot pass because the fixture stopped being approximate.
        var console = new ConsoleFormatter().Format(suite);
        await Assert.That(console).Contains("CPU Cycles");
        await Assert.That(console).Contains("monotonic clock proxy");
    }

    // ─── Per-state fixtures (R-T4 · B1) ────────────────────────────

    /// <summary>
    /// One per-operation state: the bare cell value of every per-operation column, the
    /// marker its tables print, and the cycle cell. The renderer below turns those
    /// into the dialect of each table, so one fixture covers all four formats.
    /// </summary>
    private sealed class StateFixture
    {
        private StateFixture(
            Statistics statistics,
            IReadOnlyDictionary<string, string> values,
            string? marker,
            string cycleCell,
            string csvAvg
        )
        {
            Statistics = statistics;
            Values = values;
            Marker = marker;
            CycleCell = cycleCell;
            CsvAvg = csvAvg;
        }

        public Statistics Statistics { get; }

        /// <summary>The bare per-operation cell values, keyed by column name (results table).</summary>
        public IReadOnlyDictionary<string, string> Values { get; }

        /// <summary>The word the table's own note must carry, or <c>null</c> for a plain number.</summary>
        public string? Marker { get; }

        /// <summary>The cycle cell in all four formats.</summary>
        public string CycleCell { get; }

        /// <summary>The <c>Avg_ns</c> cell in CSV — a different precision from the display formats.</summary>
        public string CsvAvg { get; }

        /// <summary>
        /// The exact cells one table must publish, in that table's dialect. The
        /// comparisons table has no <c>P95 (ns)</c> column, so its expectation has no
        /// <c>P95 (ns)</c> key either.
        /// </summary>
        public IReadOnlyDictionary<string, string> Cells(Dialect dialect, bool comparisonTable)
        {
            var cells = new Dictionary<string, string>();
            foreach (var (column, value) in Values)
            {
                if (comparisonTable && column == "P95 (ns)")
                    continue;

                cells[column] = Render(dialect, value);
            }

            return cells;
        }

        /// <summary>
        /// How one format decorates a per-operation value: Markdown appends the marker
        /// inside the cell, HTML's comparisons table wraps it in a note span, and the
        /// Console results table and HTML results table print the bare value (the
        /// results row is flagged elsewhere — HTML in its Name cell, Console in the
        /// note under the table).
        /// </summary>
        private string Render(Dialect dialect, string value)
        {
            // HTML escapes the bound (`<2.3` → `&lt;2.3`); nothing else rewrites it.
            var cell = dialect is Dialect.HtmlResults or Dialect.HtmlComparisons
                ? value.Replace("<", "&lt;")
                : value;

            if (Marker is not { } marker)
                return cell;

            return dialect switch
            {
                Dialect.Markdown => cell + " (" + marker + ")",
                Dialect.HtmlComparisons => cell + " <span class=\"note\">(" + marker + ")</span>",
                _ => cell,
            };
        }

        public static StateFixture Of(string state) =>
            state switch
            {
                "sub-floor" => new StateFixture(
                    SubFloorStatistics(),
                    Uniform("<2.3"),
                    marker: "below resolution",
                    cycleCell: "~42",
                    csvAvg: "<2.34"
                ),
                "unmeasured" => new StateFixture(
                    UnmeasuredStatistics(),
                    Uniform("N/A"),
                    marker: "unmeasured",
                    cycleCell: "~42",
                    csvAvg: "N/A"
                ),
                "proxy" => new StateFixture(
                    CycleStatistics(approximate: true),
                    Resolved(),
                    marker: null,
                    cycleCell: "~42",
                    csvAvg: "100"
                ),
                "exact" => new StateFixture(
                    CycleStatistics(approximate: false),
                    Resolved(),
                    marker: null,
                    cycleCell: "42",
                    csvAvg: "100"
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown per-operation state"),
            };
    }

    // ─── Assertions ────────────────────────────────────────────────

    /// <summary>
    /// One table publishes exactly the expected per-operation columns, in the expected
    /// order, and every one of their cells reads the expected text.
    /// </summary>
    private static async Task AssertPerOpCells(
        ParsedTable table,
        string where,
        IReadOnlyDictionary<string, string> expected
    )
    {
        var actualColumns = table.Header.Where(header => PerOpColumns.Contains(header)).ToList();
        await Assert
            .That(string.Join(" | ", actualColumns))
            .IsEqualTo(string.Join(" | ", expected.Keys))
            .Because($"{where}: the per-operation columns of the table");

        foreach (var (column, value) in expected)
        {
            var cells = table.ColumnCells(column).ToList();
            await Assert.That(cells.Count).IsGreaterThan(0).Because($"{where}: the {column} column has rows");
            foreach (var cell in cells)
                await Assert
                    .That(cell)
                    .IsEqualTo(value)
                    .Because($"{where}: the {column} cell must read `{value}`");
        }
    }

    /// <summary>Every cell of the cycle column of one table carries the expected marker set.</summary>
    private static async Task AssertCycleCells(ParsedTable table, string where, string expected)
    {
        var cells = table.CycleCells().ToList();
        await Assert.That(cells.Count).IsGreaterThan(0).Because($"{where}: the cycle column has rows");
        foreach (var cell in cells)
            await Assert.That(cell).IsEqualTo(expected).Because($"{where}: the cycle cell must read `{expected}`");
    }

    /// <summary>
    /// The cycle column is named exactly once in this table's <b>header row</b>, and
    /// named <paramref name="expected"/> — the assertion a document-wide
    /// <c>Contains</c> could not make (R-T4 · B2).
    /// </summary>
    private static async Task AssertCycleHeader(ParsedTable table, string where, string expected)
    {
        var exact = table.Header.Count(header => string.Equals(header, expected, StringComparison.Ordinal));
        await Assert.That(exact).IsEqualTo(1).Because($"{where}: exactly one header cell must read `{expected}`");

        // The other spelling must not sit in the same header row either.
        var other = expected == "CPU Cycles" ? "CPU" : "CPU Cycles";
        foreach (var header in table.Header)
            await Assert
                .That(header)
                .IsNotEqualTo(other)
                .Because($"{where}: the header row must not also carry `{other}`");
    }

    /// <summary>One note under one table says this exactly once.</summary>
    private static async Task AssertNoteOnce(string notes, string where, string phrase) =>
        await Assert
            .That(CountOf(notes, phrase))
            .IsEqualTo(1)
            .Because($"{where}: the note under this table must say `{phrase}` exactly once");

    /// <summary>
    /// A state's own note discipline: the marker is explained exactly once under each
    /// table that prints it, and a table that prints no marker explains none
    /// (negative control).
    /// </summary>
    private static async Task AssertFixtureNotes(StateFixture fixture, string where, string notes)
    {
        foreach (var marker in new[] { "below resolution", "unmeasured" })
        {
            var expected = fixture.Marker == marker ? 1 : 0;
            await Assert
                .That(CountOf(notes, marker))
                .IsEqualTo(expected)
                .Because(
                    expected == 1
                        ? $"{where}: must explain `{marker}` once"
                        : $"{where}: must not explain a marker it does not print"
                );
        }

        var expectedCyclesNote = fixture.CycleCell.StartsWith("~", StringComparison.Ordinal) ? 1 : 0;
        await Assert
            .That(CountOf(notes, "monotonic clock proxy"))
            .IsEqualTo(expectedCyclesNote)
            .Because($"{where}: the `~` sentence belongs only to a table that prints a `~`");
    }

    private static int CountOf(string haystack, string needle)
    {
        if (needle.Length == 0)
            return 0;

        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    // ─── Fixtures ──────────────────────────────────────────────────

    /// <summary>Every per-operation column reads the same thing (a bound, or <c>N/A</c>).</summary>
    private static IReadOnlyDictionary<string, string> Uniform(string value) =>
        PerOpColumns.ToDictionary(static column => column, _ => value);

    /// <summary>A resolved workload: <c>100.0</c> ns per operation and a <c>0.5</c> ns standard error.</summary>
    private static IReadOnlyDictionary<string, string> Resolved() =>
        PerOpColumns.ToDictionary(static column => column, static column => column == "StdErr (ns)" ? "0.5" : "100.0");

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

    /// <summary>
    /// The clock never advanced for the whole sample: every per-operation figure is
    /// <c>N/A</c>, and the cycle counter is still a proxy.
    /// </summary>
    private static Statistics UnmeasuredStatistics() =>
        new()
        {
            Avg = 100.0,
            P50 = 100.0,
            P90 = 100.0,
            P95 = 100.0,
            P99 = 100.0,
            Min = 100.0,
            Max = 100.0,
            StdDev = 0.0,
            StandardError = 0.0,
            RelativeStdDevPercent = 0.0,
            CpuCyclesPerOp = 42.0,
            CpuCyclesAreApproximate = true,
            UnmeasuredSampleCount = 3,
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

    // ─── Table extraction (R-T4 · B1/B2) ───────────────────────────

    /// <summary>One table of one document: its header row and its data rows.</summary>
    private sealed class ParsedTable
    {
        public ParsedTable(string[] header, List<string[]> rows)
        {
            Header = header;
            Rows = rows;
        }

        public string[] Header { get; }

        public List<string[]> Rows { get; }

        /// <summary>The column's index, or a hard failure — a missing column must never skip a loop.</summary>
        public int IndexOf(string column)
        {
            var index = Array.IndexOf(Header, column);
            if (index < 0)
                throw new InvalidOperationException(
                    $"the table has no '{column}' column; its header reads: {string.Join(" | ", Header)}"
                );

            return index;
        }

        public IEnumerable<string> ColumnCells(string column) => CellsAt(IndexOf(column));

        /// <summary>
        /// The cycle column, whichever of the two known spellings this format uses
        /// (<c>CPU Cycle</c> in Markdown, <c>CPU Cycles</c> elsewhere).
        /// </summary>
        public IEnumerable<string> CycleCells()
        {
            var spellings = Header.Where(static header => header is "CPU Cycle" or "CPU Cycles").ToList();
            if (spellings.Count != 1)
                throw new InvalidOperationException(
                    $"the table has {spellings.Count} cycle columns; its header reads: {string.Join(" | ", Header)}"
                );

            return CellsAt(IndexOf(spellings[0]));
        }

        private IEnumerable<string> CellsAt(int index)
        {
            foreach (var row in Rows)
            {
                if (index >= row.Length)
                    throw new InvalidOperationException(
                        $"a row has {row.Length} cells, the wanted column is at index {index}"
                    );

                yield return row[index];
            }
        }
    }

    private static string[] Lines(string document) => document.Replace("\r\n", "\n").Split('\n');

    /// <summary>The Console section named by its <c>═══ Heading ═══</c> rule.</summary>
    private static string ConsoleSection(string document, string heading)
    {
        var lines = Lines(document);
        var start = Array.FindIndex(lines, line => line.Trim() == $"═══ {heading} ═══");
        if (start < 0)
            throw new InvalidOperationException($"the console output has no '═══ {heading} ═══' section");

        var end = start + 1;
        while (
            end < lines.Length
            && !lines[end].TrimStart().StartsWith("═══ ", StringComparison.Ordinal)
            && !lines[end].TrimStart().StartsWith("Summary:", StringComparison.Ordinal)
        )
            end++;

        return string.Join("\n", lines, start, end - start);
    }

    /// <summary>
    /// The Console table of a section, plus the notes printed under it. Box-drawing
    /// rows are the table; everything else below the heading is a note.
    /// </summary>
    private static (ParsedTable Table, string Notes) ConsoleSectionTable(string document, string heading)
    {
        var lines = Lines(ConsoleSection(document, heading));
        var boxed = lines
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("│", StringComparison.Ordinal))
            .ToList();

        if (boxed.Count < 2)
            throw new InvalidOperationException($"the console '{heading}' section has no table rows");

        var table = new ParsedTable(SplitBoxRow(boxed[0]), boxed.Skip(1).Select(SplitBoxRow).ToList());

        var notes = lines
            .Select(static line => line.Trim())
            .Where(static line =>
                line.Length > 0
                && line[0] is not ('│' or '┌' or '├' or '└')
                && !line.StartsWith("═══ ", StringComparison.Ordinal)
            )
            .ToList();

        return (table, string.Join("\n", notes));
    }

    private static string[] SplitBoxRow(string line) =>
        line.Split('│').Skip(1).SkipLast(1).Select(static cell => cell.Trim()).ToArray();

    /// <summary>The Markdown section named by its <c>## Heading</c> heading.</summary>
    private static string MarkdownSection(string document, string heading)
    {
        var lines = Lines(document);
        var start = Array.FindIndex(lines, line => line.Trim() == "## " + heading);
        if (start < 0)
            throw new InvalidOperationException($"the markdown output has no '## {heading}' section");

        var end = start + 1;
        while (
            end < lines.Length
            && !lines[end].StartsWith("## ", StringComparison.Ordinal)
            && !lines[end].StartsWith("### ", StringComparison.Ordinal)
        )
            end++;

        return string.Join("\n", lines, start, end - start);
    }

    /// <summary>The Markdown table of a section, plus the <c>&gt;</c> notes under it.</summary>
    private static (ParsedTable Table, string Notes) MarkdownSectionTable(string document, string heading)
    {
        var lines = Lines(MarkdownSection(document, heading));
        var pipeRows = lines
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("|", StringComparison.Ordinal))
            .ToList();

        if (pipeRows.Count < 2)
            throw new InvalidOperationException($"the markdown '{heading}' section has no table rows");

        var table = new ParsedTable(
            SplitMarkdownRow(pipeRows[0]),
            pipeRows.Skip(1).Where(static row => !IsMarkdownSeparator(row)).Select(SplitMarkdownRow).ToList()
        );

        var notes = lines
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("> ", StringComparison.Ordinal))
            .ToList();

        return (table, string.Join("\n", notes));
    }

    private static string[] SplitMarkdownRow(string line) =>
        line.Trim().Trim('|').Split('|').Select(static cell => cell.Trim()).ToArray();

    private static bool IsMarkdownSeparator(string row) =>
        row.Contains('-')
        && row.Split('|').Skip(1).SkipLast(1).All(static cell =>
            cell.Trim().Length > 0 && cell.Trim().All(static c => c == '-' || c == ':')
        );

    /// <summary>The HTML section named by its <c>&lt;h2&gt;Heading&lt;/h2&gt;</c> heading.</summary>
    private static string HtmlSection(string document, string heading)
    {
        var marker = "<h2>" + heading + "</h2>";
        var start = document.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"the html output has no '<h2>{heading}</h2>' section");

        var end = new[]
        {
            document.IndexOf("<h2>", start + marker.Length, StringComparison.Ordinal),
            document.IndexOf("<footer", start + marker.Length, StringComparison.Ordinal),
        }
            .Where(static index => index >= 0)
            .DefaultIfEmpty(document.Length)
            .Min();

        return document.Substring(start, end - start);
    }

    /// <summary>
    /// The HTML table of a section, plus its <c>&lt;p class="note"&gt;</c> notes. Cells
    /// keep their inner markup: the note span <b>is</b> the marker there, so trimming it
    /// away would hide exactly what this file asserts.
    /// </summary>
    private static (ParsedTable Table, string Notes) HtmlSectionTable(string document, string heading)
    {
        var section = HtmlSection(document, heading);

        var head = Regex.Match(section, "<thead>(.*?)</thead>", RegexOptions.Singleline);
        if (!head.Success)
            throw new InvalidOperationException($"the html '{heading}' section has no <thead>");

        var header = Regex
            .Matches(head.Groups[1].Value, "<th[^>]*>(.*?)</th>", RegexOptions.Singleline)
            .Select(static match => match.Groups[1].Value.Trim())
            .ToArray();

        var body = Regex.Match(section, "<tbody>(.*?)</tbody>", RegexOptions.Singleline);
        if (!body.Success)
            throw new InvalidOperationException($"the html '{heading}' section has no <tbody>");

        var rows = Regex
            .Matches(body.Groups[1].Value, "<tr>(.*?)</tr>", RegexOptions.Singleline)
            .Select(static row =>
                Regex
                    .Matches(row.Groups[1].Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
                    .Select(static cell => cell.Groups[1].Value.Trim())
                    .ToArray()
            )
            .ToList();

        var notes = Regex
            .Matches(section, "<p class=\"note\">(.*?)</p>", RegexOptions.Singleline)
            .Select(static match => match.Groups[1].Value.Trim())
            .ToList();

        return (new ParsedTable(header, rows), string.Join("\n", notes));
    }

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
