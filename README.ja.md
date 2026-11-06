# PicoBench

[English](README.md) | [简体中文](README.zh.md) | [日本語](README.ja.md) | [Español](README.es.md) | [Português](README.pt.md) | [繁體中文](README.zh-tw.md) | [한국어](README.ko.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

軽量な.NETベンチマークライブラリで、**2つの補完的API**を提供します：命令型APIと、属性ベースでソース生成される、完全に**AOT互換**のAPIです。NuGet 参照は netstandard2.0 で `ValueTask<T>` を補う BCL polyfill（`System.Threading.Tasks.Extensions`）が 1 つだけです。

## 特徴

- **サードパーティ依存なし** — netstandard2.0 で `ValueTask<T>` を補う BCL polyfill（`System.Threading.Tasks.Extensions`）が 1 つだけ。外部パッケージはありません
- **2つのAPI** - アドホックテスト用の命令型API (`Benchmark.Run`)；構造化されたスイート用の属性ベースAPI (`[Benchmark]` + ソースジェネレーター)
- **AOT互換ソースジェネレーター** - インクリメンタルジェネレーターが実行時に直接メソッド呼び出しを生成、リフレクションゼロ
- **クロスプラットフォーム** - Windows、Linux、macOSを完全サポート
- **サブマイクロ秒タイミング** - `Stopwatch` ベースの1操作あたりの計測で、ハーネス自身の1操作あたりコストを実測して差し引きます。実行の分解能限界を下回るワークロードは上限値（`<limit`）として報告され `ResolutionLimited` のフラグが立ちます — ナノ秒精度の数値として報告されることはありません
- **開示された限界** - `OverheadNs`、`ResolutionLimitNanoseconds`、`ResolutionLimited`、`Unmeasured` は、その実行が何を分解できて何を分解できなかったかを示すため、ノイズレベルの結果を高速な操作として読み違えることはありません
- **GC追跡** - ベンチマーク中のGen0/Gen1/Gen2コレクション回数を監視（setup/teardown を除く）
- **CPUサイクルカウント** - 同期パスではスレッドスコープのサイクル数を取得します（Windows `QueryThreadCycleTime`；Linux では OS が非特権アクセスを許可する場合 `perf_event`）。非同期ベンチマークはプロセス全体のカウンターを読み（スレッド単位のカウンターはスレッド遷移をまたいで追跡できないため）、`CpuCyclesAreApproximate` のフラグが立ちます（4つの全フォーマットで `~`）。macOS にはサイクルカウンターがなく、`mach_absolute_time` はサイクルではなく単調な*時間*プロキシであり、同じフラグが立ちます
- **統計分析** - `Avg`、`P50`、`P90`、`P95`、`P99`、`Min`、`Max`、`StdDev`、`StandardError`（相対標準偏差 `RSD`）。パーセンタイルは隣接する 2 標本の線形補間（統計パッケージでいう *type 7*、Excel `PERCENTILE.INC` と NumPy の既定）で求め、分散は不偏分散（`N-1`）です。標本数が約 20 未満では P99 が最大値に近づくため、テール列は安定した裾を意味しません。
- **複数出力形式** - 4 つの組み込み `IFormatter` フォーマッター（Console、Markdown、HTML、CSV）とプログラム要約出力
- **パラメータ化ベンチマーク** - `[Params]`属性で自動デカルト積反復
- **比較サポート** - ベースラインと候補を交互のラウンドで計測し、判定にはラウンドごとのペア比の中央値を使います。5% のデッドバンドと、同等性を排除する分布非依存の区間の両方が必要なので、ラウンド間のノイズが改善として公表されることはありません
- **設定可能** - Quick、Default、Preciseプリセット、自動キャリブレーション、または完全カスタム設定
- **netstandard2.0** - .NET Framework **4.7.2+**（`RuntimeInformation` を無条件で使用するため、それ以前の .NET Framework バージョンは非対応）、.NET Core 2.0+、.NET 5+と互換性

## インストール

**PicoBench** NuGetパッケージを参照してください。ソースジェネレーター (`PicoBench.Generators`) はアナライザーとして自動的にバンドルされます - 追加の参照は不要です。

```bash
dotnet add package PicoBench
```

## クイックスタート

### 命令型API

```csharp
using PicoBench;

var result = Benchmark.Run("My Benchmark", () =>
{
    Thread.SpinWait(100);
});

Console.WriteLine($"Average: {result.Statistics.Avg:F1} ns/op");
```

### 属性ベースAPI（ソース生成）

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

> クラスは`partial`で**なければなりません**。ソースジェネレーターはコンパイル時に`IBenchmarkClass`実装を生成します - リフレクションなし、完全AOT安全。

> 不正な属性の使い方に対しては、非`partial`クラス、重複した baseline、無効なライフサイクルシグネチャ、互換性のない`[Params]`値などの一般的なミスをジェネレーター診断として報告します。 さらに、`async void`ベンチマーク（PBGEN011）、ジェネリック/ネスト/record/インスタンス化不可のベンチマーククラス（PBGEN012-015）を拒否し、空の`[Params]`（PBGEN016）と基底型のベンチマーク属性（PBGEN017）について警告します。

---

## 命令型APIリファレンス

### 基本ベンチマーク

```csharp
using PicoBench;
using PicoBench.Formatters;

var result = Benchmark.Run("SpinWait", () => Thread.SpinWait(100));
Console.WriteLine(new ConsoleFormatter().Format(result));
```

### 状態付きベンチマーク（クロージャ回避）

```csharp
var data = new byte[1024];
var result = Benchmark.Run("ArrayCopy", data, static d =>
{
    var copy = new byte[d.Length];
    Buffer.BlockCopy(d, 0, copy, 0, d.Length);
});
```

### スコープ付きベンチマーク（DIフレンドリー）

```csharp
var result = Benchmark.RunScoped("DbQuery",
    () => new MyDbContext(),
    static ctx => ctx.Users.FirstOrDefault()
);
// 各サンプルで新しいスコープが作成され、各サンプル後にスコープが破棄されます。
```

非同期バリアントも利用できます:

```csharp
var result = await Benchmark.RunScopedAsync("DbQueryAsync",
    () => new MyDbContext(),
    static async ctx => await ctx.Users.FirstOrDefaultAsync()
);
// 非同期バリアント: 各サンプルで新しいスコープを作成し、各サンプル後に破棄します。
```

### 2つの実装を比較

```csharp
var comparison = Benchmark.Compare(
    "String vs StringBuilder",
    "String Concat",  () => { var s = ""; for (int i = 0; i < 100; i++) s += "a"; },
    "StringBuilder",  () => { var sb = new StringBuilder(); for (int i = 0; i < 100; i++) sb.Append('a'); _ = sb.ToString(); }
);

Console.WriteLine($"Speedup: {comparison.Speedup:F2}x ({comparison.ImprovementPercent:F1}%) [{(comparison.Interval?.ToString() ?? "no interval")}] -> {comparison.Outcome}");
```

- **`Speedup`** — ベースライン時間 ÷ 候補時間で、`> 1` なら候補が高速です。候補が何も計測できなかったラウンドは比率にまったく寄与しません。使用可能な比率が1つも得られない場合、比較は判定を捏造せず `NaN` を報告します（`ImprovementPercent` も同時に `NaN` になります）。2つの独立した `BenchmarkResult` から構築した `ComparisonResult` は、2つの平均値の比率を報告します。 0/0 のラウンドはまさにこのケースで、`1.0` ではなく `NaN` になります（全ゼロの2つの独立した結果から構築した比較では従来どおり `1.0` です）。
- **`Interval`** — その中央値の分布非依存な95%区間（10個のペア比の2番目/9番目の順序統計量）です。`Interval.Contains(1.0)` は同等性がまだ妥当であることを意味します。2つの独立した結果から構築した比較には**区間がありません**（`Interval` は `null`） — ペアにできる交互ラウンドが存在しないため、ばらつきについて何も言えません。
- **`Outcome`** — 判定です。`Faster` / `Slower` は、比率が**5%のデッドバンド**を外れること*に加えて*、（区間が存在する場合は）区間が1を排除することを必要とします。その内側にあるものは `Inconclusive`、計算できなかった比率（非有限または非正）は `Unknown` であり、使用可能なペアラウンドが6未満の比較も同様です（それを下回ると区間が中央値を95%信頼度で覆えません）。
- **`IsFaster`** — `Outcome == Faster` の場合のみ `true` です。つまり**以前より狭い**定義で、3%の差は `false`（`Inconclusive`）を返し、ラウンドを持たない比較も同じ5%のデッドバンドを通過する必要があります。三者択の答えには `Outcome`、「測定可能な差がない」と「まったく測定できなかった」の区別には `IsFasterKnown` を使用してください。

コスト: 直接比較は各レーンを10回ずつ、つまり**20 `Run`** を実行します — 遅すぎる場合は A/B 実行で `SampleCount` / `WarmupIterations` を下げてください。

### 詳細：独立したウォームアップ、セットアップ、ティアダウン

```csharp
var result = Benchmark.Run(
    name:     "Custom",
    action:   () => DoWork(),
    warmup:   () => DoWork(),      // nullでウォームアップスキップ
    config:   BenchmarkConfig.Precise,
    setup:    () => PrepareState(), // 各サンプル前に呼び出し（計時対象外）
    teardown: () => CleanUp()       // 各サンプル後に呼び出し（計時対象外）
);
```

---

## 属性ベースAPIリファレンス

`[BenchmarkClass]`で**partial**クラスを修飾し、そのメソッド/プロパティを以下の属性で修飾します。ソースジェネレーターがコンパイル時にすべての接続コードを生成します。

### 属性

| 属性 | ターゲット | 説明 |
|-----------|--------|-------------|
| `[BenchmarkClass]` | クラス | コード生成のためクラスをマーク。オプションの`Description`プロパティ。 |
| `[Benchmark]` | メソッド | パラメータなしメソッドをベンチマークとしてマーク。参照メソッドには`Baseline = true`を設定。オプションの`Description`。 |
| `[Params(values)]` | プロパティ / フィールド | 指定されたコンパイル時定数値を反復。複数の`[Params]`プロパティはデカルト積を生成。 |
| `[GlobalSetup]` | メソッド | 各パラメータ組み合わせごとに**1回呼び出し**、ベンチマーク実行前。 |
| `[GlobalCleanup]` | メソッド | 各パラメータ組み合わせごとに**1回呼び出し**、ベンチマーク実行後。 |
| `[IterationSetup]` | メソッド | 各サンプル**前に呼び出し**（計時対象外）。 |
| `[IterationCleanup]` | メソッド | 各サンプル**後に呼び出し**（計時対象外）。 |

`[Benchmark]`とライフサイクルメソッドは、インスタンスメソッドであり、ジェネリックではなく、引数なしでなければなりません。戻り値は`void`、`Task`、`ValueTask`、`Task<T>`、`ValueTask<T>`のいずれかで、非同期メソッドは await され、戻り値は破棄されます。`[Benchmark]`メソッドを`async void`にすることはできません（PBGEN011）。`[Params]`の対象は書き込み可能なインスタンスプロパティ、または`readonly`でないインスタンスフィールドでなければなりません。ベンチマーククラスは非ジェネリック、非ネスト、非抽象で、public な引数なしコンストラクターを持つ必要があります。

### 完全な例

```csharp
using PicoBench;

[BenchmarkClass(Description = "文字列連結戦略の比較")]
public partial class StringBenchmarks
{
    [Params(10, 100, 1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() { /* 現在のNのデータを準備 */ }

    [GlobalCleanup]
    public void Cleanup() { /* リソースを解放 */ }

    [IterationSetup]
    public void BeforeSample() { /* サンプルごとの準備 */ }

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

### 実行

```csharp
// 内部でインスタンス作成：
var suite = BenchmarkRunner.Run<StringBenchmarks>(BenchmarkConfig.Quick);

// または事前設定済みインスタンスで：
var instance = new StringBenchmarks();
var suite2 = BenchmarkRunner.Run(instance, BenchmarkConfig.Quick);
```

## 非同期ベンチマーク

両方の API が非同期処理をサポートします。非同期ベンチマークメソッドはイテレーションごとに await され、ライフサイクルメソッドは同じクラス内で同期と非同期を混在できます。

```csharp
// 命令型
var result = await Benchmark.RunAsync("Http call", async () =>
{
    using var response = await httpClient.GetAsync(url);
});

// サンプルごとの非同期 setup/teardown
var result2 = await Benchmark.RunAsync(
    "With lifecycle",
    action: async () => await DoWorkAsync(),
    warmup: async () => await DoWorkAsync(),
    config: BenchmarkConfig.Quick,
    setup: async () => await ResetStateAsync(),
    teardown: async () => await DisposeStateAsync());

// 状態を持つ非同期（クロージャ割り当てなし。setup/teardown オーバーロードなし）
var result3 = await Benchmark.RunAsync("Stateful", state, async s => await ProcessAsync(s));

// 非同期スコープ（DI フレンドリー）
var result4 = await Benchmark.RunScopedAsync("Scoped",
    () => container.CreateScope(),
    async scope => await scope.Service.DoWorkAsync());
```

属性ベースのクラスでは、非同期ベンチマークと非同期ライフサイクルメソッドを宣言できます:

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

- **計測モード** – `AsyncTimingMode.WallClock`（既定）は await による中断を含む総時間を計測します。`AsyncTimingMode.CpuOnly` は `Process.TotalProcessorTime` を計測し、I/O 待ちを除外します。CPU クロックは OS のタイマーティック（通常 10〜16 ms）単位でしか進まないため、自動キャリブレーションは最小サンプル予算を実測したクロック粒度まで引き上げます。CpuOnly モードでは GC データを収集しません。
- **計測不能なサンプル** – `CpuOnly` モードでは、1回の OS タイマーティックより短いワークロードは CPU 時間がちょうどゼロとして読まれます。そのようなサンプルは `Statistics.UnmeasuredSampleCount` に数えられ（`Unmeasured` が `true`）、1操作あたりの値は `0.000` ではなく**1タイミング量子 — 上限値**になり、フォーマッターは1操作あたりのセルを `N/A` と表示します。そのような実行の統計は処理の測定値ではありません。「CPU クロックの分解能を下回っている」と読んでください。問題を隠すためにイテレーション回数が黙って水増しされることはありません — キャリブレーションするのは `AutoCalibrateIterations = true`（`Quick`/`Precise` プリセット）の場合のみで、`Default` は設定された `IterationsPerSample` をそのまま使います。
- **GC の帰属** – 非同期の GC カウントは近似値としてマークされます（`GcInfo.IsApproximate`）。setup/teardown の割り当てはどちらのモードでも除外されます。
- **CPU サイクル** – 同期パスはスレッドスコープのカウンターを読みます（Windows では `QueryThreadCycleTime`、Linux では計測スレッド用に開いた `perf_event`）。非同期ベンチマークはプロセス全体のカウンターを読みます（スレッド単位のカウンターはスレッド遷移をまたいで追跡できないため）。その数値には他のスレッドが行った処理が含まれ、`Statistics.CpuCyclesAreApproximate` でフラグが立ち、Console、Markdown、HTML、CSV で `~` 付きで描画されます。macOS にはサイクルカウンターがなく、`mach_absolute_time` はアーキテクチャ上のサイクルではなく単調な*時間*プロキシであり、同じフラグが立ちます。
- **キャンセル** – `BenchmarkConfig.CancellationToken` は非同期ベンチマークのサンプル境界でのみ確認されます。同期ベンチマークは無視します。
- **混在クラス** – クラスに他の非同期メンバーがあっても、同期 `[Benchmark]` メソッドは直接の同期計測パスを使います。
- **ウォームアップ** – ウォームアップのイテレーションはウォームアップデリゲートのみを呼び出します。`setup`/`teardown` と `[IterationSetup]`/`[IterationCleanup]` はウォームアップ中には実行されません。ウォームアップ処理は自己完結させてください。

---

## 計測の忠実度

PicoBench はインプロセスで実行されるため、CLR の設定が絶対値に影響します。

- 階層化 JIT を無効化: `DOTNET_TieredCompilation=0`、`DOTNET_TieredPGO=0`（または `COMPlus_*`）。
- シングルスレッドのマイクロベンチマークではワークステーション GC を使用: `DOTNET_gcServer=0`。
- CPU サイクル数は OS が非特権カウンターを許可する場合にのみ利用できます（Windows `QueryThreadCycleTime`；Linux では `perf_event_paranoid` が 2 以下の場合の `perf_event`）。Windows の同期実行はスレッドスコープで、非同期実行と macOS はプロセス全体のカウンターを、macOS では実際のサイクルの代わりに単調クロックプロキシを使用します — どちらも `CpuCyclesAreApproximate` でフラグが立ちます。`EnvironmentInfo` とすべてのフォーマッターが使用されたソースを報告します。
- `BenchmarkConfig.BoostPriorities`（既定 `true`）は実行中のみプロセスとスレッドの優先度を上げ、終了後に元の値へ戻します。

### 数値が分解できること・できないこと

すべての実行は、同じイテレーション回数でハーネス自身が1操作あたりに追加するコストを計測して差し引き、その後、まだ分解できる最小値を公表します:

| フィールド | 意味 |
|---|---|
| `Statistics.OverheadNs` | 計測・差し引かれたハーネスの1操作あたりコスト（デリゲートディスパッチ、ループの管理、クロックとカウンターの読み取り）。非同期実行ではオーバーヘッドを差し引かないため `0` |
| `Statistics.ResolutionLimitNanoseconds` | この実行が分解できる最小の1操作あたりの値: クロック1ティックをイテレーション数で割った値、差し引いたオーバーヘッドの実行間ばらつき、そのオーバーヘッドの1操作あたりの大きさ（上限）のうち最大のもの |
| `Statistics.ResolutionLimited` | `Avg` がその限界以下であるか、少なくとも1つのサンプルがまったく計測できなかったことを示します。フォーマッターは1操作あたりのセルを、数値ではなく上限値（4形式すべてで `<limit`。Console は `[below resolution]`、Markdown と HTML は `(below resolution)` を付けます）として表示します |
| `Statistics.Unmeasured` / `UnmeasuredSampleCount` | タイミングソースがまったく進めなかったサンプル数（`CpuOnly` のゼロティック）。それらのセルは `0.000` ではなく `N/A` と表示されます |

したがって、サブナノ秒の結果に対する正直な読み方は「`<limit` ns 未満」であり、「0.02 ns」ではありません: その値は測定されたのではなく上限で区切られており、その上限はクロックの量子化と差し引いたオーバーヘッドのばらつきを覆います（クロックティック、差し引いたオーバーヘッドのばらつき、そのオーバーヘッドの1操作あたりの大きさのうち最大のものであり、残差はゼロでクランプされるため各サンプルは「真値 ≤ 残差 + オーバーヘッド」を満たします。したがってこの上限が同じ実行で差し引いた下限を下回ることはもうありません）。さらに2つの非対称性に留意してください: ハーネスのオーバーヘッド差し引きは同期ベンチマークにのみ実装されており（非同期の1操作あたりの値は依然としてサンプルごとのラッパーコストを抱えます）、GC バイト数はまったく計測されません — プロセス単位の収集回数のみで、非同期パスでは近似値です。

---

---

## 設定

### プリセット

| プリセット | ウォームアップ | サンプル数 | 基本反復数/サンプル | 自動キャリブレーション | 使用例 |
|--------|--------|---------|----------------------|------------------------|----------|
| `Quick` | 100 | 10 | 1,000 | はい | 高速反復 / CI |
| `Default` | 1,000 | 100 | 10,000 | いいえ | 一般的なベンチマーク |
| `Precise` | 5,000 | 200 | 50,000 | はい | 最終測定 |

### カスタム設定

```csharp
var config = new BenchmarkConfig
{
    WarmupIterations    = 500,
    SampleCount         = 50,
    IterationsPerSample = 5000,
    RetainSamples       = true,  // 生のTimingSampleデータを保持
    AutoCalibrateIterations = true,
    MinSampleTime       = TimeSpan.FromMilliseconds(0.5),
    MaxAutoIterationsPerSample = 1_000_000,
    ForceGcBeforeBenchmark = true,  // false でベンチマーク前の強制フル GC をスキップ
    BoostPriorities     = true,   // 実行中だけプロセス/スレッドの優先度を上げ、終了後に元へ戻す
    TimingMode          = AsyncTimingMode.WallClock, // または CpuOnly（非同期のみ）
    CancellationToken   = default,                   // 非同期のみ。同期ベンチマークは無視します
};

var result = Benchmark.Run("Test", action, config);
```

自動キャリブレーションを有効にすると、PicoBench は最小サンプル時間の予算に達するか、`MaxAutoIterationsPerSample` に達するまで `IterationsPerSample` を増やします。これは、タイマーノイズの影響を受けやすい極めて高速な処理で特に有効です。 多数のベンチマークとパラメーターの組み合わせを実行する場合、`ForceGcBeforeBenchmark = false` に設定するとコレクション段階前の強制フル GC をスキップできます。自動キャリブレーションは**オプトイン**です（`AutoCalibrateIterations` の既定値は `false` で、`Default` プリセットも設定された反復数を変更しません）。PicoBench が結果を解決可能に見せるために反復数を黙って水増しすることはありません — `CpuOnly` モードで引き上げられる予算は実測した CPU クロック粒度で、それより短い値はゼロとして読まれるためです。

---

## 出力フォーマッター

5つの組み込みフォーマッターが`IFormatter`を実装：

```csharp
using PicoBench.Formatters;

var console  = new ConsoleFormatter();     // ボックス描画コンソールテーブル
var markdown = new MarkdownFormatter();    // GitHub対応Markdown
var html     = new HtmlFormatter();        // スタイル付きHTMLレポート
var csv      = new CsvFormatter();         // データ分析用CSV

// 比較要約の静的ヘルパー：
Console.WriteLine(SummaryFormatter.Format(suite.Comparisons));
```

4 つのフォーマッターはすべて同じ 1 操作あたりの指標 — `Avg`、`P50`、`P90`、`P95`、`P99`、`StandardError`、相対標準偏差（`RSD (%)` 列）— と、利用可能な場合の CPU カウンター注記を表示します。Console のスイート表にも `StdErr` と `RSD` 列があります（以前は欠けていたため、ノイズの信号が既定の出力に現れませんでした）。Console、Markdown、HTML は同じデータに同じ表の単位を使い、CSV は常にナノ秒のままです（下記参照）。

### 精度プロファイル：raw と display

1 つの数値には 2 つの役割があり、フォーマッターはそれぞれが折衷するのではなく、両者を分けます。値は **raw**（実測そのままのナノ秒）か **display**（その表に合う単位へ換算した同じ値）のどちらかです。

- **Raw** (`MetricPrecision.Raw`): 裸のナノ秒で、列見出し（または CSV 列名 `Avg_ns`、`StdErr_ns` など）が `ns` と明示します。データにどの単位が似合うかに関係なく `CsvFormatter` は常にこの形で書きます — 単位がデータの桁で変わるデータファイルは機械可読ではありません。
- **Display** (`MetricPrecision.Display`): 表のすべての 1 操作あたり列に対して**1 つ**の単位を選び — 1 000 ns 未満は `ns`、1 000 µs 未満は `µs`、1 000 ms 未満は `ms`、それ以上は秒 — その単位を見出しに書きます（`Avg (µs)`）。セルごとではなく表ごとに 1 単位なので、列は比較可能なままです。5 秒のベンチマークは `Avg (s)` の下で `5.0` と読め、同じ見出しの下で 150 ns の隣は `0.0` と読めます（黙って単位が変わることはありません）。

```csharp
var raw = new FormatterOptions { PrecisionProfile = MetricPrecision.Raw };
Console.WriteLine(new ConsoleFormatter(raw).Format(suite)); // "Avg (ns)" の下に 5000000000.0
```

Console、Markdown、HTML で表示される文字列は完全に同一で（テストが 1 文字ずつ照合します）、生の数値は常に CSV にあります。したがって換算後の表示だけが測定値の唯一のコピーになることはありません。

### フォーマット対象

```csharp
formatter.Format(result);               // 単一のBenchmarkResult
formatter.Format(results);              // IEnumerable<BenchmarkResult>
formatter.Format(comparison);           // 単一のComparisonResult
formatter.Format(comparisons);          // IEnumerable<ComparisonResult>
formatter.Format(suite);                // 完全なBenchmarkSuite
```

### フォーマッターオプション

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
    PrecisionProfile      = MetricPrecision.Display, // または .Raw；CSV は常に raw
    OutputDirectory       = "results", // Used by WriteToFile methods
};

var formatter = new ConsoleFormatter(options);
// 他にも利用可能：FormatterOptions.Default, .Compact, .Minimal
```

### 結果の保存

```csharp
var dir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(dir);

File.WriteAllText(Path.Combine(dir, "results.md"),   new MarkdownFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.html"), new HtmlFormatter().Format(suite));
File.WriteAllText(Path.Combine(dir, "results.csv"),  new CsvFormatter().Format(suite));
```

### CSV 列の契約

`CsvFormatter` はヘッダー行を書き、続けてデータ行を書きます。スイートは `#` コメント行（`# Schema:` 契約行、`# key,value` メタデータ行: `Suite`、`Description`、`Environment`、`CpuCounter`、`CpuCounterNote`、`GcInfoNote`、`Timestamp`、`Duration`、`NoiseNote`）と2つのセクション `# Results` と `# Comparisons` を追加し、**それぞれが独自のヘッダー行**を持ちます。したがってファイルには複数のヘッダー行があるため、`pandas.read_csv(path, comment='#')` はコメントをスキップしますが、2つのセクションは自分で分割する必要があります。結果ファイルの先頭行はこの `# Schema: v2; …` 契約行であり、`#` で始まる行はすべてパーサーがスキップすべきコメントです。

メタデータ行が次の物理行に続くことはありません。値の中の改行は 2 文字 `\n`（CRLF・CR・LF を同様に）として書かれ、必要なら CSV の引用符が付きます。これにより、行をまたぐ説明でも `comment='#'` が安全になります。

結果の列（この順序、既定オプション。`IncludePercentiles`、`IncludeGcInfo`、`IncludeCpuCycles`、`IncludeTimestamp` を `false` にすると対応する列が消えます）:

| # | 列 | 意味 |
|---|---|---|
| 1 | `Name` | ベンチマーク名（`,` `"` または改行を含む場合は引用符で囲み、先頭の `=`/`+`/`-`/`@` には `'` を前置） |
| 2 | `Category` | カテゴリ。未設定の場合は空 |
| 3 | `Avg_ns` | 1操作あたりのナノ秒 — セルには3つの状態があります（下記参照） |
| 4 | `P50_ns` | 1操作あたりの中央値ナノ秒（同じ3つの状態） |
| 5-12 | `P90_ns`, `P95_ns`, `P99_ns`, `Min_ns`, `Max_ns`, `StdDev_ns`, `StdErr_ns`, `RSD_pct` | パーセンタイルとばらつき。`RSD_pct` は単なるパーセンテージですが、他の7つは1操作あたりのセル状態を使います |
| 13 | `CpuCycles` | 1操作あたりのサイクル数。`~` 接頭辞はスレッドサイクルではなくプロセス全体のカウンターまたはクロックプロキシ（`CpuCyclesAreApproximate`）を示します |
| 14-16 | `GC0`, `GC1`, `GC2` | 収集回数。GC が収集されなかった場合は `N/A` |
| 17 | `GCApproximate` | カウンターが近似値（非同期パス）なら `1`、正確なら `0`、収集されなかった場合は `N/A` |
| 18 | `SampleCount` | サンプル数（整数） |
| 19 | `IterationsPerSample` | サンプルあたりのイテレーション数（整数） |
| 20 | `Timestamp` | ISO-8601 ラウンドトリップ（`o`、インバリアントカルチャ） |

1操作あたりの列のセル状態:

| セル | 意味 |
|---|---|
| `12.5` | 測定されたナノ秒値（インバリアントカルチャ、ラウンドトリップ形式） |
| `<0.02` | **上限値**: 実行が `Statistics.ResolutionLimitNanoseconds` を超える値を分解できませんでした。`<` の後ろの数値がその限界であり、測定値ではありません |
| `N/A` | 計測されなかった（`Statistics.Unmeasured`、クロック差分がゼロ）か、収集されなかった（GC 列） |

消費側への2つの影響: `<0.02` のため、1操作あたりの列が**数値であることは保証されません**。そのため pandas のようなリーダーは `object` と推論し、上限の行を明示的に扱う必要があります。また `GCApproximate` は `GC2` と `SampleCount` の間の位置17に挿入されたため、**位置ベースのリーダーは `SampleCount`/`IterationsPerSample` を1つずらす必要があります**（名前ベースのリーダーは影響を受けません）。

比較の列: `TestCase`、`Provider`、`Avg_ns`、`Speedup`、`P50_ns`、`P90_ns`、`P99_ns`、`StdErr_ns`、`RSD_pct`、`CpuCycles`、`GC0`、`GC1`、`GC2`、`GCApproximate`、`SampleCount`、`IterationsPerSample`。各比較は2行（候補が先、ベースラインが後）を書き、`Speedup` は候補の行にのみ入ります。比較の1操作あたりのセルは結果テーブルと同じフォーマッターを通るため、`<limit` と `N/A` はそこにも現れ、`CpuCycles` には同じ `~` マーカー（`CpuCyclesAreApproximate`）が付きます。

`# Schema:` 行はすでに実装済みで、**ファイルの先頭行**です。結果ファイル（および結果を持つスイートファイル）は、続く列を説明する契約行から始まり、比較セクションは `# Comparisons` の直前に自分自身の契約行を書きます:

```
# Schema: v2; section=results; columns=Name|Category|Avg_ns|P50_ns|P90_ns|P95_ns|P99_ns|Min_ns|Max_ns|StdDev_ns|StdErr_ns|RSD_pct|CpuCycles|GC0|GC1|GC2|GCApproximate|SampleCount|IterationsPerSample|Timestamp; per-op cells (Avg_ns,P50_ns,P90_ns,P95_ns,P99_ns,Min_ns,Max_ns,StdDev_ns,StdErr_ns) = number (nanoseconds) | "<limit" (upper bound: the value is at the harness floor) | "N/A" (not measured); RSD_pct = percent; SampleCount = samples taken | IterationsPerSample = iterations per sample; CpuCycles = count | "~count" (not a thread-scoped cycle count); GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected)
# Schema: v2; section=comparisons; columns=TestCase|Provider|Avg_ns|Speedup|P50_ns|P90_ns|P99_ns|StdErr_ns|RSD_pct|CpuCycles|GC0|GC1|GC2|GCApproximate|SampleCount|IterationsPerSample; per-op cells (Avg_ns,P50_ns,P90_ns,P99_ns,StdErr_ns) = number (nanoseconds) | "<limit" (upper bound: the value is at the harness floor) | "N/A" (not measured); RSD_pct = percent; Speedup = ratio (baseline avg / candidate avg, or the median of the per-round paired ratios for a direct comparison) | "N/A" (no finite ratio: a candidate average of zero is unbounded, a non-finite candidate average is unmeasurable, or too few usable paired rounds); SampleCount = samples taken | IterationsPerSample = iterations per sample; CpuCycles = count | "~count" (not a thread-scoped cycle count); GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected)
```

`v2` は Batch 0 の形 — 上記の1操作あたりの3つのセル状態に `GCApproximate` を加えたものです。`columns=` の一覧は実際に書かれるヘッダー行から生成されるため、上記の表とずれることはなく、オプションで消えた列を挙げることもありません。`section=` が2つの契約行を区別します。パーサーは `#` で始まるすべての行をスキップし、列順はハードコードせず `columns=` の一覧から読む必要があります。

---

## 結果モデル

| タイプ | 説明 |
|------|-------------|
| `BenchmarkResult` | 名前、カテゴリ、タグ、統計、サンプル、サンプルあたり反復数、サンプル数、タイムスタンプ |
| `ComparisonResult` | 名前、カテゴリ、タグ、ベースライン、候補、高速化、改善率、**Outcome**、**IsFasterKnown**、**Interval**、高速かどうか |
| `BenchmarkSuite` | 名前、説明、結果、比較、環境、期間、タイムスタンプ |
| `ComparisonOutcome` | 列挙型: `Unknown`（比率を計算できなかった）、`Inconclusive`（5% デッドバンド内、または区間が依然として同等性を含む）、`Faster`、`Slower` |
| `ComparisonInterval` | Low, High, Contains(value), ToString() — 中央値ペア比の分布非依存な95%区間 |
| `Statistics` | `Avg`、`P50`、`P90`、`P95`、`P99`、`Min`、`Max`、`StdDev`、`StandardError`、`RelativeStdDevPercent`、`CpuCyclesPerOp`、**CpuCyclesAreApproximate**、**OverheadNs**、**ResolutionLimitNanoseconds**、**ResolutionLimited**、**Unmeasured**、**UnmeasuredSampleCount**、GcInfo、HasHighVariance |
| `TimingSample` | 経過ナノ秒、経過ミリ秒、経過ティック、CPUサイクル、**CpuCyclesAreApproximate**、GcInfo |
| `GcInfo` | Gen0、Gen1、Gen2、合計、ゼロか、IsApproximate |
| `EnvironmentInfo` | OS、アーキテクチャ、ランタイムバージョン、プロセッサ数、実行モード、設定、CPUカウンター種別 / 可用性 / 意味の有無、カスタムタグ |

計測忠実度の作業で追加されたメンバーの意味:

| メンバー | 契約 |
|---|---|
| `ComparisonResult.Outcome` | 三者択の判定。`Faster`/`Slower` には 5% デッドバンド**と**1を排除する区間の両方が必要 |
| `ComparisonResult.IsFasterKnown` | `Outcome != Unknown` — 「2つのレーンは比較された」（まだ区別できない場合もあります） |
| `ComparisonResult.IsFaster` | `Outcome == Faster` の場合のみ。旧来の `Speedup > 1.0` より狭く、デッドバンド内の差はもはや「高速」ではありません |
| `ComparisonResult.Interval` | `ComparisonInterval?`。交互ラウンドではなく2つの独立した結果から比較を構築した場合は `null` |
| `Statistics.ResolutionLimited` | 1操作あたりの値が測定値ではなく上限値（`<limit`）であることを示します |
| `Statistics.UnmeasuredSampleCount` / `Unmeasured` | タイミングソースが進めなかったサンプル。それらの1操作あたりのセルは `N/A` |
| `Statistics.CpuCyclesAreApproximate` | `CpuCyclesPerOp` がスレッドサイクルではなくプロセス全体（非同期）または単調クロックプロキシ（macOS）であることを示します。フォーマッターは `~` を前置します |
| `TimingSample.CpuCyclesAreApproximate` | サンプルレベルで同じフラグ。`Statistics` は論理和で集約します |

---

## アーキテクチャ

```
src/
+-- PicoBench/                        # メインライブラリ (netstandard2.0)
|   +-- Benchmark.cs                   # 命令型API (Run, Compare, RunScoped)
|   +-- BenchmarkRunner.cs             # 属性ベースエントリーポイント (Run<T>)
|   +-- BenchmarkConfig.cs             # プリセット付き設定
|   +-- Attributes.cs                  # 7つのベンチマーク属性
|   +-- IBenchmarkClass.cs             # ジェネレーターが生成するインターフェース
|   +-- Runner.cs                      # 低レベルのタイミングフローとサンプル生成
|   +-- Runner.Gc.cs                   # GC ベースラインと差分追跡
|   +-- Runner.Cpu.cs                  # プラットフォーム固有の CPU カウンター実装
|   +-- StatisticsCalculator.cs        # パーセンタイル / 統計計算
|   +-- Models.cs                      # 結果タイプ
|   +-- Formatters/
|       +-- IFormatter.cs              # IFormatter, FormatterOptions & FormatterBase
|       +-- ConsoleFormatter.cs        # ボックス描画コンソールテーブル
|       +-- MarkdownFormatter.cs       # GitHub Markdownテーブル
|       +-- HtmlFormatter.cs           # スタイル付きHTMLレポート
|       +-- CsvFormatter.cs            # CSVエクスポート
|       +-- SummaryFormatter.cs        # 勝敗要約
|
+-- PicoBench.Generators/            # ソースジェネレーター (netstandard2.0)
    +-- BenchmarkGenerator.cs          # IIncrementalGeneratorエントリーポイント
    +-- BenchmarkClassAnalyzer.cs      # Roslyn 解析と診断
    +-- CSharpLiteralFormatter.cs      # 生成されるパラメーター用の C# リテラル整形
    +-- DiagnosticDescriptors.cs       # ジェネレーター診断定義
    +-- Emitter.cs                     # C#コードエミッター (AOT安全)
    +-- Models.cs                      # Roslyn分析モデル
```

---

## プラットフォーム固有機能

| 機能 | Windows | Linux | macOS |
|---------|---------|-------|-------|
| タイミング | Stopwatch | Stopwatch | Stopwatch |
| GC追跡 (Gen0/1/2) | はい | はい | はい |
| CPUサイクルカウント（同期） | `QueryThreadCycleTime`（スレッドスコープ） | スレッドごとの `perf_event_open`（`perf_event_paranoid` ≤ 2 の場合） | `mach_absolute_time`、**時間プロキシ** — `~` フラグ付き |
| CPUサイクルカウント（非同期） | `QueryProcessCycleTime`（プロセス全体） — `~` フラグ付き | プロセス全体の `perf_event` — `~` フラグ付き | `mach_absolute_time` — `~` フラグ付き |
| プロセス優先度ブースト | はい（範囲限定、復元） | はい（範囲限定、復元） | はい（範囲限定、復元） |

macOS では、エクスポートされる CPU カウンターはアーキテクチャ上のサイクル数ではなく、高分解能の単調な*時間*プロキシです。値は実行された命令数ではなく実時間とともに進むため、`CpuCyclesAreApproximate` でフラグが立ち、`~` 付きで表示されます。同じフラグが非同期パスにも使われ、そのプロセス全体のカウンターには他のスレッドが行った処理が含まれます。`EnvironmentInfo` とフォーマッターの出力はどちらの区別も明示的に示します。

---

## サンプル

| サンプル | APIスタイル | 説明 |
|--------|-----------|-------------|
| `StringVsStringBuilder` | 命令型 | `string +=`、`StringBuilder`、容量指定`StringBuilder`の比較 |
| `AttributeBased` | 属性ベース | `[Benchmark]`、`[Params]`、ソースジェネレーターを使用した同じ比較 |
| `CollectionBenchmarks` | 属性ベース | List vs Dictionary vs HashSet検索 - すべての属性を展示 |
| `AsyncBenchmarks` | 属性（非同期） | 非同期エントリーポイントと混在ライフサイクル：`BenchmarkRunner.RunAsync<T>()`、非同期 `[GlobalSetup]`/`[IterationSetup]`、`Task` を返す `[Benchmark]`、`SummaryFormatter` |

```bash
dotnet run --project samples/StringVsStringBuilder -c Release
dotnet run --project samples/AttributeBased -c Release
dotnet run --project samples/CollectionBenchmarks -c Release
dotnet run --project samples/AsyncBenchmarks -c Release
```

---

## BenchmarkDotNetとの比較

| 機能 | PicoBench | BenchmarkDotNet |
|---------|-----------|----------------|
| 依存関係 | BCL polyfill 1 個 | 多数 |
| パッケージサイズ | 小 | 大 |
| ターゲットフレームワーク | netstandard2.0 | net6.0+ |
| AOTサポート | ソースジェネレーター | リフレクションベース |
| 属性API | `[Benchmark]`、`[Params]` | `[Benchmark]`、`[Params]` |
| セットアップ時間 | 即時 | 秒単位 |
| 出力形式 | 5 | 10+ |
| 統計的深さ | 良好 | 広範 |
| 使用例 | 高速A/Bテスト、CI、AOTアプリ | 詳細分析、出版物 |

---

## ライセンス

MITライセンス - 詳細は[LICENSE](LICENSE)ファイルを参照してください。

## ビルドと公開

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

リリースは PicoHex のバージョンルール `<year>.<x>.<y>` に従います。公開 API が変わった場合は **x** を、変わらない場合は **y** を進めます。判定は、直前のリリースの表面を保持する `api/PicoBench.public.txt` の API ベースラインに基づきます。

```bash
# API 差分と次バージョンを確認
pwsh ./scripts/release.ps1 -DryRun

# リリース: api/ を更新し、chore(release): v<version> をコミットし、タグを付けて push
pwsh ./scripts/release.ps1 -Push
```

push されたタグが GitHub Actions のリリースパイプラインを起動し、テスト、タグバージョンでの全パッケージの pack、[NuGet.org](https://www.nuget.org/packages/PicoBench) への公開を行います。兄弟リポジトリのインデックス待ちを埋めるには、同じバージョンを `NuGet.config` で宣言したローカルフィードに pack できます:

```bash
dotnet pack src/PicoBench/PicoBench.csproj -c Release -o artifacts/nupkg -p:Version=<version>
```

## 貢献

1. リポジトリをフォーク
2. 機能ブランチを作成
3. テスト付きで変更を実施
4. プルリクエストを送信
