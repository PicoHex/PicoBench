namespace PicoBench.Generators;

/// <summary>
/// The <b>single</b> place that turns a CLR string/char back into C# literal
/// text. Batch 1 · 1.3 (B3-C4) merged the two independent copies that used to
/// live in <c>CSharpLiteralFormatter</c> (the <c>[Params]</c> path) and
/// <c>Emitter.EscapeRaw</c> (the class <c>Description</c> path) — both stopped
/// at <c>c &lt; 0x20</c>, so one of them drifting was only a matter of time.
/// </summary>
/// <remarks>
/// <para>
/// Two classes of characters have to be escaped and neither is "exotic":
/// </para>
/// <list type="number">
/// <item>
/// <b>C# <c>new_line_character</c>s</b>: U+000D, U+000A, <b>U+0085 (NEL)</b>,
/// <b>U+2028 (LS)</b>, <b>U+2029 (PS)</b>. A string literal may not contain any
/// of them, so a raw one ends the literal and the rest of the generated file is
/// parsed as code — the consumer gets <c>CS0825</c>/<c>CS1010</c> at a line they
/// never wrote. NEL/LS/PS arrive easily by copy-paste from Word/PDF.
/// </item>
/// <item>
/// <b>Unpaired surrogates</b>. A <c>string</c> may hold a lone <c>\uD800</c>,
/// but a <c>.g.cs</c> file is text: written raw it cannot be encoded as UTF-8
/// (it must not be), so the value the compiler sees is no longer the value the
/// attribute declared. A <i>paired</i> surrogate is a normal character and is
/// emitted as-is — splitting a valid pair into two escapes would be wrong.
/// </item>
/// </list>
/// <para>
/// Everything else that is invisible is escaped too (C0 controls, DEL and the
/// rest of C1): legal in a literal, unreadable in a diff.
/// </para>
/// </remarks>
internal static class CSharpLiterals
{
    /// <summary>Escapes <paramref name="value"/> — the body of a string literal, without the quotes.</summary>
    internal static string EscapeString(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\0':
                    sb.Append("\\0");
                    break;
                default:
                    if (
                        char.IsHighSurrogate(c)
                        && i + 1 < value.Length
                        && char.IsLowSurrogate(value[i + 1])
                    )
                    {
                        // A valid pair is a real character (😀 is legal in a
                        // literal); keep both halves together.
                        sb.Append(c).Append(value[++i]);
                    }
                    else if (NeedsEscape(c))
                    {
                        AppendUnicodeEscape(sb, c);
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>A complete C# string literal, quotes included.</summary>
    internal static string StringLiteral(string? value) =>
        value is null ? "null" : $"\"{EscapeString(value)}\"";

    /// <summary>A complete C# <c>char</c> literal.</summary>
    internal static string CharLiteral(char c) =>
        c switch
        {
            '\'' => "'\\''",
            '\\' => "'\\\\'",
            '\n' => "'\\n'",
            '\r' => "'\\r'",
            '\t' => "'\\t'",
            '\0' => "'\\0'",
            _ when NeedsEscape(c) || c > 0x7E => $"'\\u{(int)c:X4}'",
            _ => $"'{c}'",
        };

    /// <summary>
    /// <c>true</c> for the characters that may not appear raw in generated
    /// source: C# <c>new_line_character</c>s, the invisible control range, and
    /// lone surrogates.
    /// </summary>
    internal static bool NeedsEscape(char c) =>
        c < 0x20 // C0 controls, including \r \n \t \0 (never reached with the named escapes above)
        || (c >= 0x7F && c <= 0x9F) // DEL, NEL (0x85) and the C1 controls
        || c == '\u2028' // LINE SEPARATOR
        || c == '\u2029' // PARAGRAPH SEPARATOR
        || char.IsSurrogate(c);

    private static void AppendUnicodeEscape(StringBuilder sb, char c) =>
        sb.Append("\\u")
            .Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
}
