// ─── Async Benchmark Sample ────────────────────────────────────────
// Demonstrates async lifecycle methods ([GlobalSetup], [GlobalCleanup],
// [IterationSetup], [IterationCleanup]) with Task return types.
// Also shows async benchmark methods ([Benchmark] returning Task) and
// mixed sync/async usage within the same class.
//
// Uses BenchmarkRunner.RunAsync<T>() — the fully async entry point.
// ─────────────────────────────────────────────────────────────────────

var suite = await BenchmarkRunner.RunAsync<FileSimulationBenchmarks>(BenchmarkConfig.Quick);

// Console output
Console.WriteLine(new ConsoleFormatter().Format(suite));

// Summary
if (suite.Comparisons is not null)
    Console.WriteLine(SummaryFormatter.Format(suite.Comparisons));

// Save results
var outputDir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(outputDir);
File.WriteAllText(Path.Combine(outputDir, "results.md"), new MarkdownFormatter().Format(suite));
Console.WriteLine($"\nResults saved to: {outputDir}");

// ─────────────────────────────────────────────────────────────────────
// Benchmark class — mixed sync/async lifecycle and benchmark methods.
// The source generator turns this into a full runner automatically.
// ─────────────────────────────────────────────────────────────────────

[BenchmarkClass(Description = "Simulating async I/O operations")]
public partial class FileSimulationBenchmarks
{
    private string? _tempPath;

    [GlobalSetup]
    public void SetupSync()
    {
        // Sync setup: create a temp file for shared fixture
        _tempPath = Path.GetTempFileName();
        File.WriteAllText(_tempPath, new string('x', 10_000));
    }

    [GlobalCleanup]
    public void CleanupSync()
    {
        // Sync cleanup: delete the temp file
        if (_tempPath is not null && File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    [IterationSetup]
    public async Task PrepareAsync()
    {
        // Async iteration setup: simulate warm-up I/O
        await Task.Delay(1);
    }

    /// <summary>
    /// Baseline: sequential async reads (simulated).
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task SequentialReadsAsync()
    {
        if (_tempPath is null) return;

        await File.ReadAllTextAsync(_tempPath);
        await File.ReadAllTextAsync(_tempPath);
        await File.ReadAllTextAsync(_tempPath);
    }

    /// <summary>
    /// Candidate: parallel async reads via Task.WhenAll.
    /// </summary>
    [Benchmark]
    public async Task ParallelReadsAsync()
    {
        if (_tempPath is null) return;

        var t1 = File.ReadAllTextAsync(_tempPath);
        var t2 = File.ReadAllTextAsync(_tempPath);
        var t3 = File.ReadAllTextAsync(_tempPath);
        await Task.WhenAll(t1, t2, t3);
    }

    /// <summary>
    /// Candidate: sync read (contrast with async I/O methods above).
    /// </summary>
    [Benchmark]
    public void SyncRead()
    {
        if (_tempPath is null) return;

        for (var i = 0; i < 3; i++)
            _ = File.ReadAllText(_tempPath);
    }
}
