namespace PicoBench.Tests.Batch4;

/// <summary>
/// Batch 4 · T2 — the S3 assertion for the auto-calibration reproducibility fix.
/// <para>
/// The S1 probe (<see cref="CalibrationStabilityProbeTests"/>) only observes;
/// this test asserts the same measurement: five consecutive runs of one workload
/// under <see cref="BenchmarkConfig.Quick"/> must resolve the <i>same</i> iteration
/// count. Before the fix the preset enabled auto-calibration, whose multiplicative
/// search stopped wherever a single probe overshot <c>MinSampleTime</c>, so the
/// resolved count varied by up to 2.4x across runs of one workload. The fix pins
/// the preset's own <c>IterationsPerSample</c>.
/// </para>
/// <para>
/// The tolerance is 1.5x rather than 1.0x so the test guards the <i>defect</i>
/// (a search that lands on a different count every run) and not an exact constant:
/// it is red at the old code (2.0-2.4x) and green at the deterministic preset.
/// </para>
/// </summary>
public class CalibrationStabilityTests
{
    /// <summary>Runs per workload; matches the S1 probe.</summary>
    private const int Runs = 5;

    /// <summary>Upper bound for max/min of the resolved counts across runs.</summary>
    private const double MaxSpread = 1.5;

    /// <summary>
    /// Workloads whose per-op cost is far below the Quick preset's 0.25 ms budget
    /// at the preset's own 1000 iterations, i.e. exactly the cases the old
    /// calibration search had to grow — and therefore the cases it got wrong.
    /// </summary>
    public static IEnumerable<int> LowCostWorkloads() => [1, 2, 5];

    [Test]
    [Property("Category", "Batch4")]
    [Property("Batch4", "T2 stability")]
    [MethodDataSource(nameof(LowCostWorkloads))]
    [NotInParallel] // timing probe: no interference from other tests
    public async Task QuickPreset_ResolvesSameIterations_AcrossRuns(int spins)
    {
        var (iterations, _) = CalibrationStabilityProbeTests.Repeat(
            BenchmarkConfig.Quick,
            spins,
            Runs
        );

        var spread = CalibrationStabilityProbeTests.Spread(iterations);

        await Assert
            .That(spread)
            .IsLessThanOrEqualTo(MaxSpread)
            .Because(
                $"Quick preset resolved {string.Join("/", iterations)} for SpinWait({spins}) "
                    + $"over {Runs} runs (max/min={spread:F2}x); a preset must be reproducible"
            );
    }

    /// <summary>
    /// The preset itself must not ask for auto-calibration: that is the root
    /// cause, and pinning it here keeps a future edit from silently re-enabling it.
    /// </summary>
    [Test]
    [Property("Category", "Batch4")]
    [Property("Batch4", "T2 stability")]
    public async Task Presets_DoNotAutoCalibrate()
    {
        await Assert.That(BenchmarkConfig.Quick.AutoCalibrateIterations).IsFalse();
        await Assert.That(BenchmarkConfig.Precise.AutoCalibrateIterations).IsFalse();
    }
}
