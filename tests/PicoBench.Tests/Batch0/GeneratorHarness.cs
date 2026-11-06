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

        /// <summary>
        /// Driver-level diagnostics (<c>GeneratorDriverRunResult.Diagnostics</c>).
        /// This is where a generator that throws lands: <c>CS8785</c> —
        /// "Generator 'X' failed to generate source" — which the per-generator
        /// <see cref="Diagnostics"/> array never contains.
        /// </summary>
        public required ImmutableArray<Diagnostic> DriverDiagnostics { get; init; }

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

    /// <summary>
    /// A compilation whose <b>language version</b> and nullable context are
    /// chosen by the caller, so a test can stand in for the compilation a real
    /// consumer produces (a default <c>net48</c>/<c>netstandard2.0</c> project is
    /// <c>C# 7.3</c> with nullable disabled).
    /// </summary>
    internal static Compilation CreateCompilation(
        string source,
        CSharpParseOptions parseOptions,
        NullableContextOptions nullableContextOptions = NullableContextOptions.Disable
    ) =>
        CSharpCompilation.Create(
            assemblyName: "Batch0GeneratorTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: nullableContextOptions
            )
        );

    internal static GeneratorOutput RunGenerator(string source) =>
        RunGenerator(CreateCompilation(source));

    internal static GeneratorOutput RunGenerator(Compilation compilation)
    {
        // The generated trees are parsed with the driver's parse options, and a
        // compilation refuses trees whose language version differs from its own
        // (ArgumentException "Inconsistent language versions"). So the consumer's
        // options are passed through here as well - which is also what a real
        // build does: a net48 project parses the .g.cs at C# 7.3.
        var parseOptions =
            compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions
            ?? CSharpParseOptions.Default;

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new BenchmarkGenerator().AsSourceGenerator() },
            parseOptions: parseOptions
        );
        driver = driver.RunGenerators(compilation);

        var runResult = driver.GetRunResult();
        var generatorResult = runResult.Results.Single();

        return new GeneratorOutput
        {
            Diagnostics = generatorResult.Diagnostics,
            DriverDiagnostics = runResult.Diagnostics,
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

    /// <summary>
    /// Adds the generated trees to the <b>same</b> compilation the generator ran
    /// against and returns every error/warning it produces — i.e. what a consumer
    /// build would see, with the consumer's own language version and nullable
    /// context. <see cref="DiagnosticSeverity.Hidden"/> is dropped (it is not a
    /// consumer-visible diagnostic).
    /// </summary>
    internal static IReadOnlyList<Diagnostic> Diagnostics(
        Compilation compilation,
        GeneratorOutput output
    ) =>
        compilation
            .AddSyntaxTrees(output.Trees)
            .GetDiagnostics()
            .Where(static d => d.Severity != DiagnosticSeverity.Hidden)
            .ToList();

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
