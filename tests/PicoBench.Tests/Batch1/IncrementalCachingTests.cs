namespace PicoBench.Tests.Batch1;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PicoBench.Tests.Batch0;

/// <summary>
/// Batch 1 · 1.8 (B3-M1) — incremental caching, asserted on the driver's own
/// step bookkeeping instead of on "the output looks right".
/// <para>
/// The pipeline's transform returns a <c>GeneratorAnalysisResult</c>, and that
/// type used to be a plain reference-equal class: every compilation change —
/// including editing a file that has nothing to do with benchmarks — produced a
/// "new" value, so <c>SourceOutput</c> re-ran <c>Emitter.Generate</c> +
/// <c>AddSource</c> for every benchmark class in the project. The models below
/// it (<c>BenchmarkClassModel</c>, …) already had value equality, so the fix is
/// <c>IEquatable</c> on the wrapper and nothing else.
/// </para>
/// <para>
/// The three steps this file pins down are the review's exact acceptance
/// criteria: first run has output; an unrelated edit leaves the step
/// <c>Cached</c>/<c>Unchanged</c> (never <c>Modified</c>); editing the class
/// re-runs it and the emitted code actually changes.
/// </para>
/// </summary>
public class IncrementalCachingTests
{
    private const string BenchmarkPath = "Benchmarks.cs";
    private const string UnrelatedPath = "Unrelated.cs";

    /// <summary>A benchmark class that pulls nothing in from anywhere else.</summary>
    private const string BenchmarkSource = """
        using PicoBench;

        namespace Demo
        {
            [BenchmarkClass]
            public partial class Incremental
            {
                [Benchmark]
                public void First() { }
            }
        }
        """;

    private const string UnrelatedSource = """
        namespace Demo
        {
            public class Unrelated
            {
                public int Value { get; set; }
            }
        }
        """;

    /// <summary>
    /// Run 1 of a fresh driver: there is a source, the pipeline recorded the
    /// steps this file asserts on, and the emitted code compiles.
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.8/incremental-first-run")]
    public async Task FirstRun_ProducesSourceAndRecordsTheOutputStep()
    {
        var compilation = Compile(Parse(BenchmarkSource, BenchmarkPath));
        var driver = CreateDriver().RunGenerators(compilation);
        var run = Run(driver);

        var outputStep = OutputStep(run);
        var reasons = Reasons(outputStep);
        Batch0Support.Log(
            $"first run: sources={run.GeneratedSources.Length} reasons=[{Join(reasons)}]"
        );

        await Assert.That(run.GeneratedSources.Length).IsEqualTo(1);
        await Assert.That(run.Diagnostics.Length).IsEqualTo(0);
        await Assert.That(reasons).IsNotEmpty();
        await Assert
            .That(reasons.All(static r => r is IncrementalStepRunReason.New))
            .IsTrue()
            .Because("the first run can only be New (or Modified), never cached");

        await Assert.That(CompilerErrors(compilation, run).Count).IsEqualTo(0);
    }

    /// <summary>
    /// The B3-M1 case: a file that has nothing to do with benchmarks is edited.
    /// The transform value must compare <b>equal</b>, so the step that feeds
    /// <c>Emitter.Generate</c> is cached — the review's wording is "无关编辑时
    /// <c>SourceOutput</c> 为 <c>Cached</c>".
    /// <para>
    /// Before the <c>IEquatable</c> fix this test is RED twice over: the
    /// transform step reports <c>Modified</c> (reference equality) and so does
    /// <c>SourceOutput</c>.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.8/incremental-unrelated-edit")]
    public async Task UnrelatedFileEdit_KeepsTransformAndSourceOutputCached()
    {
        var benchmarkTree = Parse(BenchmarkSource, BenchmarkPath);

        var driver = CreateDriver().RunGenerators(Compile(benchmarkTree));
        var first = Run(driver);

        var firstText = first.GeneratedSources.Single().SourceText.ToString();
        var firstHint = first.GeneratedSources.Single().HintName;

        // Only *Unrelated.cs* is touched: new file, then an edit inside it.
        var edited = Compile(
            benchmarkTree,
            Parse(UnrelatedSource + "\n// edited\n", UnrelatedPath)
        );
        driver = driver.RunGenerators(edited);
        var second = Run(driver);

        var transformReasons = Reasons(TransformStep(second));
        var outputReasons = Reasons(OutputStep(second));
        Batch0Support.Log(
            $"[1.8] transform=[{Join(transformReasons)}] sourceOutput=[{Join(outputReasons)}]"
        );

        await Assert
            .That(transformReasons.Contains(IncrementalStepRunReason.Modified))
            .IsFalse()
            .Because(
                "GeneratorAnalysisResult must compare by value, otherwise any edit anywhere "
                    + "re-runs the transform for every benchmark class"
            );
        await Assert
            .That(outputReasons.Contains(IncrementalStepRunReason.Modified))
            .IsFalse()
            .Because("emitting is exactly the work the cache is supposed to skip");
        await Assert
            .That(
                outputReasons.All(static r =>
                    r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged
                )
            )
            .IsTrue()
            .Because($"expected Cached/Unchanged, got [{Join(outputReasons)}]");

        // Cached must still mean "the same output" — not "no output".
        await Assert.That(second.GeneratedSources.Length).IsEqualTo(1);
        await Assert.That(second.GeneratedSources.Single().HintName).IsEqualTo(firstHint);
        await Assert
            .That(second.GeneratedSources.Single().SourceText.ToString())
            .IsEqualTo(firstText);
        await Assert.That(CompilerErrors(edited, second).Count).IsEqualTo(0);
    }

    /// <summary>
    /// The other half of the criterion: changing the benchmark class must
    /// re-run the output step and produce <b>different</b> code. A cache
    /// assertion that cannot see a real change would be worthless.
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.8/incremental-class-edit")]
    public async Task BenchmarkClassEdit_RerunsTheOutputAndUpdatesTheCode()
    {
        var driver = CreateDriver().RunGenerators(Compile(Parse(BenchmarkSource, BenchmarkPath)));
        var first = Run(driver);
        var firstText = first.GeneratedSources.Single().SourceText.ToString();
        await Assert.That(firstText.Contains("this.First()", StringComparison.Ordinal)).IsTrue();

        var edited = Compile(
            Parse(
                BenchmarkSource.Replace(
                    "public void First() { }",
                    "public void First() { }\n\n        [Benchmark]\n        public void Second() { }",
                    StringComparison.Ordinal
                ),
                BenchmarkPath
            )
        );
        driver = driver.RunGenerators(edited);
        var second = Run(driver);

        var transformReasons = Reasons(TransformStep(second));
        var outputReasons = Reasons(OutputStep(second));
        Batch0Support.Log(
            $"[1.8] class edit: transform=[{Join(transformReasons)}] sourceOutput=[{Join(outputReasons)}]"
        );

        await Assert
            .That(transformReasons.Contains(IncrementalStepRunReason.Modified))
            .IsTrue()
            .Because("a changed model is a changed value");
        await Assert
            .That(
                outputReasons.Any(static r =>
                    r is IncrementalStepRunReason.Modified or IncrementalStepRunReason.New
                )
            )
            .IsTrue()
            .Because($"expected the output step to re-run, got [{Join(outputReasons)}]");

        var secondText = second.GeneratedSources.Single().SourceText.ToString();
        await Assert.That(secondText.Contains("this.Second()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(secondText).IsNotEqualTo(firstText);
        await Assert.That(CompilerErrors(edited, second).Count).IsEqualTo(0);
    }

    /// <summary>
    /// Extra guard for the same fix, and the case an IDE actually produces: the
    /// benchmark file itself is re-parsed but the class didn't change (a
    /// trailing comment). The model is equal and the class position is
    /// unchanged, so nothing may be re-emitted — a class of edits that still
    /// misses if equality is done on the <c>SyntaxTree</c> instance rather than
    /// on the position it describes.
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.8/incremental-comment-only-edit")]
    public async Task CommentOnlyEditInTheBenchmarkFile_KeepsTheOutputCached()
    {
        var driver = CreateDriver().RunGenerators(Compile(Parse(BenchmarkSource, BenchmarkPath)));
        var first = Run(driver);
        var firstText = first.GeneratedSources.Single().SourceText.ToString();

        var reparsed = Compile(Parse(BenchmarkSource + "\n// trailing comment\n", BenchmarkPath));
        driver = driver.RunGenerators(reparsed);
        var second = Run(driver);

        var outputReasons = Reasons(OutputStep(second));
        Batch0Support.Log($"[1.8] comment-only edit: sourceOutput=[{Join(outputReasons)}]");

        await Assert
            .That(
                outputReasons.All(static r =>
                    r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged
                )
            )
            .IsTrue()
            .Because($"expected Cached/Unchanged, got [{Join(outputReasons)}]");
        await Assert
            .That(second.GeneratedSources.Single().SourceText.ToString())
            .IsEqualTo(firstText);
    }

    // ── plumbing ────────────────────────────────────────────────────────

    private static GeneratorDriver CreateDriver() =>
        CSharpGeneratorDriver.Create(
            [new BenchmarkGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );

    private static SyntaxTree Parse(string source, string path) =>
        CSharpSyntaxTree.ParseText(source, path: path);

    /// <summary>Same reference set as <see cref="GeneratorHarness"/>, with the trees we choose.</summary>
    private static Compilation Compile(params SyntaxTree[] trees) =>
        GeneratorHarness
            .CreateCompilation(string.Empty)
            .RemoveAllSyntaxTrees()
            .AddSyntaxTrees(trees);

    private static GeneratorRunResult Run(GeneratorDriver driver) =>
        driver.GetRunResult().Results.Single();

    /// <summary>The step that produces the emitted sources.</summary>
    private static ImmutableArray<IncrementalGeneratorRunStep> OutputStep(GeneratorRunResult run) =>
        FindStep(
            run.TrackedOutputSteps,
            static name => name == "SourceOutput",
            "output step 'SourceOutput'"
        );

    /// <summary>
    /// The transform step whose value is the <c>GeneratorAnalysisResult</c>
    /// (Roslyn names it after the provider, e.g.
    /// <c>result_ForAttributeWithMetadataName</c>).
    /// </summary>
    private static ImmutableArray<IncrementalGeneratorRunStep> TransformStep(
        GeneratorRunResult run
    ) =>
        FindStep(
            run.TrackedSteps,
            static name =>
                name.StartsWith("result_", StringComparison.Ordinal)
                && name.Contains("ForAttributeWithMetadataName", StringComparison.Ordinal),
            "transform step 'result_ForAttributeWithMetadataName'"
        );

    private static ImmutableArray<IncrementalGeneratorRunStep> FindStep(
        ImmutableDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> steps,
        Func<string, bool> match,
        string description
    )
    {
        var matches = steps.Where(kv => match(kv.Key)).Select(kv => kv.Value).ToList();

        // A renamed pipeline node must fail loudly here instead of silently
        // asserting on an empty set.
        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"The generator pipeline has no {description}. Tracked steps: "
                    + string.Join(", ", steps.Keys.OrderBy(static k => k, StringComparer.Ordinal))
            );

        return matches.SelectMany(static steps => steps).ToImmutableArray();
    }

    private static IncrementalStepRunReason[] Reasons(
        ImmutableArray<IncrementalGeneratorRunStep> steps
    ) =>
        steps
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason)
            .ToArray();

    private static string Join(IEnumerable<IncrementalStepRunReason> reasons) =>
        string.Join(", ", reasons);

    private static IReadOnlyList<Diagnostic> CompilerErrors(
        Compilation compilation,
        GeneratorRunResult run
    ) =>
        compilation
            .AddSyntaxTrees(run.GeneratedSources.Select(static source => source.SyntaxTree))
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();
}
