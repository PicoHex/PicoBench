namespace PicoBench;

/// <summary>
/// Interface implemented by source-generated benchmark classes.
/// The source generator adds this interface to any class decorated with
/// <see cref="BenchmarkClassAttribute"/>, emitting a full <see cref="RunBenchmarksAsync"/>
/// implementation that is AOT-compatible (no reflection).
/// </summary>
public interface IBenchmarkClass
{
    /// <summary>
    /// Runs all <see cref="BenchmarkAttribute"/>-marked methods in this class
    /// and returns a <see cref="BenchmarkSuite"/> containing the results.
    /// </summary>
    /// <param name="config">
    /// Optional configuration. Defaults to <see cref="BenchmarkConfig.Default"/> when <c>null</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that completes with the suite
    /// containing individual results and any comparisons.
    /// For pure sync classes, returns <c>ValueTask.FromResult</c>
    /// with zero heap allocation.
    /// </returns>
    ValueTask<BenchmarkSuite> RunBenchmarksAsync(BenchmarkConfig? config = null);
}
