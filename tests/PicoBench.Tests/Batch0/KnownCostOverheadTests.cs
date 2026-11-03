namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · 0.0 — the "known cost" regression net, part 1: measurement
/// overhead and per-op normalisation (acceptance criteria of Batch 0 · 0.1).
/// <para>
/// These tests are the executable form of the C2 findings behind B1-C2: an
/// empty delegate reports several ns/op that are never disclosed or deducted,
/// and the same lambda reports a wildly different per-op cost at 100 vs 10000
/// iterations. #1 and #3 are expected to be RED at HEAD 5960970d and to turn
/// green when 0.1 lands; #2 is a guard that is already satisfied.
/// </para>
/// </summary>
public class KnownCostOverheadTests
{
    // Non-elidable tiny workload: a static field increment cannot be optimised
    // away, so the lambda measures ~a delegate dispatch plus one store.
    private static int _iterationSink;

    /// <summary>
    /// Batch 0 · 0.1 — an empty lambda is the absolute resolution floor. If the
    /// library reports more than 0.5 ns/op for it, it must disclose that cost
    /// (<c>Overhead</c> / <c>ResolutionLimited</c> on <see cref="Statistics"/>)
    /// so callers can subtract it or know the number is meaningless.
    /// <para>Expected at HEAD: <b>RED</b> — an empty lambda measures 8-9 ns/op
    /// on the reference machine and <see cref="Statistics"/> exposes neither
    /// signal (it has no such property at all).</para>
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
        var averages = new List<double>(trials);
        var standardErrors = new List<double>(trials);
        Statistics? last = null;

        for (var trial = 0; trial < trials; trial++)
        {
            var result = Benchmark.Run("empty-lambda", empty, warmup: null, config);
            last = result.Statistics;
            averages.Add(last.Avg);
            standardErrors.Add(last.StandardError);
            Batch0Support.Log(
                $"trial {trial}: avg={last.Avg:F3} ns/op min={last.Min:F3} p50={last.P50:F3} se={last.StandardError:F4}"
            );
        }

        var emptyAvg = Batch0Support.Median(averages);
        var emptySe = Batch0Support.Median(standardErrors);
        var disclosed = Batch0Support.TryGetDisclosedOverhead(last!, out _, out var overheadDetail);
        var flagged = Batch0Support.HasResolutionLimitedSignal(last!, out var flagDetail);

        Batch0Support.Log(
            $"empty lambda: median avg={emptyAvg:F3} ns/op (median SE {emptySe:F4}); {overheadDetail}; {flagDetail}"
        );
        Batch0Support.Log(
            $"promised floor = {Batch0Support.ResolutionFloorNanoseconds} ns/op"
        );

        if (disclosed || flagged)
            return; // the library tells the caller what the floor is: acceptable

        // Nothing disclosed: the reported number must itself be within the floor.
        await Assert.That(emptyAvg).IsLessThanOrEqualTo(Batch0Support.ResolutionFloorNanoseconds);
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
    /// Batch 0 · 0.1 — per-op cost must be invariant to the iteration count:
    /// the same lambda measured at 100 and at 10000 iterations per sample must
    /// agree within 20%.
    /// <para>Expected at HEAD: <b>RED</b> — measured ~2.9× on the reference
    /// machine for this workload (a static-field increment), i.e. the same
    /// mechanism as the FINAL-VERDICT's "现最坏 2.5×". An empty lambda shows
    /// ~9-14×, so the defect is not special to zero-cost work.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.1")]
    [NotInParallel]
    public async Task Normalization_PerOpInvariantAcrossIterationCounts()
    {
        // Warmup stays 0 on purpose: the point is the normalisation, not the JIT.
        Action work = static () => _iterationSink++;

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
