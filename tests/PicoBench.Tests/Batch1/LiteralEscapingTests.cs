namespace PicoBench.Tests.Batch1;

using Microsoft.CodeAnalysis;
using PicoBench.Tests.Batch0;

/// <summary>
/// Batch 1 · 1.3 (B3-C4 / B3-L2) — every string the emitter copies into a
/// literal must be escaped by <b>one</b> implementation, and that
/// implementation must cover the code points C# counts as
/// <c>new_line_character</c> (U+000D, U+000A, <b>U+0085</b>, <b>U+2028</b>,
/// <b>U+2029</b>) plus the rest of the non-printable range (C0 controls,
/// <b>U+007F</b>), and the unpaired surrogates that cannot survive a UTF-8
/// round trip of the generated file.
/// <para>
/// At HEAD there are two independent escapers — <c>CSharpLiteralFormatter</c>
/// (the <c>[Params]</c> path) and <c>Emitter.EscapeRaw</c> (the class
/// <c>Description</c> path) — and both stop at <c>c &lt; 0x20</c>. A raw
/// U+2028 in the middle of a literal terminates it, and everything after it is
/// parsed as code: the consumer sees <c>CS0825</c>/<c>CS1010</c> pointing at
/// line 62 of a file they never wrote.
/// </para>
/// <para>Nodes in this file are RED at HEAD for the reasons noted per test.</para>
/// </summary>
public class LiteralEscapingTests
{
    /// <summary>
    /// <c>[Params]</c> path. The user writes escapes (<c>"a\u2028b"</c>) — the
    /// compiler hands the generator the real code point, and the generator has
    /// to escape it again on the way out.
    /// <para>
    /// Expected at HEAD: <b>RED</b> — the generated literal holds raw
    /// U+0085/U+2028/U+2029/U+007F, so <c>compilerErrors &gt; 0</c>
    /// (<c>CS1010</c>/<c>CS0825</c>).
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.3/params-new-line-code-points")]
    public async Task ParamsStringValues_NewLineCodePoints_AreEscapedAndCompile()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class Literals
                {
                    [Params("nel\u0085x", "ls\u2028x", "ps\u2029x", "del\u007fx", "c0\u0001x", "us\u001fx")]
                    public string? Text { get; set; }

                    [Params('\u0085', '\u2028', '\u2029', '\u007f', '\u0001')]
                    public char Marker { get; set; }

                    [Benchmark]
                    public void Work()
                    {
                        _ = Text;
                        _ = Marker;
                    }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("params literals", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);
        await Assert.That(output.Sources.Length).IsEqualTo(1);

        var code = GeneratorHarness.AllSources(output);
        await AssertThatNoRawCodePoint(code);

        // …and the escapes are actually there (a silently dropped value would
        // also satisfy "no raw code point").
        foreach (var escape in new[] { "\\u0085", "\\u2028", "\\u2029", "\\u007F", "\\u0001" })
        {
            await Assert
                .That(code.Contains(escape, StringComparison.Ordinal))
                .IsTrue()
                .Because($"the generated source must contain the escape sequence {escape}");
        }

        await Assert.That(compilerErrors.Count).IsEqualTo(0);
        await AssertNoTruncationErrors(compilerErrors);
    }

    /// <summary>
    /// <c>Description</c> path — <c>[BenchmarkClass(Description = …)]</c> goes
    /// through <c>Emitter.EscapeRaw</c>, a second, independently evolved copy of
    /// the same escape table.
    /// <para>
    /// Expected at HEAD: <b>RED</b> — same defect, different call site
    /// (<c>Emitter.cs:333</c> in the review's line numbering).
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.3/description-new-line-code-points")]
    public async Task DescriptionString_NewLineCodePoints_AreEscapedAndCompile()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass(Description = "nel\u0085x" + "|ls\u2028x" + "|ps\u2029x" + "|del\u007fx" + "|c0\u0001x")]
                public partial class Described
                {
                    [Benchmark]
                    public void Work() { }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("description literals", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);
        await Assert.That(output.Sources.Length).IsEqualTo(1);

        var code = GeneratorHarness.AllSources(output);
        await AssertThatNoRawCodePoint(code);
        foreach (var escape in new[] { "\\u0085", "\\u2028", "\\u2029", "\\u007F", "\\u0001" })
        {
            await Assert
                .That(code.Contains(escape, StringComparison.Ordinal))
                .IsTrue()
                .Because($"the generated description must contain {escape}");
        }

        await Assert.That(compilerErrors.Count).IsEqualTo(0);
        await AssertNoTruncationErrors(compilerErrors);
    }

    /// <summary>
    /// Unpaired surrogates. A lone <c>\uD800</c> is a legal <c>string</c>
    /// element but <b>not</b> encodable as UTF-8: written raw into
    /// <c>.g.cs</c> the file either fails to save or comes back as U+FFFD, so
    /// the compiled literal silently differs from the attribute.
    /// <para>
    /// Expected at HEAD: <b>RED</b> — the emitted source contains the bare
    /// surrogate instead of <c>\uD800</c> / <c>\uDFFF</c>.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.3/lone-surrogates")]
    public async Task UnpairedSurrogates_AreEscaped()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass(Description = "high\ud800low\udfffend")]
                public partial class Surrogates
                {
                    [Params("h\ud800x", "l\udfffx")]
                    public string? Text { get; set; }

                    [Benchmark]
                    public void Work() => _ = Text;
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("lone surrogates", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        var code = GeneratorHarness.AllSources(output);

        await Assert
            .That(ContainsUnpairedSurrogate(code))
            .IsFalse()
            .Because("a lone surrogate cannot be encoded as UTF-8 in the .g.cs file");
        await Assert.That(code.Contains("\\uD800", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("\\uDFFF", StringComparison.Ordinal)).IsTrue();

        // Only the generated trees are judged here: the user's own file is
        // theirs (a lone surrogate in *their* source is not this generator's
        // bug and Roslyn may or may not warn about it).
        await Assert.That(Batch1Support.ErrorsInGeneratedCode(output, compilerErrors).Count).IsEqualTo(0);
    }

    /// <summary>
    /// A <b>paired</b> surrogate (a real non-BMP character) is legal inside a
    /// C# literal and must not be split into two <c>\uXXXX</c> escapes by an
    /// escaper that treats every surrogate as unpaired. Not red at HEAD — this
    /// is the guard that the 1.3 fix does not break what already worked.
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.3/surrogate-pair")]
    public async Task SurrogatePairs_StayIntactAndCompile()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass(Description = "emoji \U0001F600 pair")]
                public partial class Pair
                {
                    [Params("a\U0001F600b")]
                    public string? Text { get; set; }

                    [Benchmark]
                    public void Work() => _ = Text;
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("surrogate pair", output, compilerErrors);

        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);
        var code = GeneratorHarness.AllSources(output);
        await Assert
            .That(code.Contains("a\U0001F600b", StringComparison.Ordinal))
            .IsTrue()
            .Because("a valid surrogate pair stays a single character in the literal");
        await Assert.That(ContainsUnpairedSurrogate(code)).IsFalse();
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    private static async Task AssertThatNoRawCodePoint(string code)
    {
        foreach (var (name, ch) in new[]
        {
            ("U+0085 (NEL)", '\u0085'),
            ("U+2028 (LS)", '\u2028'),
            ("U+2029 (PS)", '\u2029'),
            ("U+007F (DEL)", '\u007f'),
            ("U+0001", '\u0001'),
            ("U+001F", '\u001f'),
        })
        {
            await Assert
                .That(code.Contains(ch))
                .IsFalse()
                .Because($"{name} must be written as an escape sequence, never raw");
        }
    }

    private static async Task AssertNoTruncationErrors(IReadOnlyList<Diagnostic> compilerErrors)
    {
        foreach (var id in new[] { "CS0825", "CS1010" })
        {
            await Assert
                .That(compilerErrors.Any(d => d.Id == id))
                .IsFalse()
                .Because($"{id} means the literal was truncated and the rest of the file misparsed");
        }
    }

    private static bool ContainsUnpairedSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++; // a valid pair
                    continue;
                }
                return true;
            }

            if (char.IsLowSurrogate(text[i]))
                return true;
        }

        return false;
    }
}
