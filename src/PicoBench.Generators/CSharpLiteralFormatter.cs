namespace PicoBench.Generators;

/// <summary>
/// Turns a Roslyn <see cref="TypedConstant"/> into the C# literal text the
/// emitter pastes into the generated source.
/// <para>
/// Batch 1 · 1.3 (B3-C4) moved every string/char escape into
/// <see cref="CSharpLiterals"/>; this type no longer owns an escape table of its
/// own (the copy here had already drifted from <c>Emitter.EscapeRaw</c>).
/// </para>
/// </summary>
internal static class CSharpLiteralFormatter
{
    internal static string FormatConstant(TypedConstant constant, ITypeSymbol? targetType)
    {
        if (constant.IsNull)
            return "null";

        if (targetType is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            return FormatEnumLiteral(enumType, constant.Value);

        var value = constant.Value;
        return value switch
        {
            string s => CSharpLiterals.StringLiteral(s),
            bool b => b ? "true" : "false",
            char c => CSharpLiterals.CharLiteral(c),
            float f => FormatFloatLiteral(f),
            double d => FormatDoubleLiteral(d),
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
            ulong ul => ul.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL",
            uint ui => ui.ToString(System.Globalization.CultureInfo.InvariantCulture) + "U",
            _ => value?.ToString() ?? "default",
        };
    }

    private static string FormatEnumLiteral(INamedTypeSymbol enumType, object? value)
    {
        var underlyingValue = value is null ? "0" : FormatPrimitiveNumericLiteral(value);
        return $"({enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){underlyingValue}";
    }

    private static string FormatPrimitiveNumericLiteral(object value)
    {
        return value switch
        {
            byte b => b.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sbyte sb => sb.ToString(System.Globalization.CultureInfo.InvariantCulture),
            short s => s.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ushort us => us.ToString(System.Globalization.CultureInfo.InvariantCulture),
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            uint ui => ui.ToString(System.Globalization.CultureInfo.InvariantCulture) + "U",
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
            ulong ul => ul.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "0",
        };
    }

    private static string FormatFloatLiteral(float f)
    {
        if (float.IsNaN(f))
            return "float.NaN";
        if (float.IsPositiveInfinity(f))
            return "float.PositiveInfinity";
        if (float.IsNegativeInfinity(f))
            return "float.NegativeInfinity";
        return f.ToString("G", System.Globalization.CultureInfo.InvariantCulture) + "F";
    }

    private static string FormatDoubleLiteral(double d)
    {
        if (double.IsNaN(d))
            return "double.NaN";
        if (double.IsPositiveInfinity(d))
            return "double.PositiveInfinity";
        if (double.IsNegativeInfinity(d))
            return "double.NegativeInfinity";
        return d.ToString("G", System.Globalization.CultureInfo.InvariantCulture) + "D";
    }
}
