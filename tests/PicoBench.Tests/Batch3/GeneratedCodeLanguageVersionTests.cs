namespace PicoBench.Tests.Batch3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PicoBench.Tests.Batch0;

/// <summary>
/// Batch 3 · G20 — the generated code must compile at the language version a
/// consumer project <b>actually</b> defaults to.
/// <para>
/// V-B3 (<c>.review-2026-11/43-verify-b3.md</c> §7) reproduced the defect on a
/// fresh <c>net48</c> consumer that never set <c>LangVersion</c>: the SDK derives
/// <b>C# 7.3</b> for .NET Framework / netstandard2.0 targets, the first line of
/// the generated file was <c>#nullable enable</c> and its body used
/// <c>config ??= ...</c>, so the build died with
/// <c>error CS8370: Feature 'nullable reference types' is not available in C# 7.3</c>
/// naming neither PicoBench nor the generator.
/// </para>
/// <para>
/// The fix (A) makes the emitter write C# 7.3-compatible code: no <c>#nullable</c>
/// directive, no <c>??=</c>, and no nullable annotation on the interface override.
/// The choice is made from the consumer's <see cref="Compilation.LanguageVersion"/>
/// (not a preprocessor symbol — the SDK does not define
/// <c>CSHARP_8_OR_GREATER</c>, measured). These tests compile the generated trees
/// into a compilation at <see cref="LanguageVersion.CSharp7_3"/> and assert not a
/// single error, and they pin that a C# 8+ consumer still gets the nullable-aware
/// emission it had before.
/// </para>
/// </summary>
public class GeneratedCodeLanguageVersionTests
{
    /// <summary>
    /// The full emitter shape at once — namespace, two <c>[Params]</c> axes, all
    /// four lifecycle hooks, a baseline, a sync and an async benchmark — so every
    /// emission path is compiled at 7.3 rather than only the trivial one.
    /// </summary>
    private const string FullShapeSource = """
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
                public void Setup() { }

                [GlobalCleanup]
                public void Cleanup() { }

                [IterationSetup]
                public void IterationSetup() { }

                [IterationCleanup]
                public void IterationCleanup() { }

                [Benchmark(Baseline = true)]
                public void Baseline() { _ = N; }

                [Benchmark]
                public async Task Candidate() { await Task.Yield(); }
            }
        }
        """;

    private const string PureSyncSource = """
        using PicoBench;

        namespace Demo
        {
            [BenchmarkClass]
            public partial class SyncOnly
            {
                [Benchmark]
                public void Work() { }
            }
        }
        """;

    /// <summary>
    /// A default .NET Framework / netstandard2.0 consumer: <c>C# 7.3</c>, nullable
    /// disabled (the only nullable context a 7.3 compilation accepts — see
    /// <c>NullableEnabledAtCSharp7_3_IsRejectedByTheCompilerItself</c>).
    /// <b>Zero</b> errors required.
    /// <para>
    /// Expected before G20: <b>RED</b> — three <c>CS8370</c>s: the
    /// <c>#nullable enable</c> directive (line 3), the <c>BenchmarkConfig?</c>
    /// annotation (line 10) and the <c>??=</c> (line 12).
    /// </para>
    /// </summary>
    [Test]
    [Arguments(FullShapeSource, "full-shape")]
    [Arguments(PureSyncSource, "pure-sync")]
    [Property("Category", "Batch3")]
    [Property("Batch3", "G20/csharp7_3-default-consumer")]
    public async Task GeneratedCode_CompilesAtCSharp7_3(string source, string label)
    {
        var errors = await CompileAtAsync(
            source,
            LanguageVersion.CSharp7_3,
            NullableContextOptions.Disable,
            label
        );

        await Assert
            .That(errors)
            .IsEmpty()
            .Because(
                "a net48 / netstandard2.0 consumer that never sets LangVersion compiles the "
                    + "generated code at C# 7.3; any error here is the consumer's build failing"
            );
    }

    /// <summary>
    /// The emission really follows the consumer's language version — the core of
    /// the fix and the proof that neither branch is dead code:
    /// <list type="bullet">
    /// <item>at C# 7.3 the source contains <b>no</b> <c>#nullable</c> and <b>no</b>
    /// <c>BenchmarkConfig?</c> (both would be <c>CS8370</c>);</item>
    /// <item>at C# 8 it contains <b>both</b> again, so a nullable-aware consumer is
    /// not silently downgraded to the annotation-free signature (which would cost
    /// it <c>CS8767</c>/<c>CS8625</c> warnings).</item>
    /// </list>
    /// </summary>
    [Test]
    [Property("Category", "Batch3")]
    [Property("Batch3", "G20/emission-follows-consumer-language-version")]
    public async Task EmittedSource_FollowsTheConsumerLanguageVersion()
    {
        var low = GeneratorHarness.RunGenerator(
            GeneratorHarness.CreateCompilation(
                FullShapeSource,
                new CSharpParseOptions(LanguageVersion.CSharp7_3)
            )
        );
        var high = GeneratorHarness.RunGenerator(
            GeneratorHarness.CreateCompilation(
                FullShapeSource,
                new CSharpParseOptions(LanguageVersion.CSharp8)
            )
        );

        var lowCode = low.Sources.Single();
        var highCode = high.Sources.Single();

        Batch0Support.Log($"[G20/branch] 7.3  source starts: {Header(lowCode)}");
        Batch0Support.Log($"[G20/branch] C# 8 source starts: {Header(highCode)}");

        // C# 7.3: nothing nullable may be emitted.
        await Assert.That(lowCode).DoesNotContain("#nullable");
        await Assert.That(lowCode).DoesNotContain("BenchmarkConfig?");
        await Assert.That(lowCode).Contains("BenchmarkConfig config = null");

        // C# 8: the nullable-aware emission is restored.
        await Assert.That(highCode).Contains("#nullable enable");
        await Assert.That(highCode).Contains("BenchmarkConfig? config = null");
    }

    /// <summary>
    /// The modern path is not just present, it is <b>warning-free</b>: a C# 8
    /// consumer that also enables nullable must see no <c>CS8767</c> (signature
    /// nullability mismatch) and no <c>CS8625</c> (null literal to a non-nullable
    /// parameter) — the two warnings the annotation-free signature would produce.
    /// </summary>
    [Test]
    [Property("Category", "Batch3")]
    [Property("Batch3", "G20/csharp8-nullable-consumer-stays-clean")]
    public async Task AtCSharp8WithNullableEnabled_TheAnnotatedSignatureIsWarningFree()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp8);
        var compilation = GeneratorHarness.CreateCompilation(
            FullShapeSource,
            parseOptions,
            NullableContextOptions.Enable
        );
        var output = GeneratorHarness.RunGenerator(compilation);
        var diagnostics = GeneratorHarness.Diagnostics(compilation, output);

        var nullabilityWarnings = diagnostics
            .Where(static d => d.Id is "CS8767" or "CS8625" or "CS8632")
            .ToList();

        Batch0Support.Log(
            $"[G20/csharp8] diagnostics={diagnostics.Count} "
                + $"nullabilityWarnings=[{string.Join(", ", nullabilityWarnings.Select(static d => d.Id))}]"
        );
        foreach (var d in diagnostics)
            Batch0Support.Log($"[G20/csharp8] {GeneratorHarness.Describe(d)}");

        await Assert.That(nullabilityWarnings).IsEmpty();
        await Assert
            .That(output.Sources.Single())
            .Contains("BenchmarkConfig? config = null")
            .Because("at C# 8 the annotated signature must be the one emitted");
    }

    /// <summary>
    /// The same generated code must still be clean at C# 8 and at the
    /// default/latest version. Without this half the fix could "pass" by deleting
    /// the emitted code altogether; here the code is compiled against the real
    /// interface, so a missing member or a wrong signature is an error.
    /// </summary>
    [Test]
    [Arguments(LanguageVersion.CSharp8)]
    [Arguments(LanguageVersion.Latest)]
    [Property("Category", "Batch3")]
    [Property("Batch3", "G20/still-clean-at-modern-language-versions")]
    public async Task GeneratedCode_CompilesAtOrAboveCSharp8(LanguageVersion languageVersion)
    {
        var errors = await CompileAtAsync(
            FullShapeSource,
            languageVersion,
            NullableContextOptions.Disable,
            languageVersion.ToString()
        );

        await Assert
            .That(errors)
            .IsEmpty()
            .Because("lowering the floor must not raise it — C# 8+ consumers stay clean");
    }

    /// <summary>
    /// The generated half must really implement the interface: the compile is
    /// bound to the real <see cref="IBenchmarkClass"/>, so a signature that stopped
    /// satisfying it would be <c>CS0535</c>, not a silent pass. This is also the
    /// proof that an annotation-free override is legal at 7.3 (the interface's own
    /// annotation is not visible there, and the consumer's nullable context is off).
    /// </summary>
    [Test]
    [Property("Category", "Batch3")]
    [Property("Batch3", "G20/implementation-is-bound")]
    public async Task GeneratedCode_ImplementsTheInterfaceAtCSharp7_3()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var compilation = GeneratorHarness.CreateCompilation(
            PureSyncSource,
            parseOptions,
            NullableContextOptions.Disable
        );
        var output = GeneratorHarness.RunGenerator(compilation);

        await Assert.That(output.Trees.Length).IsEqualTo(1);

        var bound = compilation
            .AddSyntaxTrees(output.Trees)
            .GetTypeByMetadataName("Demo.SyncOnly")!
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Any(static m => m.Name == "RunBenchmarksAsync");

        await Assert
            .That(bound)
            .IsTrue()
            .Because("the generated half must still declare the interface implementation");
    }

    /// <summary>
    /// Documents <b>why</b> there is no "7.3 + nullable enabled" case: the
    /// compiler rejects the <i>option</i> itself at 7.3 with <c>CS8630</c>, and that
    /// diagnostic carries no source tree — it is the consuming project's own
    /// configuration (<c>&lt;Nullable&gt;enable&lt;/Nullable&gt;</c> + derived
    /// C# 7.3), not anything the generator emitted. The control below shows the
    /// identical error appears for a compilation with no generated code in it.
    /// </summary>
    [Test]
    [Property("Category", "Batch3")]
    [Property("Batch3", "G20/cs8630-is-a-project-config-error")]
    public async Task NullableEnabledAtCSharp7_3_IsRejectedByTheCompilerItself()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var compilation = GeneratorHarness.CreateCompilation(
            PureSyncSource,
            parseOptions,
            NullableContextOptions.Enable
        );

        var cs8630 = compilation.GetDiagnostics().Where(static d => d.Id == "CS8630").ToList();

        Batch0Support.Log($"[G20/cs8630] {cs8630.Count} diagnostic(s) on the empty compilation");

        await Assert.That(cs8630.Count).IsEqualTo(1);
        await Assert
            .That(cs8630[0].Location.SourceTree)
            .IsNull()
            .Because(
                "CS8630 has no source location: it describes the project's "
                    + "<Nullable>enable</Nullable> + derived C# 7.3, not generated code"
            );
    }

    // ── plumbing ────────────────────────────────────────────────────────

    private static string Header(string code)
    {
        var firstLines = code.Replace("\r\n", "\n").Split('\n').Take(12);
        return string.Join(" / ", firstLines.Select(static l => l.Trim()).Where(static l => l.Length > 0));
    }

    private static async Task<IReadOnlyList<Diagnostic>> CompileAtAsync(
        string source,
        LanguageVersion languageVersion,
        NullableContextOptions nullableContextOptions,
        string label
    )
    {
        var parseOptions = new CSharpParseOptions(languageVersion);
        var compilation = GeneratorHarness.CreateCompilation(
            source,
            parseOptions,
            nullableContextOptions
        );
        var output = GeneratorHarness.RunGenerator(compilation);
        var diagnostics = GeneratorHarness.Diagnostics(compilation, output);

        var errors = diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Batch0Support.Log(
            $"[G20/{label}] lv={languageVersion} nullable={nullableContextOptions} "
                + $"sources={output.Sources.Length} errors={errors.Count}"
        );
        foreach (var diagnostic in diagnostics)
            Batch0Support.Log($"[G20/{label}] {GeneratorHarness.Describe(diagnostic)}");

        // A generator run that produced nothing would make every assertion above
        // pass on an empty compile. Pin the shape that is being judged.
        await Assert.That(output.Trees.Length).IsEqualTo(1);

        return errors;
    }
}
