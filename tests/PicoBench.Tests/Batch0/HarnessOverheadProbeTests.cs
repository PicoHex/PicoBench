namespace PicoBench.Tests.Batch0;

/// <summary>
/// Ablation hole <b>H1-A22</b> — the first harness probe must be discarded.
/// <para>
/// <see cref="Runner.MeasureHarnessOverhead(int, int, System.Func{int, TimingSample})"/>
/// runs <c>repetitions + 1</c> probes and keeps the minimum and the maximum of all but
/// the first one: the first probe carries first-call warm-up (the delegate, the
/// counters and the loop are all cold). The 2026-11 ablation pass
/// (<c>.review-2026-11/ablation-consolidated.md</c> §5) deleted the
/// <c>if (r == 0) continue;</c> guard and the whole suite stayed green (742/742) —
/// nothing in the repository called the probe logic directly (<c>AsyncOverheadTests</c>
/// only mentions it in a comment), so the guard had no test behind it.
/// </para>
/// <para>
/// What the guard is worth: <see cref="Benchmark"/> derives
/// <c>spreadPerOp</c> and <c>overheadUpperBoundPerOp</c> — and with them the
/// disclosed per-operation resolution limit — from the probe's <b>maximum</b>. A cold
/// first probe therefore inflates the published limit and paints genuinely measurable
/// small costs as <c>(below resolution)</c> / unmeasured.
/// </para>
/// <para>
/// The tests inject the probe sequence through the existing <c>sample</c> seam instead
/// of relying on real timing: a counter makes the first probe return a value chosen by
/// the test. That makes the assertion <b>deterministic</b> — no sleep, no spin, no
/// CPU-speed dependence — and it is strictly stronger than a "first probe is slower"
/// timing heuristic, which cannot prove that the first probe was <i>excluded</i> from
/// the minimum as opposed to merely being fast. Two shapes are used: a slow first probe
/// (returns 10× the steady state, which is what warm-up really looks like) and a fast
/// first probe (which only an explicit exclusion keeps out of the minimum).
/// </para>
/// </summary>
public class HarnessOverheadProbeTests
{
    /// <summary>The steady-state cost every probe after the first reports.</summary>
    private const double SteadyNanoseconds = 10.0;

    /// <summary>What the discarded first probe reports: 100× the steady state.</summary>
    private const double ColdFirstNanoseconds = 1000.0;

    /// <summary>What a fast first probe reports: below the steady state.</summary>
    private const double FastFirstNanoseconds = 1.0;

    private const int Repetitions = 5;

    /// <summary>
    /// H1-A22, synchronous path — a cold first probe must not reach either end of the
    /// reported <see cref="Runner.HarnessOverhead"/>.
    /// <para>
    /// Injected sequence: <c>r = 0</c> → 1 000 ns, <c>r ≥ 1</c> → 10 ns. Both the
    /// minimum and the maximum must be 10 ns. The minimum alone would already fail if
    /// the guard were moved, but the <b>maximum</b> is the value the run's resolution
    /// limit is computed from, so it is the one that has to be pinned.
    /// </para>
    /// <para>Expected with <c>if (r == 0) continue;</c> deleted: <b>RED</b> —
    /// <c>MaxNanoseconds = 1000</c> instead of 10.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Ablation", "H1-A22")]
    public async Task HarnessOverhead_FirstProbeIsDiscarded()
    {
        var calls = 0;
        var overhead = Runner.MeasureHarnessOverhead(
            iterations: 1,
            repetitions: Repetitions,
            sample: _ =>
                new TimingSample
                {
                    ElapsedNanoseconds = calls++ == 0 ? ColdFirstNanoseconds : SteadyNanoseconds,
                }
        );

        Batch0Support.Log(
            $"sync injected sequence [r=0 → {ColdFirstNanoseconds:F0}, r≥1 → {SteadyNanoseconds:F0}] "
                + $"over {calls} probes: min={overhead.MinNanoseconds:F3} max={overhead.MaxNanoseconds:F3}"
        );

        // The seam was driven exactly repetitions + 1 times (r = 0 … repetitions), so
        // "the first probe was skipped" means one probe fewer in the statistics, not a
        // shorter loop.
        await Assert.That(calls).IsEqualTo(Repetitions + 1);

        await Assert
            .That(overhead.MinNanoseconds)
            .IsEqualTo(SteadyNanoseconds)
            .Because(
                $"Min = {overhead.MinNanoseconds:F3}: the discarded probes must all report the "
                    + $"steady state ({SteadyNanoseconds:F0})"
            );
        await Assert
            .That(overhead.MaxNanoseconds)
            .IsEqualTo(SteadyNanoseconds)
            .Because(
                $"Max = {overhead.MaxNanoseconds:F3} carries the cold first probe "
                    + $"({ColdFirstNanoseconds:F0}): the run computes its disclosed resolution limit "
                    + "from this maximum, so a warm-up probe would inflate the published limit"
            );
    }

    /// <summary>
    /// H1-A22, asynchronous path — the twin of
    /// <see cref="HarnessOverhead_FirstProbeIsDiscarded"/> for
    /// <see cref="Runner.MeasureHarnessOverheadAsync(int, int, System.Func{int, System.Threading.Tasks.Task{TimingSample}})"/>.
    /// <para>
    /// The ablation report flagged the async guard structurally only (the same
    /// <c>if (r == 0) continue;</c> at the second loop); this test makes it observable.
    /// </para>
    /// <para>Expected with the async guard deleted: <b>RED</b> —
    /// <c>MaxNanoseconds = 1000</c> instead of 10.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Ablation", "H1-A22")]
    public async Task HarnessOverheadAsync_FirstProbeIsDiscarded()
    {
        var calls = 0;
        var overhead = await Runner.MeasureHarnessOverheadAsync(
            iterations: 1,
            repetitions: Repetitions,
            sample: _ => Task.FromResult(
                new TimingSample
                {
                    ElapsedNanoseconds = calls++ == 0 ? ColdFirstNanoseconds : SteadyNanoseconds,
                }
            )
        );

        Batch0Support.Log(
            $"async injected sequence [r=0 → {ColdFirstNanoseconds:F0}, r≥1 → {SteadyNanoseconds:F0}] "
                + $"over {calls} probes: min={overhead.MinNanoseconds:F3} max={overhead.MaxNanoseconds:F3}"
        );

        await Assert.That(calls).IsEqualTo(Repetitions + 1);
        await Assert
            .That(overhead.MinNanoseconds)
            .IsEqualTo(SteadyNanoseconds)
            .Because($"async Min = {overhead.MinNanoseconds:F3}");
        await Assert
            .That(overhead.MaxNanoseconds)
            .IsEqualTo(SteadyNanoseconds)
            .Because(
                $"async Max = {overhead.MaxNanoseconds:F3} carries the cold first probe: the async "
                    + "path derives the same disclosed resolution limit from it"
            );
    }

    /// <summary>
    /// H1-A22 — the exclusion must apply to the <b>minimum</b> too, and that half of the
    /// guard cannot be shown with a realistically slow first probe (a slow probe can only
    /// raise the maximum; the minimum of the remaining probes is still the steady state).
    /// <para>
    /// Injected sequence: <c>r = 0</c> → 1 ns, <c>r ≥ 1</c> → 10 ns. A first probe that
    /// is excluded leaves <c>Min = 10</c>; one that is merely "not the slowest" leaves
    /// <c>Min = 1</c>. This is the structural half of the assertion: it pins that the
    /// sample set used for the statistics starts at <c>r = 1</c>, independent of what any
    /// probe costs.
    /// </para>
    /// <para>Expected with <c>if (r == 0) continue;</c> deleted: <b>RED</b> —
    /// <c>MinNanoseconds = 1</c> instead of 10 (and, on the async twin,
    /// <c>MaxNanoseconds = 1000</c> in the test above).</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Ablation", "H1-A22")]
    public async Task HarnessOverhead_FirstProbeIsExcludedFromTheMinimum()
    {
        var syncCalls = 0;
        var sync = Runner.MeasureHarnessOverhead(
            iterations: 1,
            repetitions: Repetitions,
            sample: _ =>
                new TimingSample
                {
                    ElapsedNanoseconds = syncCalls++ == 0 ? FastFirstNanoseconds : SteadyNanoseconds,
                }
        );

        var asyncCalls = 0;
        var async = await Runner.MeasureHarnessOverheadAsync(
            iterations: 1,
            repetitions: Repetitions,
            sample: _ => Task.FromResult(
                new TimingSample
                {
                    ElapsedNanoseconds = asyncCalls++ == 0
                        ? FastFirstNanoseconds
                        : SteadyNanoseconds,
                }
            )
        );

        Batch0Support.Log(
            $"fast-first sequence [r=0 → {FastFirstNanoseconds:F0}, r≥1 → {SteadyNanoseconds:F0}]: "
                + $"sync min={sync.MinNanoseconds:F3} max={sync.MaxNanoseconds:F3}; "
                + $"async min={async.MinNanoseconds:F3} max={async.MaxNanoseconds:F3}"
        );

        await Assert.That(syncCalls).IsEqualTo(Repetitions + 1);
        await Assert.That(asyncCalls).IsEqualTo(Repetitions + 1);

        // The minimum is documented as "the best estimate of the additive cost, because
        // noise can only inflate a measurement" — a first probe that is *below* the
        // steady state is therefore exactly the sample the guard has to remove.
        await Assert
            .That(sync.MinNanoseconds)
            .IsEqualTo(SteadyNanoseconds)
            .Because(
                $"sync Min = {sync.MinNanoseconds:F3}: the first probe ({FastFirstNanoseconds:F0}) "
                    + "was included in the minimum"
            );
        await Assert
            .That(async.MinNanoseconds)
            .IsEqualTo(SteadyNanoseconds)
            .Because(
                $"async Min = {async.MinNanoseconds:F3}: the first probe ({FastFirstNanoseconds:F0}) "
                    + "was included in the minimum"
            );
    }
}
