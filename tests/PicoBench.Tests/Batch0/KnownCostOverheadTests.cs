namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · 0.0 — the "known cost" regression net, part 1: measurement overhead and
/// per-op normalisation (acceptance criteria of Batch 0 · 0.1).
/// <para>
/// These tests are the executable form of the C2 findings behind B1-C2: an empty delegate
/// used to report several ns/op that were never disclosed or deducted, and the same lambda
/// reported a different per-op cost at 100 vs 10000 iterations. 0.1 fixed that by probing
/// an empty delegate, subtracting the probe per <b>sample</b> and publishing the residual
/// together with the resolution limit it can back up.
/// </para>
/// <para>
/// <b>FIX-FLAKY-2 rewrote both timing thresholds.</b> Both used to be absolute wall-clock
/// constants, and the same binary then read 0.00 ns/op on an idle 16-CPU box and
/// 1.900 ns/op on a 2-vCPU hosted runner (CI run 44285730921, win-x64) — the constant was
/// measuring the runner, not the library, which is why the pair went red at random on
/// three different runs (44007511777, 43213497484, 44285730921) without a code change.
/// The thresholds are now <b>self-consistent</b>:
/// <list type="bullet">
/// <item>an exact arithmetic identity (the same closed form on every machine) proves that
/// the floor was measured <b>and</b> deducted — that is the defect detector;</item>
/// <item>what is left of the wall-clock expectation is derived from the floor <b>the same
/// run measured</b> (<see cref="Statistics.OverheadNs"/> and
/// <see cref="Statistics.ResolutionLimitNanoseconds"/>), never from a nanosecond
/// constant, with the factor taken from the measured noise distribution
/// (<see cref="Batch0Support.ResidualNoiseFactor"/>).</item>
/// </list>
/// See <c>.review-2026-11/106-fix-flaky2.md</c> for the measured distributions on idle,
/// 2-CPU and loaded machines, for the mutants each leg kills, and for the argument why
/// this is a correct criterion rather than a relaxed one.
/// </para>
/// </summary>
public class KnownCostOverheadTests
{
    /// <summary>
    /// Batch 0 · 0.1 — an empty lambda is the absolute resolution floor: the run must
    /// <b>measure</b> that floor, <b>deduct</b> it, and <b>disclose</b> what is left instead
    /// of printing a plain number it cannot back up.
    /// <para>
    /// Four legs, none of them an absolute nanosecond constant:
    /// <list type="number">
    /// <item><b>a floor was measured</b> — <see cref="Statistics.OverheadNs"/> is &gt; 0 on
    /// every trial. A run that measures nothing publishes 0, which is exactly the 0.1
    /// defect (this leg is deterministic: no clock is involved);</item>
    /// <item><b>the floor was deducted</b> — <see cref="Batch0Support.RecomputedPerOpAvg"/>
    /// re-derives <see cref="Statistics.Avg"/> from the retained samples minus the
    /// <b>published</b> <see cref="Statistics.OverheadNs"/>. The identity is exact, so a run
    /// that announces a floor and forgets to subtract it (or subtracts another amount) is
    /// red on every machine, every time;</item>
    /// <item><b>the disclosure contract</b> — <c>ResolutionLimited ⇔ Avg ≤ limit</c>, and a
    /// limited row prints its <c>&lt;limit</c> bound, which must cover the floor that same
    /// run deducted (<see cref="Statistics.ResolutionLimitNanoseconds"/> ≥ floor per op); a
    /// plain number below the run's own limit is the one thing that is forbidden;</item>
    /// <item><b>a gross-inflation net</b> — when the run <b>does</b> resolve the empty lambda
    /// above its own limit (what the win-x64 CI runner honestly did: 1.900 ns/op), the
    /// residual may not exceed <see cref="Batch0Support.ResidualNoiseFactor"/> × the floor
    /// that same run measured. This leg is deliberately loose: it catches the 5.5-15.4 ns/op
    /// an <b>unsubtracted</b> empty delegate reports, not the last picosecond of noise.</item>
    /// </list>
    /// </para>
    /// <para>
    /// What the previous version asserted, and why it cannot stay: «median empty ≤ 1.5 ns/op,
    /// or a disclosure». The number 1.5 is ≈4 σ of the reference machine's floor but ≈0.8 σ of
    /// a 2-vCPU runner's, so on that runner a correct library fails the leg and no formatter
    /// discloses anything (the run resolved 1.900 ns/op above a lower limit, which is
    /// self-consistent). Legs 1-3 above hold on every machine measured — idle 16-CPU,
    /// 2 CPUs, 2 CPUs + 4 competing busy loops — and they fail every mutant the absolute
    /// constant failed (see the report).
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.1")]
    [NotInParallel] // timing test: keep the core to ourselves
    public async Task Overhead_EmptyLambda_IsBelowResolutionOrSubtracted()
    {
        var config = Batch0Support.UncalibratedConfig();
        Action empty = static () => { };

        const int trials = 5;
        var runs = new List<BenchmarkResult>(trials);

        for (var trial = 0; trial < trials; trial++)
        {
            var run = Benchmark.Run("empty-lambda", empty, warmup: null, config);
            runs.Add(run);

            Batch0Support.Log(
                $"trial {trial}: empty={run.Statistics.Avg:F3}±{run.Statistics.StandardError:F4} ns/op "
                    + $"min={run.Statistics.Min:F3} "
                    + $"floor/op={run.Statistics.OverheadNs / run.IterationsPerSample:F4} ns "
                    + $"limit={run.Statistics.ResolutionLimitNanoseconds:F3} "
                    + $"limited={run.Statistics.ResolutionLimited}"
            );
        }

        // The assertions are made on the LEAST INTERFERED trial, not on a median.
        //
        // Interference can only inflate a timing measurement, never deflate it, so the
        // quietest of five trials is the premise with the least ambient state — the same
        // selection AsyncOverheadTests documents for its disclosure contract. The medians
        // stay in the log for the report; they are not asserted on, because a median that
        // carries one interrupted trial is a machine artefact, not a library property.
        var typical = runs.MinBy(static r => r.Statistics.Avg)!;
        var statistics = typical.Statistics;
        var floorPerOp = statistics.OverheadNs / typical.IterationsPerSample;
        var runFloor = Math.Max(floorPerOp, statistics.ResolutionLimitNanoseconds);
        var cell = Batch0Support.ReadCsvCell(
            new CsvFormatter().Format(typical),
            typical.Name,
            "Avg_ns"
        );

        Batch0Support.Log(
            $"typical trial: avg={statistics.Avg:F3} ns/op floor/op={floorPerOp:F4} ns "
                + $"limit={statistics.ResolutionLimitNanoseconds:F3} ns "
                + $"limited={statistics.ResolutionLimited} csv Avg_ns='{cell}'"
        );

        // ── (1) a floor was measured: no clock, no threshold, no noise ──────
        var withoutFloor = runs.FindAll(static r => !(r.Statistics.OverheadNs > 0));
        await Assert
            .That(withoutFloor.Count)
            .IsEqualTo(0)
            .Because(
                $"{withoutFloor.Count} of {trials} trials reported OverheadNs = 0 "
                    + $"(typical: {statistics.OverheadNs:F0} ns/sample) — an empty delegate must be "
                    + "measured and deducted, not published as the cost of the measured action"
            );

        // ── (2) the floor was really deducted (exact identity, no wall clock) ──
        var recomputed = Batch0Support.RecomputedPerOpAvg(typical);
        Batch0Support.Log(
            $"identity: published avg={statistics.Avg:F9} ns/op, recomputed from retained samples "
                + $"minus the published {statistics.OverheadNs:F0} ns/sample floor={recomputed:F9} ns/op "
                + $"(delta {(statistics.Avg - recomputed):E2})"
        );

        await Assert
            .That(Math.Abs(statistics.Avg - recomputed))
            .IsLessThanOrEqualTo(1e-9 * Math.Max(1.0, Math.Abs(statistics.Avg)))
            .Because(
                $"the run published avg = {statistics.Avg:F6} ns/op while its own retained samples "
                    + $"minus the {statistics.OverheadNs:F0} ns floor it reports give {recomputed:F6} ns/op — "
                    + "the floor was announced but not deducted, or deducted by an amount other than "
                    + "the one it reports"
            );

        // ── (3) the disclosure contract of the number that IS published ──────
        await Assert
            .That(statistics.ResolutionLimited)
            .IsEqualTo(statistics.Avg <= statistics.ResolutionLimitNanoseconds)
            .Because(
                $"ResolutionLimited = {statistics.ResolutionLimited} while avg = {statistics.Avg:F3} ns/op and "
                    + $"limit = {statistics.ResolutionLimitNanoseconds:F3} ns/op — the flag must be exactly "
                    + "'this value is at or below the limit', or a user reads a bound as a measurement"
            );

        if (statistics.ResolutionLimited)
        {
            await Assert
                .That(cell.StartsWith("<", StringComparison.Ordinal))
                .IsTrue()
                .Because(
                    $"the run is resolution limited (avg {statistics.Avg:F3} ns/op ≤ limit "
                        + $"{statistics.ResolutionLimitNanoseconds:F3} ns/op) but the per-op cell is "
                        + $"'{cell}' — a value the run cannot resolve must be printed as its upper bound"
                );

            var printedBound = Batch0Support.ParseUpperBound(cell);
            Batch0Support.Log(
                $"published bound={printedBound:F4} ns/op vs limit={statistics.ResolutionLimitNanoseconds:F4} "
                    + $"vs deducted floor/op={floorPerOp:F4}"
            );

            await Assert
                .That(printedBound)
                .IsGreaterThanOrEqualTo(0.99 * statistics.ResolutionLimitNanoseconds)
                .Because(
                    $"the printed bound {printedBound:F4} ns/op is smaller than the limit "
                        + $"{statistics.ResolutionLimitNanoseconds:F4} ns/op the same run publishes"
                );

            await Assert
                .That(printedBound)
                .IsGreaterThanOrEqualTo(0.99 * floorPerOp)
                .Because(
                    $"the printed bound {printedBound:F4} ns/op claims a precision below the "
                        + $"{floorPerOp:F4} ns/op floor this very run measured and deducted"
                );
        }
        else
        {
            // The run resolved the empty lambda above its own limit — on a slow or heavily
            // shared runner that is the honest outcome, and the library says so by printing a
            // plain number. What must still hold is the magnitude: a residual of the size the
            // unsubtracted floor produces (5.5-15.4 ns/op on the reference machine) is not
            // noise. The bound is this run's own floor × the measured noise factor, so a slow
            // runner moves both sides together and cannot fail it on its own.
            var leastInflated = runs.Min(static r =>
                r.Statistics.Avg
                / Math.Max(
                    r.Statistics.OverheadNs / r.IterationsPerSample,
                    r.Statistics.ResolutionLimitNanoseconds
                )
            );

            Batch0Support.Log(
                $"not limited: avg={statistics.Avg:F3} ns/op, run floor={runFloor:F4} ns/op, "
                    + $"least inflated trial factor={leastInflated:F3} "
                    + $"(allowed {Batch0Support.ResidualNoiseFactor:F1})"
            );

            await Assert
                .That(leastInflated)
                .IsLessThanOrEqualTo(Batch0Support.ResidualNoiseFactor)
                .Because(
                    $"an empty delegate was published as a plain {statistics.Avg:F3} ns/op, i.e. "
                        + $"{leastInflated:F2}× the {runFloor:F4} ns/op floor the same run measured and "
                        + $"deducted (allowed {Batch0Support.ResidualNoiseFactor:F1}×) — that is the size of "
                        + "an unsubtracted measurement, not of a resolved one"
                );
        }
    }

    /// <summary>
    /// Batch 0 · 0.1 — <c>Math.Sqrt</c> must be statistically distinguishable from an empty
    /// lambda <b>whenever this run can resolve either of them</b>; when it cannot, the run must
    /// say so instead of guessing.
    /// <para>
    /// The old assertion was unconditional (<c>sqrt ≥ empty + 2 × SE</c>) and went red on
    /// osx-arm64 with <c>sqrt = 0.380</c> against a threshold of <c>0.763</c> built from an
    /// empty lambda that itself hopped between 0.03 and 4.6 ns/op. Both numbers were below the
    /// run's own resolution limit, i.e. the pair was <b>not measurable</b> on that runner: the
    /// assertion was asking a 2-vCPU hosted VM for a precision it does not have. The criterion
    /// is now resolvability-aware:
    /// </para>
    /// <list type="number">
    /// <item><b>always</b> — both rows measured a floor (<c>OverheadNs &gt; 0</c>) and deducted
    /// it (the exact identity of <see cref="Batch0Support.RecomputedPerOpAvg"/>), so the
    /// workload really was normalised on both sides;</item>
    /// <item><b>if the sqrt workload cleared this run's own limit</b>
    /// (<c>sqrt &gt; sqrt limit</c>, i.e. not <see cref="Statistics.ResolutionLimited"/>) —
    /// then the original requirement holds: <c>sqrt ≥ empty + 2 × empty SE</c>, with the empty
    /// side taken from the quietest trial (interference can only inflate);</item>
    /// <item><b>otherwise</b> — the run admits it cannot separate them, so what is asserted is
    /// that it <b>discloses</b> that: <c>ResolutionLimited ⇔ Avg ≤ limit</c> on the sqrt row and
    /// a <c>&lt;limit</c> per-op cell whose bound covers the floor the run deducted.</item>
    /// </list>
    /// <para>
    /// Note the workload stays <c>Math.Sqrt</c> on a plain static field. Adding
    /// <c>Volatile.Read</c> to defeat JIT constant folding was measured (FIX-FLAKY-2 §1) and
    /// makes it <b>worse</b>: the volatile read lets the JIT common-subexpression-eliminate the
    /// square root, and the workload then costs 0.01 ns/op above an empty lambda, i.e. it
    /// measures nothing. The plain static read keeps a 1.6 ns/op signal (4.06 vs 2.44 ns/op
    /// raw, 5 trials, stable).
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.1")]
    [NotInParallel]
    public async Task Overhead_Sqrt_IsDistinguishableFromEmpty()
    {
        var config = Batch0Support.UncalibratedConfig();
        Action empty = static () => { };

        // The argument is read from a static field so the JIT cannot fold the
        // square root into a constant and measure nothing at all.
        Action sqrt = static () => _ = Math.Sqrt(SqrtSeed);

        const int trials = 5;
        var pairs = new List<(BenchmarkResult Empty, BenchmarkResult Sqrt)>(trials);

        for (var trial = 0; trial < trials; trial++)
        {
            var emptyResult = Benchmark.Run("empty-lambda", empty, warmup: null, config);
            var sqrtResult = Benchmark.Run("sqrt", sqrt, warmup: null, config);
            pairs.Add((emptyResult, sqrtResult));

            Batch0Support.Log(
                $"trial {trial}: empty={emptyResult.Statistics.Avg:F3}±{emptyResult.Statistics.StandardError:F4} ns "
                    + $"(limit {emptyResult.Statistics.ResolutionLimitNanoseconds:F3}, "
                    + $"limited {emptyResult.Statistics.ResolutionLimited}) "
                    + $"sqrt={sqrtResult.Statistics.Avg:F3}±{sqrtResult.Statistics.StandardError:F4} ns "
                    + $"(limit {sqrtResult.Statistics.ResolutionLimitNanoseconds:F3}, "
                    + $"limited {sqrtResult.Statistics.ResolutionLimited})"
            );
        }

        // The floor side is the LEAST interfered empty lambda (interference only inflates),
        // the sqrt side is the median of five trials: what has to stand out is the workload,
        // and a single interrupted sqrt trial must not decide it.
        var quietestEmpty = pairs.MinBy(static p => p.Empty.Statistics.Avg)!.Empty;
        var emptyAvg = quietestEmpty.Statistics.Avg;
        var emptySe = quietestEmpty.Statistics.StandardError;
        var sqrtAvg = Batch0Support.Median([.. pairs.Select(static p => p.Sqrt.Statistics.Avg)]);
        var sqrtLimit = Batch0Support.Median([
            .. pairs.Select(static p => p.Sqrt.Statistics.ResolutionLimitNanoseconds),
        ]);
        var threshold = emptyAvg + (2 * emptySe);
        var resolvable = sqrtAvg > sqrtLimit;

        // The row whose disclosure is checked: the least interfered sqrt trial, for the same
        // reason the contract above uses the quietest empty lambda.
        var typicalSqrt = pairs.MinBy(static p => p.Sqrt.Statistics.Avg)!.Sqrt;
        var sqrtCell = Batch0Support.ReadCsvCell(
            new CsvFormatter().Format(typicalSqrt),
            typicalSqrt.Name,
            "Avg_ns"
        );

        Batch0Support.Log(
            $"median sqrt={sqrtAvg:F3} ns/op (median limit {sqrtLimit:F3}, resolvable={resolvable}); "
                + $"quietest empty={emptyAvg:F3}±{emptySe:F4} ns/op → distinguishable threshold={threshold:F3} ns/op; "
                + $"sqrt csv Avg_ns='{sqrtCell}'"
        );

        // ── (1) both rows measured a floor and deducted exactly that amount ────
        var pairWithoutFloor = pairs.FindIndex(static p =>
            !(p.Empty.Statistics.OverheadNs > 0) || !(p.Sqrt.Statistics.OverheadNs > 0)
        );
        await Assert
            .That(pairWithoutFloor)
            .IsEqualTo(-1)
            .Because(
                pairWithoutFloor < 0
                    ? "both sides of this comparison measured and published a harness floor"
                    : $"trial {pairWithoutFloor} reported OverheadNs = "
                        + $"{pairs[pairWithoutFloor].Empty.Statistics.OverheadNs:F0}/"
                        + $"{pairs[pairWithoutFloor].Sqrt.Statistics.OverheadNs:F0} ns per sample — "
                        + "both sides of this comparison must be normalised"
            );

        var recomputedEmpty = Batch0Support.RecomputedPerOpAvg(quietestEmpty);
        var recomputedSqrt = Batch0Support.RecomputedPerOpAvg(typicalSqrt);
        Batch0Support.Log(
            $"identity: empty delta={quietestEmpty.Statistics.Avg - recomputedEmpty:E2}, "
                + $"sqrt delta={typicalSqrt.Statistics.Avg - recomputedSqrt:E2}"
        );

        await Assert
            .That(
                Math.Max(
                    Math.Abs(quietestEmpty.Statistics.Avg - recomputedEmpty)
                        / Math.Max(1.0, Math.Abs(quietestEmpty.Statistics.Avg)),
                    Math.Abs(typicalSqrt.Statistics.Avg - recomputedSqrt)
                        / Math.Max(1.0, Math.Abs(typicalSqrt.Statistics.Avg))
                )
            )
            .IsLessThanOrEqualTo(1e-9)
            .Because(
                $"empty published {quietestEmpty.Statistics.Avg:F6} vs recomputed {recomputedEmpty:F6} ns/op, "
                    + $"sqrt published {typicalSqrt.Statistics.Avg:F6} vs recomputed {recomputedSqrt:F6} ns/op — "
                    + "the harness floor each row reports must be the floor it actually subtracted"
            );

        // ── (2)/(3) distinguishability when resolvable, disclosure when not ────
        if (resolvable)
        {
            await Assert
                .That(sqrtAvg)
                .IsGreaterThanOrEqualTo(threshold)
                .Because(
                    $"the run resolved the sqrt workload above its own limit "
                        + $"({sqrtAvg:F3} > {sqrtLimit:F3} ns/op), so it must separate it from an empty "
                        + $"lambda: sqrt {sqrtAvg:F3} ns/op < empty {emptyAvg:F3} + 2×SE {emptySe:F4} "
                        + $"= {threshold:F3} ns/op"
                );
        }
        else
        {
            await Assert
                .That(typicalSqrt.Statistics.ResolutionLimited)
                .IsEqualTo(
                    typicalSqrt.Statistics.Avg <= typicalSqrt.Statistics.ResolutionLimitNanoseconds
                )
                .Because(
                    $"the sqrt workload was not resolvable above this run's limit "
                        + $"({sqrtAvg:F3} ≤ {sqrtLimit:F3} ns/op), so the row must be flagged honestly: "
                        + $"ResolutionLimited = {typicalSqrt.Statistics.ResolutionLimited} while avg = "
                        + $"{typicalSqrt.Statistics.Avg:F3} and limit = "
                        + $"{typicalSqrt.Statistics.ResolutionLimitNanoseconds:F3} ns/op"
                );

            await Assert
                .That(sqrtCell.StartsWith("<", StringComparison.Ordinal))
                .IsTrue()
                .Because(
                    $"the sqrt workload is below this run's resolution limit but its per-op cell is "
                        + $"'{sqrtCell}' — a value the run cannot resolve must be published as its bound, "
                        + "not as a number"
                );

            var printedBound = Batch0Support.ParseUpperBound(sqrtCell);
            var sqrtFloorPerOp = typicalSqrt.Statistics.OverheadNs / typicalSqrt.IterationsPerSample;
            await Assert
                .That(printedBound)
                .IsGreaterThanOrEqualTo(0.99 * sqrtFloorPerOp)
                .Because(
                    $"the published sqrt bound {printedBound:F4} ns/op claims a precision below the "
                        + $"{sqrtFloorPerOp:F4} ns/op floor this same run measured"
                );
        }

        // ── (4) ablation guard A3: the requirement above cannot be deleted silently ──
        //
        // A test with no assertion passes, so "the distinguishability requirement was
        // removed" is invisible to any runtime observation — that mutant is green by
        // construction. The requirement is therefore pinned in this file's own text (the same
        // ablation technique the workflow lint uses on .github/workflows/ci.yml): the anchor
        // has to appear both in its definition and in the assertion that applies it, so a
        // mutant that deletes the call leaves a single occurrence and turns this test red.
        var occurrences =
            Batch0Support.ReadOwnSource().Split(SqrtRequirementAnchor, StringSplitOptions.None).Length
            - 1;
        Batch0Support.Log($"anchor '{SqrtRequirementAnchor}' occurrences: {occurrences}");
        await Assert
            .That(occurrences)
            .IsGreaterThanOrEqualTo(2)
            .Because(
                $"the distinguishability requirement '{SqrtRequirementAnchor}' occurs {occurrences} time(s) "
                    + "in KnownCostOverheadTests.cs — its definition plus the assertion that enforces it must "
                    + "both be there, otherwise this anchor is all that is left of the guard"
            );
    }

    /// <summary>
    /// The assertion this test's ablation guard (A3) pins: <c>sqrt</c> has to clear the
    /// threshold derived from the empty lambda whenever the run resolves it.
    /// </summary>
    private const string SqrtRequirementAnchor = "IsGreaterThanOrEqualTo(threshold)";

    private static double SqrtSeed = 2.0;

    /// <summary>

    /// Batch 0 · 0.0 (M4 guard) + 0.1 — per-op cost must be invariant to the

    /// iteration count: the same lambda measured at 100 and at 10000 iterations

    /// per sample must agree within 20% (ratio ∈ [0.8, 1.25]).

    /// <para>

    /// The workload is <c>Thread.SpinWait(200)</c> — a known CPU cost of

    /// ≈6-9 µs/op — and not the static-field increment used before. R0 §2.6/§3

    /// measured that the old load had a net cost of ≈0 ns/op: once 0.1 subtracts

    /// the per-sample overhead, the ratio became two noises divided

    /// (−1.79 / 0.18 / 1.79) and the test stayed red even for a <b>correct</b>

    /// implementation, so it could never become the M4 guard it was written for.

    /// With the heavy load R0 measured ratio = <b>1.046</b> on a correct 0.1 and

    /// <b>0.0117</b> under the M4 mutation (drop <c>/ iterationsPerSample</c>).

    /// </para>

    /// <para>

    /// Expected at HEAD: <b>GREEN</b> — HEAD does not subtract the per-sample

    /// overhead, so both counts carry the same ≈3.7 µs additive cost on top of

    /// ≫100 ns of real work and the ratio stays ≈1.0 (the defect this test used

    /// to catch at HEAD is 0.1's per-sample overhead, which is guarded by

    /// <see cref="Overhead_EmptyLambda_IsBelowResolutionOrSubtracted"/>). Its job

    /// is the scale/normalisation mutation, where it is the only guard today.

    /// </para>

    /// </summary>

    [Test]

    [Property("Category", "Batch0")]

    [Property("Batch0", "0.0/0.1 (M4 guard)")]

    [NotInParallel]

    public async Task Normalization_PerOpInvariantAcrossIterationCounts()

    {

        // ~6-9 µs/op: far enough above the per-sample additive cost (≈3.7 µs) and

        // the clock resolution (≈100 ns) for a per-op ratio to mean something.

        Action work = static () => Thread.SpinWait(200);



        // Discarded warm-up: with WarmupIterations = 0 the first timed block would

        // otherwise measure partly tier-0 code, biasing the 100-iteration block

        // upward (measured ratio drift 1.04-1.15, i.e. only 9% below the 1.25

        // bound) — that would be a JIT artefact, not a normalisation signal. A

        // prefaced run of the same delegate leaves the assertion's meaning intact

        // (it stays green here and red under the M4 mutation).

        var warmupSample = Runner.Time(5_000, work);

        Batch0Support.Log(

            $"discarded warm-up: {warmupSample.ElapsedNanoseconds / 5_000:F0} ns/op"

        );



        const int trials = 5;

        var smallAvgs = new List<double>(trials);

        var largeAvgs = new List<double>(trials);

        var perTrialRatios = new List<double>(trials);



        for (var trial = 0; trial < trials; trial++)

        {

            BenchmarkResult small;

            BenchmarkResult large;



            // Alternate the order so a monotone drift (CPU frequency, background

            // load) is not systematically attributed to one iteration count.

            if (trial % 2 == 0)

            {

                small = Measure(100);

                large = Measure(10_000);

            }

            else

            {

                large = Measure(10_000);

                small = Measure(100);

            }



            smallAvgs.Add(small.Statistics.Avg);

            largeAvgs.Add(large.Statistics.Avg);

            perTrialRatios.Add(

                large.Statistics.Avg <= 0 ? double.PositiveInfinity : small.Statistics.Avg / large.Statistics.Avg

            );



            Batch0Support.Log(

                $"trial {trial} ({(trial % 2 == 0 ? "small-first" : "large-first")}): "

                    + $"iters={small.IterationsPerSample,6} per-op={small.Statistics.Avg,10:F3} ns | "

                    + $"iters={large.IterationsPerSample,6} per-op={large.Statistics.Avg,10:F3} ns | "

                    + $"ratio={perTrialRatios[^1]:F3}"

            );

        }



        var smallPerOp = Batch0Support.Median(smallAvgs);

        var largePerOp = Batch0Support.Median(largeAvgs);

        var ratioOfMedians = largePerOp <= 0 ? double.PositiveInfinity : smallPerOp / largePerOp;



        // The paired median is the quantity under assertion: a ratio per trial

        // cancels drift between the two moments of measurement, whereas a ratio

        // of separately pooled medians does not.

        var ratio = Batch0Support.Median(perTrialRatios);



        Batch0Support.Log(

            $"median per-op: iters=100 {smallPerOp:F3} ns, iters=10000 {largePerOp:F3} ns, "

                + $"ratio-of-medians={ratioOfMedians:F3}; paired median ratio={ratio:F3} (allowed 0.80-1.25)"

        );



        await Assert.That(ratio).IsGreaterThanOrEqualTo(0.8);

        await Assert.That(ratio).IsLessThanOrEqualTo(1.25);



        BenchmarkResult Measure(int iterationsPerSample) =>

            Benchmark.Run(

                "same-lambda",

                work,

                warmup: null,

                Batch0Support.UncalibratedConfig(sampleCount: 10, iterationsPerSample: iterationsPerSample)

            );

    }

}

