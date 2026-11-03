namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · 0.0 — positive controls ("known cost" anchors) that the current
/// implementation already satisfies. They exist so that the Batch 0 fixes
/// cannot be "paid for" by breaking measurement that works today: B4's
/// mutation experiments M3 (cycles forced to 0) and M2 (GC counts forced to 0)
/// passed all 559 tests at HEAD, so these two anchors are the ones that would
/// have caught them.
/// </summary>
public class KnownCostAnchorTests
{
    // Keeps the allocation alive so the JIT cannot elide it.
    private static byte[]? _allocationSink;

    /// <summary>
    /// Batch 0 · 0.0 (B4 mutation M1 guard) — a <b>known absolute</b> cost: the
    /// library's wall-clock reading must agree with an independent
    /// <see cref="Stopwatch"/> wrapped around the same call.
    /// <para>Why this test exists: B4's M1 mutation (<c>elapsedNs * 10</c>)
    /// passed all 559 tests at HEAD, because every timing assertion is either
    /// scale-invariant (per-op ratios, quantiles) or a tautology. This is the
    /// only kind of assertion that can catch a pure scale factor — and the
    /// mutation lab confirmed none of the other ten Batch 0 tests does.</para>
    /// <para>Expected at HEAD: <b>GREEN</b> (ratio ≈ 1.00). Under M1 the ratio
    /// becomes ≈10 and the test turns red; that is its whole job.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0 (M1 guard)")]
    [NotInParallel] // wall-clock anchor: avoid interference from other tests
    public async Task WallClock_AgreesWithIndependentStopwatch()
    {
        const int sleepMilliseconds = 20;

        // Independent clock, taken around exactly the same call.
        var watch = Stopwatch.StartNew();
        var sample = Runner.Time(1, () => Thread.Sleep(sleepMilliseconds));
        watch.Stop();

        var independent = watch.Elapsed.TotalMilliseconds;
        var reported = sample.ElapsedMilliseconds;
        var ratio = independent <= 0 ? double.PositiveInfinity : reported / independent;

        Batch0Support.Log(
            $"Thread.Sleep({sleepMilliseconds}): independent={independent:F3} ms, library={reported:F3} ms "
                + $"({sample.ElapsedNanoseconds:F0} ns, {sample.ElapsedTicks} ticks), ratio={ratio:F3}"
        );

        // The interval has to be long enough for the comparison to mean anything
        // (Windows rounds a 5 ms sleep up to its ~15.6 ms timer tick).
        await Assert.That(independent).IsGreaterThanOrEqualTo(4.0);

        // Same interval, two clocks: a 2x band is generous and still kills any
        // uniform scale factor applied to the reported value.
        await Assert.That(ratio).IsGreaterThanOrEqualTo(0.5);
        await Assert.That(ratio).IsLessThanOrEqualTo(2.0);
    }

    /// <summary>
    /// Batch 0 · 0.1 anchor — <c>Thread.SpinWait</c> is a known CPU cost, so
    /// total cycles must grow with the iteration count and cycles-per-op must
    /// stay roughly constant (wide band: 3×).
    /// <para>Expected at HEAD: <b>GREEN</b> (measured 8.4-9.3 k cycles/op at
    /// 1e3 / 1e4 / 1e5 iterations, max/min ≤ 1.25). Mutation M3
    /// (<c>GetCpuCycles()</c> → <c>return 0</c>) turns it red.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.1")]
    [NotInParallel] // reads process-wide cycle counters
    public async Task SpinWait_IsLinearInIterations()
    {
        var environment = new EnvironmentInfo();
        var useCycles = environment.CpuCyclesAreMeaningful;

        Batch0Support.Log(
            $"cycle source = {environment.CpuCycleMeasurement} (meaningful={useCycles}); falling back to ns/op when unavailable"
        );

        var iterationCounts = new[] { 1_000, 10_000, 100_000 };
        var totals = new List<double>(iterationCounts.Length);
        var perOps = new List<double>(iterationCounts.Length);

        foreach (var iterations in iterationCounts)
        {
            var sample = Runner.Time(iterations, static () => Thread.SpinWait(100));

            // B4's mutation M3 zeroed the cycle counter and all 559 tests stayed
            // green. The metric must actually move when the platform claims a
            // meaningful cycle source; falling back to ns would hide that.
            if (useCycles)
                await Assert.That(sample.CpuCycles).IsGreaterThan(0UL);

            var total = useCycles && sample.CpuCycles > 0
                ? sample.CpuCycles
                : sample.ElapsedNanoseconds;
            totals.Add(total);
            perOps.Add(total / iterations);

            Batch0Support.Log(
                $"iters={iterations,7}: cycles={sample.CpuCycles,12} ns={sample.ElapsedNanoseconds,14:F0} "
                    + $"per-op={(total / iterations),10:F2}"
            );
        }

        // 1) The total cost must grow with the iteration count.
        for (var i = 1; i < totals.Count; i++)
        {
            await Assert.That(totals[i]).IsGreaterThan(totals[i - 1]);
        }

        // 2) Per-op cost must stay in the same order of magnitude (linear in N).
        var min = perOps.Min();
        var max = perOps.Max();
        await Assert.That(min).IsGreaterThan(0);
        await Assert.That(max / min).IsLessThanOrEqualTo(3.0);
    }

    /// <summary>
    /// Batch 0 · 0.1 anchor — allocation must map to Gen0 collections:
    /// an allocation-free loop reports Gen0 == 0, and crossing the Gen0 budget
    /// (≥8 MiB, using 16 MiB for margin) must report Gen0 &gt; 0.
    /// <para>Expected at HEAD: <b>GREEN</b> (0 B → Gen0 = 0 five times out of
    /// five; 16 MiB → Gen0 = 2). Mutation M2 (GC counts forced to 0) turns the
    /// second half red.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.1")]
    [NotInParallel] // reads process-wide GC counters
    public async Task Allocation_MapsToGen0()
    {
        const int allocationIterations = 16_384;
        const int allocationSizeBytes = 1024; // 16384 × 1 KiB = 16 MiB

        var clean = Runner.Time(10_000, static () => { });
        Batch0Support.Log(
            $"no allocation: Gen0={clean.GcInfo?.Gen0} Gen1={clean.GcInfo?.Gen1} Gen2={clean.GcInfo?.Gen2}"
        );
        await Assert.That(clean.GcInfo).IsNotNull();
        await Assert.That(clean.GcInfo!.Gen0).IsEqualTo(0);

        var allocating = Runner.Time(
            allocationIterations,
            static () => _allocationSink = new byte[allocationSizeBytes]
        );
        var allocatedMegabytes =
            (double)allocationIterations * allocationSizeBytes / (1024 * 1024);
        Batch0Support.Log(
            $"allocated {allocatedMegabytes:F1} MiB over {allocationIterations} iterations: "
                + $"Gen0={allocating.GcInfo?.Gen0} Gen1={allocating.GcInfo?.Gen1} Gen2={allocating.GcInfo?.Gen2}"
        );

        await Assert.That(allocating.GcInfo).IsNotNull();
        await Assert.That(allocating.GcInfo!.Gen0).IsGreaterThan(0);
    }
}
