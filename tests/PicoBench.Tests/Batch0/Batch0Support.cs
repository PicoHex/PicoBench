namespace PicoBench.Tests.Batch0;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

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
    /// How far above the floor <b>the same run measured</b> a closure may report
    /// before the value is treated as grossly inflated (FIX-FLAKY-2).
    /// <para>
    /// The floor is <c>max(Statistics.OverheadNs / iterations, ResolutionLimitNanoseconds)</c>;
    /// both quantities come from the run under test, so the bound carries no absolute
    /// nanosecond constant and a slow runner cannot break it on its own.
    /// </para>
    /// <para>
    /// Measured ratio <c>reported / floor</c> for an empty delegate, the smallest closure
    /// there is (FIX-FLAKY-2 section 1: 10 rounds per machine state, each round 5 trials of
    /// 20 x 5000 iterations, floors 1.5-3.1 ns/op): worst round <b>0.15</b> on an idle
    /// 16-CPU box, <b>0.11</b> on the same box pinned to 2 CPUs, <b>0.27</b> with 2 competing
    /// busy loops and <b>0.46</b> with 4 of them. 4.0 therefore sits 8.7x above the worst
    /// measured ratio, i.e. a runner would have to inflate an empty delegate by 4x its own
    /// measured floor before this leg moves. The 2-vCPU CI runner that reported 1.900 ns/op
    /// against the old 1.5 ns constant (run 44285730921, win-x64) printed a plain number, so
    /// its floor was somewhere below 1.900 ns/op and its ratio is not known; it is covered
    /// by the same factor as long as that floor exceeds 0.475 ns/op, which no real machine
    /// gets below (an empty delegate plus loop bookkeeping is 3-5 cycles). The leg still
    /// fails the undisclosed 5.5-15.4 ns/op the same empty delegate reported before 0.1
    /// (ratio 3.4-10 on the reference machine). It is a <b>gross-inflation net</b>, not the
    /// defect detector: the detector is the arithmetic identity in
    /// <see cref="RecomputedPerOpAvg"/>, which is exact and carries no noise at all.
    /// </para>
    /// </summary>
    internal const double ResidualNoiseFactor = 4.0;

    /// <summary>
    /// Recomputes <see cref="Statistics.Avg"/> from the raw samples a synchronous run
    /// retained, using only public values, so that "the harness floor was deducted" is
    /// checkable <b>arithmetically</b> instead of by comparing two noisy statistics
    /// (FIX-FLAKY-2, acceptance item 3).
    /// <para>
    /// The synchronous path publishes, per sample,
    /// <c>max(0, elapsed - OverheadNs) / IterationsPerSample</c>: the residual is clamped
    /// at zero and <b>not</b> at the deducted floor (unlike the asynchronous path, whose
    /// floor clamp is mirrored in <c>AsyncOverheadTests.RecomputedAvgFromRawSamples</c>).
    /// The identity therefore holds exactly, whatever the machine does: it contains the
    /// elapsed times only arithmetically, never statistically.
    /// </para>
    /// <para>
    /// A run that skips the subtraction but still publishes the measured floor misses this
    /// by the whole floor per operation (measured under that mutant: delta = 1.6 ns/op
    /// against a tolerance of 1e-9), and a run that deducts an amount other than the one it
    /// publishes misses it by the difference. A run that never measures a floor reports
    /// <c>OverheadNs == 0</c>, which makes the identity vacuous: that shape is the caller's
    /// <c>OverheadNs &gt; 0</c> assertion.
    /// </para>
    /// </summary>
    internal static double RecomputedPerOpAvg(BenchmarkResult result)
    {
        var samples = result.Samples;
        if (samples is null || samples.Count == 0)
            throw new InvalidOperationException(
                "the run did not retain its samples, so its average cannot be re-derived"
            );

        var iterations = result.IterationsPerSample;
        var overhead = result.Statistics.OverheadNs;

        var sum = 0.0;
        foreach (var sample in samples)
        {
            var net = sample.ElapsedNanoseconds - overhead;
            sum += (net > 0 ? net : 0.0) / iterations;
        }

        return sum / samples.Count;
    }

    /// <summary>
    /// Parses the numeric part of a formatted per-operation cell such as <c>&lt;1.643</c>
    /// (the published upper bound).
    /// </summary>
    /// <exception cref="InvalidOperationException">The cell is not an upper bound.</exception>
    internal static double ParseUpperBound(string cell)
    {
        if (!cell.StartsWith("<", StringComparison.Ordinal) || cell.Length < 2)
            throw new InvalidOperationException(
                $"the per-op cell '{cell}' is not an upper bound, so it discloses no limit"
            );

        return double.Parse(cell[1..], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads the source file of the calling test, so a test can pin a requirement it cannot
    /// observe at runtime: a deleted assertion makes its test pass by construction, and only
    /// the text can tell the two apart (the same ablation idea the workflow lint applies to
    /// <c>.github/workflows/ci.yml</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The source file is not where the compile-time path says it is. Thrown rather than
    /// skipped: a guard that silently disappears is the defect it guards against.
    /// </exception>
    internal static string ReadOwnSource([CallerFilePath] string path = "")
    {
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"the test source {path} was not found, so the ablation guard cannot run"
            );

        return File.ReadAllText(path);
    }

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
