namespace PicoBench.Tests.Batch0;

/// <summary>
/// Ablation hole <b>H1-A19</b> — the merged <c>Avg</c> must be the median of the
/// per-round point estimates, not their arithmetic mean.
/// <para>
/// The 2026-11 ablation pass (`.review-2026-11/ablation-consolidated.md` §5) replaced
/// <c>Avg = Median(avgs)</c> in <see cref="ComparisonMeasurement.Merge"/> with
/// <c>avgs.Sum() / avgs.Length</c> and the whole suite stayed green (742/742): the only
/// caller of <see cref="ComparisonMeasurement.Merge"/> in the repository
/// (<c>GcAllocationMeasurementTests</c>) asserts GC counters, never a numeric
/// <c>Avg</c>. That leaves the arithmetic open to the exact defect the median was
/// introduced for — one noisy round deciding the number a user reads — and it makes
/// the merged row contradict itself, because every other column (P50/P90/P95) is still
/// a median, so <c>Avg</c> can be published <b>above P95</b>.
/// </para>
/// <para>
/// The tests below drive <see cref="ComparisonMeasurement.Merge"/> with synthetic
/// <see cref="Statistics"/> rounds, so nothing here depends on a real measurement:
/// nine clean rounds and one outlier. They are the assertions the mutation now trips.
/// </para>
/// </summary>
public class MergeStatisticsTests
{
    /// <summary>
    /// H1-A19 — a single outlying round must not move the merged <c>Avg</c>.
    /// <para>
    /// <see cref="ComparisonMeasurement.Merge"/> merges every column with the
    /// <b>median</b> over rounds, precisely so one noisy round cannot decide the
    /// number a user reads. The rest of the merged row (P50/P90/P95, the cycle and
    /// overhead figures) is a median too, so an arithmetic mean in the <c>Avg</c>
    /// column makes the row contradict itself: <c>Avg</c> can end up <b>above
    /// P95</b>, which is impossible for a genuine summary of the same samples.
    /// </para>
    /// <para>Rounds: nine at 1.0 ns and one at 100 ns. The median of the ten column
    /// values is <c>(x₅ + x₆)/2 = 1.0</c>; the arithmetic mean is 10.9.</para>
    /// <para>Expected with the H1-A19 mutation (<c>Avg = avgs.Sum() / avgs.Length</c>):
    /// <b>RED</b> — <c>Avg = 10.9</c>, <c>Avg ≠ 1</c> and <c>Avg &gt; P95 = 1.2</c>.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Ablation", "H1-A19")]
    public async Task Merge_OneOutlierRound_DoesNotMoveAvg()
    {
        var rounds = new List<Statistics>(10);
        for (var i = 0; i < 9; i++)
            rounds.Add(Statistics_1_0());
        rounds.Add(Statistics_100_0());

        var merged = ComparisonMeasurement.Merge(rounds);

        Batch0Support.Log(
            $"Merge([1.0 × 9, 100.0]): Avg={merged.Avg:F4} P50={merged.P50:F4} "
                + $"P90={merged.P90:F4} P95={merged.P95:F4} (arithmetic mean would be 10.9)"
        );

        // The point estimate is the median of the per-round point estimates…
        await Assert
            .That(merged.Avg)
            .IsEqualTo(1.0)
            .Because($"Avg = {merged.Avg:F4}: the outlier round moved the merged median");
        // …it agrees with the merged P50 of the same column family…
        await Assert.That(merged.Avg).IsEqualTo(merged.P50);
        // …and it obeys the invariant a real measurement obeys.
        await Assert
            .That(merged.Avg)
            .IsLessThanOrEqualTo(merged.P95)
            .Because(
                $"Avg = {merged.Avg:F4} exceeded P95 = {merged.P95:F4}: a mean in the Avg column "
                    + "contradicts the medians in the columns next to it"
            );
        await Assert.That(merged.Avg).IsGreaterThanOrEqualTo(merged.P50);

        // The outlier is still reported as an extreme — it is dropped from the point
        // estimate, not hidden from the run.
        await Assert.That(merged.Max).IsGreaterThanOrEqualTo(140.0);
    }

    /// <summary>
    /// H1-A19 — the same invariant as a bounded quantity: one outlying round may not
    /// lift the merged <c>Avg</c> more than 10% above the typical round.
    /// <para>
    /// Nine rounds at 1.0 ns and one at 3.0 ns. The median is 1.0 (0% lift); the
    /// arithmetic mean is 1.2, i.e. +20% — one round is enough to push the published
    /// number outside the 10% band the comparison dead band already calls "no
    /// difference". The bound is on the merged value itself, so it cannot be
    /// satisfied by weakening an assertion elsewhere.
    /// </para>
    /// <para>Expected with the H1-A19 mutation: <b>RED</b> — <c>Avg = 1.2</c>, a 20%
    /// lift.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Ablation", "H1-A19")]
    public async Task Merge_OneOutlierRound_KeepsAvgWithinTenPercentOfTheTypicalRound()
    {
        var rounds = new List<Statistics>(10);
        for (var i = 0; i < 9; i++)
            rounds.Add(Statistics_1_0());
        rounds.Add(Statistics_3_0());

        var merged = ComparisonMeasurement.Merge(rounds);
        var lift = merged.Avg / 1.0 - 1.0;

        Batch0Support.Log(
            $"Merge([1.0 × 9, 3.0]): Avg={merged.Avg:F4} lift={lift:P1} "
                + "(an arithmetic mean gives 1.2 = +20%)"
        );

        await Assert
            .That(Math.Abs(lift))
            .IsLessThanOrEqualTo(0.10)
            .Because(
                $"Avg = {merged.Avg:F4} sits {lift:P1} above the typical round: one round in ten "
                    + "moved the published number"
            );
        await Assert.That(merged.Avg).IsEqualTo(1.0);
    }

    /// <summary>
    /// H1-A19 — the merged <c>Avg</c> is the median of the <b>Avg</b> column, not of the
    /// column next to it.
    /// <para>
    /// Real rounds report an average above their median (a right-skewed sample), so nine
    /// rounds at <c>Avg = 1.05 / P50 = 1.00</c> and one outlier at
    /// <c>Avg = 105 / P50 = 100</c> separate the two candidate implementations: the
    /// median of the Avg column is 1.05, the median of the P50 column is 1.00. A merge
    /// that reads the wrong column — a plausible refactor slip, and indistinguishable in
    /// the tests above because they set both columns to the same value — publishes a
    /// median as an average and silently moves every reported number down.
    /// </para>
    /// <para>Expected with <c>Avg = Median(p50)</c>: <b>RED</b> — <c>Avg = 1.00</c>
    /// instead of 1.05, i.e. <c>Avg == P50</c>.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Ablation", "H1-A19")]
    public async Task Merge_UsesTheAvgColumn_NotTheP50Column()
    {
        var rounds = new List<Statistics>(10);
        for (var i = 0; i < 9; i++)
            rounds.Add(SkewedRound(1.05, 1.00));
        rounds.Add(SkewedRound(105.0, 100.0));

        var merged = ComparisonMeasurement.Merge(rounds);

        Batch0Support.Log(
            $"Merge([Avg 1.05 / P50 1.00 × 9, Avg 105 / P50 100]): "
                + $"Avg={merged.Avg:F4} P50={merged.P50:F4}"
        );

        await Assert
            .That(merged.Avg)
            .IsEqualTo(1.05)
            .Because(
                $"Avg = {merged.Avg:F4}: the merged average must come from the Avg column "
                    + "(1.05), not from the P50 column (1.00)"
            );
        await Assert.That(merged.P50).IsEqualTo(1.00);
        await Assert.That(merged.Avg).IsGreaterThan(merged.P50);
    }

    /// <summary>
    /// One round with a right-skewed sample: <paramref name="avg"/> above
    /// <paramref name="p50"/>, the shape a real measurement has.
    /// </summary>
    private static Statistics SkewedRound(double avg, double p50) =>
        new()
        {
            Avg = avg,
            P50 = p50,
            P90 = p50 * 1.1,
            P95 = p50 * 1.2,
            P99 = p50 * 1.25,
            Min = p50 * 0.9,
            Max = p50 * 1.3,
            StdDev = p50 * 0.05,
            StandardError = p50 * 0.01,
            RelativeStdDevPercent = 5.0,
            CpuCyclesPerOp = p50 * 3.0,
            OverheadNs = 100.0,
            ResolutionLimitNanoseconds = 0.5,
        };

    /// <summary>One clean round: 1.0 ns/op typical, 1.2 ns/op at P95.</summary>
    private static Statistics Statistics_1_0() =>
        new()
        {
            Avg = 1.0,
            P50 = 1.0,
            P90 = 1.1,
            P95 = 1.2,
            P99 = 1.25,
            Min = 0.9,
            Max = 1.3,
            StdDev = 0.05,
            StandardError = 0.01,
            RelativeStdDevPercent = 5.0,
            CpuCyclesPerOp = 3.0,
            OverheadNs = 100.0,
            ResolutionLimitNanoseconds = 0.5,
        };

    /// <summary>One round 100× slower than <see cref="Statistics_1_0"/> — the outlier.</summary>
    private static Statistics Statistics_100_0() =>
        new()
        {
            Avg = 100.0,
            P50 = 100.0,
            P90 = 110.0,
            P95 = 120.0,
            P99 = 125.0,
            Min = 90.0,
            Max = 140.0,
            StdDev = 5.0,
            StandardError = 1.0,
            RelativeStdDevPercent = 5.0,
            CpuCyclesPerOp = 300.0,
            OverheadNs = 100.0,
            ResolutionLimitNanoseconds = 0.5,
        };

    /// <summary>One round 3× slower than <see cref="Statistics_1_0"/> — a milder outlier.</summary>
    private static Statistics Statistics_3_0() =>
        new()
        {
            Avg = 3.0,
            P50 = 3.0,
            P90 = 3.1,
            P95 = 3.2,
            P99 = 3.25,
            Min = 2.9,
            Max = 3.3,
            StdDev = 0.05,
            StandardError = 0.01,
            RelativeStdDevPercent = 5.0,
            CpuCyclesPerOp = 9.0,
            OverheadNs = 100.0,
            ResolutionLimitNanoseconds = 0.5,
        };
}
