namespace Pico.Bench;

/// <summary>
/// Computes statistical metrics from timing samples.
/// </summary>
internal static class StatisticsCalculator
{
    /// <summary>
    /// Compute statistics from timing data.
    /// </summary>
    public static Statistics Compute(
        double[] perOpTimes,
        double[] perOpCycles,
        TimingSample[] samples
    )
    {
        // Sort for percentile calculation
        var sorted = (double[])perOpTimes.Clone();
        Array.Sort(sorted);

        // Aggregate GC info
        int gen0 = 0,
            gen1 = 0,
            gen2 = 0;
        foreach (var sample in samples)
        {
            gen0 += sample.GcInfo.Gen0;
            gen1 += sample.GcInfo.Gen1;
            gen2 += sample.GcInfo.Gen2;
        }

        // Optimized statistics calculation
        var sum = 0.0;
        var min = double.MaxValue;
        var max = double.MinValue;

        for (int i = 0; i < perOpTimes.Length; i++)
        {
            var value = perOpTimes[i];
            sum += value;
            if (value < min)
                min = value;
            if (value > max)
                max = value;
        }

        var avg = sum / perOpTimes.Length;

        // Welford's online algorithm for variance (more stable)
        var variance = 0.0;
        if (perOpTimes.Length > 1)
        {
            var m2 = 0.0;
            for (int i = 0; i < perOpTimes.Length; i++)
            {
                var delta = perOpTimes[i] - avg;
                m2 += delta * delta;
            }
            variance = m2 / perOpTimes.Length;
        }
        var stdDev = Math.Sqrt(variance);

        // Calculate CPU cycles average
        var cpuCyclesSum = 0.0;
        for (int i = 0; i < perOpCycles.Length; i++)
        {
            cpuCyclesSum += perOpCycles[i];
        }
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
            CpuCyclesPerOp = cpuCyclesAvg,
            GcInfo = new GcInfo
            {
                Gen0 = gen0,
                Gen1 = gen1,
                Gen2 = gen2
            }
        };
    }

    private static double GetPercentile(double[] sortedData, int percentile)
    {
        var index = (int)Math.Ceiling(percentile / 100.0 * sortedData.Length) - 1;
        // Manual clamping for netstandard2.0 compatibility
        if (index < 0)
            index = 0;
        if (index >= sortedData.Length)
            index = sortedData.Length - 1;
        return sortedData[index];
    }
}
