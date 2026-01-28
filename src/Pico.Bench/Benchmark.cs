namespace Pico.Bench;

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
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Benchmark name cannot be null or whitespace.",
                nameof(name)
            );
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        return Run(name, action, warmup: action, config);
    }

    /// <summary>
    /// Run a benchmark with separate warmup and measured actions.
    /// </summary>
    public static BenchmarkResult Run(
        string name,
        Action action,
        Action? warmup,
        BenchmarkConfig? config = null
    )
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Benchmark name cannot be null or whitespace.",
                nameof(name)
            );
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        config ??= BenchmarkConfig.Default;

        // Initialize runner if not already done
        Runner.Initialize();

        // Warmup phase
        if (warmup != null && config.WarmupIterations > 0)
        {
            for (var i = 0; i < config.WarmupIterations; i++)
                warmup();
        }

        // Collection phase
        var samples = new TimingSample[config.SampleCount];
        var perOpTimes = new double[config.SampleCount];
        var perOpCycles = new double[config.SampleCount];

        for (int s = 0; s < config.SampleCount; s++)
        {
            var sample = Runner.Time(config.IterationsPerSample, action);
            samples[s] = sample;
            perOpTimes[s] = sample.ElapsedNanoseconds / config.IterationsPerSample;
            perOpCycles[s] = (double)sample.CpuCycles / config.IterationsPerSample;
        }

        // Compute statistics
        var stats = StatisticsCalculator.Compute(perOpTimes, perOpCycles, samples);

        return new BenchmarkResult(
            name: name,
            statistics: stats,
            iterationsPerSample: config.IterationsPerSample,
            sampleCount: config.SampleCount,
            samples: config.RetainSamples ? samples : null
        );
    }

    /// <summary>
    /// Run a benchmark with state passed to avoid closure allocation.
    /// </summary>
    public static BenchmarkResult Run<TState>(
        string name,
        TState state,
        Action<TState> action,
        Action<TState>? warmup = null,
        BenchmarkConfig? config = null
    )
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Benchmark name cannot be null or whitespace.",
                nameof(name)
            );
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
        else if (config.WarmupIterations > 0)
        {
            for (var i = 0; i < config.WarmupIterations; i++)
                action(state);
        }

        // Collection phase
        var samples = new TimingSample[config.SampleCount];
        var perOpTimes = new double[config.SampleCount];
        var perOpCycles = new double[config.SampleCount];

        for (var s = 0; s < config.SampleCount; s++)
        {
            var sample = Runner.Time(config.IterationsPerSample, state, action);
            samples[s] = sample;
            perOpTimes[s] = sample.ElapsedNanoseconds / config.IterationsPerSample;
            perOpCycles[s] = (double)sample.CpuCycles / config.IterationsPerSample;
        }

        var stats = StatisticsCalculator.Compute(perOpTimes, perOpCycles, samples);

        return new BenchmarkResult(
            name: name,
            statistics: stats,
            iterationsPerSample: config.IterationsPerSample,
            sampleCount: config.SampleCount,
            samples: config.RetainSamples ? samples : null
        );
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
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Benchmark name cannot be null or whitespace.",
                nameof(name)
            );
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

        // Collection phase - one scope per sample
        var samples = new TimingSample[config.SampleCount];
        var perOpTimes = new double[config.SampleCount];
        var perOpCycles = new double[config.SampleCount];

        for (int s = 0; s < config.SampleCount; s++)
        {
            using var scope = scopeFactory();
            var sample = Runner.Time(config.IterationsPerSample, scope, action);
            samples[s] = sample;
            perOpTimes[s] = sample.ElapsedNanoseconds / config.IterationsPerSample;
            perOpCycles[s] = (double)sample.CpuCycles / config.IterationsPerSample;
        }

        var stats = StatisticsCalculator.Compute(perOpTimes, perOpCycles, samples);

        return new BenchmarkResult(
            name: name,
            statistics: stats,
            iterationsPerSample: config.IterationsPerSample,
            sampleCount: config.SampleCount,
            samples: config.RetainSamples ? samples : null
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
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Comparison name cannot be null or whitespace.",
                nameof(name)
            );

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
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Comparison name cannot be null or whitespace.",
                nameof(name)
            );
        if (string.IsNullOrWhiteSpace(baselineName))
            throw new ArgumentException(
                "Baseline name cannot be null or whitespace.",
                nameof(baselineName)
            );
        if (baselineAction == null)
            throw new ArgumentNullException(nameof(baselineAction));
        if (string.IsNullOrWhiteSpace(candidateName))
            throw new ArgumentException(
                "Candidate name cannot be null or whitespace.",
                nameof(candidateName)
            );
        if (candidateAction == null)
            throw new ArgumentNullException(nameof(candidateAction));

        var baseline = Run(baselineName, baselineAction, config);
        var candidate = Run(candidateName, candidateAction, config);

        return new ComparisonResult(name: name, baseline: baseline, candidate: candidate);
    }
}
