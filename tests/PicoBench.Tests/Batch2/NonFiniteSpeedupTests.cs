namespace PicoBench.Tests.Batch2;

/// <summary>
/// Batch 2 · 2.2 (B2-M2) — a non-finite speedup must not reach the output as a
/// pseudo token, and it must not swallow the aggregate of every other scenario.
/// </summary>
/// <remarks>
/// Two reachable inputs: <c>candidate.Avg == 0</c> makes
/// <see cref="ComparisonResult.Speedup"/> <c>+∞</c>
/// (<c>Models.cs</c> · <c>ComputeSpeedup</c>), and <c>candidate.Avg == NaN</c>
/// makes it <c>NaN</c>. Both used to travel to the formatter: the two helpers
/// printed <c>Infinityx</c> (because <c>"F2"</c> on infinity is the word
/// "Infinity") while the interpolated sites printed <c>∞x</c> / <c>NaN</c> — and
/// <c>Average(c =&gt; c.Speedup)</c> turned the whole Summary into ∞.
/// </remarks>
public class NonFiniteSpeedupTests
{
    private static IReadOnlyList<string> NonFiniteTokens =>
        ["Infinity", "NaN", "∞", "-∞", "ليس رقم"];

    /// <summary>Candidate cost 0 ⇒ the ratio is <c>+∞</c>.</summary>
    private static ComparisonResult Unbounded() =>
        Batch2Support.Comparison("Unbounded", Stats(100.0), Stats(0.0));

    /// <summary>Candidate average NaN ⇒ the ratio is <c>NaN</c>.</summary>
    private static ComparisonResult Undefined() =>
        Batch2Support.Comparison("Undefined", Stats(100.0), Stats(double.NaN));

    /// <summary>A perfectly ordinary 2.00x scenario.</summary>
    private static ComparisonResult Ordinary() =>
        Batch2Support.Comparison("Ordinary", Stats(100.0), Stats(50.0));

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

    private static Dictionary<string, string> Render(params ComparisonResult[] comparisons)
    {
        var list = comparisons.ToList();
        var suite = Batch2Support.SuiteWithComparisons(list);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["console"] = new ConsoleFormatter().Format(list),
            ["csv"] = new CsvFormatter().Format(list),
            ["markdown-suite"] = new MarkdownFormatter().Format(suite),
            ["html-suite"] = new HtmlFormatter().Format(suite),
            ["summary"] = SummaryFormatter.Format(list),
            ["console-one"] = new ConsoleFormatter().Format(list[0]),
            ["markdown-one"] = new MarkdownFormatter().Format(list[0]),
            ["html-one"] = new HtmlFormatter().Format(list[0]),
            ["csv-one"] = new CsvFormatter().Format(list[0]),
        };
    }

    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.2/non-finite speedup")]
    public async Task UnboundedAndUndefinedSpeedups_NeverPrintANonFiniteToken()
    {
        var rendered = Render(Unbounded(), Undefined(), Ordinary());

        foreach (var (format, text) in rendered)
        {
            foreach (var token in NonFiniteTokens)
            {
                await Assert
                    .That(text.Contains(token, StringComparison.Ordinal))
                    .IsFalse()
                    .Because($"{format} printed the non-finite token '{token}'");
            }
        }
    }

    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.2/non-finite speedup")]
    public async Task UnboundedSpeedup_IsReportedAsNotComputable_NotAsInfinity()
    {
        var consoleOne = new ConsoleFormatter().Format(Unbounded());
        var summary = SummaryFormatter.Format([Unbounded()]);
        var markdown = new MarkdownFormatter().Format([Unbounded()]);
        var csv = new CsvFormatter().Format([Unbounded()]);

        Batch2Support.Log("console single: " + string.Join(" | ", Batch2Support.Lines(consoleOne)));
        Batch2Support.Log("summary: " + string.Join(" | ", Batch2Support.Lines(summary)));
        Batch2Support.Log("csv: " + Batch2Support.Describe(Batch2Support.Lines(csv)));

        // The cell says "no ratio could be computed"; the verdict next to it says why.
        await Assert.That(consoleOne).Contains("Speedup: N/A");
        await Assert.That(consoleOne).Contains("unknown (not measurable)");
        await Assert.That(summary).Contains("Average speedup: N/A");
        await Assert.That(summary).Contains("Maximum speedup: N/A");
        await Assert.That(markdown).Contains("N/A");
        await Assert.That(csv).Contains("N/A");
    }

    /// <summary>
    /// The heart of B2-M2: one ∞ scenario used to turn "Average speedup" into ∞
    /// (and one NaN scenario into NaN), hiding the numbers of every other
    /// scenario. The aggregate must ignore the non-finite ratios and say how many
    /// it had to ignore.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.2/non-finite speedup")]
    public async Task OneUnboundedScenario_DoesNotPoisonTheAverage()
    {
        var comparisons = new List<ComparisonResult> { Ordinary(), Unbounded() };
        var suite = Batch2Support.SuiteWithComparisons(comparisons);

        var summary = SummaryFormatter.Format(comparisons);
        var console = new ConsoleFormatter().Format(comparisons);
        var markdown = new MarkdownFormatter().Format(suite);
        var html = new HtmlFormatter().Format(suite);

        Batch2Support.Log("summary:\n" + summary);
        Batch2Support.Log("console table tail: " + Line(console, "Summary:"));
        Batch2Support.Log("markdown tail: " + Line(markdown, "Average speedup"));

        // 2.00x is the only computable ratio, so every aggregate reports it.
        await Assert.That(summary).Contains("Average speedup: 2.00x");
        await Assert.That(summary).Contains("Maximum speedup: 2.00x");
        await Assert.That(console).Contains("Avg: 2.00x");
        await Assert.That(console).Contains("Max: 2.00x");
        await Assert.That(markdown).Contains("Average speedup: 2.00x");
        await Assert.That(markdown).Contains("Maximum speedup: 2.00x");
        await Assert.That(html).Contains("2.00x");

        // ... and the excluded scenario is disclosed, not silently dropped.
        await Assert.That(summary).Contains("1 scenario not computable");
        await Assert.That(console).Contains("1 scenario not computable");
        await Assert.That(markdown).Contains("1 scenario not computable");
        await Assert.That(html).Contains("1 scenario not computable");

        // A quiet suite must not carry the note (negative control).
        var quietSummary = SummaryFormatter.Format([Ordinary()]);
        await Assert.That(quietSummary).DoesNotContain("not computable");
        var quietConsole = new ConsoleFormatter().Format([Ordinary()]);
        await Assert.That(quietConsole).DoesNotContain("not computable");
    }

    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.2/non-finite speedup")]
    public async Task SpeedupIndicator_IsNotAttachedToANonFiniteRatio()
    {
        var markdown = new MarkdownFormatter().Format([Unbounded()]);
        var console = new ConsoleFormatter().Format([Unbounded()]);

        await Assert.That(markdown).DoesNotContain("**N/A** ***");
        await Assert.That(markdown).DoesNotContain("**N/A** (!)");
        await Assert.That(console).DoesNotContain("Speedup: N/A ***");
        await Assert.That(console).DoesNotContain("Speedup: N/A (!)");
    }

    /// <summary>
    /// B-2 (fix/batch2): the detailed rows of the summary box appended a literal
    /// <c>x</c> to <c>FormatterBase.FormatMetric</c>'s output, so a non-finite
    /// ratio read <c>N/Ax</c> — the exact token shape 2.2 set out to remove. The
    /// release note promises a non-finite ratio is rendered as <c>N/A</c> "in the
    /// comparison summary" too, so a per-scenario row has to use the same helper
    /// as the aggregate lines above it.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.2/non-finite speedup")]
    public async Task SummaryDetailRows_NeverPrintTheTokenNax()
    {
        var summary = SummaryFormatter.Format([Unbounded(), Undefined(), Ordinary()]);

        Batch2Support.Log(summary);

        await Assert.That(summary).DoesNotContain("N/Ax");

        // One detailed row per scenario; the non-finite ones carry the bare N/A
        // the aggregate lines already use.
        var unbounded = Batch2Support
            .Lines(summary)
            .Single(static l => l.TrimStart().StartsWith("Unbounded", StringComparison.Ordinal));
        await Assert.That(unbounded).Contains(": N/A ");
        await Assert.That(unbounded).DoesNotContain("x ");
    }

    private static string Line(string text, string prefix) =>
        Batch2Support
            .Lines(text)
            .FirstOrDefault(line => line.TrimStart().StartsWith(prefix, StringComparison.Ordinal))
        ?? "<none>";
}
