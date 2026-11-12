namespace PicoBench.Tests.Batch0;

using System.Diagnostics;

/// <summary>
/// Batch 0 · 0.1b — the <b>asynchronous</b> paths must deduct the harness floor,
/// not just the synchronous one.
/// <para>
/// 0.1 measured the empty-delegate floor for <see cref="Runner.Time"/> and
/// deducted it, but <c>CollectAndBuildAsync</c> still passed
/// <c>harnessOverheadNanoseconds: 0</c>. On the reference machine one empty async
/// delegate round trip costs a genuine 12-15 ns/op (the <c>Task</c> dispatch, the
/// awaited loop body and the process-cycle reads), and all of it was published as
/// the cost of the measured action.
/// </para>
/// <para>
/// The probe has to travel through the <b>same timing entry point</b> as the
/// measurement. A probe of a different shape — or one the JIT can constant-fold,
/// e.g. one read straight from a <c>static readonly</c> field — measures a
/// different loop body and would restore the very defect this fixes. That is why
/// the probes live in one-element arrays and why the assertions below compare the
/// deduction against an independently measured floor instead of trusting a
/// non-zero number.
/// </para>
/// </summary>
public class AsyncOverheadTests
{
    /// <summary>
    /// Batch 0 · 0.1b — an empty async delegate is the asynchronous resolution
    /// floor. Four things must hold, none of which held at HEAD:
    /// <list type="number">
    /// <item>the run <b>deducts</b> a floor at all (<see cref="Statistics.OverheadNs"/>
    /// was 0 on every async path) — the defect itself;</item>
    /// <item>the reported per-operation value is therefore below the raw
    /// measurement it was normalised from;</item>
    /// <item>an empty async delegate is never published as a plain number it cannot
    /// back up: it is at the resolution floor, disclosed as <c>&lt;limit</c>, or
    /// demonstrably above this run's own resolution limit;</item>
    /// <item>the published limit covers the floor the run deducted, exactly as the
    /// synchronous B2 guard demands — a bound below the deducted floor can never be
    /// an upper bound.</item>
    /// </list>
    /// <para>Expected at HEAD: <b>RED</b> — <c>OverheadNs == 0</c>, nothing is
    /// deducted, and 12-15 ns/op of harness is printed as real work.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.1b/async floor")]
    [NotInParallel] // timing test: keep the core to ourselves
    public async Task Async_EmptyDelegate_FloorIsDeductedAndDisclosed()
    {
        const int trials = 3;
        var rawPerOps = new List<double>(trials);
        var reportedPerOps = new List<double>(trials);
        var overheads = new List<double>(trials);
        var results = new List<BenchmarkResult>(trials);

        Func<Task> empty = static () => Task.CompletedTask;

        for (var trial = 0; trial < trials; trial++)
        {
            var result = await Benchmark.RunAsync(
                "async-empty-lambda",
                empty,
                warmup: null,
                config: AsyncConfig()
            );
            results.Add(result);

            var rawPerOp =
                result.Samples!.Average(static s => s.ElapsedNanoseconds)
                / result.IterationsPerSample;

            rawPerOps.Add(rawPerOp);
            reportedPerOps.Add(result.Statistics.Avg);
            overheads.Add(result.Statistics.OverheadNs);

            Batch0Support.Log(
                $"trial {trial}: raw={rawPerOp:F3} ns/op reported={result.Statistics.Avg:F3} "
                    + $"overhead={result.Statistics.OverheadNs:F0} ns/sample "
                    + $"({result.Statistics.OverheadNs / result.IterationsPerSample:F3} ns/op) "
                    + $"limit={result.Statistics.ResolutionLimitNanoseconds:F3} ns/op "
                    + $"limited={result.Statistics.ResolutionLimited}"
            );
        }

        // The disclosure contract is asserted on the LEAST INTERFERED trial.
        //
        // An empty delegate is the workload that cannot be resolved, and a trial the
        // machine did not interrupt shows exactly that. Measured on a loaded machine
        // (full suite 45-59 s against 21 s idle), one trial read raw 64.820 ns/op
        // where the other two read 27.964 and 25.072, and its residual
        // (47.460 ns/op) exceeded the limit that same run disclosed
        // (20.000 ns/op) — so the run published a plain number, because under that
        // much interference an empty delegate genuinely did appear to cost 47 ns/op.
        // Interference can only make an empty delegate look resolvable, never the
        // reverse, so taking the minimum removes the ambient state from the premise
        // without weakening the claim. The median assertions below stay medians:
        // they are about the floor, which every trial deducts.
        var typical = results.MinBy(static r => r.Statistics.Avg)!;
        var statistics = typical.Statistics;
        var raw = Batch0Support.Median(rawPerOps);
        var reported = Batch0Support.Median(reportedPerOps);

        // (1) A deduction happened at all — this is the defect of 0.1b.
        await Assert
            .That(statistics.OverheadNs)
            .IsGreaterThan(0.0)
            .Because(
                $"the async run reported OverheadNs = {statistics.OverheadNs} while its raw sample was "
                    + $"{raw:F3} ns/op: an async benchmark must deduct its own harness floor, not publish it "
                    + "as the cost of the measured action"
            );

        // (2) …and the floor really left the reported value: it was subtracted from
        // every sample, not merely announced on the result.
        //
        // This used to be `median(reported) <= median(raw)`, which is NOT an invariant
        // of the implementation. A sample that elapses less than the floor probed for
        // the run is published at the floor instead (`Benchmark.PerOpNanosecondsWithFloor`),
        // and that floor can exceed the sample's own raw value: measured on the
        // reference machine the inequality was violated in 108 of 200 quiet
        // repetitions, while a noisy full-suite process (whose samples sit further
        // above the probed floor) almost never violated it. A machine-state-dependent
        // coin flip is a flaky assertion, not a defect detector.
        //
        // The identity below holds exactly for a WallClock run (its timing quantum is
        // 0), and it catches the half of the defect no ratio can see: a floor that is
        // measured and announced but never subtracted fails it by the whole floor
        // (measured under a mutant that reports the floor and skips the subtraction:
        // delta = 13.2 ns/op against a 2.2e-8 tolerance). A floor that is never
        // measured at all leaves OverheadNs = 0, which makes this identity vacuous —
        // that shape is assertion (1)'s job, and it was measured red on the same mutant.
        var recomputed = RecomputedAvgFromRawSamples(typical);
        Batch0Support.Log(
            $"raw median={raw:F3} ns/op reported median={reported:F3} ns/op "
                + $"quietest-trial avg={statistics.Avg:F6} recomputed-from-samples={recomputed:F6} "
                + $"delta={statistics.Avg - recomputed:E2}"
        );

        await Assert
            .That(Math.Abs(statistics.Avg - recomputed))
            .IsLessThanOrEqualTo(1e-9 * Math.Max(1.0, statistics.Avg))
            .Because(
                $"the run published avg = {statistics.Avg:F6} ns/op while its own retained samples "
                    + $"minus the {statistics.OverheadNs:F0} ns floor it reports give {recomputed:F6} ns/op "
                    + $"(raw median {raw:F3} ns/op, reported median {reported:F3} ns/op) — the floor was "
                    + "announced but not deducted, or deducted by an amount other than the one it reports"
            );

        // (3) An empty delegate is not a plain number it cannot back up: at the floor,
        // disclosed, or above this run's own resolution limit.
        //
        // The library's contract is <c>ResolutionLimited ⇔ Avg &lt;= limit</c>
        // (StatisticsCalculator), and the third case is what CI measured on osx-arm64:
        // 11.912 ns/op against a 10.867 ns/op limit (run 43213497484), then 11.352
        // against 9.583 (run 43223332511). An empty async round trip costs ≈21 ns there,
        // the floor probe recovers ≈9.5 of it, and the residual lands just above the
        // limit — so the run is self-consistent and a plain number is honest. This
        // assertion used to accept only "at the floor or disclosed", i.e. it also
        // assumed the probe recovers the *whole* async harness on every platform; how
        // much of it a probe recovers is a platform detail, not a library contract.
        // What is not negotiable is the deduction itself, and that stays pinned twice
        // over: assertion (1) fails when no floor is measured at all, assertion (2)
        // fails when one is announced but subtracted by another amount (both
        // re-measured red against mutants, see the notes in the batch report).
        var cell = Batch0Support.ReadCsvCell(
            new CsvFormatter().Format(typical),
            typical.Name,
            "Avg_ns"
        );
        var atFloor = statistics.Avg <= Batch0Support.ResolutionFloorNanoseconds;
        var disclosed =
            statistics.ResolutionLimited && cell.StartsWith("<", StringComparison.Ordinal);
        var aboveTheRunsOwnLimit = statistics.Avg >= statistics.ResolutionLimitNanoseconds;

        Batch0Support.Log(
            $"reported={statistics.Avg:F3} ns/op cell='{cell}' atFloor={atFloor} disclosed={disclosed} "
                + $"aboveTheRunsOwnLimit={aboveTheRunsOwnLimit}"
        );

        await Assert
            .That(atFloor || disclosed || aboveTheRunsOwnLimit)
            .IsTrue()
            .Because(
                $"an empty async delegate was reported as '{cell}' (avg {statistics.Avg:F3} ns/op, "
                    + $"limit {statistics.ResolutionLimitNanoseconds:F3} ns/op, "
                    + $"resolutionLimited {statistics.ResolutionLimited}) — a value below the run's own "
                    + "resolution limit, published as a plain number: that is the one thing the "
                    + "disclosure contract forbids"
            );

        // (4) The published limit must cover the floor the run deducted.
        var perOpFloor = statistics.OverheadNs / typical.IterationsPerSample;
        Batch0Support.Log(
            $"limit={statistics.ResolutionLimitNanoseconds:F4} ns/op vs deducted floor {perOpFloor:F4} ns/op"
        );
        await Assert.That(statistics.ResolutionLimitNanoseconds).IsGreaterThanOrEqualTo(perOpFloor);
    }

    /// <summary>
    /// Batch 0 · 0.1b — every asynchronous entry point that can be corrected must
    /// be: the delegate overload, the setup/teardown overload, the state-passing
    /// overload and the scope overload. They build their samples through different
    /// lambdas, and HEAD deducted nothing in any of them.
    /// <para>
    /// <c>CpuOnly</c> is treated separately on purpose: its timing source is the
    /// process CPU clock, whose granularity (hundreds of microseconds, measured at
    /// run time) can be coarser than a whole probe of 5 000 empty awaits — the
    /// probe then reads exactly 0 and there is genuinely no floor to subtract. The
    /// requirement there is that the run <b>discloses</b> that it could not resolve
    /// the value, instead of publishing the tick as measured work.
    /// </para>
    /// <para>Expected at HEAD: <b>RED</b> — the four wall-clock paths report
    /// <c>OverheadNs == 0</c>.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.1b/all async paths")]
    [NotInParallel]
    public async Task Async_EveryEntryPointDeductsTheFloor()
    {
        var config = AsyncConfig();

        Func<Task> empty = static () => Task.CompletedTask;

        var measurements = new List<(string Path, double OverheadNs, double Avg)>();

        var plain = await Benchmark.RunAsync("async-plain", empty, warmup: null, config: config);
        measurements.Add(("plain", plain.Statistics.OverheadNs, plain.Statistics.Avg));

        var withSetup = await Benchmark.RunAsync(
            "async-setup-teardown",
            empty,
            warmup: null,
            config: config,
            setup: static () => Task.CompletedTask,
            teardown: static () => Task.CompletedTask
        );
        measurements.Add(
            ("setup/teardown", withSetup.Statistics.OverheadNs, withSetup.Statistics.Avg)
        );

        var state = await Benchmark.RunAsync(
            "async-state",
            0,
            static (int _) => Task.CompletedTask,
            warmup: null,
            config: config
        );
        measurements.Add(("state", state.Statistics.OverheadNs, state.Statistics.Avg));

        var scoped = await Benchmark.RunScopedAsync(
            "async-scoped",
            static () => new ProbeScope(),
            static (ProbeScope _) => Task.CompletedTask,
            config
        );
        measurements.Add(("scoped", scoped.Statistics.OverheadNs, scoped.Statistics.Avg));

        foreach (var (path, overheadNs, avg) in measurements)
            Batch0Support.Log($"{path}: overhead={overheadNs:F0} ns/sample avg={avg:F3} ns/op");

        foreach (var (path, overheadNs, avg) in measurements)
        {
            await Assert
                .That(overheadNs)
                .IsGreaterThan(0.0)
                .Because(
                    $"the '{path}' async path reported OverheadNs = {overheadNs:F0} and published "
                        + $"avg = {avg:F3} ns/op; the harness floor is not an operation the user wrote"
                );
        }

        // ── CpuOnly: a floor is only required when the clock could express one ──
        var cpuOnly = await Benchmark.RunAsync(
            "async-cpu-only",
            empty,
            warmup: null,
            config: AsyncConfig(AsyncTimingMode.CpuOnly)
        );

        var cpuOverhead = cpuOnly.Statistics.OverheadNs;
        var cpuDisclosed = cpuOnly.Statistics.ResolutionLimited || cpuOnly.Statistics.Unmeasured;

        Batch0Support.Log(
            $"cpu-only: overhead={cpuOverhead:F0} ns/sample avg={cpuOnly.Statistics.Avg:F3} ns/op "
                + $"limit={cpuOnly.Statistics.ResolutionLimitNanoseconds:F3} limited={cpuOnly.Statistics.ResolutionLimited} "
                + $"unmeasured={cpuOnly.Statistics.Unmeasured}"
        );

        await Assert
            .That(cpuOverhead > 0.0 || cpuDisclosed)
            .IsTrue()
            .Because(
                $"the CpuOnly path measured no floor (overhead={cpuOverhead:F0}) and still published "
                    + $"avg = {cpuOnly.Statistics.Avg:F3} ns/op as a measurement — a clock that cannot "
                    + "express the cost has to say so"
            );

        await Assert
            .That(cpuOnly.Statistics.ResolutionLimitNanoseconds)
            .IsGreaterThanOrEqualTo(cpuOverhead / cpuOnly.IterationsPerSample);
    }

    /// <summary>
    /// Batch 0 · 0.1b — a <b>known cost</b>. <c>Thread.SpinWait(200)</c> followed by
    /// <c>await Task.CompletedTask</c> is a workload of ≈8 µs/op, measured
    /// independently by the manual Stopwatch loop below, which awaits the same
    /// delegate with no library involvement. The framework's per-operation value for
    /// that loop must agree with the reference within ≈35%.
    /// <para>
    /// The workload carries the await deliberately: it is the asynchronous shape
    /// whose harness floor 0.1b had to remove, and it is heavy enough that the
    /// removal is what makes the two numbers comparable. A bare
    /// <c>await Task.CompletedTask</c> cannot serve as the reference, because its
    /// net cost is ≈0.1 ns/op — awaiting an already-completed task is essentially
    /// free — so both sides would sit at the harness floor and their ratio would
    /// measure noise, not the deduction.
    /// </para>
    /// <para>
    /// <b>Where the tolerance comes from</b> (measured on the reference machine,
    /// 8 cores / 16 threads, 700-test suite). The ratio compares two wall-clock
    /// windows taken at different times, so it inherits the machine's frequency and
    /// preemption drift — nothing tighter than that survives, and nothing wider
    /// would still mean anything:
    /// </para>
    /// <list type="bullet">
    /// <item>one manual window (the original shape): per-trial ratio fell to 0.275
    /// and 7 of 36 five-trial blocks read below 0.70 — unusable, because one
    /// descheduling inflates the whole reference for that trial;</item>
    /// <item>fastest of <b>three</b> windows (the previous shape): blocks spanned
    /// 0.763-0.977, and this test was still measured red once at 0.5956 (median
    /// reference 10.58 µs/op against a median reported 6.30 µs/op, i.e. a reference
    /// inflated 1.68×) — the windows were few enough that every one of them could
    /// be preempted;</item>
    /// <item>fastest of <b>seven</b> windows (what this test now uses): blocks
    /// spanned 0.911-1.035, per-trial minimum 0.772, p10 0.862;</item>
    /// <item>fastest of fifteen windows: 0.920-1.056, per-trial minimum 0.793 — the
    /// curve has flattened (0.772 → 0.793 for more than double the windows), which
    /// is why seven is where it starts and stops: more windows buy nothing.</item>
    /// </list>
    /// <para>
    /// All of the seven- and fifteen-window rows were taken with 40 trials in one
    /// process on a deliberately <b>loaded</b> machine (full-suite duration 28-45 s
    /// against 21 s idle), where the workload measured 6.25 µs/op rather than the
    /// 8 µs/op the header quotes — the point of the numbers is their spread, not
    /// their absolute level.
    /// </para>
    /// <para>
    /// Hence the window <c>[0.65, 1.35]</c>. The bottom follows the measured tail
    /// rather than a round number: seven windows put the worst block at 0.911, and
    /// the one real failure at three windows (0.5956) sat a further 0.78× below that
    /// shape's own worst block (0.763) — machine states the sample did not contain.
    /// 0.911 × 0.78 ≈ 0.71, so 0.65 keeps ≈9% headroom under the worst state
    /// extrapolated from data, while a 0.5× regression still lands outside. The top
    /// stays at 1.35 for the opposite signature: seven-window blocks measured up to
    /// 1.035, and an inflated report is also caught arithmetically below.
    /// </para>
    /// <para>
    /// The floor itself is <b>not</b> what this ratio defends: on a ≈8 µs/op workload
    /// the deducted floor is ≈13 ns/op, i.e. 0.2%, which no wall-clock ratio can
    /// resolve. The deduction is defended arithmetically instead — by
    /// <c>OverheadNs &gt; 0</c> and by the exact identity re-derived from the retained
    /// samples (see the assertions below). The two shapes of the defect need both:
    /// a floor that is never measured reports <c>OverheadNs == 0</c> and is caught by
    /// <c>OverheadNs &gt; 0</c> (the identity is then vacuous), while a floor that is
    /// reported but never subtracted is caught by the identity — measured at delta =
    /// 13.2 ns/op and 25.8 ns/op on the two tests, eight orders of magnitude beyond
    /// the 1e-9 relative tolerance.
    /// </para>
    /// <para>Expected at HEAD: <b>RED</b> — nothing is deducted, so the run reports
    /// its own harness cost on top of the spin and <c>OverheadNs</c> is 0.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.1b/known cost")]
    [NotInParallel]
    public async Task Async_KnownCost_AgreesWithManualStopwatchReference()
    {
        const int iterationsPerSample = 2_000;
        const int trials = 5;
        const int referenceWindows = 7;

        Func<Task> spinThenAwait = static async () =>
        {
            Thread.SpinWait(200);
            await Task.CompletedTask;
        };

        // Discarded warm-up: the first timed loop would otherwise carry tier-0
        // compilation of the async state machine and inflate the reference.
        _ = await Runner.TimeAsync(iterationsPerSample, spinThenAwait);

        var manuals = new List<double>(trials);
        var reported = new List<double>(trials);
        var overheads = new List<double>(trials);
        BenchmarkResult? last = null;

        for (var trial = 0; trial < trials; trial++)
        {
            var result = await Benchmark.RunAsync(
                "async-known-cost",
                spinThenAwait,
                warmup: null,
                config: AsyncConfig(iterationsPerSample: iterationsPerSample)
            );
            last = result;

            // The independent reference: the same spins and the same awaits, the
            // same iteration count, timed by hand with a Stopwatch only.
            //
            // Seven windows, of which the FASTEST is kept. A single window has
            // nothing to compare against when it is preempted: one descheduling
            // inflates the whole reference for that trial (measured: per-trial ratio
            // down to 0.275), while the library dilutes the same preemption over ten
            // samples — an asymmetry between the two sides that has nothing to do
            // with the floor under test. The fastest window is the least interfered
            // one, which is also the statistic PicoBench itself uses to probe its
            // harness floor (`Runner.MeasureHarnessOverheadAsync` keeps the minimum
            // over its repetitions). Seven is the knee of the measured curve:
            // fastest-of-1/3/7/15 puts the per-trial minimum at 0.275/0.552/0.772/
            // 0.793, so the estimator converges by seven and extra windows merely
            // cost time (seven × 16 ms per trial, on a ~0.5 s test).
            var fastestWindow = double.MaxValue;
            for (var window = 0; window < referenceWindows; window++)
            {
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < iterationsPerSample; i++)
                    await spinThenAwait();
                watch.Stop();
                fastestWindow = Math.Min(
                    fastestWindow,
                    watch.Elapsed.TotalNanoseconds / iterationsPerSample
                );
            }

            manuals.Add(fastestWindow);
            reported.Add(result.Statistics.Avg);
            overheads.Add(result.Statistics.OverheadNs);

            Batch0Support.Log(
                $"trial {trial}: manual={fastestWindow:F1} ns/op reported={result.Statistics.Avg:F1} ns/op "
                    + $"ratio={result.Statistics.Avg / fastestWindow:F4} "
                    + $"overhead={result.Statistics.OverheadNs:F0} ns/sample"
            );
        }

        var manual = Batch0Support.Median(manuals);
        var measured = Batch0Support.Median(reported);
        var ratio = measured / manual;

        Batch0Support.Log(
            $"median manual={manual:F1} ns/op, median reported={measured:F1} ns/op, ratio={ratio:F4} "
                + "(allowed 0.65-1.35)"
        );

        // (1) The floor was measured at all.
        await Assert
            .That(Batch0Support.Median(overheads))
            .IsGreaterThan(0.0)
            .Because("the known-cost async run deducted nothing from its samples");

        // (2) …and it was applied to every sample: the published average is exactly
        // what the retained raw samples give once the reported floor is subtracted.
        // This assertion carries the 0.1b defect, because it is arithmetic — it cannot
        // drift with the machine — and it fails for both shapes of the defect: no
        // floor measured, or a floor reported but not deducted.
        var recomputed = RecomputedAvgFromRawSamples(last!);
        Batch0Support.Log(
            $"avg={last!.Statistics.Avg:F6} recomputed-from-samples={recomputed:F6} "
                + $"delta={last.Statistics.Avg - recomputed:E2}"
        );
        await Assert
            .That(Math.Abs(last.Statistics.Avg - recomputed))
            .IsLessThanOrEqualTo(1e-9 * Math.Max(1.0, last.Statistics.Avg))
            .Because(
                $"the run published {last.Statistics.Avg:F6} ns/op while its own samples minus the "
                    + $"{last.Statistics.OverheadNs:F0} ns floor it reports give {recomputed:F6} ns/op — "
                    + "the harness floor was not deducted from the value the user sees"
            );

        // (3) The absolute scale still agrees with an independent stopwatch.
        await Assert
            .That(ratio)
            .IsGreaterThanOrEqualTo(0.65)
            .Because(
                $"the run reported {measured:F1} ns/op for awaits a manual Stopwatch loop measures at "
                    + $"{manual:F1} ns/op (ratio {ratio:F4}, allowed 0.65-1.35) — the two sides disagree "
                    + "by more than this machine's measured drift (σ ≈ 0.05-0.08) can explain"
            );
        await Assert
            .That(ratio)
            .IsLessThanOrEqualTo(1.35)
            .Because(
                $"the run reported {measured:F1} ns/op for awaits a manual Stopwatch loop measures at "
                    + $"{manual:F1} ns/op (ratio {ratio:F4}, allowed 0.65-1.35) — the reported value is "
                    + "inflated far beyond anything the harness floor could add to an 8 µs/op workload"
            );
    }

    /// <summary>
    /// Recomputes <see cref="Statistics.Avg"/> from the raw samples a run retained,
    /// using only public values, so that "the harness floor was deducted" can be
    /// checked <b>arithmetically</b> instead of by comparing two noisy statistics.
    /// <para>
    /// For a <see cref="AsyncTimingMode.WallClock"/> run the published per-operation
    /// value of a sample is <c>max((elapsed − overhead) / iterations, overhead /
    /// iterations)</c>: a sample whose elapsed time is not above the floor the run
    /// probed is published at that floor, because the harness cost is real even when
    /// this particular sample could not be observed above it. The WallClock timing
    /// quantum is 0, so no other term enters.
    /// </para>
    /// </summary>
    private static double RecomputedAvgFromRawSamples(BenchmarkResult result)
    {
        var samples = result.Samples;
        if (samples is null || samples.Count == 0)
            throw new InvalidOperationException(
                "the run did not retain its samples, so its average cannot be re-derived"
            );

        var iterations = result.IterationsPerSample;
        var overhead = result.Statistics.OverheadNs;
        var overheadPerOp = overhead / iterations;

        var sum = 0.0;
        foreach (var sample in samples)
        {
            var net = sample.ElapsedNanoseconds - overhead;
            sum += net > 0 ? net / iterations : Math.Max(0.0, overheadPerOp);
        }

        return sum / samples.Count;
    }

    private static BenchmarkConfig AsyncConfig(
        AsyncTimingMode timingMode = AsyncTimingMode.WallClock,
        int iterationsPerSample = 5_000
    ) =>
        new()
        {
            WarmupIterations = 0,
            SampleCount = 10,
            IterationsPerSample = iterationsPerSample,
            AutoCalibrateIterations = false,
            ForceGcBeforeBenchmark = false,
            RetainSamples = true,
            TimingMode = timingMode,
        };

    private sealed class ProbeScope : IDisposable
    {
        public void Dispose() { }
    }
}
