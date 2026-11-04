# PicoBench

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

Una biblioteca de benchmarking ligera y sin dependencias para .NET con **dos API complementarias**: una API imperativa y una API basada en atributos, generada por código fuente, completamente **compatible con AOT**.

## Características

- **Cero Dependencias** - Implementación pura de .NET, no se requieren paquetes externos
- **Dos APIs** - Imperativa (`Benchmark.Run`) para pruebas ad hoc; basada en atributos (`[Benchmark]` + generador de código fuente) para suites estructuradas
- **Generador de Código Fuente Compatible con AOT** - El generador incremental emite llamadas directas a métodos sin reflexión en tiempo de ejecución
- **Multiplataforma** - Soporte completo para Windows, Linux y macOS
- **Cronometría Sub-Microsegundo** - Temporizaciones por operación basadas en `Stopwatch` con el coste por operación del propio arnés medido y descontado. Una carga de trabajo por debajo del límite de resolución de la ejecución se reporta como cota superior (`<limit`) y se marca `ResolutionLimited` — nunca como un número con precisión de nanosegundos
- **Límites Declarados** - `OverheadNs`, `ResolutionLimitNanoseconds`, `ResolutionLimited` y `Unmeasured` indican qué pudo y qué no pudo resolver una ejecución, de modo que un resultado a nivel de ruido no pueda leerse como una operación rápida
- **Seguimiento de GC** - Monitorea los recuentos de recolección Gen0/Gen1/Gen2 durante los benchmarks (se excluye setup/teardown)
- **Conteo de Ciclos de CPU** - Recuentos de ciclos por hilo en la ruta síncrona (Windows `QueryThreadCycleTime`; Linux `perf_event` cuando el SO permite acceso sin privilegios). Los benchmarks asíncronos leen un contador de todo el proceso, porque un contador por hilo no puede seguirse entre cambios de hilo, y se marcan `CpuCyclesAreApproximate` (`~` en los cuatro formatos). macOS no tiene contador de ciclos: `mach_absolute_time` es un proxy de *tiempo* monotónico, no ciclos, y se marca de la misma forma
- **Análisis Estadístico** - Media, Mediana, P90, P95, P99, Mínimo, Máximo, Desviación Estándar, error estándar y desviación estándar relativa
- **Múltiples Formatos de Salida** - Cuatro formateadores integrados (`IFormatter`) (Console, Markdown, HTML, CSV) más salida de resumen programática
- **Benchmarks Parametrizados** - Atributo `[Params]` con iteración automática de producto cartesiano
- **Soporte de Comparación** - Línea base vs candidato medidos en rondas intercaladas; el veredicto es la mediana de los cocientes emparejados por ronda y requiere tanto una banda muerta del 5% como un intervalo libre de distribución que excluya la paridad, de modo que el ruido entre rondas no se publique como una mejora
- **Configurable** - Preajustes Quick, Default y Precise, auto-calibración o configuración completamente personalizada
- **netstandard2.0** - Compatible con .NET Framework **4.7.2+** (`RuntimeInformation` se usa de forma incondicional, por lo que las versiones anteriores de .NET Framework no son compatibles), .NET Core 2.0+, .NET 5+

## Instalación

Referencia el paquete NuGet **PicoBench**. El generador de código fuente (`PicoBench.Generators`) se incluye automáticamente como un analizador - no se necesita referencia adicional.

```bash
dotnet add package PicoBench
```

## Inicio Rápido

### API Imperativa

```csharp
using PicoBench;

var result = Benchmark.Run("My Benchmark", () =>
{
    Thread.SpinWait(100);
});

Console.WriteLine($"Average: {result.Statistics.Avg:F1} ns/op");
```

### API Basada en Atributos (Generada por Código Fuente)

```csharp
using PicoBench;

var suite = BenchmarkRunner.Run<MyBenchmarks>();
Console.WriteLine(new PicoBench.Formatters.ConsoleFormatter().Format(suite));

[BenchmarkClass]
public partial class MyBenchmarks
{
    [Benchmark(Baseline = true)]
    public void Baseline() { /* ... */ }

    [Benchmark]
    public void Candidate() { /* ... */ }
}
```

> La clase **debe** ser `partial`. El generador de código fuente emite una implementación `IBenchmarkClass` en tiempo de compilación - sin reflexión, completamente segura para AOT.

> El uso inválido de atributos ahora produce diagnósticos del generador para errores comunes como clases no `partial`, baselines duplicadas, firmas de ciclo de vida no válidas y valores `[Params]` incompatibles. El generador también rechaza benchmarks `async void` (PBGEN011) y clases de benchmark genéricas, anidadas, record o no instanciables (PBGEN012-015), y advierte sobre `[Params]` vacíos (PBGEN016) y atributos de benchmark en tipos base (PBGEN017).

---

## Referencia de API Imperativa

### Benchmark Básico

```csharp
using PicoBench;
using PicoBench.Formatters;

var result = Benchmark.Run("SpinWait", () => Thread.SpinWait(100));
Console.WriteLine(new ConsoleFormatter().Format(result));
```

### Benchmark con Estado (Evitar Closures)

```csharp
var data = new byte[1024];
var result = Benchmark.Run("ArrayCopy", data, static d =>
{
    var copy = new byte[d.Length];
    Buffer.BlockCopy(d, 0, copy, 0, d.Length);
});
```

### Benchmarks con Alcance (Amigable con DI)

```csharp
var result = Benchmark.RunScoped("DbQuery",
    () => new MyDbContext(),
    static ctx => ctx.Users.FirstOrDefault()
);
// Se crea un nuevo alcance por muestra; el alcance se elimina después de cada muestra.
```

También está disponible una variante asíncrona:

```csharp
var result = await Benchmark.RunScopedAsync("DbQueryAsync",
    () => new MyDbContext(),
    static async ctx => await ctx.Users.FirstOrDefaultAsync()
);
// Variante asíncrona: un nuevo alcance por muestra, eliminado después de cada muestra.
```

### Comparar Dos Implementaciones

```csharp
var comparison = Benchmark.Compare(
    "String vs StringBuilder",
    "String Concat",  () => { var s = ""; for (int i = 0; i < 100; i++) s += "a"; },
    "StringBuilder",  () => { var sb = new StringBuilder(); for (int i = 0; i < 100; i++) sb.Append('a'); _ = sb.ToString(); }
);

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%) [{(comparison.Interval?.ToString() ?? "no interval")}] -> {comparison.Outcome}");
```

`Benchmark.Compare(name, baselineAction, candidateAction)` **no** ejecuta cada línea una sola vez. Intercala las dos líneas a lo largo de 10 rondas (qué línea va primero se alterna en cada ronda, para que ningún lado herede un proceso más caliente) y reporta la **mediana de los cocientes emparejados por ronda**. Qué significa eso para los números:

- **`Speedup`** — tiempo de la línea base ÷ tiempo del candidato; `> 1` significa que el candidato es más rápido. Una ronda en la que el candidato no midió nada no aporta ningún cociente; cuando ninguna ronda produce un cociente utilizable, la comparación reporta `NaN` (y `ImprovementPercent` es `NaN` con ella) en lugar de inventar un veredicto. Un `ComparisonResult` construido a partir de dos `BenchmarkResult` independientes sigue reportando el cociente de los dos promedios.
- **`Interval`** — intervalo del 95% libre de distribución de esa mediana (estadístico de orden 2.º/9.º de los 10 cocientes emparejados). `Interval.Contains(1.0)` significa que la paridad sigue siendo plausible. Una comparación construida a partir de dos resultados independientes **no tiene intervalo** (`Interval` es `null`): no hay rondas intercaladas que emparejar, así que no se puede decir nada sobre la dispersión.
- **`Outcome`** — el veredicto. `Faster` / `Slower` requieren que el cociente salga de una **banda muerta del 5%** *y* (cuando existe un intervalo) que el intervalo excluya 1. Todo lo que quede dentro es `Inconclusive`; un cociente que no se pudo calcular (no finito o no positivo) es `Unknown`, y también lo es una comparación con menos de seis rondas emparejadas utilizables (por debajo de eso el intervalo no puede cubrir la mediana con un 95% de confianza).
- **`IsFaster`** — `true` solo para `Outcome == Faster`, es decir, **más estrecho que antes**: una diferencia del 3% ahora devuelve `false` (`Inconclusive`), y una comparación sin rondas debe superar la misma banda muerta del 5%. Usa `Outcome` para la respuesta de tres vías y `IsFasterKnown` para distinguir "ninguna diferencia medible" de "no se pudo medir en absoluto".

Coste: una comparación directa ejecuta cada línea 10 veces, es decir, **20 `Run`s** — baja `SampleCount` / `WarmupIterations` para las ejecuciones A/B si eso resulta demasiado lento.

### Avanzado: Calentamiento, Configuración y Limpieza Separados

```csharp
var result = Benchmark.Run(
    name:     "Custom",
    action:   () => DoWork(),
    warmup:   () => DoWork(),      // null para omitir calentamiento
    config:   BenchmarkConfig.Precise,
    setup:    () => PrepareState(), // llamado antes de cada muestra (no cronometrado)
    teardown: () => CleanUp()       // llamado después de cada muestra (no cronometrado)
);
```

---

## Referencia de API Basada en Atributos

Decora una clase **partial** con `[BenchmarkClass]` y sus métodos/propiedades con los atributos siguientes. El generador de código fuente emite todo el código de conexión en tiempo de compilación.

### Atributos

| Atributo | Destino | Descripción |
|-----------|--------|-------------|
| `[BenchmarkClass]` | Clase | Marca la clase para generación de código. Propiedad opcional `Description`. |
| `[Benchmark]` | Método | Marca un método sin parámetros como benchmark. Establece `Baseline = true` para el método de referencia. Opcional `Description`. |
| `[Params(values)]` | Propiedad / Campo | Itera los valores constantes en tiempo de compilación dados. Múltiples propiedades `[Params]` producen un producto cartesiano. |
| `[GlobalSetup]` | Método | Llamado **una vez** por combinación de parámetros, antes de que se ejecuten los benchmarks. |
| `[GlobalCleanup]` | Método | Llamado **una vez** por combinación de parámetros, después de que se ejecuten los benchmarks. |
| `[IterationSetup]` | Método | Llamado antes de **cada muestra** (no cronometrado). |
| `[IterationCleanup]` | Método | Llamado después de **cada muestra** (no cronometrado). |

Los métodos `[Benchmark]` y de ciclo de vida deben ser de instancia, no genéricos y sin parámetros. Pueden devolver `void`, `Task`, `ValueTask`, `Task<T>` o `ValueTask<T>`; los métodos asíncronos se esperan con await y los valores devueltos se descartan. Los métodos `[Benchmark]` no pueden ser `async void` (PBGEN011). Los destinos de `[Params]` deben ser propiedades de instancia editables o campos de instancia no `readonly`. Las clases de benchmark deben ser no genéricas, no anidadas, no abstractas y declarar un constructor público sin parámetros.

### Ejemplo Completo

```csharp
using PicoBench;

[BenchmarkClass(Description = "Comparando estrategias de concatenación de cadenas")]
public partial class StringBenchmarks
{
    [Params(10, 100, 1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() { /* preparar datos para N actual */ }

    [GlobalCleanup]
    public void Cleanup() { /* liberar recursos */ }

    [IterationSetup]
    public void BeforeSample() { /* preparación por muestra */ }

    [Benchmark(Baseline = true)]
    public void StringConcat()
    {
        var s = string.Empty;
        for (var i = 0; i < N; i++) s += "a";
    }

    [Benchmark]
    public void StringBuilder()
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < N; i++) sb.Append('a');
        _ = sb.ToString();
    }
}
```

### Ejecución

```csharp
// Crear instancia internamente:
var suite = BenchmarkRunner.Run<StringBenchmarks>(BenchmarkConfig.Quick);

// O con una instancia preconfigurada:
var instance = new StringBenchmarks();
var suite2 = BenchmarkRunner.Run(instance, BenchmarkConfig.Quick);
```

## Benchmarks Asíncronos

Ambas APIs admiten trabajo asíncrono. Los métodos de benchmark asíncronos se esperan por iteración; los métodos de ciclo de vida pueden mezclar sincronía y asincronía en la misma clase.

```csharp
var result = await Benchmark.RunAsync("Http call", async () =>
{
    using var response = await httpClient.GetAsync(url);
});

var scoped = await Benchmark.RunScopedAsync("Scoped",
    () => container.CreateScope(),
    async scope => await scope.Service.DoWorkAsync());
```

- **Modos de medición** – `AsyncTimingMode.WallClock` (predeterminado) mide la duración total, incluidas las suspensiones await; `AsyncTimingMode.CpuOnly` mide `Process.TotalProcessorTime` y excluye la espera de E/S. El reloj de CPU avanza solo en tics del temporizador (normalmente 10–16 ms), por lo que la autocalibración eleva su presupuesto mínimo de muestra a la granularidad medida del reloj. En CpuOnly no se recopilan datos de GC.
- **Muestras no medibles** – en modo `CpuOnly` una carga de trabajo más corta que un tic del temporizador del SO se lee como exactamente cero tiempo de CPU. Esa muestra se cuenta en `Statistics.UnmeasuredSampleCount` (`Unmeasured` es `true`), su valor por operación pasa a ser **un cuanto de temporización — una cota superior —, no `0.000`**, y los formateadores imprimen las celdas por operación como `N/A`. Las estadísticas de una ejecución así no son mediciones del trabajo: léelas como "por debajo de la resolución del reloj de CPU". Los recuentos de iteraciones **no** se inflan de forma silenciosa para ocultar el problema — solo `AutoCalibrateIterations = true` (los preajustes `Quick`/`Precise`) calibra; `Default` usa el `IterationsPerSample` configurado sin cambios.
- **Atribución de GC** – los contadores de GC asíncronos se marcan como aproximados (`GcInfo.IsApproximate`). Las asignaciones de setup/teardown se excluyen en ambos modos.
- **Ciclos de CPU** – la ruta síncrona lee un contador por hilo (`QueryThreadCycleTime` en Windows, un `perf_event` abierto para el hilo de medición en Linux). Los benchmarks asíncronos leen un contador de todo el proceso, porque un contador por hilo no puede seguirse entre cambios de hilo: ese número incluye trabajo hecho por otros hilos y se marca `Statistics.CpuCyclesAreApproximate`, que se renderiza con un `~` en Console, Markdown, HTML y CSV. macOS no tiene contador de ciclos — `mach_absolute_time` es un proxy de *tiempo* monotónico en lugar de ciclos arquitectónicos, y se marca de la misma forma.
- **Cancelación** – `BenchmarkConfig.CancellationToken` solo se comprueba en los límites de muestra de los benchmarks asíncronos; los síncronos lo ignoran.
- **Clases mixtas** – un método `[Benchmark]` síncrono conserva la ruta de medición síncrona directa aunque la clase tenga otros miembros asíncronos.
- **Warmup** – las iteraciones de warmup solo invocan el delegado de warmup; `setup`/`teardown` y `[IterationSetup]`/`[IterationCleanup]` no se ejecutan durante el warmup. Las acciones de warmup deben ser autosuficientes.

---

## Fidelidad de Medición

PicoBench se ejecuta en proceso, por lo que la configuración del CLR afecta a los valores absolutos:

- Desactive el JIT por niveles: `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0` (o `COMPlus_*`).
- Para microbenchmarks de un solo hilo use GC de estación de trabajo: `DOTNET_gcServer=0`.
- Los recuentos de ciclos de CPU solo están disponibles donde el SO permite contadores sin privilegios (Windows `QueryThreadCycleTime`; Linux `perf_event` cuando `perf_event_paranoid` es 2 o menor). Las ejecuciones síncronas en Windows son por hilo; las ejecuciones asíncronas y macOS usan un contador de todo el proceso o, en macOS, un proxy de reloj monotónico en lugar de ciclos reales — ambos se marcan `CpuCyclesAreApproximate`. `EnvironmentInfo` y todos los formateadores informan qué fuente se usó.
- `BenchmarkConfig.BoostPriorities` (predeterminado `true`) eleva la prioridad del proceso y del hilo solo durante la ejecución y restaura los valores anteriores al terminar.

### Qué pueden y qué no pueden resolver los números

Cada ejecución mide el coste que el propio arnés añade por operación con el mismo recuento de iteraciones y lo descuenta, y luego publica el valor más pequeño que aún puede resolver:

| Campo | Significado |
|---|---|
| `Statistics.OverheadNs` | coste por operación del arnés (despacho de delegados, contabilidad del bucle, lecturas de reloj y de contadores) que se midió y se descontó. `0` para ejecuciones asíncronas, cuyo sobrecoste no se descuenta |
| `Statistics.ResolutionLimitNanoseconds` | valor por operación más pequeño que esta ejecución puede resolver: un tic de reloj repartido entre las iteraciones, o la dispersión entre ejecuciones del sobrecoste descontado, el que sea mayor |
| `Statistics.ResolutionLimited` | `Avg` está en ese límite o por debajo, o al menos una muestra no pudo medirse en absoluto. Los formateadores imprimen las celdas por operación como cota superior (`<limit` en Console y CSV, `[below resolution]` en Markdown y HTML) en lugar de un número |
| `Statistics.Unmeasured` / `UnmeasuredSampleCount` | cuántas muestras no pudo avanzar la fuente de temporización (un tic cero en `CpuOnly`). Esas celdas imprimen `N/A`, nunca `0.000` |

Por lo tanto, la lectura honesta de un resultado sub-nanosegundo es **"por debajo de `<limit` ns"**, no "0.02 ns": el valor no está medido, está acotado, y la cota cubre la cuantización del reloj y la dispersión del sobrecoste descontado (el residuo se recorta a cero, así que es un suelo de lo que el arnés puede resolver, no una prueba de que el coste real sea menor). Merece la pena recordar dos asimetrías más: la deducción del sobrecoste del arnés solo está implementada para benchmarks síncronos (los valores por operación asíncronos siguen cargando su coste de envoltorio por muestra), y los bytes de GC no se miden en absoluto — solo los recuentos de recolección, por proceso, aproximados en la ruta asíncrona.

---

---

## Configuración

### Preajustes

| Preajuste | Calentamiento | Muestras | Iteraciones base/Muestra | Auto-calibración | Caso de Uso |
|--------|--------|---------|--------------------------|------------------|----------|
| `Quick` | 100 | 10 | 1,000 | Sí | Iteración rápida / CI |
| `Default` | 1,000 | 100 | 10,000 | No | Benchmarking general |
| `Precise` | 5,000 | 200 | 50,000 | Sí | Mediciones finales |

### Configuración Personalizada

```csharp
var config = new BenchmarkConfig
{
    WarmupIterations    = 500,
    SampleCount         = 50,
    IterationsPerSample = 5000,
    RetainSamples       = true,  // Conservar datos brutos de TimingSample
    AutoCalibrateIterations = true,
    MinSampleTime       = TimeSpan.FromMilliseconds(0.5),
    MaxAutoIterationsPerSample = 1_000_000,
    ForceGcBeforeBenchmark = true,  // false skips the pre-benchmark full GC
};

var result = Benchmark.Run("Test", action, config);
```

Cuando la auto-calibración está habilitada, PicoBench incrementa `IterationsPerSample` hasta alcanzar un presupuesto mínimo de tiempo por muestra o hasta llegar a `MaxAutoIterationsPerSample`. Esto es especialmente útil para operaciones ultrarrápidas dominadas por el ruido del temporizador. Establece `ForceGcBeforeBenchmark = false` para omitir la GC completa forzada que precede a la fase de recolección; útil al ejecutar muchos benchmarks y combinaciones de parámetros.

---

## Formateadores de Salida

Cinco formateadores incorporados implementan `IFormatter`:

```csharp
using PicoBench.Formatters;

var console  = new ConsoleFormatter();     // Tablas de consola con dibujo de cajas
var markdown = new MarkdownFormatter();    // Markdown amigable con GitHub
var html     = new HtmlFormatter();        // Informe HTML estilizado
var csv      = new CsvFormatter();         // CSV para análisis de datos

// Ayudante estático para resúmenes de comparación:
Console.WriteLine(SummaryFormatter.Format(suite.Comparisons));
```

Las salidas de Console, Markdown, HTML y CSV incluyen metadatos orientados a la precisión, como error estándar, desviación estándar relativa y notas sobre el contador de CPU cuando están disponibles.

### Objetivos de Formateo

```csharp
formatter.Format(result);               // BenchmarkResult único
formatter.Format(results);              // IEnumerable<BenchmarkResult>
formatter.Format(comparison);           // ComparisonResult único
formatter.Format(comparisons);          // IEnumerable<ComparisonResult>
formatter.Format(suite);                // BenchmarkSuite completo
```

### Opciones del Formateador

```csharp
var options = new FormatterOptions
{
    IncludeEnvironment   = true,
    IncludeTimestamp      = true,
    IncludeGcInfo         = true,
    IncludeCpuCycles      = true,
    IncludePercentiles    = true,
    TimeDecimalPlaces     = 1,
    SpeedupDecimalPlaces  = 2,
    BaselineLabel         = "Old",
    CandidateLabel        = "New"
    OutputDirectory       = "results", // Used by WriteToFile methods
};

var formatter = new ConsoleFormatter(options);
// También disponibles: FormatterOptions.Default, .Compact, .Minimal
```

### Guardar Resultados

```csharp
var dir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(dir);

File.WriteAllText(Path.Combine(dir, "results.md"),   new MarkdownFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.html"), new HtmlFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.csv"),  new CsvFormatter().Format(suite));
```

### Contrato de Columnas del CSV

`CsvFormatter` escribe una fila de encabezado y luego filas de datos; una suite añade líneas de comentario `#` (`# Suite:`, `# Environment:`, `# CpuCounter:`, `# CpuCounterNote:`, `# GcInfoNote:`, `# Timestamp:`, `# Duration:`) y dos secciones, `# Results` y `# Comparisons`, **cada una con su propia fila de encabezado**. Por lo tanto, un archivo tiene más de una línea de encabezado — `pandas.read_csv(path, comment='#')` omite los comentarios, pero debes separar las dos secciones tú mismo.

Columnas de resultados, en este orden (opciones predeterminadas; `IncludePercentiles`, `IncludeGcInfo`, `IncludeCpuCycles` e `IncludeTimestamp` eliminan sus columnas cuando valen `false`):

| # | Columna | Significado |
|---|---|---|
| 1 | `Name` | nombre del benchmark (entre comillas si contiene `,` `"` o un salto de línea; un `=`/`+`/`-`/`@` inicial se prefija con `'`) |
| 2 | `Category` | categoría, vacía cuando no está definida |
| 3 | `Avg_ns` | nanosegundos por operación — tres estados de celda, ver abajo |
| 4 | `P50_ns` | mediana de nanosegundos por operación (los mismos tres estados) |
| 5-12 | `P90_ns`, `P95_ns`, `P99_ns`, `Min_ns`, `Max_ns`, `StdDev_ns`, `StdErr_ns`, `RSD_pct` | percentiles y dispersión; `RSD_pct` es un porcentaje simple, pero los otros siete usan los estados de celda por operación |
| 13 | `CpuCycles` | ciclos por operación; un prefijo `~` marca un contador de todo el proceso o un proxy de reloj (`CpuCyclesAreApproximate`), no ciclos de hilo |
| 14-16 | `GC0`, `GC1`, `GC2` | recuentos de recolección, o `N/A` cuando no se recopiló GC |
| 17 | `GCApproximate` | `1` cuando los contadores son aproximados (ruta asíncrona), `0` cuando son exactos, `N/A` cuando no se recopilaron |
| 18 | `SampleCount` | número de muestras (entero) |
| 19 | `IterationsPerSample` | iteraciones por muestra (entero) |
| 20 | `Timestamp` | ISO-8601 round-trip (`o`, cultura invariante) |

Estados de celda de una columna por operación:

| Celda | Significado |
|---|---|
| `12.5` | un valor medido en nanosegundos (cultura invariante, formato round-trip) |
| `<0.02` | **cota superior**: la ejecución no pudo resolver el valor por encima de `Statistics.ResolutionLimitNanoseconds`. El número que sigue a `<` es ese límite, y no es un valor medido |
| `N/A` | no medido (`Statistics.Unmeasured`, un delta de reloj cero) o no recopilado (columnas de GC) |

Dos consecuencias para los consumidores: por culpa de `<0.02`, una columna por operación **no** tiene garantizado ser numérica, así que lectores como pandas infieren `object` y hay que manejar explícitamente las filas de cota; y `GCApproximate` se insertó en la posición 17, entre `GC2` y `SampleCount`, así que **los lectores posicionales deben desplazar `SampleCount`/`IterationsPerSample` en uno** (los lectores basados en nombres no se ven afectados).

Columnas de comparaciones: `TestCase`, `Provider`, `Avg_ns`, `Speedup`, `P50_ns`, `P90_ns`, `P99_ns`, `StdErr_ns`, `RSD_pct`, `CpuCycles`, `GC0`, `GC1`, `GC2`, `GCApproximate`. Cada comparación escribe dos filas — primero el candidato, luego la línea base — y `Speedup` solo se rellena en la fila del candidato. Actualmente las celdas de comparación pasan por el formateador de números simple, así que `N/A`/`<limit` de la tabla de resultados no aparecen allí.

> Una línea de encabezado `# Schema:` legible por máquina que declare estos estados está registrada como un seguimiento separado del Batch 0. Hasta que llegue, la tabla anterior es el contrato, y un archivo de suite todavía no identifica su versión de esquema.

---

## Modelo de Resultados

| Tipo | Descripción |
|------|-------------|
| `BenchmarkResult` | Nombre, Categoría, Etiquetas, Estadísticas, Muestras, IteracionesPorMuestra, RecuentoDeMuestras, Marca de tiempo |
| `ComparisonResult` | Nombre, Categoría, Etiquetas, Línea base, Candidato, Aceleración, PorcentajeDeMejora, **Outcome**, **IsFasterKnown**, **Interval**, EsMásRápido |
| `BenchmarkSuite` | Nombre, Descripción, Resultados, Comparaciones, Entorno, Duración, Marca de tiempo |
| `ComparisonOutcome` | Enum: `Unknown` (no se pudo calcular ningún cociente), `Inconclusive` (dentro de la banda muerta del 5%, o el intervalo aún contiene la paridad), `Faster`, `Slower` |
| `ComparisonInterval` | Low, High, Contains(value), ToString() — intervalo del 95% libre de distribución de la mediana del cociente emparejado |
| `Statistics` | Promedio, P50, P90, P95, P99, Mínimo, Máximo, DesviaciónEstándar, StandardError, RelativeStdDevPercent, CiclosDeCpuPorOp, **CpuCyclesAreApproximate**, **OverheadNs**, **ResolutionLimitNanoseconds**, **ResolutionLimited**, **Unmeasured**, **UnmeasuredSampleCount**, GcInfo, HasHighVariance |
| `TimingSample` | NanosegundosTranscurridos, MilisegundosTranscurridos, TicksTranscurridos, CiclosDeCpu, **CpuCyclesAreApproximate**, GcInfo |
| `GcInfo` | Gen0, Gen1, Gen2, Total, EsCero, IsApproximate |
| `EnvironmentInfo` | SO, Arquitectura, VersiónDelRuntime, RecuentoDeProcesadores, Modo de ejecución, Configuración, tipo / disponibilidad / significado del contador de CPU, etiquetas personalizadas |

Semántica de los miembros añadidos por el trabajo de fidelidad de medición:

| Miembro | Contrato |
|---|---|
| `ComparisonResult.Outcome` | Veredicto de tres vías; `Faster`/`Slower` necesitan la banda muerta del 5% **y** un intervalo que excluya 1 |
| `ComparisonResult.IsFasterKnown` | `Outcome != Unknown` — "las dos líneas se compararon" (pueden seguir siendo indistinguibles) |
| `ComparisonResult.IsFaster` | Solo `Outcome == Faster`. Más estrecho que el antiguo `Speedup > 1.0`: una diferencia dentro de la banda muerta ya no es "más rápido" |
| `ComparisonResult.Interval` | `ComparisonInterval?`; `null` cuando la comparación se construyó a partir de dos resultados independientes en lugar de rondas intercaladas |
| `Statistics.ResolutionLimited` | El valor por operación es una cota superior (`<limit`), no una medición |
| `Statistics.UnmeasuredSampleCount` / `Unmeasured` | Muestras para las que la fuente de temporización no pudo avanzar; sus celdas por operación son `N/A` |
| `Statistics.CpuCyclesAreApproximate` | `CpuCyclesPerOp` es de todo el proceso (asíncrono) o un proxy de reloj monotónico (macOS), no ciclos de hilo: los formateadores lo prefijan con `~` |
| `TimingSample.CpuCyclesAreApproximate` | La misma marca a nivel de muestra; `Statistics` la agrega con OR lógico |

---

## Arquitectura

```
src/
+-- PicoBench/                        # Biblioteca principal (netstandard2.0)
|   +-- Benchmark.cs                   # API imperativa (Run, Compare, RunScoped)
|   +-- BenchmarkRunner.cs             # Punto de entrada basado en atributos (Run<T>)
|   +-- BenchmarkConfig.cs             # Configuración con preajustes
|   +-- Attributes.cs                  # 7 atributos de benchmark
|   +-- IBenchmarkClass.cs             # Interfaz emitida por el generador
|   +-- Runner.cs                      # Flujo de temporización de bajo nivel y creación de muestras
|   +-- Runner.Gc.cs                   # Línea base y delta de GC
|   +-- Runner.Cpu.cs                  # Implementación del contador de CPU específica de la plataforma
|   +-- StatisticsCalculator.cs        # Cálculo de percentiles / estadísticas
|   +-- Models.cs                      # Tipos de resultados
|   +-- Formatters/
|       +-- IFormatter.cs              # IFormatter, FormatterOptions & FormatterBase
|       +-- ConsoleFormatter.cs        # Tablas de consola con dibujo de cajas
|       +-- MarkdownFormatter.cs       # Tablas Markdown de GitHub
|       +-- HtmlFormatter.cs           # Informes HTML estilizados
|       +-- CsvFormatter.cs            # Exportación CSV
|       +-- SummaryFormatter.cs        # Resumen de ganancias/pérdidas
|
+-- PicoBench.Generators/            # Generador de código fuente (netstandard2.0)
    +-- BenchmarkGenerator.cs          # Punto de entrada IIncrementalGenerator
    +-- BenchmarkClassAnalyzer.cs      # Análisis y diagnósticos de Roslyn
    +-- CSharpLiteralFormatter.cs      # Formateo de literales C# para parámetros emitidos
    +-- DiagnosticDescriptors.cs       # Definiciones de diagnósticos del generador
    +-- Emitter.cs                     # Emisor de código C# (seguro para AOT)
    +-- Models.cs                      # Modelos de análisis Roslyn
```

---

## Características Específicas de Plataforma

| Característica | Windows | Linux | macOS |
|---------|---------|-------|-------|
| Temporización | Stopwatch | Stopwatch | Stopwatch |
| Seguimiento de GC (Gen0/1/2) | Sí | Sí | Sí |
| Conteo de ciclos de CPU, síncrono | `QueryThreadCycleTime` (por hilo) | `perf_event_open` por hilo (cuando `perf_event_paranoid` ≤ 2) | `mach_absolute_time`, un **proxy de tiempo** — marcado `~` |
| Conteo de ciclos de CPU, asíncrono | `QueryProcessCycleTime` (todo el proceso) — marcado `~` | `perf_event` de todo el proceso — marcado `~` | `mach_absolute_time` — marcado `~` |
| Aumento de prioridad de proceso | Sí (con alcance, restaurado) | Sí (con alcance, restaurado) | Sí (con alcance, restaurado) |

En macOS, el contador de CPU exportado es un proxy de *tiempo* monotónico de alta resolución y no ciclos arquitectónicos: el valor avanza con el tiempo real, no con las instrucciones ejecutadas, y por eso se marca `CpuCyclesAreApproximate` y se imprime con `~`. La misma marca se usa para la ruta asíncrona, cuyo contador de todo el proceso incluye trabajo hecho por otros hilos. `EnvironmentInfo` y la salida de los formateadores exponen ambas distinciones de forma explícita.

---

## Ejemplos

| Ejemplo | Estilo de API | Descripción |
|--------|-----------|-------------|
| `StringVsStringBuilder` | Imperativa | Compara `string +=`, `StringBuilder` y `StringBuilder` con capacidad |
| `AttributeBased` | Basada en atributos | Misma comparación usando `[Benchmark]`, `[Params]` y el generador de código fuente |
| `CollectionBenchmarks` | Basada en atributos | Búsqueda List vs Dictionary vs HashSet - muestra cada atributo |

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
dotnet run --project samples/AttributeBased -c Release
dotnet run --project samples/CollectionBenchmarks -c Release
```

---

## Comparación con BenchmarkDotNet

| Característica | PicoBench | BenchmarkDotNet |
|---------|-----------|----------------|
| Dependencias | 0 | Muchas |
| Tamaño del paquete | Pequeño | Grande |
| Framework objetivo | netstandard2.0 | net6.0+ |
| Soporte AOT | Generador de código fuente | Basado en reflexión |
| API de atributos | `[Benchmark]`, `[Params]` | `[Benchmark]`, `[Params]` |
| Tiempo de configuración | Instantáneo | Segundos |
| Formatos de salida | 5 | 10+ |
| Profundidad estadística | Buena | Extensa |
| Caso de uso | Pruebas A/B rápidas, CI, aplicaciones AOT | Análisis detallado, publicaciones |

---

## Licencia

Licencia MIT - consulte el archivo [LICENSE](LICENSE) para más detalles.

## Compilación y Publicación

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

Las versiones siguen la regla PicoHex `<año>.<x>.<y>`: **x** aumenta cuando cambió la API pública, **y** cuando no. La decisión proviene de la baseline de API comprometida en `api/PicoBench.public.txt`, que siempre contiene la superficie del último release.

```bash
# ver el delta de API y la próxima versión
pwsh ./scripts/release.ps1 -DryRun

# cortar el release: refrescar api/, commit chore(release): v<version>, etiquetar, push
pwsh ./scripts/release.ps1 -Push
```

La etiqueta empujada activa la pipeline de release de GitHub Actions, que ejecuta las pruebas, empaqueta cada paquete con la versión de la etiqueta y publica en [NuGet.org](https://www.nuget.org/packages/PicoBench). Para cubrir la brecha de indexación de los repositorios hermanos, la misma versión puede empaquetarse en el feed local declarado en `NuGet.config`:

```bash
dotnet pack src/PicoBench/PicoBench.csproj -c Release -o artifacts/nupkg -p:Version=<version>
```

## Contribuir

1. Bifurca el repositorio
2. Crea una rama de características
3. Realiza cambios con pruebas
4. Envía una solicitud de extracción
