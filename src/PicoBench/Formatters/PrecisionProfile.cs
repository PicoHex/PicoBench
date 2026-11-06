namespace PicoBench.Formatters;

/// <summary>
/// How a formatter renders the per-operation figures (nanoseconds, percentiles,
/// standard error).
/// <para>
/// Batch 2 · 2.4 (B2-M5, B2-M6) split the two jobs one number had: a value has to
/// be <em>readable</em> in a table and <em>exact</em> in a data file, and the four
/// formatters each picked a different compromise. The profile makes the choice
/// explicit per formatter instead of implicit per cell:
/// </para>
/// <list type="bullet">
/// <item><see cref="Raw"/> — the number as measured, in nanoseconds, and the
/// column header (or CSV column name) says <c>ns</c>. This is what
/// <see cref="CsvFormatter"/> always does, because a machine-readable file must not
/// change its unit with the magnitude of the data.</item>
/// <item><see cref="Display"/> — the same number scaled to the table's readable
/// unit (<c>ns</c>, <c>µs</c>, <c>ms</c>, <c>s</c>), with the unit in the column
/// header. Console, Markdown and HTML default to this.</item>
/// </list>
/// <para>
/// Either way the value comes from the same place and only the presentation
/// changes: the three display formats agree character for character on the same
/// measurement, and the raw number is always available from CSV.
/// </para>
/// </summary>
public enum MetricPrecision
{
    /// <summary>Unscaled nanoseconds, labelled <c>ns</c>.</summary>
    Raw = 0,

    /// <summary>Scaled to the table's readable unit (nanoseconds, µs, ms or seconds).</summary>
    Display = 1,
}

/// <summary>
/// The unit one per-operation table is expressed in, and the scale between it and
/// nanoseconds.
/// <para>
/// Batch 2 · 2.4 (B2-M6): <c>Avg = 5_000_000_000</c> rendered as
/// <c>5000000000.0</c> in a column whose header said <c>(ns)</c>: correct, and
/// unreadable. The unit is chosen <b>once per table</b> from the largest
/// per-operation value the table holds, so every column of one table shares it.
/// That is what makes the column comparable, and it is also why a small neighbour
/// of a large value reads <c>0.0</c>: the <c>2.5 ms</c> row and the <c>150 ns</c>
/// row of one table are both printed in <c>ms</c>, so the second one rounds to
/// zero. A per-cell rescale would put <c>150.0</c> and <c>2.5</c> in the same column
/// instead — each readable on its own, and the column no longer comparable across
/// rows.
/// </para>
/// <para>
/// The thresholds are one thousand of the next smaller unit, i.e. the unit that
/// keeps the integer part short: below 1 000 ns the column is <c>ns</c>, below
/// 1 000 µs <c>µs</c>, below 1 000 ms <c>ms</c>, and seconds above that.
/// </para>
/// </summary>
internal readonly struct TimeUnit
{
    private TimeUnit(string symbol, double divisor)
    {
        Symbol = symbol;
        Divisor = divisor;
    }

    /// <summary>The unit as it is printed in a column header (<c>ns</c>, <c>µs</c>, <c>ms</c>, <c>s</c>).</summary>
    internal string Symbol { get; }

    /// <summary>How many nanoseconds one unit is.</summary>
    internal double Divisor { get; }

    /// <summary>Nanoseconds as this unit.</summary>
    internal double Of(double nanoseconds) => nanoseconds / Divisor;

    internal static readonly TimeUnit Nanoseconds = new("ns", 1.0);
    internal static readonly TimeUnit Microseconds = new("µs", 1_000.0);
    internal static readonly TimeUnit Milliseconds = new("ms", 1_000_000.0);
    internal static readonly TimeUnit Seconds = new("s", 1_000_000_000.0);

    /// <summary>
    /// The readable unit for a table whose largest per-operation value is
    /// <paramref name="maxNanoseconds"/>. A non-finite or non-positive maximum
    /// (every cell <c>N/A</c>, or a pathological fixture) keeps the table in
    /// nanoseconds: a scale chosen from a value that is not a measurement would be
    /// a guess dressed up as precision.
    /// </summary>
    internal static TimeUnit For(double maxNanoseconds)
    {
        if (!(maxNanoseconds > 0) || double.IsInfinity(maxNanoseconds))
            return Nanoseconds;

        return maxNanoseconds switch
        {
            < 1_000.0 => Nanoseconds,
            < 1_000_000.0 => Microseconds,
            < 1_000_000_000.0 => Milliseconds,
            _ => Seconds,
        };
    }
}
