namespace PicoBench.Tests.Batch2;

/// <summary>
/// Batch 2 · F1 (fix/batch2): the HTML format has to neutralise control characters
/// like every other format does.
/// <para>
/// <c>HtmlFormatter.Escape</c> entity-escaped <c>&amp;</c> <c>&lt;</c>
/// <c>&gt;</c> <c>"</c> <c>'</c> and stopped there, so a name or a description
/// carrying a C0 control — an ESC (0x1B) from an ANSI sequence, a lone CR — wrote
/// that byte into the document. Console and Markdown have shown a control picture
/// for it since 2.5 (and the CSV keeps it deliberately: it is a data file). HTML
/// is the one that must not, because the file is meant to be opened.
/// </para>
/// </summary>
public class HtmlSanitizationTests
{
    /// <summary>A raw ESC (0x1B), built from a char so no editor can fuse it.</summary>
    private static readonly string RawEsc = new(new[] { (char)27 });

    /// <summary>A CR LF pair, written the same way.</summary>
    private static readonly string CRLF = new(new[] { (char)13, (char)10 });

    private static Statistics Stats(double avg) =>
        new()
        {
            Avg = avg,
            P50 = avg,
            P90 = avg,
            P95 = avg,
            P99 = avg,
            Min = avg,
            Max = avg,
            StdDev = 1.0,
            StandardError = 1.0,
            RelativeStdDevPercent = 1.0,
            CpuCyclesPerOp = 300.0,
            GcInfo = GcInfoFactory.Create(),
        };

    /// <summary>
    /// The four places the formatter writes user text by hand — the document
    /// <c>&lt;title&gt;</c>, the <c>&lt;h1&gt;</c>, the description paragraph and the
    /// results table — must all carry a control picture instead of the control.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "F1/html control characters")]
    public async Task Html_NameAndDescription_DoNotCarryRawControlCharacters()
    {
        var hostileName = "hostile" + RawEsc + "[31mRED S\r\n## Injected";
        var hostileDescription = "line1" + RawEsc + "[0m and <b>html</b>\r\nsecond";

        var suite = new BenchmarkSuite(
            name: hostileName,
            environment: new EnvironmentInfo(),
            results: [Batch2Support.Result(hostileName, Stats(150.0))],
            duration: TimeSpan.FromSeconds(1),
            description: hostileDescription,
            comparisons: [Batch2Support.Comparison("cmp" + RawEsc + "[33mcase", Stats(100.0), Stats(50.0))],
            timestamp: Batch2Support.FixedTimestamp
        );

        var html = new HtmlFormatter().Format(suite);

        Batch2Support.Log(string.Join("\n", Batch2Support.Lines(html).Where(static l => l.Contains("hostile") || l.Contains("description"))));

        await Assert
            .That(html.Contains(RawEsc, StringComparison.Ordinal))
            .IsFalse()
            .Because("the HTML document forwarded a raw ESC (0x1B)");

        // The character is shown, not dropped: the same control picture the other
        // two human-readable formats use.
        var title = Batch2Support
            .Lines(html)
            .Single(static l => l.StartsWith("<title>", StringComparison.Ordinal));
        await Assert.That(title).Contains("␛[31mRED");
        await Assert.That(title).DoesNotContain("\n");

        var h1 = Batch2Support
            .Lines(html)
            .Single(static l => l.StartsWith("<h1>", StringComparison.Ordinal));
        await Assert.That(h1).Contains("␛[31mRED");

        var description = Batch2Support
            .Lines(html)
            .Single(static l => l.Contains("class=\"description\"", StringComparison.Ordinal));
        await Assert.That(description).Contains("␛[0m");
        await Assert.That(description).Contains("&lt;b&gt;html&lt;/b&gt;");
    }

    /// <summary>
    /// Entity escaping still does its job — the sanitizer must not trade structural
    /// safety for control-character safety.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "F1/html control characters")]
    public async Task Html_StillEscapesEntities_AndStillBlocksMarkup()
    {
        var suite = new BenchmarkSuite(
            name: "<script>alert('x')</script> & \"quoted\"",
            environment: new EnvironmentInfo(),
            results: [Batch2Support.Result("<img src=x onerror=y>", Stats(150.0))],
            duration: TimeSpan.FromSeconds(1),
            description: "</p><script>bad()</script>",
            comparisons: [],
            timestamp: Batch2Support.FixedTimestamp
        );

        var html = new HtmlFormatter().Format(suite);

        await Assert.That(html).DoesNotContain("<script>alert");
        await Assert.That(html).DoesNotContain("<script>bad()");
        await Assert.That(html).DoesNotContain("<img src=x");
        await Assert.That(html).Contains("&lt;script&gt;");
        await Assert.That(html).Contains("&amp;");
        await Assert.That(html).Contains("&quot;");
    }

    /// <summary>
    /// The release note has to say the control rule applies to HTML: it used to claim
    /// "HTML is unchanged", which was only true of the markup.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "F1/html control characters")]
    public async Task ReleaseNote_NoLongerClaimsHtmlIsCompletelyUnchanged()
    {
        var root = Path.GetDirectoryName(
            PicoBench.Tests.Batch0.Batch0Support.FindRepositoryFile("PicoBench.slnx")
        )!;
        var note = File.ReadAllText(Path.Combine(root, "docs/release-notes/batch2.md"));

        await Assert.That(note).DoesNotContain("HTML is unchanged: HTML escaping already prevented");
        await Assert.That(note).Contains("the control-character rule now applies here too");
    }

    /// <summary>
    /// The same character reads the same way in Console, Markdown and HTML: the
    /// point of the finding is the disagreement, not the byte on its own.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "F1/html control characters")]
    public async Task Html_AgreesWithConsoleAndMarkdown_AboutControlCharacters()
    {
        var name = "op" + RawEsc + "[1mBOLD";
        var suite = new BenchmarkSuite(
            name: name,
            environment: new EnvironmentInfo(),
            results: [Batch2Support.Result(name, Stats(150.0))],
            duration: TimeSpan.FromSeconds(1),
            description: null,
            comparisons: [],
            timestamp: Batch2Support.FixedTimestamp
        );

        var html = new HtmlFormatter().Format(suite);
        var console = new ConsoleFormatter().Format(suite);
        var markdown = new MarkdownFormatter().Format(suite);

        foreach (var (format, text) in new[] { ("html", html), ("console", console), ("markdown", markdown) })
        {
            await Assert
                .That(text.Contains(RawEsc, StringComparison.Ordinal))
                .IsFalse()
                .Because($"{format} kept the raw ESC that the other formats replace");
            await Assert.That(text).Contains("␛[1mBOLD");
        }
    }
}
