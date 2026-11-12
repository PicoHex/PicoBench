namespace PicoBench.Tests.Batch0;

/// <summary>
/// V-FINAL2 · C1 / P3 — the two things a <b>reader</b> of the single-result view is
/// told: what the harness deducted, in the unit the label claims, and whether the
/// run could resolve the workload at all.
/// <para>
/// C1: <c>Statistics.OverheadNs</c> is the cost <b>one sample</b> carries (it is
/// subtracted from a whole sample before the residual is divided by the iteration
/// count), but the single-result view printed it as <c>ns/op</c> — a 47.8 ns/op
/// floor on a 1 000-iteration run read <c>47800.000 ns/op deducted</c> beside
/// <c>Avg: 2.7 µs</c>, i.e. 17.7× more than the operation itself. No test read the
/// unit, only the number, which is why the label survived the whole Batch 0 net.
/// </para>
/// <para>
/// P3: the <c>[below resolution]</c> / <c>[unmeasured]</c> marker
/// (<c>ConsoleFormatter.DescribeMeasurementLimit</c>) had no test at all — V-FINAL2
/// deleted the call and 11/11 stayed green. It is the only thing that tells a
/// reader of the single-result view that the number below it is a bound.
/// </para>
/// <para>
/// The fixtures are built by hand, so the assertion is about the <b>rendering
/// rule</b> and not about one machine's timing: <c>OverheadNs = 47 800</c> with
/// <c>IterationsPerSample = 1 000</c> must print <c>47.800</c>, never
/// <c>47800.000</c>.
/// </para>
/// </summary>
public class OverheadLabelTests
{
    /// <summary>The per-sample total the fixtures publish (a hand-picked figure).</summary>
    private const double PerSampleOverheadNs = 47_800.0;

    /// <summary>The iteration count that turns it into 47.8 ns/op.</summary>
    private const int IterationsPerSample = 1_000;

    /// <summary>The per-operation figure the same run must print.</summary>
    private const string PerOpOverheadText = "47.800";

    /// <summary>
    /// C1 — the single-result view prints the deducted floor <b>per operation</b>.
    /// <para>
    /// <c>47800.000 ns/op deducted</c> is what the view printed before this fix;
    /// it claims the harness took 47 800 ns out of every 2.7 µs call. The value is
    /// the total one sample carries, so the label is only true once it is divided by
    /// <see cref="BenchmarkResult.IterationsPerSample"/>.
    /// </para>
    /// <para>Expected under the C1 mutation (drop the division): <b>RED</b> — the
    /// view prints <c>47800.000</c> and calls it <c>ns/op</c>.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "V-FINAL2/C1")]
    public async Task SingleResultView_PrintsTheDeductedFloorPerOperation()
    {
        var result = Result(Stats(avg: 2_700.0, overheadNs: PerSampleOverheadNs));
        var console = new ConsoleFormatter().Format(result);

        Batch0Support.Log("console single-result view:\n" + console);

        await Assert
            .That(console)
            .Contains(PerOpOverheadText + " ns/op deducted")
            .Because(
                $"the view must print the {PerSampleOverheadNs} ns/sample floor as "
                    + $"{PerSampleOverheadNs / IterationsPerSample} ns/op"
            );

        // The per-sample total is not a per-operation figure and may not be printed
        // beside one: `47800.000 ns/op` is a claim that is 1 000× too large, and it
        // is larger than the 2 700 ns/op the same view reports as the measurement.
        await Assert
            .That(console.Contains("47800", StringComparison.Ordinal))
            .IsFalse()
            .Because("the per-sample total was printed into a per-operation figure:\n" + console);
        await Assert
            .That(console.Contains("47.800.000", StringComparison.Ordinal))
            .IsFalse()
            .Because("the per-operation floor was scaled twice:\n" + console);

        // Sanity: the printed floor is smaller than the operation the view reports
        // (2 700 ns/op), which is what a 47.8 ns/op deduction looks like and what a
        // 47 800 ns/op "deduction" could not be.
        var printed = double.Parse(PerOpOverheadText, CultureInfo.InvariantCulture);
        await Assert.That(printed).IsGreaterThan(0.0);
        await Assert.That(printed).IsLessThan(result.Statistics.Avg);
    }

    /// <summary>
    /// C1 — the same rule in the <b>below-resolution</b> branch, which prints the
    /// floor inside the resolution sentence rather than on its own line. It is the
    /// branch a sub-nanosecond workload reaches, i.e. exactly the case where the
    /// deduction dwarfs the measurement.
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "V-FINAL2/C1")]
    public async Task BelowResolutionView_PrintsTheDeductedFloorPerOperation()
    {
        var result = Result(
            Stats(
                avg: 0.2,
                overheadNs: PerSampleOverheadNs,
                resolutionLimited: true,
                resolutionLimitNanoseconds: 0.345
            )
        );
        var console = new ConsoleFormatter().Format(result);

        Batch0Support.Log("console single-result view (below resolution):\n" + console);

        await Assert
            .That(console)
            .Contains("harness overhead " + PerOpOverheadText + " ns/op deducted")
            .Because("the below-resolution sentence prints the same per-sample total");
        await Assert
            .That(console.Contains("47800", StringComparison.Ordinal))
            .IsFalse()
            .Because("the per-sample total leaked into the per-operation sentence");
    }

    /// <summary>
    /// C1 — <b>no format</b> may label the per-sample total as a per-operation
    /// figure. The three data formats (CSV / Markdown / HTML) and the summary helper
    /// do not print the floor at all, and the Console view prints it divided down;
    /// what none of them may contain is the bare per-sample total, which is only
    /// meaningful once the iteration count is applied.
    /// <para>
    /// A new formatter that prints <c>OverheadNs</c> as <c>ns/op</c> fails here
    /// instead of shipping the same 1 000× overstatement a second time.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "V-FINAL2/C1")]
    public async Task NoFormat_PrintsThePerSampleTotalAsAPerOperationFigure()
    {
        var result = Result(Stats(avg: 2_700.0, overheadNs: PerSampleOverheadNs));
        var suite = Suite(result);

        var outputs = new (string Format, string Text)[]
        {
            ("console/result", new ConsoleFormatter().Format(result)),
            ("console/suite", new ConsoleFormatter().Format(suite)),
            ("csv", new CsvFormatter().Format(result)),
            ("markdown", new MarkdownFormatter().Format(result)),
            ("html", new HtmlFormatter().Format(result)),
            (
                "summary",
                SummaryFormatter.Format(
                    [
                        new ComparisonResult(
                            "faster",
                            result,
                            Result(Stats(avg: 1_350.0), "Baseline_Op")
                        ),
                    ],
                    suite.Duration
                )
            ),
        };

        foreach (var (format, text) in outputs)
        {
            var mentionsFloor = text.Contains("47800", StringComparison.Ordinal);
            Batch0Support.Log(
                $"{format}: mentions the per-sample total = {mentionsFloor}, "
                    + $"mentions {PerOpOverheadText} ns/op = "
                    + $"{text.Contains(PerOpOverheadText, StringComparison.Ordinal)}"
            );

            await Assert
                .That(mentionsFloor)
                .IsFalse()
                .Because(
                    $"{format} printed the per-sample total {PerSampleOverheadNs} where a reader "
                        + "reads it as a per-operation figure"
                );
        }

        // …and the floor is not simply dropped from the user's sight: the one place
        // that prints it, prints it in the unit its label claims.
        await Assert
            .That(
                outputs[0]
                    .Text.Contains(PerOpOverheadText + " ns/op deducted", StringComparison.Ordinal)
            )
            .IsTrue();
    }

    /// <summary>
    /// C1 (semantics, both entry points) — <see cref="Statistics.OverheadNs"/> is
    /// published in <b>one</b> unit by both the synchronous and the asynchronous
    /// path: the cost of one sample.
    /// <para>
    /// The two paths disagreed before this fix — <c>CollectAndBuild</c> divided the
    /// measured total by the iteration count while <c>CollectAndBuildAsync</c> did
    /// not, so the one property meant 1.9 ns/op on one path and 26 200 ns/sample on
    /// the other (measured on this machine with the probe of
    /// <c>.review-2026-11/86-f-c1-report.md</c>). An empty delegate is the case where
    /// the identity is readable without trusting a formula: what one sample costs is
    /// (almost) the harness floor, so the published floor has to sit on the same
    /// order of magnitude as the raw sample total, not 1 000× below it.
    /// </para>
    /// <para>Expected under the pre-fix sync division: <b>RED</b> — <c>OverheadNs</c>
    /// is 2.2 while one empty sample costs millions of nanoseconds.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "V-FINAL2/C1")]
    [NotInParallel] // timing test: keep the core to ourselves
    public async Task EmptyDelegate_PublishesTheFloorInTheUnitOfOneSample()
    {
        var config = Batch0Support.UncalibratedConfig(sampleCount: 10, iterationsPerSample: 5_000);
        Action empty = static () => { };

        var result = Benchmark.Run("empty-lambda", empty, warmup: null, config);
        var statistics = result.Statistics;
        var rawPerSampleNs = result.Samples!.Average(static s => s.ElapsedNanoseconds);

        Batch0Support.Log(
            $"sync: overhead={statistics.OverheadNs:F1} ns/sample, one raw sample={rawPerSampleNs:F1} ns, "
                + $"per-op floor={statistics.OverheadNs / result.IterationsPerSample:F4} ns/op, "
                + $"avg={statistics.Avg:F4} ns/op"
        );

        await Assert.That(statistics.OverheadNs).IsGreaterThan(0.0);

        // Wide on purpose: the probe and the measured loop are two different windows,
        // so the ratio only has to be "the same quantity". A per-operation value
        // (2 ns against a 7 000 000 ns sample) misses this by nine orders of magnitude,
        // which is what makes the assertion discriminating rather than precise.
        var ratio = statistics.OverheadNs / Math.Max(rawPerSampleNs, 1.0);
        var because =
            $"OverheadNs = {statistics.OverheadNs:F1} against a raw sample of {rawPerSampleNs:F1} ns: "
            + "the property must publish the cost of one sample, not of one iteration";

        await Assert.That(ratio).IsGreaterThanOrEqualTo(0.02).Because(because);
        await Assert.That(ratio).IsLessThanOrEqualTo(50.0).Because(because);
    }

    /// <summary>
    /// P3 — the single-result view <b>marks</b> a run it could not resolve, next to
    /// the benchmark name.
    /// <para>
    /// V-FINAL2 deleted the <c>DescribeMeasurementLimit</c> call and 11/11 stayed
    /// green: the marker was rendered (a reader could see it) but never read (nobody
    /// asserted it). It is the only warning on this view that the numbers below it
    /// are bounds, so it is pinned here — for both states it can report, and for the
    /// negative control (a resolved run carries no marker at all).
    /// </para>
    /// <para>Expected under the P3 mutation (delete the call): <b>RED</b> — the
    /// benchmark line reads <c>Benchmark: name</c> for a below-resolution run.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "V-FINAL2/P3")]
    public async Task SingleResultView_MarksAnUnresolvableRunNextToItsName()
    {
        var belowResolution = Result(
            Stats(
                avg: 0.2,
                overheadNs: 1_200.0,
                resolutionLimited: true,
                resolutionLimitNanoseconds: 0.345
            ),
            name: "Below_Resolution_Op"
        );
        var unmeasured = Result(Stats(avg: 0.2, unmeasuredSampleCount: 2), name: "Unmeasured_Op");
        var plain = Result(Stats(avg: 2_700.0, overheadNs: PerSampleOverheadNs), name: "Plain_Op");

        var belowLine = NameLine(
            new ConsoleFormatter().Format(belowResolution),
            belowResolution.Name
        );
        var unmeasuredLine = NameLine(new ConsoleFormatter().Format(unmeasured), unmeasured.Name);
        var plainLine = NameLine(new ConsoleFormatter().Format(plain), plain.Name);

        Batch0Support.Log(
            $"below resolution: '{belowLine}'\nunmeasured: '{unmeasuredLine}'\nplain: '{plainLine}'"
        );

        await Assert
            .That(belowLine)
            .Contains("[below resolution]")
            .Because("the single-result view has to say that its numbers are a bound");
        await Assert
            .That(unmeasuredLine)
            .Contains("[unmeasured]")
            .Because("a run whose clock never advanced has no per-operation value at all");
        await Assert
            .That(plainLine)
            .DoesNotContain("[below resolution]")
            .Because("the marker may not appear on a run that resolved its workload");
        await Assert.That(plainLine).DoesNotContain("[unmeasured]");

        // The marker is the Console spelling of a state the other formats disclose in
        // their own words (`<limit` cells, `(below resolution)` suffixes). At least one
        // of the four user-visible formats must carry the disclosure, so the state can
        // never become invisible everywhere at once.
        var disclosures = new (string Format, bool Discloses)[]
        {
            ("console", belowLine.Contains("[below resolution]", StringComparison.Ordinal)),
            (
                "csv",
                Batch0Support
                    .ReadCsvCell(
                        new CsvFormatter().Format(belowResolution),
                        belowResolution.Name,
                        "Avg_ns"
                    )
                    .StartsWith("<", StringComparison.Ordinal)
            ),
            (
                "markdown",
                new MarkdownFormatter()
                    .Format(belowResolution)
                    .Contains("below resolution", StringComparison.Ordinal)
            ),
            (
                "html",
                new HtmlFormatter()
                    .Format(belowResolution)
                    .Contains("below resolution", StringComparison.Ordinal)
            ),
        };

        foreach (var (format, discloses) in disclosures)
            Batch0Support.Log($"{format} discloses the below-resolution state: {discloses}");

        await Assert
            .That(disclosures.Any(static d => d.Discloses))
            .IsTrue()
            .Because("no format disclosed that the run could not resolve its workload");
    }

    #region Fixtures

    /// <summary>The <c>Benchmark: …</c> line of a single-result view (its first line).</summary>
    private static string NameLine(string console, string name) =>
        console
            .Split('\n')
            .Select(static l => l.TrimEnd('\r'))
            .First(l => l.StartsWith("Benchmark:", StringComparison.Ordinal));

    /// <summary>
    /// A statistics fixture: no high-variance noise line (RSD 1%) and a cycle count,
    /// so the only lines that vary between fixtures are the ones under test.
    /// </summary>
    private static Statistics Stats(
        double avg,
        double overheadNs = 0.0,
        bool resolutionLimited = false,
        double resolutionLimitNanoseconds = 0.0,
        int unmeasuredSampleCount = 0
    ) =>
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
            StandardError = avg * 0.01,
            RelativeStdDevPercent = 1.0,
            CpuCyclesPerOp = 300.0,
            OverheadNs = overheadNs,
            ResolutionLimited = resolutionLimited,
            ResolutionLimitNanoseconds = resolutionLimitNanoseconds,
            UnmeasuredSampleCount = unmeasuredSampleCount,
        };

    /// <summary>A result carrying <see cref="IterationsPerSample"/> = 1 000.</summary>
    private static BenchmarkResult Result(Statistics statistics, string name = "Measured_Op") =>
        new(
            name: name,
            statistics: statistics,
            iterationsPerSample: IterationsPerSample,
            sampleCount: 10
        );

    /// <summary>One-result suite, for the formats that only render suites.</summary>
    private static BenchmarkSuite Suite(BenchmarkResult result) =>
        new(
            name: "OverheadLabelSuite",
            environment: new EnvironmentInfo(),
            results: [result],
            duration: TimeSpan.FromSeconds(1),
            description: null,
            comparisons: [],
            timestamp: new DateTime(2026, 11, 2, 13, 45, 0, DateTimeKind.Utc)
        );

    #endregion
}
