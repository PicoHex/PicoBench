namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · 0.0 — "known cost" net, part 3: the semantics of the numbers.
/// These three tests pin the acceptance criteria of Batch 0 · 0.2 (neutral
/// self-comparison), 0.3 (<c>CpuOnly</c> must not report a measured zero) and
/// 0.8 (teardown must run when an iteration throws).
/// </summary>
public class MeasurementSemanticsTests
{
    private static int _compareSink;

    /// <summary>
    /// Batch 0 · 0.2 — comparing a delegate with itself must be neutral:
    /// <c>Speedup ∈ [0.9, 1.1]</c> and <c>IsFaster</c> must never be true.
    /// <para>Expected at HEAD: <b>RED</b> — 10 identical comparisons produced
    /// <c>IsFaster = true</c> 5 times out of 10 with speedups 0.90-1.24
    /// (plus a JIT-cold 3.9 outlier), i.e. B1-C1 "noise becomes a conclusion".</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.2")]
    [NotInParallel] // timing test: keep the core to ourselves
    public async Task Compare_SelfComparison_IsNeutral()
    {
        var config = Batch0Support.UncalibratedConfig(sampleCount: 10, iterationsPerSample: 500);
        Action work = static () => _compareSink++;

        const int trials = 10;
        var speedups = new List<double>(trials);
        var flipped = new List<int>();

        for (var trial = 0; trial < trials; trial++)
        {
            var comparison = Benchmark.Compare("self", "A", work, "B", work, config);
            speedups.Add(comparison.Speedup);
            if (comparison.IsFaster)
                flipped.Add(trial);

            Batch0Support.Log(
                $"trial {trial}: speedup={comparison.Speedup:F4} isFaster={comparison.IsFaster} "
                    + $"baseline={comparison.Baseline.Statistics.Avg:F3} ns candidate={comparison.Candidate.Statistics.Avg:F3} ns"
            );
        }

        var min = speedups.Min();
        var max = speedups.Max();
        Batch0Support.Log(
            $"speedups: [{string.Join(", ", speedups.Select(static s => s.ToString("F3", CultureInfo.InvariantCulture)))}]"
        );
        Batch0Support.Log(
            $"range [{min:F3}, {max:F3}] (allowed [0.900, 1.100]); IsFaster true {flipped.Count}/{trials} times"
        );

        // The core of B1-C1: with no real difference, no run may claim one.
        await Assert.That(flipped.Count).IsEqualTo(0);

        await Assert.That(min).IsGreaterThanOrEqualTo(0.9);
        await Assert.That(max).IsLessThanOrEqualTo(1.1);
    }

    /// <summary>
    /// Batch 0 · 0.3 — <c>CpuOnly</c> with the default
    /// (<see cref="BenchmarkConfig.AutoCalibrateIterations"/> = false) measures
    /// <see cref="Process.TotalProcessorTime"/>, whose Windows granularity is
    /// ~15.6 ms. A pure I/O wait therefore yields <c>cpuDelta == 0</c> samples.
    /// Such a sample must not be presented as "0 ns/op measured": either
    /// <c>P50</c>/<c>Min</c> stay above zero, or the result carries an explicit
    /// <c>Unmeasured</c> style signal.
    /// <para>Expected at HEAD: <b>RED</b> — 8 of 12 samples were exactly 0,
    /// giving <c>Avg = 6.5 ms</c>, <c>Min = 0</c>, <c>P50 = 0</c>, RSD 160%,
    /// with no signal anywhere in <see cref="Statistics"/> (B1-C3 / K1).</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.3")]
    [NotInParallel] // reads process-wide CPU time
    public async Task CpuOnly_Unmeasured_IsNotZero()
    {
        var config = new BenchmarkConfig
        {
            WarmupIterations = 0,
            SampleCount = 12,
            IterationsPerSample = 1,
            AutoCalibrateIterations = false, // the default, and the broken path
            ForceGcBeforeBenchmark = false,
            TimingMode = AsyncTimingMode.CpuOnly,
            RetainSamples = true,
        };

        var result = await Benchmark.RunAsync(
            "cpu-only-io-wait",
            async () => await Task.Delay(20),
            warmup: null,
            config: config
        );

        var statistics = result.Statistics;
        var samples = result.Samples ?? [];
        var unmeasuredSamples = samples.Count(static s => s.ElapsedNanoseconds <= 0);
        var signalled = Batch0Support.HasResolutionLimitedSignal(
            statistics,
            out var signalDetail
        );

        Batch0Support.Log(
            $"per-sample ns: [{string.Join(", ", samples.Select(static s => s.ElapsedNanoseconds.ToString("F0", CultureInfo.InvariantCulture)))}]"
        );
        Batch0Support.Log(
            $"avg={statistics.Avg:F3} min={statistics.Min:F3} p50={statistics.P50:F3} max={statistics.Max:F3} "
                + $"sd={statistics.StdDev:F3} rsd={statistics.RelativeStdDevPercent:F1}%"
        );
        Batch0Support.Log(
            $"{unmeasuredSamples}/{samples.Count} samples read exactly 0 CPU time; {signalDetail}"
        );

        if (unmeasuredSamples == 0 || signalled)
            return; // either the clock advanced, or the library flags the result as unmeasured

        // No signal: the reported quantiles must not pretend "0 ns measured".
        await Assert.That(statistics.Min).IsGreaterThan(0);
        await Assert.That(statistics.P50).IsGreaterThan(0);
    }

    /// <summary>
    /// Batch 0 · 0.8 — teardown must run even when an iteration throws
    /// (<c>try/finally</c> around the timing loop). The exception itself must
    /// still propagate and the loop must stop at the throwing iteration.
    /// <para>Expected at HEAD: <b>RED</b> — <see cref="Runner.Time(int, Action, Action?, Action?)"/>
    /// runs the loop, then measures GC, then calls teardown; an exception at
    /// iteration 50 skips teardown entirely (observed teardown calls = 0).</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.8")]
    public async Task Teardown_RunsWhenIterationThrows()
    {
        const int iterations = 100;
        const int throwingIteration = 50;

        var invoked = 0;
        var teardownCalls = 0;
        var setupCalls = 0;
        Exception? caught = null;

        try
        {
            Runner.Time(
                iterations,
                () =>
                {
                    invoked++;
                    if (invoked == throwingIteration)
                        throw new InvalidOperationException("iteration 50 failed");
                },
                setup: () => setupCalls++,
                teardown: () => teardownCalls++
            );
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        Batch0Support.Log(
            $"setups={setupCalls} iterations={invoked} teardowns={teardownCalls} caught={caught?.Message ?? "<none>"}"
        );

        // The failure must not be swallowed.
        await Assert.That(caught).IsNotNull();
        await Assert.That(invoked).IsEqualTo(throwingIteration);
        await Assert.That(setupCalls).IsEqualTo(1);

        // Batch 0 · 0.8: the teardown is the cleanup contract; it must run once.
        await Assert.That(teardownCalls).IsEqualTo(1);
    }
}
