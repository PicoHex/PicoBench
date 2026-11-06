namespace PicoBench.Tests.Batch2;

using System.Globalization;
using PicoBench.Formatters;

/// <summary>
/// Batch 2 · 2.5 (B2-H4, B2-L4) — user text cannot rewrite the document it is
/// printed into.
/// <para>
/// A benchmark name, a suite description and a category are <b>data</b>. In
/// Markdown they used to be written straight into heading position
/// (<c>MarkdownFormatter.cs</c> <c># {suite.Name}</c> / <c>suite.Description</c>),
/// so <c>"S\r\n## Injected"</c> produced a real second-level heading, a
/// description containing <c>```</c> closed the fence around it, and nothing
/// stripped <c>\u001b</c> anywhere — an ANSI sequence in a name reached the
/// terminal unchanged. In the Console the same characters broke the box and the
/// table alignment (a <c>\r</c> swallowed the rest of the line, a CJK name was
/// padded by chars instead of display cells).
/// </para>
/// <para>
/// These tests attack all four surfaces with the same hostile strings and check
/// the output's <em>structure</em> (which lines are headings, how many pipes a row
/// has, whether the frame closes) rather than its wording.
/// </para>
/// </summary>
public class UserTextEscapingTests
{
    private const string Esc = "\u001b";
    private const string HostileName = "S\r\n## Injected heading";
    private const string HostileDescription = "line1\nline2 ```\n# fake heading";

    /// <summary>
    /// A name with a line break followed by <c>##</c> and a description containing a
    /// fence must not add a single heading or open/close a code fence.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.5/no heading from a name")]
    public async Task Markdown_CannotGrowAHeading_FromANameOrADescription()
    {
        var suite = new BenchmarkSuite(
            name: HostileName,
            environment: new EnvironmentInfo(),
            results: [Batch2Support.Result("Plain", Stats(150.0))],
            duration: TimeSpan.FromSeconds(1),
            description: HostileDescription,
            comparisons: [],
            timestamp: FixedTimestamp
        );

        // Minimal keeps the output to: H1, description, "## Results", the table.
        var markdown = new MarkdownFormatter(FormatterOptions.Minimal).Format(suite);
        var lines = Batch2Support.Lines(markdown);

        Batch2Support.Log(markdown);

        var headings = lines.Where(static l => l.StartsWith('#')).ToList();
        await Assert.That(headings.Count).IsEqualTo(2);
        await Assert.That(headings[0]).StartsWith("# ");
        await Assert.That(headings[1]).IsEqualTo("## Results");

        // Not one line of this document is a fence.
        await Assert
            .That(lines.Count(static l => l.TrimStart().StartsWith("```", StringComparison.Ordinal)))
            .IsEqualTo(0);

        // The hostile text is still there, inert: it is data, not structure.
        await Assert.That(markdown).Contains("Injected heading");
        await Assert.That(markdown).Contains("fake heading");
    }

    /// <summary>
    /// A pipe inside a table cell must not become a column, a backtick must not
    /// become code, and a line break must not become a row.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.5/table structure survives")]
    public async Task MarkdownTable_KeepsItsColumnCount_WithPipesBreaksAndFences()
    {
        var results = new List<BenchmarkResult>
        {
            Batch2Support.Result("pipe|inside", Stats(150.0)),
            Batch2Support.Result("break\r\nsecond line", Stats(160.0)),
            Batch2Support.Result("```fenced```", Stats(170.0)),
        };

        var markdown = new MarkdownFormatter().Format(results);
        var lines = Batch2Support.Lines(markdown);

        Batch2Support.Log(markdown);

        var tableLines = lines.Where(static l => l.StartsWith('|')).ToList();
        var separator = tableLines.First(static l => l.Contains("---"));
        var pipes = DelimiterCount(separator);

        foreach (var line in tableLines)
            await Assert
                .That(DelimiterCount(line))
                .IsEqualTo(pipes)
                .Because($"'{line}' does not have the separator's {pipes} cells");

        await Assert
            .That(lines.Count(static l => l.TrimStart().StartsWith("```", StringComparison.Ordinal)))
            .IsEqualTo(0);
    }

    /// <summary>
    /// An ANSI escape sequence in a name reaches neither the terminal nor the
    /// Markdown file: it is replaced by a visible control picture.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.5/no bare ESC")]
    public async Task EscapeSequences_AreReplaced_NotForwarded()
    {
        var hostile = "red" + Esc + "[31mRED" + Esc + "[0m" + "\u0007bell";
        var result = Batch2Support.Result(hostile, Stats(150.0));
        var suite = new BenchmarkSuite(
            name: "Ansi",
            environment: new EnvironmentInfo(),
            results: [result],
            duration: TimeSpan.FromSeconds(1),
            description: null,
            comparisons: [],
            timestamp: FixedTimestamp
        );

        var console = new ConsoleFormatter().Format(suite);
        var single = new ConsoleFormatter().Format(result);
        var markdown = new MarkdownFormatter().Format(suite);

        Batch2Support.Log(single);

        foreach (var (format, text) in new[]
        {
            ("console", console),
            ("console-single", single),
            ("markdown", markdown),
        })
        {
            await Assert
                .That(text.Contains(Esc, StringComparison.Ordinal))
                .IsFalse()
                .Because($"{format} forwarded a raw ESC (0x1B) to the output");
            await Assert.That(text).Contains("␛");
        }

        // The colour codes survive as inert text, and the bell is a picture too.
        await Assert.That(console).Contains("[31mRED");
        await Assert.That(console).Contains("␇");
    }

    /// <summary>
    /// CJK names are two cells wide, a name with a line break is one row: the frame
    /// has to close on every line all the same.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.5/console frame closes")]
    public async Task ConsoleTable_StaysAligned_WithCjkNamesAndLineBreaks()
    {
        var results = new List<BenchmarkResult>
        {
            Batch2Support.Result("测试基准A", Stats(150.0)),
            Batch2Support.Result("split\r\nname", Stats(160.0)),
            Batch2Support.Result("plain", Stats(170.0)),
        };

        var console = new ConsoleFormatter().Format(results);
        var lines = Batch2Support.Lines(console).Where(static l => l.Length > 0).ToList();

        foreach (var line in lines)
            Batch2Support.Log($"{DisplayWidth(line),3} | {line}");

        // top border, header, separator, three rows, bottom border
        await Assert.That(lines.Count).IsEqualTo(7);

        var width = DisplayWidth(lines[0]);
        foreach (var line in lines)
            await Assert
                .That(DisplayWidth(line))
                .IsEqualTo(width)
                .Because($"'{line}' does not line up with the frame ({DisplayWidth(line)} vs {width} cells)");

        // Same content, one line, marked but not lost.
        await Assert.That(console).Contains("split␍␊name");
        await Assert.That(console).Contains("测试基准A");
    }

    /// <summary>
    /// The single-result view and the summary box are user visible too: a category
    /// name with CJK text must not tilt the box.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.5/summary box closes")]
    public async Task SummaryBox_StaysAligned_WithCjkCategories()
    {
        var comparison = new ComparisonResult(
            "对比",
            Batch2Support.Result("baseline" + CRLF + "line", Stats(100.0)),
            Batch2Support.Result("candidate", Stats(50.0)),
            category: "性能类别"
        );

        // The box title is user text as well: a CJK title is two cells per char.
        var summary = SummaryFormatter.Format(
            [comparison],
            TimeSpan.FromSeconds(1),
            new SummaryOptions { Title = "性能摘要" }
        );
        var lines = Batch2Support.Lines(summary).Where(static l => l.Length > 0).ToList();

        foreach (var line in lines)
            Batch2Support.Log($"{DisplayWidth(line),3} | {line}");

        var boxLines = lines
            .Where(static l => l.StartsWith('║') || l.StartsWith('╔') || l.StartsWith('╚'))
            .ToList();
        await Assert.That(boxLines.Count).IsGreaterThan(2);

        var width = DisplayWidth(boxLines[0]);
        foreach (var line in boxLines)
            await Assert
                .That(DisplayWidth(line))
                .IsEqualTo(width)
                .Because($"'{line}' does not close the box ({DisplayWidth(line)} vs {width} cells)");

        await Assert.That(summary).Contains("性能类别");
        await Assert.That(summary).Contains("性能摘要");
    }

    /// <summary>
    /// The suite box frame is user text too: a CJK title is two cells per char and a
    /// description with line breaks is several framed rows — the frame has to close
    /// on every one of them.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.5/console box closes")]
    public async Task ConsoleSuiteBox_StaysAligned_WithCjkTitleAndMultilineDescription()
    {
        var suite = new BenchmarkSuite(
            name: "性能测试套件",
            environment: new EnvironmentInfo(),
            results: [],
            duration: TimeSpan.FromSeconds(1),
            description: "第一行" + LF + "second line" + CRLF + "第三条",
            comparisons: [],
            timestamp: FixedTimestamp
        );

        var console = new ConsoleFormatter().Format(suite);
        var boxLines = Batch2Support
            .Lines(console)
            .Where(static l => l.StartsWith('╔') || l.StartsWith('║') || l.StartsWith('╚'))
            .ToList();

        foreach (var line in boxLines)
            Batch2Support.Log($"{DisplayWidth(line),3} | {line}");

        // top border + title + three description lines + bottom border
        await Assert.That(boxLines.Count).IsEqualTo(6);

        var width = DisplayWidth(boxLines[0]);
        foreach (var line in boxLines)
            await Assert
                .That(DisplayWidth(line))
                .IsEqualTo(width)
                .Because($"'{line}' does not close the box ({DisplayWidth(line)} vs {width} cells)");
    }

    /// <summary>
    /// B2-L4: the group heading of <see cref="MarkdownFormatter.FormatGroupedComparisons"/>
    /// is user text in heading position — a category name must not inject a heading
    /// and must not break the table that follows.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.5/group heading escaped")]
    public async Task GroupHeadings_AreEscaped_AndCannotInjectAHeading()
    {
        var comparisons = new List<ComparisonResult>
        {
            new(
                "case",
                Batch2Support.Result("b", Stats(100.0)),
                Batch2Support.Result("c", Stats(50.0)),
                category: "cat|pipe"
            ),
            new(
                "case2",
                Batch2Support.Result("b2", Stats(100.0), category: "grp\n## injected"),
                Batch2Support.Result("c2", Stats(50.0), category: "grp\n## injected")
            ),
        };

        var markdown = MarkdownFormatter.FormatGroupedComparisons(
            comparisons,
            static c => c.Category ?? "Other",
            FormatterOptions.Minimal
        );
        var lines = Batch2Support.Lines(markdown);

        Batch2Support.Log(markdown);

        var headings = lines.Where(static l => l.StartsWith('#')).ToList();
        await Assert.That(headings.Count).IsEqualTo(2);
        foreach (var heading in headings)
        {
            await Assert.That(heading.StartsWith("### ", StringComparison.Ordinal)).IsTrue();
            await Assert.That(heading).DoesNotContain("## injected");
        }

        // The pipe in the category name is escaped inside the heading.
        await Assert.That(headings[0]).Contains("cat\\|pipe");
    }

    /// <summary>
    /// B-1 (fix/batch2): the single-comparison view names its two sides with
    /// <c>comparison.Baseline.Name</c> / <c>comparison.Candidate.Name</c>, both of
    /// which are caller-supplied text. Node names are just as reachable as suite
    /// names — <c>Benchmark.Compare(name, baselineName, candidateName)</c> is the
    /// documented way to build a comparison — so they need the same treatment:
    /// no raw ESC, no line break that swallows the rest of the line.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.5/comparison node names escaped")]
    public async Task ComparisonNodeNames_CannotForwardEsc_OrSwallowALine()
    {
        var comparison = new ComparisonResult(
            "hostile case",
            Batch2Support.Result("base" + ESC_TEXT + "[31mRED" + CRLF + "## Injected", Stats(100.0)),
            Batch2Support.Result("cand" + ESC_TEXT + "[0m", Stats(50.0))
        );

        var console = new ConsoleFormatter().Format(comparison);

        Batch2Support.Log(console);

        await Assert
            .That(console.Contains(RAW_ESC, StringComparison.Ordinal))
            .IsFalse()
            .Because("the comparison view forwarded a raw ESC (0x1B) in a node name");

        // Every figure the view prints sits on its own physical line: a CRLF in a
        // name must not make 'Baseline (...)' continue into '## Injected'.
        var baselineLine = Batch2Support
            .Lines(console)
            .Single(static l => l.StartsWith("  Baseline (", StringComparison.Ordinal));
        await Assert.That(baselineLine).Contains("␛[31mRED");
        await Assert.That(baselineLine).Contains("## Injected");
    }

    // ─── helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Counts the pipes that delimit cells: an escaped <c>\|</c> is data, not a
    /// column boundary, which is exactly what the escape is for.
    /// </summary>
    private static int DelimiterCount(string line)
    {
        var count = 0;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '|')
                continue;
            if (i > 0 && line[i - 1] == BACKSLASH)
                continue;
            count++;
        }
        return count;
    }

    private const char BACKSLASH = (char)92;

    /// <summary>
    /// A raw ESC (0x1B), spelled through <see cref="char"/> so no editor or source
    /// generator fuses it into a neighbouring digit. Deliberately a different
    /// constant from <see cref="Esc"/>: a node-name assertion that reused the same
    /// literal could pass by comparing the injected text with itself.
    /// </summary>
    private static readonly string RAW_ESC = new(new[] { (char)27 });

    /// <summary>The same character, written as an escape, for building fixtures.</summary>
    private const string ESC_TEXT = "\u001b";

    /// <summary>A CR LF pair, written without a literal escape so no editor can fuse it.</summary>
    private static readonly string CRLF = new string(new[] { (char)13, (char)10 });

    /// <summary>A bare line feed, written the same way.</summary>
    private static readonly string LF = new string(new[] { (char)10 });

    private static readonly DateTime FixedTimestamp = new(2026, 11, 2, 13, 45, 0, DateTimeKind.Utc);

    private static Statistics Stats(double avg) =>
        new()
        {
            Avg = avg,
            P50 = avg * 0.95,
            P90 = avg * 1.10,
            P95 = avg * 1.15,
            P99 = avg * 1.25,
            Min = avg * 0.8,
            Max = avg * 1.3,
            StdDev = avg * 0.1,
            StandardError = avg * 0.1 / Math.Sqrt(5),
            RelativeStdDevPercent = 3.0,
            CpuCyclesPerOp = 300.0,
            GcInfo = new GcInfo { Gen0 = 0, Gen1 = 0, Gen2 = 0 },
        };

    /// <summary>
    /// Batch 2 · F2: an emoji is two cells wide, and the frame has to close on a row
    /// that carries one. 🚀 (U+1F680) was outside the old table, so the box grew by a
    /// cell; a variation selector (U+FE0F) was counted as a cell of its own.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "F2/emoji display width")]
    public async Task ConsoleTable_StaysAligned_WithEmojiAndVariationSelectors()
    {
        var results = new List<BenchmarkResult>
        {
            Batch2Support.Result("rocket🚀_Op", Stats(150.0)),
            Batch2Support.Result("orange🟠_Op", Stats(160.0)),
            Batch2Support.Result("swan🩰_Op", Stats(170.0)),
            Batch2Support.Result("check✅_Op", Stats(180.0)),
            Batch2Support.Result("cross❌_Op", Stats(190.0)),
            Batch2Support.Result("bolt⚡_Op", Stats(200.0)),
            Batch2Support.Result("watch⌚_Op", Stats(210.0)),
            Batch2Support.Result("flag️_Op", Stats(220.0)),
            Batch2Support.Result("family👨‍👩‍👧_Op", Stats(230.0)),
            Batch2Support.Result("plain_Op", Stats(240.0)),
        };

        var console = new ConsoleFormatter().Format(results);
        var lines = Batch2Support.Lines(console).Where(static l => l.Length > 0).ToList();

        foreach (var line in lines)
            Batch2Support.Log($"{DisplayWidth(line),3} | {line}");

        var width = DisplayWidth(lines[0]);
        foreach (var line in lines)
            await Assert
                .That(DisplayWidth(line))
                .IsEqualTo(width)
                .Because($"'{line}' does not line up with the frame ({DisplayWidth(line)} vs {width} cells)");

        // The names really are in the output (the fixture is not silently empty).
        await Assert.That(console).Contains("rocket🚀_Op");
        await Assert.That(console).Contains("family👨‍👩‍👧_Op");
    }

    /// <summary>
    /// Batch 2 · F2: the display width of the individual characters the emoji test
    /// relies on, straight from the snapshot. This is the assertion the copied oracle
    /// could not make — it had the same blind spots as the implementation.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "F2/emoji display width")]
    public async Task Snapshot_AgreesWithTheKnownWidths()
    {
        // (code point, expected cells) — the cases review 40 measured.
        var cases = new (int Code, int Cells)[]
        {
            (0x1F680, 2), // 🚀 rocket
            (0x1F7E0, 2), // 🟠 orange circle
            (0x1FA70, 2), // 🩰 ballet shoes
            (0x2705, 2), // ✅ check mark
            (0x274C, 2), // ❌ cross mark
            (0x26A1, 2), // ⚡ high voltage
            (0x231A, 2), // ⌚ watch
            (0xFE0F, 0), // variation selector-16
            (0x0301, 0), // combining acute
            (0x1F1EF, 1), // regional indicator J (flags are two of them, one cell each)
            (0x4E2D, 2), // CJK 'middle'
            (0x41, 1), // 'A'
        };

        foreach (var (code, cells) in cases)
        {
            Batch2Support.Log($"U+{code:X4}: snapshot says {UnicodeWidth(code)}, expected {cells}");
            await Assert
                .That(UnicodeWidth(code))
                .IsEqualTo(cells)
                .Because($"the snapshot disagrees with the known width of U+{code:X4}");
        }
    }

    /// <summary>
    /// Display width of a line, from the Unicode data snapshot next to this file —

    /// <b>not</b> from a copy of the production range table. Batch 2 · F2: the first
    /// version of this helper was a verbatim copy of <c>DisplayWidth.IsWide</c>, so
    /// the two could only ever agree; it was structurally incapable of noticing that
    /// 🚀 (U+1F680) was padded as one cell. This one reads
    /// <c>UnicodeWidthSnapshot.txt</c>, which <c>tools/gen-unicode-width.py</c>
    /// generates from the UCD independently of the C# table.
    /// </summary>
    private static int DisplayWidth(string line)
    {
        var width = 0;
        foreach (var rune in line.EnumerateRunes())
            width += UnicodeWidth(rune.Value);
        return width;
    }

    /// <summary>Cells the snapshot assigns to one code point.</summary>
    private static int UnicodeWidth(int code)
    {
        foreach (var (cells, start, end) in UnicodeWidthRanges)
        {
            if (code >= start && code <= end)
                return cells;
        }

        return 1; // every code point the snapshot does not list is one cell
    }

    /// <summary>
    /// The snapshot, parsed once: <c>(cells, start, end)</c>, ignoring the
    /// <c>#</c> header.
    /// </summary>
    private static readonly (int Cells, int Start, int End)[] UnicodeWidthRanges =
        LoadUnicodeWidthRanges();

    private static (int Cells, int Start, int End)[] LoadUnicodeWidthRanges()
    {
        var root = Path.GetDirectoryName(
            PicoBench.Tests.Batch0.Batch0Support.FindRepositoryFile("PicoBench.slnx")
        )!;
        var path = Path.Combine(root, "tests", "PicoBench.Tests", "Batch2", "UnicodeWidthSnapshot.txt");

        return File
            .ReadAllLines(path)
            .Where(static line => line.Length > 0 && line[0] != '#')
            .Select(static line => line.Split(' '))
            .Select(static parts => (
                Cells: int.Parse(parts[0], CultureInfo.InvariantCulture),
                Start: Convert.ToInt32(parts[1], 16),
                End: Convert.ToInt32(parts[2], 16)
            ))
            .ToArray();
    }
}
