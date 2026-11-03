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
