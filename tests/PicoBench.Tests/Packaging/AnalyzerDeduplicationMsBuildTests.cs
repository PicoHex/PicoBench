namespace PicoBench.Tests.Packaging;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PicoBench.Generators;
using PicoBench.Tests.Batch0;

/// <summary>
/// F1 / merge condition ① of <c>.review-2026-11/31-verify-b1.md</c> — the
/// analyzer-dedup target needs a <b>semantic</b> regression, not an XML shape
/// check.
/// <para>
/// <c>AnalyzerDeduplicationPackagingTests</c> only asserts that the target exists,
/// sits in front of <c>CoreCompile</c> and mentions the opt-out property. The
/// verifier reversed the opt-out <c>Condition</c> — making the guard a no-op for
/// ordinary consumers and destructive for anyone who opted out — and the whole
/// suite stayed green (626/626). The correctness of the one thing that prevents
/// <c>CS0111</c> for a consumer carrying two copies of the generator was pinned
/// only by <c>scripts/verify-analyzer-dedupe.ps1</c>, which CI does not run.
/// </para>
/// <para>
/// So this test does not read the XML: it copies the shipped targets file into a
/// directory laid out exactly like the package
/// (<c>buildTransitive/PicoBench.Generators/analyzers/dotnet/cs/…dll</c>), gives
/// a bare MSBuild project two analyzer items — the packaged copy and a renamed
/// copy of the same assembly, which is the vendored/renamed shape the SDK's own
/// conflict resolution does not collapse — and asks MSBuild what
/// <c>@(Analyzer)</c> holds after the target has run. Default: the duplicate is
/// gone (1). Opt-out: both are back (2). Reversing the <c>Condition</c> makes the
/// first assertion fail; that mutation was run and is recorded in
/// <c>.review-2026-11/32-g15-report.md</c>.
/// </para>
/// <para>
/// No production MSBuild involvement: the project has no SDK, no targets of its
/// own and nothing to build, so the only thing under test is the shipped file.
/// </para>
/// </summary>
public class AnalyzerDeduplicationMsBuildTests
{
    private const string DedupeTargetName = "_PicoBenchRemoveDuplicateGeneratorAnalyzers";
    private const string OptOutProperty = "PicoBenchDisableGeneratorDeduplication";
    private const string GeneratorFileName = "PicoBench.Generators.dll";
    private const string VendoredFileName = "PicoBench.Generators.Copy.dll";
    private const string UnrelatedFileName = "SomeOther.Analyzer.dll";

    /// <summary>
    /// Child processes run here so <c>global.json</c> pins the same SDK the rest of
    /// the suite runs on — a consumer builds under their own pin too, and an
    /// MSBuild evaluation must not silently float to whatever preview happens to
    /// be installed on the machine.
    /// </summary>
    private static readonly string RepositoryRoot = Path.GetDirectoryName(
        Batch0Support.FindRepositoryFile("PicoBench.slnx")
    )!;

    /// <summary>
    /// The dedup target must really remove the duplicate <c>Analyzer</c> item,
    /// must honour the opt-out, and must leave analyzers that are none of its
    /// business alone.
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.7/analyzer-dedupe-msbuild-evaluation")]
    [NotInParallel("msbuild-dedupe")]
    public async Task AnalyzerList_KeepsExactlyOneGeneratorCopy_UnlessOptedOut()
    {
        var dotnetHost = LocateDotnetHost();
        Skip.Unless(
            dotnetHost is not null,
            "No .NET SDK / MSBuild host found (tried DOTNET_HOST_PATH, 'dotnet' on PATH and the "
                + "runtime directory). This test needs a real MSBuild evaluation; wire it into CI by "
                + "running the test suite on an image that has the SDK (the repo's ci.yml already "
                + "does), and keep scripts/verify-analyzer-dedupe.ps1 as the package-level E2E."
        );

        using var layout = ConsumerLayout.Create();
        Batch0Support.Log($"layout root: {layout.Root}");

        // 1) Default: the packaged copy survives, the renamed duplicate is removed.
        var deduped = RunMsBuild(
            dotnetHost!,
            layout,
            $"-t:{DedupeTargetName} -getItem:Analyzer -verbosity:quiet"
        );
        var dedupedAnalyzers = AnalyzeFullPaths(deduped);

        Batch0Support.Log($"[dedupe on] @(Analyzer) = {string.Join(" | ", dedupedAnalyzers)}");

        await Assert
            .That(GeneratorCopies(dedupedAnalyzers).Count)
            .IsEqualTo(1)
            .Because(
                "the duplicate must be removed before Csc sees it, otherwise the consumer "
                    + $"gets CS0111; @(Analyzer) was {string.Join(" | ", dedupedAnalyzers)}"
            );
        await Assert
            .That(IsSameFile(GeneratorCopies(dedupedAnalyzers)[0], layout.PackagedGenerator))
            .IsTrue()
            .Because("the survivor must be the copy the package ships, not whichever came last");

        // …and a third-party analyzer is not collateral damage.
        await Assert
            .That(dedupedAnalyzers.Any(path => IsSameFile(path, layout.UnrelatedAnalyzer)))
            .IsTrue()
            .Because("the guard removes only files of this generator");

        // 2) Opt-out: both copies are handed to the compiler (that is the point of
        //    the switch — a consumer testing their own build of the generator).
        var optedOut = RunMsBuild(
            dotnetHost!,
            layout,
            $"-t:{DedupeTargetName} -getItem:Analyzer -verbosity:quiet "
                + $"-p:{OptOutProperty}=true"
        );
        var optedOutAnalyzers = AnalyzeFullPaths(optedOut);

        Batch0Support.Log($"[dedupe off] @(Analyzer) = {string.Join(" | ", optedOutAnalyzers)}");

        await Assert
            .That(GeneratorCopies(optedOutAnalyzers).Count)
            .IsEqualTo(2)
            .Because(
                $"-p:{OptOutProperty}=true must leave the analyzer list untouched; it was "
                    + string.Join(" | ", optedOutAnalyzers)
            );
        await Assert
            .That(
                GeneratorCopies(optedOutAnalyzers).Any(p => IsSameFile(p, layout.PackagedGenerator))
            )
            .IsTrue();
        await Assert
            .That(
                GeneratorCopies(optedOutAnalyzers).Any(p => IsSameFile(p, layout.VendoredGenerator))
            )
            .IsTrue();
    }

    /// <summary>
    /// N-6 of the review: the "I threw your copy away" message was
    /// <c>Importance="normal"</c>, so a default <c>dotnet build</c> (minimal
    /// verbosity) said nothing at all and the generator disappeared silently.
    /// <para>
    /// Run at <c>-verbosity:minimal</c> and require the message: only
    /// <c>Importance="high"</c> survives that setting, so this test is the pin for
    /// the change rather than a restatement of the XML.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.7/analyzer-dedupe-visible")]
    [NotInParallel("msbuild-dedupe")]
    public async Task DedupeNotice_IsVisibleAtMinimalVerbosity()
    {
        var dotnetHost = LocateDotnetHost();
        Skip.Unless(
            dotnetHost is not null,
            "No .NET SDK / MSBuild host found; the message-visibility check needs a real build."
        );

        using var layout = ConsumerLayout.Create();

        var output = RunMsBuild(dotnetHost!, layout, $"-t:{DedupeTargetName} -verbosity:minimal");

        Batch0Support.Log($"[dedupe notice] {output}");

        await Assert
            .That(output.Contains("ignoring duplicate copy", StringComparison.Ordinal))
            .IsTrue()
            .Because(
                "a silently discarded generator is a support ticket: the message must be "
                    + $"Importance=high so it shows at default verbosity. Output was:\n{output}"
            );
        await Assert.That(output.Contains(VendoredFileName, StringComparison.Ordinal)).IsTrue();
    }

    // ---------------------------------------------------------------- helpers

    private static IReadOnlyList<string> GeneratorCopies(IReadOnlyList<string> analyzerPaths) =>
        analyzerPaths
            .Where(static path =>
                Path.GetFileName(path)
                    .StartsWith("PicoBench.Generators", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

    private static bool IsSameFile(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>
    /// The <c>dotnet</c> muxer, or <c>null</c> when this machine cannot evaluate
    /// MSBuild at all (the caller turns that into a skip, never a silent pass).
    /// </summary>
    private static string? LocateDotnetHost()
    {
        var candidates = new List<string>();

        var fromEnvironment = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(fromEnvironment))
            candidates.Add(fromEnvironment);

        candidates.Add(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        // <dotnet root>/shared/Microsoft.NETCore.App/<version>/ → <dotnet root>
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (!string.IsNullOrEmpty(runtimeDirectory))
        {
            var root = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."));
            candidates.Add(
                Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")
            );
        }

        foreach (var candidate in candidates)
        {
            if (Path.IsPathRooted(candidate) && !File.Exists(candidate))
                continue;

            // "dotnet" on PATH is a valid candidate: Process resolves it.
            try
            {
                var probe = RunProcess(candidate, ["--version"], RepositoryRoot, 60_000);
                if (probe.ExitCode == 0 && probe.Output.Trim().Length > 0)
                {
                    Batch0Support.Log($"MSBuild host: {candidate} (SDK {probe.Output.Trim()})");
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                Batch0Support.Log($"MSBuild host candidate '{candidate}' unusable: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>Runs <c>dotnet msbuild</c> in the layout directory and returns stdout+stderr.</summary>
    private static string RunMsBuild(string dotnetHost, ConsumerLayout layout, string arguments)
    {
        var result = RunProcess(
            dotnetHost,
            ["msbuild", layout.ProjectPath, "-nodeReuse:false", .. Tokenize(arguments)],
            RepositoryRoot,
            180_000
        );

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet msbuild {arguments} failed with exit code {result.ExitCode}:\n{result.Output}"
            );

        return result.Output;
    }

    private static IEnumerable<string> Tokenize(string arguments) =>
        arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Reads the <c>@(Analyzer)</c> items out of the JSON <c>-getItem</c> prints.
    /// Combined with <c>-t:</c> the item list is the one that exists <b>after</b>
    /// the target ran — which is the whole question here.
    /// </summary>
    private static IReadOnlyList<string> AnalyzeFullPaths(string msbuildOutput)
    {
        var start = msbuildOutput.IndexOf('{');
        var end = msbuildOutput.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException(
                $"No MSBuild -getItem JSON in the output:\n{msbuildOutput}"
            );

        using var document = JsonDocument.Parse(msbuildOutput[start..(end + 1)]);
        if (
            !document.RootElement.TryGetProperty("Items", out var items)
            || !items.TryGetProperty("Analyzer", out var analyzer)
        )
            return [];

        return analyzer
            .EnumerateArray()
            .Select(static element =>
                element.TryGetProperty("FullPath", out var fullPath)
                    ? fullPath.GetString() ?? string.Empty
                    : string.Empty
            )
            .Where(static path => path.Length > 0)
            .ToList();
    }

    private static (int ExitCode, string Output) RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutMilliseconds
    )
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        // Keep the child deterministic and quiet: no first-run experience, no
        // telemetry, English diagnostics (the assertions parse nothing else).
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(startInfo)!;
        var output = new StringBuilder();
        var stdoutClosed = new TaskCompletionSource();
        var stderrClosed = new TaskCompletionSource();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
                stdoutClosed.TrySetResult();
            else
                lock (output)
                    output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                stderrClosed.TrySetResult();
            else
                lock (output)
                    output.AppendLine(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already gone — the timeout just lost the race.
            }

            throw new TimeoutException(
                $"{fileName} {string.Join(' ', arguments)} did not finish in {timeoutMilliseconds} ms"
            );
        }

        // Let the redirected streams drain before the buffer is read.
        Task.WaitAll([stdoutClosed.Task, stderrClosed.Task], timeoutMilliseconds);
        process.WaitForExit();

        lock (output)
            return (process.ExitCode, output.ToString());
    }

    /// <summary>
    /// A throwaway directory laid out like the part of the package that matters:
    /// the targets file next to the analyzer folder it points at, plus a bare
    /// project that declares the same generator twice under two file names.
    /// </summary>
    private sealed class ConsumerLayout : IDisposable
    {
        private ConsumerLayout(string root)
        {
            Root = root;
            ProjectPath = Path.Combine(root, "consumer.proj");
            PackagedGenerator = Path.Combine(
                root,
                "buildTransitive",
                "PicoBench.Generators",
                "analyzers",
                "dotnet",
                "cs",
                GeneratorFileName
            );
            VendoredGenerator = Path.Combine(root, "vendor", VendoredFileName);
            UnrelatedAnalyzer = Path.Combine(root, "vendor", UnrelatedFileName);
        }

        public string Root { get; }

        public string ProjectPath { get; }

        public string PackagedGenerator { get; }

        public string VendoredGenerator { get; }

        public string UnrelatedAnalyzer { get; }

        public static ConsumerLayout Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "pb-g15-dedupe-" + Guid.NewGuid().ToString("N")
            );
            var layout = new ConsumerLayout(root);

            Directory.CreateDirectory(Path.GetDirectoryName(layout.PackagedGenerator)!);
            Directory.CreateDirectory(Path.GetDirectoryName(layout.VendoredGenerator)!);

            // The shipped file itself — never a copy kept in the test project, so
            // an edit to PicoBench.targets is what this test evaluates.
            var shippedTargets = Batch0Support.FindRepositoryFile(
                Path.Combine("src", "buildTransitive", "PicoBench.targets")
            );
            File.Copy(
                shippedTargets,
                Path.Combine(root, "buildTransitive", "PicoBench.targets"),
                overwrite: true
            );

            // The same assembly under two file names — the vendored/renamed shape
            // the SDK's own conflict resolution does not collapse (F5).
            var generatorAssembly = typeof(BenchmarkGenerator).Assembly.Location;
            File.Copy(generatorAssembly, layout.PackagedGenerator, overwrite: true);
            File.Copy(generatorAssembly, layout.VendoredGenerator, overwrite: true);
            File.WriteAllText(
                layout.UnrelatedAnalyzer,
                "// a third-party analyzer: none of PicoBench's business"
            );

            File.WriteAllText(layout.ProjectPath, ProjectXml);

            return layout;
        }

        /// <summary>
        /// No SDK, no TargetFramework, nothing to build: the project exists only to
        /// own two <c>@(Analyzer)</c> items and import the shipped targets.
        /// </summary>
        private const string ProjectXml = """
            <Project>
              <Import Project="buildTransitive/PicoBench.targets" />

              <ItemGroup>
                <Analyzer Include="buildTransitive/PicoBench.Generators/analyzers/dotnet/cs/PicoBench.Generators.dll" />
                <Analyzer Include="vendor/PicoBench.Generators.Copy.dll" />
                <Analyzer Include="vendor/SomeOther.Analyzer.dll" />
              </ItemGroup>
            </Project>
            """;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch (IOException ex)
            {
                Batch0Support.Log($"could not delete {Root}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Batch0Support.Log($"could not delete {Root}: {ex.Message}");
            }
        }
    }
}
