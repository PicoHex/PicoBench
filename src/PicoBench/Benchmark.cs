namespace PicoBench;

/// <summary>
/// High-level benchmark orchestrator that runs measurements and computes statistics.
/// </summary>
public static class Benchmark
{
    /// <summary>
    /// Run a benchmark with the given action.
    /// </summary>
    /// <param name="name">Name of the benchmark.</param>
    /// <param name="action">The action to measure.</param>
    /// <param name="config">Optional configuration (uses <see cref="BenchmarkConfig.Default"/> if null).</param>
    /// <returns>A <see cref="BenchmarkResult"/> containing statistics.</returns>
    public static BenchmarkResult Run(string name, Action action, BenchmarkConfig? config = null)
    {
        ValidateName(name, nameof(name));
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        return Run(name, action, warmup: action, config);
    }

    /// <summary>
    /// Run a benchmark with separate warmup and measured actions.
    /// </summary>
    /// <param name="name">Name of the benchmark.</param>
    /// <param name="action">The action to measure.</param>
    /// <param name="warmup">Optional warmup action. If null, no warmup is performed.</param>
    /// <param name="config">Optional configuration (uses <see cref="BenchmarkConfig.Default"/> if null).</param>
    /// <param name="setup">Optional per-sample setup action (not timed).</param>
    /// <param name="teardown">Optional per-sample teardown action (not timed).</param>
    /// <returns>A <see cref="BenchmarkResult"/> containing statistics.</returns>
    public static BenchmarkResult Run(
        string name,
        Action action,
        Action? warmup,
        BenchmarkConfig? config = null,
        Action? setup = null,
        Action? teardown = null
    )
    {
        ValidateName(name, nameof(name));
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        config ??= BenchmarkConfig.Default;
        using var __priority = Runner.BoostPriorities(config.BoostPriorities);
        Runner.Initialize();

        // Warmup phase
        if (warmup != null && config.WarmupIterations > 0)
        {
            for (var i = 0; i < config.WarmupIterations; i++)
                warmup();
        }

        return CollectAndBuild(
            name,
            config,
            iterations => Runner.Time(iterations, action, setup, teardown)
        );
    }

    /// <summary>
    /// Run a benchmark with state passed to avoid closure allocation.
    /// </summary>
    /// <param name="name">Name of the benchmark.</param>
    /// <param name="state">State passed to the action, avoiding closure allocation.</param>
    /// <param name="action">The action to measure.</param>
    /// <param name="warmup">Optional warmup action. If null, no warmup is performed.</param>
    /// <param name="config">Optional configuration (uses <see cref="BenchmarkConfig.Default"/> if null).</param>
    public static BenchmarkResult Run<TState>(
        string name,
        TState state,
        Action<TState> action,
        Action<TState>? warmup = null,
        BenchmarkConfig? config = null
    )
    {
        ValidateName(name, nameof(name));
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        config ??= BenchmarkConfig.Default;
        using var __priority = Runner.BoostPriorities(config.BoostPriorities);
        Runner.Initialize();

        // Warmup phase
        if (warmup != null && config.WarmupIterations > 0)
        {
            for (int i = 0; i < config.WarmupIterations; i++)
                warmup(state);
        }

        return CollectAndBuild(name, config, iterations => Runner.Time(iterations, state, action));
    }

    /// <summary>
    /// Run a benchmark with a scope factory (creates new scope per sample).
    /// Useful for DI container benchmarks.
    /// </summary>
    public static BenchmarkResult RunScoped<TScope>(
        string name,
        Func<TScope> scopeFactory,
        Action<TScope> action,
        BenchmarkConfig? config = null
    )
        where TScope : IDisposable
    {
        ValidateName(name, nameof(name));
        if (scopeFactory == null)
            throw new ArgumentNullException(nameof(scopeFactory));
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        config ??= BenchmarkConfig.Default;
        using var __priority = Runner.BoostPriorities(config.BoostPriorities);
        Runner.Initialize();

        // Warmup phase - use a single scope
        if (config.WarmupIterations > 0)
        {
            using var warmupScope = scopeFactory();
            for (var i = 0; i < config.WarmupIterations; i++)
                action(warmupScope);
        }

        return CollectAndBuild(
            name,
            config,
            iterations =>
            {
                using var scope = scopeFactory();
                return Runner.Time(iterations, scope, action);
            }
        );
    }

    /// <summary>
    /// Run an async benchmark with a scope factory (creates new scope per
    /// sample). Useful for DI container benchmarks with async work.
    /// </summary>
    public static async Task<BenchmarkResult> RunScopedAsync<TScope>(
        string name,
        Func<TScope> scopeFactory,
        Func<TScope, Task> action,
        BenchmarkConfig? config = null
    )
        where TScope : IDisposable
    {
        ValidateName(name, nameof(name));
        if (scopeFactory == null)
            throw new ArgumentNullException(nameof(scopeFactory));
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        config ??= BenchmarkConfig.Default;
        using var __priority = Runner.BoostPriorities(config.BoostPriorities);
        Runner.Initialize();

        // Warmup phase - use a single scope
        if (config.WarmupIterations > 0)
        {
            using var warmupScope = scopeFactory();
            for (var i = 0; i < config.WarmupIterations; i++)
                await action(warmupScope);
        }

        return await CollectAndBuildAsync(
            name,
            config,
            iterations =>
            {
                using var scope = scopeFactory();
                return config.TimingMode == AsyncTimingMode.CpuOnly
                    ? Runner.TimeCpuAsync(iterations, scope, s => action(s))
                    : Runner.TimeAsync(iterations, scope, s => action(s));
            },
            // The probe is the empty state-passing delegate of the *same* scope
            // type, timed by the *same* entry point, so the deducted cost and the
            // measurement share one loop body.
            iterations =>
            {
                using var scope = scopeFactory();
                return config.TimingMode == AsyncTimingMode.CpuOnly
                    ? Runner.TimeCpuAsync(iterations, scope, Runner.HarnessProbeAsyncActionFor<TScope>())
                    : Runner.TimeAsync(iterations, scope, Runner.HarnessProbeAsyncActionFor<TScope>());
            }
        );
    }

    /// <summary>
    /// Compare two benchmarks and return a comparison result.
    /// </summary>
    public static ComparisonResult Compare(
        string name,
        BenchmarkResult baseline,
        BenchmarkResult candidate
    )
    {
        ValidateName(name, nameof(name));
        return new ComparisonResult(name: name, baseline: baseline, candidate: candidate);
    }

    /// <summary>
    /// Compare two actions directly and return a comparison result.
    /// <para>
    /// Both lanes are measured in <see cref="ComparisonMeasurement.Rounds"/>
    /// interleaved rounds (the lane that goes first alternates every round), the
    /// verdict is the median of the per-round paired ratios, and a difference is only
    /// reported when it is outside a 5% dead band and its interval excludes parity —
    /// see <see cref="ComparisonResult.Outcome"/>. A single sequential baseline-then-
    /// candidate run used to turn noise into a conclusion (Batch 0 · 0.2, B1-C1).
    /// </para>
    /// </summary>
    public static ComparisonResult Compare(
        string name,
        string baselineName,
        Action baselineAction,
        string candidateName,
        Action candidateAction,
        BenchmarkConfig? config = null
    )
    {
        ValidateName(name, nameof(name));
        ValidateName(baselineName, nameof(baselineName));
        if (baselineAction == null)
            throw new ArgumentNullException(nameof(baselineAction));
        ValidateName(candidateName, nameof(candidateName));
        if (candidateAction == null)
            throw new ArgumentNullException(nameof(candidateAction));

        var baselineRounds = new List<BenchmarkResult>(ComparisonMeasurement.Rounds);
        var candidateRounds = new List<BenchmarkResult>(ComparisonMeasurement.Rounds);

        for (var round = 0; round < ComparisonMeasurement.Rounds; round++)
        {
            // Alternate which lane runs first: otherwise the second lane always
            // inherits a warm process and wins on that alone.
            if ((round & 1) == 1)
            {
                candidateRounds.Add(Run(candidateName, candidateAction, config));
                baselineRounds.Add(Run(baselineName, baselineAction, config));
            }
            else
            {
                baselineRounds.Add(Run(baselineName, baselineAction, config));
                candidateRounds.Add(Run(candidateName, candidateAction, config));
            }
        }

        var pairedRatios = new double[ComparisonMeasurement.Rounds];
        for (var round = 0; round < ComparisonMeasurement.Rounds; round++)
        {
            pairedRatios[round] = ComparisonMeasurement.Ratio(
                baselineRounds[round].Statistics.Avg,
                candidateRounds[round].Statistics.Avg
            );
        }

        var (speedup, interval) = ComparisonMeasurement.PairedSpeedup(pairedRatios);

        return new ComparisonResult(
            name: name,
            baseline: MergeRounds(baselineName, baselineRounds),
            candidate: MergeRounds(candidateName, candidateRounds),
            category: null,
            tags: null,
            speedup: speedup,
            interval: interval
        );
    }

    /// <summary>
    /// Merges the rounds of one lane into a single result (median point estimates,
    /// run extremes, summed GC counts) so a caller still gets one
    /// <see cref="BenchmarkResult"/> per lane.
    /// </summary>
    private static BenchmarkResult MergeRounds(string name, List<BenchmarkResult> rounds)
    {
        var statistics = ComparisonMeasurement.Merge([.. rounds.Select(r => r.Statistics)]);

        List<TimingSample>? samples = null;
        if (rounds.TrueForAll(static r => r.Samples is not null))
        {
            samples = new List<TimingSample>();
            foreach (var round in rounds)
                samples.AddRange(round.Samples!);
        }

        return new BenchmarkResult(
            name: name,
            statistics: statistics,
            iterationsPerSample: rounds[0].IterationsPerSample,
            sampleCount: rounds.Sum(static r => r.SampleCount),
            samples: samples
        );
    }

    #region Private Helpers

    /// <summary>
    /// Validate that a name parameter is not null or whitespace.
    /// </summary>
    private static void ValidateName(string name, string paramName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"{paramName} cannot be null or whitespace.", paramName);
    }

    /// <summary>
    /// Force a full GC to establish a clean baseline before the collection phase.
    /// </summary>
    private static void ForceGc()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
    }

    /// <summary>
    /// How many empty delegates are timed to estimate the harness overhead of a
    /// run. The minimum is used, so extra repetitions only cost time.
    /// </summary>
    private const int HarnessOverheadRepetitions = 5;

    /// <summary>
    /// Run the collection phase, compute statistics, and build the result.
    /// </summary>
    private static BenchmarkResult CollectAndBuild(
        string name,
        BenchmarkConfig config,
        Func<int, TimingSample> sampleFunc
    )
    {
        if (config.ForceGcBeforeBenchmark)
            ForceGc();

        var iterationsPerSample = ResolveIterationsPerSample(config, sampleFunc);
        var overhead = MeasureOverhead(iterationsPerSample);

        var samples = new TimingSample[config.SampleCount];
        var perOpTimes = new double[config.SampleCount];
        var perOpCycles = new double[config.SampleCount];

        for (var s = 0; s < config.SampleCount; s++)
        {
            var sample = sampleFunc(iterationsPerSample);
            samples[s] = sample;
            perOpTimes[s] = PerOpNanoseconds(sample, iterationsPerSample, overhead.Nanoseconds);
            perOpCycles[s] = (double)sample.CpuCycles / iterationsPerSample;
        }

        var stats = StatisticsCalculator.Compute(
            perOpTimes,
            perOpCycles,
            samples,
            overhead.Nanoseconds / iterationsPerSample,
            overhead.ResolutionLimitNanoseconds
        );

        return new BenchmarkResult(
            name: name,
            statistics: stats,
            iterationsPerSample: iterationsPerSample,
            sampleCount: config.SampleCount,
            samples: config.RetainSamples ? samples : null
        );
    }

    /// <summary>
    /// Measures the fixed cost a sample carries regardless of the measured
    /// action, using the same iteration count as the run itself, and turns it
    /// into the two numbers the statistics need: the total to deduct and the
    /// smallest per-operation value this run can still resolve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <b>deducted</b> amount is the smallest of the empty-delegate probes,
    /// because noise can only inflate a measurement and the minimum is therefore
    /// the best estimate of the additive cost.
    /// </para>
    /// <para>
    /// The <b>resolution limit</b> is the opposite quantity: an upper bound the
    /// user can trust. <see cref="PerOpNanoseconds"/> clamps the residual at zero,
    /// so a sample whose own overhead happened to exceed the deducted floor loses
    /// that difference — and a limit built only from the clock tick and the probe
    /// <i>spread</i> ignored it, printing <c>&lt;0.02 ns</c> for a true 0.3 ns/op
    /// (G4 · B2). Any sample satisfies
    /// <c>true per-op ≤ residual/iterations + overhead/iterations</c> (the
    /// overhead is never negative), so the limit has to carry the per-operation
    /// size of the amount that was subtracted; the largest probe is used, since the
    /// residual is clamped against the smallest one. Without that term the claim
    /// "this costs less than <c>limit</c>" can be smaller than the harness floor
    /// the very same run deducted.
    /// </para>
    /// </remarks>
    private static (double Nanoseconds, double ResolutionLimitNanoseconds) MeasureOverhead(
        int iterationsPerSample
    )
    {
        var harness = Runner.MeasureHarnessOverhead(iterationsPerSample, HarnessOverheadRepetitions);
        var spreadPerOp =
            Math.Max(0.0, harness.MaxNanoseconds - harness.MinNanoseconds) / iterationsPerSample;
        var clockTickPerOp =
            1_000_000_000.0 / Stopwatch.Frequency / iterationsPerSample;
        var overheadUpperBoundPerOp =
            Math.Max(0.0, harness.MaxNanoseconds) / iterationsPerSample;

        return (
            harness.MinNanoseconds,
            Math.Max(clockTickPerOp, Math.Max(spreadPerOp, overheadUpperBoundPerOp))
        );
    }

    /// <summary>
    /// Per-operation nanoseconds of a sample with the harness overhead removed.
    /// A measurement can never be negative, so the residual is clamped at zero.
    /// </summary>
    private static double PerOpNanoseconds(
        TimingSample sample,
        int iterationsPerSample,
        double overheadNanoseconds
    )
    {
        var net = sample.ElapsedNanoseconds - overheadNanoseconds;
        return (net > 0 ? net : 0.0) / iterationsPerSample;
    }

    private static int ResolveIterationsPerSample(
        BenchmarkConfig config,
        Func<int, TimingSample> sampleFunc
    )
    {
        if (!config.AutoCalibrateIterations)
            return config.IterationsPerSample;

        var iterations = config.IterationsPerSample;
        var minSampleNanoseconds = Math.Max(
            config.MinSampleTime.TotalMilliseconds * 1_000_000.0,
            1.0
        );

        // Discard the first probe: it may include JIT compilation of the
        // measured delegate, which would skew the calibration downwards.
        _ = sampleFunc(iterations);

        while (iterations < config.MaxAutoIterationsPerSample)
        {
            var sample = sampleFunc(iterations);
            if (sample.ElapsedNanoseconds >= minSampleNanoseconds)
                return iterations;

            var scale = minSampleNanoseconds / Math.Max(sample.ElapsedNanoseconds, 1.0);
            var nextIterations = (int)
                Math.Min(
                    config.MaxAutoIterationsPerSample,
                    Math.Max(
                        iterations + 1,
                        Math.Ceiling(iterations * Math.Min(Math.Max(scale, 2.0), 10.0))
                    )
                );

            if (nextIterations <= iterations)
                break;
            iterations = nextIterations;
        }

        return iterations;
    }

    /// <summary>
    /// Resolves the minimum per-sample time target for auto-calibration.
    /// In <see cref="AsyncTimingMode.CpuOnly"/> mode the process CPU clock
    /// only advances in full timer ticks, so the target is raised to at least
    /// the measured clock granularity (otherwise calibration probes read zero).
    /// </summary>
    internal static double ResolveMinSampleNanoseconds(
        BenchmarkConfig config,
        TimeSpan cpuClockGranularity
    )
    {
        var minSampleTime = config.MinSampleTime;
        if (config.TimingMode == AsyncTimingMode.CpuOnly && cpuClockGranularity > minSampleTime)
        {
            minSampleTime = cpuClockGranularity;
        }

        return Math.Max(minSampleTime.TotalMilliseconds * 1_000_000.0, 1.0);
    }

    /// <summary>
    /// Run an async benchmark with the given action.
    /// </summary>
    public static Task<BenchmarkResult> RunAsync(
        string name,
        Func<Task> action,
        BenchmarkConfig? config = null
    )
    {
        ValidateName(name, nameof(name));
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        return RunAsync(name, action, warmup: action, config);
    }

    /// <summary>
    /// Run an async benchmark with separate warmup, setup, and teardown.
    /// </summary>
    public static async Task<BenchmarkResult> RunAsync(
        string name,
        Func<Task> action,
        Func<Task>? warmup,
        BenchmarkConfig? config = null,
        Func<Task>? setup = null,
        Func<Task>? teardown = null
    )
    {
        ValidateName(name, nameof(name));
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        config ??= BenchmarkConfig.Default;
        using var __priority = Runner.BoostPriorities(config.BoostPriorities);
        Runner.Initialize();

        // Warmup phase
        if (warmup != null && config.WarmupIterations > 0)
        {
            for (int i = 0; i < config.WarmupIterations; i++)
                await warmup();
        }

        return await CollectAndBuildAsync(
            name,
            config,
            iterations => DispatchAsyncTiming(iterations, action, setup, teardown, config),
            // Setup and teardown are awaited outside the timed loop, so probing
            // without them walks the identical inner path.
            iterations =>
                DispatchAsyncTiming(
                    iterations,
                    Runner.HarnessProbeAsyncAction,
                    setup: null,
                    teardown: null,
                    config
                )
        );
    }

    /// <summary>
    /// Run an async benchmark with state passed.
    /// </summary>
    /// <param name="name">Name of the benchmark.</param>
    /// <param name="state">State passed to the action, avoiding closure allocation.</param>
    /// <param name="action">The action to measure.</param>
    /// <param name="warmup">Optional warmup action. If null, no warmup is performed.</param>
    /// <param name="config">Optional configuration (uses <see cref="BenchmarkConfig.Default"/> if null).</param>
    public static async Task<BenchmarkResult> RunAsync<TState>(
        string name,
        TState state,
        Func<TState, Task> action,
        Func<TState, Task>? warmup = null,
        BenchmarkConfig? config = null
    )
    {
        ValidateName(name, nameof(name));
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        config ??= BenchmarkConfig.Default;
        using var __priority = Runner.BoostPriorities(config.BoostPriorities);
        Runner.Initialize();

        if (warmup != null && config.WarmupIterations > 0)
        {
            for (int i = 0; i < config.WarmupIterations; i++)
                await warmup(state);
        }

        return await CollectAndBuildAsync(
            name,
            config,
            config.TimingMode == AsyncTimingMode.CpuOnly
                ? iterations => Runner.TimeCpuAsync(iterations, state, action)
                : iterations => Runner.TimeAsync(iterations, state, action),
            config.TimingMode == AsyncTimingMode.CpuOnly
                ? iterations =>
                    Runner.TimeCpuAsync(
                        iterations,
                        state,
                        Runner.HarnessProbeAsyncActionFor<TState>()
                    )
                : iterations =>
                    Runner.TimeAsync(iterations, state, Runner.HarnessProbeAsyncActionFor<TState>())
        );
    }

    private static Task<TimingSample> DispatchAsyncTiming(
        int iterations,
        Func<Task> action,
        Func<Task>? setup,
        Func<Task>? teardown,
        BenchmarkConfig config
    )
    {
        return config.TimingMode == AsyncTimingMode.CpuOnly
            ? Runner.TimeCpuAsync(iterations, action, setup, teardown)
            : Runner.TimeAsync(iterations, action, setup, teardown);
    }

    /// <summary>
    /// Asynchronous counterpart of <see cref="MeasureOverhead(int)"/>: times the
    /// empty-delegate probe with the same iteration count and the same entry point
    /// as the run, and returns the amount to deduct together with the smallest
    /// per-operation value the run can still resolve.
    /// <para>
    /// The deducted amount is the smallest probe, for the reason given on
    /// <see cref="MeasureOverhead(int)"/>; the limit additionally carries the
    /// per-operation harness floor, the probe spread and the quantisation of the
    /// timing source this run reads, so it stays an upper bound even when the
    /// residual was clamped at zero.
    /// </para>
    /// </summary>
    private static async Task<(
        double Nanoseconds,
        double ResolutionLimitNanoseconds
    )> MeasureOverheadAsync(
        int iterationsPerSample,
        Func<int, Task<TimingSample>> probeFuncAsync,
        double quantumPerOp
    )
    {
        var harness = await Runner.MeasureHarnessOverheadAsync(
            iterationsPerSample,
            HarnessOverheadRepetitions,
            probeFuncAsync
        );

        var clockTickPerOp = 1_000_000_000.0 / Stopwatch.Frequency / iterationsPerSample;
        var spreadPerOp =
            Math.Max(0.0, harness.MaxNanoseconds - harness.MinNanoseconds) / iterationsPerSample;
        var overheadUpperBoundPerOp =
            Math.Max(0.0, harness.MaxNanoseconds) / iterationsPerSample;

        return (
            harness.MinNanoseconds,
            Math.Max(
                Math.Max(quantumPerOp, clockTickPerOp),
                Math.Max(spreadPerOp, overheadUpperBoundPerOp)
            )
        );
    }

    /// <summary>
    /// Per-operation nanoseconds of a sample, never a "measured zero": a sample
    /// whose clock did not advance is reported as one quantum of the timing
    /// source, the smallest value that source can express. The sample itself stays
    /// flagged as unmeasured (<see cref="Statistics.Unmeasured"/>), so the value is
    /// disclosed as "not measured" rather than published as a fast operation.
    /// <para>
    /// <paramref name="overheadNanoseconds"/> is the harness cost measured for this
    /// run and is subtracted first — in the asynchronous path it used to be 0, so
    /// the whole async floor stayed attributed to the measured action. A sample
    /// whose residual is not positive resolves to the larger of the quantum and the
    /// per-operation harness floor: it may never claim a cost smaller than what the
    /// harness itself adds.
    /// </para>
    /// </summary>
    private static double PerOpNanosecondsWithFloor(
        TimingSample sample,
        int iterationsPerSample,
        double quantumPerOp,
        double overheadNanoseconds = 0
    )
    {
        var net = sample.ElapsedNanoseconds - overheadNanoseconds;
        if (net > 0)
            return net / iterationsPerSample;

        var overheadPerOp = Math.Max(0.0, overheadNanoseconds) / iterationsPerSample;
        return Math.Max(quantumPerOp, overheadPerOp);
    }

    /// <summary>
    /// Smallest time span the asynchronous timing source can express, in
    /// nanoseconds: the measured granularity of the process CPU clock in
    /// <see cref="AsyncTimingMode.CpuOnly"/> mode (0 elsewhere, where the source
    /// already resolves nanoseconds).
    /// </summary>
    internal static double ResolveTimingQuantumNanoseconds(BenchmarkConfig config) =>
        config.TimingMode == AsyncTimingMode.CpuOnly
            ? Runner.GetCpuClockGranularity().Ticks * 100.0
            : 0.0;

    /// <summary>
    /// Asynchronous counterpart of
    /// <see cref="CollectAndBuild(string, BenchmarkConfig, Func{int, TimingSample})"/>.
    /// <para>
    /// <paramref name="probeFuncAsync"/> is the empty-delegate probe of the same
    /// shape as <paramref name="sampleFuncAsync"/> (same scope/state type, same
    /// timing entry point). It is what makes the deduced overhead real: Batch 0 ·
    /// 0.1b measured the async floor at 0.65-2.7 ns/op that this path attributed
    /// to the measured action. When it is null no deduction is made and the result
    /// says so (<c>OverheadNs = 0</c>).
    /// </para>
    /// </summary>
    private static async Task<BenchmarkResult> CollectAndBuildAsync(
        string name,
        BenchmarkConfig config,
        Func<int, Task<TimingSample>> sampleFuncAsync,
        Func<int, Task<TimingSample>>? probeFuncAsync = null
    )
    {
        if (config.ForceGcBeforeBenchmark)
            ForceGc();

        var iterationsPerSample = await ResolveIterationsPerSampleAsync(config, sampleFuncAsync);

        // Floor of the timing source this run uses. CpuOnly reads the process CPU
        // clock, which advances in whole timer ticks: one sample can never be
        // resolved below one tick, and a sub-tick sample reads as exactly zero.
        // WallClock reads Stopwatch, whose tick is already the nanosecond scale.
        var quantumPerOp = ResolveTimingQuantumNanoseconds(config) / iterationsPerSample;

        var overhead = probeFuncAsync is null
            ? default
            : await MeasureOverheadAsync(iterationsPerSample, probeFuncAsync, quantumPerOp);

        var samples = new TimingSample[config.SampleCount];
        var perOpTimes = new double[config.SampleCount];
        var perOpCycles = new double[config.SampleCount];

        for (var s = 0; s < config.SampleCount; s++)
        {
            config.CancellationToken.ThrowIfCancellationRequested();

            var sample = await sampleFuncAsync(iterationsPerSample);
            samples[s] = sample;
            perOpTimes[s] = PerOpNanosecondsWithFloor(
                sample,
                iterationsPerSample,
                quantumPerOp,
                overhead.Nanoseconds
            );
            perOpCycles[s] = (double)sample.CpuCycles / iterationsPerSample;
        }

        var stats = StatisticsCalculator.Compute(
            perOpTimes,
            perOpCycles,
            samples,
            harnessOverheadNanoseconds: overhead.Nanoseconds,
            resolutionLimitNanoseconds: overhead.ResolutionLimitNanoseconds
        );

        return new BenchmarkResult(
            name: name,
            statistics: stats,
            iterationsPerSample: iterationsPerSample,
            sampleCount: config.SampleCount,
            samples: config.RetainSamples ? samples : null
        );
    }

    private static async Task<int> ResolveIterationsPerSampleAsync(
        BenchmarkConfig config,
        Func<int, Task<TimingSample>> sampleFuncAsync
    )
    {
        if (!config.AutoCalibrateIterations)
            return config.IterationsPerSample;

        var iterations = config.IterationsPerSample;
        var minSampleNanoseconds = ResolveMinSampleNanoseconds(
            config,
            Runner.GetCpuClockGranularity()
        );

        // Discard the first probe: it may include JIT compilation of the
        // measured delegate, which would skew the calibration downwards.
        await sampleFuncAsync(iterations);

        while (iterations < config.MaxAutoIterationsPerSample)
        {
            var sample = await sampleFuncAsync(iterations);
            if (sample.ElapsedNanoseconds >= minSampleNanoseconds)
                return iterations;

            var scale = minSampleNanoseconds / Math.Max(sample.ElapsedNanoseconds, 1.0);
            var nextIterations = (int)
                Math.Min(
                    config.MaxAutoIterationsPerSample,
                    Math.Max(
                        iterations + 1,
                        Math.Ceiling(iterations * Math.Min(Math.Max(scale, 2.0), 10.0))
                    )
                );

            if (nextIterations <= iterations)
                break;
            iterations = nextIterations;
        }

        return iterations;
    }

    #endregion
}
