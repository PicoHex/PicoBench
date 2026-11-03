namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · 0.0 — "known cost" net, part 4: the output layer must distinguish
/// "not collected" from "zero" (acceptance criterion of Batch 0 · 0.5 / K5,
/// B2-H2).
/// </summary>
public class OutputFormatTests
{
    /// <summary>
    /// Batch 0 · 0.5 — when <see cref="Statistics.GcInfo"/> is <c>null</c>
    /// (which is exactly what <c>CpuOnly</c> produces) all four formatters must
    /// express "not collected".
    /// <para>Expected at HEAD: <b>RED</b> — Console/Markdown/HTML already print
    /// <c>N/A</c> via <c>FormatterBase.FormatGcInfo</c>, but
    /// <c>CsvFormatter</c> writes <c>0,0,0</c> (CsvFormatter.cs:180-182 and
    /// :271-273), so a CSV consumer cannot tell "no GC" from "GC not measured".</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.5")]
    public async Task Formatters_UnknownGc_IsNotZero()
    {
        var statistics = new Statistics
        {
            Avg = 100.0,
            P50 = 100.0,
            P90 = 100.0,
            P95 = 100.0,
            P99 = 100.0,
            Min = 90.0,
            Max = 110.0,
            StdDev = 5.0,
            StandardError = 1.0,
            RelativeStdDevPercent = 5.0,
            CpuCyclesPerOp = 300.0,
            GcInfo = null, // "not collected" — CpuOnly semantics
        };

        var result = new BenchmarkResult(
            name: "NoGc",
            statistics: statistics,
            iterationsPerSample: 1_000,
            sampleCount: 10,
            category: "Batch0"
        );
        var results = new[] { result };

        // ── Console / Markdown / HTML: an explicit marker ──────────────
        var console = new ConsoleFormatter().Format(results);
        var markdown = new MarkdownFormatter().Format(results);
        var html = new HtmlFormatter().Format(results);

        Batch0Support.Log("console GC cell: " + ExtractTableGcCell(console));
        Batch0Support.Log("markdown GC cell: " + ExtractTableGcCell(markdown));
        Batch0Support.Log("html GC cell: " + ExtractHtmlGcCell(html));

        await Assert.That(console).Contains("N/A");
        await Assert.That(markdown).Contains("N/A");
        await Assert.That(html).Contains("N/A");

        // ── CSV: the machine-readable one, where 0 is a real value ─────
        var csv = new CsvFormatter().Format(results);
        var lines = csv.Replace("\r\n", "\n")
            .Split('\n')
            .Where(static line => line.Length > 0)
            .ToArray();

        var headerIndex = Array.FindIndex(
            lines,
            static line => line.StartsWith("Name,", StringComparison.Ordinal)
        );
        await Assert.That(headerIndex).IsGreaterThanOrEqualTo(0);

        var header = lines[headerIndex].Split(',');
        var gcColumns = new[] { "GC0", "GC1", "GC2" };
        var positions = gcColumns.Select(column => Array.IndexOf(header, column)).ToArray();
        await Assert.That(positions.All(static position => position >= 0)).IsTrue();

        var dataRow = lines[headerIndex + 1].Split(',');
        var gcCells = positions
            .Select(position => position < dataRow.Length ? dataRow[position] : string.Empty)
            .ToArray();
        var notExpressed = gcCells.Where(static cell => !ExpressesNotCollected(cell)).ToArray();

        Batch0Support.Log(
            $"csv GC columns {string.Join("/", gcColumns)} = [{string.Join("|", gcCells)}]"
        );

        await Assert.That(notExpressed.Length).IsEqualTo(0);
    }

    /// <summary>Cells that legitimately mean "this value was not collected".</summary>
    private static bool ExpressesNotCollected(string cell) =>
        cell.Length == 0
        || cell.Equals("N/A", StringComparison.OrdinalIgnoreCase)
        || cell.Equals("NA", StringComparison.OrdinalIgnoreCase)
        || cell.Equals("null", StringComparison.OrdinalIgnoreCase)
        || cell == "-";

    private static string ExtractTableGcCell(string formatted)
    {
        foreach (var line in formatted.Replace("\r\n", "\n").Split('\n'))
        {
            if (!line.Contains("N/A", StringComparison.Ordinal))
                continue;

            var cells = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" | ", cells.Select(static c => c.Trim()));
        }

        return "<no row contains N/A>";
    }

    private static string ExtractHtmlGcCell(string html)
    {
        const string marker = "<td class=\"gc-info\">";
        var index = html.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return "<no gc-info cell>";

        var start = index + marker.Length;
        var end = html.IndexOf("</td>", start, StringComparison.Ordinal);
        return end > start ? html[start..end] : "<unterminated gc-info cell>";
    }
}
