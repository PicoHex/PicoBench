namespace PicoBench.Tests.Packaging;

using System.Xml.Linq;
using PicoBench.Tests.Batch0;

/// <summary>
/// B3-M2 — a consumer that references the source generator twice (package plus
/// a <c>ProjectReference</c> / vendored copy) used to get two generator
/// instances, two copies of the same partial class and
/// <c>error CS0111: Type 'X' already defines a member called 'RunBenchmarksAsync'</c>.
/// <para>
/// The fix lives in <c>src/buildTransitive/PicoBench.targets</c> and is only
/// worth anything if it is actually imported by consumers — i.e. shipped inside
/// the package, imported <b>after</b> the project body (a props file is too
/// early to see analyzers the consumer declared itself). These tests pin the
/// three properties that make the guard work; nothing here re-tests MSBuild,
/// which is covered end to end by <c>scripts/verify-analyzer-dedupe.ps1</c>.
/// </para>
/// </summary>
public class AnalyzerDeduplicationPackagingTests
{
    private const string TargetsRelativePath = "src/buildTransitive/PicoBench.targets";
    private const string PropsRelativePath = "src/buildTransitive/PicoBench.props";
    private const string ProjectRelativePath = "src/PicoBench/PicoBench.csproj";

    private const string DedupeTargetName = "_PicoBenchRemoveDuplicateGeneratorAnalyzers";
    private const string OptOutProperty = "PicoBenchDisableGeneratorDeduplication";

    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.7/analyzer-dedupe")]
    public async Task Targets_RunBeforeCompileAndHonourTheOptOut()
    {
        var target = FindDedupeTarget();

        var beforeTargets = (string?)target.Attribute("BeforeTargets") ?? "";
        var condition = (string?)target.Attribute("Condition") ?? "";

        Batch0Support.Log($"BeforeTargets={beforeTargets} Condition={condition}");

        await Assert.That(beforeTargets).Contains("CoreCompile");
        await Assert.That(condition).Contains(OptOutProperty);
    }

    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.7/analyzer-dedupe")]
    public async Task Targets_DropForeignCopiesAndKeepThePackagedOne()
    {
        var target = FindDedupeTarget();
        var xml = target.ToString();

        // The removal has to be an <Analyzer Remove="..."/> — anything else
        // (a filtered re-Include, a Reference removal) would not touch what the
        // Csc task receives.
        var removals = target
            .Descendants("Analyzer")
            .Where(static element => element.Attribute("Remove") is not null)
            .Select(static element => (string?)element.Attribute("Remove") ?? "")
            .ToArray();

        Batch0Support.Log($"Analyzer removals: {string.Join(" | ", removals)}");

        await Assert.That(removals.Length).IsEqualTo(1);
        await Assert.That(removals[0]).Contains("_PicoBenchForeignGeneratorCopy");

        // The survivor must be the copy the package ships, not "whichever one".
        await Assert.That(xml).Contains("_PicoBenchPackagedGeneratorCopy");

        // And the generator it dedupes has to be named, not wildcarded.
        var source = File.ReadAllText(Batch0Support.FindRepositoryFile(TargetsRelativePath));
        await Assert.That(source).Contains("PicoBench.Generators");
    }

    [Test]
    [Property("Category", "Batch1")]
    [Property("Batch1", "1.7/analyzer-dedupe")]
    public async Task Package_ShipsTheGuardAsBuildTransitiveTargets()
    {
        // A targets file (not a props file): NuGet imports it after the project
        // body, so @(Analyzer) already holds the consumer's own analyzer items.
        var project = XDocument.Load(Batch0Support.FindRepositoryFile(ProjectRelativePath));

        var packed = project
            .Descendants("None")
            .Where(static element =>
                string.Equals(
                    (string?)element.Attribute("PackagePath"),
                    "buildTransitive/PicoBench.targets",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToArray();

        Batch0Support.Log($"Pack entries for the guard: {packed.Length}");

        await Assert.That(packed.Length).IsEqualTo(1);

        var packAttribute = (string?)packed[0].Attribute("Pack") ?? "";
        await Assert
            .That(packAttribute.Equals("true", StringComparison.OrdinalIgnoreCase))
            .IsTrue();
        await Assert
            .That((string?)packed[0].Attribute("Include") ?? "")
            .Contains("PicoBench.targets");

        // The props file is what adds the packaged analyzer; both must ship.
        var props = project
            .Descendants("None")
            .Any(static element =>
                string.Equals(
                    (string?)element.Attribute("PackagePath"),
                    "buildTransitive/PicoBench.props",
                    StringComparison.OrdinalIgnoreCase
                )
            );

        await Assert.That(props).IsTrue();
        await Assert
            .That(File.Exists(Batch0Support.FindRepositoryFile(TargetsRelativePath)))
            .IsTrue();
        await Assert
            .That(File.Exists(Batch0Support.FindRepositoryFile(PropsRelativePath)))
            .IsTrue();
    }

    private static XElement FindDedupeTarget()
    {
        var targets = XDocument.Load(Batch0Support.FindRepositoryFile(TargetsRelativePath));
        var target = targets
            .Descendants("Target")
            .FirstOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("Name"),
                    DedupeTargetName,
                    StringComparison.Ordinal
                )
            );

        if (target is null)
            throw new InvalidOperationException($"No target named {DedupeTargetName}.");

        return target;
    }
}
