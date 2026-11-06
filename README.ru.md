# PicoBench

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoBench/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoBench.svg)](https://www.nuget.org/packages/PicoBench)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Легковесная библиотека для бенчмаркинга в .NET с **двумя взаимодополняющими API**: императивным API и API на основе атрибутов, генерируемым из исходного кода, полностью **совместимым с AOT**. Единственная ссылка NuGet — BCL polyfill (`System.Threading.Tasks.Extensions`) для `ValueTask<T>` на netstandard2.0.

## Особенности

- **Нет сторонних зависимостей** — только один BCL polyfill (`System.Threading.Tasks.Extensions`) для `ValueTask<T>` на netstandard2.0. Внешних пакетов нет.
- **Два API** - Императивный (`Benchmark.Run`) для ad-hoc тестов; на основе атрибутов (`[Benchmark]` + генератор исходного кода) для структурированных наборов
- **Генератор исходного кода, совместимый с AOT** - Инкрементальный генератор создает прямые вызовы методов без рефлексии во время выполнения
- **Кроссплатформенность** - Полная поддержка Windows, Linux и macOS
- **Субмикросекундное измерение времени** - Пооперационное измерение на основе `Stopwatch`, при котором собственные затраты харнесса на операцию измеряются и вычитаются. Нагрузка ниже предела разрешения запуска сообщается как верхняя граница (`<limit`) и помечается `ResolutionLimited` — никогда как точное до наносекунды число
- **Раскрытые пределы** - `OverheadNs`, `ResolutionLimitNanoseconds`, `ResolutionLimited` и `Unmeasured` показывают, что запуск смог и чего не смог разрешить, поэтому результат на уровне шума нельзя прочитать как быструю операцию
- **Отслеживание GC** - Мониторинг количества сборок Gen0/Gen1/Gen2 во время бенчмарков (без setup/teardown)
- **Подсчёт циклов CPU** - Циклы с областью потока на синхронном пути (Windows `QueryThreadCycleTime`; Linux `perf_event`, когда ОС разрешает непривилегированный доступ). Асинхронные бенчмарки читают счётчик всего процесса, поскольку поточный счётчик невозможно отследить при переходах между потоками, и помечаются `CpuCyclesAreApproximate` (`~` во всех четырёх форматах). На macOS счётчика циклов нет: `mach_absolute_time` — монотонный прокси *времени*, а не циклы, и помечается так же
- **Статистический анализ** - `Avg`, `P50`, `P90`, `P95`, `P99`, `Min`, `Max`, `StdDev`, `StandardError` и относительное стандартное отклонение (`RSD`). Перцентили вычисляются линейной интерполяцией между двумя соседними отсчётами (метод, который статистические пакеты называют *type 7* — `PERCENTILE.INC` в Excel и значение по умолчанию в NumPy), а дисперсия — выборочная (`N-1`); при числе отсчётов меньше примерно 20 значение P99 близко к максимуму, поэтому столбцы хвоста не описывают устойчивый хвост.
- **Несколько форматов вывода** - Четыре встроенных форматтера `IFormatter` (Console, Markdown, HTML, CSV) и программная сводка
- **Параметризованные бенчмарки** - Атрибут `[Params]` с автоматической итерацией декартова произведения
- **Поддержка сравнений** - Базовый вариант и кандидат измеряются в чередующихся раундах; вердикт — медиана парных отношений по раундам, и для него нужны и мёртвая зона 5%, и свободный от распределения интервал, исключающий паритет, поэтому шум от раунда к раунду не публикуется как улучшение
- **Настраиваемость** - Предустановки Quick, Default и Precise, автокалибровка или полностью пользовательская конфигурация
- **netstandard2.0** - Совместимо с .NET Framework **4.7.2+** (`RuntimeInformation` используется безусловно, поэтому более ранние версии .NET Framework не поддерживаются), .NET Core 2.0+, .NET 5+

## Установка

Добавьте ссылку на пакет NuGet **PicoBench**. Генератор исходного кода (`PicoBench.Generators`) автоматически включается как анализатор - дополнительная ссылка не требуется.

```bash
dotnet add package PicoBench
```

## Быстрый старт

### Императивный API

```csharp
using PicoBench;

var result = Benchmark.Run("My Benchmark", () =>
{
    Thread.SpinWait(100);
});

Console.WriteLine($"Average: {result.Statistics.Avg:F1} ns/op");
```

### API на основе атрибутов (генерируемый из исходного кода)

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

> Класс **должен** быть `partial`. Генератор исходного кода создает реализацию `IBenchmarkClass` во время компиляции - без рефлексии, полностью безопасно для AOT.

> Некорректное использование атрибутов теперь дает диагностику генератора для типичных ошибок: класс не `partial`, дублирующиеся baseline, неверные сигнатуры lifecycle-методов и несовместимые значения `[Params]`. Генератор также отклоняет `async void`-бенчмарки (PBGEN011) и обобщённые, вложенные, record-или неинстанцируемые классы бенчмарков (PBGEN012-015), предупреждает о пустых `[Params]` (PBGEN016) и атрибутах бенчмарков в базовых типах (PBGEN017).

---

## Справочник по императивному API

### Базовый бенчмарк

```csharp
using PicoBench;
using PicoBench.Formatters;

var result = Benchmark.Run("SpinWait", () => Thread.SpinWait(100));
Console.WriteLine(new ConsoleFormatter().Format(result));
```

### Бенчмарк с состоянием (избегание замыканий)

```csharp
var data = new byte[1024];
var result = Benchmark.Run("ArrayCopy", data, static d =>
{
    var copy = new byte[d.Length];
    Buffer.BlockCopy(d, 0, copy, 0, d.Length);
});
```

### Бенчмарки с областью видимости (удобно для DI)

```csharp
var result = Benchmark.RunScoped("DbQuery",
    () => new MyDbContext(),
    static ctx => ctx.Users.FirstOrDefault()
);
// Новая область создается для каждого образца; область удаляется после каждого образца.
```

Также доступен асинхронный вариант:

```csharp
var result = await Benchmark.RunScopedAsync("DbQueryAsync",
    () => new MyDbContext(),
    static async ctx => await ctx.Users.FirstOrDefaultAsync()
);
// Асинхронный вариант: новая область на образец, удаляется после каждого образца.
```

### Сравнение двух реализаций

```csharp
var comparison = Benchmark.Compare(
    "String vs StringBuilder",
    "String Concat",  () => { var s = ""; for (int i = 0; i < 100; i++) s += "a"; },
    "StringBuilder",  () => { var sb = new StringBuilder(); for (int i = 0; i < 100; i++) sb.Append('a'); _ = sb.ToString(); }
);

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%) [{(comparison.Interval?.ToString() ?? "no interval")}] -> {comparison.Outcome}");
```

- **`Speedup`** - время базового варианта ÷ время кандидата; `> 1` означает, что кандидат быстрее. Раунд, в котором кандидат ничего не измерил, не даёт отношения вовсе; когда ни один раунд не даёт пригодного отношения, сравнение сообщает `NaN` (и `ImprovementPercent` вместе с ним — `NaN`) вместо выдуманного вердикта. `ComparisonResult`, построенный из двух независимых `BenchmarkResult`, всё равно сообщает отношение двух средних. Раунд 0/0 — это ровно такой случай: `NaN`, а не `1.0` (сравнение, построенное из двух независимых нулевых результатов, сохраняет прежнее `1.0`).
- **`Interval`** - свободный от распределения 95%-й интервал этой медианы (2-я/9-я порядковая статистика из 10 парных отношений). `Interval.Contains(1.0)` означает, что паритет всё ещё правдоподобен. Сравнение, построенное из двух независимых результатов, **не имеет интервала** (`Interval` равен `null`) — парных чередующихся раундов нет, поэтому о разбросе сказать нечего.
- **`Outcome`** - вердикт. `Faster` / `Slower` требуют, чтобы отношение вышло за **мёртвую зону 5%** *и* (когда интервал существует) чтобы интервал исключал 1. Всё внутри — `Inconclusive`; отношение, которое не удалось вычислить (не конечное или не положительное), — `Unknown`, как и сравнение с менее чем шестью пригодными парными раундами (ниже этого интервал не может покрыть медиану с 95%-й уверенностью).
- **`IsFaster`** - `true` только при `Outcome == Faster`, то есть **более узкое, чем раньше**: разница в 3% теперь возвращает `false` (`Inconclusive`), а сравнение без раундов должно пройти ту же мёртвую зону 5%. Используйте `Outcome` для трёхзначного ответа и `IsFasterKnown`, чтобы отличить «нет измеримой разницы» от «измерить вообще не удалось».

Стоимость: прямое сравнение прогоняет каждую полосу 10 раз, то есть **20 `Run`** — уменьшите `SampleCount` / `WarmupIterations` для A/B-запусков, если это слишком медленно.

### Продвинутый: отдельные разогрев, настройка и очистка

```csharp
var result = Benchmark.Run(
    name:     "Custom",
    action:   () => DoWork(),
    warmup:   () => DoWork(),      // null чтобы пропустить разогрев
    config:   BenchmarkConfig.Precise,
    setup:    () => PrepareState(), // вызывается перед каждым образцом (не учитывается во времени)
    teardown: () => CleanUp()       // вызывается после каждого образца (не учитывается во времени)
);
```

---

## Справочник по API на основе атрибутов

Пометьте класс **partial** атрибутом `[BenchmarkClass]` и его методы/свойства атрибутами ниже. Генератор исходного кода создает весь связующий код во время компиляции.

### Атрибуты

| Атрибут | Цель | Описание |
|-----------|--------|-------------|
| `[BenchmarkClass]` | Класс | Помечает класс для генерации кода. Необязательное свойство `Description`. |
| `[Benchmark]` | Метод | Помечает метод без параметров как бенчмарк. Установите `Baseline = true` для эталонного метода. Необязательное `Description`. |
| `[Params(values)]` | Свойство / Поле | Итерирует заданные значения констант времени компиляции. Несколько свойств `[Params]` создают декартово произведение. |
| `[GlobalSetup]` | Метод | Вызывается **один раз** для каждой комбинации параметров, перед запуском бенчмарков. |
| `[GlobalCleanup]` | Метод | Вызывается **один раз** для каждой комбинации параметров, после запуска бенчмарков. |
| `[IterationSetup]` | Метод | Вызывается перед **каждым образцом** (не учитывается во времени). |
| `[IterationCleanup]` | Метод | Вызывается после **каждого образца** (не учитывается во времени). |

Методы `[Benchmark]` и lifecycle-методы должны быть методами экземпляра, не быть обобщенными и не иметь параметров. Они могут возвращать `void`, `Task`, `ValueTask`, `Task<T>` или `ValueTask<T>`; асинхронные методы ожидаются (await), а возвращаемые значения отбрасываются. Методы `[Benchmark]` не могут быть `async void` (PBGEN011). Целями `[Params]` должны быть записываемые свойства экземпляра или поля экземпляра без `readonly`. Классы бенчмарков должны быть необобщёнными, не вложенными, не абстрактными и иметь public-конструктор без параметров.

### Полный пример

```csharp
using PicoBench;

[BenchmarkClass(Description = "Сравнение стратегий конкатенации строк")]
public partial class StringBenchmarks
{
    [Params(10, 100, 1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() { /* подготовка данных для текущего N */ }

    [GlobalCleanup]
    public void Cleanup() { /* освобождение ресурсов */ }

    [IterationSetup]
    public void BeforeSample() { /* подготовка для каждого образца */ }

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

### Запуск

```csharp
// Создать экземпляр внутри:
var suite = BenchmarkRunner.Run<StringBenchmarks>(BenchmarkConfig.Quick);

// Или с предварительно настроенным экземпляром:
var instance = new StringBenchmarks();
var suite2 = BenchmarkRunner.Run(instance, BenchmarkConfig.Quick);
```

## Асинхронные бенчмарки

Оба API поддерживают асинхронную работу. Асинхронные методы бенчмарков ожидаются на каждой итерации; в одном классе можно сочетать синхронные и асинхронные lifecycle-методы.

```csharp
// Императивный
var result = await Benchmark.RunAsync("Http call", async () =>
{
    using var response = await httpClient.GetAsync(url);
});

// Асинхронные setup/teardown на образец
var result2 = await Benchmark.RunAsync(
    "With lifecycle",
    action: async () => await DoWorkAsync(),
    warmup: async () => await DoWorkAsync(),
    config: BenchmarkConfig.Quick,
    setup: async () => await ResetStateAsync(),
    teardown: async () => await DisposeStateAsync());

// С состоянием, асинхронно (без аллокации замыкания; без перегрузки с setup/teardown)
var result3 = await Benchmark.RunAsync("Stateful", state, async s => await ProcessAsync(s));

// Асинхронные области (дружественно к DI)
var result4 = await Benchmark.RunScopedAsync("Scoped",
    () => container.CreateScope(),
    async scope => await scope.Service.DoWorkAsync());
```

Классы на основе атрибутов могут объявлять асинхронные бенчмарки и асинхронные lifecycle-методы:

```csharp
[BenchmarkClass]
public partial class IoBenchmarks
{
    private string _path = "";

    [GlobalSetup]
    public async Task SetupAsync() => await File.WriteAllTextAsync(_path, "data");

    [IterationSetup]
    public async Task ResetAsync() => await Task.Yield();

    [Benchmark(Baseline = true)]
    public async Task ReadAsync() => await File.ReadAllTextAsync(_path);

    [Benchmark]
    public void ReadSync() => File.ReadAllText(_path);
}
```

- **Режимы измерения** – `AsyncTimingMode.WallClock` (по умолчанию) измеряет полное время, включая паузы await; `AsyncTimingMode.CpuOnly` измеряет `Process.TotalProcessorTime` и исключает ожидание ввода-вывода. Часы CPU идут только тиками таймера ОС (обычно 10–16 мс), поэтому автокалибровка поднимает минимальный бюджет выборки до измеренной гранулярности часов. В режиме CpuOnly данные GC не собираются.
- **Неизмеримые образцы** – в режиме `CpuOnly` нагрузка короче одного тика таймера ОС читается как ровно нулевое время CPU. Такой образец учитывается в `Statistics.UnmeasuredSampleCount` (`Unmeasured` равно `true`), его значение на операцию становится **одним квантом измерения — верхней границей — а не `0.000`**, а форматтеры печатают ячейки на операцию как `N/A`. Статистика такого запуска не является измерением работы: читайте её как «ниже разрешения часов CPU». Количество итераций при этом **не** раздувается молча, чтобы скрыть проблему — калибрует только `AutoCalibrateIterations = true` (предустановки `Quick`/`Precise`); `Default` использует заданное `IterationsPerSample` без изменений.
- **Атрибуция GC** – асинхронные счётчики GC помечены как приблизительные (`GcInfo.IsApproximate`). Аллокации setup/teardown исключаются в обоих режимах.
- **Циклы CPU** – синхронный путь читает счётчик уровня потока (`QueryThreadCycleTime` в Windows, `perf_event`, открытый для измеряемого потока, в Linux). Асинхронные бенчмарки читают счётчик всего процесса, поскольку поточный счётчик невозможно отследить при переходах между потоками: это число включает работу других потоков и помечается `Statistics.CpuCyclesAreApproximate`, отображаясь с `~` в Console, Markdown, HTML и CSV. На macOS счётчика циклов нет — `mach_absolute_time` является монотонным прокси *времени*, а не архитектурными циклами, и помечается так же.
- **Отмена** – `BenchmarkConfig.CancellationToken` проверяется только на границах выборок асинхронных бенчмарков; синхронные его игнорируют.
- **Смешанные классы** – синхронный метод `[Benchmark]` сохраняет прямой синхронный путь измерения, даже если в классе есть другие асинхронные члены.
- **Прогрев** – итерации прогрева вызывают только делегат прогрева; `setup`/`teardown` и `[IterationSetup]`/`[IterationCleanup]` не выполняются во время прогрева. Действия прогрева должны быть самодостаточными.

---

## Точность измерений

PicoBench работает в том же процессе, поэтому настройки CLR влияют на абсолютные значения:

- Отключите многоуровневую компиляцию JIT: `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0` (или `COMPlus_*`).
- Для однопоточных микробенчмарков используйте workstation GC: `DOTNET_gcServer=0`.
- Циклы CPU доступны только там, где ОС разрешает непривилегированные счётчики (Windows `QueryThreadCycleTime`; Linux `perf_event` при `perf_event_paranoid` = 2 или ниже). Синхронные запуски в Windows имеют область потока; асинхронные запуски и macOS используют счётчик всего процесса или, на macOS, монотонный прокси часов вместо настоящих циклов — оба случая помечаются `CpuCyclesAreApproximate`. `EnvironmentInfo` и каждый форматтер сообщают, какой источник использован.
- `BenchmarkConfig.BoostPriorities` (по умолчанию `true`) повышает приоритет процесса и потока только на время запуска и возвращает прежние значения после его окончания.

### Что числа могут и что не могут разрешить

Каждый запуск измеряет затраты, которые харнесс сам добавляет на операцию, при том же количестве итераций, и вычитает их, а затем публикует наименьшее значение, которое ещё способен разрешить:

| Поле | Значение |
|---|---|
| `Statistics.OverheadNs` | затраты харнесса на операцию (диспетчеризация делегата, учёт цикла, чтение часов и счётчиков), которые были измерены и вычтены. `0` для асинхронных запусков, чьи затраты не вычитаются |
| `Statistics.ResolutionLimitNanoseconds` | наименьшее значение на операцию, которое этот запуск способен разрешить: максимум из одного тика часов, распределённого по итерациям, разброса вычтенных затрат от запуска к запуску и размера этих затрат на операцию (их верхней границы) |
| `Statistics.ResolutionLimited` | `Avg` находится на этом пределе или ниже, либо хотя бы один образец вообще не удалось измерить. Форматтеры печатают ячейки на операцию как верхнюю границу (во всех четырёх форматах `<limit`; Console добавляет пометку `[below resolution]`, Markdown и HTML — `(below resolution)`) вместо числа |
| `Statistics.Unmeasured` / `UnmeasuredSampleCount` | сколько образцов источник измерения не смог продвинуть (`CpuOnly` с нулевым тиком). Такие ячейки печатают `N/A`, никогда `0.000` |

Таким образом, честное прочтение субнаносекундного результата — **«ниже `<limit` нс»**, а не «0.02 нс»: значение не измерено, а ограничено, и граница покрывает квантование часов и разброс вычтенных затрат (это максимум из тика часов, разброса вычтенных затрат и размера этих затрат на операцию; а поскольку остаток ограничен снизу нулём, для каждого образца выполняется «истинное значение ≤ остаток + затраты», поэтому граница больше не может опуститься ниже предела, вычтенного в том же запуске). Стоит помнить ещё о двух асимметриях: вычитание затрат харнесса реализовано только для синхронных бенчмарков (асинхронные значения на операцию по-прежнему несут затраты обёртки на образец), а байты GC не измеряются вообще — только количество сборок, по процессу, приблизительно на асинхронном пути.

---

---

## Конфигурация

### Предустановки

| Предустановка | Разогрев | Образцы | Базовых итераций/образец | Автокалибровка | Сценарий использования |
|--------|--------|---------|--------------------------|----------------|----------|
| `Quick` | 100 | 10 | 1,000 | Да | Быстрая итерация / CI |
| `Default` | 1,000 | 100 | 10,000 | Нет | Общий бенчмаркинг |
| `Precise` | 5,000 | 200 | 50,000 | Да | Финальные измерения |

### Пользовательская конфигурация

```csharp
var config = new BenchmarkConfig
{
    WarmupIterations    = 500,
    SampleCount         = 50,
    IterationsPerSample = 5000,
    RetainSamples       = true,  // Сохранять сырые данные TimingSample
    AutoCalibrateIterations = true,
    MinSampleTime       = TimeSpan.FromMilliseconds(0.5),
    MaxAutoIterationsPerSample = 1_000_000,
    ForceGcBeforeBenchmark = true,  // false пропускает принудительную полную GC перед бенчмарком
    BoostPriorities     = true,   // повышает приоритет процесса/потока только на время запуска и затем восстанавливает
    TimingMode          = AsyncTimingMode.WallClock, // или CpuOnly (только для асинхронных)
    CancellationToken   = default,                   // только для асинхронных; синхронные бенчмарки его игнорируют
};

var result = Benchmark.Run("Test", action, config);
```

При включенной автокалибровке PicoBench увеличивает `IterationsPerSample`, пока не будет достигнут минимальный бюджет времени на образец или предел `MaxAutoIterationsPerSample`. Это особенно полезно для очень быстрых операций, которые иначе будут сильно зависеть от шума таймера. Установите `ForceGcBeforeBenchmark = false`, чтобы пропустить принудительную полную сборку мусора перед фазой сбора — полезно при большом количестве бенчмарков и комбинаций параметров.Автокалибровка **включается явно** (`AutoCalibrateIterations` по умолчанию `false`, и предустановка `Default` не меняет заданное количество итераций): PicoBench никогда не раздувает итерации молча, чтобы результат выглядел разрешимым — в режиме `CpuOnly` поднятый бюджет равен измеренной гранулярности часов CPU, потому что всё более короткое читается как ноль.

---

## Форматтеры вывода

Пять встроенных форматтеров реализуют `IFormatter`:

```csharp
using PicoBench.Formatters;

var console  = new ConsoleFormatter();     // Таблицы консоли с рисованием рамок
var markdown = new MarkdownFormatter();    // Markdown таблицы, совместимые с GitHub
var html     = new HtmlFormatter();        // Стилизованные HTML отчеты
var csv      = new CsvFormatter();         // CSV для анализа данных

// Статический помощник для сводок сравнений:
Console.WriteLine(SummaryFormatter.Format(suite.Comparisons));
```

Все четыре форматтера показывают один и тот же набор метрик на операцию — `Avg`, `P50`, `P90`, `P95`, `P99`, `StandardError` и относительное стандартное отклонение (столбец `RSD (%)`) — плюс, если доступны, пояснения по счётчику CPU. В таблице набора Console также есть столбцы `StdErr` и `RSD` (раньше их не было, поэтому сигнал о шуме не попадал в вывод по умолчанию); Console, Markdown и HTML используют одну и ту же единицу таблицы для одних и тех же данных, а CSV всегда остаётся в наносекундах (см. ниже).

### Профиль точности: raw против display

У одного числа две задачи, и форматтеры разделяют их, вместо того чтобы каждый искал собственный компромисс: значение либо **raw** — наносекунды ровно в том виде, в каком измерены, — либо **display** — то же значение, приведённое к единице, подходящей таблице.

- **Raw** (`MetricPrecision.Raw`): чистые наносекунды, и заголовок столбца (или имя столбца в CSV: `Avg_ns`, `StdErr_ns`, …) указывает `ns`. `CsvFormatter` всегда пишет эту форму, какой бы единице ни отдавали предпочтение данные, потому что файл, единица которого меняется вместе с порядком величины данных, не читается машиной.
- **Display** (`MetricPrecision.Display`): таблица выбирает **одну** единицу для всех своих столбцов на операцию — `ns` ниже 1 000 ns, `µs` ниже 1 000 µs, `ms` ниже 1 000 ms, секунды выше — и называет её в заголовке (`Avg (µs)`). Одна единица на таблицу, а не на ячейку, чтобы столбец оставался сравнимым: пятисекундный бенчмарк читается как `5.0` под `Avg (s)`, а его сосед в 150 ns — как `0.0` под тем же заголовком, вместо молчаливого пересчёта.

```csharp
var raw = new FormatterOptions { PrecisionProfile = MetricPrecision.Raw };
Console.WriteLine(new ConsoleFormatter(raw).Format(suite)); // 5000000000.0 под "Avg (ns)"
```

Отображаемый текст идентичен в Console, Markdown и HTML (тест сверяет его посимвольно), а исходное число всегда есть в CSV, поэтому масштабированное отображение никогда не становится единственной копией измерения.

### Цели форматирования

```csharp
formatter.Format(result);               // Одиночный BenchmarkResult
formatter.Format(results);              // IEnumerable<BenchmarkResult>
formatter.Format(comparison);           // Одиночный ComparisonResult
formatter.Format(comparisons);          // IEnumerable<ComparisonResult>
formatter.Format(suite);                // Полный BenchmarkSuite
```

### Опции форматтера

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
    CandidateLabel        = "New",
    PrecisionProfile      = MetricPrecision.Display, // или .Raw; CSV всегда пишет raw
    OutputDirectory       = "results", // Used by WriteToFile methods
};

var formatter = new ConsoleFormatter(options);
// Также доступны: FormatterOptions.Default, .Compact, .Minimal
```

### Сохранение результатов

```csharp
var dir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(dir);

File.WriteAllText(Path.Combine(dir, "results.md"),   new MarkdownFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.html"), new HtmlFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.csv"),  new CsvFormatter().Format(suite));
```

### Контракт колонок CSV

`CsvFormatter` записывает строку заголовка, затем строки данных; для набора добавляются строки комментариев `#` (`# Schema:` строка контракта; строки метаданных `# key,value`: `Suite`, `Description`, `Environment`, `CpuCounter`, `CpuCounterNote`, `GcInfoNote`, `Timestamp`, `Duration`, `NoiseNote`) и два раздела, `# Results` и `# Comparisons`, **каждый со своей строкой заголовка**. Поэтому в файле больше одной строки заголовка — `pandas.read_csv(path, comment='#')` пропускает комментарии, но два раздела нужно разделять самостоятельно. Первая строка файла результатов — эта строка контракта `# Schema: v2; …`, а любая строка, начинающаяся с `#`, является комментарием, который парсер должен пропускать.

Строка метаданных никогда не переносится на следующую физическую строку: перевод строки внутри значения записывается двумя символами `\n` (CRLF, CR и LF одинаково), а само значение при необходимости берётся в кавычки по правилам CSV. Именно поэтому `comment='#'` безопасен для описания из нескольких строк.

Колонки результатов в этом порядке (параметры по умолчанию; `IncludePercentiles`, `IncludeGcInfo`, `IncludeCpuCycles` и `IncludeTimestamp` при значении `false` убирают свои колонки):

| # | Колонка | Значение |
|---|---|---|
| 1 | `Name` | имя бенчмарка (в кавычках, если содержит `,` `"` или перевод строки; ведущие `=`/`+`/`-`/`@` получают префикс `'`) |
| 2 | `Category` | категория, пустая, если не задана |
| 3 | `Avg_ns` | наносекунды на операцию — три состояния ячейки, см. ниже |
| 4 | `P50_ns` | медиана наносекунд на операцию (те же три состояния) |
| 5-12 | `P90_ns`, `P95_ns`, `P99_ns`, `Min_ns`, `Max_ns`, `StdDev_ns`, `StdErr_ns`, `RSD_pct` | процентили и разброс; `RSD_pct` — обычный процент, а остальные семь используют состояния ячейки на операцию |
| 13 | `CpuCycles` | циклы на операцию; префикс `~` обозначает счётчик всего процесса или прокси часов (`CpuCyclesAreApproximate`), а не циклы потока |
| 14-16 | `GC0`, `GC1`, `GC2` | количество сборок или `N/A`, если GC не собирался |
| 17 | `GCApproximate` | `1`, если счётчики приблизительны (асинхронный путь), `0`, если точны, `N/A`, если не собирались |
| 18 | `SampleCount` | количество образцов (целое) |
| 19 | `IterationsPerSample` | итераций на образец (целое) |
| 20 | `Timestamp` | ISO-8601 round-trip (`o`, инвариантная культура) |

Состояния ячейки колонки на операцию:

| Ячейка | Значение |
|---|---|
| `12.5` | измеренное значение в наносекундах (инвариантная культура, round-trip формат) |
| `<0.02` | **верхняя граница**: запуск не смог разрешить значение выше `Statistics.ResolutionLimitNanoseconds`. Число после `<` — это тот предел, и оно не является измеренным значением |
| `N/A` | не измерено (`Statistics.Unmeasured`, нулевая дельта часов) или не собрано (колонки GC) |

Два следствия для потребителей: из-за `<0.02` колонка на операцию **не** гарантированно числовая, поэтому читатели вроде pandas выводят `object`, и со строками-границами нужно работать явно; а `GCApproximate` вставлена в позицию 17, между `GC2` и `SampleCount`, поэтому **позиционные читатели должны сдвинуть `SampleCount`/`IterationsPerSample` на единицу** (читатели по именам не затронуты).

Колонки сравнений: `TestCase`, `Provider`, `Avg_ns`, `Speedup`, `P50_ns`, `P90_ns`, `P99_ns`, `StdErr_ns`, `RSD_pct`, `CpuCycles`, `GC0`, `GC1`, `GC2`, `GCApproximate`, `SampleCount`, `IterationsPerSample`. Каждое сравнение пишет две строки — сначала кандидат, затем базовый вариант — и `Speedup` заполняется только в строке кандидата. Ячейки на операцию в сравнениях проходят через тот же форматтер, что и таблица результатов, поэтому `<limit` и `N/A` появляются и там, а `CpuCycles` несёт ту же пометку `~` (`CpuCyclesAreApproximate`).

Строка `# Schema:` уже реализована и стоит **в первой строке файла**: файл результатов (и файл набора с результатами) начинается с контракта, описывающего следующие ниже колонки, а раздел сравнений пишет свою строку контракта непосредственно перед `# Comparisons`:

```
# Schema: v2; section=results; columns=Name|Category|Avg_ns|P50_ns|P90_ns|P95_ns|P99_ns|Min_ns|Max_ns|StdDev_ns|StdErr_ns|RSD_pct|CpuCycles|GC0|GC1|GC2|GCApproximate|SampleCount|IterationsPerSample|Timestamp; per-op cells (Avg_ns,P50_ns,P90_ns,P95_ns,P99_ns,Min_ns,Max_ns,StdDev_ns,StdErr_ns) = number (nanoseconds) | "<limit" (upper bound: the value is at the harness floor) | "N/A" (not measured); RSD_pct = percent; SampleCount = samples taken | IterationsPerSample = iterations per sample; CpuCycles = count | "~count" (not a thread-scoped cycle count); GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected)
# Schema: v2; section=comparisons; columns=TestCase|Provider|Avg_ns|Speedup|P50_ns|P90_ns|P99_ns|StdErr_ns|RSD_pct|CpuCycles|GC0|GC1|GC2|GCApproximate|SampleCount|IterationsPerSample; per-op cells (Avg_ns,P50_ns,P90_ns,P99_ns,StdErr_ns) = number (nanoseconds) | "<limit" (upper bound: the value is at the harness floor) | "N/A" (not measured); RSD_pct = percent; Speedup = ratio (baseline avg / candidate avg, or the median of the per-round paired ratios for a direct comparison) | "N/A" (no finite ratio: a candidate average of zero is unbounded, a non-finite candidate average is unmeasurable, or too few usable paired rounds); SampleCount = samples taken | IterationsPerSample = iterations per sample; CpuCycles = count | "~count" (not a thread-scoped cycle count); GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected)
```

`v2` — это форма Batch 0: три состояния ячейки на операцию, описанные выше, плюс `GCApproximate`. Список `columns=` выводится из реально записанной строки заголовка, поэтому он не может разойтись с таблицами выше и никогда не называет колонку, убранную параметром; `section=` разделяет две строки контракта. Парсер должен пропускать все строки, начинающиеся с `#`, и брать порядок колонок из списка `columns=`, а не задавать его в коде.

---

## Модель результатов

| Тип | Описание |
|------|-------------|
| `BenchmarkResult` | Имя, Категория, Метки, Статистика, Образцы, ИтерацийНаОбразец, КоличествоОбразцов, Метка времени |
| `ComparisonResult` | Имя, Категория, Метки, Базовый, Кандидат, Ускорение, ПроцентУлучшения, **Outcome**, **IsFasterKnown**, **Interval**, Быстрее |
| `BenchmarkSuite` | Имя, Описание, Результаты, Сравнения, Окружение, Длительность, Метка времени |
| `ComparisonOutcome` | Перечисление: `Unknown` (отношение вычислить не удалось), `Inconclusive` (внутри мёртвой зоны 5%, или интервал всё ещё содержит паритет), `Faster`, `Slower` |
| `ComparisonInterval` | Low, High, Contains(value), ToString() — свободный от распределения 95%-й интервал медианы парного отношения |
| `Statistics` | `Avg`, `P50`, `P90`, `P95`, `P99`, `Min`, `Max`, `StdDev`, `StandardError`, `RelativeStdDevPercent`, `CpuCyclesPerOp`, **CpuCyclesAreApproximate**, **OverheadNs**, **ResolutionLimitNanoseconds**, **ResolutionLimited**, **Unmeasured**, **UnmeasuredSampleCount**, GcInfo, HasHighVariance |
| `TimingSample` | ПрошедшиеНаносекунды, ПрошедшиеМиллисекунды, ПрошедшиеТики, ЦиклыCpu, **CpuCyclesAreApproximate**, GcInfo |
| `GcInfo` | Gen0, Gen1, Gen2, Всего, Нулевое, IsApproximate |
| `EnvironmentInfo` | ОС, Архитектура, ВерсияСредыВыполнения, КоличествоПроцессоров, Режим выполнения, Конфигурация, тип / доступность / значимость счетчика CPU, пользовательские теги |

Семантика членов, добавленных работой по точности измерений:

| Член | Контракт |
|---|---|
| `ComparisonResult.Outcome` | Трёхзначный вердикт; `Faster`/`Slower` требуют мёртвой зоны 5% **и** интервала, исключающего 1 |
| `ComparisonResult.IsFasterKnown` | `Outcome != Unknown` — «две полосы были сравнены» (они всё ещё могут быть неразличимы) |
| `ComparisonResult.IsFaster` | Только `Outcome == Faster`. Уже, чем прежнее `Speedup > 1.0`: разница внутри мёртвой зоны больше не считается «быстрее» |
| `ComparisonResult.Interval` | `ComparisonInterval?`; `null`, когда сравнение построено из двух независимых результатов, а не из чередующихся раундов |
| `Statistics.ResolutionLimited` | Значение на операцию является верхней границей (`<limit`), а не измерением |
| `Statistics.UnmeasuredSampleCount` / `Unmeasured` | Образцы, которые источник измерения не смог продвинуть; их ячейки на операцию — `N/A` |
| `Statistics.CpuCyclesAreApproximate` | `CpuCyclesPerOp` относится ко всему процессу (асинхронно) или является монотонным прокси часов (macOS), а не циклами потока: форматтеры добавляют к нему префикс `~` |
| `TimingSample.CpuCyclesAreApproximate` | Тот же флаг на уровне образца; `Statistics` агрегирует его логическим ИЛИ |

---

## Архитектура

```
src/
+-- PicoBench/                        # Основная библиотека (netstandard2.0)
|   +-- Benchmark.cs                   # Императивный API (Run, Compare, RunScoped)
|   +-- BenchmarkRunner.cs             # Точка входа на основе атрибутов (Run<T>)
|   +-- BenchmarkConfig.cs             # Конфигурация с предустановками
|   +-- Attributes.cs                  # 7 атрибутов бенчмаркинга
|   +-- IBenchmarkClass.cs             # Интерфейс, создаваемый генератором
|   +-- Runner.cs                      # Низкоуровневый поток измерения времени и создание образцов
|   +-- Runner.Gc.cs                   # Базовая линия GC и расчёт дельты
|   +-- Runner.Cpu.cs                  # Платформенно-специфичная реализация счётчика CPU
|   +-- StatisticsCalculator.cs        # Вычисление процентилей / статистики
|   +-- Models.cs                      # Типы результатов
|   +-- Formatters/
|       +-- IFormatter.cs              # IFormatter, FormatterOptions & FormatterBase
|       +-- ConsoleFormatter.cs        # Таблицы консоли с рисованием рамок
|       +-- MarkdownFormatter.cs       # Markdown таблицы GitHub
|       +-- HtmlFormatter.cs           # Стилизованные HTML отчеты
|       +-- CsvFormatter.cs            # Экспорт CSV
|       +-- SummaryFormatter.cs        # Сводка побед/поражений
|
+-- PicoBench.Generators/            # Генератор исходного кода (netstandard2.0)
    +-- BenchmarkGenerator.cs          # Точка входа IIncrementalGenerator
    +-- BenchmarkClassAnalyzer.cs      # Анализ и диагностика Roslyn
    +-- CSharpLiteralFormatter.cs      # Форматирование литералов C# для генерируемых параметров
    +-- DiagnosticDescriptors.cs       # Определения диагностик генератора
    +-- Emitter.cs                     # Генератор кода C# (безопасно для AOT)
    +-- Models.cs                      # Модели анализа Roslyn
```

---

## Особенности платформы

| Особенность | Windows | Linux | macOS |
|---------|---------|-------|-------|
| Измерение времени | Stopwatch | Stopwatch | Stopwatch |
| Отслеживание GC (Gen0/1/2) | Да | Да | Да |
| Подсчёт циклов CPU, синхронно | `QueryThreadCycleTime` (уровень потока) | `perf_event_open` на поток (при `perf_event_paranoid` ≤ 2) | `mach_absolute_time`, **прокси времени** — помечается `~` |
| Подсчёт циклов CPU, асинхронно | `QueryProcessCycleTime` (уровень процесса) — помечается `~` | `perf_event` уровня процесса — помечается `~` | `mach_absolute_time` — помечается `~` |
| Повышение приоритета процесса | Да (в рамках запуска, восстанавливается) | Да (в рамках запуска, восстанавливается) | Да (в рамках запуска, восстанавливается) |

На macOS экспортируемый счётчик CPU — монотонный прокси *времени* высокого разрешения, а не архитектурный счётчик циклов: значение растёт с временем, а не с выполненными инструкциями, поэтому он помечается `CpuCyclesAreApproximate` и печатается с `~`. Тот же флаг используется для асинхронного пути, чей счётчик уровня процесса включает работу других потоков. `EnvironmentInfo` и вывод форматтеров явно показывают оба различия.

---

## Примеры

| Пример | Стиль API | Описание |
|--------|-----------|-------------|
| `StringVsStringBuilder` | Императивный | Сравнивает `string +=`, `StringBuilder` и `StringBuilder` с емкостью |
| `AttributeBased` | На основе атрибутов | То же сравнение с использованием `[Benchmark]`, `[Params]` и генератора исходного кода |
| `CollectionBenchmarks` | На основе атрибутов | Поиск в List vs Dictionary vs HashSet - демонстрирует каждый атрибут |
| `AsyncBenchmarks` | Атрибутный (асинхронный) | Асинхронная точка входа и смешанный жизненный цикл: `BenchmarkRunner.RunAsync<T>()`, асинхронные `[GlobalSetup]`/`[IterationSetup]`, `[Benchmark]`, возвращающий `Task`, и `SummaryFormatter` |

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
dotnet run --project samples/AttributeBased -c Release
dotnet run --project samples/CollectionBenchmarks -c Release
dotnet run --project samples/AsyncBenchmarks -c Release
```

---

## Сравнение с BenchmarkDotNet

| Особенность | PicoBench | BenchmarkDotNet |
|---------|-----------|----------------|
| Зависимости | 1 BCL polyfill | Много |
| Размер пакета | Маленький | Большой |
| Целевая платформа | netstandard2.0 | net6.0+ |
| Поддержка AOT | Генератор исходного кода | На основе рефлексии |
| API атрибутов | `[Benchmark]`, `[Params]` | `[Benchmark]`, `[Params]` |
| Время настройки | Мгновенно | Секунды |
| Форматы вывода | 5 | 10+ |
| Статистическая глубина | Хорошая | Обширная |
| Сценарий использования | Быстрые A/B тесты, CI, AOT приложения | Детальный анализ, публикации |

---

## Лицензия

Лицензия MIT - подробности см. в файле [LICENSE](LICENSE).

## Сборка и публикация

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

Релизы следуют правилу PicoHex `<year>.<x>.<y>`: **x** увеличивается при изменении публичного API, **y** — когда API не менялся. Решение основано на коммитнутом базисе API `api/PicoBench.public.txt`, который всегда содержит поверхность последнего релиза.

```bash
# показать дельту API и следующую версию
pwsh ./scripts/release.ps1 -DryRun

# сделать релиз: обновить api/, коммит chore(release): v<version>, тег, push
pwsh ./scripts/release.ps1 -Push
```

Пуш тега запускает release-конвейер GitHub Actions: тесты, упаковка всех пакетов с версией тега и публикация на [NuGet.org](https://www.nuget.org/packages/PicoBench). Чтобы перекрыть задержку индексации для сестринских репозиториев, ту же версию можно упаковать в локальный источник из `NuGet.config`:

```bash
dotnet pack src/PicoBench/PicoBench.csproj -c Release -o artifacts/nupkg -p:Version=<version>
```

## Вклад

1. Форкните репозиторий
2. Создайте ветку функции
3. Внесите изменения с тестами
4. Отправьте pull request
