# PicoBench

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

Uma biblioteca de benchmarking leve para .NET com **duas APIs complementares**: uma API imperativa e uma API baseada em atributos, gerada por código fonte, totalmente **compatível com AOT**. Sem dependências de terceiros — a única referência NuGet é um polyfill BCL do .NET para `ValueTask<T>` no netstandard2.0.

## Características

- **Zero Dependências de Terceiros** — Apenas um polyfill BCL (`System.Threading.Tasks.Extensions`) para trazer `ValueTask<T>` ao netstandard2.0. Nenhum pacote externo.
- **Duas APIs** - Imperativa (`Benchmark.Run`) para testes ad hoc; baseada em atributos (`[Benchmark]` + gerador de código fonte) para suites estruturadas
- **Gerador de Código Fonte Compatível com AOT** - O gerador incremental emite chamadas de método diretas com zero reflexão em tempo de execução
- **Multiplataforma** - Suporte total para Windows, Linux e macOS
- **Temporização Sub-Microssegundo** - Temporizações por operação baseadas em `Stopwatch`, com o custo por operação do próprio arnês medido e descontado. Uma carga de trabalho abaixo do limite de resolução da execução é relatada como limite superior (`<limit`) e marcada `ResolutionLimited` — nunca como um número com precisão de nanossegundos
- **Limites Declarados** - `OverheadNs`, `ResolutionLimitNanoseconds`, `ResolutionLimited` e `Unmeasured` dizem o que uma execução pôde e não pôde resolver, para que um resultado em nível de ruído não possa ser lido como uma operação rápida
- **Monitoramento de GC** - Monitora contagens de coleta Gen0/Gen1/Gen2 durante benchmarks (setup/teardown excluídos)
- **Contagem de Ciclos de CPU** - Contagens de ciclos por thread no caminho síncrono (Windows `QueryThreadCycleTime`; Linux `perf_event` quando o SO permite acesso sem privilégios). Benchmarks assíncronos leem um contador de todo o processo, porque um contador por thread não pode ser seguido entre trocas de thread, e são marcados `CpuCyclesAreApproximate` (`~` nos quatro formatos). O macOS não tem contador de ciclos: `mach_absolute_time` é um proxy de *tempo* monotônico, não ciclos, e é marcado da mesma forma
- **Análise Estatística** - Média, Mediana, P90, P95, P99, Mín, Máx, Desvio Padrão, erro padrão e desvio padrão relativo
- **Múltiplos Formatos de Saída** - Quatro formatadores `IFormatter` integrados (Console, Markdown, HTML, CSV) mais saída de resumo programático
- **Benchmarks Parametrizados** - Atributo `[Params]` com iteração automática de produto cartesiano
- **Suporte a Comparações** - Baseline vs candidato medidos em rodadas intercaladas; o veredicto é a mediana das razões pareadas por rodada e exige tanto uma faixa morta de 5% quanto um intervalo livre de distribuição que exclua a paridade, para que o ruído entre rodadas não seja publicado como melhoria
- **Configurável** - Predefinições Rápido, Padrão e Preciso, auto-calibração ou configuração totalmente personalizada
- **netstandard2.0** - Compatível com .NET Framework **4.7.2+** (`RuntimeInformation` é usado incondicionalmente, portanto versões anteriores do .NET Framework não são suportadas), .NET Core 2.0+, .NET 5+

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

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%) [{(comparison.Interval?.ToString() ?? "no interval")}] -> {comparison.Outcome}");
```

`Benchmark.Compare(name, baselineAction, candidateAction)` **não** executa cada linha uma única vez. Ele intercala as duas linhas ao longo de 10 rodadas (qual linha vai primeiro alterna a cada rodada, para que nenhum lado herde um processo mais aquecido) e relata a **mediana das razões pareadas por rodada**. O que isso significa para os números:

- **`Speedup`** — tempo da baseline ÷ tempo do candidato; `> 1` significa que o candidato é mais rápido. Uma rodada cujo candidato não mediu nada não contribui com nenhuma razão; quando nenhuma rodada produz uma razão utilizável, a comparação relata `NaN` (e `ImprovementPercent` é `NaN` junto) em vez de inventar um veredicto. Um `ComparisonResult` construído a partir de dois `BenchmarkResult` independentes ainda relata a razão das duas médias. Uma rodada 0/0 é exatamente esse caso: `NaN`, não `1.0` (uma comparação construída a partir de dois resultados independentes zerados mantém o `1.0` legado).
- **`Interval`** — intervalo de 95% livre de distribuição dessa mediana (2.ª/9.ª estatística de ordem das 10 razões pareadas). `Interval.Contains(1.0)` significa que a paridade ainda é plausível. Uma comparação construída a partir de dois resultados independentes **não tem intervalo** (`Interval` é `null`) — não há rodadas intercaladas para parear, então nada pode ser dito sobre a dispersão.
- **`Outcome`** — o veredicto. `Faster` / `Slower` exigem que a razão saia de uma **faixa morta de 5%** *e* (quando existe um intervalo) que o intervalo exclua 1. Qualquer coisa dentro disso é `Inconclusive`; uma razão que não pôde ser calculada (não finita ou não positiva) é `Unknown`, e o mesmo vale para uma comparação com menos de seis rodadas pareadas utilizáveis (abaixo disso o intervalo não consegue cobrir a mediana com 95% de confiança).
- **`IsFaster`** — `true` apenas para `Outcome == Faster`, ou seja, **mais estreito do que antes**: uma diferença de 3% agora retorna `false` (`Inconclusive`), e uma comparação sem rodadas precisa passar pela mesma faixa morta de 5%. Use `Outcome` para a resposta de três vias e `IsFasterKnown` para distinguir "nenhuma diferença mensurável" de "não foi possível medir de forma alguma".

Custo: uma comparação direta executa cada linha 10 vezes, ou seja, **20 `Run`s** — reduza `SampleCount` / `WarmupIterations` para execuções A/B se isso for lento demais.

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
- **Amostras não mensuráveis** – no modo `CpuOnly`, uma carga de trabalho mais curta que um tick do temporizador do SO é lida como exatamente zero de tempo de CPU. Essa amostra é contada em `Statistics.UnmeasuredSampleCount` (`Unmeasured` é `true`), seu valor por operação passa a ser **um quantum de temporização — um limite superior —, não `0.000`**, e os formatadores imprimem as células por operação como `N/A`. Estatísticas de uma execução assim não são medições do trabalho: leia-as como "abaixo da resolução do relógio de CPU". As contagens de iterações **não** são infladas silenciosamente para esconder o problema — apenas `AutoCalibrateIterations = true` (as predefinições `Quick`/`Precise`) calibra; `Default` usa o `IterationsPerSample` configurado sem alterações.
- **Atribuição de GC** – contadores de GC assíncronos são marcados como aproximados (`GcInfo.IsApproximate`). Alocações de setup/teardown são excluídas nos dois modos.
- **Ciclos de CPU** – o caminho síncrono lê um contador por thread (`QueryThreadCycleTime` no Windows, um `perf_event` aberto para a thread de medição no Linux). Benchmarks assíncronos leem um contador de todo o processo, porque um contador por thread não pode ser seguido entre trocas de thread: esse número inclui trabalho feito por outras threads e é marcado `Statistics.CpuCyclesAreApproximate`, renderizado com um `~` em Console, Markdown, HTML e CSV. O macOS não tem contador de ciclos — `mach_absolute_time` é um proxy de *tempo* monotônico em vez de ciclos arquiteturais, e é marcado da mesma forma.
- **Cancelamento** – `BenchmarkConfig.CancellationToken` só é verificado nos limites de amostra dos benchmarks assíncronos; benchmarks síncronos o ignoram.
- **Classes mistas** – um método `[Benchmark]` síncrono mantém o caminho de medição síncrono direto mesmo quando a classe tem outros membros assíncronos.
- **Warmup** – iterações de warmup chamam apenas o delegado de warmup; `setup`/`teardown` e `[IterationSetup]`/`[IterationCleanup]` não são executados durante o warmup. Ações de warmup devem ser autossuficientes.

---

## Fidelidade de Medição

PicoBench executa em processo, portanto as configurações do CLR afetam os valores absolutos:

- Desative o JIT em camadas: `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0` (ou `COMPlus_*`).
- Para microbenchmarks de thread única, use GC workstation: `DOTNET_gcServer=0`.
- Contagens de ciclos de CPU só estão disponíveis onde o SO permite contadores sem privilégios (Windows `QueryThreadCycleTime`; Linux `perf_event` quando `perf_event_paranoid` é 2 ou menor). Execuções síncronas no Windows são por thread; execuções assíncronas e o macOS usam um contador de todo o processo ou, no macOS, um proxy de relógio monotônico em vez de ciclos reais — ambos são marcados `CpuCyclesAreApproximate`. `EnvironmentInfo` e todos os formatadores informam qual fonte foi usada.
- `BenchmarkConfig.BoostPriorities` (padrão `true`) eleva a prioridade do processo e da thread apenas durante a execução e restaura os valores anteriores ao final.

### O que os números podem e não podem resolver

Toda execução mede o custo que o próprio arnês adiciona por operação com a mesma contagem de iterações e o desconta, e então publica o menor valor que ainda consegue resolver:

| Campo | Significado |
|---|---|
| `Statistics.OverheadNs` | custo por operação do arnês (despacho de delegates, contabilidade do laço, leituras de relógio e de contadores) que foi medido e descontado. `0` para execuções assíncronas, cujo overhead não é descontado |
| `Statistics.ResolutionLimitNanoseconds` | menor valor por operação que esta execução consegue resolver: o maior entre um tick de relógio distribuído pelas iterações, a dispersão entre execuções do overhead descontado e o tamanho por operação desse overhead (seu limite superior) |
| `Statistics.ResolutionLimited` | `Avg` está nesse limite ou abaixo dele, ou pelo menos uma amostra não pôde ser medida de forma alguma. Os formatadores imprimem as células por operação como limite superior (`<limit` nos quatro formatos; o Console acrescenta a marca `[below resolution]` e Markdown/HTML acrescentam `(below resolution)`) em vez de um número |
| `Statistics.Unmeasured` / `UnmeasuredSampleCount` | quantas amostras a fonte de temporização não conseguiu avançar (um tick zero em `CpuOnly`). Essas células imprimem `N/A`, nunca `0.000` |

Portanto, a leitura honesta de um resultado sub-nanossegundo é **"abaixo de `<limit` ns"**, não "0.02 ns": o valor não é medido, é limitado, e o limite cobre a quantização do relógio e a dispersão do overhead descontado (é o maior entre o tick de relógio, a dispersão do overhead descontado e o tamanho por operação desse overhead; e, como o resíduo é truncado em zero, cada amostra satisfaz verdadeiro ≤ resíduo + overhead, então o limite não pode mais ficar abaixo do piso que a mesma execução descontou). Vale lembrar mais duas assimetrias: a dedução do overhead do arnês está implementada apenas para benchmarks síncronos (valores por operação assíncronos ainda carregam seu custo de wrapper por amostra), e bytes de GC não são medidos de forma alguma — apenas contagens de coleta, por processo, aproximadas no caminho assíncrono.

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

### Contrato de Colunas do CSV

`CsvFormatter` escreve uma linha de cabeçalho e depois linhas de dados; uma suite adiciona linhas de comentário `#` (`# Schema:`, `# Suite:`, `# Environment:`, `# CpuCounter:`, `# CpuCounterNote:`, `# GcInfoNote:`, `# Timestamp:`, `# Duration:`) e duas seções, `# Results` e `# Comparisons`, **cada uma com sua própria linha de cabeçalho**. Portanto, um arquivo tem mais de uma linha de cabeçalho — `pandas.read_csv(path, comment='#')` pula os comentários, mas você precisa separar as duas seções sozinho. A primeira linha de um arquivo de resultados é essa linha de contrato `# Schema: v2; …`, e toda linha que comece com `#` é um comentário que o leitor deve pular.

Colunas de resultados, nesta ordem (opções padrão; `IncludePercentiles`, `IncludeGcInfo`, `IncludeCpuCycles` e `IncludeTimestamp` removem suas colunas quando `false`):

| # | Coluna | Significado |
|---|---|---|
| 1 | `Name` | nome do benchmark (entre aspas se contiver `,`, `"` ou uma quebra de linha; um `=`/`+`/`-`/`@` inicial recebe o prefixo `'`) |
| 2 | `Category` | categoria, vazia quando não definida |
| 3 | `Avg_ns` | nanossegundos por operação — três estados de célula, veja abaixo |
| 4 | `P50_ns` | mediana de nanossegundos por operação (os mesmos três estados) |
| 5-12 | `P90_ns`, `P95_ns`, `P99_ns`, `Min_ns`, `Max_ns`, `StdDev_ns`, `StdErr_ns`, `RSD_pct` | percentis e dispersão; `RSD_pct` é um percentual simples, mas os outros sete usam os estados de célula por operação |
| 13 | `CpuCycles` | ciclos por operação; um prefixo `~` marca um contador de todo o processo ou um proxy de relógio (`CpuCyclesAreApproximate`), não ciclos por thread |
| 14-16 | `GC0`, `GC1`, `GC2` | contagens de coleta, ou `N/A` quando o GC não foi coletado |
| 17 | `GCApproximate` | `1` quando os contadores são aproximados (caminho assíncrono), `0` quando são exatos, `N/A` quando não coletados |
| 18 | `SampleCount` | número de amostras (inteiro) |
| 19 | `IterationsPerSample` | iterações por amostra (inteiro) |
| 20 | `Timestamp` | ISO-8601 round-trip (`o`, cultura invariante) |

Estados de célula de uma coluna por operação:

| Célula | Significado |
|---|---|
| `12.5` | um valor medido em nanossegundos (cultura invariante, formato round-trip) |
| `<0.02` | **limite superior**: a execução não conseguiu resolver o valor acima de `Statistics.ResolutionLimitNanoseconds`. O número após `<` é esse limite, e não é um valor medido |
| `N/A` | não medido (`Statistics.Unmeasured`, um delta de relógio zero) ou não coletado (colunas de GC) |

Duas consequências para consumidores: por causa de `<0.02`, uma coluna por operação **não** tem garantia de ser numérica, então leitores como o pandas inferem `object` e você precisa tratar explicitamente as linhas de limite; e `GCApproximate` foi inserido na posição 17, entre `GC2` e `SampleCount`, então **leitores posicionais devem deslocar `SampleCount`/`IterationsPerSample` em um** (leitores baseados em nome não são afetados).

Colunas de comparações: `TestCase`, `Provider`, `Avg_ns`, `Speedup`, `P50_ns`, `P90_ns`, `P99_ns`, `StdErr_ns`, `RSD_pct`, `CpuCycles`, `GC0`, `GC1`, `GC2`, `GCApproximate`. Cada comparação escreve duas linhas — primeiro o candidato, depois a baseline — e `Speedup` é preenchido apenas na linha do candidato. As células por operação das comparações passam pelo mesmo formatador da tabela de resultados, então `<limit` e `N/A` também aparecem lá, e `CpuCycles` leva a mesma marca `~` (`CpuCyclesAreApproximate`).

A linha `# Schema:` já está implementada e é a **primeira linha do arquivo**: um arquivo de resultados (e um arquivo de suite com resultados) começa com o contrato que descreve as colunas abaixo, e uma seção de comparações escreve sua própria linha de contrato logo antes de `# Comparisons`:

```
# Schema: v2; section=results; columns=Name|Category|Avg_ns|P50_ns|P90_ns|P95_ns|P99_ns|Min_ns|Max_ns|StdDev_ns|StdErr_ns|RSD_pct|CpuCycles|GC0|GC1|GC2|GCApproximate|SampleCount|IterationsPerSample|Timestamp; per-op cells (Avg_ns,P50_ns,P90_ns,P95_ns,P99_ns,Min_ns,Max_ns,StdDev_ns,StdErr_ns) = number (nanoseconds) | "<limit" (upper bound: the value is at the harness floor) | "N/A" (not measured); RSD_pct = percent; CpuCycles = count | "~count" (not a thread-scoped cycle count); GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected)
# Schema: v2; section=comparisons; columns=TestCase|Provider|Avg_ns|Speedup|P50_ns|P90_ns|P99_ns|StdErr_ns|RSD_pct|CpuCycles|GC0|GC1|GC2|GCApproximate; per-op cells (Avg_ns,P50_ns,P90_ns,P99_ns,StdErr_ns) = number (nanoseconds) | "<limit" (upper bound: the value is at the harness floor) | "N/A" (not measured); RSD_pct = percent; CpuCycles = count | "~count" (not a thread-scoped cycle count); GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected)
```

`v2` é a forma do Batch 0 — os três estados de célula por operação acima mais `GCApproximate`. A lista `columns=` é derivada da linha de cabeçalho realmente escrita, então não pode divergir das tabelas acima e nunca nomeia uma coluna que uma opção removeu; `section=` separa as duas linhas de contrato. Um leitor deve pular toda linha que comece com `#` e obter a ordem das colunas da lista `columns=` em vez de codificá-la.

---

## Modelo de Resultados

| Tipo | Descrição |
|------|-------------|
| `BenchmarkResult` | Nome, Categoria, Tags, Estatísticas, Amostras, IterationsPerSample, SampleCount, Timestamp |
| `ComparisonResult` | Nome, Categoria, Tags, Baseline, Candidate, Speedup, ImprovementPercent, **Outcome**, **IsFasterKnown**, **Interval**, IsFaster |
| `BenchmarkSuite` | Nome, Descrição, Results, Comparisons, Environment, Duration, Timestamp |
| `ComparisonOutcome` | Enum: `Unknown` (nenhuma razão pôde ser calculada), `Inconclusive` (dentro da faixa morta de 5%, ou o intervalo ainda contém a paridade), `Faster`, `Slower` |
| `ComparisonInterval` | Low, High, Contains(value), ToString() — intervalo de 95% livre de distribuição da mediana da razão pareada |
| `Statistics` | Avg, P50, P90, P95, P99, Min, Max, StdDev, StandardError, RelativeStdDevPercent, CpuCyclesPerOp, **CpuCyclesAreApproximate**, **OverheadNs**, **ResolutionLimitNanoseconds**, **ResolutionLimited**, **Unmeasured**, **UnmeasuredSampleCount**, GcInfo, HasHighVariance |
| `TimingSample` | ElapsedNanoseconds, ElapsedMilliseconds, ElapsedTicks, CpuCycles, **CpuCyclesAreApproximate**, GcInfo |
| `GcInfo` | Gen0, Gen1, Gen2, Total, IsZero, IsApproximate |
| `EnvironmentInfo` | Os, Architecture, RuntimeVersion, ProcessorCount, modo de execução, Configuration, tipo / disponibilidade / significado do contador de CPU, tags personalizadas |

Semântica dos membros adicionados pelo trabalho de fidelidade de medição:

| Membro | Contrato |
|---|---|
| `ComparisonResult.Outcome` | Veredicto de três vias; `Faster`/`Slower` precisam da faixa morta de 5% **e** de um intervalo que exclua 1 |
| `ComparisonResult.IsFasterKnown` | `Outcome != Unknown` — "as duas linhas foram comparadas" (podem continuar indistinguíveis) |
| `ComparisonResult.IsFaster` | Apenas `Outcome == Faster`. Mais estreito que o antigo `Speedup > 1.0`: uma diferença dentro da faixa morta não é mais "mais rápido" |
| `ComparisonResult.Interval` | `ComparisonInterval?`; `null` quando a comparação foi construída a partir de dois resultados independentes em vez de rodadas intercaladas |
| `Statistics.ResolutionLimited` | O valor por operação é um limite superior (`<limit`), não uma medição |
| `Statistics.UnmeasuredSampleCount` / `Unmeasured` | Amostras para as quais a fonte de temporização não conseguiu avançar; suas células por operação são `N/A` |
| `Statistics.CpuCyclesAreApproximate` | `CpuCyclesPerOp` é de todo o processo (assíncrono) ou um proxy de relógio monotônico (macOS), não ciclos por thread: os formatadores o prefixam com `~` |
| `TimingSample.CpuCyclesAreApproximate` | A mesma marca no nível da amostra; `Statistics` a agrega com OR lógico |

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
| Temporização | Stopwatch | Stopwatch | Stopwatch |
| Monitoramento de GC (Gen0/1/2) | Sim | Sim | Sim |
| Contagem de ciclos de CPU, síncrona | `QueryThreadCycleTime` (por thread) | `perf_event_open` por thread (quando `perf_event_paranoid` ≤ 2) | `mach_absolute_time`, um **proxy de tempo** — marcado `~` |
| Contagem de ciclos de CPU, assíncrona | `QueryProcessCycleTime` (todo o processo) — marcado `~` | `perf_event` de todo o processo — marcado `~` | `mach_absolute_time` — marcado `~` |
| Aumento de prioridade de processo | Sim (com escopo, restaurado) | Sim (com escopo, restaurado) | Sim (com escopo, restaurado) |

No macOS, o contador de CPU exportado é um proxy de *tempo* monotônico de alta resolução, e não ciclos arquiteturais: o valor avança com o tempo real, não com as instruções executadas, e por isso é marcado `CpuCyclesAreApproximate` e impresso com `~`. A mesma marca é usada para o caminho assíncrono, cujo contador de todo o processo inclui trabalho feito por outras threads. `EnvironmentInfo` e a saída dos formatadores expõem ambas as distinções explicitamente.

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
