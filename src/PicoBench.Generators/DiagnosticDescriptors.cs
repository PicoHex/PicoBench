namespace PicoBench.Generators;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor BenchmarkClassMustBePartial = new(
        id: "PBGEN001",
        title: "Benchmark class must be partial",
        messageFormat: "Benchmark class '{0}' must be declared partial",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor NoBenchmarkMethods = new(
        id: "PBGEN002",
        title: "No benchmark methods found",
        messageFormat: "Benchmark class '{0}' must declare at least one valid [Benchmark] method",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidBenchmarkMethod = new(
        id: "PBGEN003",
        title: "Invalid benchmark method",
        messageFormat: "Benchmark method '{0}' must be an instance, non-generic method with no parameters",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidLifecycleMethod = new(
        id: "PBGEN004",
        title: "Invalid lifecycle method",
        messageFormat: "{0} method '{1}' must be an instance, non-generic, parameterless method returning void, Task, or ValueTask",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor DuplicateBaseline = new(
        id: "PBGEN005",
        title: "Duplicate baseline benchmark",
        messageFormat: "Only one [Benchmark(Baseline = true)] method is allowed per benchmark class",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor DuplicateLifecycleMethod = new(
        id: "PBGEN006",
        title: "Duplicate lifecycle method",
        messageFormat: "Only one {0} method is allowed per benchmark class",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidParamsMember = new(
        id: "PBGEN007",
        title: "Invalid [Params] member",
        messageFormat: "[Params] member '{0}' must be an instance writable property or a non-readonly field",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor IncompatibleParamsValue = new(
        id: "PBGEN008",
        title: "Incompatible [Params] value",
        messageFormat: "[Params] value '{0}' is not compatible with member '{1}' of type '{2}'",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor AsyncVoidLifecycleMethod = new(
        id: "PBGEN009",
        title: "Async void lifecycle method",
        messageFormat: "{0} method '{1}' is async void. It will not be awaited. Use Task or ValueTask.",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ConflictingMethodRoles = new(
        id: "PBGEN010",
        title: "Conflicting benchmark method roles",
        messageFormat: "Method '{0}' cannot be both a [Benchmark] and a lifecycle method",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor AsyncVoidBenchmarkMethod = new(
        id: "PBGEN011",
        title: "Async void benchmark method",
        messageFormat: "Benchmark method '{0}' is async void; its work cannot be awaited or measured. Use Task or ValueTask.",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor GenericBenchmarkClass = new(
        id: "PBGEN012",
        title: "Generic benchmark class",
        messageFormat: "Benchmark class '{0}' must not be generic",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor NestedBenchmarkClass = new(
        id: "PBGEN013",
        title: "Nested benchmark class",
        messageFormat: "Benchmark class '{0}' must not be nested inside another type",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor RecordBenchmarkClass = new(
        id: "PBGEN014",
        title: "Record benchmark class",
        messageFormat: "Benchmark class '{0}' must be a plain partial class; record types are not supported",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor NonInstantiableBenchmarkClass = new(
        id: "PBGEN015",
        title: "Benchmark class is not instantiable",
        messageFormat: "Benchmark class '{0}' must be non-abstract and declare a public parameterless constructor",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    /// <summary>
    /// Batch 1 · 1.2 (B3-C3). An explicit interface implementation's symbol name
    /// is <c>IThing.Do</c>, which cannot be used to call the method — the
    /// generated code used to say <c>this.IThing.Do()</c> (CS1003/CS1061) with no
    /// diagnostic at all.
    /// </summary>
    /// <summary>
    /// Batch 1 · 1.6 (B3-H2). A <c>file</c> type is only visible in its own file,
    /// so the generated <c>partial</c> half cannot see its members: the consumer
    /// used to get <c>CS1061</c> on <c>.g.cs</c> for a class that is perfectly
    /// legal C#.
    /// </summary>
    public static readonly DiagnosticDescriptor FileLocalBenchmarkClass = new(
        id: "PBGEN020",
        title: "File-local benchmark class",
        messageFormat: "Benchmark class '{0}' is file-local; a file-local type cannot be extended from generated code - declare it as a non-file partial class",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ExplicitInterfaceImplementationNotSupported = new(
        id: "PBGEN019",
        title: "Explicit interface implementation cannot be benchmarked",
        messageFormat: "Explicit interface implementation '{0}' cannot be a [Benchmark] or lifecycle method; implement the member implicitly and call that method instead",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor EmptyParamsValues = new(
        id: "PBGEN016",
        title: "Empty [Params] value set",
        messageFormat: "[Params] member '{0}' has no values; the benchmark will not run",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    /// <summary>
    /// Batch 1 · 1.1 (B3-C2). One class failed, the rest of the project must not
    /// notice: this replaces the <c>CS8785</c> that used to throw the whole run's
    /// generated output away.
    /// </summary>
    public static readonly DiagnosticDescriptor SourceGenerationFailed = new(
        id: "PBGEN018",
        title: "Benchmark class source generation failed",
        messageFormat: "Source generation for benchmark class '{0}' failed: {1} (other benchmark classes are still generated)",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InheritedBenchmarkMembers = new(
        id: "PBGEN017",
        title: "Benchmark attributes on base types are ignored",
        messageFormat: "Base type '{0}' declares benchmark or lifecycle attributes; only members declared on the benchmark class are discovered",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    /// <summary>
    /// B of <c>.review-2026-11/30-review-b1.md</c> (§6 of the review and F6 of
    /// <c>31-verify-b1.md</c>). <c>[Params(null)]</c> binds <c>null</c> to the
    /// whole <c>params object[]</c> parameter, so the attribute argument arrives
    /// as an array whose <c>Values</c> is <c>default</c>. Enumerating it threw a
    /// <c>NullReferenceException</c> that the transform containment turned into
    /// <c>PBGEN018 "… failed: Object reference not set to an instance of an
    /// object."</c> — an Error the user cannot act on, for legal C#.
    /// <para>
    /// The important half is that this is <b>not</b> a generator fault: the
    /// argument shape is known and legal, so it gets its own message naming the
    /// member and saying what to write instead. The <c>Exception</c> text stays
    /// out of the diagnostic.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor NullParamsArrayArgument = new(
        id: "PBGEN021",
        title: "[Params] argument is a null array",
        messageFormat: "[Params] on member '{0}' received a null value array, so no values can be read; list the values explicitly, for example [Params(1, 2)]",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}
