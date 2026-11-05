namespace PicoBench.Tests.Batch1;

using Microsoft.CodeAnalysis;
using PicoBench.Tests.Batch0;

/// <summary>
/// Batch 1 · 1.4 (B3-C6) — <c>[Params]</c> accepts more than primitives, so the
/// formatter has to understand the two <c>TypedConstantKind</c> shapes it used
/// to fall through:
/// <list type="bullet">
/// <item><c>Kind == Type</c>: <c>[Params(typeof(int))]</c>. At HEAD the
/// <c>ITypeSymbol</c> value hits <c>value.ToString()</c> → <c>System.Int32</c>,
/// and the generated <c>new global::System.Type[] { System.Int32 }</c> is
/// <c>CS1525</c> ("Invalid expression term 'int'").</item>
/// <item><c>Kind == Array</c>: <c>[Params(new[]{1,2})]</c>. Reading
/// <c>TypedConstant.Value</c> on an array <b>throws</b> ("TypedConstant is an
/// array. Use Values property."), which used to be one of the CS8785
/// whole-project-loss triggers; after 1.1 it is a PBGEN018 for one class.
/// This item makes it a <b>literal</b> instead: arrays are formatted
/// recursively, so the natural spelling compiles.</item>
/// </list>
/// <para>
/// The judgement call is "recursive formatting", not "new diagnostic": the array
/// is exactly the value the user wrote, and rejecting valid input behind an
/// error would be a usability regression. The diagnostic branch stays as
/// PBGEN008 for a genuinely incompatible value (an <c>int[]</c> handed to an
/// <c>int</c> member).
/// </para>
/// <para>
/// Scope note: only <i>non-variant</i> element types reach the generator as an
/// array constant. A <c>string[]</c>/<c>Type[]</c> argument is convertible to
/// the <c>params object[]</c> by array covariance, so C# <b>unrolls</b> it into
/// one argument per element (and Roslyn then rejects the array creation inside
/// an attribute with <c>CS0182</c>) — there is no legal spelling for "one array
/// value" on a reference-element array, so those members are not usable with
/// <c>[Params]</c> at all, before and after this fix.
/// </para>
/// </summary>
public class ParamsConstantKindTests
{
    /// <summary>
    /// <c>Kind == Type</c> → <c>typeof(...)</c> with a fully qualified name.
    /// <para>Expected at HEAD: <b>RED</b> — <c>CS1525</c> in the generated file.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.4/typeof")]
    public async Task TypeParams_EmitTypeofAndCompile()
    {
        const string source = """
            using System;
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class TypeParams
                {
                    [Params(typeof(int), typeof(string), typeof(Demo.TypeParams))]
                    public Type? Target { get; set; }

                    [Params(typeof(long), null)]
                    public Type? MaybeTarget { get; set; }

                    [Params(typeof(int))]
                    public object? Boxed { get; set; }

                    [Benchmark]
                    public void Work()
                    {
                        _ = Target;
                        _ = MaybeTarget;
                        _ = Boxed;
                    }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("typeof params", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);
        await Assert.That(output.Sources.Length).IsEqualTo(1);

        var code = GeneratorHarness.AllSources(output);
        await Assert.That(code.Contains("typeof(int)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("typeof(string)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("typeof(global::Demo.TypeParams)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("typeof(long)", StringComparison.Ordinal)).IsTrue();

        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// <c>Kind == Array</c> → an array literal of the constant's own type:
    /// <c>int[]</c>, <c>byte[]</c> (element values need the int→byte constant
    /// conversion from 1.5) and an enum array (elements are formatted against
    /// the <i>element</i> type, not the member's array type).
    /// <para>
    /// Expected at HEAD: <b>RED</b> — one <c>PBGEN018</c> per class
    /// ("TypedConstant is an array"), <c>sources = 0</c>.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.4/array")]
    public async Task ArrayParam_EmitsArrayLiteralAndCompiles()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                public enum Mode
                {
                    Fast = 0,
                    Slow = 1,
                }

                [BenchmarkClass]
                public partial class ArrayParams
                {
                    [Params(new[] { 1, 2 })]
                    public int[] Data = new[] { 1 };

                    [Params(new byte[] { 1, 2 })]
                    public byte[] Buffer = new byte[] { 1 };

                    [Params(new[] { Mode.Fast, Mode.Slow })]
                    public Mode[] Modes = new[] { Mode.Fast };

                    [Benchmark]
                    public void Work()
                    {
                        _ = Data;
                        _ = Buffer;
                        _ = Modes;
                    }
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("array params", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);
        await Assert.That(output.Sources.Length).IsEqualTo(1);

        var code = GeneratorHarness.AllSources(output);
        await Assert.That(code.Contains("new int[] { 1, 2 }", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("new byte[] { 1, 2 }", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("global::Demo.Mode[]", StringComparison.Ordinal)).IsTrue();
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Recursion is selected by the <i>value</i>, not by the member: an
    /// <c>int[]</c> passed to an <c>object</c> member is a legal implicit
    /// conversion and must produce an array literal, not a mismatch.
    /// <para>
    /// Expected at HEAD: <b>RED</b> — <c>PBGEN018</c> ("TypedConstant is an
    /// array"), no source.
    /// </para>
    /// <para>
    /// Honest scope note: a <c>null</c> <i>element</i> cannot be reached through
    /// a legal attribute — arrays that keep their identity here are value-type
    /// arrays (<c>int[]</c>, <c>byte[]</c>, enum arrays), and
    /// <c>string[]</c>/<c>Type[]</c>/<c>int?[]</c> are either unrolled by array
    /// covariance or rejected outright by the compiler (<c>CS0182</c>). The
    /// formatter still emits <c>null</c> for a null element (it is the same code
    /// path every other value takes) instead of throwing.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.4/array-on-object-member")]
    public async Task ArrayParamOnObjectMember_Compiles()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class BoxedArray
                {
                    [Params(new[] { 1, 2 })]
                    public object? Boxed { get; set; }

                    [Benchmark]
                    public void Work() => _ = Boxed;
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("array on object member", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert.That(Batch1Support.GeneratorErrors(output).Count).IsEqualTo(0);
        await Assert.That(output.Sources.Length).IsEqualTo(1);

        var code = GeneratorHarness.AllSources(output);
        await Assert.That(code.Contains("new object[] { new int[] { 1, 2 } }", StringComparison.Ordinal)).IsTrue();
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// An array handed to a non-array member is a real mismatch, and the answer
    /// is a diagnostic — <b>not</b> a generator crash and not invalid code.
    /// <para>
    /// Expected at HEAD: <b>RED</b> for the id — a <c>PBGEN018</c> ("generation
    /// failed: TypedConstant is an array") is reported instead of the
    /// <c>PBGEN008</c> ("value is not compatible") the user can act on.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.4/array-on-scalar-member")]
    public async Task ArrayParamOnScalarMember_ReportsPBGEN008AndDoesNotCrash()
    {
        const string source = """
            using PicoBench;

            namespace Demo
            {
                [BenchmarkClass]
                public partial class Mismatch
                {
                    [Params(new[] { 1, 2 })]
                    public int N { get; set; }

                    [Benchmark]
                    public void Work() => _ = N;
                }
            }
            """;

        var output = GeneratorHarness.RunGenerator(source);
        var compilerErrors = GeneratorHarness.CompileErrors(source, output);
        Batch1Support.LogRun("array on scalar", output, compilerErrors);

        await Assert.That(Batch1Support.HasGeneratorFailure(output)).IsFalse();
        await Assert
            .That(Batch1Support.ReportsId(output, "PBGEN018"))
            .IsFalse()
            .Because("an incompatible array is a user error, not a generator failure");

        var incompatible = output.Diagnostics.Where(d => d.Id == "PBGEN008").ToList();
        await Assert.That(incompatible.Count).IsEqualTo(1);
        await Assert.That(incompatible[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert
            .That(incompatible[0].GetMessage(CultureInfo.InvariantCulture))
            .Contains("new int[] { 1, 2 }")
            .Because("the message should show the value in the form the user wrote it");
        await Assert.That(compilerErrors.Count).IsEqualTo(0);
    }
}
