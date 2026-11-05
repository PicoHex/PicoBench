namespace PicoBench.Tests.Batch1;

using Microsoft.CodeAnalysis;
using PicoBench.Tests.Batch0;

/// <summary>
/// Batch 1 · 1.1 (B3-C2) — a single benchmark class must not be able to take
/// the whole generation run down.
/// <para>
/// At HEAD the whole pipeline is one <c>RegisterSourceOutput</c> with no
/// isolation, and the hint name is the raw <c>namespace_class.g.cs</c>, so any
/// throw (a hint-name collision, an <c>@</c> in a keyword namespace, a
/// <c>[Params]</c> array crashing the transform) surfaces as one easy-to-miss
/// <c>CS8785</c> and <b>every</b> class in the project loses its <c>.g.cs</c>.
/// These tests compile the emitted code, so "the other classes still generate"
/// means the compiler accepts them, not that a string was produced.
/// </para>
/// </summary>
public class GeneratorIsolationTests
{
    /// <summary>
    /// Trigger 1 — hint-name collision. <c>Demo.Perf.Marks</c> and
    /// <c>Demo.Perf_Marks</c> both flatten to <c>Demo_Perf_Marks.g.cs</c>.
    /// <para>Expected at HEAD: <b>RED</b> — <c>CS8785</c>, 0 sources.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.1/hint-collision")]
    public async Task HintNameCollision_BothClassesStillGenerate()
    {
        const string source = """
            using PicoBench;

            namespace Demo.Perf
            {
                [BenchmarkClass]
                public partial class Marks
                {
                    [Benchmark]
                    public void Work() { }
                }
            }

            namespace Demo
            {
                [BenchmarkClass]
                public partial class Perf_Marks
                {
                    [Benchmark]
                    public void Work() { }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("hint collision", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);
        await Assert.That(output.Sources.Length).IsEqualTo(2);
        await Assert.That(output.HintNames.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(2);
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Trigger 2 — keyword namespace. <c>namespace @class.@namespace</c> makes
    /// the hint name contain <c>@</c>, which Roslyn rejects.
    /// <para>Expected at HEAD: <b>RED</b> — <c>CS8785</c> ("invalid character '@'"), 0 sources.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.1/keyword-namespace")]
    public async Task KeywordNamespace_GeneratesCompilableCode()
    {
        const string source = """
            using PicoBench;

            namespace @class.@namespace
            {
                [BenchmarkClass]
                public partial class Kw
                {
                    [Benchmark]
                    public void Work() { }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("keyword namespace", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);
        await Assert.That(output.Sources.Length).IsEqualTo(1);

        // A hint name is a file name: letters, digits, '.' and '_' only.
        var hint = output.HintNames.Single();
        Batch0Support.Log($"hint: '{hint}'");
        await Assert.That(hint.Contains('@')).IsFalse();
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Trigger 3 — <c>[Params(new[] { 1, 2 })]</c>. The transform itself throws
    /// (<c>TypedConstant is an array. Use Values property.</c>) before
    /// <c>RegisterSourceOutput</c> is ever reached, so this is the trigger that
    /// decides whether the isolation lives in the output stage only or around
    /// both stages.
    /// <para>Expected at HEAD: <b>RED</b> — <c>CS8785</c>, sibling class loses its source.</para>
    /// <para>Acceptance: exactly <b>one</b> diagnostic for the failing class, the
    /// sibling's <c>.g.cs</c> still generated and compiling, no <c>CS8785</c>.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.1/params-array")]
    public async Task ParamsArrayArgument_FailsOneClassOnly()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class WithArrayParam
                {
                    [Params(new[] { 1, 2 })]
                    public int[] Data = new[] { 1 };

                    [Benchmark]
                    public void Work() { _ = Data; }
                }

                [BenchmarkClass]
                public partial class Sibling
                {
                    [Benchmark]
                    public void Work() { }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("params array", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();

        // At most one diagnostic — for the one broken class, not one per member
        // and not a cascade. (0 is the better answer: Batch 1 · 1.4 may teach the
        // formatter to emit an array literal, and then nothing is wrong at all.
        // Either way this class may not take the sibling with it.)
        var generatorErrors = Batch1Support.GeneratorErrors(output);
        await Assert.That(generatorErrors.Count).IsLessThanOrEqualTo(1);

        // …and the healthy sibling always generates, and the result compiles.
        await Assert
            .That(output.HintNames.Any(static h => h.Contains("Sibling", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(output.Sources.Length).IsEqualTo(generatorErrors.Count == 0 ? 2 : 1);
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Trigger 4 — a throw inside the <b>transform</b> stage, which is not
    /// inside <c>RegisterSourceOutput</c> at all. <c>[Params(null)]</c> binds
    /// <c>null</c> to the whole <c>params object[]</c> array, so the attribute
    /// argument arrives with <c>Kind == Array</c> and a <c>default</c>
    /// <c>ImmutableArray</c> for its values; enumerating that throws.
    /// <para>
    /// This is the only trigger of the three that no longer has a natural
    /// resident: 1.4 taught the formatter to handle <c>typeof</c>/arrays, so the
    /// <c>[Params(new[]{{...}})]</c> crash is gone. What must <b>never</b> go away
    /// is the guarantee — a throw here aborts the whole generator run
    /// (<c>CS8785</c>) and every class in the project silently loses its
    /// <c>.g.cs</c>.
    /// </para>
    /// <para>
    /// Scope note: the underlying <c>NullReferenceException</c> is a separate,
    /// contained defect (the diagnostic is the unhelpful PBGEN018 rather than a
    /// "no values" message; a guard on <c>arg.Values.IsDefaultOrEmpty</c> would
    /// fix it). This test pins the <i>containment</i> contract only, and so stays
    /// valid when that follow-up lands: one diagnostic for one class, no
    /// <c>CS8785</c>, the sibling still generates and everything compiles.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.1/params-null-array")]
    public async Task ParamsNullArrayArgument_FailsOneClassOnly()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class NullParams
                {
                    [Params(null)]
                    public int[]? Data { get; set; }

                    [Benchmark]
                    public void Work() { _ = Data; }
                }

                [BenchmarkClass]
                public partial class Sibling
                {
                    [Benchmark]
                    public void Work() { }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("params null array", output, compilerErrors);

        // The transform runs before any output stage; an unguarded throw there is
        // CS8785 and takes every class's source with it.
        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();

        var generatorErrors = Batch1Support.GeneratorErrors(output);
        await Assert
            .That(generatorErrors.Count)
            .IsEqualTo(1)
            .Because("exactly one diagnostic, for the one class that failed");

        await Assert
            .That(output.HintNames.Any(static h => h.Contains("Sibling", StringComparison.Ordinal)))
            .IsTrue()
            .Because("the healthy sibling must still get its .g.cs");
        await Assert.That(output.Sources.Length).IsEqualTo(1);

        await Assert
            .That(Batch1Support.ErrorsInGeneratedCode(output, compilerErrors).Count)
            .IsEqualTo(0);
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// The same partial class annotated <c>[BenchmarkClass]</c> twice used to
    /// add the identical hint name twice (<c>ArgumentException</c> → <c>CS8785</c>).
    /// Both analyses see every member of the merged type, so generating the
    /// class once is correct and loses nothing.
    /// <para>Expected at HEAD: <b>RED</b> — <c>CS8785</c>, 0 sources.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.1/duplicate-attribute")]
    public async Task ClassAnnotatedTwice_GeneratesOnce()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class Twice
                {
                    [Benchmark]
                    public void A() { }
                }

                [BenchmarkClass]
                public partial class Twice
                {
                    [Benchmark]
                    public void B() { }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("annotated twice", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);
        await Assert.That(output.Sources.Length).IsEqualTo(1);

        // The surviving source must still cover both declarations' members.
        var code = GeneratorHarness.AllSources(output);
        await Assert.That(code.Contains("this.A()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("this.B()", StringComparison.Ordinal)).IsTrue();

        // Nothing generated may fail to compile. The one remaining error is the
        // user's own: [BenchmarkClass] is AllowMultiple = false, so annotating
        // two partial declarations of one type is CS0579 in *their* file — not a
        // generator failure, and no longer a reason to lose the whole output.
        await Assert
            .That(Batch1Support.ErrorsInGeneratedCode(output, compilerErrors).Count)
            .IsEqualTo(0);
        await Assert.That(compilerErrors.Count).IsEqualTo(1);
        await Assert.That(compilerErrors[0].Id).IsEqualTo("CS0579");
    }

    /// <summary>
    /// Hint names must be stable across runs (the reason the short hash is a
    /// hand-rolled FNV-1a over the qualified name, never <c>GetHashCode()</c>,
    /// whose string seed is randomised per process) and must be legal file names.
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.1/hint-stability")]
    public async Task HintNames_AreStableAndSanitised()
    {
        const string source = """
            using PicoBench;

            namespace @class.Perf
            {
                [BenchmarkClass]
                public partial class @lock
                {
                    [Benchmark]
                    public void Work() { }
                }
            }
            """;

        var first = GeneratorHarness.RunGenerator(source);
        var second = GeneratorHarness.RunGenerator(source);
        Batch1Support.LogRun("hint stability", first, []);

        await Assert.That(first.HintNames.Length).IsEqualTo(1);
        await Assert.That(first.HintNames[0]).IsEqualTo(second.HintNames[0]);

        var hint = first.HintNames[0];
        await Assert.That(hint.Contains('@')).IsFalse();
        await Assert.That(hint.EndsWith(".g.cs", StringComparison.Ordinal)).IsTrue();
        await Assert
            .That(
                hint[..^".g.cs".Length]
                    .All(static c => char.IsLetterOrDigit(c) || c is '_' || c is '.')
            )
            .IsTrue();
    }
}
