namespace PicoBench.Tests.Batch0;

using System.Text.RegularExpressions;
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
        // R0 §2.12: a bare Contains("ValueTask<") is tautological — the method
        // signature line itself (`ValueTask<BenchmarkSuite> RunBenchmarksAsync(...)`)
        // satisfies it. The constructor form is what has to be present, with or
        // without the global:: qualifier the emitter prefers.
        var ctorPattern = new Regex(
            @"new\s+(?:global::)?[\w.]*ValueTask<",
            RegexOptions.CultureInvariant
        );
        await Assert.That(ctorPattern.IsMatch(code)).IsTrue();
        await Assert.That(code).DoesNotContain("ValueTask.FromResult");
    }

    /// <summary>
    /// Batch 0 · 0.6 — the generator must be consumable by an SDK 8 project.
    /// <para>
    /// <b>This is the cheap replacement guard for 0.6; the real SDK 8 consumer
    /// verification lives in CI</b> (build a net8.0 consumer project with the
    /// generator referenced as <c>OutputItemType="Analyzer"</c> and require a
    /// <c>.g.cs</c> in <c>obj</c> and no <c>CS9057</c>). A generator compiled
    /// against Roslyn &gt; 4.8 cannot be loaded by SDK 8 (that is exactly what
    /// <c>CS9057</c> reports), so pinning the centrally managed
    /// <c>Microsoft.CodeAnalysis.CSharp</c> version ≤ 4.8 is a static,
    /// network-free proxy for "SDK 8 can load this generator".
    /// </para>
    /// <para>Expected at HEAD: <b>RED</b> — `Directory.Packages.props` pins
    /// <c>Microsoft.CodeAnalysis.CSharp</c> 5.9.0.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.0/0.6")]
    public async Task Generator_RoslynVersion_IsLoadableBySdk8()
    {
        // 0.6 substitute guard: real SDK 8 consumer build is verified in CI.
        var propsPath = Batch0Support.FindRepositoryFile("Directory.Packages.props");
        var versionText = Batch0Support.ReadCentralPackageVersion("Microsoft.CodeAnalysis.CSharp");

        Batch0Support.Log($"{propsPath}: Microsoft.CodeAnalysis.CSharp = {versionText ?? "<missing>"}");
        await Assert.That(versionText).IsNotNull();

        var version = Version.Parse(versionText!);
        var sdk8Ceiling = new Version(4, 8);

        Batch0Support.Log(
            $"SDK 8 compatible ceiling = {sdk8Ceiling}; generator build {version} "
                + (version <= sdk8Ceiling ? "is loadable by SDK 8" : "would fail with CS9057")
        );

        await Assert.That(version).IsLessThanOrEqualTo(sdk8Ceiling);
    }
}
