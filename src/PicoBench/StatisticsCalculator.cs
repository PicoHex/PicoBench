namespace PicoBench;

/// <summary>
/// Computes statistical metrics from timing samples.
/// </summary>
internal static class StatisticsCalculator
{
    /// <summary>
    /// Compute statistics from timing data.
    /// </summary>
    /// <param name="perOpTimes">Per-operation nanoseconds, with the harness overhead already deducted.</param>
    /// <param name="perOpCycles">Per-operation CPU cycles.</param>
    /// <param name="samples">The raw samples the per-operation values were derived from.</param>
    /// <param name="harnessOverheadNanoseconds">Per-operation harness overhead that was deducted.</param>
    /// <param name="resolutionLimitNanoseconds">
    /// Smallest per-operation value this run can resolve; 0 when unknown.
    /// </param>
    /// <remarks>
    /// The percentiles this publishes (<c>P50</c>, <c>P90</c>, <c>P95</c>, <c>P99</c>)
    /// use linear interpolation between the two nearest order statistics (type 7;
    /// see <see cref="GetPercentile"/>). The spread is the <b>sample</b> standard
    /// deviation (Bessel's correction, <c>N-1</c>), and the relative standard
    /// deviation is that deviation over <c>|mean|</c>, kept at 0 when the mean is
    /// below <c>1e-12</c> rather than dividing by zero.
    /// </remarks>
    public static Statistics Compute(
        double[] perOpTimes,
        double[] perOpCycles,
        TimingSample[] samples,
        double harnessOverheadNanoseconds = 0,
        double resolutionLimitNanoseconds = 0
    )
    {
        // Sort for percentile calculation
        var sorted = (double[])perOpTimes.Clone();
        Array.Sort(sorted);

        // Aggregate GC info (skip samples with null GcInfo)
        int gen0Sum = 0,
            gen1Sum = 0,
            gen2Sum = 0;
        bool anyApproximate = false;
        int gcInfoCount = 0;

        // A sample whose timing source did not advance carries no measurement:
        // CpuOnly reads Process.TotalProcessorTime, which only moves in whole
        // timer ticks, so every sub-tick workload reads as exactly zero.
        var unmeasuredCount = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            if (!(samples[i].ElapsedNanoseconds > 0))
                unmeasuredCount++;

            var gc = samples[i].GcInfo;
            if (gc == null)
                continue;
            gen0Sum += gc.Gen0;
            gen1Sum += gc.Gen1;
            gen2Sum += gc.Gen2;
            if (gc.IsApproximate)
                anyApproximate = true;
            gcInfoCount++;
        }

        GcInfo? aggregatedGcInfo = null;
        if (gcInfoCount > 0)
        {
            aggregatedGcInfo = new GcInfo
            {
                Gen0 = gen0Sum,
                Gen1 = gen1Sum,
                Gen2 = gen2Sum,
                IsApproximate = anyApproximate,
            };
        }

        // Optimized statistics calculation
        var sum = 0.0;
        var min = double.MaxValue;
        var max = double.MinValue;

        foreach (var value in perOpTimes)
        {
            sum += value;
            if (value < min)
                min = value;
            if (value > max)
                max = value;
        }

        var avg = sum / perOpTimes.Length;

        // Two-pass variance calculation: compute mean first, then sum of squared deviations.
        // Uses Bessel's correction (N-1) for unbiased sample variance.
        var variance = 0.0;
        if (perOpTimes.Length > 1)
        {
            double m2 = 0;
            for (int i = 0; i < perOpTimes.Length; i++)
            {
                var delta = perOpTimes[i] - avg;
                m2 += delta * delta;
            }
            variance = m2 / (perOpTimes.Length - 1);
        }
        var stdDev = Math.Sqrt(Math.Max(0, variance));
        var standardError = perOpTimes.Length > 0 ? stdDev / Math.Sqrt(perOpTimes.Length) : 0.0;
        var relativeStdDevPercent = Math.Abs(avg) < 1e-12 ? 0.0 : (stdDev / Math.Abs(avg)) * 100.0;

        // Calculate CPU cycles average
        double cpuCyclesSum = 0;
        for (int i = 0; i < perOpCycles.Length; i++)
            cpuCyclesSum += perOpCycles[i];
        var cpuCyclesAvg = cpuCyclesSum / perOpCycles.Length;

        return new Statistics
        {
            Avg = avg,
            P50 = GetPercentile(sorted, 50),
            P90 = GetPercentile(sorted, 90),
            P95 = GetPercentile(sorted, 95),
            P99 = GetPercentile(sorted, 99),
            Min = min,
            Max = max,
            StdDev = stdDev,
            StandardError = standardError,
            RelativeStdDevPercent = relativeStdDevPercent,
            CpuCyclesPerOp = cpuCyclesAvg,
            CpuCyclesAreApproximate = Array.Exists(samples, static s => s.CpuCyclesAreApproximate),
            OverheadNs = harnessOverheadNanoseconds,
            ResolutionLimitNanoseconds = resolutionLimitNanoseconds,
            ResolutionLimited =
                unmeasuredCount > 0
                || (resolutionLimitNanoseconds > 0 && avg <= resolutionLimitNanoseconds),
            UnmeasuredSampleCount = unmeasuredCount,
            GcInfo = aggregatedGcInfo,
        };
    }

    /// <summary>
    /// One percentile of a sorted sample, by <b>linear interpolation between the two
    /// nearest order statistics</b> — the method statistics packages call <b>type 7</b>
    /// (Excel <c>PERCENTILE.INC</c>, NumPy's default, R's <c>quantile(type = 7)</c>).
    /// <para>
    /// The position is <c>p/100 * (N - 1)</c> and the value is
    /// <c>x[floor] * (1 - w) + x[floor + 1] * w</c> with <c>w</c> the fractional part.
    /// Worked examples, which are the values <c>[1,2,3,4]</c> and <c>[1,3]</c> produce
    /// (and which the tests pin, so a change of method cannot pass silently):
    /// </para>
    /// <list type="bullet">
    /// <item><c>[1,2,3,4]</c>: P50 = 2.5 (position 1.5), P90 = 3.7 (2.7), P95 = 3.85
    /// (2.85), P99 = 3.97 (2.97).</item>
    /// <item><c>[1,3]</c>: P50 = 2.0 (position 0.5), P90 = 2.8 (0.9).</item>
    /// </list>
    /// <para>
    /// Consequence worth knowing at small N: with ten samples (the
    /// <c>BenchmarkConfig.Quick</c> preset) P95 and P99 sit within a few percent of
    /// <see cref="Statistics.Max"/> — <c>1..10</c> gives P99 = 9.91 — so below about
    /// twenty samples the tail columns say "the tail is the maximum", not "the tail is
    /// stable". N = 0 yields 0 and N = 1 yields the single value (the library must not
    /// return NaN for a degenerate run).
    /// </para>
    /// </summary>
    private static double GetPercentile(double[] sortedData, int percentile)

    {
        switch (sortedData.Length)
        {
            case 0:
                return 0;
            case 1:
                return sortedData[0];
        }

        // Type-7 linear interpolation; see the doc comment above for the worked values.
        var position = (percentile / 100.0) * (sortedData.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = lower + 1;

        if (upper >= sortedData.Length)
            return sortedData[lower];

        var weight = position - lower;
        return sortedData[lower] * (1 - weight) + sortedData[upper] * weight;
    }
}
