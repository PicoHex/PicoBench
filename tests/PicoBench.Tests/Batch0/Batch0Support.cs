namespace PicoBench.Tests.Batch0;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>
/// Shared helpers for the Batch 0 "known cost" regression net
/// (<c>.review-2026-11/00-FINAL-VERDICT.md</c> §5, item 0.0).
/// <para>
/// The helpers deliberately stay on the public API surface: they call
/// <see cref="Benchmark"/> / <see cref="Runner"/> the way a consumer would and
/// never re-implement a measurement formula, so a test can only pass when the
/// production code really reports the cost.
/// </para>
/// </summary>
internal static class Batch0Support
{
    /// <summary>
    /// The additive per-operation floor Batch 0 · 0.1 promises for an empty
    /// delegate: ≤0.5 ns/op, or an explicitly disclosed overhead.
    /// <para>
    /// Relaxed from 0.5 ns to 1.5 ns by R0's review
    /// (<c>.review-2026-11/08-regression-net-review.md</c> §2.4): the residual of
    /// two independent empty-delegate measurements has sd ≈ 0.5 ns, so 0.5 ns
    /// is ≈1 σ and would flag a correct implementation roughly half the time.
    /// 1.5 ns is still 4.5-9× below what HEAD reports (6.7-13.6 ns/op), so the
    /// guard keeps its red at HEAD.
    /// </para>
    /// </summary>
    internal const double ResolutionFloorNanoseconds = 1.5;

    /// <summary>
    /// Configuration for short runs that are *not* auto-calibrated, so the
    /// requested iteration count reaches the timing loop unchanged. Warmup is
    /// pinned to 0 because <see cref="Benchmark.Run(string, Action, Action?, BenchmarkConfig?, Action?, Action?)"/>
    /// would otherwise warm up 1000 times.
    /// </summary>
    internal static BenchmarkConfig UncalibratedConfig(
        int sampleCount = 20,
        int iterationsPerSample = 5_000
    ) =>
        new()
        {
            WarmupIterations = 0,
            SampleCount = sampleCount,
            IterationsPerSample = iterationsPerSample,
            AutoCalibrateIterations = false,
            ForceGcBeforeBenchmark = false,
            RetainSamples = true,
        };

    /// <summary>Arithmetic median; used to damp single-run noise in timing tests.</summary>
    internal static double Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
            return double.NaN;

        var sorted = values.OrderBy(static v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>Median of <paramref name="count"/> repeated measurements of the same statistic.</summary>
    internal static double MedianOf(Func<double> measure, int count)
    {
        var values = new List<double>(count);
        for (var i = 0; i < count; i++)
            values.Add(measure());

        return Median(values);
    }

    /// <summary>
    /// Words a formatter may print to say "this is not a genuine measurement"
    /// (delegate floor disclosed, value below the resolution floor, clock
    /// granularity reached). Deliberately narrower than "N/A": a bare
    /// <c>N/A</c> in an unrelated column must never count as a disclosure —
    /// R0 §2.10 warned that a document-wide <c>Contains("N/A")</c> turns
    /// tautological as soon as more than one column can be empty.
    /// </summary>
    private static readonly Regex ResolutionMarkerPattern = new(
        "below[ _-]?resolution|resolution[ _-]?limit|overhead|unmeasured|not[ _-]?measured|unmeasurable",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    /// <summary>
    /// Cell values that mean "this cell holds no number" when found in the
    /// exact per-operation cell (CSV <c>Avg_ns</c>) of the row.
    /// </summary>
    private static readonly string[] NotCollectedCells =
    [
        "N/A",
        "NA",
        "null",
        "-",
        "unknown",
        "NaN",
    ];

    // Property names a fix would plausibly use for "this number did not reach
    // the resolution floor / was not measured" (Batch 0 · 0.1 / 0.3).
    private static readonly string[] ResolutionSignalNames =
    [
        "ResolutionLimited",
        "IsResolutionLimited",
        "BelowResolution",
        "IsBelowResolution",
        "ResolutionLimitNanoseconds",
        "Unmeasured",
        "IsUnmeasured",
    ];

    /// <summary>
    /// Reads the per-operation value a <b>user actually sees</b> for
    /// <paramref name="result"/> and decides whether the library discloses the
    /// delegate floor / resolution limit <b>in its formatted output</b>.
    /// <para>
    /// R0's stub experiment (§2.4) proved that guessing property names on
    /// <see cref="Statistics"/> is spoofable: adding
    /// <c>public double OverheadPerOp =&gt; 1.0;</c> — a property nothing reads —
    /// turned the old assertion green while the user still saw 5.5 ns/op. This
    /// helper therefore only trusts what a formatter prints: either the exact
    /// per-operation cell of the row carries a "not a real measurement" marker,
    /// or it carries a number (returned through
    /// <paramref name="numericPerOpNanoseconds"/> so the caller can apply the
    /// absolute floor itself). A decoration property cannot influence either.
    /// </para>
    /// </summary>
    internal static bool FormattedOutputDisclosesResolution(
        BenchmarkResult result,
        out double? numericPerOpNanoseconds,
        out string evidence
    )
    {
        numericPerOpNanoseconds = null;
        var notes = new List<string>(4);

        // ── CSV: the one place where a cell can be located exactly ──────
        var csv = new CsvFormatter().Format(result);
        var csvCell = ReadCsvPerOpCell(csv, result.Name);
        notes.Add($"csv Avg_ns='{csvCell}'");

        if (double.TryParse(csvCell, NumberStyles.Float, CultureInfo.InvariantCulture, out var cellValue))
        {
            numericPerOpNanoseconds = cellValue;
            evidence = string.Join("; ", notes) + " → plain number, not a disclosure";
            return false;
        }

        if (IsNotACollectedValue(csvCell))
        {
            evidence = string.Join("; ", notes) + " → the per-op cell itself says 'not measured'";
            return true;
        }

        // ── Console / Markdown / HTML: the marker must sit on this row ───
        foreach (var (name, text) in new (string Name, string Text)[]
        {
            ("console", new ConsoleFormatter().Format(result)),
            ("markdown", new MarkdownFormatter().Format(result)),
            ("html", new HtmlFormatter().Format(result)),
        })
        {
            var row = FindRowMentioning(text, result.Name);
            notes.Add($"{name} row='{Shorten(row)}'");
            if (ResolutionMarkerPattern.IsMatch(row))
            {
                evidence = string.Join("; ", notes) + $" → {name} discloses the floor on that row";
                return true;
            }
        }

        evidence = string.Join("; ", notes) + " → no formatter discloses the floor";
        return false;
    }

    private static bool IsNotACollectedValue(string cell) =>
        cell.Length > 0
        && NotCollectedCells.Any(value => cell.Trim().Equals(value, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the <c>Avg_ns</c> cell of the CSV row whose name matches.</summary>
    private static string ReadCsvPerOpCell(string csv, string resultName)
    {
        var lines = csv.Replace("\r\n", "\n").Split('\n');
        var headerIndex = Array.FindIndex(
            lines,
            static line => line.StartsWith("Name,", StringComparison.Ordinal)
        );
        if (headerIndex < 0 || headerIndex + 1 >= lines.Length)
            return "<no Name, header in csv>";

        var header = lines[headerIndex].Split(',');
        var column = Array.FindIndex(
            header,
            static cell => cell.Equals("Avg_ns", StringComparison.OrdinalIgnoreCase)
        );
        if (column < 0)
            return "<no Avg_ns column in csv>";

        var row = Array.FindIndex(
            lines,
            headerIndex + 1,
            line => line.Split(',') is { Length: > 0 } cells
                && cells[0].Trim('"', '\'').Equals(resultName, StringComparison.Ordinal)
        );
        if (row < 0)
            return $"<no csv row named {resultName}>";

        var cells = lines[row].Split(',');
        return column < cells.Length ? cells[column].Trim('"', '\'') : "<csv row too short>";
    }

    /// <summary>
    /// Returns the value of <paramref name="column"/> in the CSV row named
    /// <paramref name="rowName"/>.
    /// <para>
    /// Throws when the header, the column or the row is missing: a helper that
    /// returned a placeholder string would let a numeric assertion pass on nothing
    /// at all.
    /// </para>
    /// </summary>
    internal static string ReadCsvCell(string csv, string rowName, string column)
    {
        var lines = csv.Replace("\r\n", "\n").Split('\n');
        var headerIndex = Array.FindIndex(
            lines,
            static line => line.StartsWith("Name,", StringComparison.Ordinal)
        );
        if (headerIndex < 0)
            throw new InvalidOperationException("the CSV output has no Name, header row");

        var header = lines[headerIndex].Split(',');
        var columnIndex = Array.FindIndex(
            header,
            cell => cell.Equals(column, StringComparison.OrdinalIgnoreCase)
        );
        if (columnIndex < 0)
            throw new InvalidOperationException("the CSV header has no " + column + " column");

        for (var index = headerIndex + 1; index < lines.Length; index++)
        {
            var cells = lines[index].Split(',');
            if (cells.Length > columnIndex && cells[0].Trim(CsvQuotes).Equals(rowName, StringComparison.Ordinal))
                return cells[columnIndex].Trim(CsvQuotes);
        }

        throw new InvalidOperationException("the CSV output has no row named " + rowName);
    }

    /// <summary>Quote characters a CSV cell may carry; stripped before comparing names.</summary>
    private static readonly char[] CsvQuotes = [(char)34, (char)39];

    /// <summary>The first line of <paramref name="text"/> that mentions <paramref name="needle"/>.</summary>
    private static string FindRowMentioning(string text, string needle)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Contains(needle, StringComparison.Ordinal))
                return line;
        }

        return "<no row mentions the benchmark>";
    }

    private static string Shorten(string value) =>
        value.Length <= 160 ? value.Trim() : value[..160].Trim() + "…";

    /// <summary>
    /// Walks up to the repository root and returns the absolute path of
    /// <paramref name="fileName"/> (e.g. <c>Directory.Packages.props</c>).
    /// <para>
    /// Three roots are tried in order: <see cref="AppContext.BaseDirectory"/>,
    /// the current working directory, and the directory of the calling test
    /// source file. The extra roots are what make the guard work under
    /// <c>--artifacts-path</c> (Batch 0's isolated-build rule): the binaries then
    /// live under <c>%TEMP%</c>, so walking up from the binaries can never reach
    /// the repository's <c>Directory.Packages.props</c>. This is path discovery
    /// only — no assertion is relaxed.
    /// </para>
    /// </summary>
    internal static string FindRepositoryFile(
        string fileName,
        [CallerFilePath] string callerFilePath = ""
    )
    {
        string?[] roots =
        [
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(callerFilePath),
        ];

        foreach (var root in roots)
        {
            var directory = string.IsNullOrEmpty(root) ? null : new DirectoryInfo(root);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, fileName);
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException(
            $"Could not find {fileName} above {AppContext.BaseDirectory}, "
                + $"{Directory.GetCurrentDirectory()} or {callerFilePath}",
            fileName
        );
    }

    /// <summary>Reads a centrally managed package version out of <c>Directory.Packages.props</c>.</summary>
    internal static string? ReadCentralPackageVersion(string packageId)
    {
        var props = XDocument.Load(FindRepositoryFile("Directory.Packages.props"));
        return props
            .Descendants("PackageVersion")
            .FirstOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("Include"),
                    packageId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            ?.Attribute("Version")
            ?.Value;
    }

    /// <summary>
    /// True when <see cref="Statistics"/> flags the value as
    /// resolution-limited / unmeasured rather than a genuine measurement.
    /// </summary>
    internal static bool HasResolutionLimitedSignal(Statistics statistics, out string detail)
    {
        var type = typeof(Statistics);

        foreach (var name in ResolutionSignalNames)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
                continue;

            var raw = property.GetValue(statistics);
            switch (raw)
            {
                case bool flag when flag:
                    detail = $"Statistics.{name} = true";
                    return true;
                case bool:
                    detail = $"Statistics.{name} = false";
                    return false;
                default:
                    if (raw is not null && IsNumeric(property.PropertyType))
                    {
                        var value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        detail = $"Statistics.{name} = {value:F3} ns";
                        return !double.IsNaN(value) && value > 0;
                    }

                    break;
            }
        }

        detail = $"Statistics exposes none of [{string.Join(", ", ResolutionSignalNames)}]";
        return false;
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(double)
        || type == typeof(float)
        || type == typeof(decimal)
        || type == typeof(int)
        || type == typeof(long)
        || type == typeof(TimeSpan);

    /// <summary>
    /// Writes a diagnostic line to the TUnit test output (falls back to the
    /// console when no test context is active, e.g. under a debugger).
    /// </summary>
    internal static void Log(string message)
    {
        var writer = TestContext.Current?.OutputWriter;
        if (writer is null)
            Console.WriteLine(message);
        else
            writer.WriteLine(message);
    }
}
