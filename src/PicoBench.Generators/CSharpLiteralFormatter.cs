namespace PicoBench.Generators;

/// <summary>
/// Turns a Roslyn <see cref="TypedConstant"/> into the C# literal text the
/// emitter pastes into the generated source.
/// <para>
/// Batch 1 · 1.3 (B3-C4) moved every string/char escape into
/// <see cref="CSharpLiterals"/>; this type no longer owns an escape table of its
/// own (the copy here and <c>Emitter.EscapeRaw</c> had already drifted).
/// Batch 1 · 1.4 (B3-C6) added the two <see cref="TypedConstantKind"/> shapes
/// that used to fall through the switch: <see cref="TypedConstantKind.Type"/>
/// (<c>[Params(typeof(int))]</c>) and <see cref="TypedConstantKind.Array"/>
/// (<c>[Params(new[]{1,2})]</c>, whose <c>Value</c> getter <b>throws</b>).
/// </para>
/// </summary>
internal static class CSharpLiteralFormatter
{
    internal static string FormatConstant(TypedConstant constant, ITypeSymbol? targetType)
    {
        switch (constant.Kind)
        {
            case TypedConstantKind.Error:
                // The compiler could not bind the argument (it already reported
                // why); never let our own text become the second error.
                return "default";

            case TypedConstantKind.Type:
                return constant.Value is ITypeSymbol typeArgument
                    ? $"typeof({typeArgument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})"
                    : "null";

            case TypedConstantKind.Array:
                return FormatArrayLiteral(constant);
        }

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

    /// <summary>
    /// An array-valued <c>[Params]</c> entry, formatted recursively from
    /// <see cref="TypedConstant.Values"/> (never from <c>Value</c>, which throws
    /// for arrays). The constant's own type names the literal, so an
    /// <c>int[]</c> value on an <c>int[]</c> member stays exactly what the user
    /// wrote. Nested arrays cannot actually reach here — C# rejects a jagged
    /// array in an attribute argument (<c>CS0182</c>) — but recursion costs
    /// nothing and a future Roslyn that allows it must not crash us.
    /// </summary>
    private static string FormatArrayLiteral(TypedConstant constant)
    {
        var elementType = (constant.Type as IArrayTypeSymbol)?.ElementType;
        var typeName =
            constant.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            ?? "global::System.Object[]";

        var elements = string.Join(
            ", ",
            constant.Values.Select(element => FormatConstant(element, elementType))
        );
        return $"new {typeName} {{ {elements} }}";
    }

    /// <summary>
    /// A value for a diagnostic message. <c>ToCSharpString()</c> is not usable
    /// on the shapes 1.4 taught us about (it has no <c>Type</c>/<c>Array</c>
    /// story of its own), so the same formatter produces the text — the message
    /// then shows the user the value they actually typed.
    /// </summary>
    internal static string DescribeConstant(TypedConstant constant) =>
        constant.Kind switch
        {
            TypedConstantKind.Array => FormatArrayLiteral(constant),
            TypedConstantKind.Type => FormatConstant(constant, null),
            _ => constant.ToCSharpString(),
        };

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
