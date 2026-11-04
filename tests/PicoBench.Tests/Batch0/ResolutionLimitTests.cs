namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · G6 — the disclosed per-operation limit must be a real <b>upper
/// bound</b>, not just the clock quantisation.
/// <para>
/// G4 · B2: <c>Benchmark.PerOpNanoseconds</c> clamps the residual at zero, while
/// <c>ResolutionLimitNanoseconds</c> only carried the clock tick and the spread of
/// the five empty-delegate probes — never the uncertainty of the amount that was
/// <b>subtracted</b>. A run could therefore print <c>&lt;0.02 ns</c> for a
/// workload whose true cost is 0.3 ns/op, i.e. the published bound was smaller
/// than the harness floor the same run had just deducted. G4 measured exactly
/// that on the empty lambda (<c>Avg_ns = '&lt;0.02'</c>).
/// </para>
/// </summary>
public class ResolutionLimitTests
{
    /// <summary>
    /// G4 · B2 — an empty lambda is not resolvable, so it is disclosed as
    /// <c>&lt;limit</c>; that limit must be at least the per-operation harness cost
    /// the run deducted, which is measured independently of the limit formula
    /// (<see cref="Statistics.OverheadNs"/> / <c>IterationsPerSample</c>).
    /// <para>
    /// 50 000 iterations per sample put the overhead's <b>spread</b> far below the
    /// per-operation harness cost: the spread shrinks with the sample length while
    /// the per-op harness cost does not, which makes the gap between "what was
    /// deducted" and "what was published as the bound" visible instead of a
    /// coincidence of one run's noise.
    /// </para>
    /// <para>Expected before the fix: <b>RED</b> — the CSV cell reads
    /// <c>&lt;0.0xx</c> (or a bare number), both smaller than the ≈0.6 ns/op floor
    /// the run deducted.</para>
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.1/B2")]
    [NotInParallel] // timing test: keep the core to ourselves
    public async Task EmptyLambda_DisclosedLimit_CoversTheDeductedFloor()
    {
        Action empty = static () => { };

        // 5 000 keeps the harness floor visible per operation (the probe path is not
        // folded away, so the amount that is deducted is ≈2.7 ns/op — the case G4
        // measured as "<0.02 ns" for a true 0.3 ns/op); 50 000 shrinks the probe
        // *spread* below the floor, so a limit built only from the spread collapses
        // to the clock tick and the empty lambda is not disclosed at all.
        foreach (var iterationsPerSample in new[] { 5_000, 50_000 })
        {
            var config = Batch0Support.UncalibratedConfig(
                sampleCount: 10,
                iterationsPerSample: iterationsPerSample
            );

            var result = Benchmark.Run("empty-lambda", empty, warmup: null, config);
            var statistics = result.Statistics;

            var perOpFloor = statistics.OverheadNs / result.IterationsPerSample;
            var clockTickPerOp = 1_000_000_000.0 / Stopwatch.Frequency / result.IterationsPerSample;

            var cell = ReadCsvPerOpCell(new CsvFormatter().Format(result), result.Name);
            Batch0Support.Log(
                $"iters={result.IterationsPerSample} avg={statistics.Avg:F4} ns/op "
                    + $"overhead={statistics.OverheadNs:F1} ns/sample => {perOpFloor:F4} ns/op; "
                    + $"limit={statistics.ResolutionLimitNanoseconds:F4} ns/op; "
                    + $"resolutionLimited={statistics.ResolutionLimited}; clock tick={clockTickPerOp:F5} ns/op"
            );
            Batch0Support.Log($"iters={result.IterationsPerSample} csv Avg_ns = '{cell}'");

            // The empty lambda cannot be resolved: whatever the run publishes has to
            // be an upper bound, not a number that looks like a measurement.
            await Assert.That(cell.StartsWith("<", StringComparison.Ordinal)).IsTrue();
            await Assert.That(statistics.ResolutionLimited).IsTrue();

            var disclosed = double.Parse(cell[1..], CultureInfo.InvariantCulture);

            // The claim "this operation costs less than `disclosed`" is only true if
            // the bound also covers the harness floor the run deducted — that floor
            // is a lower bound of what one call costs as this run measures it, and
            // any sample satisfies true ≤ residual + overhead (overhead ≥ 0), so a
            // limit below the floor can never be an upper bound.
            await Assert.That(disclosed).IsGreaterThanOrEqualTo(perOpFloor);
            await Assert.That(disclosed).IsGreaterThan(clockTickPerOp);
            await Assert.That(disclosed).IsGreaterThan(0.0);
        }
    }

    /// <summary>Returns the <c>Avg_ns</c> cell of the CSV row whose name matches.</summary>
    private static string ReadCsvPerOpCell(string csv, string resultName)
    {
        var lines = csv.Replace("\r\n", "\n").Split('\n');
        var headerIndex = Array.FindIndex(
            lines,
            static line => line.StartsWith("Name,", StringComparison.Ordinal)
        );
        if (headerIndex < 0)
            throw new InvalidOperationException("the CSV output has no 'Name,' header row");

        var header = lines[headerIndex].Split(',');
        var column = Array.FindIndex(
            header,
            static entry => entry.Equals("Avg_ns", StringComparison.OrdinalIgnoreCase)
        );
        if (column < 0)
            throw new InvalidOperationException("the CSV header has no Avg_ns column");

        for (var index = headerIndex + 1; index < lines.Length; index++)
        {
            var cells = lines[index].Split(',');
            if (
                cells.Length > column
                && cells[0].Trim('"', '\'').Equals(resultName, StringComparison.Ordinal)
            )
            {
                return cells[column].Trim('"', '\'');
            }
        }

        throw new InvalidOperationException($"the CSV output has no row named {resultName}");
    }
}
