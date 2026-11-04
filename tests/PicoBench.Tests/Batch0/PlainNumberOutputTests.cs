namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · G6 (guard gap of 0.1) — a workload that <b>is</b> above the floor must
/// still be printed as a plain number in all four formats.
/// <para>
/// G5's matrix (<c>.review-2026-11/16-verification.md</c> §3.2, row <c>G</c>)
/// made <c>Statistics.ResolutionLimited</c> unconditionally true and the whole
/// suite stayed green, even though every per-operation cell then reads
/// <c>&lt;limit</c> / <c>[below resolution]</c> and the library becomes useless.
/// The Batch 0 net only pinned the "must disclose" direction, never the "must not
/// disclose" one; this test is that positive control.
/// </para>
/// </summary>
public class PlainNumberOutputTests
{
    /// <summary>
    /// <c>Thread.SpinWait(200)</c> costs microseconds per operation, orders of
    /// magnitude above the harness floor. Every per-operation cell of every format
    /// must therefore be a plain, parseable number without a <c>&lt;</c>, and the
    /// run must not be flagged as resolution-limited.
    /// <para>Expected under the <c>ResolutionLimited =&gt; true</c> mutation:
    /// <b>RED</b> — cells become <c>&lt;x</c>, the console row says
    /// <c>[below resolution]</c> and the flag is true for a 7 µs operation.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.1/positive control")]
    [NotInParallel] // timing test: keep the core to ourselves
    public async Task SpinWait_NormalLoad_PrintsPlainNumbers()
    {
        var config = Batch0Support.UncalibratedConfig(sampleCount: 10, iterationsPerSample: 200);
        Action work = static () => Thread.SpinWait(200);

        var result = Benchmark.Run("spinwait-normal-load", work, warmup: null, config);
        var statistics = result.Statistics;

        Batch0Support.Log(
            $"avg={statistics.Avg:F3} ns/op p50={statistics.P50:F3} "
                + $"limit={statistics.ResolutionLimitNanoseconds:F4} "
                + $"resolutionLimited={statistics.ResolutionLimited} "
                + $"unmeasured={statistics.Unmeasured}"
        );

        // The run resolved its workload: nothing here may be flagged.
        await Assert.That(statistics.ResolutionLimited).IsFalse();
        await Assert.That(statistics.Unmeasured).IsFalse();

        var console = new ConsoleFormatter().Format([result]);
        var markdown = new MarkdownFormatter().Format([result]);
        var html = new HtmlFormatter().Format([result]);
        var csv = new CsvFormatter().Format(result);

        var cells = new (string Format, string Cell)[]
        {
            ("console/Avg", ReadConsoleTableCell(console, result.Name, 1)),
            ("console/P50", ReadConsoleTableCell(console, result.Name, 2)),
            ("markdown/Avg", ReadPipeTableCell(markdown, result.Name, 2)),
            ("markdown/P50", ReadPipeTableCell(markdown, result.Name, 3)),
            ("html/Avg", ReadHtmlNumberCell(html, 0)),
            ("html/P50", ReadHtmlNumberCell(html, 1)),
            ("csv/Avg_ns", Batch0Support.ReadCsvCell(csv, result.Name, "Avg_ns")),
            ("csv/P50_ns", Batch0Support.ReadCsvCell(csv, result.Name, "P50_ns")),
        };

        foreach (var (format, cell) in cells)
        {
            Batch0Support.Log($"{format} = '{cell}'");

            await Assert
                .That(cell.Contains('<', StringComparison.Ordinal))
                .IsFalse()
                .Because($"{format} printed '{cell}' for a {statistics.Avg:F3} ns/op workload");
            await Assert
                .That(
                    double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                )
                .IsTrue()
                .Because($"{format} printed '{cell}', which is not a plain number");
            await Assert.That(value).IsGreaterThan(0.0);
        }

        // The console must not decorate a resolvable workload with a floor note.
        await Assert.That(console).DoesNotContain("below resolution");
        await Assert.That(console).DoesNotContain("unmeasured");
    }

    /// <summary>
    /// Reads cell <paramref name="index"/> (0 = name, 1 = Avg, 2 = P50) of the
    /// console table row that carries <paramref name="resultName"/>.
    /// </summary>
    private static string ReadConsoleTableCell(string console, string resultName, int index)
    {
        foreach (var line in console.Replace("\r\n", "\n").Split('\n'))
        {
            if (!line.Contains(resultName, StringComparison.Ordinal) || !line.Contains('│'))
                continue;

            var cells = line.Split('│');
            if (cells.Length > 1 + index)
                return cells[1 + index].Trim();
        }

        return $"<no console table row named {resultName}>";
    }

    /// <summary>
    /// Reads cell <paramref name="index"/> of the Markdown pipe-table row that
    /// carries <paramref name="resultName"/> (the row starts with an empty cell).
    /// </summary>
    private static string ReadPipeTableCell(string markdown, string resultName, int index)
    {
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (!line.StartsWith("|", StringComparison.Ordinal) || !line.Contains('|'))
                continue;
            if (!line.Contains(resultName, StringComparison.Ordinal))
                continue;

            var cells = line.Split('|');
            if (cells.Length > index)
                return cells[index].Trim();
        }

        return $"<no markdown table row named {resultName}>";
    }

    /// <summary>
    /// Reads the <paramref name="index"/>-th numeric cell (<c>&lt;td class="number"&gt;</c>)
    /// of the HTML results table; with a single result those are Avg, P50, ….
    /// </summary>
    private static string ReadHtmlNumberCell(string html, int index)
    {
        const string marker = "<td class=\"number\">";
        var start = -1;
        for (var occurrence = 0; occurrence <= index; occurrence++)
        {
            start = html.IndexOf(marker, start + 1, StringComparison.Ordinal);
            if (start < 0)
                return $"<no numeric cell #{index} in html>";
        }

        var valueStart = start + marker.Length;
        var valueEnd = html.IndexOf("</td>", valueStart, StringComparison.Ordinal);
        return valueEnd > valueStart ? html[valueStart..valueEnd] : $"<unterminated numeric cell #{index}>";
    }
}
