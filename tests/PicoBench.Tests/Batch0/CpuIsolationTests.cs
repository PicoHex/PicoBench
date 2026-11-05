namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · 0.0 (new guard for 0.4) — the cycle metric must be
/// <b>thread</b>-scoped, not process-wide.
/// <para>
/// B1-H1 is the finding that the synchronous path reads
/// <c>QueryProcessCycleTime</c> (a process-wide counter), so any other thread in
/// the process — a GC helper, a runtime background thread, or a neighbour
/// benchmark — inflates the reported cycles-per-op of the measured work. Batch 0
/// · 0.4 asks for <c>GetCpuCycles()</c> on the synchronous path and for
/// "cyc/op 变化 &lt; 30% when an idle thread is spinning". R0's review
/// (<c>.review-2026-11/08-regression-net-review.md</c> §4) found 0.4 had
/// <b>no test at all</b>: <see cref="KnownCostAnchorTests.SpinWait_IsLinearInIterations"/>
/// only asserts positivity and linearity, both of which a process-wide counter
/// satisfies.
/// </para>
/// <para>
/// The shape of the assertion is deliberately independent of the absolute cycle
/// cost: the same workload is measured twice — once alone, once while a second
/// thread burns a core — and the cycles/op of the measured work must stay inside
/// a tolerance band. A thread-scoped counter is unaffected by the neighbour; a
/// process-scoped counter roughly doubles (R0 measured <b>2.03×</b> at HEAD).
/// </para>
/// <para>
/// The band is <b>0.65–1.35</b>, widened from 0.70–1.30 by G15 on the strength of
/// <c>.review-2026-11/19-final-verification.md</c> §3 (77 runs, 0 failures, ratio
/// min 0.845 / max 1.189, sd ≈ 0.072), which also showed which edge is at risk:
/// with the whole suite running around it the mean drops to 0.928 (the solo half
/// pays the first-call tier-0/JIT tax), so the <b>lower</b> edge is the binding
/// one — 0.70 sat ≈3.9σ from the mean, and the verifier saw one unrelated red at
/// that edge in 25 full-suite runs (<c>.review-2026-11/31-verify-b1.md</c> F4).
/// </para>
/// <para>
/// The widening does not weaken the guard: a process-wide counter measures
/// <b>1.92–2.03×</b>, still far outside 1.35 (42% of headroom left), so the
/// red-at-HEAD property this test exists for survives intact.
/// </para>
/// </summary>
public class CpuIsolationTests
{
    /// <summary>
    /// Batch 0 · 0.4 — an idle (busy-spinning) neighbour thread must not change
    /// cycles/op of the measured workload beyond the 0.65–1.35 band (see the class
    /// remarks for why that band, and why widening it keeps the red at HEAD).
    /// <para>Expected at HEAD: <b>RED</b> — the synchronous timing loop reads
    /// process-wide cycle counts (<c>Runner.Cpu.cs</c> <c>GetProcessCpuCycles</c>
    /// behind <c>Runner.cs</c>), so cycles/op roughly doubles (2.03× measured by
    /// R0) while the measured work itself is unchanged.</para>
    /// <para>Not assertable on platforms without a meaningful cycle source
    /// (macOS exposes a monotonic proxy, Linux without perf events exposes
    /// nothing); there the test logs and returns instead of pretending.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.4")]
    [NotInParallel] // must own the process counters for the whole measurement
    public async Task CpuCycles_IgnoreBusyNeighbourThread()
    {
        var environment = new EnvironmentInfo();
        if (!environment.CpuCyclesAreMeaningful)
        {
            Batch0Support.Log(
                $"0.4 guard not assertable here: cycle source = {environment.CpuCycleMeasurement} "
                    + "(meaningful=False)"
            );
            return;
        }

        const int iterations = 2_000;

        // ≈70 k cycles/op (≈20-25 µs): long enough that per-sample fixed costs and
        // counter resolution are irrelevant, short enough to run twice quickly.
        Action work = static () => Thread.SpinWait(500);

        // 1) Alone.
        var solo = Runner.Time(iterations, work);
        var soloPerOp = solo.CpuCycles / (double)iterations;

        // 2) With a neighbour thread spinning on another core for the whole run.
        using var stopNeighbour = new ManualResetEventSlim(false);
        using var neighbourStarted = new ManualResetEventSlim(false);
        var neighbour = new Thread(() =>
        {
            neighbourStarted.Set();
            while (!stopNeighbour.IsSet)
                Thread.SpinWait(4_000);
        })
        {
            IsBackground = true,
            Name = "batch0-busy-neighbour",
        };

        TimingSample loaded;
        try
        {
            neighbour.Start();

            // Give the neighbour a moment to actually be spinning before the
            // measurement window opens (thread start is not instantaneous).
            await Assert.That(neighbourStarted.Wait(TimeSpan.FromSeconds(5))).IsTrue();
            Thread.Sleep(50);

            loaded = Runner.Time(iterations, work);
        }
        finally
        {
            stopNeighbour.Set();
            neighbour.Join(TimeSpan.FromSeconds(5));
        }

        var loadedPerOp = loaded.CpuCycles / (double)iterations;
        var ratio = soloPerOp <= 0 ? double.PositiveInfinity : loadedPerOp / soloPerOp;

        Batch0Support.Log(
            $"cycle source = {environment.CpuCycleMeasurement}; iterations={iterations}"
        );
        Batch0Support.Log(
            $"solo cycles/op={soloPerOp:F0} (total {solo.CpuCycles}), "
                + $"with busy neighbour cycles/op={loadedPerOp:F0} (total {loaded.CpuCycles}), "
                + $"ratio={ratio:F3} (allowed 0.65-1.35)"
        );

        await Assert.That(soloPerOp).IsGreaterThan(0);

        // 0.4: the counter must only see the measured thread, so the neighbour
        // must be invisible in cycles/op inside the tolerance band; a process-wide
        // counter lands near 1.92-2.03x, i.e. well outside it.
        await Assert
            .That(ratio)
            .IsLessThanOrEqualTo(1.35)
            .Because(
                $"a busy neighbour thread changed cycles/op by {ratio:F2}× "
                    + $"(solo {soloPerOp:F0} → {loadedPerOp:F0}), i.e. the counter is process-wide"
            );
        await Assert.That(ratio).IsGreaterThanOrEqualTo(0.65);
    }
}
