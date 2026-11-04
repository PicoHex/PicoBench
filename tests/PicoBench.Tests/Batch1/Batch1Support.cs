namespace PicoBench.Tests.Batch1;

using Microsoft.CodeAnalysis;
using PicoBench.Tests.Batch0;

/// <summary>
/// Shared helpers for the Batch 1 generator-hardening tests
/// (<c>.review-2026-11/00-FINAL-VERDICT.md</c> §5, items 1.1 / 1.2 / 1.6).
/// </summary>
internal static class Batch1Support
{
    /// <summary>Every <see cref="DiagnosticSeverity.Error"/> the generator itself reported.</summary>
    internal static IReadOnlyList<Diagnostic> GeneratorErrors(
        GeneratorHarness.GeneratorOutput output
    ) => Errors(output.Diagnostics);

    internal static IReadOnlyList<Diagnostic> Errors(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ToList();

    /// <summary>
    /// <c>CS8785</c> ("Generator 'X' failed to generate source") only ever shows
    /// up in the driver-level diagnostics — the per-generator array never
    /// contains it, which is exactly how the whole-project loss used to hide.
    /// </summary>
    internal static bool HasGeneratorFailure(GeneratorHarness.GeneratorOutput output) =>
        output.DriverDiagnostics.Any(static d => d.Id == "CS8785");

    internal static bool ReportsId(GeneratorHarness.GeneratorOutput output, string id) =>
        output.Diagnostics.Any(d => d.Id == id) || output.DriverDiagnostics.Any(d => d.Id == id);

    /// <summary>Dumps a generator run to the test output: hints, every diagnostic, every compile error.</summary>
    internal static void LogRun(
        string label,
        GeneratorHarness.GeneratorOutput output,
        IReadOnlyList<Diagnostic> compilerErrors
    )
    {
        Batch0Support.Log($"[{label}] hint names: [{string.Join(", ", output.HintNames)}]");

        foreach (var diagnostic in output.Diagnostics)
            Batch0Support.Log($"[{label}] generator: {GeneratorHarness.Describe(diagnostic)}");

        foreach (var diagnostic in output.DriverDiagnostics)
            Batch0Support.Log($"[{label}] driver: {GeneratorHarness.Describe(diagnostic)}");

        foreach (var diagnostic in compilerErrors)
            Batch0Support.Log($"[{label}] compiler: {GeneratorHarness.Describe(diagnostic)}");

        Batch0Support.Log(
            $"[{label}] sources={output.Sources.Length} "
                + $"generatorErrors={GeneratorErrors(output).Count} "
                + $"compilerErrors={compilerErrors.Count} "
                + $"cs8785={HasGeneratorFailure(output)}"
        );
    }

    /// <summary>
    /// Only the compiler errors that live in a generated tree — "the code the
    /// generator emitted does not compile", separated from errors the user's own
    /// source already had.
    /// </summary>
    internal static IReadOnlyList<Diagnostic> ErrorsInGeneratedCode(
        GeneratorHarness.GeneratorOutput output,
        IReadOnlyList<Diagnostic> compilerErrors
    )
    {
        var generated = new HashSet<SyntaxTree>(output.Trees);
        return compilerErrors
            .Where(d => d.Location.SourceTree is { } tree && generated.Contains(tree))
            .ToList();
    }

    /// <summary>Formats compiler errors for an assertion message.</summary>
    internal static string DescribeAll(IReadOnlyList<Diagnostic> diagnostics) =>
        string.Join("\n", diagnostics.Select(GeneratorHarness.Describe));
}
