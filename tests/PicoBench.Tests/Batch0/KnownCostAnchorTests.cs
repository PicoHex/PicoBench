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

        // Same interval, two clocks. The band is one-sided by physics: the
        // independent Stopwatch necessarily encloses the library's window (it
        // also covers the GC baseline, the cycle reads and the Stopwatch start),
        // so a correct implementation reports ratio structurally <= 1 — R0
        // measured 0.950-0.972 with CV 0.93% over 6 runs.
        // The upper bound used to be 2.0, which let a x1.5 scale error through
        // (R0: ratio 1.391 -> green). 1.10 keeps 1.13x margin over the measured
        // worst case (0.972) and still reds the M1 mutation (x10 -> 9.9) and the
        // x1.5 mutation (-> 1.39).
        await Assert.That(ratio).IsGreaterThanOrEqualTo(0.5);
        await Assert.That(ratio).IsLessThanOrEqualTo(1.10);
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
    /// Batch 0 · 0.1 anchor — a run that allocates must report its allocation, and a
    /// run that does not must report zero.
    /// <para>
    /// The <b>primary</b> evidence is the exact thread-scoped byte count
    /// (<c>GcInfo.AllocatedBytes</c>, added with T3): it moves with the measured work
    /// on every platform. <c>GcInfo.Gen0</c> is still reported, but only
    /// lower-bounded, because a generation-0 collection is a property of the
    /// <b>ambient gen0 budget</b>, not of the allocation volume.
    /// </para>
    /// <para>
    /// Why this changed (CI run 43213497484, 2026-11-12): the old form required
    /// <c>Gen0 &gt; 0</c> after a 16 MiB loop and went red on <b>win-x64, win-arm64,
    /// linux-arm64 and osx-arm64</b> with "allocated 16.0 MiB over 16384 iterations:
    /// Gen0=0 Gen1=0 Gen2=0" — the allocation happened, the collection did not. The
    /// runtime sizes the gen0 budget from the L2 cache (a bigger cache buys a bigger
    /// budget) and, under server GC, from the core count, so 16 MiB of garbage is not
    /// guaranteed to buy a collection on any machine. Reproduced on the reference
    /// machine by raising the budget with <c>DOTNET_GCgen0size</c>: default (7.2 MiB
    /// effective budget) → 16 MiB loop: Gen0=2, AllocatedBytes=16.4 MiB; 256 MiB
    /// budget → Gen0=0 at 16 MiB, Gen0=1 at 64 MiB; 1 GiB budget → Gen0=0 even at
    /// 64 MiB. That last line is why "just allocate more" is not the fix: the budget
    /// has no upper bound, the byte counter is the fact.
    /// </para>
    /// <para>
    /// The GC <b>counts</b> are still guarded, but <b>deterministically</b>: a window
    /// that forces one generation-0 collection must report it. That is the assertion
    /// mutation M2 (GC counts forced to 0) has to survive, and no budget, GC flavour
    /// or quiet machine can make it vacuous.
    /// </para>
    /// <para>
    /// Reverse mutation (measured): make the measured delegate allocate nothing
    /// (<c>static () =&gt; { }</c>) — the test turns red on the 90% volume floor of
    /// assertion (2), which reported 40 B (this process's own timing state) against the
    /// 16 MiB the loop asked for. Charging a running process's state to the benchmark is
    /// exactly the failure that a bare <c>&gt; 0</c> would wave through.
    /// </para>
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
            $"no allocation: Gen0={clean.GcInfo?.Gen0} Gen1={clean.GcInfo?.Gen1} "
                + $"Gen2={clean.GcInfo?.Gen2} allocatedBytes={clean.GcInfo?.AllocatedBytes}"
        );
        await Assert.That(clean.GcInfo).IsNotNull();
        await Assert.That(clean.GcInfo!.Gen0).IsEqualTo(0);

        // (1) The negative control, read through the same counter: no measured work, no
        // bytes of consequence. Not *exactly* zero — the first sample of a process can
        // carry a few hundred bytes of lazily initialised timing/GC state inside its own
        // window (184 B measured on the reference machine) — but four orders of magnitude
        // below the 16 MiB the other window asks for, which is the contrast that makes
        // assertion (2) meaningful.
        await Assert.That(clean.GcInfo.AllocatedBytes).IsNotNull();
        await Assert
            .That(clean.GcInfo.AllocatedBytes!.Value)
            .IsLessThanOrEqualTo(4_096L)
            .Because(
                "10 000 iterations of an empty delegate allocated "
                    + $"{clean.GcInfo.AllocatedBytes} B; anything above a few KiB means the "
                    + "counter is charging things this loop did not do"
            );

        var allocating = Runner.Time(
            allocationIterations,
            static () => _allocationSink = new byte[allocationSizeBytes]
        );
        var expectedBytes = (long)allocationIterations * allocationSizeBytes;
        var expectedMegabytes = expectedBytes / (1024.0 * 1024.0);
        var measuredBytes = allocating.GcInfo?.AllocatedBytes;
        var measuredMegabytes =
            measuredBytes is { } bytes
                ? (bytes / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture)
                : "N/A";
        Batch0Support.Log(
            $"allocated {expectedMegabytes:F1} MiB over {allocationIterations} iterations: "
                + $"AllocatedBytes={measuredBytes?.ToString(CultureInfo.InvariantCulture) ?? "N/A"} "
                + $"({measuredMegabytes} MiB) Gen0={allocating.GcInfo?.Gen0} "
                + $"Gen1={allocating.GcInfo?.Gen1} Gen2={allocating.GcInfo?.Gen2}"
        );

        await Assert.That(allocating.GcInfo).IsNotNull();

        // (2) The fact under test: the measured work allocated. Exact, thread-scoped,
        // and independent of GC flavour, core count and heap geometry.
        await Assert
            .That(measuredBytes)
            .IsNotNull()
            .Because(
                "GcInfo.AllocatedBytes is the primary evidence of this test; N/A means the "
                    + "runtime exposed no counter and the allocation claim is unproven"
            );
        await Assert
            .That(measuredBytes!.Value)
            .IsGreaterThan(0L)
            .Because(
                $"{allocationIterations} iterations of `_allocationSink = new byte[{allocationSizeBytes}]` "
                    + "must show up in the byte counter: this is the assertion a measured delegate "
                    + "that no longer allocates has to fail"
            );

        // …and it is the volume that was asked for, not a stray byte: the counter also
        // sees this thread's other allocations, so it can over-count but never
        // under-count. A 90% floor therefore cannot flake, while a stuck or partial
        // counter still fails.
        await Assert
            .That(measuredBytes.Value)
            .IsGreaterThanOrEqualTo(expectedBytes * 9 / 10)
            .Because(
                $"the loop asked for {expectedMegabytes:F1} MiB; a counter reporting less than "
                    + "90% of that is not measuring this loop"
            );

        // (3) Gen0 is reported, not required: zero is legal whenever the ambient gen0
        // budget is bigger than the loop (see the doc comment). It is logged next to
        // the byte count so CI shows both, and a broken-counts bug is caught by (4)
        // instead of by heap geometry.
        await Assert.That(allocating.GcInfo!.Gen0).IsGreaterThanOrEqualTo(0);
        if (allocating.GcInfo.Gen0 == 0)
            Batch0Support.Log(
                "note: Gen0 == 0 although the bytes were allocated — the ambient gen0 budget "
                    + "exceeded this loop (legal: how many collections a 16 MiB loop causes is an "
                    + "implementation and platform detail, the allocation is the fact). "
                    + "Assertion (4) still pins the counts."
            );

        // (4) Deterministic count guard (mutation M2: GC counts forced to 0). A forced
        // generation-0 collection always bumps GC.CollectionCount(0), so the library
        // has to report at least one.
        var forced = Runner.Time(
            1,
            static () => GC.Collect(0, GCCollectionMode.Forced, blocking: true)
        );
        Batch0Support.Log($"forced gen0 collection: Gen0={forced.GcInfo?.Gen0}");
        await Assert.That(forced.GcInfo).IsNotNull();
        await Assert
            .That(forced.GcInfo!.Gen0)
            .IsGreaterThan(0)
            .Because(
                "one forced GC.Collect(0) inside the measured window must be reported as Gen0 >= 1; "
                    + "a library that zeroes the collection counts fails here on every platform"
            );
    }
}
