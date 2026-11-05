using System;
using PicoBench;

namespace PicoBench.Net4xSmoke
{
    /// <summary>
    /// Runs one suite on .NET Framework and prints the result count, mirroring
    /// what the AOT job asserts on .NET 10. If the netstandard2.0 assembly (or
    /// the generated code) were unusable on net4x, this would not compile or
    /// would throw at the first measurement.
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            BenchmarkSuite suite = BenchmarkRunner.Run<Net4xBenchmarks>(BenchmarkConfig.Quick);
            Console.WriteLine("Framework: " + Environment.Version);
            Console.WriteLine("Benchmarks: " + suite.Results.Count);
            return suite.Results.Count == 2 ? 0 : 1;
        }
    }

    [BenchmarkClass]
    public partial class Net4xBenchmarks
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
