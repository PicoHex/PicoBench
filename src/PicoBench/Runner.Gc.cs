using System.Reflection;

namespace PicoBench;

public static partial class Runner
{
    /// <summary>
    /// The GC counters captured before a sample runs: collection counts plus the
    /// allocation counters, so one baseline covers both.
    /// </summary>
    private readonly struct GcBaseline
    {
        internal long[] Counts { get; init; }

        /// <summary>
        /// Thread-scoped allocation counter at the start of the window, or
        /// <c>null</c> when the running runtime does not expose it.
        /// </summary>
        internal long? ThreadAllocatedBytes { get; init; }

        /// <summary>
        /// Process-wide allocation counter at the start of the window, or
        /// <c>null</c> when the running runtime does not expose it.
        /// </summary>
        internal long? ProcessAllocatedBytes { get; init; }
    }

    /// <summary>
    /// Records the current GC state without forcing a collection.
    /// Forced GC.Collect per sample introduces significant overhead and distorts
    /// the timing results. The caller (Benchmark) should perform a single forced
    /// GC before the entire collection loop if a clean baseline is desired.
    /// </summary>
    private static GcBaseline GetGcBaseline()
    {
        var gcCounts = new long[GC.MaxGeneration + 1];
        for (var i = 0; i <= GC.MaxGeneration; i++)
            gcCounts[i] = GC.CollectionCount(i);

        return new GcBaseline
        {
            Counts = gcCounts,
            ThreadAllocatedBytes = GetAllocatedBytesForCurrentThread(),
            ProcessAllocatedBytes = GetTotalAllocatedBytes(),
        };
    }

    /// <summary>
    /// Builds the sample's <see cref="GcInfo"/> from the baseline.
    /// </summary>
    /// <param name="baseline">Counters taken before the measured window.</param>
    /// <param name="processWideAllocation">
    /// True for the asynchronous path, which may resume on another thread and
    /// therefore reads (and marks approximate) the process-wide allocation
    /// counter instead of the thread-scoped one.
    /// </param>
    private static GcInfo CalculateGcDelta(GcBaseline baseline, bool processWideAllocation)
    {
        static int ComputeDelta(int current, long baseline)
        {
            var currentU = unchecked((uint)current);
            var baselineU = unchecked((uint)baseline);
            var deltaU = currentU - baselineU;
            return unchecked((int)deltaU);
        }

        var gen0 = ComputeDelta(GC.CollectionCount(0), baseline.Counts[0]);
        var gen1 =
            GC.MaxGeneration >= 1 ? ComputeDelta(GC.CollectionCount(1), baseline.Counts[1]) : 0;
        var gen2 =
            GC.MaxGeneration >= 2 ? ComputeDelta(GC.CollectionCount(2), baseline.Counts[2]) : 0;

        if (gen0 < 0)
            gen0 = 0;
        if (gen1 < 0)
            gen1 = 0;
        if (gen2 < 0)
            gen2 = 0;

        var allocation = CalculateAllocationDelta(baseline, processWideAllocation);

        return new GcInfo
        {
            Gen0 = gen0,
            Gen1 = gen1,
            Gen2 = gen2,
            AllocatedBytes = allocation.Bytes,
            AllocatedBytesAreApproximate = allocation.Approximate,
        };
    }

    /// <summary>
    /// The allocation delta of the window, and whether it is approximate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The asynchronous path <b>always</b> uses the process-wide counter: an
    /// <c>await</c> may resume on another thread, so a thread-scoped counter taken
    /// before the loop would not describe the same thread. The value is flagged
    /// approximate because it also counts allocations made by other threads.
    /// </para>
    /// <para>
    /// The synchronous path prefers the thread-scoped counter (exact). When the
    /// runtime does not expose it but does expose the process-wide one, that is used
    /// instead and flagged approximate, so the metric is still reported — a
    /// downgrade, never a silent zero.
    /// </para>
    /// <para>
    /// When the counter a run needs is unavailable the caller gets <c>null</c> ("not
    /// collected", rendered <c>N/A</c>): a <b>synchronous</b> run degrades from the
    /// thread-scoped counter to the process-wide one only if the former is absent,
    /// and an <b>asynchronous</b> run needs the process-wide counter, which .NET
    /// Framework does not expose at all, so the net4x async path reports <c>N/A</c>.
    /// A <b>negative</b> delta is treated the same way: a cumulative counter cannot
    /// run backwards, so a negative reading is a failed measurement that must not be
    /// published as <c>0</c>.
    /// </para>
    /// </remarks>
    private static (long? Bytes, bool Approximate) CalculateAllocationDelta(
        GcBaseline baseline,
        bool processWideAllocation
    )
    {
        if (processWideAllocation)
            return Delta(GetTotalAllocatedBytes(), baseline.ProcessAllocatedBytes, approximate: true);

        var thread = Delta(
            GetAllocatedBytesForCurrentThread(),
            baseline.ThreadAllocatedBytes,
            approximate: false
        );
        if (thread.Bytes is not null)
            return thread;

        // Fallback: this runtime has no thread-scoped counter but does expose the
        // process-wide one. Report it, and disclose that it is process-wide.
        return Delta(GetTotalAllocatedBytes(), baseline.ProcessAllocatedBytes, approximate: true);
    }

    /// <summary>Difference of a cumulative counter, or "not collected".</summary>
    private static (long? Bytes, bool Approximate) Delta(
        long? current,
        long? baseline,
        bool approximate
    )
    {
        if (current is not { } now || baseline is not { } start)
            return (null, false);

        var delta = unchecked(now - start);
        return delta < 0 ? (null, false) : (delta, approximate);
    }

    // ─── Allocation counters ─────────────────────────────────────────
    //
    // The netstandard2.0 reference assembly declares neither counter, so the
    // library resolves them once at run time through reflection and binds each to a
    // delegate (the delegate is what the hot path calls — no per-call reflection, no
    // boxing). The probe is also what keeps the AOT/trimming legs working: a method
    // that does not exist yields null instead of failing the publish, and the CI AOT
    // job compiles this file with IL2xxx/IL3xxx as errors.

    /// <summary>Resolved once; both members are <c>null</c> when unavailable.</summary>
    private static readonly Lazy<AllocationCounters> ResolvedAllocationCounters = new(
        ResolveAllocationCounters,
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    private readonly struct AllocationCounters
    {
        internal Func<long>? ThreadBytes { get; init; }

        internal Func<bool, long>? ProcessBytes { get; init; }
    }

    private static AllocationCounters ResolveAllocationCounters() =>
        new()
        {
            ThreadBytes = ResolveDelegate<Func<long>>(
                "GetAllocatedBytesForCurrentThread",
                Type.EmptyTypes
            ),
            ProcessBytes = ResolveDelegate<Func<bool, long>>(
                "GetTotalAllocatedBytes",
                [typeof(bool)]
            ),
        };

    /// <summary>
    /// Resolves one <see cref="GC"/> method by shape and binds it to
    /// <typeparamref name="TDelegate"/>. Every failure mode (method absent, wrong
    /// signature, trimming that removed the metadata, a runtime that refuses
    /// delegate creation) yields <c>null</c>: the metric becomes "not collected"
    /// rather than the benchmark failing.
    /// </summary>
    private static TDelegate? ResolveDelegate<TDelegate>(string methodName, Type[] parameters)
        where TDelegate : class
    {
        try
        {
            var method = typeof(GC).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: parameters,
                modifiers: null
            );

            return method is null ? null : method.CreateDelegate(typeof(TDelegate)) as TDelegate;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Bytes allocated by the current thread so far, or <c>null</c> when the
    /// runtime does not expose a thread-scoped counter.
    /// </summary>
    private static long? GetAllocatedBytesForCurrentThread() =>
        ResolvedAllocationCounters.Value.ThreadBytes is { } probe ? probe() : null;

    /// <summary>
    /// Bytes allocated by the process so far, or <c>null</c> when the runtime does
    /// not expose a process-wide counter. Read with <c>precise: false</c>: the
    /// precise reading can force a collection, which would distort the sample it is
    /// taken around.
    /// </summary>
    private static long? GetTotalAllocatedBytes() =>
        ResolvedAllocationCounters.Value.ProcessBytes is { } probe ? probe(false) : null;
}
