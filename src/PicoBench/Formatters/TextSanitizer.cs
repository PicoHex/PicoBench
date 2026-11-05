namespace PicoBench.Formatters;

/// <summary>
/// Escapes <b>user text</b> — benchmark names, suite descriptions, category and
/// group names — before it is written into an output format.
/// <para>
/// Batch 2 · 2.5 (B2-H4, B2-L4). A name is data; it used to be written into
/// Markdown heading position verbatim, so <c>"S\r\n## Injected"</c> produced a real
/// heading, a description containing <c>```</c> closed the fence around it, and a
/// category name could break the table that followed its heading. In the Console
/// the same characters broke the frame: <c>\r</c> swallowed the rest of the line and
/// nothing stripped <c>\u001b</c>, so an ANSI sequence in a name reached the
/// terminal as an escape sequence.
/// </para>
/// <para>
/// Two rules, applied at every insertion point:
/// </para>
/// <list type="number">
/// <item><b>Control characters never travel.</b> C0 controls (including ESC) become
/// the matching Unicode <em>control picture</em> (<c>\u241b</c> for ESC, <c>\u240a</c>
/// for a line feed), DEL becomes <c>\u2421</c>, and the C1 range becomes <c>\uFFFD</c>,
/// so the reader sees that a control character was there instead of executing it.
/// U+2028/U+2029 (line and paragraph separator) are line breaks with extra steps and
/// are treated as such.</item>
/// <item><b>Document structure characters are escaped.</b> In Markdown,
/// <c>\</c>, <c>`</c>, <c>|</c> and <c>#</c> are prefixed with a backslash, so no
/// user text can open a code fence, add a column, or start a heading — a line break
/// in heading or paragraph position becomes a space (a cell keeps its
/// <c>&lt;br&gt;</c>, which cannot escape the table it is in).</item>
/// </list>
/// </summary>
internal static class TextSanitizer
{
    /// <summary>
    /// Text as it is printed to a terminal: single line, no escape sequences, no
    /// control characters that could move the cursor. A line break becomes a
    /// visible <c>\u240d</c>/<c>\u240a</c> pair so a reader can tell "this name
    /// contains a newline" from "the row was split".
    /// </summary>
    internal static string ForTerminal(string value)
    {
        if (value.Length == 0)
            return value;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (IsLineBreak(c))
            {
                sb.Append(LineBreakPicture(c));
                continue;
            }

            sb.Append(Picture(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// User text inside one Markdown table cell: the terminal escape rules, except
    /// that a line break stays a <c>&lt;br&gt;</c> (the cell can hold one; the row
    /// cannot), plus backslash-escaping of the four characters that carry Markdown
    /// structure — <c>\</c>, <c>`</c>, <c>|</c> and <c>#</c>.
    /// </summary>
    internal static string ForMarkdownCell(string value) => EscapeMarkdown(Pictures(value, cell: true));

    /// <summary>
    /// User text in Markdown <em>line</em> position — an ATX heading or a paragraph.
    /// Every line break collapses to a space, because that is the one thing user
    /// text must not do here: start a new Markdown block.
    /// </summary>
    internal static string ForMarkdownLine(string value) =>
        EscapeMarkdown(Pictures(value, cell: false));

    /// <summary>True for the characters that end a line in at least one renderer.</summary>
    internal static bool IsLineBreak(char c) =>
        c is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';

    private static char LineBreakPicture(char c) =>
        c switch
        {
            '\r' => '\u240d',
            '\u0085' => '\u240a', // NEL: next line, i.e. a line feed for this purpose
            '\n' or '\u2028' or '\u2029' => '\u240a',
            _ => '\u240a',
        };

    /// <summary>
    /// One character, made printable: a C0 control becomes the matching control
    /// picture, DEL the DEL picture, C1 the replacement character, everything else
    /// is left alone.
    /// </summary>
    private static char Picture(char c) =>
        c switch
        {
            < ' ' => (char)(0x2400 + c),
            '\u007f' => '\u2421',
            >= '\u0080' and <= '\u009f' => '\uFFFD',
            _ => c,
        };

    /// <summary>
    /// Control characters of a whole string: line breaks become <c>&lt;br&gt;</c> in
    /// cell position and a space in line position, everything else goes through
    /// <see cref="Picture(char)"/>.
    /// </summary>
    private static string Pictures(string value, bool cell)
    {
        if (value.Length == 0)
            return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (IsLineBreak(c))
            {
                // CRLF is one line break, not two pictures.
                if (c == '\r' && i + 1 < value.Length && value[i + 1] == '\n')
                    i++;

                sb.Append(cell ? "<br>" : " ");
                continue;
            }

            sb.Append(Picture(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Backslash-escapes the four characters that give user text document
    /// structure. The backslash itself goes first, so an escaped character cannot be
    /// produced by the text.
    /// </summary>
    private static string EscapeMarkdown(string value)
    {
        if (value.Length == 0)
            return value;

        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                case '`':
                case '|':
                case '#':
                    sb.Append('\\').Append(c);
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Display width of a string in terminal cells.
/// <para>
/// Batch 2 · 2.5 (B2-L4). The Console tables padded with
/// <see cref="string.Length"/>, which counts <em>characters</em>: a CJK name is two
/// cells wide but one char, so every line of a table holding one was short by one
/// cell per CJK character and the frame did not close. A combining mark is zero
/// cells wide, an emoji is two.
/// </para>
/// </summary>
internal static class DisplayWidth
{
    /// <summary>Cells a string occupies when printed in a monospaced terminal.</summary>
    internal static int Of(string value)
    {
        var width = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                width += IsWide(char.ConvertToUtf32(c, value[i + 1])) ? 2 : 1;
                i++;
                continue;
            }

            if (IsZeroWidth(c))
                continue;

            width += IsWide(c) ? 2 : 1;
        }

        return width;
    }

    /// <summary>
    /// Cuts a string to at most <paramref name="width"/> cells, never through a
    /// surrogate pair.
    /// </summary>
    internal static string Truncate(string value, int width)
    {
        var used = 0;
        var i = 0;
        while (i < value.Length)
        {
            var step = char.IsHighSurrogate(value[i]) && i + 1 < value.Length ? 2 : 1;
            var cell = Of(value.Substring(i, step));
            if (used + cell > width)
                return value.Substring(0, i);
            used += cell;
            i += step;
        }

        return value;
    }

    /// <summary>Right-pads a string with spaces until it occupies <paramref name="width"/> cells.</summary>
    internal static string PadRight(string value, int width, char padding = ' ')
    {
        var current = Of(value);
        return current >= width ? value : value + new string(padding, width - current);
    }

    /// <summary>Left-pads a string with spaces until it occupies <paramref name="width"/> cells.</summary>
    internal static string PadLeft(string value, int width, char padding = ' ')
    {
        var current = Of(value);
        return current >= width ? value : new string(padding, width - current) + value;
    }

    private static bool IsZeroWidth(char c) =>
        c is >= '\u0300' and <= '\u036f' // combining diacritical marks
            or >= '\u200b' and <= '\u200f'; // zero-width space/joiners, bidi marks

    private static bool IsWide(int code) =>
        code is >= 0x1100 and <= 0x115F // Hangul Jamo
            or >= 0x2E80 and <= 0x303E // CJK radicals, Kangxi
            or >= 0x3041 and <= 0x33FF // Hiragana .. CJK compatibility
            or >= 0x3400 and <= 0x4DBF // CJK ext A
            or >= 0x4E00 and <= 0x9FFF // CJK unified
            or >= 0xA000 and <= 0xA4CF // Yi
            or >= 0xAC00 and <= 0xD7A3 // Hangul syllables
            or >= 0xF900 and <= 0xFAFF // CJK compatibility ideographs
            or >= 0xFE30 and <= 0xFE6F // CJK compatibility forms
            or >= 0xFF00 and <= 0xFF60 // fullwidth forms
            or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1F64F // emoji
            or >= 0x1F900 and <= 0x1F9FF
            or >= 0x20000 and <= 0x3FFFD;
}
