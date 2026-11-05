namespace PicoBench.Tests.Batch2;

/// <summary>
/// Batch 2 · 2.6 (K6 / B2-M4) — the statistics layer tested directly.
/// <para>
/// Every existing test of <see cref="Statistics"/> went through
/// <c>Benchmark.Run</c>, i.e. through a real clock, so it could only assert
/// invariants that are true by construction (<c>P99 &lt;= Max</c>,
/// <c>Avg &gt;= 0</c>). A wrong percentile formula, a wrong variance denominator or
/// a broken zero-guard would have passed all of them.
/// <see cref="StatisticsCalculator.Compute"/> is <c>internal</c> and the test
/// assembly is already a friend, so these tests feed fixed arrays and assert exact
/// values.
/// </para>
/// <para>
/// The percentile definition is pinned on purpose: <b>linear interpolation between
/// the two nearest order statistics</b> (the "type 7" / Excel <c>PERCENTILE.INC</c> /
/// NumPy default method), with <c>position = p/100 * (N - 1)</c>. For
/// <c>[1,2,3,4]</c> that gives <c>P90 = 3.7</c>; the nearest-rank method would give
/// <c>4.0</c>, and the tests below fail under exactly that mutation.
/// </para>
/// </summary>
public class StatisticsDirectTests
{
    private static readonly TimingSample[] NoSamples = [];

    /// <summary>
    /// The canonical four-value case: mean 2.5, unbiased sample variance
    /// <c>5/3</c> (Bessel's correction), median 2.5, and the type-7 percentiles.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.6/[1,2,3,4]")]
    public async Task Compute_OnOneToFour_MatchesTheHandComputedValues()
    {
        var statistics = Compute([1.0, 2.0, 3.0, 4.0]);

        Batch2Support.Log(
            $"avg={statistics.Avg} var={statistics.StdDev * statistics.StdDev} "
                + $"p50={statistics.P50} p90={statistics.P90} p95={statistics.P95} "
                + $"p99={statistics.P99} se={statistics.StandardError} rsd={statistics.RelativeStdDevPercent}"
        );

        await Assert.That(statistics.Avg).IsEqualTo(2.5);

        // Sample (not population) variance: sum of squared deviations (5) over N-1 (3).
        var variance = 5.0 / 3.0;
        await AssertNear(statistics.StdDev * statistics.StdDev, variance);
        await AssertNear(statistics.StdDev, Math.Sqrt(variance));

        await Assert.That(statistics.P50).IsEqualTo(2.5);
        await Assert.That(statistics.Min).IsEqualTo(1.0);
        await Assert.That(statistics.Max).IsEqualTo(4.0);

        // position = p/100 * (N-1) = 2.7 for P90 -> 3 * 0.3 + 4 * 0.7
        await AssertNear(statistics.P90, 3.7);
        // 2.85 -> 3 * 0.15 + 4 * 0.85
        await AssertNear(statistics.P95, 3.85);
        // 2.97 -> 3 * 0.03 + 4 * 0.97
        await AssertNear(statistics.P99, 3.97);

        await AssertNear(statistics.StandardError, Math.Sqrt(variance) / 2.0);
        await AssertNear(statistics.RelativeStdDevPercent, Math.Sqrt(variance) / 2.5 * 100.0);
    }

    /// <summary>A single sample is its own mean, median and every percentile; no spread.</summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.6/N=1")]
    public async Task Compute_WithOneSample_DegeneratesCleanly()
    {
        var statistics = Compute([42.0]);

        await Assert.That(statistics.Avg).IsEqualTo(42.0);
        await Assert.That(statistics.Min).IsEqualTo(42.0);
        await Assert.That(statistics.Max).IsEqualTo(42.0);
        await Assert.That(statistics.P50).IsEqualTo(42.0);
        await Assert.That(statistics.P90).IsEqualTo(42.0);
        await Assert.That(statistics.P95).IsEqualTo(42.0);
        await Assert.That(statistics.P99).IsEqualTo(42.0);
        await Assert.That(statistics.StdDev).IsZero();
        await Assert.That(statistics.StandardError).IsZero();
        await Assert.That(statistics.RelativeStdDevPercent).IsZero();
    }

    /// <summary>
    /// Two samples: the variance denominator is <c>N-1 = 1</c>, and the median sits
    /// halfway between them while P90/P99 interpolate towards the maximum.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.6/N=2")]
    public async Task Compute_WithTwoSamples_UsesBothDegreesOfFreedom()
    {
        var statistics = Compute([1.0, 3.0]);

        await Assert.That(statistics.Avg).IsEqualTo(2.0);
        await AssertNear(statistics.StdDev, Math.Sqrt(2.0));
        await AssertNear(statistics.StandardError, 1.0);
        await Assert.That(statistics.P50).IsEqualTo(2.0);
        await AssertNear(statistics.P90, 2.8);
        await AssertNear(statistics.P99, 2.98);
    }

    /// <summary>Four equal samples: zero variance, zero RSD, every percentile the same value.</summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.6/all equal")]
    public async Task Compute_WithEqualSamples_ReportsNoSpread()
    {
        var statistics = Compute([7.0, 7.0, 7.0, 7.0]);

        await Assert.That(statistics.Avg).IsEqualTo(7.0);
        await Assert.That(statistics.StdDev).IsZero();
        await Assert.That(statistics.StandardError).IsZero();
        await Assert.That(statistics.RelativeStdDevPercent).IsZero();
        await Assert.That(statistics.P50).IsEqualTo(7.0);
        await Assert.That(statistics.P90).IsEqualTo(7.0);
        await Assert.That(statistics.P99).IsEqualTo(7.0);
        await Assert.That(statistics.HasHighVariance).IsFalse();
    }

    /// <summary>
    /// All zeros: the RSD guard (<c>|mean| &lt; 1e-12</c>) keeps the ratio at 0
    /// instead of dividing by zero, and the percentiles stay at 0 — no NaN, no ∞.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.6/all zero")]
    public async Task Compute_WithAllZeroSamples_StaysFinite()
    {
        var statistics = Compute([0.0, 0.0, 0.0, 0.0]);

        await Assert.That(statistics.Avg).IsZero();
        await Assert.That(statistics.Min).IsZero();
        await Assert.That(statistics.Max).IsZero();
        await Assert.That(statistics.P50).IsZero();
        await Assert.That(statistics.P99).IsZero();
        await Assert.That(statistics.StdDev).IsZero();
        await Assert.That(statistics.RelativeStdDevPercent).IsZero();
        await Assert.That(double.IsNaN(statistics.RelativeStdDevPercent)).IsFalse();
        await Assert.That(double.IsInfinity(statistics.RelativeStdDevPercent)).IsFalse();
    }

    /// <summary>
    /// The documented consequence of type-7 interpolation at small N: with the ten
    /// samples of <c>BenchmarkConfig.Quick</c> (the README's CI preset) P99 sits at
    /// <c>9.91</c> for <c>1..10</c> — next to <c>Max = 10</c>, so the P95/P99 columns
    /// carry no tail information below twenty samples. That is a property of the
    /// definition, not a defect, and the README says so.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.6/small N and the tail")]
    public async Task Compute_AtQuickPresetSampleCount_P99IsCloseToMax()
    {
        var statistics = Compute([1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0]);

        Batch2Support.Log(
            $"p90={statistics.P90} p95={statistics.P95} p99={statistics.P99} max={statistics.Max}"
        );

        await AssertNear(statistics.P90, 9.1);
        await AssertNear(statistics.P95, 9.55);
        await AssertNear(statistics.P99, 9.91);
        await Assert.That(statistics.P99).IsLessThan(statistics.Max);
        await AssertNear(statistics.P99 - statistics.Max, -0.09);
    }

    /// <summary>
    /// The GC counters and the two resolution flags are aggregated from the samples,
    /// not from the per-operation values: a <c>null</c> counter stays "not collected"
    /// instead of turning into a zero collection count.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.6/aggregates")]
    public async Task Compute_AggregatesGcCountersAndResolutionFlags()
    {
        var samples = new[]
        {
            new TimingSample
            {
                ElapsedNanoseconds = 10,
                GcInfo = new GcInfo { Gen0 = 1, Gen1 = 0, Gen2 = 0 },
            },
            new TimingSample
            {
                ElapsedNanoseconds = 10,
                GcInfo = new GcInfo
                {
                    Gen0 = 2,
                    Gen1 = 1,
                    Gen2 = 0,
                    IsApproximate = true,
                },
            },
        };

        var statistics = StatisticsCalculator.Compute([5.0, 5.0], [0.0, 0.0], samples);

        await Assert.That(statistics.GcInfo).IsNotNull();
        await Assert.That(statistics.GcInfo!.Gen0).IsEqualTo(3);
        await Assert.That(statistics.GcInfo.Gen1).IsEqualTo(1);
        await Assert.That(statistics.GcInfo.IsApproximate).IsTrue();
        await Assert.That(statistics.Unmeasured).IsFalse();

        // A sample the clock never advanced for marks the run as unmeasured.
        var frozen = StatisticsCalculator.Compute(
            [0.0, 0.0],
            [0.0, 0.0],
            [new TimingSample { ElapsedNanoseconds = 0, GcInfo = null }]
        );
        await Assert.That(frozen.Unmeasured).IsTrue();
        await Assert.That(frozen.GcInfo).IsNull();
    }

    // ─── helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Asserts two doubles are the same number up to the last bits: the calculator
    /// works in <see cref="double"/>, so <c>sqrt(x) * sqrt(x)</c> is <c>x</c> only to
    /// within one rounding, and an exact comparison here would pin the hardware's
    /// rounding instead of the formula.
    /// </summary>
    private static async Task AssertNear(double actual, double expected)
    {
        await Assert
            .That(Math.Abs(actual - expected))
            .IsLessThanOrEqualTo(1e-12)
            .Because($"expected {expected:R}, got {actual:R}");
    }

    /// <summary>
    /// Runs the internal calculator over fixed per-operation values, with the cycle
    /// array the same length (it is required to match) and no raw samples.
    /// </summary>
    private static Statistics Compute(double[] perOpTimes) =>
        StatisticsCalculator.Compute(
            perOpTimes,
            new double[perOpTimes.Length],
            NoSamples
        );
}
