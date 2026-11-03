namespace PicoBench.Tests.Batch0;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Minimal compile-level harness for <see cref="BenchmarkGenerator"/>.
/// <para>
/// B4's H5 finding: the existing generator tests stop at
/// <c>driver.RunGenerators(compilation)</c> and only assert on strings, so the
/// generated code is never fed back into a compilation. This harness does that
/// part (<c>AddSyntaxTrees(...).GetDiagnostics()</c>), which is what turns
/// "the emitter produced text we expected" into "the emitter produced code the
/// compiler accepts".
/// </para>
/// </summary>
internal static class GeneratorHarness
{
    /// <summary>A generator run: its own diagnostics plus every emitted source.</summary>
    internal sealed class GeneratorOutput
    {
        public required ImmutableArray<Diagnostic> Diagnostics { get; init; }

        public required string[] HintNames { get; init; }

        public required string[] Sources { get; init; }

        public required SyntaxTree[] Trees { get; init; }
    }

    internal static Compilation CreateCompilation(string source) =>
        CSharpCompilation.Create(
            assemblyName: "Batch0GeneratorTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

    internal static GeneratorOutput RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new BenchmarkGenerator().AsSourceGenerator()
        );
        driver = driver.RunGenerators(compilation);

        var generatorResult = driver.GetRunResult().Results.Single();

        return new GeneratorOutput
        {
            Diagnostics = generatorResult.Diagnostics,
            HintNames = generatorResult.GeneratedSources.Select(static s => s.HintName).ToArray(),
            Sources = generatorResult
                .GeneratedSources.Select(static s => s.SourceText.ToString())
                .ToArray(),
            Trees = generatorResult.GeneratedSources.Select(static s => s.SyntaxTree).ToArray(),
        };
    }

    /// <summary>
    /// Adds the generated trees back to the compilation and returns only
    /// compiler <b>errors</b> — i.e. what a consumer's <c>dotnet build</c>
    /// would refuse to compile.
    /// </summary>
    internal static IReadOnlyList<Diagnostic> CompileErrors(string source, GeneratorOutput output)
    {
        var compilation = CreateCompilation(source).AddSyntaxTrees(output.Trees);
        return compilation
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    /// <summary>Formats a diagnostic as <c>ID @line:col: message</c> for the test log.</summary>
    internal static string Describe(Diagnostic diagnostic)
    {
        var position = diagnostic.Location.GetLineSpan().StartLinePosition;
        return $"{diagnostic.Id} @{position.Line + 1},{position.Character + 1}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Concatenates the generated sources so a single <c>Contains</c> can scan them all.</summary>
    internal static string AllSources(GeneratorOutput output) =>
        string.Join("\n// ---- next source ----\n", output.Sources);

    private static MetadataReference[] GetMetadataReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Concat([typeof(BenchmarkClassAttribute).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return paths.Select(static path => MetadataReference.CreateFromFile(path)).ToArray();
    }
}
