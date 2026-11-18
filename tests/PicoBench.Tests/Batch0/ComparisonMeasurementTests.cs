namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · G6 — direct (deterministic) tests of the comparison statistics.
/// <para>
/// G5's verification matrix (<c>.review-2026-11/16-verification.md</c> §3.2)
/// changed the production code five ways and the whole suite stayed green: with
/// 10 interleaved rounds, the paired median alone keeps a self-comparison
/// neutral, so the 5% dead band and the "interval excludes parity" condition were
/// each removable without a single red. This file pins both gates — and the
/// number of rounds the sign-test interval needs — on <b>synthetic ratios</b>,
/// so no assertion here depends on a real measurement.
/// </para>
/// <para>
/// The tests reach <see cref="ComparisonMeasurement"/> through
/// <c>InternalsVisibleTo</c> but never re-implement its arithmetic: they call
/// <see cref="ComparisonMeasurement.PairedSpeedup"/> and
/// <see cref="ComparisonResult.Classify"/> — the two halves of
/// <see cref="ComparisonResult.Outcome"/> — and assert on the public verdict.
/// </para>
/// </summary>
public class ComparisonMeasurementTests
{
    /// <summary>
    /// G4 · B1 — a comparison whose sign-test interval cannot reach 95% must not
    /// state a verdict. The coverage of <c>[x_(k), x_(n+1-k)]</c> is
    /// <c>1 - 2·P(Bin(n, ½) ≤ k-1)</c>, so n = 1/2/3/4/5 tops out at
    /// 50/50/75/87.5/93.75% — all below the 95% the interval claims — and for
    /// n ≤ 2 the interval degenerates to <c>[r, r]</c>, which makes a lone ratio
    /// of 2.0 look like a difference with a zero-width interval.
    /// <para>
    /// This is the reachable B1 path: <c>Benchmark.PerOpNanoseconds</c> clamps the
    /// residual at 0, so an all-zero <see cref="Statistics.Avg"/> lane makes
    /// <see cref="ComparisonMeasurement.Ratio"/> return <see cref="double.NaN"/>
    /// and drops the round (G4 §4.1 measured 9 zero rounds + one 2.0 ⇒ a false
    /// <c>Faster</c>).
    /// </para>
    /// <para>Expected before the fix: <b>RED</b> — every case below reports
    /// <c>speedup = 2.0</c>, an interval of <c>[2.000, 2.000]</c> and
    /// <see cref="ComparisonOutcome.Faster"/>.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.2/B1")]
    public async Task PairedSpeedup_FewerThanSixUsableRounds_StatesNoVerdict()
    {
        foreach (var usable in new[] { 1, 2, 3, 4, 5 })
        {
            // `usable` rounds measured a candidate half the baseline's time, the
            // remaining rounds measured nothing at all (Avg == 0 ⇒ Ratio ⇒ NaN) —
            // exactly the shape the clamp produces for a sub-floor workload.
            var ratios = Ratios(usable, 2.0);
            var (speedup, interval) = ComparisonMeasurement.PairedSpeedup(ratios);
            var comparison = Comparison(speedup, interval);

            Batch0Support.Log(
                $"usable={usable}/10: speedup={speedup:F3} interval={interval?.ToString() ?? "<none>"} "
                    + $"outcome={comparison.Outcome} isFaster={comparison.IsFaster} "
                    + $"isFasterKnown={comparison.IsFasterKnown}"
            );

            await Assert.That(double.IsNaN(speedup)).IsTrue();
            await Assert.That(interval).IsNull();
            await Assert.That(comparison.Outcome).IsEqualTo(ComparisonOutcome.Unknown);
            await Assert.That(comparison.IsFaster).IsFalse();
            await Assert.That(comparison.IsFasterKnown).IsFalse();
        }
    }

    /// <summary>
    /// G4 · B1, positive control — the "no interval below 6 rounds" rule must not
    /// degenerate into "never report an interval". Six usable rounds are the
    /// smallest count whose 2nd/5th order statistics cover the median with
    /// 1 − 2·P(Bin(6, ½) ≤ 0) = 96.875%, so the interval comes back and a 2×
    /// candidate is still called faster.
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.2/B1 (positive control)")]
    public async Task PairedSpeedup_WithSixUsableRounds_StillReportsAVerdict()
    {
        var (speedup, interval) = ComparisonMeasurement.PairedSpeedup(Ratios(6, 2.0));
        var comparison = Comparison(speedup, interval);

        Batch0Support.Log(
            $"usable=6/10: speedup={speedup:F3} interval={interval?.ToString() ?? "<none>"} "
                + $"outcome={comparison.Outcome}"
        );

        await Assert.That(speedup).IsEqualTo(2.0);
        await Assert.That(interval).IsNotNull();
        await Assert.That(comparison.Outcome).IsEqualTo(ComparisonOutcome.Faster);
        await Assert.That(comparison.IsFasterKnown).IsTrue();
    }

    /// <summary>
    /// G5 §3.2 #4 — the 5% dead band is its own gate and needs its own guard: with
    /// 10 interleaved rounds the paired median alone keeps a self-comparison
    /// neutral, so removing the dead band kept every existing test green.
    /// <para>
    /// A 2% difference is inside the noise of one round: <c>Inconclusive</c>, not
    /// "faster". The ratios are synthetic, so this cannot be satisfied by a lucky
    /// measurement.
    /// </para>
    /// <para>Expected with <c>DeadBand = 0</c>: <b>RED</b> — 1.02 &gt; 1.0 with an
    /// interval of [1.020, 1.020] reads as <see cref="ComparisonOutcome.Faster"/>.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.2/dead band")]
    public async Task DeadBand_TwoPercentDifference_IsNeutral()
    {
        var (speedup, interval) = ComparisonMeasurement.PairedSpeedup(Uniform(1.02));
        var comparison = Comparison(speedup, interval);

        Batch0Support.Log(
            $"median=1.02: speedup={speedup:F4} interval={interval?.ToString() ?? "<none>"} "
                + $"outcome={comparison.Outcome}"
        );

        await Assert.That(speedup).IsEqualTo(1.02);
        await Assert.That(comparison.IsFasterKnown).IsTrue();
        await Assert.That(comparison.IsFaster).IsFalse();
        await Assert.That(comparison.Outcome).IsEqualTo(ComparisonOutcome.Inconclusive);
    }

    /// <summary>
    /// G5 §3.2 #4, positive half — the dead band must not swallow a real difference.
    /// A 50% faster candidate is outside the band with an interval that excludes
    /// parity, so it must still be reported as faster (this is what keeps the guard
    /// from being satisfied by "never report a difference").
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.2/dead band (positive control)")]
    public async Task DeadBand_FiftyPercentDifference_IsFaster()
    {
        var (speedup, interval) = ComparisonMeasurement.PairedSpeedup(Uniform(1.5));
        var comparison = Comparison(speedup, interval);

        Batch0Support.Log(
            $"median=1.50: speedup={speedup:F4} interval={interval?.ToString() ?? "<none>"} "
                + $"outcome={comparison.Outcome}"
        );

        await Assert.That(comparison.Outcome).IsEqualTo(ComparisonOutcome.Faster);
        await Assert.That(comparison.IsFaster).IsTrue();
    }

    /// <summary>
    /// G5 §3.2 #5 — the "interval excludes parity" condition is a second, separate
    /// gate. A point estimate 6% above parity (outside the 5% dead band) whose
    /// interval still contains 1.0 must not become a verdict.
    /// <para>Ratios: one round at 0.90 and one at 0.95 drag the 2nd order statistic
    /// below parity, the remaining eight sit at 1.06, so the median is 1.06 while
    /// the 95% interval is [0.95, 1.06].</para>
    /// <para>Expected with the <c>Low &gt; 1</c> / <c>High &lt; 1</c> conditions
    /// removed from <c>Classify</c>: <b>RED</b> — 1.06 &gt; 1.05 is reported as
    /// faster even though parity is still inside the interval.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.2/interval")]
    public async Task Interval_ContainingParity_HoldsTheVerdictBack()
    {
        double[] ratios = [0.90, 0.95, 1.06, 1.06, 1.06, 1.06, 1.06, 1.06, 1.06, 1.06];

        var (speedup, interval) = ComparisonMeasurement.PairedSpeedup(ratios);
        var comparison = Comparison(speedup, interval);

        Batch0Support.Log(
            $"median=1.06, parity inside: speedup={speedup:F4} "
                + $"interval={interval?.ToString() ?? "<none>"} outcome={comparison.Outcome}"
        );

        await Assert.That(speedup).IsGreaterThan(1.05);
        await Assert.That(interval).IsNotNull();
        await Assert.That(interval!.Value.Low).IsLessThanOrEqualTo(1.0);
        await Assert.That(comparison.IsFaster).IsFalse();
        await Assert.That(comparison.Outcome).IsEqualTo(ComparisonOutcome.Inconclusive);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Ablation holes H1-A18 / H1-A19 (branch fix/ablation-holes-runtime).
    //
    // The 2026-11 ablation pass (`.review-2026-11/ablation-consolidated.md` §5)
    // changed two of the statistics below and the whole suite stayed green:
    //
    //   H1-A18  MedianConfidenceRank → "any count returns rank 1"   (742/742 green)
    //   H1-A19  Merge: Avg = Median(avgs) → avgs.Sum() / Length      (742/742 green)
    //
    // Nothing in the repository called the rank function directly, and no test
    // asserted a numeric value that a mean (instead of a median) would move, so
    // both defects lived inside a green suite. The tests below are the missing
    // assertions; they are the reason the corresponding mutations now go red.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// H1-A18 — the sign-test interval must actually narrow as the round count
    /// grows, and the ten-round comparison must drop the <b>outermost pair</b> of
    /// order statistics.
    /// <para>
    /// Coverage of <c>[x_(k), x_(n+1-k)]</c> is <c>1 - 2·P(Bin(n, ½) ≤ k-1)</c>, so
    /// k = 2 is the first rank that reaches 95% at n = 10 (97.9%) and k = 3 is the
    /// first at n = 12 (98.4%). A rank pinned to 1 for every count returns the full
    /// range <c>[x₁, x_n]</c>, i.e. a single stray round decides the interval —
    /// while the UI still advertises 95%.
    /// </para>
    /// <para>Expected with <c>MedianConfidenceRank</c> returning 1 for every count
    /// (the H1-A18 mutation <c>count &lt; 3</c> → <c>count &lt; int.MaxValue</c>):
    /// <b>RED</b> — rank(10) = 1 instead of 2 and rank(12) = 1 instead of 3.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Ablation", "H1-A18")]
    public async Task MedianConfidenceRank_TenAndTwelveRounds_DropTheOutermostOrderStatistics()
    {
        var rank10 = ComparisonMeasurement.MedianConfidenceRank(10);
        var rank12 = ComparisonMeasurement.MedianConfidenceRank(12);
        var rank6 = ComparisonMeasurement.MedianConfidenceRank(
            ComparisonMeasurement.MinimumUsableRounds
        );

        Batch0Support.Log(
            $"rank(6)={rank6} rank(10)={rank10} rank(12)={rank12} (n=10 must drop x1 and x10)"
        );

        // Ten interleaved rounds: [x_(2), x_(9)] — the first and the last ratio are
        // both discarded, which is exactly what the Rounds doc comment claims.
        await Assert
            .That(rank10)
            .IsEqualTo(2)
            .Because($"rank(10) = {rank10}: ten rounds must drop the outermost pair");
        // Twelve usable ratios narrow it one step further: [x_(3), x_(10)].
        await Assert
            .That(rank12)
            .IsEqualTo(3)
            .Because($"rank(12) = {rank12}: twelve rounds reach 95% with three dropped per side");
        // Six is the smallest count that reaches 95%, and it does so with the full
        // range (96.875% = 1 - 2·P(Bin(6, ½) ≤ 0)) — no order statistic is dropped.
        await Assert
            .That(rank6)
            .IsEqualTo(1)
            .Because($"rank(6) = {rank6}: n = 6 is the 95% boundary at k = 1");
    }

    /// <summary>
    /// H1-A18 — rank <c>k</c> is the <i>narrowest</i> interval that keeps 95%, so it
    /// can only grow with the round count: more rounds buy a tighter tail, never a
    /// wider one. A constant rank (the surviving mutation) is monotone by accident,
    /// which is why the equality assertions above are the ones that bite; this test
    /// pins the property a re-derivation of the loop must preserve, together with the
    /// two bounds the loop enforces itself (<c>1 ≤ k ≤ (n+1)/2</c>).
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Ablation", "H1-A18")]
    public async Task MedianConfidenceRank_IsMonotonicNonDecreasingInRoundCount()
    {
        var ranks = new List<int>(40);
        var previous = 0;

        for (var count = 3; count <= 40; count++)
        {
            var rank = ComparisonMeasurement.MedianConfidenceRank(count);
            ranks.Add(rank);

            // A rank below 1 indexes outside the array; above (n+1)/2 it reverses
            // the interval ends.
            await Assert.That(rank).IsGreaterThanOrEqualTo(1);
            await Assert.That(rank).IsLessThanOrEqualTo((count + 1) / 2);

            if (count > 3)
            {
                await Assert
                    .That(rank)
                    .IsGreaterThanOrEqualTo(previous)
                    .Because(
                        $"rank({count}) = {rank} fell below rank({count - 1}) = {previous}: "
                            + "more rounds can only narrow the 95% interval"
                    );
            }

            previous = rank;
        }

        Batch0Support.Log(
            "rank by count 3..40: "
                + string.Join(' ', ranks.Select((rank, i) => $"{i + 3}:{rank}"))
        );
    }

    /// <summary>
    /// H1-A18 — the interval <see cref="ComparisonMeasurement.PairedSpeedup"/> builds
    /// from those ratios must be <b>strictly inside</b> the full range of the ten
    /// ratios whenever the two extremes are outliers.
    /// <para>
    /// The input carries a 20× low outlier (0.5) and a 10× high outlier (10.0); the
    /// remaining eight sit between 0.9 and 1.1. With rank 2 the interval is
    /// <c>[x_(2), x_(9)] = [0.9, 1.1]</c>, so both ends are strictly inside the run's
    /// own extremes and the width collapses from 9.5 to 0.2. With the rank forced to
    /// 1 (H1-A18) it degenerates to <c>[0.5, 10.0]</c>: the low outlier drags
    /// <c>Low</c> down to <c>min</c>, the high outlier drags <c>High</c> up to
    /// <c>max</c>, and one bad round per side decides the very verdict the interval
    /// is supposed to guard.
    /// </para>
    /// <para>Expected with the H1-A18 mutation: <b>RED</b> — <c>Low == min = 0.5</c>
    /// and <c>High == max = 10.0</c>.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Ablation", "H1-A18")]
    public async Task PairedSpeedup_IntervalIsStrictlyInsideTheFullRatioRange()
    {
        double[] ratios = [0.5, 0.9, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.1, 10.0];
        var min = ratios.Min();
        var max = ratios.Max();

        var (speedup, interval) = ComparisonMeasurement.PairedSpeedup(ratios);
        var comparison = Comparison(speedup, interval);

        Batch0Support.Log(
            $"full range=[{min:F3}, {max:F3}] median={speedup:F4} "
                + $"interval={interval?.ToString() ?? "<none>"} outcome={comparison.Outcome}"
        );

        await Assert.That(interval).IsNotNull();
        var (low, high) = (interval!.Value.Low, interval.Value.High);

        // The point estimate is the median, i.e. inside the narrowed interval…
        await Assert.That(speedup).IsEqualTo(1.0);
        await Assert.That(low).IsLessThanOrEqualTo(speedup);
        await Assert.That(high).IsGreaterThanOrEqualTo(speedup);

        // …and the interval is strictly narrower than the run's own extremes:
        // both outliers are dropped, not merely covered.
        await Assert
            .That(low)
            .IsGreaterThan(min)
            .Because(
                $"Low = {low:F3} equals the run minimum {min:F3}: the low outlier was not dropped"
            );
        await Assert
            .That(high)
            .IsLessThan(max)
            .Because(
                $"High = {high:F3} equals the run maximum {max:F3}: the high outlier was not dropped"
            );

        // Quantified: the 95% interval is under a quarter of the range it sits in
        // (0.2 against 9.5). A rank of 1 makes this ratio exactly 1.0.
        await Assert
            .That(high - low)
            .IsLessThan(0.25 * (max - min))
            .Because($"interval width {high - low:F3} vs full range {max - min:F3}");
    }

    /// <summary>Ten identical ratios — the shape a perfectly consistent comparison produces.</summary>
    private static double[] Uniform(double ratio)
    {
        var ratios = new double[ComparisonMeasurement.Rounds];
        for (var i = 0; i < ratios.Length; i++)
            ratios[i] = ratio;

        return ratios;
    }

    /// <summary>
    /// Builds 10 per-round ratios: the first <paramref name="usable"/> carry
    /// <paramref name="ratio"/>, the rest are <see cref="double.NaN"/> (a round in
    /// which the candidate lane measured nothing).
    /// </summary>
    private static double[] Ratios(int usable, double ratio)
    {
        var ratios = new double[ComparisonMeasurement.Rounds];
        for (var i = 0; i < ratios.Length; i++)
            ratios[i] = i < usable ? ratio : double.NaN;

        return ratios;
    }

    /// <summary>
    /// Wraps a measured ratio and interval in a <see cref="ComparisonResult"/>, so
    /// the assertions run against the same public verdict a consumer reads
    /// (<see cref="ComparisonResult.Outcome"/> → <see cref="ComparisonResult.IsFaster"/>).
    /// </summary>
    private static ComparisonResult Comparison(double speedup, ComparisonInterval? interval) =>
        new(
            name: "synthetic",
            baseline: BenchmarkResultFactory.Create(name: "baseline"),
            candidate: BenchmarkResultFactory.Create(name: "candidate"),
            category: null,
            tags: null,
            speedup: speedup,
            interval: interval
        );
}
