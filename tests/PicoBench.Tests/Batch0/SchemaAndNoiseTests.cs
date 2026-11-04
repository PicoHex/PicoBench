namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · G6 — the two output contracts G4/G5 asked for: the CSV file must
/// describe itself, and a suite whose numbers are noise must say so in the main
/// table (0.5, third clause).
/// </summary>
public class SchemaAndNoiseTests
{
    /// <summary>
    /// G4 §7 — <c>Avg_ns</c> gained a third state (<c>&lt;limit</c>) and the CSV
    /// gained the <c>GCApproximate</c> column, so a consumer can no longer assume
    /// "a numeric column holds a number or N/A". The file therefore starts with a
    /// <c># Schema:</c> line carrying the column order, the allowed cell shapes, the
    /// meaning of <c>GCApproximate</c> and the schema version.
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.5/csv schema")]
    public async Task Csv_FirstLine_DeclaresTheSchema()
    {
        var suite = CreateSuite(noisy: false);
        var csv = new CsvFormatter().Format(suite);
        var lines = csv.Replace("\r\n", "\n").Split('\n');

        var schemaLine = lines.FirstOrDefault(static line =>
            line.StartsWith("# Schema:", StringComparison.Ordinal)
        );
        Batch0Support.Log("schema: " + (schemaLine ?? "<no # Schema: line>"));

        await Assert.That(schemaLine).IsNotNull();
        await Assert.That(schemaLine!.StartsWith("# Schema: v2;", StringComparison.Ordinal)).IsTrue();

        // The column order must be the one that is really written, otherwise the
        // contract would drift from the file it describes.
        var header = lines.First(static line => line.StartsWith("Name,", StringComparison.Ordinal));
        var expected = "; columns=" + string.Join("|", header.Split(','));
        await Assert.That(schemaLine).Contains(expected);

        // Cell shapes: a number, an upper bound, or "not measured".
        await Assert.That(schemaLine).Contains("per-op cells (Avg_ns,");
        await Assert.That(schemaLine).Contains("\"<limit\"");
        await Assert.That(schemaLine).Contains("harness floor");

        // The GC flag is a tri-state, and the meaning has to be written down.
        await Assert
            .That(schemaLine)
            .Contains(
                "GCApproximate = 1 (approximate counters) | 0 (exact) | \"N/A\" (not collected)"
            );

        // The schema line must be the first line of the file, not buried in it.
        await Assert.That(lines[0].StartsWith("# Schema:", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// Batch 0 · 0.5, third clause — <c>HasHighVariance</c> must be visible in the
    /// suite table of all four formats (it used to be printed only in the
    /// single-result Console view), and a quiet suite must not carry the marker.
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.5/noise marker")]
    public async Task NoiseMarker_IsShownForAHighVarianceSuite_InAllFourFormats()
    {
        var noisySuite = CreateSuite(noisy: true);
        var quietSuite = CreateSuite(noisy: false);

        var console = new ConsoleFormatter().Format(noisySuite);
        var markdown = new MarkdownFormatter().Format(noisySuite);
        var html = new HtmlFormatter().Format(noisySuite);
        var csv = new CsvFormatter().Format(noisySuite);

        Batch0Support.Log(
            "console noise note: "
                + (
                    console
                        .Replace("\r\n", "\n")
                        .Split('\n')
                        .FirstOrDefault(static line =>
                            line.Contains("noise", StringComparison.OrdinalIgnoreCase)
                        )
                    ?? "<none>"
                )
        );

        foreach (var (format, text) in new (string, string)[]
        {
            ("console", console),
            ("markdown", markdown),
            ("html", html),
            ("csv", csv),
        })
        {
            await Assert
                .That(text.Contains("noise", StringComparison.OrdinalIgnoreCase))
                .IsTrue()
                .Because($"{format} has no noise marker for a 12.3% RSD suite");
            await Assert.That(text).Contains("12.3");
            await Assert.That(text).Contains(NoisyName);
        }

        // Negative control: a suite whose spread is normal must stay clean, so the
        // marker cannot be satisfied by a constant string.
        var consoleQuiet = new ConsoleFormatter().Format(quietSuite);
        var markdownQuiet = new MarkdownFormatter().Format(quietSuite);
        var htmlQuiet = new HtmlFormatter().Format(quietSuite);
        var csvQuiet = new CsvFormatter().Format(quietSuite);

        foreach (var (format, text) in new (string, string)[]
        {
            ("console", consoleQuiet),
            ("markdown", markdownQuiet),
            ("html", htmlQuiet),
            ("csv", csvQuiet),
        })
        {
            await Assert
                .That(text.Contains("noise", StringComparison.OrdinalIgnoreCase))
                .IsFalse()
                .Because($"{format} flags a suite whose RSD is 1.0%");
        }
    }

    private const string NoisyName = "noisy-benchmark";
    private const string QuietName = "quiet-benchmark";

    private static BenchmarkSuite CreateSuite(bool noisy)
    {
        var quiet = new BenchmarkResult(
            name: QuietName,
            statistics: StatisticsFactory.Create(relativeStdDevPercent: 1.0),
            iterationsPerSample: 1_000,
            sampleCount: 10
        );

        var results = new List<BenchmarkResult> { quiet };
        if (noisy)
        {
            results.Add(
                new BenchmarkResult(
                    name: NoisyName,
                    statistics: StatisticsFactory.Create(
                        avg: 100.0,
                        stdDev: 12.3,
                        relativeStdDevPercent: 12.3
                    ),
                    iterationsPerSample: 1_000,
                    sampleCount: 10
                )
            );
        }

        return new BenchmarkSuite(
            name: "contract suite",
            environment: new EnvironmentInfo(),
            results: results,
            duration: TimeSpan.FromSeconds(1)
        );
    }
}
