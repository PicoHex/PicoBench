namespace PicoBench.Tests.Batch2;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PicoBench.Tests.Batch0;

/// <summary>
/// V-G19 (§11) — the "generated, do not hand-edit" claim has to be enforceable.
/// <para>
/// <c>tools/gen-unicode-width.py</c> is named, in both generated files, as the one
/// source of truth for <c>UnicodeWidthTable.cs</c> and the sibling
/// <c>UnicodeWidthSnapshot.txt</c> that the display-width tests use as their
/// oracle. Following that instruction used to be a trap rather than a no-op: the
/// script emitted <c>private static readonly</c> tables while the checked-in file
/// is <c>internal</c> (which <c>TextSanitizer</c> depends on), so a literal
/// regeneration produced <c>CS0122 ×2</c> and nothing in the suite noticed.
/// </para>
/// <para>
/// This test runs the generator <b>for real</b> — into a scratch directory, so it
/// cannot touch the working tree — and compares what it produces with what is
/// checked in, line for line, for both outputs. A hand-edit of a range, of a
/// modifier, or of the snapshot header turns it red; the recorded mutations are in
/// <c>.review-2026-11/50-g22-report.md</c>.
/// </para>
/// </summary>
public class UnicodeWidthGeneratorDriftTests
{
    private const string TableRelativePath = "src/PicoBench/Formatters/UnicodeWidthTable.cs";
    private const string SnapshotRelativePath =
        "tests/PicoBench.Tests/Batch2/UnicodeWidthSnapshot.txt";

    /// <summary>
    /// Anchored on the solution file, not on <c>README.md</c>: the latter exists in
    /// several directories and <c>FindRepositoryFile</c> walks up from the test
    /// binary, so a nearer copy would win.
    /// </summary>
    private static string RepositoryRoot =>
        Path.GetDirectoryName(Batch0Support.FindRepositoryFile("PicoBench.slnx"))!;

    /// <summary>One flat pair of a generated range table: <c>0x0300, 0x036F,</c>.</summary>
    private static readonly Regex RangeLine = new(
        @"^\s*0x(?<start>[0-9A-F]{4,6}), 0x(?<end>[0-9A-F]{4,6}),$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly Regex UnicodeVersion = new(
        @"// Unicode version : (?<version>\d+\.\d+\.\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Regenerating from the UCD must reproduce both checked-in files exactly. This
    /// is the drift gate: it is the difference between a comment that says "do not
    /// hand-edit" and a rule that holds.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "V-G19/regeneration is a no-op")]
    public async Task Regenerating_ReproducesBothCheckedInFiles()
    {
        var python = LocatePython();
        Skip.Unless(
            python is not null,
            "No Python interpreter on this machine (tried PYTHON, py, python3, python). The "
                + "generator is Python; wire this into CI by running the suite on an image with "
                + "python3, which the repository's release tooling already needs."
        );

        // The generator reads the UCD through Python's `unicodedata`, so what it produces
        // is a function of the interpreter on PATH, not of this repository: CPython 3.12
        // bundles Unicode 15.0.0 (the version both checked-in files were cut from), 3.13
        // bundles 15.1.0, and later releases bundle newer ones. CI run 43213497484 is
        // exactly that: win-arm64 regenerated "Unicode version : 15.1.0" and osx-arm64
        // "17.0.0" against a checked-in "15.0.0", while the three legs whose `python3` is
        // 3.12 passed. A byte comparison of two different UCD versions says nothing about
        // hand-edits, so the equality gate is skipped — loudly, and only then — when the
        // host cannot reproduce the checked-in UCD. The version to compare against is the
        // one the generator stamped into the header.
        var checkedInUcdVersion = UnicodeVersion
            .Match(Normalise(ReadRepositoryFile(TableRelativePath)))
            .Groups["version"]
            .Value;
        var hostUcdVersion = HostUcdVersion(python!);
        Batch2Support.Log(
            $"UCD version: checked in '{checkedInUcdVersion}', host python "
                + $"'{hostUcdVersion ?? "unknown"}'"
        );
        Skip.Unless(
            hostUcdVersion is null || hostUcdVersion == checkedInUcdVersion,
            $"this machine's Python ships Unicode {hostUcdVersion}, but the checked-in table and "
                + $"snapshot were generated from Unicode {checkedInUcdVersion}. "
                + "tools/gen-unicode-width.py takes the UCD from Python's `unicodedata`, so "
                + "regenerating on this interpreter can only differ — that is a version mismatch, "
                + "not drift. Run this gate on an interpreter whose `unicodedata.unidata_version` "
                + $"is {checkedInUcdVersion} (CPython 3.12 for 15.0.0), or regenerate the table and "
                + "the snapshot together with a pinned one. The cross-file oracle check "
                + "(TheCheckedInSnapshot_HoldsTheSameRangesAsTheTable) still runs everywhere."
        );

        using var scratch = Scratch.Create();

        var generated = RunGenerator(python!, scratch.Root);

        var producedTable = Normalise(generated.Table);
        var checkedInTable = Normalise(ReadRepositoryFile(TableRelativePath));
        var producedSnapshot = Normalise(generated.Snapshot);
        var checkedInSnapshot = Normalise(ReadRepositoryFile(SnapshotRelativePath));

        Batch2Support.Log(
            $"generated {producedTable.Length} chars of table, "
                + $"{producedSnapshot.Length} chars of snapshot"
        );

        // The message points at the first differing line: "the file changed" is not
        // actionable, "line 21: internal -> private" is.
        await Assert
            .That(producedTable)
            .IsEqualTo(checkedInTable)
            .Because(
                "tools/gen-unicode-width.py is the declared source of truth for "
                    + $"{TableRelativePath}; regenerating it changed the file. "
                    + FirstDifference(producedTable, checkedInTable)
            );

        await Assert
            .That(producedSnapshot)
            .IsEqualTo(checkedInSnapshot)
            .Because(
                "the oracle snapshot must come from the same run as the table, otherwise the "
                    + "test can measure one Unicode version while the library ships another. "
                    + FirstDifference(producedSnapshot, checkedInSnapshot)
            );

        // Spelled out because this is exactly the V-G19 finding: the modifier is part
        // of the generated bytes, not a hand-edit a later regeneration may drop. A
        // `private` table does not compile against TextSanitizer (CS0122).
        await Assert
            .That(producedTable)
            .Contains("    internal static readonly int[] ZeroWidth =")
            .Because("the generated member accessibility is what the checked-in file compiles with");
        await Assert.That(producedTable).Contains("    internal static readonly int[] Wide =");
    }

    /// <summary>
    /// V-G19 §3: the snapshot is an oracle only if it holds the same ranges as the
    /// table it is used to check. Both come out of the one generator run
    /// (<see cref="Regenerating_ReproducesBothCheckedInFiles"/>), so this pins the
    /// two <em>checked-in</em> files to each other as well — a hand-edit of either
    /// breaks the pair even when no Python is installed.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "V-G19/snapshot matches the table")]
    public async Task TheCheckedInSnapshot_HoldsTheSameRangesAsTheTable()
    {
        var tableText = Normalise(ReadRepositoryFile(TableRelativePath));
        var (zero, wide) = ParseTable(tableText);
        var snapshot = ParseSnapshot(Normalise(ReadRepositoryFile(SnapshotRelativePath)));

        var snapshotZero = snapshot
            .Where(static row => row.Cells == 0)
            .Select(static row => (row.Start, row.End))
            .ToList();
        var snapshotWide = snapshot
            .Where(static row => row.Cells == 2)
            .Select(static row => (row.Start, row.End))
            .ToList();

        Batch2Support.Log(
            $"table: {zero.Count} zero-width / {wide.Count} wide ranges; "
                + $"snapshot: {snapshotZero.Count} / {snapshotWide.Count}"
        );

        await Assert
            .That(Render(snapshotZero))
            .IsEqualTo(Render(zero))
            .Because("the snapshot's zero-width ranges are the oracle for the checked-in table");
        await Assert
            .That(Render(snapshotWide))
            .IsEqualTo(Render(wide))
            .Because("the snapshot's wide ranges are the oracle for the checked-in table");

        // The table records which UCD it was cut from; a table and an oracle from two
        // different Unicode versions would agree on the common ranges and disagree at
        // the edges, which is the drift the header warns about.
        await Assert
            .That(UnicodeVersion.Match(tableText).Groups["version"].Value)
            .IsNotEmpty()
            .Because("the generated table must name the UCD version it was generated from");
    }

    // ---------------------------------------------------------------- helpers

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(
            Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))
        );

    /// <summary>
    /// CRLF to LF. The generator writes LF and the working tree may hold either; this
    /// comparison is about content, and a checkout's EOL convention is not a drift.
    /// </summary>
    private static string Normalise(string text) => text.Replace("\r\n", "\n");

    private static string Render(IEnumerable<(int Start, int End)> ranges) =>
        string.Join("\n", ranges.Select(static range => $"0x{range.Start:X4} 0x{range.End:X4}"));

    /// <summary>First differing line, for the failure message ("" when identical).</summary>
    private static string FirstDifference(string actual, string expected)
    {
        var left = actual.Split('\n');
        var right = expected.Split('\n');
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var a = i < left.Length ? left[i] : "<end of file>";
            var b = i < right.Length ? right[i] : "<end of file>";
            if (!string.Equals(a, b, StringComparison.Ordinal))
                return $"First difference at line {i + 1}: generated '{a}' vs checked in '{b}'.";
        }

        return string.Empty;
    }

    private static (List<(int Start, int End)> Zero, List<(int Start, int End)> Wide) ParseTable(
        string text
    )
    {
        var zero = new List<(int Start, int End)>();
        var wide = new List<(int Start, int End)>();
        var current = zero;

        foreach (var line in text.Split('\n'))
        {
            if (line.Contains("int[] ZeroWidth", StringComparison.Ordinal))
                current = zero;
            else if (line.Contains("int[] Wide", StringComparison.Ordinal))
                current = wide;

            var match = RangeLine.Match(line);
            if (match.Success)
                current.Add(
                    (
                        Convert.ToInt32(match.Groups["start"].Value, 16),
                        Convert.ToInt32(match.Groups["end"].Value, 16)
                    )
                );
        }

        return (zero, wide);
    }

    private static List<(int Cells, int Start, int End)> ParseSnapshot(string text) =>
        text.Split('\n')
            .Where(static line => line.Length > 0 && line[0] != '#')
            .Select(static line => line.Split(' '))
            .Select(static parts =>
                (
                    Cells: int.Parse(parts[0], CultureInfo.InvariantCulture),
                    Start: Convert.ToInt32(parts[1], 16),
                    End: Convert.ToInt32(parts[2], 16)
                )
            )
            .ToList();

    /// <summary>
    /// The UCD version bundled with the host interpreter, or <c>null</c> when it cannot
    /// be read (an exotic interpreter) — the caller then falls back to plain byte
    /// equality, which is the behaviour the gate had before this probe existed.
    /// </summary>
    private static string? HostUcdVersion(string python)
    {
        var result = Run(
            python,
            ["-c", "import unicodedata; print('UCD=' + unicodedata.unidata_version)"],
            RepositoryRoot
        );
        if (result.ExitCode != 0)
            return null;

        // Prefixed so that anything else on the streams cannot be mistaken for the
        // version; Run merges stdout and stderr.
        foreach (var line in result.Output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("UCD=", StringComparison.Ordinal))
                return trimmed["UCD=".Length..].Trim();
        }

        return null;
    }

    /// <summary>
    /// The interpreter, or <c>null</c> when the machine has none — the caller turns
    /// that into a skip, never a silent pass.
    /// </summary>
    private static string? LocatePython()
    {
        var candidates = new List<string>();

        var fromEnvironment = Environment.GetEnvironmentVariable("PYTHON");
        if (!string.IsNullOrEmpty(fromEnvironment))
            candidates.Add(fromEnvironment);

        if (OperatingSystem.IsWindows())
            candidates.Add("py");

        candidates.Add("python3");
        candidates.Add("python");

        foreach (var candidate in candidates)
        {
            try
            {
                if (Run(candidate, ["--version"], RepositoryRoot).ExitCode == 0)
                {
                    Batch2Support.Log($"python: {candidate}");
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                Batch2Support.Log($"python candidate '{candidate}' unusable: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the generator with <paramref name="workingDirectory"/> as its cwd. Both
    /// output paths in the script are relative, so the scratch directory is the only
    /// thing that gets written — a test run cannot touch the repository.
    /// </summary>
    private static (string Table, string Snapshot) RunGenerator(string python, string workingDirectory)
    {
        Directory.CreateDirectory(Path.Combine(workingDirectory, "src", "PicoBench", "Formatters"));
        Directory.CreateDirectory(Path.Combine(workingDirectory, "tests", "PicoBench.Tests", "Batch2"));

        var script = Path.Combine(RepositoryRoot, "tools", "gen-unicode-width.py");
        var result = Run(python, [script], workingDirectory);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"tools/gen-unicode-width.py exited {result.ExitCode}:\n{result.Output}"
            );

        return (
            File.ReadAllText(
                Path.Combine(workingDirectory, "src", "PicoBench", "Formatters", "UnicodeWidthTable.cs")
            ),
            File.ReadAllText(
                Path.Combine(workingDirectory, "tests", "PicoBench.Tests", "Batch2", "UnicodeWidthSnapshot.txt")
            )
        );
    }

    private static (int ExitCode, string Output) Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory
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

        // Deterministic and side-effect free: UTF-8 on both streams, and no
        // __pycache__ written beside the checked-in tool.
        startInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

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

        if (!process.WaitForExit(180_000))
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
                $"{fileName} {string.Join(' ', arguments)} did not finish in 180000 ms"
            );
        }

        Task.WaitAll([stdoutClosed.Task, stderrClosed.Task], 180_000);
        process.WaitForExit();

        lock (output)
            return (process.ExitCode, output.ToString());
    }

    /// <summary>A temp directory the generator writes into, removed on dispose.</summary>
    private sealed class Scratch : IDisposable
    {
        private Scratch(string root) => Root = root;

        public string Root { get; }

        public static Scratch Create() =>
            new(Directory.CreateTempSubdirectory("pb-unicode-gen-").FullName);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a green run over.
            }
        }
    }
}
