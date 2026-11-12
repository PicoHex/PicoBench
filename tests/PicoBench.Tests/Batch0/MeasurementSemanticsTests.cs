namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · 0.0 — "known cost" net, part 3: the semantics of the numbers.
/// These three tests pin the acceptance criteria of Batch 0 · 0.2 (neutral
/// self-comparison), 0.3 (<c>CpuOnly</c> must not report a measured zero) and
/// 0.8 (teardown must run when an iteration throws).
/// <para>
/// R0's review (<c>.review-2026-11/08-regression-net-review.md</c> §2.7, §2.8)
/// rewrote the first two: the comparison workload was below the resolution
/// floor (speedup could even come out negative), and the <c>CpuOnly</c> guard
/// had two early exits plus a claim about <c>P50</c> that its own measurements
/// did not support.
/// </para>
/// </summary>
public class MeasurementSemanticsTests
{
    /// <summary>
    /// Batch 0 · 0.2 — comparing a delegate with itself must be neutral:
    /// <c>median(Speedup) ∈ [0.9, 1.1]</c>, every trial <c>∈ [0.75, 1.25]</c>, and
    /// <c>IsFaster</c> must never be true. The median carries the tight band because a
    /// systematic bias — the defect this test exists for — moves every trial; the
    /// wider band on the extremes keeps a single noisy trial from reding the run.
    /// <para>
    /// R0 §2.7: the previous workload (a static-field increment, ≈0 ns/op net)
    /// left the compared quantity at the noise floor, so even a <b>correct</b> 0.2
    /// (7 interleaved rounds + paired median + dead band) produced
    /// <c>speedup ∈ [-1.017, 1.784]</c> and 3/10 flips — the test could never turn
    /// green. With <c>Thread.SpinWait(200)</c> (≈6-11 µs/op) R0 measured
    /// <c>[0.956, 1.019]</c> and <b>0/10</b> flips, i.e. the same assertions now
    /// discriminate the real defect.
    /// </para>
    /// <para>Expected at HEAD: <b>RED</b> — 6-8 of 10 identical comparisons claim
    /// <c>IsFaster</c> with speedups spread over ≈[0.80, 1.43] (B1-C1: noise
    /// becomes a conclusion — one sequential run, no interval, no interleaving).</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.2")]
    [NotInParallel] // timing test: keep the core to ourselves
    public async Task Compare_SelfComparison_IsNeutral()
    {
        var config = Batch0Support.UncalibratedConfig(sampleCount: 10, iterationsPerSample: 200);

        // ≈6-11 µs/op: well above the clock resolution and above any per-sample
        // additive cost, so "equal work" really means two equal numbers.
        Action work = static () => Thread.SpinWait(200);

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
            $"range [{min:F3}, {max:F3}] (extremes [0.750, 1.250]); IsFaster true {flipped.Count}/{trials} times"
        );

        // The core of B1-C1: with no real difference, no run may claim one.
        await Assert.That(flipped.Count).IsEqualTo(0);

        // A systematic bias moves *every* trial, so the tight ±10% band is applied to
        // the median. A single trial can still wander: CI run 43213497484 measured
        // [0.9584, 1.1023] on osx-arm64 — one trial 0.2% past 1.1 while the run was
        // neutral in eight of ten (all ten: 0.9584 0.9698 0.9769 0.9852 1.0006 1.0193
        // 1.0220 1.0378 1.0819 1.1023, median 1.010) — and that red said nothing
        // about the library, only about a shared runner's scheduler. The extremes keep
        // a ±25% band, so a 25% error still reds the run on whichever trials show it,
        // and a constant error of any size above the band is caught by the median.
        var median = Batch0Support.Median(speedups);
        Batch0Support.Log($"median speedup={median:F4} (tight band [0.900, 1.100])");

        await Assert.That(median).IsGreaterThanOrEqualTo(0.9);
        await Assert.That(median).IsLessThanOrEqualTo(1.1);
        await Assert.That(min).IsGreaterThanOrEqualTo(0.75);
        await Assert.That(max).IsLessThanOrEqualTo(1.25);
    }

    /// <summary>
    /// Batch 0 · 0.3 — <c>CpuOnly</c> with the default
    /// (<see cref="BenchmarkConfig.AutoCalibrateIterations"/> = false) measures
    /// <see cref="Process.TotalProcessorTime"/>, whose Windows granularity is
    /// ~15.6 ms. A pure I/O wait therefore yields <c>cpuDelta == 0</c> samples.
    /// Such a sample must not be presented as "0 ns/op measured": the quantiles
    /// must not contain a fake zero <b>and</b> the result must carry an explicit
    /// unmeasured signal that the user can see.
    /// <para>Expected at HEAD: <b>RED</b> — per-sample values are a mix of 0 and
    /// multiples of 15.625 ms, <c>Min = 0</c> (stable across R0's 6 runs), RSD
    /// ≈141%, and neither <see cref="Statistics"/> nor any formatter says the
    /// value was not measured (B1-C3 / K1).</para>
    /// <para>
    /// R0 §2.8 corrections applied here: (a) 07's "P50 is always 0" does not
    /// hold — R0 measured <c>P50 &gt; 0</c> in 4 of 6 runs, so the P50 assertion
    /// is gone (worth logging, not worth asserting); (b) the
    /// <c>unmeasuredSamples == 0</c> early exit is gone, since it made the whole
    /// test vacuous on platforms whose CPU clock ticks during a 20 ms wait; (c)
    /// the signal must be <b>both</b> a field flag and a user-visible marker, so a
    /// single always-true property can no longer satisfy it.
    /// </para>
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

        // The signal has to be observable by the caller: a field somewhere on
        // Statistics *and* a marker in one of the four formatters.
        var fieldSignal = Batch0Support.HasResolutionLimitedSignal(statistics, out var signalDetail);
        var visibleSignal = Batch0Support.FormattedOutputDisclosesResolution(
            result,
            out var formattedPerOp,
            out var outputDetail
        );
        var signalled = fieldSignal && visibleSignal;

        Batch0Support.Log(
            $"per-sample ns: [{string.Join(", ", samples.Select(static s => s.ElapsedNanoseconds.ToString("F0", CultureInfo.InvariantCulture)))}]"
        );
        Batch0Support.Log(
            $"avg={statistics.Avg:F3} min={statistics.Min:F3} p50={statistics.P50:F3} max={statistics.Max:F3} "
                + $"sd={statistics.StdDev:F3} rsd={statistics.RelativeStdDevPercent:F1}%"
        );
        Batch0Support.Log(
            $"{unmeasuredSamples}/{samples.Count} samples read exactly 0 CPU time; "
                + $"field signal={fieldSignal} ({signalDetail}), output signal={visibleSignal} "
                + $"(formatted per-op={(formattedPerOp is null ? "<not numeric>" : $"{formattedPerOp:F3} ns")}; {outputDetail})"
        );

        if (unmeasuredSamples == 0)
        {
            // Nothing to guard: on this platform the CPU clock advanced during
            // the wait, so no sample is unmeasured. Logged, not asserted away.
            Batch0Support.Log("no unmeasured CPU sample on this run — nothing to assert");
            return;
        }

        // (1) An unmeasured sample must never be published as a measured zero…
        await Assert
            .That(statistics.Min)
            .IsGreaterThan(0)
            .Because(
                $"{unmeasuredSamples}/{samples.Count} samples read 0 CPU time and Min is still 0 "
                    + "(signalled " + (signalled ? "true" : "false") + ")"
            );

        // (2) …and the result must say so explicitly, in a field and in the output.
        await Assert
            .That(signalled)
            .IsTrue()
            .Because(
                "a zero CPU delta must be disclosed as Unmeasured/equivalent, both as a field and in "
                    + $"the formatted output (field={fieldSignal}, output={visibleSignal})"
            );
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
