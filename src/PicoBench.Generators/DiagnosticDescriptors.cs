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

    public static readonly DiagnosticDescriptor EmptyParamsValues = new(
        id: "PBGEN016",
        title: "Empty [Params] value set",
        messageFormat: "[Params] member '{0}' has no values; the benchmark will not run",
        category: "PicoBench.Generators",
        defaultSeverity: DiagnosticSeverity.Warning,
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
}
