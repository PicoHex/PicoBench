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
