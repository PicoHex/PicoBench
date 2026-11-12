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
    /// <item>an empty async delegate is never published as a plain number: it is
    /// either at the resolution floor or disclosed as <c>&lt;limit</c>;</item>
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
        BenchmarkResult? last = null;

        Func<Task> empty = static () => Task.CompletedTask;

        for (var trial = 0; trial < trials; trial++)
        {
            var result = await Benchmark.RunAsync(
                "async-empty-lambda",
                empty,
                warmup: null,
                config: AsyncConfig()
            );
            last = result;

            var rawPerOp =
                result.Samples!.Average(static s => s.ElapsedNanoseconds) / result.IterationsPerSample;

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

        var statistics = last!.Statistics;
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

        // (2) …and the floor really left the reported value.
        await Assert
            .That(reported)
            .IsLessThanOrEqualTo(raw)
            .Because(
                $"an empty async delegate was reported at {reported:F3} ns/op against a raw "
                    + $"{raw:F3} ns/op — the deduction raised the value it was supposed to reduce"
            );

        // (3) An empty delegate is not a plain number: at the floor, or disclosed.
        var cell = Batch0Support.ReadCsvCell(new CsvFormatter().Format(last), last.Name, "Avg_ns");
        var atFloor = statistics.Avg <= Batch0Support.ResolutionFloorNanoseconds;
        var disclosed = statistics.ResolutionLimited && cell.StartsWith("<", StringComparison.Ordinal);

        Batch0Support.Log(
            $"reported={statistics.Avg:F3} ns/op cell='{cell}' atFloor={atFloor} disclosed={disclosed}"
        );

        await Assert
            .That(atFloor || disclosed)
            .IsTrue()
            .Because(
                $"an empty async delegate was reported as '{cell}' (avg {statistics.Avg:F3} ns/op, "
                    + $"limit {statistics.ResolutionLimitNanoseconds:F3} ns/op, "
                    + $"resolutionLimited {statistics.ResolutionLimited}) — neither at the floor nor disclosed"
            );

        // (4) The published limit must cover the floor the run deducted.
        var perOpFloor = statistics.OverheadNs / last.IterationsPerSample;
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
    /// that loop must agree with the reference within 25%.
    /// <para>
    /// The workload carries the await deliberately: it is the asynchronous shape
    /// whose harness floor 0.1b had to remove, and it is heavy enough that the
    /// removal is what makes the two numbers comparable. A bare
    /// <c>await Task.CompletedTask</c> cannot serve as the reference, because its
    /// net cost is ≈0.1 ns/op — awaiting an already-completed task is essentially
    /// free — so both sides would sit at the harness floor and their ratio would
    /// measure noise, not the deduction.
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
        const int trials = 3;

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

        for (var trial = 0; trial < trials; trial++)
        {
            var result = await Benchmark.RunAsync(
                "async-known-cost",
                spinThenAwait,
                warmup: null,
                config: AsyncConfig(iterationsPerSample: iterationsPerSample)
            );

            // The independent reference: the same spins and the same awaits, the
            // same iteration count, timed by hand with a Stopwatch only.
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < iterationsPerSample; i++)
                await spinThenAwait();
            watch.Stop();
            var manualPerOp = watch.Elapsed.TotalNanoseconds / iterationsPerSample;

            manuals.Add(manualPerOp);
            reported.Add(result.Statistics.Avg);
            overheads.Add(result.Statistics.OverheadNs);

            Batch0Support.Log(
                $"trial {trial}: manual={manualPerOp:F1} ns/op reported={result.Statistics.Avg:F1} ns/op "
                    + $"ratio={result.Statistics.Avg / manualPerOp:F4} overhead={result.Statistics.OverheadNs:F0} ns/sample"
            );
        }

        var manual = Batch0Support.Median(manuals);
        var measured = Batch0Support.Median(reported);
        var ratio = measured / manual;

        Batch0Support.Log(
            $"median manual={manual:F1} ns/op, median reported={measured:F1} ns/op, ratio={ratio:F4} "
                + "(allowed 0.75-1.25)"
        );

        // The floor was deducted from the awaits, not left on top of them.
        await Assert
            .That(Batch0Support.Median(overheads))
            .IsGreaterThan(0.0)
            .Because("the known-cost async run deducted nothing from its samples");

        await Assert
            .That(ratio)
            .IsGreaterThanOrEqualTo(0.75)
            .Because(
                $"the run reported {measured:F1} ns/op for awaits a manual Stopwatch loop measures at "
                    + $"{manual:F1} ns/op — the deduction overshot the floor it was correcting"
            );
        await Assert
            .That(ratio)
            .IsLessThanOrEqualTo(1.25)
            .Because(
                $"the run reported {measured:F1} ns/op for awaits a manual Stopwatch loop measures at "
                    + $"{manual:F1} ns/op — the harness floor is still attributed to the awaits"
            );
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
