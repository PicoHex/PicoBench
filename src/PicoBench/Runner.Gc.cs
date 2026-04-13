namespace PicoBench;

public static partial class Runner
{
    private static long[] GetGcBaselineCounts()
    {
        // Record current GC counts without forcing a collection.
        // Forced GC.Collect per sample introduces significant overhead and distorts
        // the timing results. The caller (Benchmark) should perform a single forced
        // GC before the entire collection loop if a clean baseline is desired.
        var gcCounts = new long[GC.MaxGeneration + 1];
        for (var i = 0; i <= GC.MaxGeneration; i++)
            gcCounts[i] = GC.CollectionCount(i);

        return gcCounts;
    }

    private static GcInfo CalculateGcDelta(long[] baselineCounts)
    {
        static int ComputeDelta(int current, long baseline)
        {
            var currentU = unchecked((uint)current);
            var baselineU = unchecked((uint)baseline);
            var deltaU = currentU - baselineU;
            return unchecked((int)deltaU);
        }

        var gen0 = ComputeDelta(GC.CollectionCount(0), baselineCounts[0]);
        var gen1 =
            GC.MaxGeneration >= 1 ? ComputeDelta(GC.CollectionCount(1), baselineCounts[1]) : 0;
        var gen2 =
            GC.MaxGeneration >= 2 ? ComputeDelta(GC.CollectionCount(2), baselineCounts[2]) : 0;

        if (gen0 < 0)
            gen0 = 0;
        if (gen1 < 0)
            gen1 = 0;
        if (gen2 < 0)
            gen2 = 0;

        return new GcInfo
        {
            Gen0 = gen0,
            Gen1 = gen1,
            Gen2 = gen2
        };
    }
}
