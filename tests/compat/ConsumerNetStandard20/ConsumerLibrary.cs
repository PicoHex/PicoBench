using System.Threading.Tasks;
using PicoBench;

namespace ConsumerNetStandard20
{
    /// <summary>
    /// A netstandard2.0 library that owns a generated benchmark class and drives
    /// it through the generated entry point (not only the imperative API), which
    /// is where netstandard2.0-hostile emitted code surfaces.
    /// </summary>
    public sealed class ConsumerBenchmarks
    {
        public async Task<int> CountAsync()
        {
            var instance = new LibraryBenchmarks();
            BenchmarkSuite suite = await instance.RunBenchmarksAsync(BenchmarkConfig.Quick).ConfigureAwait(false);
            return suite.Results.Count;
        }
    }

    [BenchmarkClass]
    public partial class LibraryBenchmarks
    {
        [Benchmark(Baseline = true)]
        public void Baseline()
        {
        }

        [Benchmark]
        public void Candidate()
        {
        }
    }
}
