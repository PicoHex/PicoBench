namespace PicoBench.Tests;

using PicoBench.Tests.Batch0;

/// <summary>
/// Open task T3 — <b>GC allocation bytes</b>.
/// <para>
/// The library published GC <b>counts</b> (<c>Gen0</c>/<c>Gen1</c>/<c>Gen2</c>) but
/// never <b>how much was allocated</b>, which is the metric a consumer asks for
/// first (review <c>01-runtime-measurement.md</c> §H2: a benchmark that allocates
/// 100 MB without triggering a collection and a benchmark that allocates nothing
/// both reported <c>0/0/0</c>).
/// </para>
/// <para>
/// These tests pin the four properties the fix has to keep:
/// </para>
/// <list type="number">
/// <item>a synchronous run reports the bytes its measured work allocated
/// (<c>GC.GetAllocatedBytesForCurrentThread</c>, exact — not flagged
/// approximate);</item>
/// <item>an asynchronous run reports bytes too, flagged approximate (the
/// process-wide counter includes other threads);</item>
/// <item><b>not collected</b> is <c>N/A</c> in every format and never <c>0</c>,
/// while a measured <b>zero</b> stays a real <c>0</c> — the same tri-state the
/// GC counters already have;</item>
/// <item>the CSV carries the new column and its own contract clause.</item>
/// </list>
/// </summary>
public class GcAllocationMeasurementTests
{
    // Keeps the allocation alive so the JIT cannot elide it.
    private static byte[]? _sink;

    /// <summary>A 1024-byte array costs 1024 bytes of payload plus one object
    /// header; the band (1x…4x of the payload) only asserts the order of
    /// magnitude, which is what "the counter tracks the work" means.</summary>
    private const int Iterations = 1_000;
    private const int PayloadBytes = 1_024;
    /// <summary>
    /// Allocation is a thread-scoped counter on the synchronous path, so the
    /// number the run publishes must match the work: <c>Iterations</c> arrays of
    /// <c>PayloadBytes</c>. A thread-scoped reading is <b>exact</b>, so the
    /// approximate flag must stay false.
    /// </summary>
    [Test]
    [Property("Category", "GcBytes")]
    [Property("T3", "sync allocation")]
    [NotInParallel] // reads GC allocation counters
    public async Task Sync_ReportsAllocatedBytes_AndDoesNotCallThemApproximate()
    {
        var iterationsPerSample = Iterations;

        var sample = Runner.Time(iterationsPerSample, () => _sink = new byte[PayloadBytes]);
        GC.KeepAlive(_sink);

        var gc = sample.GcInfo;
        Batch0Support.Log(
            $"sync: allocated={Describe(gc?.AllocatedBytes)} approximate={gc?.AllocatedBytesAreApproximate}"
        );

        await Assert.That(gc).IsNotNull();
        await Assert.That(gc!.AllocatedBytes).IsNotNull();
        await Assert
            .That(gc.AllocatedBytes!.Value)
            .IsGreaterThanOrEqualTo((long)iterationsPerSample * PayloadBytes)
            .Because("a 1024-byte array per iteration must show up in the allocation delta");
        await Assert
            .That(gc.AllocatedBytes.Value)
            .IsLessThan((long)iterationsPerSample * PayloadBytes * 4)
            .Because("the delta is the measured arrays, not an unrelated flood");
        await Assert
            .That(gc.AllocatedBytesAreApproximate)
            .IsFalse()
            .Because("the synchronous path reads a thread-scoped counter, which is exact");
    }

    /// <summary>
    /// A workload that allocates nothing publishes a measured <b>zero</b> — the
    /// counter was read twice and did not move. This is what keeps "0 bytes" and
    /// "not collected" apart: the value may not be <c>null</c> here.
    /// </summary>
    [Test]
    [Property("Category", "GcBytes")]
    [Property("T3", "zero allocation")]
    [NotInParallel]
    public async Task ZeroAllocation_ReportsZeroBytes_NotNotCollected()
    {
        var sample = Runner.Time(Iterations, static () => { });

        var gc = sample.GcInfo;
        Batch0Support.Log($"empty lambda x{Iterations}: allocated={Describe(gc?.AllocatedBytes)}");

        await Assert.That(gc).IsNotNull();
        await Assert
            .That(gc!.AllocatedBytes)
            .IsNotNull()
            .Because("a measured zero must be reported as 0, never as 'not collected'");
        await Assert
            .That(gc.AllocatedBytes!.Value)
            .IsGreaterThanOrEqualTo(0L)
            .And.IsLessThanOrEqualTo(4_096L)
            .Because("an empty loop allocates nothing; a small slack absorbs runtime bookkeeping");
    }

    /// <summary>
    /// The asynchronous path cannot use the thread counter (an <c>await</c> may
    /// resume on another thread), so it reads the process-wide counter. The
    /// number is therefore flagged approximate and carries the same <c>~</c>
    /// marker the GC counts and CPU cycles already use.
    /// </summary>
    [Test]
    [Property("Category", "GcBytes")]
    [Property("T3", "async allocation")]
    [NotInParallel]
    public async Task Async_ReportsAllocatedBytes_MarkedApproximate()
    {
        var sample = await Runner.TimeAsync(
            50,
            async () =>
            {
                await Task.Yield();
                _sink = new byte[PayloadBytes];
            }
        );
        GC.KeepAlive(_sink);

        var gc = sample.GcInfo;
        Batch0Support.Log(
            $"async: allocated={Describe(gc?.AllocatedBytes)} approximate={gc?.AllocatedBytesAreApproximate}"
        );

        await Assert.That(gc).IsNotNull();
        await Assert.That(gc!.AllocatedBytesAreApproximate).IsTrue();
        await Assert.That(gc.IsApproximate).IsTrue();
        await Assert
            .That(gc.AllocatedBytes)
            .IsNotNull()
            .Because("the process-wide counter must report the async path's allocations");
    }

    /// <summary>
    /// "Not collected" is <c>N/A</c> in all four formats and is <b>never</b>
    /// written as <c>0</c>. The CSV cell is the machine-readable one, where a
    /// fabricated <c>0</c> is most dangerous, so all four are checked.
    /// </summary>
    [Test]
    [Property("Category", "GcBytes")]
    [Property("T3", "not collected")]
    public async Task NotCollectedAllocation_IsNAInAllFourFormats_NeverZero()
    {
        var result = ResultWithAllocation(null);
        var csv = new CsvFormatter().Format(result);
        var cell = Cell(csv, "AllocBytes");
        Batch0Support.Log($"csv AllocBytes (not collected) = '{cell}'");

        await Assert.That(cell).IsEqualTo("N/A");
        await Assert.That(cell).IsNotEqualTo("0");

        var console = new ConsoleFormatter().Format(result);
        var markdown = new MarkdownFormatter().Format(result);
        var html = new HtmlFormatter().Format(result);

        foreach (var (format, text) in new (string, string)[]
        {
            ("console", console),
            ("markdown", markdown),
            ("html", html),
        })
        {
            await Assert
                .That(text)
                .Contains("N/A")
                .Because($"{format} must disclose the missing allocation as N/A");
        }
    }

    /// <summary>
    /// The counterpart of the negative control: a run that really allocated
    /// nothing is a <b>measured</b> zero and reads <c>0</c> everywhere.
    /// </summary>
    [Test]
    [Property("Category", "GcBytes")]
    [Property("T3", "measured zero")]
    public async Task MeasuredZeroAllocation_IsZeroInAllFourFormats()
    {
        var result = ResultWithAllocation(0L);
        var csv = new CsvFormatter().Format(result);
        Batch0Support.Log("csv AllocBytes (measured zero) = '" + Cell(csv, "AllocBytes") + "'");

        await Assert.That(Cell(csv, "AllocBytes")).IsEqualTo("0");

        var console = new ConsoleFormatter().Format(result);
        var markdown = new MarkdownFormatter().Format(result);
        var html = new HtmlFormatter().Format(result);

        foreach (var (format, text) in new (string, string)[]
        {
            ("console", console),
            ("markdown", markdown),
            ("html", html),
        })
        {
            await Assert
                .That(text)
                .Contains("0 B")
                .Because($"{format} must print a measured zero, not N/A");
        }
    }

    /// <summary>
    /// An approximate allocation carries the <c>~</c> marker in every format, so
    /// a reader cannot mistake a process-wide number for the benchmark's own —
    /// the same rule <c>CpuCycles</c> follows.
    /// </summary>
    [Test]
    [Property("Category", "GcBytes")]
    [Property("T3", "approximate marker")]
    public async Task ApproximateAllocation_CarriesTheTildeMarker()
    {
        var result = ResultWithAllocation(2_048L, approximate: true);
        var csv = new CsvFormatter().Format(result);
        var cell = Cell(csv, "AllocBytes");
        Batch0Support.Log($"csv AllocBytes (approximate) = '{cell}'");

        await Assert.That(cell).IsEqualTo("~2048");

        var console = new ConsoleFormatter().Format(result);
        var markdown = new MarkdownFormatter().Format(result);
        var html = new HtmlFormatter().Format(result);

        foreach (var (format, text) in new (string, string)[]
        {
            ("console", console),
            ("markdown", markdown),
            ("html", html),
        })
        {
            await Assert
                .That(text)
                .Contains("~2.0 KB")
                .Because($"{format} must mark the process-wide allocation with ~");
        }
    }

    /// <summary>
    /// The file describes itself: the new column is in the <c>columns=</c> list
    /// (which is derived from the header that is really written) and its cell
    /// shapes are stated, exactly like the GC counters'.
    /// </summary>
    [Test]
    [Property("Category", "GcBytes")]
    [Property("T3", "csv schema")]
    public async Task CsvSchema_DeclaresTheAllocationColumn()
    {
        var csv = new CsvFormatter().Format(ResultWithAllocation(1_024L));
        var schema = CsvSchemaLines(csv);

        foreach (var line in schema)
            Batch0Support.Log("schema: " + line);

        await Assert.That(schema).IsNotEmpty();
        foreach (var line in schema)
        {
            await Assert.That(line).Contains("AllocBytes");
            await Assert
                .That(line)
                .Contains(
                    "AllocBytes = bytes allocated | \"~bytes\" (approximate: process-wide counter)"
                        + " | \"N/A\" (not collected)"
                )
                .Because("the column's three cell shapes must be written down");
        }
    }

    /// <summary>
    /// The aggregation preserves the tri-state: bytes are summed across samples,
    /// and one sample that could not be read makes the run's total <c>N/A</c>
    /// rather than a partial sum that would understate the allocation.
    /// </summary>
    [Test]
    [Property("Category", "GcBytes")]
    [Property("T3", "aggregation")]
    public async Task Statistics_AggregatesBytes_AndIsNotCollectedWhenASampleWasNot()
    {
        var complete = StatisticsCalculator.Compute(
            [1.0, 1.0],
            [0.0, 0.0],
            [SampleWithAllocation(1_000L), SampleWithAllocation(2_000L)]
        );
        Batch0Support.Log($"aggregate complete: allocated={Describe(complete.GcInfo?.AllocatedBytes)}");
        await Assert.That(complete.GcInfo!.AllocatedBytes).IsEqualTo(3_000L);

        var partial = StatisticsCalculator.Compute(
            [1.0, 1.0],
            [0.0, 0.0],
            [SampleWithAllocation(1_000L), SampleWithAllocation(null)]
        );
        Batch0Support.Log($"aggregate partial: allocated={Describe(partial.GcInfo?.AllocatedBytes)}");
        await Assert
            .That(partial.GcInfo!.AllocatedBytes)
            .IsNull()
            .Because("a partial sum is not the run's allocation and must read N/A");

        var approximate = StatisticsCalculator.Compute(
            [1.0, 1.0],
            [0.0, 0.0],
            [SampleWithAllocation(1_000L, approximate: true), SampleWithAllocation(1_000L)]
        );
        await Assert.That(approximate.GcInfo!.AllocatedBytesAreApproximate).IsTrue();
        await Assert.That(approximate.GcInfo.AllocatedBytes).IsEqualTo(2_000L);
    }

    /// <summary>
    /// The interleaved-comparison merge sums the per-round allocations and keeps
    /// the approximate bit, like every other merged GC field.
    /// </summary>
    [Test]
    [Property("Category", "GcBytes")]
    [Property("T3", "comparison merge")]
    public async Task ComparisonMerge_SumsAllocatedBytes()
    {
        var merged = ComparisonMeasurement.Merge(
            [
                StatisticsWithAllocation(1_000L, approximate: false),
                StatisticsWithAllocation(2_000L, approximate: true),
            ]
        );

        Batch0Support.Log(
            $"merged: allocated={Describe(merged.GcInfo?.AllocatedBytes)} approximate={merged.GcInfo?.AllocatedBytesAreApproximate}"
        );

        await Assert.That(merged.GcInfo!.AllocatedBytes).IsEqualTo(3_000L);
        await Assert.That(merged.GcInfo.AllocatedBytesAreApproximate).IsTrue();
    }

    /// <summary>
    /// The GC counters keep their existing contract: the format of
    /// <see cref="GcInfo.ToString"/> is unchanged and the counting columns still
    /// read <c>gen0/gen1/gen2</c>.
    /// </summary>
    [Test]
    [Property("Category", "GcBytes")]
    [Property("T3", "no regression")]
    public async Task GcCounters_KeepTheirExistingFormat()
    {
        var gc = new GcInfo
        {
            Gen0 = 3,
            Gen1 = 2,
            Gen2 = 1,
            AllocatedBytes = 4_096L,
        };

        Batch0Support.Log($"GcInfo.ToString() = '{gc}'");
        await Assert.That(gc.ToString()).IsEqualTo("3/2/1");
        await Assert.That(gc.Total).IsEqualTo(6);
        await Assert.That(gc.IsZero).IsFalse();
    }

    /// <summary>
    /// The ten READMEs are the consumer-facing contract and are hand-maintained,
    /// so the new column has to appear in every one of them (the same drift guard
    /// the Speedup clause already has).
    /// </summary>
    [Test]
    [Property("Category", "GcBytes")]
    [Property("T3", "readme drift")]
    public async Task EveryReadme_DocumentsTheAllocationColumn()
    {
        var root = Path.GetDirectoryName(Batch0Support.FindRepositoryFile("PicoBench.slnx"))!;
        var missing = new List<string>();

        foreach (var file in ReadmeFiles)
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            var present = text.Contains("AllocBytes", StringComparison.Ordinal);
            Batch0Support.Log($"{file}: AllocBytes present = {present}");
            if (!present)
                missing.Add(file);
        }

        await Assert
            .That(missing)
            .IsEmpty()
            .Because("these READMEs do not document the AllocBytes column: " + string.Join(", ", missing));
    }

    // ─── Fixtures ──────────────────────────────────────────────────

    private static string Describe(long? bytes) =>
        bytes?.ToString(CultureInfo.InvariantCulture) ?? "N/A";

    private static readonly string[] ReadmeFiles =
    [
        "README.md",
        "README.zh.md",
        "README.zh-tw.md",
        "README.ja.md",
        "README.ko.md",
        "README.de.md",
        "README.es.md",
        "README.fr.md",
        "README.pt.md",
        "README.ru.md",
    ];

    private static BenchmarkResult ResultWithAllocation(long? bytes, bool approximate = false) =>
        BenchmarkResultFactory.Create(
            name: "alloc-fixture",
            statistics: StatisticsWithAllocation(bytes, approximate)
        );

    private static Statistics StatisticsWithAllocation(long? bytes, bool approximate = false) =>
        new()
        {
            Avg = 100.0,
            P50 = 100.0,
            P90 = 100.0,
            P95 = 100.0,
            P99 = 100.0,
            Min = 100.0,
            Max = 100.0,
            StdDev = 1.0,
            StandardError = 0.5,
            RelativeStdDevPercent = 1.0,
            CpuCyclesPerOp = 42.0,
            GcInfo = new GcInfo
            {
                Gen0 = 1,
                Gen1 = 0,
                Gen2 = 0,
                AllocatedBytes = bytes,
                AllocatedBytesAreApproximate = approximate,
            },
        };

    private static TimingSample SampleWithAllocation(long? bytes, bool approximate = false) =>
        new()
        {
            ElapsedNanoseconds = 1_000.0,
            ElapsedMilliseconds = 0.0001,
            ElapsedTicks = 10,
            GcInfo = new GcInfo
            {
                Gen0 = 1,
                AllocatedBytes = bytes,
                AllocatedBytesAreApproximate = approximate,
            },
        };

    /// <summary>The <c>AllocBytes</c> cell of the results section, located by column name.</summary>
    private static string Cell(string csv, string column) =>
        Batch0Support.ReadCsvCell(csv, "alloc-fixture", column);

    /// <summary>Every <c># Schema:</c> line of the document.</summary>
    private static List<string> CsvSchemaLines(string csv) =>
        csv.Replace("\r\n", "\n")
            .Split('\n')
            .Where(static line => line.StartsWith("# Schema:", StringComparison.Ordinal))
            .ToList();
}
