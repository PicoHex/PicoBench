namespace PicoBench.Generators;

internal static class BenchmarkClassAnalyzer
{
    internal const string BenchmarkClassAttributeName = "PicoBench.BenchmarkClassAttribute";

    private const string BenchmarkAttributeName = "PicoBench.BenchmarkAttribute";
    private const string GlobalSetupAttributeName = "PicoBench.GlobalSetupAttribute";
    private const string GlobalCleanupAttributeName = "PicoBench.GlobalCleanupAttribute";
    private const string IterationSetupAttributeName = "PicoBench.IterationSetupAttribute";
    private const string IterationCleanupAttributeName = "PicoBench.IterationCleanupAttribute";
    private const string ParamsAttributeName = "PicoBench.ParamsAttribute";

    /// <summary>
    /// Test seam — the transform stage's fault-injection hook, reachable only
    /// from <c>PicoBench.Tests</c> (<c>InternalsVisibleTo</c>).
    /// <para>
    /// Batch 1 · B of <c>.review-2026-11/30-review-b1.md</c> / F2 of
    /// <c>31-verify-b1.md</c>: before this seam, the only thing that ever threw
    /// out of <see cref="AnalyzeTargetCore"/> was the <c>[Params(null)]</c>
    /// <c>NullReferenceException</c>, so <see cref="AnalyzeTarget"/>'s catch was
    /// guarded by a defect. Fixing that defect would have silently removed the
    /// guard: nothing would then notice if the catch were deleted, and one throw
    /// in one class would cost the whole project its generated output
    /// (<c>CS8785</c>). With the seam, the containment contract is testable on
    /// purpose rather than by accident.
    /// </para>
    /// <para>
    /// Always <c>null</c> in production runs; the hook is a filter so that a test
    /// run overlapping another one cannot break it.
    /// </para>
    /// </summary>
    internal static Action<INamedTypeSymbol>? TransformFaultForTests;

    /// <summary>
    /// Analyses one <c>[BenchmarkClass]</c> target.
    /// <para>
    /// Batch 1 · 1.1 (B3-C2): this is the transform stage, and an exception
    /// thrown here is <b>not</b> inside <c>RegisterSourceOutput</c> — it aborts
    /// the whole generator run (<c>CS8785</c>) and every class in the project
    /// silently loses its <c>.g.cs</c>. <c>[Params(new[] { … })]</c> is a real
    /// trigger: reading <c>TypedConstant.Value</c> on an array throws. So the
    /// transform is isolated too, and a failure here becomes one diagnostic for
    /// this one class.
    /// </para>
    /// </summary>
    internal static GeneratorAnalysisResult AnalyzeTarget(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct
    )
    {
        try
        {
            return AnalyzeTargetCore(ctx, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var name = ctx.TargetSymbol?.Name ?? "<unknown>";
            return new GeneratorAnalysisResult(
                null,
                ImmutableArray.Create(
                    Diagnostic.Create(
                        DiagnosticDescriptors.SourceGenerationFailed,
                        GetSymbolLocation(ctx.TargetSymbol),
                        name,
                        DescribeException(ex)
                    )
                ),
                GetSymbolLocation(ctx.TargetSymbol)
            );
        }
    }

    private static GeneratorAnalysisResult AnalyzeTargetCore(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct
    )
    {
        var diagnostics = new List<Diagnostic>();

        if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return new GeneratorAnalysisResult(null, diagnostics.ToImmutableArray());

        ct.ThrowIfCancellationRequested();

        // Test-only fault injection (see TransformFaultForTests). Deliberately
        // inside the try so it exercises AnalyzeTarget's catch exactly like a
        // real transform failure would.
        TransformFaultForTests?.Invoke(typeSymbol);

        if (!IsPartial(typeSymbol, ct))
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.BenchmarkClassMustBePartial,
                    GetTypeLocation(typeSymbol, ct),
                    typeSymbol.Name
                )
            );
        }

        if (typeSymbol.IsRecord)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.RecordBenchmarkClass,
                    GetTypeLocation(typeSymbol, ct),
                    typeSymbol.Name
                )
            );
        }

        if (typeSymbol.IsGenericType)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.GenericBenchmarkClass,
                    GetTypeLocation(typeSymbol, ct),
                    typeSymbol.Name
                )
            );
        }

        // Batch 1 · 1.6 (B3-H2). A `file` type cannot be extended from another
        // file, so the generated partial half cannot see its members: the consumer
        // used to get CS1061 on .g.cs with nothing explaining why.
        if (typeSymbol.IsFileLocal)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.FileLocalBenchmarkClass,
                    GetTypeLocation(typeSymbol, ct),
                    typeSymbol.Name
                )
            );
        }

        if (typeSymbol.ContainingType is not null)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.NestedBenchmarkClass,
                    GetTypeLocation(typeSymbol, ct),
                    typeSymbol.Name
                )
            );
        }

        if (!IsInstantiable(typeSymbol))
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.NonInstantiableBenchmarkClass,
                    GetTypeLocation(typeSymbol, ct),
                    typeSymbol.Name
                )
            );
        }

        var inheritedAttributesOwner = FindBaseTypeWithBenchmarkMembers(typeSymbol);
        if (inheritedAttributesOwner is not null)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InheritedBenchmarkMembers,
                    GetTypeLocation(typeSymbol, ct),
                    inheritedAttributesOwner
                )
            );
        }

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var accessibility = typeSymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal",
        };

        string? description = null;
        foreach (var named in ctx.Attributes.SelectMany(attr => attr.NamedArguments))
        {
            if (named is { Key: "Description", Value.Value: string desc })
                description = desc;
        }

        LifecycleMethodInfo? globalSetup = null;
        LifecycleMethodInfo? globalCleanup = null;
        LifecycleMethodInfo? iterSetup = null;
        LifecycleMethodInfo? iterCleanup = null;
        string? baselineMethod = null;
        var hasBenchmarkDeclarations = false;
        var methods = ImmutableArray.CreateBuilder<BenchmarkMethodModel>();
        var paramsProps = ImmutableArray.CreateBuilder<ParamsPropertyModel>();

        foreach (var member in typeSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            switch (member)
            {
                case IMethodSymbol method:
                    AnalyzeMethod(
                        method,
                        diagnostics,
                        ref globalSetup,
                        ref globalCleanup,
                        ref iterSetup,
                        ref iterCleanup,
                        ref baselineMethod,
                        ref hasBenchmarkDeclarations,
                        methods,
                        ct
                    );
                    break;
                case IPropertySymbol property:
                    TryAddParamsModel(
                        property,
                        property.Name,
                        property.Type,
                        property.GetAttributes(),
                        ctx.SemanticModel.Compilation,
                        diagnostics,
                        paramsProps,
                        ct
                    );
                    break;
                case IFieldSymbol field:
                    TryAddParamsModel(
                        field,
                        field.Name,
                        field.Type,
                        field.GetAttributes(),
                        ctx.SemanticModel.Compilation,
                        diagnostics,
                        paramsProps,
                        ct
                    );
                    break;
            }
        }

        if (!hasBenchmarkDeclarations)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.NoBenchmarkMethods,
                    GetTypeLocation(typeSymbol, ct),
                    typeSymbol.Name
                )
            );
        }

        if (diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
            return new GeneratorAnalysisResult(
                null,
                diagnostics.ToImmutableArray(),
                GetTypeLocation(typeSymbol, ct)
            );

        var isAsync =
            (globalSetup?.IsAsync ?? false)
            || (globalCleanup?.IsAsync ?? false)
            || (iterSetup?.IsAsync ?? false)
            || (iterCleanup?.IsAsync ?? false)
            || methods.Any(m => m.IsAsync);

        return new GeneratorAnalysisResult(
            new BenchmarkClassModel
            {
                Namespace = ns,
                ClassName = typeSymbol.Name,
                AccessModifier = accessibility,
                Description = description,
                IsAsync = isAsync,
                GlobalSetupMethod = globalSetup,
                GlobalCleanupMethod = globalCleanup,
                IterationSetupMethod = iterSetup,
                IterationCleanupMethod = iterCleanup,
                Methods = methods.ToImmutable(),
                ParamsProperties = paramsProps.ToImmutable(),
            },
            diagnostics.ToImmutableArray(),
            GetTypeLocation(typeSymbol, ct)
        );
    }

    private static void AnalyzeMethod(
        IMethodSymbol method,
        List<Diagnostic> diagnostics,
        ref LifecycleMethodInfo? globalSetup,
        ref LifecycleMethodInfo? globalCleanup,
        ref LifecycleMethodInfo? iterSetup,
        ref LifecycleMethodInfo? iterCleanup,
        ref string? baselineMethod,
        ref bool hasBenchmarkDeclarations,
        ImmutableArray<BenchmarkMethodModel>.Builder methods,
        CancellationToken ct
    )
    {
        var attrNames = new HashSet<string>();
        AttributeData? conflictingAttr = null;
        foreach (var attr in method.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();
            if (attrName is null)
                continue;
            attrNames.Add(attrName);
            conflictingAttr ??= attr;
        }

        var isBenchmark = attrNames.Contains(BenchmarkAttributeName);
        var isLifecycle =
            attrNames.Contains(GlobalSetupAttributeName)
            || attrNames.Contains(GlobalCleanupAttributeName)
            || attrNames.Contains(IterationSetupAttributeName)
            || attrNames.Contains(IterationCleanupAttributeName);

        // Batch 1 · 1.2 (B3-C3). An explicit interface implementation's symbol
        // name is "IThing.Do", which is not an identifier: the emitter used to
        // paste it into a local name and a call site (`this.IThing.Do()`), so a
        // legal C# class failed to compile with CS1003/CS1061 and the generator
        // said nothing. Reporting the shape is honest; calling it through the
        // interface would put a cast in every emission path (sync, async,
        // per-iteration setup/teardown) and change what is measured.
        if ((isBenchmark || isLifecycle) && method.ExplicitInterfaceImplementations.Length > 0)
        {
            if (isBenchmark)
                hasBenchmarkDeclarations = true;

            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.ExplicitInterfaceImplementationNotSupported,
                    GetAttributeLocation(conflictingAttr!, ct),
                    method.Name
                )
            );
            return;
        }

        if (isBenchmark && isLifecycle)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.ConflictingMethodRoles,
                    GetAttributeLocation(conflictingAttr!, ct),
                    method.Name
                )
            );
            return;
        }

        foreach (var attr in method.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();
            switch (attrName)
            {
                case BenchmarkAttributeName:
                    hasBenchmarkDeclarations = true;
                    RegisterBenchmarkMethod(
                        method,
                        attr,
                        diagnostics,
                        ref baselineMethod,
                        methods,
                        ct
                    );
                    break;
                case GlobalSetupAttributeName:
                    RegisterLifecycleMethod(
                        method,
                        attr,
                        ref globalSetup,
                        "[GlobalSetup]",
                        diagnostics,
                        ct
                    );
                    break;
                case GlobalCleanupAttributeName:
                    RegisterLifecycleMethod(
                        method,
                        attr,
                        ref globalCleanup,
                        "[GlobalCleanup]",
                        diagnostics,
                        ct
                    );
                    break;
                case IterationSetupAttributeName:
                    RegisterLifecycleMethod(
                        method,
                        attr,
                        ref iterSetup,
                        "[IterationSetup]",
                        diagnostics,
                        ct
                    );
                    break;
                case IterationCleanupAttributeName:
                    RegisterLifecycleMethod(
                        method,
                        attr,
                        ref iterCleanup,
                        "[IterationCleanup]",
                        diagnostics,
                        ct
                    );
                    break;
            }
        }
    }

    private static void RegisterBenchmarkMethod(
        IMethodSymbol method,
        AttributeData attr,
        List<Diagnostic> diagnostics,
        ref string? baselineMethod,
        ImmutableArray<BenchmarkMethodModel>.Builder methods,
        CancellationToken ct
    )
    {
        if (!IsValidBenchmarkMethod(method))
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidBenchmarkMethod,
                    GetAttributeLocation(attr, ct),
                    method.Name
                )
            );
            return;
        }

        if (method.IsAsync && method.ReturnsVoid)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.AsyncVoidBenchmarkMethod,
                    GetAttributeLocation(attr, ct),
                    method.Name
                )
            );
            return;
        }

        var isBaseline = false;
        string? methodDesc = null;
        foreach (var named in attr.NamedArguments)
        {
            switch (named.Key)
            {
                case "Baseline" when named.Value.Value is true:
                    isBaseline = true;
                    break;
                case "Description" when named.Value.Value is string description:
                    methodDesc = description;
                    break;
            }
        }

        switch (isBaseline)
        {
            case true when baselineMethod is not null:
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateBaseline,
                        GetAttributeLocation(attr, ct)
                    )
                );
                return;
            case true:
                baselineMethod = method.Name;
                break;
        }

        methods.Add(
            new BenchmarkMethodModel
            {
                Name = method.Name,
                IsAsync = !method.ReturnsVoid,
                IsBaseline = isBaseline,
                Description = methodDesc,
            }
        );
    }

    private static void TryAddParamsModel(
        ISymbol memberSymbol,
        string memberName,
        ITypeSymbol memberType,
        ImmutableArray<AttributeData> attrs,
        Compilation compilation,
        List<Diagnostic> diagnostics,
        ImmutableArray<ParamsPropertyModel>.Builder paramsProps,
        CancellationToken ct
    )
    {
        var paramAttr = FindAttribute(attrs, ParamsAttributeName);
        if (paramAttr is null)
            return;

        var paramModel = BuildParamsModel(
            memberSymbol,
            memberName,
            memberType,
            paramAttr,
            compilation,
            diagnostics,
            ct
        );

        if (paramModel is not null)
            paramsProps.Add(paramModel);
    }

    private static bool IsPartial(INamedTypeSymbol typeSymbol, CancellationToken ct)
    {
        foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
        {
            if (
                syntaxRef.GetSyntax(ct) is TypeDeclarationSyntax typeDecl
                && typeDecl.Modifiers.Any(static modifier =>
                    modifier.IsKind(SyntaxKind.PartialKeyword)
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInstantiable(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.IsAbstract || typeSymbol.IsStatic)
            return false;

        foreach (var constructor in typeSymbol.InstanceConstructors)
        {
            if (
                constructor.Parameters.Length == 0
                && constructor.DeclaredAccessibility == Accessibility.Public
            )
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindBaseTypeWithBenchmarkMembers(INamedTypeSymbol typeSymbol)
    {
        for (
            var baseType = typeSymbol.BaseType;
            baseType is not null && baseType.SpecialType != SpecialType.System_Object;
            baseType = baseType.BaseType
        )
        {
            foreach (var member in baseType.GetMembers())
            {
                if (member is IMethodSymbol && HasBenchmarkAttribute(member))
                    return baseType.Name;
            }
        }

        return null;
    }

    private static bool HasBenchmarkAttribute(ISymbol member)
    {
        foreach (var attr in member.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();
            if (
                attrName
                is BenchmarkAttributeName
                    or GlobalSetupAttributeName
                    or GlobalCleanupAttributeName
                    or IterationSetupAttributeName
                    or IterationCleanupAttributeName
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidLifecycleMethod(IMethodSymbol method)
    {
        return method is { IsStatic: false, IsGenericMethod: false, Parameters.Length: 0 }
            && (method.ReturnsVoid || IsTaskOrValueTask(method.ReturnType));
    }

    private static bool IsValidBenchmarkMethod(IMethodSymbol method)
    {
        return method is { IsStatic: false, IsGenericMethod: false, Parameters.Length: 0 }
            && (method.ReturnsVoid || IsTaskOrValueTask(method.ReturnType));
    }

    private static bool IsTaskOrValueTask(ITypeSymbol type)
    {
        // Match by full namespace so a user-defined type named "Task" is not
        // mistaken for an awaitable.
        return type is INamedTypeSymbol named
            && named.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks"
            && named.Name is "Task" or "ValueTask" or "Task`1" or "ValueTask`1";
    }

    private static void RegisterLifecycleMethod(
        IMethodSymbol method,
        AttributeData attr,
        ref LifecycleMethodInfo? target,
        string attributeName,
        List<Diagnostic> diagnostics,
        CancellationToken ct
    )
    {
        if (!IsValidLifecycleMethod(method))
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidLifecycleMethod,
                    GetAttributeLocation(attr, ct),
                    attributeName,
                    method.Name
                )
            );
            return;
        }

        // Check for async void (PBGEN009 warning)
        if (method.IsAsync && method.ReturnsVoid)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.AsyncVoidLifecycleMethod,
                    GetAttributeLocation(attr, ct),
                    attributeName,
                    method.Name
                )
            );
        }

        if (target is not null)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateLifecycleMethod,
                    GetAttributeLocation(attr, ct),
                    attributeName
                )
            );
            return;
        }

        target = new LifecycleMethodInfo { Name = method.Name, IsAsync = !method.ReturnsVoid };
    }

    private static AttributeData? FindAttribute(
        ImmutableArray<AttributeData> attrs,
        string fullyQualifiedName
    )
    {
        return attrs.FirstOrDefault(attr =>
            attr.AttributeClass?.ToDisplayString() == fullyQualifiedName
        );
    }

    private static ParamsPropertyModel? BuildParamsModel(
        ISymbol memberSymbol,
        string memberName,
        ITypeSymbol memberType,
        AttributeData attr,
        Compilation compilation,
        List<Diagnostic> diagnostics,
        CancellationToken ct
    )
    {
        if (!IsValidParamsMember(memberSymbol))
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidParamsMember,
                    GetAttributeLocation(attr, ct),
                    memberName
                )
            );
            return null;
        }

        var typeName = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var values = ImmutableArray.CreateBuilder<string>();

        if (attr.ConstructorArguments.Length > 0)
        {
            var arg = attr.ConstructorArguments[0];
            if (arg.Kind == TypedConstantKind.Array)
            {
                // B of the Batch 1 review: `[Params(null)]` hands `null` to the
                // whole `params object[]`, so the argument is an array whose
                // `Values` is `default`. Enumerating that throws a
                // NullReferenceException, which the transform containment
                // reported as "PBGEN018 … failed: Object reference not set to an
                // instance of an object." — legal C#, unactionable error text.
                // There is nothing to enumerate: say so and name the member.
                if (arg.Values.IsDefault)
                {
                    diagnostics.Add(
                        Diagnostic.Create(
                            DiagnosticDescriptors.NullParamsArrayArgument,
                            GetAttributeLocation(attr, ct),
                            memberName
                        )
                    );
                    return null;
                }

                foreach (var element in arg.Values)
                {
                    if (!IsCompatibleWithTargetType(element, memberType, compilation))
                    {
                        diagnostics.Add(
                            Diagnostic.Create(
                                DiagnosticDescriptors.IncompatibleParamsValue,
                                GetAttributeLocation(attr, ct),
                                CSharpLiteralFormatter.DescribeConstant(element),
                                memberName,
                                memberType.ToDisplayString()
                            )
                        );
                        return null;
                    }

                    values.Add(CSharpLiteralFormatter.FormatConstant(element, memberType));
                }
            }
        }

        if (values.Count == 0)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.EmptyParamsValues,
                    GetAttributeLocation(attr, ct),
                    memberName
                )
            );
        }

        return new ParamsPropertyModel
        {
            Name = memberName,
            TypeFullName = typeName,
            FormattedValues = values.ToImmutable(),
        };
    }

    private static bool IsValidParamsMember(ISymbol memberSymbol)
    {
        return memberSymbol switch
        {
            IPropertySymbol property => property
                is { IsStatic: false, SetMethod.IsInitOnly: false },
            IFieldSymbol field => field is { IsStatic: false, IsReadOnly: false, IsConst: false },
            _ => false,
        };
    }

    /// <summary>
    /// Is this <c>[Params]</c> value assignable to the member?
    /// <para>
    /// Batch 1 · 1.5 (B3-H1): <c>Compilation.ClassifyConversion</c>
    /// answers a <b>type-level</b> question only, and C# has a second rule for
    /// constants: an <c>int</c> constant converts implicitly to
    /// <c>sbyte/byte/short/ushort/uint/ulong</c> when the <b>value</b> fits, and a
    /// non-negative <c>long</c> constant converts to <c>ulong</c>.
    /// Without it <c>[Params(1, 2)] public byte B</c> — the natural way to
    /// parameterise a buffer size — was an <b>Error</b> (PBGEN008) that blocked
    /// the consumer's build. The range check is what keeps the diagnostic
    /// meaningful: <c>[Params(300)] byte</c> is still an error.
    /// </para>
    /// </summary>
    private static bool IsCompatibleWithTargetType(
        TypedConstant constant,
        ITypeSymbol memberType,
        Compilation compilation
    )
    {
        if (constant.IsNull)
            return memberType.IsReferenceType
                || memberType.NullableAnnotation == NullableAnnotation.Annotated;

        if (constant.Type is null)
            return false;

        // Arrays are a value shape of their own (1.4): the conversion that
        // matters is the one from the array type, never from an element.
        var conversion = compilation.ClassifyConversion(constant.Type, memberType);
        if (conversion.Exists && (conversion.IsIdentity || conversion.IsImplicit))
            return true;

        return IsImplicitConstantExpressionConversion(constant, memberType);
    }

    /// <summary>
    /// The implicit <i>constant expression</i> conversion (C# spec §10.2.11):
    /// an <c>int</c> constant converts to a narrower integral type (and to
    /// <c>uint</c>/<c>ulong</c>) when its value is inside the target's range, and
    /// a non-negative <c>long</c> constant converts to <c>ulong</c>.
    /// <para>
    /// <b><c>char</c> is not in that list.</b> <c>char c = 65;</c> is <c>CS0266</c>,
    /// so an <c>int</c> argument cannot become a <c>char</c> value whatever its
    /// value is. Treating it as convertible (B-1 of
    /// <c>.review-2026-11/30-review-b1.md</c>) emitted
    /// <c>new char[] { 65, 66 }</c> — a file that does not compile — and reported
    /// no diagnostic at all, which is strictly worse than <c>main</c>.
    /// </para>
    /// </summary>
    private static bool IsImplicitConstantExpressionConversion(
        TypedConstant constant,
        ITypeSymbol memberType
    )
    {
        if (constant.Kind != TypedConstantKind.Primitive)
            return false;

        return constant.Value switch
        {
            int value => memberType.SpecialType switch
            {
                SpecialType.System_SByte => value is >= sbyte.MinValue and <= sbyte.MaxValue,
                SpecialType.System_Byte => value is >= byte.MinValue and <= byte.MaxValue,
                SpecialType.System_Int16 => value is >= short.MinValue and <= short.MaxValue,
                SpecialType.System_UInt16 => value is >= ushort.MinValue and <= ushort.MaxValue,
                SpecialType.System_UInt32 => value >= 0,
                SpecialType.System_UInt64 => value >= 0,
                _ => false,
            },

            // `ulong u = 1L;` is legal (N-1); `ulong u = -1L;` is not.
            long longValue => memberType.SpecialType == SpecialType.System_UInt64 && longValue >= 0,

            _ => false,
        };
    }

    /// <summary>
    /// Batch 1 · 1.6 (B3-L4): resolves the location through
    /// <c>GetSyntax(ct)</c> like every other symbol-to-syntax lookup in this
    /// file. <c>Locations</c> cannot observe cancellation, and for a type
    /// declared across many partial files this walk happens once per diagnostic.
    /// </summary>
    private static Location GetTypeLocation(INamedTypeSymbol typeSymbol, CancellationToken ct)
    {
        foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
        {
            var location = syntaxRef.GetSyntax(ct).GetLocation();
            if (location.IsInSource)
                return location;
        }

        return typeSymbol.Locations.FirstOrDefault(static location => location.IsInSource)
            ?? Location.None;
    }

    private static Location GetSymbolLocation(ISymbol? symbol)
    {
        if (symbol is null)
            return Location.None;

        return symbol.Locations.FirstOrDefault(static location => location.IsInSource)
            ?? Location.None;
    }

    /// <summary>
    /// One line, bounded length: exception messages become user-facing diagnostic
    /// text and RS1032 rejects multi-line messages.
    /// </summary>
    private static string DescribeException(Exception exception)
    {
        var message = exception.Message;
        for (var i = 0; i < message.Length; i++)
        {
            if (message[i] is '\r' or '\n')
            {
                message = message.Substring(0, i);
                break;
            }
        }

        const int maxLength = 240;
        return message.Length <= maxLength ? message : message.Substring(0, maxLength) + "…";
    }

    private static Location GetAttributeLocation(AttributeData attr, CancellationToken ct)
    {
        return attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? Location.None;
    }
}

/// <summary>
/// What one <c>[BenchmarkClass]</c> analysis produced: the emitter input, the
/// diagnostics it wants reported, and where the class is.
/// <para>
/// Batch 1 · 1.8 (B3-M1): this type is the <b>only</b> thing flowing out of the
/// transform step, so its equality decides whether the pipeline can cache at
/// all. Every model below it (<see cref="BenchmarkClassModel"/> and friends)
/// already implements value equality; this wrapper used to be a plain class,
/// i.e. reference equality, so <b>every</b> compilation change — including
/// editing a file that has nothing to do with benchmarks — re-ran the transform
/// output, <c>Emitter.Generate</c> and <c>AddSource</c>. Hence
/// <see cref="IEquatable{T}"/> here, and only here.
/// </para>
/// <para>
/// Diagnostics are compared structurally too
/// (<see cref="Diagnostic.Equals(Diagnostic)"/> does not do that: two
/// <see cref="Diagnostic"/> instances are never equal), which is what makes a
/// cached "class with PBGEN008" stay cached instead of being re-emitted on
/// every keystroke elsewhere in the project.
/// </para>
/// </summary>
internal sealed class GeneratorAnalysisResult(
    BenchmarkClassModel? model,
    ImmutableArray<Diagnostic> diagnostics,
    Location? location = null
) : IEquatable<GeneratorAnalysisResult>
{
    public BenchmarkClassModel? Model { get; } = model;

    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;

    /// <summary>
    /// Where the benchmark class lives, so an output-stage failure (Batch 1 ·
    /// 1.1) can still point the user at their own file instead of at
    /// "generated code". <see cref="Location.None"/> when unknown.
    /// </summary>
    public Location Location { get; } = location ?? Location.None;

    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);

    public bool Equals(GeneratorAnalysisResult? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return Equals(Model, other.Model)
            && LocationEquals(Location, other.Location)
            && DiagnosticsEqual(Diagnostics, other.Diagnostics);
    }

    public override bool Equals(object? obj) => Equals(obj as GeneratorAnalysisResult);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Model?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ Location.SourceSpan.GetHashCode();
            hash = (hash * 397) ^ (Location.SourceTree?.FilePath.GetHashCode() ?? 0);
            hash = (hash * 397) ^ Diagnostics.Length;
            return hash;
        }
    }

    /// <summary>
    /// <see cref="Location.Equals(object)"/> is not a value comparison (a
    /// <see cref="SourceLocation"/> holds the <see cref="SyntaxTree"/> instance),
    /// so compare the three things that actually describe a position. Using the
    /// file path rather than the tree instance is deliberate: re-parsing an
    /// untouched file produces an equal position, and that is exactly the case
    /// this comparison exists for.
    /// </summary>
    private static bool LocationEquals(Location left, Location right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left.Kind != right.Kind || left.SourceSpan != right.SourceSpan)
            return false;

        return string.Equals(
            left.SourceTree?.FilePath,
            right.SourceTree?.FilePath,
            StringComparison.Ordinal
        );
    }

    private static bool DiagnosticsEqual(
        ImmutableArray<Diagnostic> left,
        ImmutableArray<Diagnostic> right
    )
    {
        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            var l = left[i];
            var r = right[i];
            if (ReferenceEquals(l, r))
                continue;
            if (l.Id != r.Id || l.Severity != r.Severity)
                return false;
            if (!LocationEquals(l.Location, r.Location))
                return false;
            // Same id and same span can still carry different values ({"300"}
            // vs {"400"} on one member), and the message is user-facing text.
            if (
                !string.Equals(
                    l.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                    r.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.Ordinal
                )
            )
                return false;
        }

        return true;
    }
}
