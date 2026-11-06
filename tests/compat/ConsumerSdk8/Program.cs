using System;
using PicoBench;

// Compiles only if the source generator ran inside this (SDK 8) compiler: the
// generated partial supplies IBenchmarkClass and RunBenchmarksAsync.
internal static class Program
{
    private static int Main()
    {
        BenchmarkSuite suite = BenchmarkRunner.Run<Sdk8Benchmarks>(BenchmarkConfig.Quick);
        Console.WriteLine($"SDK: {Environment.Version}");
        Console.WriteLine($"Benchmarks: {suite.Results.Count}");
        return suite.Results.Count == 2 ? 0 : 1;
    }
}

[BenchmarkClass]
public partial class Sdk8Benchmarks
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
