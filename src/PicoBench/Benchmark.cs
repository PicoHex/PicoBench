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

        var baseline = Run(baselineName, baselineAction, config);
        var candidate = Run(candidateName, candidateAction, config);

        return new ComparisonResult(name: name, baseline: baseline, candidate: candidate);
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

        var samples = new TimingSample[config.SampleCount];
        var perOpTimes = new double[config.SampleCount];
        var perOpCycles = new double[config.SampleCount];

        for (var s = 0; s < config.SampleCount; s++)
        {
            var sample = sampleFunc(iterationsPerSample);
            samples[s] = sample;
            perOpTimes[s] = sample.ElapsedNanoseconds / iterationsPerSample;
            perOpCycles[s] = (double)sample.CpuCycles / iterationsPerSample;
        }

        var stats = StatisticsCalculator.Compute(perOpTimes, perOpCycles, samples);

        return new BenchmarkResult(
            name: name,
            statistics: stats,
            iterationsPerSample: iterationsPerSample,
            sampleCount: config.SampleCount,
            samples: config.RetainSamples ? samples : null
        );
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
            iterations => DispatchAsyncTiming(iterations, action, setup, teardown, config)
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
                : iterations => Runner.TimeAsync(iterations, state, action)
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

    private static async Task<BenchmarkResult> CollectAndBuildAsync(
        string name,
        BenchmarkConfig config,
        Func<int, Task<TimingSample>> sampleFuncAsync
    )
    {
        if (config.ForceGcBeforeBenchmark)
            ForceGc();

        var iterationsPerSample = await ResolveIterationsPerSampleAsync(config, sampleFuncAsync);

        var samples = new TimingSample[config.SampleCount];
        var perOpTimes = new double[config.SampleCount];
        var perOpCycles = new double[config.SampleCount];

        for (var s = 0; s < config.SampleCount; s++)
        {
            config.CancellationToken.ThrowIfCancellationRequested();

            var sample = await sampleFuncAsync(iterationsPerSample);
            samples[s] = sample;
            perOpTimes[s] = sample.ElapsedNanoseconds / iterationsPerSample;
            perOpCycles[s] = (double)sample.CpuCycles / iterationsPerSample;
        }

        var stats = StatisticsCalculator.Compute(perOpTimes, perOpCycles, samples);

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
        var minSampleNanoseconds = Math.Max(
            config.MinSampleTime.TotalMilliseconds * 1_000_000.0,
            1.0
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
