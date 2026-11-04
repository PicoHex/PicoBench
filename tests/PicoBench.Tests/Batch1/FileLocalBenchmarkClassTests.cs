namespace PicoBench.Tests.Batch1;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PicoBench.Tests.Batch0;

/// <summary>
/// Batch 1 · 1.6 (B3-H2 / B3-L4) — <c>file partial class</c> and the
/// cancellation-aware symbol→syntax lookups.
/// <para>
/// A <c>file</c>-local type cannot be extended from another file, so the
/// generated <c>partial</c> half cannot see <c>Work()</c>: at HEAD the consumer
/// gets <c>CS1061</c> pointing at <c>.g.cs</c> with nothing explaining why.
/// The missing <c>GetSyntax(ct)</c> is the same class of defect one severity
/// down: cancellation is observed on the hot path but not in the two lookups
/// that walk every declaring reference.
/// </para>
/// </summary>
public class FileLocalBenchmarkClassTests
{
    /// <summary>
    /// <para>Expected at HEAD: <b>RED</b> — no PBGEN diagnostic; the generated
    /// source calls <c>this.Work()</c> on a <c>file</c> type it cannot see
    /// (CS1061).</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.6/file-local")]
    public async Task FileLocalBenchmarkClass_ReportsDedicatedDiagnostic()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                file partial class Hidden
                {
                    [Benchmark]
                    public void Work() { }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("file-local", output, compilerErrors);

        // A dedicated, actionable diagnostic…
        await Assert.That(Batch1Support.ReportsId(output, "PBGEN020")).IsTrue();
        await Assert
            .That(output.Diagnostics.Single(static d => d.Id == "PBGEN020").GetMessage())
            .Contains("Hidden");

        // …no source generated for it, so there is nothing left to fail on.
        await Assert.That(output.Sources.Length).IsEqualTo(0);

        // The message must not be the misleading "must be declared partial"
        // (it *is* partial) and the consumer must never see CS1061.
        await Assert.That(Batch1Support.ReportsId(output, "PBGEN001")).IsFalse();
        await Assert.That(compilerErrors.Any(static d => d.Id == "CS1061")).IsFalse();
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// A file-local class must not poison the classes next to it (this is the
    /// 1.1 isolation rule meeting 1.6): the ordinary sibling still generates and
    /// compiles.
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.6/file-local-isolation")]
    public async Task FileLocalBenchmarkClass_DoesNotBlockSibling()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                file partial class Hidden
                {
                    [Benchmark]
                    public void Work() { }
                }

                [BenchmarkClass]
                public partial class Visible
                {
                    [Benchmark]
                    public void Work() { }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("file-local isolation", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert.That(Batch1Support.ReportsId(output, "PBGEN020")).IsTrue();
        await Assert.That(output.Sources.Length).IsEqualTo(1);
        await Assert
            .That(output.HintNames.Any(static h => h.Contains("Visible", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// The <c>GetSyntax(ct)</c> work is a responsiveness fix, not a behaviour
    /// change: a cancellation token that is already cancelled must surface as
    /// <see cref="OperationCanceledException"/> from the driver rather than
    /// being swallowed into a generator diagnostic (and from there into a
    /// "your code is wrong" PBGEN error).
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.6/cancellation")]
    public async Task CancelledGeneration_PropagatesCancellation()
    {
        const string source = """
            using PicoBench;

            [BenchmarkClass]
            public partial class Simple
            {
                [Benchmark]
                public void Work() { }
            }
            """;

        var compilation = GeneratorHarness.CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new BenchmarkGenerator().AsSourceGenerator()
        );

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Exception? thrown = null;
        GeneratorDriverRunResult? runResult = null;
        try
        {
            driver = driver.RunGenerators(compilation, cts.Token);
            runResult = driver.GetRunResult();
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Batch0Support.Log(
            $"cancel: thrown={thrown?.GetType().Name ?? "<none>"} "
                + $"driverDiagnostics=[{string.Join(", ", runResult?.Diagnostics.Select(static d => d.Id) ?? [])}]"
        );

        // Roslyn is allowed to re-throw the cancellation or to report it through
        // the run result; what it must never be is a *generation failure*
        // diagnostic for a class that has nothing wrong with it.
        var generatorDiagnostics = runResult?.Results.Single().Diagnostics ?? [];
        await Assert
            .That(generatorDiagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
            .IsFalse();
        await Assert
            .That(runResult?.Diagnostics.Any(static d => d.Id == "CS8785") ?? false)
            .IsFalse();
    }
}
