namespace PicoBench.Tests.Batch4;

using System.Text.RegularExpressions;
using PicoBench.Tests.Batch0;

/// <summary>
/// Batch 4 — the support baseline, pinned instead of promised.
/// <para>
/// The repository used to promise the opposite thing in three places at once: the
/// generator was built against <b>Roslyn 4.8</b> so that it would load in the
/// <b>.NET SDK 8</b> compiler, a <c>tests/compat/ConsumerSdk8</c> leg proved it,
/// and <c>docs/release-notes/batch0.md</c> §6 published that as the floor. All of
/// it was retired on purpose: the package now requires the <b>.NET SDK 11 or
/// newer</b> (<c>net11.0</c> is the only target framework), and the generator is
/// compiled against <b>Roslyn 5.9+</b>, the floor SDK 10 shipped and every later
/// compiler still loads.
/// </para>
/// <para>
/// With the old leg gone nothing failed when the promise drifted — a
/// <c>Directory.Packages.props</c> edit back to <c>4.8</c> restores the retracted
/// floor, and a README edit can silently promise an SDK that cannot load the
/// analyzer. These tests are what fails. They read the repository, not the built
/// assembly: the property under guard is a version and a sentence, and both live
/// in files.
/// </para>
/// <para>
/// They are deliberately a <b>floor</b> guard, not a ceiling (the deleted guard
/// pinned "at most 4.8"): any version at or above 5.9 passes, in floating
/// (<c>5.9.*</c>) or fixed (<c>5.9.0</c>) form, so a future bump stays a one-line
/// edit here rather than a test rewrite.
/// </para>
/// </summary>
public class SdkBaselineTests
{
    private const string RoslynPackage = "Microsoft.CodeAnalysis.CSharp";

    /// <summary>The Roslyn floor the generator compiles against.</summary>
    private const int FloorMajor = 5;
    private const int FloorMinor = 9;

    /// <summary>
    /// The published statement every README must carry: the build/host floor.
    /// Identifiers stay Latin in all ten languages, so one token covers them.
    /// </summary>
    private const string RequiredSdkToken = "SDK 11";

    /// <summary>The Roslyn floor, as the READMEs spell it (<c>**Roslyn 5.9+**</c>).</summary>
    private const string RequiredRoslynToken = "Roslyn 5.9";

    /// <summary>
    /// The retracted promise, in the forms it used to appear. A README that
    /// carries any of these says an SDK older than 11 works, which is false now:
    /// the projects target <c>net11.0</c>, so an SDK 10 cannot build them either.
    /// </summary>
    private static readonly string[] RetractedPromises =
    [
        "SDK 8",
        "SDK 9",
        "SDK 10",
        "Roslyn 4.8",
        "4.8+",
        "8.0.100",
    ];

    /// <summary>The ten READMEs the repository publishes, English first.</summary>
    public static IEnumerable<string> Readmes =>
        [
            "README.md",
            "README.zh.md",
            "README.zh-tw.md",
            "README.ja.md",
            "README.ko.md",
            "README.ru.md",
            "README.de.md",
            "README.es.md",
            "README.fr.md",
            "README.pt.md",
        ];

    /// <summary>
    /// Anchored on the solution file, not on <c>README.md</c>: the latter exists in
    /// ten copies, and <c>FindRepositoryFile</c> walks up from the test binary, so
    /// a nearer copy would win.
    /// </summary>
    private static string RepositoryRoot =>
        Path.GetDirectoryName(Batch0Support.FindRepositoryFile("PicoBench.slnx"))!;

    /// <summary>
    /// One <c>&lt;PackageVersion|PackageReference|PackageUpdate&gt;</c> element
    /// that names the Roslyn package, whichever order its attributes sit in, so
    /// the <c>Version</c> can be read off its attributes.
    /// </summary>
    private static readonly Regex RoslynItem = new(
        @"<(?<tag>PackageVersion|PackageReference|PackageUpdate)\b(?<attributes>[^>]*)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly Regex IncludeAttribute = new(
        @"\bInclude\s*=\s*""(?<id>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly Regex VersionAttribute = new(
        @"\bVersion\s*=\s*""(?<version>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>The leading <c>major.minor</c> of a version, wildcard or not.</summary>
    private static readonly Regex LeadingMinor = new(
        @"^\[?\s*(?<major>\d+)\.(?<minor>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>
    /// The central Roslyn version is at or above 5.9 — <b>and</b> it is declared
    /// exactly once, so no later <c>PackageVersion</c> can quietly re-pin it lower.
    /// <para>
    /// Expected before Batch 4: <b>RED</b> at <c>4.8</c> (the retracted floor);
    /// green at <c>5.9.*</c>.
    /// </para>
    /// </summary>
    [Test]
    [Property("Category", "Batch4")]
    [Property("Batch4", "baseline: Roslyn 5.9 floor")]
    public async Task CentralRoslynVersion_IsAtLeast59()
    {
        var props = Read("Directory.Packages.props");
        var versions = RoslynVersions(props).ToList();

        await Assert
            .That(versions.Count)
            .IsEqualTo(1)
            .Because(
                "Directory.Packages.props must declare "
                    + $"`{RoslynPackage}` exactly once: two entries leave the winner to "
                    + "evaluation order, and only the loser is visible to a reader."
            );

        var raw = versions[0];
        var (major, minor) = ParseLeadingMinor(raw);

        Batch0Support.Log($"[baseline/roslyn] Directory.Packages.props pins {RoslynPackage} {raw}");

        await Assert
            .That(IsAtLeastFloor(major, minor))
            .IsTrue()
            .Because(
                $"the generator is compiled against Roslyn {FloorMajor}.{FloorMinor}+, the "
                    + $"compiler floor of SDK 10 and of every SDK after it; `{raw}` restores a "
                    + "floor the package no longer supports (Batch 4 withdrew SDK 8 / Roslyn 4.8). "
                    + $"Got {major}.{minor}."
            );
    }

    /// <summary>
    /// No project or props file anywhere in the repository pins the Roslyn package
    /// below 5.9, in <b>any</b> form: a downgrade does not have to happen in the
    /// central file to take effect, and a fixed version in a single csproj is
    /// exactly how the old <c>4.8</c> ceiling came back the last time.
    /// </summary>
    [Test]
    [Property("Category", "Batch4")]
    [Property("Batch4", "baseline: no lower Roslyn pin anywhere")]
    public async Task NoFileInTheRepository_PinsRoslynBelowTheFloor()
    {
        var audited = 0;
        var offenders = new List<string>();

        foreach (var file in BuildFiles())
        {
            audited++;

            foreach (var raw in RoslynVersions(File.ReadAllText(file)))
            {
                var (major, minor) = ParseLeadingMinor(raw);
                Batch0Support.Log(
                    $"[baseline/roslyn] {Path.GetRelativePath(RepositoryRoot, file)}: {raw}"
                );

                if (!IsAtLeastFloor(major, minor))
                    offenders.Add($"{Path.GetRelativePath(RepositoryRoot, file)}: {raw}");
            }
        }

        await Assert
            .That(audited)
            .IsGreaterThan(0)
            .Because(
                "the audit must actually see the build files; zero means the glob found "
                    + "nothing and this test would pass on an empty list."
            );

        await Assert
            .That(offenders)
            .IsEmpty()
            .Because(
                "a pin below "
                    + $"Roslyn {FloorMajor}.{FloorMinor} makes the generator unloadable in the "
                    + "compiler it claims to target. Offending pins: "
                    + string.Join("; ", offenders)
            );
    }

    /// <summary>
    /// The English README — the file the package ships and the other nine are
    /// translated from — states the SDK 11+ baseline and the Roslyn 5.9+ floor.
    /// </summary>
    [Test]
    [Property("Category", "Batch4")]
    [Property("Batch4", "baseline: README states SDK 11+")]
    public async Task EnglishReadme_StatesTheBaseline()
    {
        var readme = Read("README.md");

        await Assert
            .That(readme.Contains(RequiredSdkToken, StringComparison.Ordinal))
            .IsTrue()
            .Because(
                $"README.md must name `{RequiredSdkToken}` (or newer): a consumer has to be "
                    + "able to learn the build floor without reading global.json."
            );

        await Assert
            .That(readme.Contains(RequiredRoslynToken, StringComparison.Ordinal))
            .IsTrue()
            .Because($"README.md must name the generator's Roslyn floor (`{RequiredRoslynToken}+`).");
    }

    /// <summary>
    /// The English README retracts the old promise, in every form it used to
    /// appear. Checking only "SDK 8" would leave "Roslyn 4.8+" and "8.0.100"
    /// free to reintroduce the same claim in different words.
    /// </summary>
    [Test]
    [Property("Category", "Batch4")]
    [Property("Batch4", "baseline: README promises no older SDK")]
    public async Task EnglishReadme_PromisesNoOlderSdk()
    {
        var readme = Read("README.md");
        var found = RetractedPromises.Where(p => readme.Contains(p, StringComparison.Ordinal)).ToList();

        await Assert
            .That(found)
            .IsEmpty()
            .Because(
                "README.md still promises an SDK older than 11: "
                    + string.Join(", ", found.Select(p => $"`{p}`"))
            );
    }

    /// <summary>
    /// All ten READMEs carry the same baseline and none of them keeps the old
    /// promise. The English file is the source, but it is not the one a Chinese or
    /// Russian reader sees, and a translation that drifts here tells that reader
    /// their SDK still works.
    /// </summary>
    /// <param name="readme">File name of the README under test.</param>
    [Test]
    [Property("Category", "Batch4")]
    [Property("Batch4", "baseline: all ten READMEs agree")]
    [Arguments("README.md")]
    [Arguments("README.zh.md")]
    [Arguments("README.zh-tw.md")]
    [Arguments("README.ja.md")]
    [Arguments("README.ko.md")]
    [Arguments("README.ru.md")]
    [Arguments("README.de.md")]
    [Arguments("README.es.md")]
    [Arguments("README.fr.md")]
    [Arguments("README.pt.md")]
    public async Task EveryReadme_CarriesTheSameBaseline(string readme)
    {
        var text = Read(readme);

        await Assert
            .That(text.Contains(RequiredSdkToken, StringComparison.Ordinal))
            .IsTrue()
            .Because($"{readme} does not name `{RequiredSdkToken}`; the ten READMEs must agree.");

        var found = RetractedPromises.Where(p => text.Contains(p, StringComparison.Ordinal)).ToList();

        await Assert
            .That(found)
            .IsEmpty()
            .Because(
                $"{readme} still promises an SDK older than 11: "
                    + string.Join(", ", found.Select(p => $"`{p}`"))
            );
    }

    /// <summary>
    /// A version is at or above the floor when its leading minor is: <c>5.9</c>
    /// and everything after it, and any higher major. <c>5.10</c> is above
    /// <c>5.9</c>, which a string comparison would get backwards.
    /// </summary>
    private static bool IsAtLeastFloor(int major, int minor) =>
        major > FloorMajor || (major == FloorMajor && minor >= FloorMinor);

    /// <summary>
    /// The leading <c>major.minor</c> of <paramref name="raw"/>, reading a floating
    /// band (<c>5.9.*</c>), a fixed version (<c>5.9.0</c>), a range
    /// (<c>[5.9.0,6.0)</c>) and a prerelease (<c>5.9.0-beta.1</c>) the same way:
    /// NuGet's floor is what the band starts at, not what it happens to resolve to
    /// on this machine.
    /// </summary>
    private static (int Major, int Minor) ParseLeadingMinor(string raw)
    {
        var match = LeadingMinor.Match(raw.Trim());
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Cannot read a major.minor floor from the version `{raw}`. A form this "
                    + "test cannot parse must fail loudly: a silent pass would make the "
                    + "baseline guard decorative."
            );
        }

        return (int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Every version string that a file assigns to the Roslyn package, from a
    /// central <c>PackageVersion</c>, a csproj <c>PackageReference</c>, or a
    /// <c>PackageOverride</c>.
    /// </summary>
    private static IEnumerable<string> RoslynVersions(string xml)
    {
        foreach (Match item in RoslynItem.Matches(xml))
        {
            var attributes = item.Groups["attributes"].Value;

            var include = IncludeAttribute.Match(attributes);
            if (!include.Success
                || !string.Equals(include.Groups["id"].Value, RoslynPackage, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var version = VersionAttribute.Match(attributes);
            if (version.Success)
                yield return version.Groups["version"].Value;
        }
    }

    /// <summary>
    /// The build files a Roslyn pin can hide in: the central props, the repo-wide
    /// props/targets, and every project and props/targets file under the folders
    /// that ship. <c>bin</c> and <c>obj</c> are excluded — a generated copy of a
    /// csproj is not a decision anybody made.
    /// </summary>
    private static IEnumerable<string> BuildFiles()
    {
        string[] roots = ["src", "tests", "samples", "tools", "scripts"];
        string[] patterns = ["*.csproj", "*.props", "*.targets"];

        var fixedFiles = new[] { "Directory.Packages.props", "Directory.Build.props", "Directory.Build.targets" }
            .Select(name => Path.Combine(RepositoryRoot, name))
            .Where(File.Exists);

        var discovered = roots
            .Select(root => Path.Combine(RepositoryRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root =>
                patterns.SelectMany(pattern =>
                    Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                )
            )
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            );

        return fixedFiles.Concat(discovered).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Reads a repository-root file, failing loudly when it is missing.</summary>
    private static string Read(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot, relativePath);

        if (!File.Exists(path))
            throw new FileNotFoundException($"{relativePath} does not exist under {RepositoryRoot}.", path);

        return File.ReadAllText(path);
    }
}
