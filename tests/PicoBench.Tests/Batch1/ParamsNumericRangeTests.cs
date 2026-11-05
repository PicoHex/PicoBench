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
/// conversions</i>, where an <c>int</c> constant converts to
/// <c>sbyte/byte/short/ushort/uint/ulong/char</c> when the <b>value</b> fits.
/// The predicate becomes "type-level implicit <b>or</b> int constant inside the
/// target range", and the range check is what keeps <c>[Params(300)] byte</c> an
/// error instead of turning PBGEN008 into a no-op.
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
        await Assert.That(code.Contains("new ushort[] { 1, 2 }", StringComparison.Ordinal)).IsTrue();
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
        await Assert.That(code.Contains("new byte[] { 0, 255 }", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("new sbyte[] { -128, 127 }", StringComparison.Ordinal)).IsTrue();
        await Assert
            .That(code.Contains("new short[] { -32768, 32767 }", StringComparison.Ordinal))
            .IsTrue();
        await Assert.That(code.Contains("new ushort[] { 0, 65535 }", StringComparison.Ordinal)).IsTrue();
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

        foreach (var (value, member) in new[]
        {
            ("300", "TooBig"),
            ("-1", "Negative"),
            ("128", "TooBigSigned"),
            ("65536", "TooBigUnsigned"),
            ("-1", "NegativeUnsigned"),
        })
        {
            await Assert
                .That(
                    incompatible.Any(d =>
                        d.GetMessage(CultureInfo.InvariantCulture).Contains(value, StringComparison.Ordinal)
                        && d.GetMessage(CultureInfo.InvariantCulture).Contains(member, StringComparison.Ordinal)
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
}
