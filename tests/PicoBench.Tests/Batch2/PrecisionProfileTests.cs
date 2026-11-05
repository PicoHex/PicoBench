namespace PicoBench.Tests.Batch2;

using System.Globalization;
using PicoBench.Formatters;
using PicoBench.Tests.Batch0;

/// <summary>
/// Batch 2 · 2.4 (B2-M5, B2-M6) — one precision profile for the four formats.
/// <para>
/// Two defects are pinned here. <b>M5</b>: the four formatters disagreed about
/// which metrics exist and which unit their columns carry — the Console suite
/// table had no <c>StdErr</c>/<c>RSD</c> at all, and only some comparison headers
/// said <c>(ns)</c>. <b>M6</b>: every figure was a fixed-point nanosecond number
/// with one decimal, so a 5-second benchmark printed <c>5000000000.0</c> in a
/// 14-character column.
/// </para>
/// <para>
/// The acceptance rule is "the same metric has the same <em>displayed</em> value
/// in every format, or the format explicitly marks it raw". These tests take the
/// first branch (Console/Markdown/HTML must agree character for character) and the
/// second (CSV cells are raw nanoseconds and its columns say <c>_ns</c>), and they
/// require the raw value to survive in the CSV so a scaled display can never
/// destroy the measurement.
/// </para>
/// </summary>
public class PrecisionProfileTests
{
    // ─── 1. four formats, one value ─────────────────────────────────

    /// <summary>
    /// The same six per-operation metrics of the same row must read identically in
    /// Console, Markdown and HTML, and the CSV cell must hold the raw nanoseconds
    /// that the displayed value is a scaled form of.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.4/four formats agree")]
    public async Task EveryDisplayFormat_ShowsTheSameValue_ForEveryMetric()
    {
        // 1.5 µs: above the ns display band, so a fixed-unit renderer and a scaled
        // one produce visibly different text — the case M6 is about.
        var result = Batch2Support.Result("Scaled_Op", Stats(1_500.0));
        var suite = SuiteWith([result]);

        var console = new ConsoleFormatter().Format(suite);
        var markdown = new MarkdownFormatter().Format(suite);
        var html = new HtmlFormatter().Format(suite);
        var csv = new CsvFormatter().Format(suite);

        var metrics = new (string Metric, string CsvColumn, int MarkdownIndex, int HtmlIndex)[]
        {
            ("Avg", "Avg_ns", 0, 0),
            ("P50", "P50_ns", 1, 1),
            ("P90", "P90_ns", 2, 2),
            ("P95", "P95_ns", 3, 3),
            ("P99", "P99_ns", 4, 4),
            ("StdErr", "StdErr_ns", 5, 5),
        };

        foreach (var (metric, csvColumn, markdownIndex, htmlIndex) in metrics)
        {
            var consoleCell = ConsoleCell(console, result.Name, metric);
            var markdownCell = MarkdownCell(markdown, result.Name, markdownIndex);
            var htmlCell = HtmlNumberCell(html, htmlIndex);
            var csvCell = Batch0Support.ReadCsvCell(csv, result.Name, csvColumn);

            Batch2Support.Log(
                $"{metric,-7} console='{consoleCell}' markdown='{markdownCell}' "
                    + $"html='{htmlCell}' csv='{csvCell}'"
            );

            await Assert.That(consoleCell).IsEqualTo(markdownCell);
            await Assert.That(htmlCell).IsEqualTo(consoleCell);

            // CSV is the raw tier: the same measurement, unscaled and invariant.
            var raw = RawCell(metric, 1_500.0);
            await Assert
                .That(Number(csvCell))
                .IsEqualTo(raw)
                .Because($"csv {csvColumn} = '{csvCell}' is not the raw {raw} ns");
        }
    }

    /// <summary>
    /// A scaled display must never be the only copy of a number: the CSV cell is
    /// the raw nanosecond value and its column name says <c>_ns</c> — that is the
    /// "explicitly marked raw" branch of the acceptance rule.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.4/raw is explicit")]
    public async Task Csv_KeepsRawNanoseconds_AndSaysSoInItsColumns()
    {
        var result = Batch2Support.Result("Raw_Op", Stats(1_500.0));
        var csv = new CsvFormatter().Format(SuiteWith([result]));

        await Assert.That(csv).Contains("Avg_ns,P50_ns");
        await Assert
            .That(Number(Batch0Support.ReadCsvCell(csv, result.Name, "Avg_ns")))
            .IsEqualTo(1_500.0);
        await Assert
            .That(Number(Batch0Support.ReadCsvCell(csv, result.Name, "P99_ns")))
            .IsEqualTo(1_875.0);
    }

    // ─── 2. a five-second benchmark is readable ─────────────────────

    /// <summary>
    /// M6's concrete complaint: a five-second-per-operation benchmark printed
    /// <c>5000000000.0</c> in a 14-character <c>Avg (ns)</c> column. The display
    /// profile scales the column to a readable unit and the header names it.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.4/5 second baseline readable")]
    public async Task FiveSecondBenchmark_IsReadable_AndTheHeaderNamesItsUnit()
    {
        var result = Batch2Support.Result("Slow_Op", Stats(5_000_000_000.0));
        var suite = SuiteWith([result]);

        var console = new ConsoleFormatter().Format(suite);
        var markdown = new MarkdownFormatter().Format(suite);
        var html = new HtmlFormatter().Format(suite);
        var csv = new CsvFormatter().Format(suite);

        Batch2Support.Log("console row: " + RowWith(console, result.Name));

        foreach (var (format, text) in new[]
        {
            ("console", console),
            ("markdown", markdown),
            ("html", html),
        })
        {
            await Assert
                .That(text.Contains("5000000000", StringComparison.Ordinal))
                .IsFalse()
                .Because($"{format} printed the raw ten-digit nanosecond figure");
            await Assert
                .That(text)
                .Contains("(s)")
                .Because($"{format} did not name the unit it scaled the column to");
        }

        await Assert.That(ConsoleCell(console, result.Name, "Avg")).IsEqualTo("5.0");
        await Assert.That(MarkdownCell(markdown, result.Name, 0)).IsEqualTo("5.0");
        await Assert.That(HtmlNumberCell(html, 0)).IsEqualTo("5.0");

        // The raw measurement is still in the machine-readable file.
        await Assert
            .That(Number(Batch0Support.ReadCsvCell(csv, result.Name, "Avg_ns")))
            .IsEqualTo(5_000_000_000.0);
    }

    /// <summary>
    /// Scaling is per table, not per cell: every per-operation column of one table
    /// carries the unit its header names, so figures stay comparable down a column
    /// and across the three display formats.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.4/one unit per table")]
    public async Task OneTable_UsesOneUnit_InEveryPerOperationColumn()
    {
        var results = new List<BenchmarkResult>
        {
            Batch2Support.Result("Fast_Op", Stats(150.0)),
            Batch2Support.Result("Slow_Op", Stats(2_500_000.0)), // 2.5 ms
        };
        var console = new ConsoleFormatter().Format(Results(results));

        var headers = HeaderRow(console);
        Batch2Support.Log(headers);

        foreach (var column in new[] { "Avg", "P50", "P90", "P95", "P99", "StdErr" })
            await Assert
                .That(headers)
                .Contains($"{column} (ms)")
                .Because("all per-operation columns of one table must share the table's unit");

        // 2 500 000 ns = 2.5 ms and 150 ns = 0.0 ms under the table's unit: one
        // unit, expressed honestly, instead of a per-cell rescale.
        await Assert.That(ConsoleCell(console, "Slow_Op", "Avg")).IsEqualTo("2.5");
        await Assert.That(ConsoleCell(console, "Fast_Op", "Avg")).IsEqualTo("0.0");
    }

    // ─── 3. M5: the metric set and the units now agree ──────────────

    /// <summary>
    /// M5: the Console suite table — the one every sample and the README recommend
    /// — had no <c>StdErr</c> and no <c>RSD</c> column at all, while Markdown and
    /// HTML had both.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.4/console carries RSD and StdErr")]
    public async Task ConsoleSuiteTable_CarriesStdErrAndRsd_LikeTheOtherFormats()
    {
        var noisy = new Statistics
        {
            Avg = 1_500.0,
            P50 = 1_400.0,
            P90 = 1_700.0,
            P95 = 1_800.0,
            P99 = 1_900.0,
            Min = 1_000.0,
            Max = 2_000.0,
            StdDev = 185.19,
            StandardError = 18.519,
            RelativeStdDevPercent = 12.3456,
            CpuCyclesPerOp = 1_234.4,
            GcInfo = new GcInfo { Gen0 = 1, Gen1 = 2, Gen2 = 3 },
        };
        var suite = SuiteWith([Batch2Support.Result("Noisy_Op", noisy)]);

        var console = new ConsoleFormatter().Format(suite);
        var markdown = new MarkdownFormatter().Format(suite);
        var html = new HtmlFormatter().Format(suite);

        Batch2Support.Log(HeaderRow(console));

        await Assert.That(HeaderRow(console)).Contains("StdErr (µs)");
        await Assert.That(HeaderRow(console)).Contains("RSD (%)");
        await Assert.That(markdown).Contains("StdErr (µs)");
        await Assert.That(html).Contains("StdErr (µs)");

        // One noise number, three formats: the same text in all of them.
        await Assert.That(ConsoleCell(console, "Noisy_Op", "RSD (%)")).IsEqualTo("12.3");
        await Assert.That(MarkdownCell(markdown, "Noisy_Op", 6)).IsEqualTo("12.3");
        await Assert.That(HtmlCell(html, "RSD (%)")).IsEqualTo("12.3");
        await Assert.That(ConsoleCell(console, "Noisy_Op", "StdErr")).IsEqualTo("0.0");
        await Assert.That(MarkdownCell(markdown, "Noisy_Op", 5)).IsEqualTo("0.0");
    }

    /// <summary>
    /// M5, second half: the Markdown and HTML comparison tables printed bare
    /// <c>P50 | P90 | P99 | StdErr</c> headers with no unit, so a reader could not
    /// tell nanoseconds from microseconds without reading the guide.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.4/comparison headers carry (ns)")]
    public async Task ComparisonTableHeaders_CarryTheirUnit_InEveryFormat()
    {
        var comparison = Batch2Support.Comparison(
            "Case_1",
            Stats(100.0, relativeStdDevPercent: 1.0),
            Stats(50.0, relativeStdDevPercent: 2.0)
        );
        var comparisons = new List<ComparisonResult> { comparison };

        var console = new ConsoleFormatter().Format(comparisons);
        var markdown = new MarkdownFormatter().Format(comparisons);
        var html = new HtmlFormatter().Format(comparisons);
        var csv = new CsvFormatter().Format(comparisons);

        foreach (var (format, text) in new[]
        {
            ("console", console),
            ("markdown", markdown),
            ("html", html),
        })
        {
            foreach (
                var column in new[]
                {
                    "Avg (ns)",
                    "P50 (ns)",
                    "P90 (ns)",
                    "P99 (ns)",
                    "StdErr (ns)",
                }
            )
                await Assert
                    .That(text)
                    .Contains(column)
                    .Because($"{format} has a unitless {column} column");
        }

        // CSV keeps the `_ns` suffix, which is the same statement in machine form.
        await Assert.That(csv).Contains("Avg_ns,Speedup,P50_ns");

        // And the Console comparison table now carries the same metrics as the
        // other two formats instead of only Avg/P50/P90/P99.
        await Assert.That(console).Contains("RSD (%)");
    }

    /// <summary>
    /// The display profile is a presentation choice, not a rewrite of the data: a
    /// caller can ask for raw nanoseconds in a display format and gets the very
    /// same value the CSV carries.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.4/raw profile is selectable")]
    public async Task RawProfile_RendersNanoseconds_InTheDisplayFormats()
    {
        var result = Batch2Support.Result("Slow_Op", Stats(5_000_000_000.0));
        var suite = SuiteWith([result]);
        var raw = new FormatterOptions { PrecisionProfile = MetricPrecision.Raw };

        var console = new ConsoleFormatter(raw).Format(suite);
        var markdown = new MarkdownFormatter(raw).Format(suite);

        Batch2Support.Log(HeaderRow(console));

        await Assert.That(HeaderRow(console)).Contains("Avg (ns)");
        await Assert.That(markdown).Contains("Avg (ns)");
        await Assert.That(Number(ConsoleCell(console, "Slow_Op", "Avg"))).IsEqualTo(5_000_000_000.0);
        await Assert.That(MarkdownCell(markdown, "Slow_Op", 0)).IsEqualTo("5000000000.0");
    }

    /// <summary>
    /// Scaling must not paper over the two states that are not numbers: a
    /// below-resolution cell keeps its <c>&lt;limit</c> and an unmeasured cell keeps
    /// its <c>N/A</c>, whatever unit the rest of the column uses.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.4/states survive scaling")]
    public async Task ScaledColumns_KeepTheLimitAndNotCollectedStates()
    {
        var suite = Batch2Support.RichSuite();
        var console = new ConsoleFormatter().Format(suite);
        var markdown = new MarkdownFormatter().Format(suite);

        foreach (var (format, text) in new[] { ("console", console), ("markdown", markdown) })
        {
            await Assert.That(text).Contains("<");
            await Assert.That(text).Contains("N/A");
            await Assert
                .That(text.Contains("<0.0 ", StringComparison.Ordinal))
                .IsFalse()
                .Because($"{format}: a scaled upper bound rounded to zero is not a bound any more");
        }
    }

    // ─── fixtures and readers ───────────────────────────────────────

    private static Statistics Stats(double avg, double relativeStdDevPercent = 5.0) =>
        new()
        {
            Avg = avg,
            P50 = avg * 0.95,
            P90 = avg * 1.10,
            P95 = avg * 1.15,
            P99 = avg * 1.25,
            Min = avg * 0.80,
            Max = avg * 1.30,
            StdDev = avg * 0.10,
            StandardError = avg * 0.10 / Math.Sqrt(5),
            RelativeStdDevPercent = relativeStdDevPercent,
            CpuCyclesPerOp = 300.0,
            GcInfo = new GcInfo { Gen0 = 1, Gen1 = 0, Gen2 = 0 },
        };

    /// <summary>The raw nanosecond value the fixture puts in one metric.</summary>
    private static double RawCell(string metric, double avg) =>
        metric switch
        {
            "Avg" => avg,
            "P50" => avg * 0.95,
            "P90" => avg * 1.10,
            "P95" => avg * 1.15,
            "P99" => avg * 1.25,
            "StdErr" => avg * 0.10 / Math.Sqrt(5),
            _ => throw new ArgumentOutOfRangeException(nameof(metric)),
        };

    private static BenchmarkSuite SuiteWith(IReadOnlyList<BenchmarkResult> results) =>
        new(
            name: "PrecisionSuite",
            environment: new EnvironmentInfo(),
            results: results,
            duration: TimeSpan.FromSeconds(2.5),
            description: "precision profile",
            comparisons: [],
            timestamp: new DateTime(2026, 11, 2, 13, 45, 0, DateTimeKind.Utc)
        );

    private static BenchmarkSuite Results(IReadOnlyList<BenchmarkResult> results) =>
        SuiteWith(results);

    private static string RowWith(string console, string name) =>
        console
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .FirstOrDefault(line =>
                line.Contains(name, StringComparison.Ordinal) && line.Contains('│')
            )
        ?? $"<no console row named {name}>";

    /// <summary>The console table header row (the first line carrying the Name column).</summary>
    private static string HeaderRow(string console) =>
        console
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .FirstOrDefault(line =>
                line.Contains("Name", StringComparison.Ordinal) && line.Contains('│')
            )
        ?? $"<no console header>{Environment.NewLine}{console}";

    /// <summary>
    /// Reads the console cell under the column whose header starts with
    /// <paramref name="column"/> (e.g. <c>Avg</c>, <c>RSD (%)</c>), located through
    /// the header row rather than by index, so inserting a column cannot silently
    /// make this reader compare the wrong cells.
    /// </summary>
    private static string ConsoleCell(string console, string resultName, string column)
    {
        var header = HeaderRow(console);
        var cells = header.Split('│');
        var index = -1;
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i].Trim().StartsWith(column, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return $"<no console column named {column} in '{header}'>";

        var row = RowWith(console, resultName);
        var values = row.Split('│');
        return values.Length > index ? values[index].Trim() : $"<no cell {index} in '{row}'>";
    }

    /// <summary>
    /// Reads a value cell of a Markdown pipe row by its position among the values:
    /// 0 is <c>Avg</c>, 1 is <c>P50</c>, 5 is <c>StdErr</c>, 6 is <c>RSD (%)</c>.
    /// </summary>
    private static string MarkdownCell(string markdown, string name, int index)
    {
        foreach (var line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (!line.StartsWith('|') || !line.Contains(name, StringComparison.Ordinal))
                continue;

            var cells = line.Split('|');
            var target = index + 2; // cells[0] is the empty cell before the first pipe
            if (cells.Length > target)
                return cells[target].Trim();
        }

        return $"<no markdown row named {name}>";
    }

    /// <summary>Reads the <paramref name="index"/>-th <c>class="number"</c> cell of an HTML table.</summary>
    private static string HtmlNumberCell(string html, int index)
    {
        var cells = HtmlNumberCells(html);
        return cells.Count > index ? cells[index] : $"<no html number cell {index}>";
    }

    private static List<string> HtmlNumberCells(string html)
    {
        var cells = new List<string>();
        var rest = html;
        while (true)
        {
            var open = rest.IndexOf("<td class=\"number\">", StringComparison.Ordinal);
            if (open < 0)
                return cells;
            var start = open + "<td class=\"number\">".Length;
            var end = rest.IndexOf("</td>", start, StringComparison.Ordinal);
            if (end < 0)
                return cells;
            cells.Add(rest[start..end].Trim());
            rest = rest[(end + "</td>".Length)..];
        }
    }

    /// <summary>Reads the HTML number cell that sits under the header <paramref name="header"/>.</summary>
    private static string HtmlCell(string html, string header)
    {
        var headers = new List<string>();
        var rest = html;
        while (true)
        {
            var open = rest.IndexOf("<th", StringComparison.Ordinal);
            if (open < 0)
                break;
            var start = rest.IndexOf('>', open) + 1;
            var end = rest.IndexOf("</th>", start, StringComparison.Ordinal);
            if (end < 0)
                break;
            headers.Add(rest[start..end].Trim());
            rest = rest[(end + "</th>".Length)..];
        }

        var index = headers.FindIndex(h => h.StartsWith(header, StringComparison.Ordinal));
        return index <= 0 ? $"<no html column named {header}>" : HtmlNumberCell(html, index - 1);
    }

    /// <summary>Parses a numeric cell written by any of the four formats.</summary>
    private static double Number(string cell) =>
        double.Parse(cell, NumberStyles.Float, CultureInfo.InvariantCulture);
}
