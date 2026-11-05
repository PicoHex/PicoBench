namespace PicoBench.Generators;

/// <summary>
/// Incremental source generator that discovers [BenchmarkClass]-attributed types
/// and generates AOT-compatible <c>IBenchmarkClass</c> implementations.
/// </summary>
[Generator]
public sealed class BenchmarkGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Configures the incremental pipelines that validate benchmark classes and emit source.
    /// </summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
            BenchmarkClassAnalyzer.BenchmarkClassAttributeName,
            predicate: static (node, _) =>
                node is ClassDeclarationSyntax or RecordDeclarationSyntax,
            transform: static (ctx, ct) => BenchmarkClassAnalyzer.AnalyzeTarget(ctx, ct)
        );

        // Collect() so the output stage can keep one set of already-emitted hint
        // names: the same partial class may be annotated [BenchmarkClass] in more
        // than one declaration, and Roslyn refuses two sources with one hint name
        // by throwing out of AddSource — which used to cost the whole run (B3-C2).
        context.RegisterSourceOutput(
            results.Collect(),
            static (spc, collected) => Execute(spc, collected)
        );
    }

    /// <summary>
    /// Emits one partial class per successfully analysed benchmark class.
    /// <para>
    /// Batch 1 · 1.1 (B3-C2): each class is isolated. A throw while emitting one
    /// class reports <c>PBGEN018</c> for <b>that</b> class and lets every other
    /// class generate; nothing here is allowed to surface as <c>CS8785</c> and
    /// take the project's entire generated half with it.
    /// </para>
    /// </summary>
    private static void Execute(
        SourceProductionContext spc,
        ImmutableArray<GeneratorAnalysisResult> collected
    )
    {
        var emittedHintNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var result in collected)
        {
            foreach (var diagnostic in result.Diagnostics)
                spc.ReportDiagnostic(diagnostic);

            if (result.Model is null || result.HasErrors)
                continue;

            var model = result.Model;

            try
            {
                var code = Emitter.Generate(model);
                var hintName = CreateHintName(model);

                // Same type, two [BenchmarkClass] declarations: both analyses see
                // every member of the merged type, so one source is complete.
                if (!emittedHintNames.Add(hintName))
                    continue;

                spc.AddSource(hintName, code);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                spc.ReportDiagnostic(
                    Diagnostic.Create(
                        DiagnosticDescriptors.SourceGenerationFailed,
                        result.Location,
                        model.ClassName,
                        DescribeException(ex)
                    )
                );
            }
        }
    }

    /// <summary>
    /// Builds the hint name — which doubles as the generated file name — for one
    /// class. Two properties matter and neither is free:
    /// <list type="bullet">
    /// <item>it is a legal Roslyn hint name (letters, digits, <c>.</c>, <c>_</c>):
    /// <c>@</c>, which keyword namespaces and keyword type names carry through
    /// <c>ToDisplayString()</c>, is stripped;</item>
    /// <item>it is unique. Flattening <c>Demo.Perf.Marks</c> and
    /// <c>Demo.Perf_Marks</c> both to <c>Demo_Perf_Marks.g.cs</c> used to make
    /// AddSource throw, so the name ends in a short hash of the qualified name.</item>
    /// </list>
    /// The hash is FNV-1a over the qualified name, written by hand instead of
    /// <c>string.GetHashCode()</c>: that one is randomised per process, which
    /// would make hint names (and therefore incremental build outputs) differ
    /// between runs.
    /// </summary>
    internal static string CreateHintName(BenchmarkClassModel model)
    {
        var qualifiedName = model.Namespace is null
            ? model.ClassName
            : $"{model.Namespace}.{model.ClassName}";

        return $"{SanitizeHintStem(qualifiedName)}_{ComputeStableHash(qualifiedName):x8}.g.cs";
    }

    /// <summary>Strips everything a hint name may not contain, keeping it readable.</summary>
    private static string SanitizeHintStem(string qualifiedName)
    {
        var sb = new StringBuilder(qualifiedName.Length);
        foreach (var c in qualifiedName)
        {
            sb.Append(
                c switch
                {
                    // IdentifierEscaping's '@' is not a legal hint-name character.
                    '@' => '_',
                    '.' => '_',
                    _ when char.IsLetterOrDigit(c) => c,
                    _ => '_',
                }
            );
        }

        return sb.Length == 0 ? "Benchmark" : sb.ToString();
    }

    /// <summary>FNV-1a over UTF-16 code units — deterministic across processes and machines.</summary>
    private static uint ComputeStableHash(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }

    /// <summary>
    /// One line, bounded length: exception messages are user-facing diagnostic
    /// text and RS1032 rejects multi-line messages.
    /// </summary>
    private static string DescribeException(Exception exception)
    {
        var message = exception.Message;
        var lineBreak = message.IndexOfAny(['\r', '\n']);
        if (lineBreak >= 0)
            message = message.Substring(0, lineBreak);

        const int maxLength = 240;
        return message.Length <= maxLength ? message : message.Substring(0, maxLength) + "…";
    }
}
