namespace PicoBench.Tests.Batch1;

using Microsoft.CodeAnalysis;
using PicoBench.Tests.Batch0;

/// <summary>
/// Batch 1 · 1.2 (B3-C3) — every identifier the emitter writes must be a legal
/// C# identifier.
/// <para>
/// <c>ISymbol.Name</c> never carries the <c>@</c> that made a keyword name
/// legal, and an explicit interface implementation's name is
/// <c>IThing.Do</c> — a dotted string that cannot be used to call the method.
/// At HEAD both are pasted straight into the generated source, so legal user
/// code produces uncompilable <c>.g.cs</c> (and, for the explicit case, not one
/// diagnostic to explain it).
/// </para>
/// </summary>
public class IdentifierEscapingTests
{
    /// <summary>
    /// The escaped form of every keyword — reserved and contextual — must be
    /// <c>@</c> + the raw name, and a non-keyword must be returned unchanged.
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.2/escape-identifier")]
    public async Task EscapeIdentifier_CoversReservedAndContextualKeywords()
    {
        string[] keywords =
        [
            // reserved
            "class",
            "lock",
            "event",
            "int",
            "namespace",
            "partial",
            "public",
            // contextual
            "record",
            "var",
            "async",
            "await",
            "value",
            "where",
            "yield",
            "global",
            "with",
            "init",
            "required",
            "file",
        ];

        foreach (var keyword in keywords)
        {
            var escaped = IdentifierEscaping.EscapeIdentifier(keyword);
            Batch0Support.Log($"{keyword} -> {escaped}");
            await Assert.That(escaped).IsEqualTo("@" + keyword);
        }

        // Ordinary identifiers are untouched, and an already-escaped name is not
        // double-escaped.
        await Assert.That(IdentifierEscaping.EscapeIdentifier("Work")).IsEqualTo("Work");
        await Assert
            .That(IdentifierEscaping.EscapeIdentifier("RunBenchmarksAsync"))
            .IsEqualTo("RunBenchmarksAsync");
        await Assert.That(IdentifierEscaping.EscapeIdentifier("@class")).IsEqualTo("@class");
    }

    /// <summary>
    /// <c>@class</c> class, <c>@lock</c> method, <c>@event</c> <c>[Params]</c>
    /// member, plus the <c>var</c>/<c>record</c>/<c>async</c> contextual
    /// keywords in method position.
    /// <para>Expected at HEAD: <b>RED</b> — the emitted source says
    /// <c>public partial class class</c> (CS0542) and <c>this.lock()</c>
    /// (CS1003).</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.2/keyword-identifiers")]
    public async Task KeywordClassMethodAndParamsMember_Compile()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class @class
                {
                    [Params(1, 2)]
                    public int @event;

                    [Benchmark(Baseline = true)]
                    public void @lock() { _ = @event; }

                    [Benchmark]
                    public void @record() { }

                    [Benchmark]
                    public void @var() { }

                    [Benchmark]
                    public void @async() { }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("keyword identifiers", output, compilerErrors);

        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);
        await Assert.That(output.Sources.Length).IsEqualTo(1);

        var code = GeneratorHarness.AllSources(output);
        await Assert.That(code.Contains("partial class @class", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("this.@lock()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("this.@event = ", StringComparison.Ordinal)).IsTrue();

        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Keyword identifiers do not stop at the class name: a <c>[Params]</c>
    /// member's <i>type</i> is printed into the generated source
    /// (<c>new {type}[] { … }</c>) and so is an enum's qualified name, so a
    /// keyword namespace or a keyword enum name has to survive that path too.
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.2/keyword-types")]
    public async Task KeywordNamespaceWithParamsAndEnum_Compiles()
    {
        const string source = """
            using PicoBench;

            namespace @class.@namespace
            {
                public enum @enum
                {
                    A = 0,
                    B = 1,
                }

                [BenchmarkClass]
                public partial class @lock
                {
                    [Params(@enum.A, @enum.B)]
                    public @enum Mode { get; set; }

                    [Params(1, 2)]
                    public int @event;

                    [Benchmark]
                    public void @var() { _ = Mode; _ = @event; }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("keyword types", output, compilerErrors);

        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);
        await Assert.That(output.Sources.Length).IsEqualTo(1);
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// The suite name and benchmark labels are <b>display strings</b>, not
    /// identifiers: <c>@lock</c> must still be reported to the user as
    /// <c>lock</c>, never as <c>@lock</c>.
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.2/display-names")]
    public async Task DisplayNames_AreNotEscaped()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class @class
                {
                    [Benchmark]
                    public void @lock() { }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var code = GeneratorHarness.AllSources(output);
        Batch1Support.LogRun(
            "display names",
            output,
            GeneratorHarness.CompileErrors(source, output)
        );

        // The name handed to Benchmark.Run(...) stays a plain string literal.
        await Assert.That(code.Contains("\"lock\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("\"@lock\"", StringComparison.Ordinal)).IsFalse();

        // …and the suite keeps the user's type name without the escape.
        await Assert.That(code.Contains("\"class\",", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// Explicit interface implementations: <c>[Benchmark] void IThing.Do()</c>.
    /// Batch 1 allows either "call it through the interface" or "report a
    /// diagnostic"; this implementation reports <c>PBGEN019</c>, because the
    /// generated call site would otherwise have to materialise a cast in every
    /// method shape (sync, async, per-iteration setup/teardown) and silently
    /// change the measurement.
    /// <para>Expected at HEAD: <b>RED</b> — no diagnostic at all, and the
    /// emitted <c>this.IThing.Do()</c> fails with CS1003/CS1061.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.2/explicit-interface")]
    public async Task ExplicitInterfaceImplementation_ReportsDiagnostic()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                public interface IThing
                {
                    void Do();
                }

                [BenchmarkClass]
                public partial class Exp : IThing
                {
                    [Benchmark]
                    void IThing.Do() { }

                    [Benchmark]
                    public void Other() { }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("explicit interface", output, compilerErrors);

        // One clear diagnostic instead of uncompilable generated code…
        var generatorErrors = Batch1Support.GeneratorErrors(output);
        await Assert.That(generatorErrors.Count).IsEqualTo(1);
        await Assert.That(generatorErrors[0].Id).IsEqualTo("PBGEN019");
        await Assert.That(generatorErrors[0].GetMessage()).Contains("IThing.Do");

        // …and no generated source at all, so nothing can fail to compile.
        await Assert.That(output.Sources.Length).IsEqualTo(0);
        await Assert.That(compilerErrors.Any(static d => d.Id is "CS1003" or "CS1061")).IsFalse();
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// A lifecycle method that is an explicit interface implementation must be
    /// rejected the same way (it would be called as <c>this.IThing.Setup()</c>).
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.2/explicit-interface-lifecycle")]
    public async Task ExplicitInterfaceLifecycleImplementation_ReportsDiagnostic()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                public interface ISetup
                {
                    void Setup();
                }

                [BenchmarkClass]
                public partial class Exp : ISetup
                {
                    [GlobalSetup]
                    void ISetup.Setup() { }

                    [Benchmark]
                    public void Work() { }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("explicit lifecycle", output, compilerErrors);

        await Assert.That(Batch1Support.ReportsId(output, "PBGEN019")).IsTrue();
        await Assert.That(output.Sources.Length).IsEqualTo(0);
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }
}
