namespace PicoBench;

/// <summary>
/// Static helper for running attribute-based benchmarks.
/// Provides generic <c>Run{T}</c> and <c>RunAsync{T}</c> entry points.
/// </summary>
public static class BenchmarkRunner
{
    /// <summary>
    /// Creates a new instance of <typeparamref name="T"/> and runs all benchmarks
    /// asynchronously.
    /// </summary>
    /// <typeparam name="T">
    /// A <see cref="BenchmarkClassAttribute"/>-decorated partial class.
    /// </typeparam>
    /// <returns>A task that completes with the <see cref="BenchmarkSuite"/>.</returns>
    public static async Task<BenchmarkSuite> RunAsync<T>(BenchmarkConfig? config = null)
        where T : IBenchmarkClass, new()
    {
        return await new T().RunBenchmarksAsync(config);
    }

    /// <summary>
    /// Runs all benchmarks on an existing instance asynchronously.
    /// </summary>
    public static async Task<BenchmarkSuite> RunAsync<T>(T instance,
        BenchmarkConfig? config = null)
        where T : IBenchmarkClass
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        return await instance.RunBenchmarksAsync(config);
    }

    /// <summary>
    /// Synchronous shortcut. Blocks the calling thread —
    /// avoid in UI/SynchronizationContext environments.
    /// </summary>
    public static BenchmarkSuite Run<T>(BenchmarkConfig? config = null)
        where T : IBenchmarkClass, new()
    {
        return new T().RunBenchmarksAsync(config).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Synchronous shortcut. Blocks the calling thread —
    /// avoid in UI/SynchronizationContext environments.
    /// </summary>
    public static BenchmarkSuite Run<T>(T instance, BenchmarkConfig? config = null)
        where T : IBenchmarkClass
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        return instance.RunBenchmarksAsync(config).GetAwaiter().GetResult();
    }
}
