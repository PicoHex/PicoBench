# PicoBench

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

Uma biblioteca de benchmarking leve para .NET com **duas APIs complementares**: uma API imperativa e uma API baseada em atributos, gerada por código fonte, totalmente **compatível com AOT**. Sem dependências de terceiros — a única referência NuGet é um polyfill BCL do .NET para `ValueTask<T>` no netstandard2.0.

## Características

- **Zero Dependências de Terceiros** — Apenas um polyfill BCL (`System.Threading.Tasks.Extensions`) para trazer `ValueTask<T>` ao netstandard2.0. Nenhum pacote externo.
- **Duas APIs** - Imperativa (`Benchmark.Run`) para testes ad hoc; baseada em atributos (`[Benchmark]` + gerador de código fonte) para suites estruturadas
- **Gerador de Código Fonte Compatível com AOT** - O gerador incremental emite chamadas de método diretas com zero reflexão em tempo de execução
- **Multiplataforma** - Suporte total para Windows, Linux e macOS
- **Temporização de Alta Precisão** - Usa `Stopwatch` e relata tempos por operação em escala de nanossegundos
- **Monitoramento de GC** - Monitora contagens de coleta Gen0/Gen1/Gen2 durante benchmarks (setup/teardown excluídos)
- **Contagem de Ciclos de CPU** - Contagem de ciclos por hardware em Windows/Linux, além de um proxy monotônico no macOS (`mach_absolute_time`)
- **Análise Estatística** - Média, Mediana, P90, P95, P99, Mín, Máx, Desvio Padrão, erro padrão e desvio padrão relativo
- **Múltiplos Formatos de Saída** - Quatro formatadores `IFormatter` integrados (Console, Markdown, HTML, CSV) mais saída de resumo programático
- **Benchmarks Parametrizados** - Atributo `[Params]` com iteração automática de produto cartesiano
- **Suporte a Comparações** - Baseline vs candidato com cálculos de aceleração
- **Configurável** - Predefinições Rápido, Padrão e Preciso, auto-calibração ou configuração totalmente personalizada
- **netstandard2.0** - Compatível com .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5+

## Instalação

Referencie o pacote NuGet **PicoBench**. O gerador de código fonte (`PicoBench.Generators`) é incluído automaticamente como um analisador - nenhuma referência extra necessária.

```bash
dotnet add package PicoBench
```

## Início Rápido

### API Imperativa

```csharp
using PicoBench;

var result = Benchmark.Run("Meu Benchmark", () =>
{
    Thread.SpinWait(100);
});

Console.WriteLine($"Média: {result.Statistics.Avg:F1} ns/op");
```

### API Baseada em Atributos (Gerada por Código Fonte)

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

> A classe **deve** ser `partial`. O gerador de código fonte emite uma implementação `IBenchmarkClass` em tempo de compilação - sem reflexão, totalmente segura para AOT.

> Uso inválido de atributos agora produz diagnósticos do gerador para erros comuns como classes não `partial`, baselines duplicados, assinaturas de ciclo de vida inválidas e valores `[Params]` incompatíveis. O gerador também rejeita benchmarks `async void` (PBGEN011) e classes de benchmark genéricas, aninhadas, record ou não instanciáveis (PBGEN012-015), e avisa sobre `[Params]` vazios (PBGEN016) e atributos de benchmark em tipos base (PBGEN017).

---

## Referência da API Imperativa

### Benchmark Básico

```csharp
using PicoBench;
using PicoBench.Formatters;

var result = Benchmark.Run("SpinWait", () => Thread.SpinWait(100));
Console.WriteLine(new ConsoleFormatter().Format(result));
```

### Benchmark com Estado (Evitar Closures)

```csharp
var data = new byte[1024];
var result = Benchmark.Run("ArrayCopy", data, static d =>
{
    var copy = new byte[d.Length];
    Buffer.BlockCopy(d, 0, copy, 0, d.Length);
});
```

### Benchmarks com Escopo (Amigável a DI)

```csharp
var result = Benchmark.RunScoped("DbQuery",
    () => new MyDbContext(),
    static ctx => ctx.Users.FirstOrDefault()
);
// Um novo escopo é criado por amostra; o escopo é descartado após cada amostra.
```

Uma variante assíncrona também está disponível:

```csharp
var result = await Benchmark.RunScopedAsync("DbQueryAsync",
    () => new MyDbContext(),
    static async ctx => await ctx.Users.FirstOrDefaultAsync()
);
// Variante assíncrona: um novo escopo por amostra, descartado após cada amostra.
```

### Comparando Duas Implementações

```csharp
var comparison = Benchmark.Compare(
    "String vs StringBuilder",
    "String Concat",  () => { var s = ""; for (int i = 0; i < 100; i++) s += "a"; },
    "StringBuilder",  () => { var sb = new StringBuilder(); for (int i = 0; i < 100; i++) sb.Append('a'); _ = sb.ToString(); }
);

Console.WriteLine($"Aceleração: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%)");
```

### Avançado: Warmup, Setup & Teardown Separados

```csharp
var result = Benchmark.Run(
    name:     "Custom",
    action:   () => DoWork(),
    warmup:   () => DoWork(),      // null para pular warmup
    config:   BenchmarkConfig.Precise,
    setup:    () => PrepareState(), // chamado antes de cada amostra (não cronometrado)
    teardown: () => CleanUp()       // chamado após cada amostra (não cronometrado)
);
```

---

## Referência da API Baseada em Atributos

Decore uma classe **partial** com `[BenchmarkClass]` e seus métodos/propriedades com os atributos abaixo. O gerador de código fonte emite todo o código de conexão em tempo de compilação.

### Atributos

| Atributo | Alvo | Descrição |
|-----------|--------|-------------|
| `[BenchmarkClass]` | Classe | Marca a classe para geração de código. Propriedade opcional `Description`. |
| `[Benchmark]` | Método | Marca um método sem parâmetros como um benchmark. Defina `Baseline = true` para o método de referência. Opcional `Description`. |
| `[Params(values)]` | Propriedade / Campo | Itera os valores constantes em tempo de compilação fornecidos. Múltiplas propriedades `[Params]` produzem um produto cartesiano. |
| `[GlobalSetup]` | Método | Chamado **uma vez** por combinação de parâmetros, antes da execução dos benchmarks. |
| `[GlobalCleanup]` | Método | Chamado **uma vez** por combinação de parâmetros, após a execução dos benchmarks. |
| `[IterationSetup]` | Método | Chamado antes de **cada amostra** (não cronometrado). |
| `[IterationCleanup]` | Método | Chamado após **cada amostra** (não cronometrado). |

Métodos `[Benchmark]` e de ciclo de vida devem ser de instância, não genéricos e sem parâmetros. Eles podem retornar `void`, `Task`, `ValueTask`, `Task<T>` ou `ValueTask<T>`; métodos assíncronos são aguardados com await e os valores retornados são descartados. Métodos `[Benchmark]` não podem ser `async void` (PBGEN011). Destinos `[Params]` devem ser propriedades de instância graváveis ou campos de instância que não sejam `readonly`. Classes de benchmark devem ser não genéricas, não aninhadas, não abstratas e declarar um construtor público sem parâmetros.

### Exemplo Completo

```csharp
using PicoBench;

[BenchmarkClass(Description = "Comparando estratégias de concatenação de strings")]
public partial class StringBenchmarks
{
    [Params(10, 100, 1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() { /* preparar dados para o N atual */ }

    [GlobalCleanup]
    public void Cleanup() { /* liberar recursos */ }

    [IterationSetup]
    public void BeforeSample() { /* preparação por amostra */ }

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

### Executando

```csharp
// Criar instância internamente:
var suite = BenchmarkRunner.Run<StringBenchmarks>(BenchmarkConfig.Quick);

// Ou com uma instância pré-configurada:
var instance = new StringBenchmarks();
var suite2 = BenchmarkRunner.Run(instance, BenchmarkConfig.Quick);
```

## Benchmarks Assíncronos

Ambas as APIs suportam trabalho assíncrono. Métodos de benchmark assíncronos são aguardados por iteração; métodos de ciclo de vida podem misturar sincronia e assincronia na mesma classe.

```csharp
var result = await Benchmark.RunAsync("Http call", async () =>
{
    using var response = await httpClient.GetAsync(url);
});

var scoped = await Benchmark.RunScopedAsync("Scoped",
    () => container.CreateScope(),
    async scope => await scope.Service.DoWorkAsync());
```

- **Modos de medição** – `AsyncTimingMode.WallClock` (padrão) mede a duração total, incluindo suspensões de await; `AsyncTimingMode.CpuOnly` mede `Process.TotalProcessorTime` e exclui espera de E/S. O relógio de CPU avança apenas em ticks do temporizador (normalmente 10–16 ms); a autocalibração eleva seu orçamento mínimo de amostra à granularidade medida do relógio. No modo CpuOnly não há dados de GC.
- **Atribuição de GC** – contadores de GC assíncronos são marcados como aproximados (`GcInfo.IsApproximate`). Alocações de setup/teardown são excluídas nos dois modos.
- **Ciclos de CPU** – benchmarks assíncronos usam contadores de todo o processo (contadores por thread não podem ser subtraídos entre trocas de thread).
- **Cancelamento** – `BenchmarkConfig.CancellationToken` só é verificado nos limites de amostra dos benchmarks assíncronos; benchmarks síncronos o ignoram.
- **Classes mistas** – um método `[Benchmark]` síncrono mantém o caminho de medição síncrono direto mesmo quando a classe tem outros membros assíncronos.
- **Warmup** – iterações de warmup chamam apenas o delegado de warmup; `setup`/`teardown` e `[IterationSetup]`/`[IterationCleanup]` não são executados durante o warmup. Ações de warmup devem ser autossuficientes.

---

## Fidelidade de Medição

PicoBench executa em processo, portanto as configurações do CLR afetam os valores absolutos:

- Desative o JIT em camadas: `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0` (ou `COMPlus_*`).
- Para microbenchmarks de thread única, use GC workstation: `DOTNET_gcServer=0`.
- Ciclos de CPU só estão disponíveis se o sistema operacional permitir contadores sem privilégios (Windows `QueryThreadCycleTime`/`QueryProcessCycleTime`; Linux `perf_event` com `perf_event_paranoid` ≤ 2; macOS fornece um proxy monotônico, não ciclos reais). `EnvironmentInfo` e todos os formatadores informam a fonte usada.
- `BenchmarkConfig.BoostPriorities` (padrão `true`) eleva a prioridade do processo e da thread apenas durante a execução e restaura os valores anteriores ao final.

---

---

## Configuração

### Predefinições

| Predefinição | Warmup | Amostras | Iters base/Amostra | Auto-calibração | Caso de Uso |
|--------|--------|---------|--------------------|-----------------|----------|
| `Quick` | 100 | 10 | 1,000 | Sim | Iteração rápida / CI |
| `Default` | 1,000 | 100 | 10,000 | Não | Benchmarking geral |
| `Precise` | 5,000 | 200 | 50,000 | Sim | Medições finais |

### Configuração Personalizada

```csharp
var config = new BenchmarkConfig
{
    WarmupIterations    = 500,
    SampleCount         = 50,
    IterationsPerSample = 5000,
    RetainSamples       = true,  // Manter dados brutos de TimingSample
    AutoCalibrateIterations = true,
    MinSampleTime       = TimeSpan.FromMilliseconds(0.5),
    MaxAutoIterationsPerSample = 1_000_000,
    ForceGcBeforeBenchmark = true,  // false skips the pre-benchmark full GC
};

var result = Benchmark.Run("Test", action, config);
```

Quando a auto-calibração está habilitada, o PicoBench aumenta `IterationsPerSample` até atingir um orçamento mínimo de tempo por amostra ou até alcançar `MaxAutoIterationsPerSample`. Isso é especialmente útil para operações muito rápidas dominadas por ruído do temporizador. Defina `ForceGcBeforeBenchmark = false` para pular o GC completo forçado que precede a fase de coleta — útil ao executar muitos benchmarks e combinações de parâmetros.

---

## Formatadores de Saída

Cinco formatadores integrados implementam `IFormatter`:

```csharp
using PicoBench.Formatters;

var console  = new ConsoleFormatter();     // Tabelas de console com desenho de caixas
var markdown = new MarkdownFormatter();    // Markdown amigável ao GitHub
var html     = new HtmlFormatter();        // Relatório HTML estilizado
var csv      = new CsvFormatter();         // CSV para análise de dados

// Auxiliar estático para resumos de comparação:
Console.WriteLine(SummaryFormatter.Format(suite.Comparisons));
```

As saídas de Console, Markdown, HTML e CSV incluem metadados voltados à precisão, como erro padrão, desvio padrão relativo e observações sobre o contador de CPU quando disponíveis.

### Alvos de Formatação

```csharp
formatter.Format(result);               // Único BenchmarkResult
formatter.Format(results);              // IEnumerable<BenchmarkResult>
formatter.Format(comparison);           // Único ComparisonResult
formatter.Format(comparisons);          // IEnumerable<ComparisonResult>
formatter.Format(suite);                // BenchmarkSuite completo
```

### Opções do Formatador

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
// Também disponíveis: FormatterOptions.Default, .Compact, .Minimal
```

### Salvando Resultados

```csharp
var dir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(dir);

File.WriteAllText(Path.Combine(dir, "results.md"),   new MarkdownFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.html"), new HtmlFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.csv"),  new CsvFormatter().Format(suite));
```

---

## Modelo de Resultados

| Tipo | Descrição |
|------|-------------|
| `BenchmarkResult` | Nome, Categoria, Tags, Estatísticas, Amostras, IterationsPerSample, SampleCount, Timestamp |
| `ComparisonResult` | Nome, Categoria, Tags, Baseline, Candidate, Speedup, IsFaster, ImprovementPercent |
| `BenchmarkSuite` | Nome, Descrição, Results, Comparisons, Environment, Duration, Timestamp |
| `Statistics` | Avg, P50, P90, P95, P99, Min, Max, StdDev, StandardError, RelativeStdDevPercent, CpuCyclesPerOp, GcInfo |
| `TimingSample` | ElapsedNanoseconds, ElapsedMilliseconds, ElapsedTicks, CpuCycles, GcInfo |
| `GcInfo` | Gen0, Gen1, Gen2, Total, IsZero |
| `EnvironmentInfo` | Os, Architecture, RuntimeVersion, ProcessorCount, modo de execução, Configuration, tipo / disponibilidade / significado do contador de CPU, tags personalizadas |

---

## Arquitetura

```
src/
+-- PicoBench/                        # Biblioteca principal (netstandard2.0)
|   +-- Benchmark.cs                   # API imperativa (Run, Compare, RunScoped)
|   +-- BenchmarkRunner.cs             # Ponto de entrada baseado em atributos (Run<T>)
|   +-- BenchmarkConfig.cs             # Configuração com predefinições
|   +-- Attributes.cs                  # 7 atributos de benchmark
|   +-- IBenchmarkClass.cs             # Interface emitida pelo gerador
|   +-- Runner.cs                      # Fluxo de temporização de baixo nível e criação de amostras
|   +-- Runner.Gc.cs                   # Linha de base e delta de GC
|   +-- Runner.Cpu.cs                  # Implementação do contador de CPU específica da plataforma
|   +-- StatisticsCalculator.cs        # Cálculo de percentis / estatísticas
|   +-- Models.cs                      # Tipos de resultado
|   +-- Formatters/
|       +-- IFormatter.cs              # IFormatter, FormatterOptions & FormatterBase
|       +-- ConsoleFormatter.cs        # Tabelas de console com desenho de caixas
|       +-- MarkdownFormatter.cs       # Tabelas Markdown do GitHub
|       +-- HtmlFormatter.cs           # Relatórios HTML estilizados
|       +-- CsvFormatter.cs            # Exportação CSV
|       +-- SummaryFormatter.cs        # Resumo de vitórias/derrotas
|
+-- PicoBench.Generators/            # Gerador de código fonte (netstandard2.0)
    +-- BenchmarkGenerator.cs          # Ponto de entrada IIncrementalGenerator
    +-- BenchmarkClassAnalyzer.cs      # Análise e diagnósticos do Roslyn
    +-- CSharpLiteralFormatter.cs      # Formatação de literais C# para parâmetros emitidos
    +-- DiagnosticDescriptors.cs       # Definições de diagnósticos do gerador
    +-- Emitter.cs                     # Emissor de código C# (seguro para AOT)
    +-- Models.cs                      # Modelos de análise Roslyn
```

---

## Recursos Específicos da Plataforma

| Recurso | Windows | Linux | macOS |
|---------|---------|-------|-------|
| Temporização de alta precisão | Stopwatch | Stopwatch | Stopwatch |
| Monitoramento de GC (Gen0/1/2) | Sim | Sim | Sim |
| Contagem de ciclos de CPU | `QueryThreadCycleTime` | `perf_event_open` | `mach_absolute_time` (proxy) |
| Aumento de prioridade de processo | Sim | Sim | Sim |

No macOS, o contador de CPU exportado é um proxy monotônico de alta resolução, não ciclos arquiteturais reais. `EnvironmentInfo` e a saída dos formatadores deixam essa distinção explícita.

---

## Exemplos

| Exemplo | Estilo de API | Descrição |
|--------|-----------|-------------|
| `StringVsStringBuilder` | Imperativa | Compara `string +=`, `StringBuilder`, e `StringBuilder` com capacidade |
| `AttributeBased` | Atributos | Mesma comparação usando `[Benchmark]`, `[Params]`, e o gerador de código fonte |
| `CollectionBenchmarks` | Atributos | Busca em List vs Dictionary vs HashSet - mostra cada atributo |

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
dotnet run --project samples/AttributeBased -c Release
dotnet run --project samples/CollectionBenchmarks -c Release
```

---

## Comparação com BenchmarkDotNet

| Recurso | PicoBench | BenchmarkDotNet |
|---------|-----------|----------------|
| Dependências | 1 polyfill BCL | Muitas |
| Tamanho do pacote | Pequeno | Grande |
| Framework de destino | netstandard2.0 | net6.0+ |
| Suporte a AOT | Gerador de código fonte | Baseado em reflexão |
| API de atributos | `[Benchmark]`, `[Params]` | `[Benchmark]`, `[Params]` |
| Tempo de configuração | Instantâneo | Segundos |
| Formatos de saída | 5 | 10+ |
| Profundidade estatística | Boa | Extensa |
| Caso de uso | Testes A/B rápidos, CI, apps AOT | Análise detalhada, publicações |

---

## Licença

Licença MIT - consulte o arquivo [LICENSE](LICENSE) para detalhes.

## Compilação e Publicação

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

As versões seguem a regra PicoHex `<ano>.<x>.<y>`: **x** aumenta quando a API pública mudou, **y** quando não mudou. A decisão vem da baseline de API commitada em `api/PicoBench.public.txt`, que sempre contém a superfície do último release.

```bash
# ver o delta da API e a próxima versão
pwsh ./scripts/release.ps1 -DryRun

# cortar o release: atualizar api/, commit chore(release): v<version>, tag, push
pwsh ./scripts/release.ps1 -Push
```

A tag enviada dispara a pipeline de release do GitHub Actions, que executa os testes, empacota cada pacote na versão da tag e publica em [NuGet.org](https://www.nuget.org/packages/PicoBench). Para cobrir a lacuna de indexação dos repositórios irmãos, a mesma versão pode ser empacotada no feed local declarado em `NuGet.config`:

```bash
dotnet pack src/PicoBench/PicoBench.csproj -c Release -o artifacts/nupkg -p:Version=<version>
```

## Contribuindo

1. Faça um fork do repositório
2. Crie um branch de funcionalidade
3. Faça alterações com testes
4. Envie uma pull request
