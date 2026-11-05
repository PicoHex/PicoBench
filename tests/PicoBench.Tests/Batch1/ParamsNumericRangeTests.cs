namespace PicoBench.Tests.Batch1;

using Microsoft.CodeAnalysis;
using PicoBench.Tests.Batch0;

/// <summary>
/// Batch 1 · 1.5 (B3-H1, B3-H3) — <b>PBGEN008 must not fire on a value that
/// C# accepts</b>.
/// <para>
/// <c>IsCompatibleWithTargetType</c> only asks <c>ClassifyConversion</c>, which
/// is a purely <i>type-level</i> question: <c>int → byte</c> has no implicit
/// conversion, so <c>[Params(1, 2)] public byte B</c> — the most natural way to
/// parameterise a buffer size — is an <b>Error</b> that blocks the build.
/// C# has a second rule for that: <i>implicit constant expression
/// conversions</i> (§10.2.11), where an <c>int</c> constant converts to
/// <c>sbyte/byte/short/ushort/uint/ulong</c> — and a <c>long</c> constant to
/// <c>ulong</c> — when the <b>value</b> fits.
/// The predicate becomes "type-level implicit <b>or</b> constant inside the
/// target range", and the range check is what keeps <c>[Params(300)] byte</c> an
/// error instead of turning PBGEN008 into a no-op.
/// </para>
/// <para>
/// B-1 of <c>.review-2026-11/30-review-b1.md</c>: <c>char</c> is <b>not</b> in
/// that list. <c>char c = 65;</c> is <c>CS0266</c> — the only implicit
/// conversions <i>to</i> <c>char</c> are from another <c>char</c> (identity) and
/// from <c>ushort</c>/<c>sbyte</c>/<c>byte</c> values that are themselves typed
/// <c>char</c>-compatible. So <c>[Params(65)] char</c> must stay a PBGEN008
/// error: widening the predicate there emitted <c>new char[] { 65 }</c> and the
/// consumer's build died on <c>CS0266</c> inside a generated file.
/// </para>
/// </summary>
public class ParamsNumericRangeTests
{
    /// <summary>
    /// The five small integer types, with int constants that fit.
    /// <para>
    /// Expected at HEAD: <b>RED</b> — five <c>PBGEN008</c> errors
    /// (<c>byte</c>, <c>sbyte</c>, <c>short</c>, <c>ushort</c>, <c>uint</c>);
    /// each one fails a real consumer build.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.5/small-integer-types")]
    public async Task SmallIntegerTypes_InRangeIntConstants_CompileWithoutDiagnostics()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class SmallIntegers
                {
                    [Params(1, 2)]
                    public byte B { get; set; }

                    [Params(1, 2)]
                    public sbyte Sb { get; set; }

                    [Params(1, 2)]
                    public short S { get; set; }

                    [Params(1, 2)]
                    public ushort Us { get; set; }

                    [Params(1, 2)]
                    public uint Ui { get; set; }

                    [Benchmark]
                    public void Work()
                    {
                        _ = B;
                        _ = Sb;
                        _ = S;
                        _ = Us;
                        _ = Ui;
                    }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("small integer params", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert
            .That(Batch1Support.DescribeAll(Batch1Support.GeneratorErrors(output)))
            .IsEqualTo(string.Empty);
        await Assert.That(output.Sources.Length).IsEqualTo(1);

        var code = GeneratorHarness.AllSources(output);
        await Assert.That(code.Contains("new byte[] { 1, 2 }", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("new sbyte[] { 1, 2 }", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("new short[] { 1, 2 }", StringComparison.Ordinal)).IsTrue();
        await Assert
            .That(code.Contains("new ushort[] { 1, 2 }", StringComparison.Ordinal))
            .IsTrue();
        await Assert.That(code.Contains("new uint[] { 1, 2 }", StringComparison.Ordinal)).IsTrue();

        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Boundary values stay accepted: <c>0</c>/<c>255</c> for
    /// <c>byte</c>, <c>-128</c>/<c>127</c> for <c>sbyte</c>, <c>65535</c> for
    /// <c>ushort</c>, <c>-32768</c>/<c>32767</c> for <c>short</c> and
    /// <c>2147483647</c> for <c>uint</c>.
    /// <para>Expected at HEAD: <b>RED</b> — five <c>PBGEN008</c>.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.5/boundaries")]
    public async Task BoundaryValues_AreAccepted()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class Boundaries
                {
                    [Params(0, 255)]
                    public byte B { get; set; }

                    [Params(-128, 127)]
                    public sbyte Sb { get; set; }

                    [Params(-32768, 32767)]
                    public short S { get; set; }

                    [Params(0, 65535)]
                    public ushort Us { get; set; }

                    [Params(0, 2147483647)]
                    public uint Ui { get; set; }

                    [Benchmark]
                    public void Work()
                    {
                        _ = B;
                        _ = Sb;
                        _ = S;
                        _ = Us;
                        _ = Ui;
                    }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("boundary params", output, compilerErrors);

        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);

        var code = GeneratorHarness.AllSources(output);
        await Assert
            .That(code.Contains("new byte[] { 0, 255 }", StringComparison.Ordinal))
            .IsTrue();
        await Assert
            .That(code.Contains("new sbyte[] { -128, 127 }", StringComparison.Ordinal))
            .IsTrue();
        await Assert
            .That(code.Contains("new short[] { -32768, 32767 }", StringComparison.Ordinal))
            .IsTrue();
        await Assert
            .That(code.Contains("new ushort[] { 0, 65535 }", StringComparison.Ordinal))
            .IsTrue();
        await Assert
            .That(code.Contains("new uint[] { 0, 2147483647 }", StringComparison.Ordinal))
            .IsTrue();

        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// The half of the fix that must <b>not</b> change: values outside the
    /// target range stay an actionable <c>PBGEN008</c> error, one per bad
    /// value, and never become silently-truncating code.
    /// <para>
    /// Already green at HEAD (it errors for the wrong reason on the in-range
    /// cases); this is the guard against "fix PBGEN008 by deleting it" and
    /// against a range check with an off-by-one.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.5/out-of-range")]
    public async Task OutOfRangeValues_StillReportPBGEN008()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class Overflow
                {
                    [Params(300)]
                    public byte TooBig { get; set; }

                    [Params(-1)]
                    public byte Negative { get; set; }

                    [Params(128)]
                    public sbyte TooBigSigned { get; set; }

                    [Params(65536)]
                    public ushort TooBigUnsigned { get; set; }

                    [Params(-1)]
                    public uint NegativeUnsigned { get; set; }

                    [Benchmark]
                    public void Work()
                    {
                        _ = TooBig;
                        _ = Negative;
                        _ = TooBigSigned;
                        _ = TooBigUnsigned;
                        _ = NegativeUnsigned;
                    }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("out of range params", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();

        var incompatible = output.Diagnostics.Where(d => d.Id == "PBGEN008").ToList();
        await Assert.That(incompatible.Count).IsEqualTo(5);
        foreach (var diagnostic in incompatible)
            await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);

        foreach (
            var (value, member) in new[]
            {
                ("300", "TooBig"),
                ("-1", "Negative"),
                ("128", "TooBigSigned"),
                ("65536", "TooBigUnsigned"),
                ("-1", "NegativeUnsigned"),
            }
        )
        {
            await Assert
                .That(
                    incompatible.Any(d =>
                        d.GetMessage(CultureInfo.InvariantCulture)
                            .Contains(value, StringComparison.Ordinal)
                        && d.GetMessage(CultureInfo.InvariantCulture)
                            .Contains(member, StringComparison.Ordinal)
                    )
                )
                .IsTrue()
                .Because($"value {value} for member {member} must be reported");
        }

        // The rejected members are dropped, so what is left still compiles.
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Type-level implicit conversions that already worked (int → long/double/
    /// float/decimal) keep working — the constant-range branch is an
    /// <b>addition</b> to the predicate, not a replacement.
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.5/type-level-conversions")]
    public async Task TypeLevelImplicitConversions_StillAccepted()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class Widening
                {
                    [Params(1, 2)]
                    public long L { get; set; }

                    [Params(1, 2)]
                    public double D { get; set; }

                    [Params(1, 2)]
                    public float F { get; set; }

                    [Params(1, 2)]
                    public decimal M { get; set; }

                    [Params(1, 2)]
                    public int I { get; set; }

                    [Benchmark]
                    public void Work()
                    {
                        _ = L;
                        _ = D;
                        _ = F;
                        _ = M;
                        _ = I;
                    }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("widening params", output, compilerErrors);

        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// B-1 of <c>.review-2026-11/30-review-b1.md</c> — <c>[Params(65)] char</c>
    /// must stay an actionable <b>PBGEN008</b>, never "no diagnostic, plus
    /// <c>new char[] { 65, 66 }</c> in the generated file".
    /// <para>
    /// <c>char</c> is not part of the implicit constant expression conversions:
    /// <c>char c = 65;</c> is <c>CS0266</c>, so no legal C# expression turns the
    /// user's <c>int</c> argument into a <c>char</c> value. HEAD widened the
    /// predicate with <c>SpecialType.System_Char</c> anyway, which emitted
    /// non-compiling code and — worse — reported <b>nothing</b> to the user.
    /// </para>
    /// <para>
    /// This is the <c>main</c> behaviour (<c>4adf19a</c> reported one PBGEN008
    /// here) restored, and the generator-side half of the same contract
    /// <c>[Params(300)] byte</c> already has: a value the generated code cannot
    /// compile is refused with a diagnostic naming the member.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.5/char-int-constant")]
    public async Task CharMember_IntConstant_ReportsExactlyOnePBGEN008()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class CharParam
                {
                    [Params(65, 66)]
                    public char C { get; set; }

                    [Benchmark]
                    public void Work() { _ = C; }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("char params from int", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();

        var incompatible = output.Diagnostics.Where(d => d.Id == "PBGEN008").ToList();
        await Assert
            .That(incompatible.Count)
            .IsEqualTo(1)
            .Because(
                "one diagnostic for the member, not one per value — and never zero "
                    + $"(sources={output.Sources.Length}, compilerErrors={Batch1Support.DescribeAll(compilerErrors)})"
            );
        await Assert.That(incompatible[0].Severity).IsEqualTo(DiagnosticSeverity.Error);

        var message = incompatible[0].GetMessage(CultureInfo.InvariantCulture);
        Batch0Support.Log($"PBGEN008 message: {message}");
        await Assert.That(message).Contains("65");
        await Assert.That(message).Contains("C");
        await Assert.That(message).Contains("char");

        // No source for that class (an Error keeps it out of generation), and
        // therefore nothing the compiler can reject with CS0266.
        await Assert.That(output.Sources.Length).IsEqualTo(0);
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
        await Assert
            .That(
                compilerErrors.Any(d =>
                    d.Id == "CS0266"
                    || d.GetMessage(CultureInfo.InvariantCulture)
                        .Contains("char", StringComparison.Ordinal)
                )
            )
            .IsFalse()
            .Because("the CS0266 that B-1 was about must not be able to come back");
    }

    /// <summary>
    /// The other half of B-1: <c>char</c> values written <b>as chars</b> are an
    /// identity conversion and must keep working. Deleting the range widening is
    /// not allowed to break <c>[Params('a', 'b')]</c> — the <i>legal</i> way to
    /// parameterise a char dimension.
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.5/char-char-constant")]
    public async Task CharMember_CharConstants_StillGenerateCompilableCode()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class CharLiteralParam
                {
                    [Params('a', 'b')]
                    public char C { get; set; }

                    [Benchmark]
                    public void Work() { _ = C; }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("char params from chars", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert
            .That(Batch1Support.DescribeAll(Batch1Support.GeneratorErrors(output)))
            .IsEqualTo(string.Empty);
        await Assert.That(output.Sources.Length).IsEqualTo(1);
        await Assert
            .That(
                GeneratorHarness
                    .AllSources(output)
                    .Contains("new char[] { 'a', 'b' }", StringComparison.Ordinal)
            )
            .IsTrue();
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// N-1 of the same review: a <c>long</c> constant converts implicitly to
    /// <c>ulong</c> when it is non-negative (<c>ulong u = 1L;</c> is legal), so
    /// <c>[Params(1L, 2L)] public ulong U</c> used to fail a consumer build with
    /// PBGEN008 for a program C# accepts.
    /// <para>
    /// Note this one has to be <i>emitted</i> correctly too: the formatter writes
    /// the user's literal (<c>1L</c>) into <c>new ulong[] { … }</c>, which is only
    /// legal through the same constant conversion — so the compile check in this
    /// test is load-bearing, not decoration.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.5/long-to-ulong")]
    public async Task NonNegativeLongConstant_ToULong_IsAccepted()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class LongToULong
                {
                    [Params(1L, 2L)]
                    public ulong U { get; set; }

                    [Params(0L)]
                    public ulong Zero { get; set; }

                    [Benchmark]
                    public void Work()
                    {
                        _ = U;
                        _ = Zero;
                    }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("long to ulong params", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert
            .That(Batch1Support.DescribeAll(Batch1Support.GeneratorErrors(output)))
            .IsEqualTo(string.Empty);
        await Assert.That(output.Sources.Length).IsEqualTo(1);
        await Assert
            .That(
                GeneratorHarness
                    .AllSources(output)
                    .Contains("new ulong[] { 1L, 2L }", StringComparison.Ordinal)
            )
            .IsTrue();
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// The negative half of N-1: <c>ulong u = -1L;</c> is illegal, so
    /// <c>[Params(-1L)] public ulong U</c> must still be a PBGEN008 — teaching
    /// the predicate about <c>long</c> is not allowed to become "any long value
    /// fits any unsigned type".
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.5/negative-long-to-ulong")]
    public async Task NegativeLongConstant_ToULong_StillReportsPBGEN008()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class NegativeLongToULong
                {
                    [Params(-1L)]
                    public ulong U { get; set; }

                    [Benchmark]
                    public void Work() { _ = U; }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("negative long to ulong params", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();

        var incompatible = output.Diagnostics.Where(d => d.Id == "PBGEN008").ToList();
        await Assert.That(incompatible.Count).IsEqualTo(1);
        await Assert.That(incompatible[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(incompatible[0].GetMessage(CultureInfo.InvariantCulture)).Contains("U");

        await Assert.That(output.Sources.Length).IsEqualTo(0);
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }
}
