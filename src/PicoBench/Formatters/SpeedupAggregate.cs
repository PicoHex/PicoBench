namespace PicoBench.Formatters;

/// <summary>
/// The speedup figures of a set of comparisons, computed over the ratios that
/// <b>exist</b> only (Batch 2 · 2.2, B2-M2).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ComparisonResult.Speedup"/> returns <c>+∞</c> when the candidate
/// costs nothing and <c>NaN</c> when it was not measured; both are legitimate
/// answers to "what is the ratio", and neither is a number a summary can
/// average. <c>comparisons.Average(c =&gt; c.Speedup)</c> therefore used to turn
/// the whole box into <c>∞</c> (or <c>NaN</c>) as soon as one scenario hit that
/// case, hiding the other scenarios' results.
/// </para>
/// <para>
/// The aggregate keeps only the finite ratios, remembers how many it had to
/// leave out, and hands the count back so every format can disclose it —
/// dropping a scenario silently would be a different lie.
/// </para>
/// </remarks>
internal readonly struct SpeedupAggregate
{
    private readonly double[] _finite;

    private SpeedupAggregate(double[] finite, int excluded)
    {
        _finite = finite;
        Excluded = excluded;
    }

    /// <summary>Number of comparisons whose ratio is <c>NaN</c> or <c>±∞</c>.</summary>
    internal int Excluded { get; }

    /// <summary>Number of comparisons that contributed a finite ratio.</summary>
    internal int Count => _finite.Length;

    /// <summary>Mean of the finite ratios; <c>null</c> when there is none.</summary>
    internal double? Average => _finite.Length == 0 ? null : _finite.Average();

    /// <summary>Largest finite ratio; <c>null</c> when there is none.</summary>
    internal double? Maximum => _finite.Length == 0 ? null : _finite.Max();

    /// <summary>Aggregates <paramref name="comparisons"/> in one pass.</summary>
    internal static SpeedupAggregate From(IEnumerable<ComparisonResult> comparisons)
    {
        if (comparisons == null)
            throw new ArgumentNullException(nameof(comparisons));

        var finite = new List<double>();
        var excluded = 0;

        foreach (var comparison in comparisons)
        {
            var speedup = comparison.Speedup;
            if (FormatterBase.IsFinite(speedup))
                finite.Add(speedup);
            else
                excluded++;
        }

        return new SpeedupAggregate([.. finite], excluded);
    }

    /// <summary>
    /// The sentence that discloses the skipped scenarios, e.g.
    /// <c>1 scenario not computable (non-finite speedup)</c>. Empty when every
    /// comparison contributed a ratio.
    /// </summary>
    internal string DescribeExcluded() =>
        Excluded == 0
            ? string.Empty
            : Excluded.ToString(CultureInfo.InvariantCulture)
                + (Excluded == 1 ? " scenario" : " scenarios")
                + " not computable (non-finite speedup)";
}
