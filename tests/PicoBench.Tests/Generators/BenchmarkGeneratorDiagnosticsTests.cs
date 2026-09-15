namespace PicoBench.Tests.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

public class BenchmarkGeneratorDiagnosticsTests
{
    [Test]
    [Property("Category", "Generators")]
    public async Task NonPartialBenchmarkClass_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public class BadBench
            {
                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN001")).IsTrue();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(0);
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task BenchmarkClassWithoutBenchmarks_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class EmptyBench
            {
                public void Helper() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN002")).IsTrue();
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task BenchmarkMethodWithParameters_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class BadBench
            {
                [Benchmark]
                public void Work(int value) { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN003")).IsTrue();
        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN002")).IsFalse();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(0);
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task DuplicateBaseline_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class BadBench
            {
                [Benchmark(Baseline = true)]
                public void A() { }

                [Benchmark(Baseline = true)]
                public void B() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN005")).IsTrue();
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task InvalidGlobalSetupSignature_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class BadBench
            {
                [GlobalSetup]
                public int Setup() => 42;

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN004")).IsTrue();
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task DuplicateIterationSetup_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class BadBench
            {
                [IterationSetup]
                public void SetupA() { }

                [IterationSetup]
                public void SetupB() { }

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN006")).IsTrue();
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task StaticBenchmarkMethod_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class BadBench
            {
                [Benchmark]
                public static void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN003")).IsTrue();
        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN002")).IsFalse();
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task GenericBenchmarkMethod_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class BadBench
            {
                [Benchmark]
                public void Work<T>() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN003")).IsTrue();
        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN002")).IsFalse();
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task StaticParamsField_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class BadBench
            {
                [Params(1, 2)]
                public static int N;

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN007")).IsTrue();
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task EnumParamsValue_GeneratesEnumCastLiteral()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            public enum TestMode
            {
                Fast = 1,
                Slow = 2
            }

            [BenchmarkClass]
            public partial class GoodBench
            {
                [Params(TestMode.Fast, TestMode.Slow)]
                public TestMode Mode { get; set; }

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.GeneratedSources[0]).Contains("(global::TestMode)1");
        await Assert.That(result.GeneratedSources[0]).Contains("(global::TestMode)2");
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task ReadOnlyParamsProperty_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class BadBench
            {
                [Params(1, 2)]
                public int N { get; }

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN007")).IsTrue();
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task IncompatibleParamsValue_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class BadBench
            {
                [Params("oops")]
                public int N { get; set; }

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN008")).IsTrue();
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task ValidBenchmarkClass_GeneratesSourceWithoutErrors()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass(Description = "Suite")]
            public partial class GoodBench
            {
                [Params(1, 2)]
                public int N { get; set; }

                [GlobalSetup]
                public void Setup() { }

                [Benchmark(Baseline = true)]
                public void Baseline() { }

                [Benchmark]
                public void Candidate() { }
            }
            """
        );

        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(1);
        await Assert.That(result.GeneratedSources[0]).Contains("partial class GoodBench");
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task StringParamsValue_GeneratesEscapedStringLiteral()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class GoodBench
            {
                [Params("line\n\"quoted\"")]
                public string Text { get; set; } = string.Empty;

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.GeneratedSources[0]).Contains("\"line\\n\\\"quoted\\\"\"");
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task CharParamsValue_GeneratesEscapedCharLiteral()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class GoodBench
            {
                [Params('\n', '\\')]
                public char Marker { get; set; }

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.GeneratedSources[0]).Contains("'\\n'");
        await Assert.That(result.GeneratedSources[0]).Contains("'\\\\'");
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task FloatAndDoubleParamsValues_GenerateTypedLiterals()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class GoodBench
            {
                [Params(1.5f)]
                public float Ratio { get; set; }

                [Params(2.5d)]
                public double Weight { get; set; }

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.GeneratedSources[0]).Contains("new float[] { 1.5F }");
        await Assert.That(result.GeneratedSources[0]).Contains("new double[] { 2.5D }");
    }

    // ─── Async lifecycle + benchmark validation ────────────────────

    [Test]
    [Property("Category", "Generators")]
    public async Task AsyncTaskGlobalSetup_NoDiagnostic()
    {
        var result = RunGenerator(
            """
            using System.Threading.Tasks;
            using PicoBench;

            [BenchmarkClass]
            public partial class GoodBench
            {
                [GlobalSetup]
                public async Task SetupAsync() { await Task.CompletedTask; }

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(1);
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task AsyncVoidGlobalSetup_ReportsWarning()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class BadBench
            {
                [GlobalSetup]
                public async void Setup() { }

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN009")).IsTrue();
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task AsyncTaskBenchmarkMethod_NoDiagnostic()
    {
        var result = RunGenerator(
            """
            using System.Threading.Tasks;
            using PicoBench;

            [BenchmarkClass]
            public partial class GoodBench
            {
                [Benchmark]
                public async Task WorkAsync() { await Task.CompletedTask; }
            }
            """
        );

        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task StaticAsyncBenchmarkMethod_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using System.Threading.Tasks;
            using PicoBench;

            [BenchmarkClass]
            public partial class BadBench
            {
                [Benchmark]
                public static async Task WorkAsync() { await Task.CompletedTask; }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN003")).IsTrue();
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task BenchmarkAndLifecycleOnSameMethod_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class ConfusedBench
            {
                [Benchmark]
                [GlobalSetup]
                public void Confused() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN010")).IsTrue();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(0);
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task Params_NullValueOnNullableValueType_GeneratesCode()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class NullableBench
            {
                [Params(1, null, 3)]
                public int? N { get; set; }

                [Benchmark]
                public void Work() { _ = N; }
            }
            """
        );

        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(1);
        await Assert.That(result.GeneratedSources[0]).Contains("null");
    }

    // ─── Unsupported class shapes ──────────────────────────────────

    [Test]
    [Property("Category", "Generators")]
    public async Task GenericBenchmarkClass_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class GenericBench<T>
            {
                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN012")).IsTrue();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(0);
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task NestedBenchmarkClass_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            public partial class Outer
            {
                [BenchmarkClass]
                public partial class NestedBench
                {
                    [Benchmark]
                    public void Work() { }
                }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN013")).IsTrue();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(0);
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task RecordBenchmarkClass_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial record RecordBench
            {
                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN014")).IsTrue();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(0);
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task AbstractBenchmarkClass_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public abstract partial class AbstractBench
            {
                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN015")).IsTrue();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(0);
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task BenchmarkClassWithoutPublicParameterlessConstructor_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class NoDefaultCtorBench
            {
                public NoDefaultCtorBench(int seed) { }

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN015")).IsTrue();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(0);
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task AsyncVoidBenchmarkMethod_ReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using System.Threading.Tasks;
            using PicoBench;

            [BenchmarkClass]
            public partial class BadBench
            {
                [Benchmark]
                public async void WorkAsync() { await Task.Delay(1); }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN011")).IsTrue();
        await Assert
            .That(
                result.Diagnostics.Any(d =>
                    d.Id == "PBGEN011" && d.Severity == DiagnosticSeverity.Error
                )
            )
            .IsTrue();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(0);
    }

    // ─── Params and inheritance completeness ───────────────────────

    [Test]
    [Property("Category", "Generators")]
    public async Task EmptyParamsAttribute_ReportsWarning()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            [BenchmarkClass]
            public partial class EmptyParamsBench
            {
                [Params]
                public int N { get; set; }

                [Benchmark]
                public void Work() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN016")).IsTrue();
        await Assert
            .That(
                result.Diagnostics.Any(d =>
                    d.Id == "PBGEN016" && d.Severity == DiagnosticSeverity.Warning
                )
            )
            .IsTrue();
        // Warning only: source is still generated.
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(1);
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task BenchmarkAttributeOnBaseClass_ReportsWarning()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            public class BaseBench
            {
                [Benchmark]
                public void InheritedWork() { }
            }

            [BenchmarkClass]
            public partial class DerivedBench : BaseBench
            {
                [Benchmark]
                public void OwnWork() { }
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN017")).IsTrue();
        await Assert
            .That(
                result.Diagnostics.Any(d =>
                    d.Id == "PBGEN017" && d.Severity == DiagnosticSeverity.Warning
                )
            )
            .IsTrue();
        // Warning only: the derived class's own benchmarks are still generated.
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(1);
    }

    // ─── Awaitable type detection must be namespace-qualified ──────

    [Test]
    [Property("Category", "Generators")]
    public async Task CustomTaskLikeType_ReportsInvalidBenchmarkMethod()
    {
        var result = RunGenerator(
            """
            using PicoBench;

            namespace Custom
            {
                public class Task { }
            }

            [BenchmarkClass]
            public partial class BadBench
            {
                [Benchmark]
                public Custom.Task Work() => new Custom.Task();
            }
            """
        );

        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN003")).IsTrue();
        await Assert.That(result.Diagnostics.Any(d => d.Id == "PBGEN002")).IsFalse();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(0);
    }

    [Test]
    [Property("Category", "Generators")]
    public async Task ValueTaskBenchmarkMethod_NoDiagnostic()
    {
        var result = RunGenerator(
            """
            using System.Threading.Tasks;
            using PicoBench;

            [BenchmarkClass]
            public partial class GoodBench
            {
                [Benchmark]
                public ValueTask WorkAsync() => ValueTask.CompletedTask;
            }
            """
        );

        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.GeneratedSources.Length).IsEqualTo(1);
    }

    private static GeneratorRunResultData RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new BenchmarkGenerator().AsSourceGenerator()
        );
        driver = driver.RunGenerators(compilation);

        var runResult = driver.GetRunResult();
        var generatorResult = runResult.Results.Single();

        return new GeneratorRunResultData(
            generatorResult.Diagnostics,
            generatorResult.GeneratedSources.Select(static s => s.SourceText.ToString()).ToArray()
        );
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Concat([typeof(BenchmarkClassAttribute).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return paths.Select(static path => MetadataReference.CreateFromFile(path)).ToArray();
    }

    private sealed class GeneratorRunResultData(
        ImmutableArray<Diagnostic> diagnostics,
        string[] generatedSources
    )
    {
        public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
        public string[] GeneratedSources { get; } = generatedSources;
    }
}
