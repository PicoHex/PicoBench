namespace PicoBench.Tests.Batch1;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using PicoBench.Tests.Batch0;

/// <summary>
/// Batch 1 · 1.8, §4.2 mine #11 — "netstandard2.0 / net472 + a <b>pure sync</b>
/// <c>[BenchmarkClass]</c>" (fix is Batch 0 · 0.7 / B3-C5).
/// <para>
/// The generated sync path used to end in <c>ValueTask.FromResult(__suite)</c>.
/// The non-generic <c>ValueTask</c> with static factories only exists from .NET
/// 5 on; a netstandard2.0/net472 consumer gets <c>ValueTask&lt;T&gt;</c> from
/// <c>System.Threading.Tasks.Extensions</c>, whose ns2.0 asset has no such
/// method → <c>error CS0117</c> in a file the consumer never wrote. Async
/// classes were unaffected, which is why the defect hid: it only shows up on
/// the sync emission path.
/// </para>
/// <para>
/// This is the one mine the Batch 0/1 harness could not see: it compiled the
/// generated code against the running .NET's assemblies, where
/// <c>ValueTask.FromResult</c> exists. The tests below compile against real
/// <b>pre-.NET-5 reference sets</b> (netstandard2.0 ref assemblies +
/// <c>System.Threading.Tasks.Extensions</c> 4.6.3, and the SDK's
/// netstandard2.1 ref pack) and <b>prove the surface is what it claims to be</b>
/// with two control snippets — so this test fails if the emitter regresses
/// <i>and</i> if the reference set ever stops being pre-.NET-5 (a silent "pass"
/// would otherwise be one NuGet bump away).
/// </para>
/// </summary>
public class PreNet5TargetFrameworkTests
{
    private const string PureSyncSource = """
        using PicoBench;

        namespace Demo
        {
            [BenchmarkClass]
            public partial class SyncOnly
            {
                [Params(1, 2)]
                public int N { get; set; }

                [Benchmark]
                public void Work()
                {
                    _ = N;
                }
            }
        }
        """;

    /// <summary>What the review names <c>ValueTask.FromResult</c> → the exact API that must not be used.</summary>
    private const string StaticFactoryControl = """
        internal static class RefusesStaticFactory
        {
            internal static System.Threading.Tasks.ValueTask<int> M() =>
                System.Threading.Tasks.ValueTask.FromResult(1);
        }
        """;

    /// <summary>The constructor the generated code must use instead — legal on every TFM.</summary>
    private const string ConstructorControl = """
        internal static class AcceptsConstructor
        {
            internal static System.Threading.Tasks.ValueTask<int> M(int value) =>
                new System.Threading.Tasks.ValueTask<int>(value);
        }
        """;

    /// <summary>
    /// A pure-sync benchmark class, compiled against a netstandard2.0 or
    /// netstandard2.1 reference set: <b>0</b> compiler errors, and the return
    /// statement really is a pre-.NET-5 <c>ValueTask&lt;T&gt;</c> construction.
    /// <para>
    /// Expected before Batch 0 · 0.7: <b>RED</b> — <c>CS0117: 'ValueTask' does
    /// not contain a definition for 'FromResult'</c>.
    /// </para>
    /// </summary>
    [Test]
    [Arguments("netstandard2.0")]
    [Arguments("netstandard2.1")]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.8/ns20-pure-sync")]
    public async Task PureSyncBenchmarkClass_CompilesAgainstPreNet5ValueTaskSurfaces(string surface)
    {
        var (references, origin) = surface switch
        {
            "netstandard2.0" => NetStandard20References(),
            "netstandard2.1" => NetStandard21References(),
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, "unknown surface"),
        };

        Batch0Support.Log($"[{surface}] {references.Length} references from {origin}");

        // A truncated/mis-rooted reference set would make every test below pass
        // for the wrong reason.
        await Assert
            .That(references.Length)
            .IsGreaterThan(100)
            .Because($"the {surface} reference set looks incomplete ({origin})");

        var compilation = CSharpCompilation.Create(
            assemblyName: "PreNet5Consumer",
            syntaxTrees: [CSharpSyntaxTree.ParseText(PureSyncSource, path: "Consumer.cs")],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create([
            new BenchmarkGenerator().AsSourceGenerator(),
        ]);
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var generatorResult = runResult.Results.Single();

        var generated = generatorResult
            .GeneratedSources.Select(static source => source.SyntaxTree)
            .ToArray();
        var withGenerated = compilation.AddSyntaxTrees(generated);
        var errors = withGenerated
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        Batch1Support.LogRun(
            $"{surface} pure sync",
            new GeneratorHarness.GeneratorOutput
            {
                Diagnostics = generatorResult.Diagnostics,
                DriverDiagnostics = runResult.Diagnostics,
                HintNames = generatorResult
                    .GeneratedSources.Select(static source => source.HintName)
                    .ToArray(),
                Sources = generatorResult
                    .GeneratedSources.Select(static source => source.SourceText.ToString())
                    .ToArray(),
                Trees = generated,
            },
            errors
        );

        await Assert.That(generatorResult.Diagnostics.Length).IsEqualTo(0);
        await Assert.That(generated.Length).IsEqualTo(1);
        await Assert.That(Batch1Support.DescribeAll(errors)).IsEqualTo(string.Empty);

        // ── controls: the reference set must really be a pre-.NET-5 surface ──
        var factoryErrors = ErrorsIn(withGenerated, "Control.Factory.cs", StaticFactoryControl);
        Batch0Support.Log(
            $"reasons: [{string.Join(", ", factoryErrors.Select(static d => d.Id))}]"
        );
        await Assert.That(factoryErrors.Count).IsEqualTo(1);
        await Assert
            .That(factoryErrors[0].Id)
            .IsEqualTo("CS0117")
            .Because(
                $"{surface} must not expose ValueTask.FromResult, otherwise this test cannot fail"
            );

        var constructorErrors = ErrorsIn(
            withGenerated,
            "Control.Constructor.cs",
            ConstructorControl
        );
        await Assert
            .That(Batch1Support.DescribeAll(constructorErrors))
            .IsEqualTo(string.Empty)
            .Because($"new ValueTask<int>(value) must compile on {surface}");
    }

    private static IReadOnlyList<Diagnostic> ErrorsIn(
        Compilation compilation,
        string path,
        string source
    )
    {
        var tree = CSharpSyntaxTree.ParseText(
            SourceText.From(source, System.Text.Encoding.UTF8),
            path: path
        );
        return compilation
            .AddSyntaxTrees(tree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    // ── reference sets ──────────────────────────────────────────────────

    /// <summary>
    /// netstandard2.0: the 114 ref assemblies from <c>netstandard.library</c>
    /// 2.0.3 plus <c>System.Threading.Tasks.Extensions</c> (the very asset the
    /// package pins, 4.6.3), i.e. what a real ns2.0 consumer compiles against.
    /// </summary>
    private static (MetadataReference[] References, string Origin) NetStandard20References()
    {
        var root = NuGetPackagesRoot();

        var refDir = VersionedFolders(Path.Combine(root, "netstandard.library"))
            .Select(static folder => Path.Combine(folder, "build", "netstandard2.0", "ref"))
            .FirstOrDefault(Directory.Exists);
        var tasksExtensions = VersionedFolders(
                Path.Combine(root, "system.threading.tasks.extensions")
            )
            .Select(static folder =>
                Path.Combine(
                    folder,
                    "lib",
                    "netstandard2.0",
                    "System.Threading.Tasks.Extensions.dll"
                )
            )
            .FirstOrDefault(File.Exists);

        if (refDir is null || tasksExtensions is null)
            throw new InvalidOperationException(
                "The netstandard2.0 reference assemblies are not in the NuGet package cache "
                    + $"({root}). Both come in with the normal restore of this repository: "
                    + "netstandard.library 2.0.3 (the src/PicoBench netstandard2.0 target) and "
                    + "System.Threading.Tasks.Extensions (Directory.Packages.props)."
            );

        // The ref set carries its own (type-forwarding) copy; use the pinned
        // package asset instead so the surface is the real one.
        var references = Directory
            .GetFiles(refDir, "*.dll")
            .Where(path =>
                !Path.GetFileName(path)
                    .Equals(
                        "System.Threading.Tasks.Extensions.dll",
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Concat([
                MetadataReference.CreateFromFile(tasksExtensions),
                MetadataReference.CreateFromFile(typeof(BenchmarkClassAttribute).Assembly.Location),
            ])
            .ToArray();

        return (references, $"{refDir} + {tasksExtensions} + PicoBench(netstandard2.0)");
    }

    /// <summary>
    /// netstandard2.1: the ref pack that ships with the SDK (derived from the
    /// running runtime's location, so no environment variable is guessed). Its
    /// <c>ValueTask</c> also predates the .NET 5 static factories, which is what
    /// this test needs; PicoBench's ns2.0 assembly resolves through the pack's
    /// type forwarders (CS1701 warnings only).
    /// </summary>
    private static (MetadataReference[] References, string Origin) NetStandard21References()
    {
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var corelib = trusted
            ?.Split(Path.PathSeparator)
            .FirstOrDefault(static path =>
                Path.GetFileName(path)
                    .Equals("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase)
            );

        if (corelib is null)
            throw new InvalidOperationException(
                "Could not locate System.Private.CoreLib.dll in the running runtime."
            );

        // <dotnet>/shared/Microsoft.NETCore.App/<version>/System.Private.CoreLib.dll
        var dotnetRoot = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(corelib)!, "..", "..", "..")
        );
        var refDir = VersionedFolders(Path.Combine(dotnetRoot, "packs", "NETStandard.Library.Ref"))
            .Select(static folder => Path.Combine(folder, "ref", "netstandard2.1"))
            .FirstOrDefault(Directory.Exists);

        if (refDir is null)
            throw new InvalidOperationException(
                $"The SDK's NETStandard.Library.Ref pack is missing under {dotnetRoot}/packs."
            );

        var references = Directory
            .GetFiles(refDir, "*.dll")
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Concat([
                MetadataReference.CreateFromFile(typeof(BenchmarkClassAttribute).Assembly.Location),
            ])
            .ToArray();

        return (references, $"{refDir} + PicoBench(netstandard2.0)");
    }

    /// <summary>Version folders of one package/pack, newest first.</summary>
    private static IEnumerable<string> VersionedFolders(string parent) =>
        Directory.Exists(parent)
            ? Directory
                .GetDirectories(parent)
                .OrderByDescending(
                    static path => Path.GetFileName(path),
                    StringComparer.OrdinalIgnoreCase
                )
            : [];

    private static string NuGetPackagesRoot()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages"
        );
    }
}
