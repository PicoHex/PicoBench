namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · G6 (guard gap of 0.3) — <see cref="Statistics.Unmeasured"/> must be
/// its own signal, not something the formatter infers from
/// <see cref="Statistics.ResolutionLimited"/>.
/// <para>
/// G5's matrix (<c>.review-2026-11/16-verification.md</c> §3.2, row <c>#7</c>)
/// forced <c>UnmeasuredSampleCount</c> to 0 and the whole suite stayed green: the
/// existing 0.3 guard accepts "a field flag <b>and</b> a user-visible marker", and
/// <c>ResolutionLimited</c> (which is also true for a zero-delta sample) kept the
/// marker printing. A caller that reads <see cref="Statistics.Unmeasured"/> then
/// gets a wrong value while the output looks right. These assertions are made on
/// the field itself and on synthetic samples, so no real clock is involved.
/// </para>
/// </summary>
public class MeasurementFieldTests
{
    private const double OneCpuTick = 15_625_000.0; // 15.625 ms, Windows' CPU-time tick

    /// <summary>
    /// A <c>CpuOnly</c> run whose clock did not advance for 8 of 12 samples must
    /// report <see cref="Statistics.Unmeasured"/>, and the two flags must be shown
    /// to mean different things: a run in which <b>every</b> sample was measured but
    /// whose mean sits at the resolution limit is resolution-limited without being
    /// unmeasured.
    /// <para>Expected under the "unmeasured count is zero" mutation: <b>RED</b> —
    /// the count reads 0 and <see cref="Statistics.Unmeasured"/> turns false while
    /// the formatted output keeps saying "not measured" (via
    /// <see cref="Statistics.ResolutionLimited"/>).</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.3/unmeasured field")]
    public async Task Unmeasured_IsFlaggedIndependentlyOfTheResolutionLimit()
    {
        // ── 1. Quantised CpuOnly samples: 8 read exactly 0, 4 read one tick ──
        var mixed = new TimingSample[12];
        for (var i = 0; i < mixed.Length; i++)
            mixed[i] = Sample(i < 8 ? 0.0 : OneCpuTick);

        var mixedStats = StatisticsCalculator.Compute(
            PerOp(mixed),
            new double[mixed.Length],
            mixed,
            harnessOverheadNanoseconds: 0,
            resolutionLimitNanoseconds: OneCpuTick
        );

        Batch0Support.Log(
            $"mixed: unmeasured={mixedStats.UnmeasuredSampleCount}/{mixed.Length} "
                + $"Unmeasured={mixedStats.Unmeasured} resolutionLimited={mixedStats.ResolutionLimited} "
                + $"avg={mixedStats.Avg:F0} min={mixedStats.Min:F0}"
        );

        await Assert.That(mixedStats.UnmeasuredSampleCount).IsEqualTo(8);
        await Assert.That(mixedStats.Unmeasured).IsTrue();

        // ── 2. Every sample measured, mean at the limit: the other flag alone ──
        var measured = new TimingSample[12];
        for (var i = 0; i < measured.Length; i++)
            measured[i] = Sample(OneCpuTick);

        var measuredStats = StatisticsCalculator.Compute(
            PerOp(measured),
            new double[measured.Length],
            measured,
            harnessOverheadNanoseconds: 0,
            resolutionLimitNanoseconds: OneCpuTick
        );

        Batch0Support.Log(
            $"measured-at-limit: unmeasured={measuredStats.UnmeasuredSampleCount}/{measured.Length} "
                + $"Unmeasured={measuredStats.Unmeasured} resolutionLimited={measuredStats.ResolutionLimited}"
        );

        await Assert.That(measuredStats.UnmeasuredSampleCount).IsEqualTo(0);
        await Assert.That(measuredStats.Unmeasured).IsFalse();
        await Assert.That(measuredStats.ResolutionLimited).IsTrue();
    }

    private static double[] PerOp(TimingSample[] samples)
    {
        var perOp = new double[samples.Length];
        for (var i = 0; i < samples.Length; i++)
            perOp[i] = samples[i].ElapsedNanoseconds; // one iteration per sample

        return perOp;
    }

    private static TimingSample Sample(double elapsedNanoseconds) =>
        new()
        {
            ElapsedNanoseconds = elapsedNanoseconds,
            ElapsedMilliseconds = elapsedNanoseconds / 1_000_000.0,
            ElapsedTicks = (long)(elapsedNanoseconds / 100.0),
            CpuCycles = 0,
            CpuCyclesAreApproximate = true,
            GcInfo = null,
        };
}
