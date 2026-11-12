namespace PicoBench.Formatters;

/// <summary>
/// The one sentence the three display formats print under a table whose cycle cells
/// carry a <c>~</c> (T4 · macOS cycle semantics).
/// <para>
/// The value is <b>kept</b>: on macOS the counter is a monotonic clock, so the number
/// is a real duration but not an architectural cycle count, and an asynchronous run
/// reads a process-wide counter that also counts other threads' work. Either way the
/// cell cannot simply be dropped, so every format keeps the number, marks it with
/// <c>~</c> in the same place, and says what the marker means. CSV carries the same
/// contract in its <c># Schema:</c> line
/// (<c>CpuCycles = count | "~count" (not a thread-scoped cycle count)</c>).
/// </para>
/// <para>
/// R-T4 · B5: this used to be three <c>internal const</c> fields in the three
/// formatters, with three different wordings (one carried an extra clause, one used
/// Markdown backticks, one ended differently) — each documented as "the one sentence
/// the three display formats print". The wording could drift and no test could see it.
/// It is now literally one <see cref="Sentence"/>; each format supplies only its own
/// prefix, so the shared part cannot diverge.
/// </para>
/// </summary>
internal static class ApproximateCycles
{
    /// <summary>
    /// What the <c>~</c> means — the part every display format has to say identically.
    /// </summary>
    internal const string Sentence =
        "the counter is process-wide (an asynchronous run can resume on another thread) "
        + "or a monotonic clock proxy (macOS mach_absolute_time), not a thread-scoped cycle count.";

    /// <summary>The Console's whole note line: it carries its own prefix.</summary>
    internal const string ConsoleNote = "~ CPU Cycles: " + Sentence;

    /// <summary>The Markdown note's bold lead-in; the line reads <c>MarkdownPrefix + Sentence</c>.</summary>
    internal const string MarkdownPrefix = "> **~ CPU Cycle** — ";

    /// <summary>The HTML note's opening tag and prefix; the line reads <c>HtmlPrefix + Sentence + &lt;/p&gt;</c>.</summary>
    internal const string HtmlPrefix = "<p class=\"note\">~ CPU Cycles: ";
}
