// The benign half of the AOT fixture: it must publish cleanly with the CI flag
// set. Without it, a mistyped publish command would "pass" the negative half for
// the wrong reason, so the harness only trusts the negative result once this
// program has proved the command works.
using System.Threading;
using PicoBench;

var suite = BenchmarkRunner.Run<SampleBenchmarks>(BenchmarkConfig.Quick);
Console.WriteLine($"Benchmarks: {suite.Results.Count}");

[BenchmarkClass]
public partial class SampleBenchmarks
{
    [Benchmark(Baseline = true)]
    public void Baseline() => Thread.SpinWait(32);

    [Benchmark]
    public void Candidate() => Thread.SpinWait(16);
}
