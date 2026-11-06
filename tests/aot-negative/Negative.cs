// The hostile half of the AOT fixture: one call whose safety the trimmer / AOT
// compiler cannot prove. `Assembly.LoadFrom` carries
// RequiresUnreferencedCodeAttribute, so with ILLinkTreatWarningsAsErrors /
// IlcTreatWarningsAsErrors on it is IL2026 and the publish MUST fail. The
// assembly it names does not exist - the build is meant to stop first.
using System.Reflection;
using System.Threading;
using PicoBench;

var loaded = Assembly.LoadFrom("plugin.dll");
Console.WriteLine($"loaded: {loaded.FullName}");

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
