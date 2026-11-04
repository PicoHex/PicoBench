namespace PicoBench;

/// <summary>
/// The measurement side of a direct comparison: interleaved rounds, paired
/// ratios, a median point estimate and a distribution-free interval.
/// <para>
/// A comparison used to run the baseline once and the candidate once, back to
/// back, and report the ratio of two arithmetic means. That turns drift into a
/// conclusion: the candidate always ran in a warmed-up process, a single GC pause
/// decided the verdict, and comparing a delegate with itself claimed a speedup in
/// 5-8 of 8 runs (Batch 0 · 0.2, B1-C1). Here both lanes are measured in
/// round-robin order (the first lane alternates every round, so neither side is
/// systematically warm or cold), the verdict comes from the <b>median of the
/// per-round paired ratios</b>, and it is only reported as a difference when the
/// ratio is outside a 5% dead band <b>and</b> the interval excludes parity.
/// </para>
/// </summary>
internal static class ComparisonMeasurement
{
    /// <summary>
    /// Interleaved rounds of each lane. An even count makes the ordering exactly
    /// balanced — half of the rounds run the baseline first and half run the
    /// candidate first — so neither lane can inherit a warmer process. Ten rounds
    /// also make the sign-test interval drop the outermost pair (the 2nd and 9th
    /// order statistics cover the median with 97.9% confidence), so one stray round
    /// can neither widen the interval nor invent a difference.
    /// </summary>
    internal const int Rounds = 10;

    /// <summary>
    /// Relative distance from parity a ratio must exceed to count as a
    /// difference: 5%, the resolution the noise of one interleaved round allows.
    /// </summary>
    internal const double DeadBand = 0.05;

    /// <summary>
    /// Smallest number of usable rounds whose sign-test interval still reaches the
    /// 95% confidence it claims.
    /// <para>
    /// The interval is <c>[x_(k), x_(n+1-k)]</c> with <c>k = 1</c> for every count
    /// this small, so its coverage is <c>1 - 2·P(Bin(n, ½) ≤ 0) = 1 - 2^(1-n)</c>:
    /// n = 3/4/5 give 75/87.5/93.75% — <b>below the 95% the interval advertises</b> —
    /// and n = 6 is the first count that reaches it (96.875%). Fewer usable rounds
    /// therefore produce no interval <b>and no verdict</b> (G4 · B1), which also
    /// removes the n ≤ 2 degenerate case where <c>[r, r]</c> let a single surviving
    /// ratio of 2.0 read as a difference with a zero-width interval.
    /// </para>
    /// <para>
    /// Reachable in practice: <c>Benchmark.PerOpNanoseconds</c> clamps the residual
    /// at zero, so a sub-floor workload produces rounds whose candidate average is
    /// exactly 0, <see cref="Ratio"/> returns <see cref="double.NaN"/> for them and
    /// they are dropped.
    /// </para>
    /// </summary>
    internal const int MinimumUsableRounds = 6;

    /// <summary>
    /// Median of the paired per-round ratios plus the distribution-free 95%
    /// interval of that median. Ratios that are not finite and positive (a lane
    /// that measured nothing) are dropped; <see cref="double.NaN"/> and no
    /// interval are returned when fewer than <see cref="MinimumUsableRounds"/>
    /// rounds produced a usable ratio, i.e. when the sign test cannot reach 95%.
    /// </summary>
    internal static (double Speedup, ComparisonInterval? Interval) PairedSpeedup(
        IReadOnlyList<double> perRoundRatios
    )
    {
        var usable = new List<double>(perRoundRatios.Count);
        foreach (var ratio in perRoundRatios)
        {
            if (!double.IsNaN(ratio) && !double.IsInfinity(ratio) && ratio > 0)
                usable.Add(ratio);
        }

        if (usable.Count < MinimumUsableRounds)
            return (double.NaN, null);

        usable.Sort();
        var median = Median(usable);
        var rank = MedianConfidenceRank(usable.Count);
        return (median, new ComparisonInterval(usable[rank - 1], usable[usable.Count - rank]));
    }

    /// <summary>Ratio of one interleaved round; <see cref="double.NaN"/> when the candidate measured nothing.</summary>
    internal static double Ratio(double baselineNanoseconds, double candidateNanoseconds) =>
        candidateNanoseconds > 0 ? baselineNanoseconds / candidateNanoseconds : double.NaN;

    /// <summary>
    /// Merges the per-round statistics of one lane into a single result: point
    /// estimates are the median over rounds (one noisy round cannot move them),
    /// <see cref="Statistics.Min"/> / <see cref="Statistics.Max"/> are the run's
    /// extremes, cycle, overhead and noise figures are the typical round, and any
    /// resolution or measurement limit seen in a round survives the merge.
    /// </summary>
    internal static Statistics Merge(IReadOnlyList<Statistics> rounds)
    {
        var avgs = new double[rounds.Count];
        var p50 = new double[rounds.Count];
        var p90 = new double[rounds.Count];
        var p95 = new double[rounds.Count];
        var p99 = new double[rounds.Count];
        var stdDev = new double[rounds.Count];
        var stdErr = new double[rounds.Count];
        var rsd = new double[rounds.Count];
        var cycles = new double[rounds.Count];
        var overhead = new double[rounds.Count];

        var min = double.MaxValue;
        var max = double.MinValue;
        var resolutionLimit = 0.0;
        var resolutionLimited = false;
        var cyclesApproximate = false;
        var unmeasured = 0;
        int gen0 = 0,
            gen1 = 0,
            gen2 = 0;
        var gcApproximate = false;
        var anyGc = false;

        for (var i = 0; i < rounds.Count; i++)
        {
            var s = rounds[i];
            avgs[i] = s.Avg;
            p50[i] = s.P50;
            p90[i] = s.P90;
            p95[i] = s.P95;
            p99[i] = s.P99;
            stdDev[i] = s.StdDev;
            stdErr[i] = s.StandardError;
            rsd[i] = s.RelativeStdDevPercent;
            cycles[i] = s.CpuCyclesPerOp;
            overhead[i] = s.OverheadNs;

            if (s.Min < min)
                min = s.Min;
            if (s.Max > max)
                max = s.Max;
            if (s.ResolutionLimitNanoseconds > resolutionLimit)
                resolutionLimit = s.ResolutionLimitNanoseconds;
            resolutionLimited |= s.ResolutionLimited;
            cyclesApproximate |= s.CpuCyclesAreApproximate;
            unmeasured += s.UnmeasuredSampleCount;

            if (s.GcInfo is { } gc)
            {
                anyGc = true;
                gen0 += gc.Gen0;
                gen1 += gc.Gen1;
                gen2 += gc.Gen2;
                gcApproximate |= gc.IsApproximate;
            }
        }

        Array.Sort(avgs);
        Array.Sort(p50);
        Array.Sort(p90);
        Array.Sort(p95);
        Array.Sort(p99);
        Array.Sort(stdDev);
        Array.Sort(stdErr);
        Array.Sort(rsd);
        Array.Sort(cycles);
        Array.Sort(overhead);

        return new Statistics
        {
            Avg = Median(avgs),
            P50 = Median(p50),
            P90 = Median(p90),
            P95 = Median(p95),
            P99 = Median(p99),
            Min = min,
            Max = max,
            StdDev = Median(stdDev),
            StandardError = Median(stdErr),
            RelativeStdDevPercent = Median(rsd),
            CpuCyclesPerOp = Median(cycles),
            CpuCyclesAreApproximate = cyclesApproximate,
            OverheadNs = Median(overhead),
            ResolutionLimitNanoseconds = resolutionLimit,
            ResolutionLimited = resolutionLimited,
            UnmeasuredSampleCount = unmeasured,
            GcInfo = anyGc
                ? new GcInfo
                {
                    Gen0 = gen0,
                    Gen1 = gen1,
                    Gen2 = gen2,
                    IsApproximate = gcApproximate,
                }
                : null,
        };
    }

    /// <summary>Median of a sequence that is either empty or already sorted.</summary>
    internal static double Median(IReadOnlyList<double> sorted)
    {
        if (sorted.Count == 0)
            return double.NaN;

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    /// <summary>
    /// Rank <c>k</c> (1-based) of the narrowest order-statistic interval
    /// <c>[x_(k), x_(n+1-k)]</c> that covers the median with at least 95%
    /// confidence. The coverage is <c>1 - 2·P(Bin(n, 0.5) ≤ k-1)</c> — the sign
    /// test, i.e. no assumption about the distribution of the ratios.
    /// <para>
    /// The 95% is only reached from <see cref="MinimumUsableRounds"/> (6) usable
    /// ratios upwards: below that this method still returns the full range, whose
    /// real coverage is 50/75/87.5/93.75% for n = 1/2/3/4/5. Callers must
    /// therefore reject counts below <see cref="MinimumUsableRounds"/> — see
    /// <see cref="PairedSpeedup"/>.
    /// </para>
    /// </summary>
    internal static int MedianConfidenceRank(int count)
    {
        if (count < 3)
            return 1;

        const double tailLimit = 0.025;
        var term = Math.Pow(0.5, count); // P(X = 0)
        var cumulative = term; // P(X <= 0)
        var rank = 1;

        for (var i = 1; i <= count; i++)
        {
            term *= (double)(count - i + 1) / i;
            cumulative += term; // P(X <= i)
            var candidate = i + 1; // interval rank whose tail is P(X <= i)
            if (cumulative > tailLimit || candidate > (count + 1) / 2)
                break;

            rank = candidate;
        }

        return rank;
    }
}
