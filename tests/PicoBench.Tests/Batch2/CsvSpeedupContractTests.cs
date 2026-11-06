namespace PicoBench.Tests.Batch2;

using System.Globalization;
using PicoBench.Tests.Batch0;

/// <summary>
/// B-3 (fix/batch2) — the <c>Speedup</c> clause of the CSV contract, and the ten
/// READMEs that repeat it, must describe the implementation.
/// <para>
/// The clause that shipped with Batch 2 read
/// <c>Speedup = ratio (baseline avg / candidate avg) | "N/A" (no finite ratio: 0/0 or too few paired rounds)</c>
/// and was wrong twice over (review 39, §3):
/// </para>
/// <list type="number">
/// <item>A 0/0 pair is <b>not</b> what produces <c>N/A</c>. A comparison built from
/// two independent results goes through <c>ComparisonResult.ComputeSpeedup</c>,
/// which returns <c>1.0</c> when both averages are zero and <c>+∞</c> when only the
/// candidate's is — and the <c>∞</c> is rendered as <c>N/A</c>, so the real cause
/// of the missing number was never written down.</item>
/// <item>"baseline avg / candidate avg" only describes a comparison built from two
/// independent results. <c>Benchmark.Compare</c> reports the <b>median of the
/// per-round paired ratios</b> (Batch 0), which is a different quantity.</item>
/// </list>
/// <para>
/// These tests pin the clause to the implementation (the review's finding was a
/// document that had drifted) and assert that all ten localisations of the block
/// quote it byte for byte, so a future edit cannot fix one language and miss nine.
/// </para>
/// </summary>
public class CsvSpeedupContractTests
{
    /// <summary>
    /// The repository root, anchored on the solution file. Not
    /// <c>FindRepositoryFile("README.md")</c>: <c>tests/</c> has a README.md of its
    /// own with the same language navigation bar, and walking up from the test
    /// binary finds that one first.
    /// </summary>
    private static string RepositoryRoot =>
        Path.GetDirectoryName(Batch0Support.FindRepositoryFile("PicoBench.slnx"))!;

    /// <summary>Every language the README ships in, relative to the repository root.</summary>
    private static readonly string[] ReadmeFiles =
    [
        "README.md",
        "README.zh.md",
        "README.zh-tw.md",
        "README.ja.md",
        "README.ko.md",
        "README.de.md",
        "README.es.md",
        "README.fr.md",
        "README.pt.md",
        "README.ru.md",
    ];

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

    /// <summary>The exact <c>Speedup</c> clause the implementation must emit.</summary>
    private const string ExpectedClause =
        "; Speedup = ratio (baseline avg / candidate avg, or the median of the per-round "
        + "paired ratios for a direct comparison) | \"N/A\" (no finite ratio: a candidate "
        + "average of zero is unbounded, a non-finite candidate average is unmeasurable, or "
        + "too few usable paired rounds)";

    /// <summary>
    /// The demonstration the review asked for, run rather than quoted: a 0/0
    /// independent pair is <c>1.0</c> (not <c>N/A</c>), and it is the
    /// zero-candidate case that goes non-finite. Both are stated in the clause the
    /// next test pins, so the clause cannot be written to contradict the code.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.3/csv contract")]
    public async Task IndependentComparison_0Over0IsOne_AndZeroCandidateIsInfinite_NotNan()
    {
        // Two independent results: the two-argument shape (measured ratio: null).
        var bothZero = new ComparisonResult("both-zero", Result("b", 0.0), Result("c", 0.0));
        // Candidate average NaN is the unmeasurable case; the ratio comes out NaN.
        var unmeasurable = new ComparisonResult(
            "unmeasurable",
            Result("b", 100.0),
            Result("c", double.NaN)
        );
        // Candidate average zero with a non-zero baseline is the infinite case.
        var unbounded = new ComparisonResult("unbounded", Result("b", 100.0), Result("c", 0.0));

        Batch2Support.Log(
            string.Join(
                " | ",
                $"0/0={Describe(bothZero.Speedup)}",
                $"candidate≈0={Describe(unbounded.Speedup)}",
                $"candidate=NaN={Describe(unmeasurable.Speedup)}"
            )
        );

        await Assert
            .That(bothZero.Speedup)
            .IsEqualTo(1.0)
            .Because("a 0/0 independent pair is 1.0, so it is NOT the cause of an N/A Speedup cell");
        await Assert.That(double.IsPositiveInfinity(unbounded.Speedup)).IsTrue();
        await Assert.That(double.IsNaN(unmeasurable.Speedup)).IsTrue();
    }

    /// <summary>
    /// The emitted clause is exactly the string the READMEs quote, and it carries
    /// the two corrections: the paired-median definition and the real N/A causes.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.3/csv contract")]
    public async Task CsvSchema_QuotesTheSpeedupClauseVerbatim()
    {
        var comparison = Batch2Support.Comparison("Faster_Case", Stats(100.0), Stats(50.0));
        var csv = new CsvFormatter().Format([comparison]);

        var schema = Batch2Support
            .Lines(csv)
            .First(line => line.StartsWith("# Schema:", StringComparison.Ordinal));

        Batch2Support.Log(schema);

        await Assert
            .That(schema)
            .Contains(ExpectedClause)
            .Because("the CSV contract line must state the ratio definition and the N/A causes the code implements");

        // The two claims the old clause got wrong must not come back.
        await Assert.That(schema).DoesNotContain("(no finite ratio: 0/0");

        // The same correction has to be in the consumer-facing release note.
        var note = File.ReadAllText(Path.Combine(RepositoryRoot, "docs/release-notes/batch2.md"));
        await Assert
            .That(note)
            .Contains("A **0/0 pair is not one of them**")
            .Because("the release note repeated the wrong cause (review 39, B-3)");
    }

    /// <summary>
    /// All ten READMEs repeat the block verbatim; every one of them has to carry the
    /// same clause (review 39 found the error copied into all ten).
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.3/csv contract")]
    public async Task EveryReadme_QuotesTheSameSpeedupClause()
    {
        var missing = new List<string>();

        foreach (var file in ReadmeFiles)
        {
            var path = Path.Combine(RepositoryRoot, file);
            var text = File.ReadAllText(path);

            Batch2Support.Log(
                $"{file}: clause present = {text.Contains(ExpectedClause, StringComparison.Ordinal)}"
            );

            if (!text.Contains(ExpectedClause, StringComparison.Ordinal))
                missing.Add(file);
        }

        await Assert.That(missing).IsEmpty().Because("these READMEs do not quote the new clause: " + string.Join(", ", missing));
    }

    private static BenchmarkResult Result(string name, double avg) =>
        new(
            name: name,
            statistics: Stats(avg),
            iterationsPerSample: 1000,
            sampleCount: 20,
            category: "CategoryA",
            timestamp: Batch2Support.FixedTimestamp
        );

    private static string Describe(double value) =>
        double.IsNaN(value) ? "NaN"
        : double.IsPositiveInfinity(value) ? "+Inf"
        : value.ToString("G", CultureInfo.InvariantCulture);
}
