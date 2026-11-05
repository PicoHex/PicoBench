namespace PicoBench.Tests.Batch2;

/// <summary>
/// Batch 2 · 2.3 (B2-M3) — the CSV output must be a legal CSV file, and the
/// comparisons section must carry enough columns to trace a ratio back to the
/// measurement it came from.
/// </summary>
/// <remarks>
/// The defect: the suite metadata was written as <c># Key: value</c> with the
/// value escaped as a quoted CSV field, so a description containing a line break
/// produced a <em>continuation line that did not start with '#'</em> — the one
/// thing a <c>comment='#'</c> reader cannot survive. The file is now
/// <c># key,value</c> rows with line breaks written as the two characters
/// <c>\n</c>, so every comment is exactly one physical line.
/// </remarks>
public class CsvLegalityTests
{
    private const string MultilineDescription =
        "line1\nline2, with a comma and a \"quote\"\nand a third line";

    private static BenchmarkSuite SuiteWithMultilineDescription() =>
        Batch2Support.RichSuite(MultilineDescription);

    /// <summary>
    /// Acceptance of B2-M3: with a description that contains line breaks, the
    /// comment-free remainder of the file parses as CSV — every record of a
    /// section has the same number of fields as that section's header.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.3/csv legality")]
    public async Task MultilineDescription_LeavesAParseableFile()
    {
        var csv = new CsvFormatter().Format(SuiteWithMultilineDescription());
        var (resultsSection, comparisonsSection) = Batch2Support.SplitSections(csv);

        await AssertSectionsParseAsync("results", resultsSection, expectedFirstColumn: "Name");
        await AssertSectionsParseAsync(
            "comparisons",
            comparisonsSection,
            expectedFirstColumn: "TestCase"
        );

        // The parser above is the equivalent of pandas.read_csv(comment='#') only
        // if the comment scan really removes the metadata block: no line outside a
        // quoted field may start with something other than '#' before the header.
        var commentFree = Batch2Support.StripCommentLines(csv);
        var records = Batch2Support.ParseCsv(commentFree);

        Batch2Support.Log("comment-free results header: " + Batch2Support.Describe(records[0]));
        Batch2Support.Log("comment-free record count: " + records.Count);

        await Assert.That(records[0][0]).IsEqualTo("Name");
    }

    /// <summary>
    /// The description itself must survive as one physical line whose line breaks
    /// are the two characters <c>\n</c> — that is what makes the comment block
    /// line-per-comment.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.3/csv legality")]
    public async Task MultilineDescription_BecomesOneMetadataRow_WithLiteralEscapes()
    {
        var csv = new CsvFormatter().Format(SuiteWithMultilineDescription());
        var lines = Batch2Support.Lines(csv);

        var descriptionRows = lines
            .Where(line => line.StartsWith("# Description", StringComparison.Ordinal))
            .ToList();

        Batch2Support.Log("description rows: " + Batch2Support.Describe(descriptionRows));

        await Assert.That(descriptionRows.Count).IsEqualTo(1);

        var row = descriptionRows[0];

        // "# Description,<value>" — a two column metadata row.
        await Assert.That(row.StartsWith("# Description,", StringComparison.Ordinal)).IsTrue();

        // The two line breaks of the fixture are literal, not physical.
        await Assert.That(row.Contains('\r')).IsFalse();
        await Assert.That(row.Split("\\n").Length).IsEqualTo(3);

        // And the value is still a readable CSV field that carries the text.
        var fields = Batch2Support.ParseCsv(row["# ".Length..])[0];
        await Assert.That(fields.Count).IsEqualTo(2);
        await Assert.That(fields[0]).IsEqualTo("Description");
        await Assert.That(fields[1]).Contains("line1");
        await Assert.That(fields[1]).Contains("with a comma");
        await Assert.That(fields[1]).Contains("\"quote\"");
    }

    /// <summary>
    /// Every metadata row is <c># key,value</c>: a consumer can split the comment
    /// block on the first comma instead of guessing at a colon.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.3/csv legality")]
    public async Task MetadataBlock_IsAKeyValueTable()
    {
        var csv = new CsvFormatter().Format(Batch2Support.RichSuite());
        var lines = Batch2Support.Lines(csv);

        var keys = new List<string>();
        foreach (var line in lines)
        {
            if (!line.StartsWith('#'))
                continue;

            // Section markers and the contract line are single-token lines.
            if (
                line is "# Results" or "# Comparisons"
                || line.StartsWith("# Schema:", StringComparison.Ordinal)
            )
                continue;

            var fields = Batch2Support.ParseCsv(line["# ".Length..]);
            await Assert.That(fields.Count).IsEqualTo(1).Because($"'{line}' is not one row");
            await Assert
                .That(fields[0].Count)
                .IsEqualTo(2)
                .Because($"'{line}' is not a '# key,value' row");

            keys.Add(fields[0][0]);
        }

        Batch2Support.Log("metadata keys: " + Batch2Support.Describe(keys));

        foreach (
            var expected in new[]
            {
                "Suite",
                "Description",
                "Environment",
                "Timestamp",
                "Duration",
                "NoiseNote",
            }
        )
            await Assert.That(keys).Contains(expected);

        // The old "# Key: value" shape would have turned the key into "Suite:".
        await Assert.That(keys).DoesNotContain("Suite:");
    }

    /// <summary>
    /// B2-M3, second half: a comparison row must carry the iteration counts of
    /// the run it describes, so a reader can normalise or reproduce the ratio.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.3/csv legality")]
    public async Task ComparisonsSection_CarriesSampleCountAndIterationsPerSample()
    {
        var suite = Batch2Support.RichSuite();
        var csv = new CsvFormatter().Format(suite);
        var (_, comparisonsSection) = Batch2Support.SplitSections(csv);
        var records = Batch2Support.ParseCsv(Batch2Support.StripCommentLines(comparisonsSection));

        var header = records[0];
        Batch2Support.Log("comparisons header: " + Batch2Support.Describe(header));

        var sampleColumn = header.IndexOf("SampleCount");
        var iterationColumn = header.IndexOf("IterationsPerSample");

        await Assert.That(sampleColumn).IsGreaterThan(-1);
        await Assert.That(iterationColumn).IsGreaterThan(-1);

        var comparison = suite.Comparisons![0];

        // Candidate row first, then the baseline row (documented order).
        var candidateRow = records[1];
        var baselineRow = records[2];

        await Assert.That(candidateRow.Count).IsEqualTo(header.Count);
        await Assert.That(baselineRow.Count).IsEqualTo(header.Count);

        await Assert
            .That(candidateRow[sampleColumn])
            .IsEqualTo(comparison.Candidate.SampleCount.ToString(CultureInfo.InvariantCulture));
        await Assert
            .That(candidateRow[iterationColumn])
            .IsEqualTo(
                comparison.Candidate.IterationsPerSample.ToString(CultureInfo.InvariantCulture)
            );
        await Assert
            .That(baselineRow[sampleColumn])
            .IsEqualTo(comparison.Baseline.SampleCount.ToString(CultureInfo.InvariantCulture));
        await Assert
            .That(baselineRow[iterationColumn])
            .IsEqualTo(
                comparison.Baseline.IterationsPerSample.ToString(CultureInfo.InvariantCulture)
            );

        // The comparisons contract line must name the columns it now writes.
        var schemaLine = Batch2Support
            .Lines(comparisonsSection)
            .First(line => line.StartsWith("# Schema:", StringComparison.Ordinal));
        await Assert
            .That(schemaLine)
            .Contains("columns=" + string.Join("|", header));
    }

    /// <summary>
    /// A results-only document (the shape the README's own example writes) must
    /// parse end to end after the comments are dropped, even when the suite
    /// description contains line breaks.
    /// </summary>
    [Test]
    [Property("Category", "Batch2")]
    [Property("Batch2", "2.3/csv legality")]
    public async Task ResultsOnlyDocument_ParsesEndToEnd()
    {
        var resultsOnly = new BenchmarkSuite(
            name: "ResultsOnly",
            environment: new EnvironmentInfo(),
            results: Batch2Support.RichSuite().Results,
            duration: Batch2Support.FixedDuration,
            description: MultilineDescription,
            comparisons: null,
            timestamp: Batch2Support.FixedTimestamp
        );

        var csv = new CsvFormatter().Format(resultsOnly);
        var records = Batch2Support.ParseCsv(Batch2Support.StripCommentLines(csv));

        await Assert.That(records.Count).IsEqualTo(resultsOnly.Results.Count + 1);
        foreach (var record in records)
            await Assert.That(record.Count).IsEqualTo(records[0].Count);

        await Assert.That(records[0][0]).IsEqualTo("Name");
    }

    private static async Task AssertSectionsParseAsync(
        string section,
        string text,
        string expectedFirstColumn
    )
    {
        await Assert.That(text.Length).IsGreaterThan(0).Because($"no {section} section");

        var records = Batch2Support.ParseCsv(Batch2Support.StripCommentLines(text));
        Batch2Support.Log($"{section}: {records.Count} record(s), {records[0].Count} field(s)");

        await Assert.That(records[0][0]).IsEqualTo(expectedFirstColumn);

        foreach (var record in records)
        {
            await Assert
                .That(record.Count)
                .IsEqualTo(records[0].Count)
                .Because($"{section} section has a ragged record: {Batch2Support.Describe(record)}");
        }
    }
}
