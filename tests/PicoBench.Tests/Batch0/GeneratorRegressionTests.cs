namespace PicoBench.Tests.Batch0;

using Microsoft.CodeAnalysis;

/// <summary>
/// Batch 0 · 0.0 — "known cost" net, part 5: compile-level guards for the
/// source generator. These are the "already green" half of B4's K3 finding
/// (the generator is correct today, but nothing compiles what it emits) plus
/// the one emitter bug the net can pin without a build: the
/// <c>ValueTask.FromResult</c> call that makes a netstandard2.0 / net472
/// consumer fail with <c>CS0117</c> (B3-C5 / K4).
/// </summary>
public class GeneratorRegressionTests
{
    /// <summary>
    /// A "full shape" class: namespace, two <c>[Params]</c> axes, all four
    /// lifecycle hooks, a baseline sync benchmark and an async benchmark.
    /// </summary>
    private const string FullShapeSource = """
        using System;
        using System.Threading.Tasks;
        using PicoBench;

        namespace Demo
        {
            [BenchmarkClass(Description = "full shape")]
            public partial class Full
            {
                [Params(10, 100)]
                public int N;

                [Params("a", "b")]
                public string Mode = "a";

                [GlobalSetup]
                public void GlobalSetup() { }

                [GlobalCleanup]
                public void GlobalCleanup() { }

                [IterationSetup]
                public void IterSetup() { }

                [Benchmark(Baseline = true)]
                public void Sync1() { _ = N + Mode.Length; }

                [Benchmark]
                public async Task Async1() { await Task.Yield(); _ = N; }
            }
        }
        """;

    /// <summary>
    /// Batch 0 · 0.0 (guard for 0.6/0.7) — the emitted code must compile.
    /// <para>Expected at HEAD: <b>GREEN</b> (0 generator diagnostics, 1 source,
    /// 0 compiler errors). This is the guard that makes the Batch 1 escape
    /// fixes (identifier/literal escaping, <c>typeof</c> params, ns2.0 TFM)
    /// verifiable instead of string-snapshot-only.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.6-0.7 guard")]
    public async Task Generator_EmittedCode_Compiles()
    {
        var output = GeneratorHarness.RunGenerator(FullShapeSource);

        foreach (var diagnostic in output.Diagnostics)
            Batch0Support.Log("generator: " + GeneratorHarness.Describe(diagnostic));

        Batch0Support.Log(
            $"generated sources: [{string.Join(", ", output.HintNames)}]"
        );

        // The generator must have produced something, and must not have
        // reported an error of its own.
        await Assert.That(output.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
            .IsFalse();
        await Assert.That(output.Sources.Length).IsEqualTo(1);

        var errors = GeneratorHarness.CompileErrors(FullShapeSource, output);
        foreach (var error in errors)
            Batch0Support.Log("compiler: " + GeneratorHarness.Describe(error));

        Batch0Support.Log($"compiler errors on generated code: {errors.Count}");

        await Assert.That(errors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Batch 0 · 0.7 (B3-C5 / K4) — a class with no async benchmark must be
    /// wrapped in a <c>ValueTask&lt;BenchmarkSuite&gt;</c> <b>constructor</b>,
    /// never in <c>ValueTask.FromResult</c>: <c>FromResult</c> does not exist on
    /// netstandard2.0 / .NET Framework 4.x, so a consuming library that targets
    /// them fails to compile with <c>CS0117</c>.
    /// <para>Expected at HEAD: <b>RED</b> — <c>Emitter.cs:229</c> emits
    /// <c>return global::System.Threading.Tasks.ValueTask.FromResult(__suite);</c>
    /// for every purely synchronous <c>[BenchmarkClass]</c>.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.7")]
    public async Task Generator_EmitsValueTaskCtor_NotFromResult()
    {
        const string pureSyncSource = """
            using PicoBench;

            [BenchmarkClass]
            public partial class PureSync
            {
                [Benchmark]
                public void Work() { }
            }
            """;

        var output = GeneratorHarness.RunGenerator(pureSyncSource);
        await Assert.That(output.Sources.Length).IsEqualTo(1);

        var code = GeneratorHarness.AllSources(output);
        var usesFromResult = code.Contains("ValueTask.FromResult", StringComparison.Ordinal);

        Batch0Support.Log($"hint: {output.HintNames[0]}");
        Batch0Support.Log($"contains ValueTask.FromResult: {usesFromResult}");
        foreach (var line in code.Split('\n'))
        {
            if (line.Contains("ValueTask", StringComparison.Ordinal))
                Batch0Support.Log("  ValueTask line: " + line.Trim());
        }

        // The return statement must build the suite through the generic
        // constructor (new ValueTask<BenchmarkSuite>(...)), which exists on every TFM.
        await Assert.That(code).Contains("ValueTask<");
        await Assert.That(code).DoesNotContain("ValueTask.FromResult");
    }
}
