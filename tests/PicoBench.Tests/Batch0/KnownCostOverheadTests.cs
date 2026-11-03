namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · 0.0 — the "known cost" regression net, part 1: measurement
/// overhead and per-op normalisation (acceptance criteria of Batch 0 · 0.1).
/// <para>
/// These tests are the executable form of the C2 findings behind B1-C2: an
/// empty delegate reports several ns/op that are never disclosed or deducted,
/// and the same lambda reports a different per-op cost at 100 vs 10000
/// iterations. #1 is expected to be RED at HEAD (7d63ea2) and to turn green
/// when 0.1 lands; #2 and #3 are guards.
/// </para>
/// <para>
/// R0's review (<c>.review-2026-11/08-regression-net-review.md</c> §2.4, §2.6,
/// §3) rewrote two of the three: the disclosure check now reads the formatted
/// output instead of guessing property names, and the normalisation workload is
/// a real cost (<c>Thread.SpinWait(200)</c>) instead of a store whose net cost is
/// below the resolution floor.
/// </para>
/// </summary>
public class KnownCostOverheadTests
{
    /// <summary>
    /// Batch 0 · 0.1 — an empty lambda is the absolute resolution floor. The
    /// reported cost of an empty delegate must either be ≤1.5 ns/op, or be
    /// **disclosed in the user-visible output** (the per-operation cell says
    /// "below resolution" / "overhead" / "N/A").
    /// <para>
    /// R0 §2.4 replaced two things here:
    /// <list type="number">
    /// <item>the 0.5 ns threshold → 1.5 ns, because the residual of two
    /// independent empty-delegate measurements has sd ≈ 0.5 ns (0.5 ns was
    /// ≈1 σ and would flag a correct fix half the time);</item>
    /// <item>the disclosure check — it used to reflect over four guessed
    /// property names on <see cref="Statistics"/>, which a three-line stub
    /// (<c>OverheadPerOp =&gt; 1.0</c>, read by nobody) satisfied while the user
    /// still saw 5.5 ns/op. It now reads the CSV <c>Avg_ns</c> cell and the
    /// Console/Markdown/HTML row for this benchmark, so only a real fix can pass
    /// (<see cref="Batch0Support.FormattedOutputDisclosesResolution"/>).</item>
    /// </list>
    /// </para>
    /// <para>
    /// The <b>relative</b> alternative the review offered (empty ≤ 0.5 ×
    /// <c>Math.Sqrt</c>) was measured and then rejected, because it green-lights
    /// the unfixed library roughly one run in six: over 6 runs at HEAD the pair
    /// was 5.457/9.294, 6.584/13.526, 7.872/12.351, 9.639/18.488, 10.256/19.598,
    /// 10.401/20.203, i.e. ratio 0.487-0.637 — one run below the 0.5 cut. Both
    /// numbers are dominated by the same additive delegate floor, so any ratio
    /// between them is noise; only the absolute floor (or a real disclosure)
    /// discriminates. Measuring the sqrt reference here was therefore dropped
    /// again: it also warmed the sqrt lambda for
    /// <see cref="Overhead_Sqrt_IsDistinguishableFromEmpty"/> and collapsed that
    /// anchor's margin (its whole signal is the sqrt cost above the floor).
    /// </para>
    /// <para>Expected at HEAD: <b>RED</b> — an empty lambda measures 5.5-15.4
    /// ns/op (3.6-10× above the floor) and no formatter prints any floor
    /// disclosure, so neither the absolute nor the disclosure leg passes.</para>
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
        var emptyAvgs = new List<double>(trials);
        var emptySes = new List<double>(trials);
        BenchmarkResult? lastEmpty = null;

        for (var trial = 0; trial < trials; trial++)
        {
            var emptyResult = Benchmark.Run("empty-lambda", empty, warmup: null, config);

            lastEmpty = emptyResult;
            emptyAvgs.Add(emptyResult.Statistics.Avg);
            emptySes.Add(emptyResult.Statistics.StandardError);

            Batch0Support.Log(
                $"trial {trial}: empty={emptyResult.Statistics.Avg:F3}±{emptyResult.Statistics.StandardError:F4} ns/op "
                    + $"min={emptyResult.Statistics.Min:F3}"
            );
        }

        var emptyAvg = Batch0Support.Median(emptyAvgs);
        var emptySe = Batch0Support.Median(emptySes);

        var absoluteLimit = Batch0Support.ResolutionFloorNanoseconds;
        var disclosed = Batch0Support.FormattedOutputDisclosesResolution(
            lastEmpty!,
            out var formattedPerOp,
            out var disclosureEvidence
        );

        Batch0Support.Log(
            $"empty lambda: median avg={emptyAvg:F3} ns/op (median SE {emptySe:F4})"
        );
        Batch0Support.Log(
            $"absolute limit={absoluteLimit:F2} ns; "
                + $"formatted per-op cell={(formattedPerOp is null ? "<not numeric>" : $"{formattedPerOp:F3} ns")}"
        );
        Batch0Support.Log("disclosure check: " + disclosureEvidence);

        if (emptyAvg <= absoluteLimit)
        {
            Batch0Support.Log($"PASS: empty lambda reported {emptyAvg:F3} ns/op ≤ {absoluteLimit:F2} ns");
            return;
        }

        if (disclosed)
        {
            Batch0Support.Log("PASS: the formatted output discloses the delegate floor");
            return;
        }

        // Red at HEAD: neither the absolute floor, nor the relative floor, nor
        // any user-visible disclosure. Note that no `Statistics` decoration
        // property can influence this assertion — only the formatted output.
        await Assert
            .That(emptyAvg)
            .IsLessThanOrEqualTo(absoluteLimit)
            .Because(
                $"empty lambda = {emptyAvg:F3} ns/op and the library discloses nothing in its output "
                    + $"(absolute limit {absoluteLimit:F2} ns)"
            );
    }

    /// <summary>
    /// Batch 0 · 0.1 — <c>Math.Sqrt</c> must be statistically distinguishable
    /// from an empty lambda (sqrt avg ≥ empty avg + 2 × empty SE). This is the
    /// positive half of the overhead test: whatever floor exists must be small
    /// enough that a genuine single-digit-ns operation still stands out.
    /// <para>Expected at HEAD: <b>GREEN</b> (reference machine: empty ≈ 7-9 ns,
    /// sqrt ≈ 16 ns, 2 × SE ≈ 0.3 ns). It is kept as a guard for 0.1: once the
    /// delegate baseline is subtracted, the difference must survive.</para>
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
        var emptyAvgs = new List<double>(trials);
        var emptySes = new List<double>(trials);
        var sqrtAvgs = new List<double>(trials);

        for (var trial = 0; trial < trials; trial++)
        {
            var emptyResult = Benchmark.Run("empty-lambda", empty, warmup: null, config);
            var sqrtResult = Benchmark.Run("sqrt", sqrt, warmup: null, config);

            emptyAvgs.Add(emptyResult.Statistics.Avg);
            emptySes.Add(emptyResult.Statistics.StandardError);
            sqrtAvgs.Add(sqrtResult.Statistics.Avg);

            Batch0Support.Log(
                $"trial {trial}: empty={emptyResult.Statistics.Avg:F3}±{emptyResult.Statistics.StandardError:F4} ns "
                    + $"sqrt={sqrtResult.Statistics.Avg:F3}±{sqrtResult.Statistics.StandardError:F4} ns"
            );
        }

        var emptyAvg = Batch0Support.Median(emptyAvgs);
        var emptySe = Batch0Support.Median(emptySes);
        var sqrtAvg = Batch0Support.Median(sqrtAvgs);
        var threshold = emptyAvg + (2 * emptySe);

        Batch0Support.Log(
            $"median empty={emptyAvg:F3} ns (median SE {emptySe:F4}), median sqrt={sqrtAvg:F3} ns, threshold={threshold:F3} ns"
        );

        await Assert.That(sqrtAvg).IsGreaterThanOrEqualTo(threshold);
    }

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

        var smallAvgs = new List<double>();
        var largeAvgs = new List<double>();

        for (var trial = 0; trial < 3; trial++)
        {
            var small = Benchmark.Run(
                "same-lambda",
                work,
                warmup: null,
                Batch0Support.UncalibratedConfig(sampleCount: 10, iterationsPerSample: 100)
            );
            var large = Benchmark.Run(
                "same-lambda",
                work,
                warmup: null,
                Batch0Support.UncalibratedConfig(sampleCount: 10, iterationsPerSample: 10_000)
            );

            smallAvgs.Add(small.Statistics.Avg);
            largeAvgs.Add(large.Statistics.Avg);

            Batch0Support.Log(
                $"trial {trial}: iters={small.IterationsPerSample,6} per-op={small.Statistics.Avg,10:F3} ns | "
                    + $"iters={large.IterationsPerSample,6} per-op={large.Statistics.Avg,10:F3} ns"
            );
        }

        var smallPerOp = Batch0Support.Median(smallAvgs);
        var largePerOp = Batch0Support.Median(largeAvgs);
        var ratio = largePerOp <= 0 ? double.PositiveInfinity : smallPerOp / largePerOp;

        Batch0Support.Log(
            $"median per-op: iters=100 {smallPerOp:F3} ns, iters=10000 {largePerOp:F3} ns, ratio={ratio:F3} (allowed 0.80-1.25)"
        );

        await Assert.That(ratio).IsGreaterThanOrEqualTo(0.8);
        await Assert.That(ratio).IsLessThanOrEqualTo(1.25);
    }
}
