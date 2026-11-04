namespace PicoBench.Generators;

/// <summary>
/// The single place that turns a symbol name into a legal C# identifier.
/// <para>
/// <c>ISymbol.Name</c> strips the <c>@</c> that made a keyword name legal
/// (<c>@class</c> arrives as <c>class</c>), so anything the emitter pastes into
/// a declaration or a call site has to go through here first. Contextual
/// keywords are escaped too: <c>@var</c> / <c>@record</c> are always legal,
/// while relying on position would break the moment a member lands in a
/// different context (and the review asks for them explicitly).
/// </para>
/// <para>
/// <b>Never use this for display strings.</b> <c>Benchmark.Run("@lock", …)</c>
/// would show the user an escape they never typed; the raw name is what belongs
/// in labels, suite names and diagnostics.
/// </para>
/// </summary>
internal static class IdentifierEscaping
{
    /// <summary>Every reserved and contextual C# keyword (C# 13).</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        // Reserved keywords
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
        // Contextual keywords (legal to escape, and position-independent)
        "add",
        "alias",
        "and",
        "args",
        "ascending",
        "async",
        "await",
        "by",
        "descending",
        "dynamic",
        "equals",
        "field",
        "file",
        "from",
        "get",
        "global",
        "group",
        "init",
        "into",
        "join",
        "let",
        "managed",
        "nameof",
        "nint",
        "not",
        "notnull",
        "nuint",
        "on",
        "or",
        "orderby",
        "partial",
        "record",
        "remove",
        "required",
        "scoped",
        "select",
        "set",
        "unmanaged",
        "value",
        "var",
        "when",
        "where",
        "with",
        "yield",
    };

    /// <summary>
    /// Returns <paramref name="identifier"/> prefixed with <c>@</c> when it is a
    /// keyword. Idempotent: an already-escaped name is returned unchanged, so a
    /// value can pass through several call sites without turning into <c>@@x</c>.
    /// </summary>
    internal static string EscapeIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return identifier;

        if (identifier[0] == '@')
            return identifier;

        return Keywords.Contains(identifier) ? "@" + identifier : identifier;
    }

    /// <summary>
    /// Escapes each segment of a dotted name — a namespace
    /// (<c>@class.@namespace</c>) or a qualified member name — segment by
    /// segment, keeping the dots.
    /// </summary>
    internal static string EscapeQualifiedName(string qualifiedName)
    {
        if (string.IsNullOrEmpty(qualifiedName))
            return qualifiedName;

        var segments = qualifiedName.Split('.');
        for (var i = 0; i < segments.Length; i++)
            segments[i] = EscapeIdentifier(segments[i]);

        return string.Join(".", segments);
    }
}
