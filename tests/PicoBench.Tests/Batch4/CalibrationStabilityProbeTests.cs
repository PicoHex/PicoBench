using PicoBench.Tests.Batch0;

namespace PicoBench.Tests.Batch4;

/// <summary>
/// Batch 4 · T2 — auto-calibration stability probe (S1).
/// <para>
/// Probe written <b>before</b> any fix, to reproduce the reported defect: the
/// <see cref="BenchmarkConfig.Quick"/> preset enables auto-calibration with
/// <c>MinSampleTime = 0.25 ms</c>, and the calibration search walks the
/// iteration count multiplicatively (×2..×10 per step) until a <i>single</i>
/// probe passes that target. A workload costing tens of nanoseconds per
/// operation therefore lands wherever the overshoot of one step put it — the
/// same workload was reported as 2000 / 4346 / 8850 iterations over three runs,
/// i.e. 53-159 ns/op with an rsd of 98-99%.
/// </para>
/// <para>
/// This probe only observes and prints (through <see cref="Batch0Support.Log"/>,
/// visible at <c>--log-level trace</c>): for several workload costs, the
/// resolved <c>IterationsPerSample</c> of five consecutive runs of one workload,
/// with per-op time, RSD and the max/min spread. S3 asserts the same
/// measurement.
/// </para>
/// </summary>
public class CalibrationStabilityProbeTests
{
    /// <summary>
    /// Workload costs (nanoseconds per operation) swept by the probe. Anything
    /// under ~250 ns/op misses the Quick preset's 0.25 ms budget at the preset's
    /// own 1000 iterations, which is what forces the search to grow.
    /// </summary>
    internal static readonly int[] SpinWaits = [1, 2, 5, 20];

    /// <summary>The workload; <c>Thread.SpinWait</c> is a known CPU cost.</summary>
    internal static void Workload(int spins) => Thread.SpinWait(spins);

    internal static readonly BenchmarkConfig Uncalibrated = new()
    {
        WarmupIterations = 0,
        SampleCount = 3,
        IterationsPerSample = 1000,
        AutoCalibrateIterations = false,
        ForceGcBeforeBenchmark = false,
    };

    /// <summary>
    /// Runs one workload <paramref name="runs"/> times and returns the resolved
    /// iteration count and result of every run.
    /// </summary>
    internal static (int[] Iterations, BenchmarkResult[] Results) Repeat(
        BenchmarkConfig config,
        int spins,
        int runs
    )
    {
        var iterations = new int[runs];
        var results = new BenchmarkResult[runs];

        for (var i = 0; i < runs; i++)
        {
            var result = Benchmark.Run(
                $"probe-{spins}",
                () => Workload(spins),
                warmup: () => Workload(spins),
                config: config
            );
            results[i] = result;
            iterations[i] = result.IterationsPerSample;
        }

        return (iterations, results);
    }

    /// <summary>max/min of the resolved iteration counts; 1.0 means deterministic.</summary>
    internal static double Spread(int[] iterations)
    {
        var min = iterations.Min();
        var max = iterations.Max();
        return min <= 0 ? double.PositiveInfinity : (double)max / min;
    }

    /// <summary>Per-op cost of a workload at a fixed iteration count (uncalibrated).</summary>
    internal static BenchmarkResult Cost(BenchmarkConfig config, int spins) =>
        Benchmark.Run(
            $"cost-{spins}",
            () => Workload(spins),
            warmup: null,
            config: config
        );

    [Test]
    [Property("Category", "Batch4")]
    [Property("Batch4", "T2 probe")]
    [NotInParallel] // timing probe: no interference from other tests
    public async Task QuickPreset_IterationsSpread_IsLogged()
    {
        const int runs = 5;

        foreach (var spins in SpinWaits)
        {
            var cost = Cost(Uncalibrated, spins);
            Batch0Support.Log(
                $"workload SpinWait({spins}) uncalibrated: {cost.Statistics.Avg:F1} ns/op, "
                    + $"rsd={cost.Statistics.RelativeStdDevPercent:F2}%"
            );

            var (iterations, results) = Repeat(BenchmarkConfig.Quick, spins, runs);
            for (var i = 0; i < runs; i++)
            {
                Batch0Support.Log(
                    $"  SpinWait({spins}) run {i}: IterationsPerSample={iterations[i]}, "
                        + $"per-op={results[i].Statistics.Avg:F1} ns/op, "
                        + $"rsd={results[i].Statistics.RelativeStdDevPercent:F2}%, "
                        + $"resolutionLimited={results[i].Statistics.ResolutionLimited}"
                );
            }

            Batch0Support.Log(
                $"  SpinWait({spins}) Quick preset spread over {runs} runs: min={iterations.Min()}, "
                    + $"max={iterations.Max()}, max/min={Spread(iterations):F2}x"
            );
        }

        await Assert.That(SpinWaits).IsNotEmpty();
    }
}
