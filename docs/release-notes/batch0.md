# PicoBench release notes — Batch 0 (measurement contract)

These notes are for **package consumers**: they describe what the next package
built from `main` does differently (version `0.0.0-dev` until the first tag) and
what you have to change, if anything, to keep reading the results correctly.
They deliberately say nothing about how the changes are implemented — the
reference manual is the README: *What the numbers can and cannot resolve*,
*Comparing Two Implementations*, *CSV Column Contract*, *Result Model*.

Batch 0 is about **honesty of the published numbers**, in two directions:

- a result the run could not resolve is now **published as a bound or as "not
  measured"**, never as a small fabricated number;
- a comparison the run could not resolve is now **`Unknown` or `Inconclusive`**,
  never as a winner.

The public API grew by a handful of members and one enum plus one struct; no
existing member was removed and no existing signature changed. Everything below
is therefore a **behaviour** change first and an **addition** second.

## At a glance

| # | Change | Section |
|---|---|---|
| 1 | `ComparisonResult.IsFaster` narrowed; 10 interleaved rounds; `Outcome` / `Interval` / `IsFasterKnown` added | [1](#1-comparison-semantics-are-narrower) |
| 2 | Resolution disclosure: `OverheadNs`, `ResolutionLimitNanoseconds`, `ResolutionLimited`, `<limit` cells | [2](#2-a-result-below-the-resolution-is-published-as-a-bound) |
| 3 | `CpuOnly` samples that measured nothing are `Unmeasured`, not `0.000` | [3](#3-cpuonly-zero-is-not-zero) |
| 4 | CPU cycles are thread-scoped only on the synchronous path; everything else is flagged `~` | [4](#4-cpu-cycles-are-thread-scoped-or-flagged) |
| 5 | CSV schema `v2`: a `# Schema:` first line and a new `GCApproximate` column | [5](#5-csv-has-a-versioned-schema-v2) |
| 6 | Source generator loads on Roslyn 4.8+ (SDK 8); netstandard2.0 floor is .NET Framework 4.7.2 | [6](#6-compatibility) |
| 7 | What is still deliberately imprecise, and what Batch 1 owns | [7](#7-known-limitations-deferred-to-batch-1) |

---

## 1. Comparison semantics are narrower

`Benchmark.Compare(name, baseline, candidate)` used to run each lane once and
report the ratio of two averages; that turned process drift into a verdict (a
comparison of a delegate with itself could claim a speedup). It now measures both
lanes in **10 interleaved rounds** — the lane that goes first alternates every
round — and reports the **median of the per-round paired ratios** with a
distribution-free 95% interval.

**Affected API / output**

- `Benchmark.Compare(...)` — 10 rounds per lane, i.e. about **20 `Run`s** (roughly
  ×20 the cost of the previous one-shot comparison). Same argument list, same
  return type.
- `ComparisonResult.Speedup` — is the median paired ratio for a direct
  comparison, not a ratio of means. It is `NaN` (with `ImprovementPercent` also
  `NaN`) when no round produced a usable ratio — a 0/0 round is exactly that
  case: `NaN`, **not** `1.0`. Fewer than **six** usable paired rounds also give
  `NaN` and no interval, because below that count the sign test cannot claim 95%
  coverage (six is the first count that does: 96.875%). That is not merely a
  missing interval: a comparison with `usable < 6` reports
  `Outcome == Unknown`, hence `IsFasterKnown == false` **and** `IsFaster == false`
  — it is "not measurable", not "not faster".
- `ComparisonResult.IsFaster` — **narrower than before**. It is `true` only for
  `Outcome == Faster`, which requires both leaving the **5% dead band** *and*,
  when an interval exists, that interval excluding 1 (parity). A 3% difference is no
  longer "faster"; it is `Inconclusive`.
- New `ComparisonResult.Outcome` (`ComparisonOutcome`: `Unknown`,
  `Inconclusive`, `Faster`, `Slower`), `ComparisonResult.Interval`
  (`ComparisonInterval?`: `Low`, `High`, `Contains(value)`, `ToString()`) and
  `ComparisonResult.IsFasterKnown` (`Outcome != Unknown`).
- New public types `ComparisonOutcome` (enum) and `ComparisonInterval` (struct).
- Console output of a comparison gains an `Interval: [low, high]` line and a
  `Winner:` line whose wording is the verdict (`none (within the 5% dead band —
  indistinguishable)`, `unknown (not measurable)`, or a lane name).

**Migration**

- Read `Outcome` instead of `IsFaster` if you want the three-way answer, and
  `IsFasterKnown` to tell "no measurable difference" apart from "could not
  measure at all" — `IsFaster == false` no longer means "not faster"; it also
  covers "no verdict".
- Treat `Speedup` as possibly `NaN` and possibly infinite: guard with
  `IsFasterKnown` before formatting, sorting or comparing it. An infinite
  `Speedup` (candidate effectively zero on a comparison built from two
  independent results) reports `Unknown`, so `IsFasterKnown == false`.
- If you need the old cheap one-shot behaviour, build the answer yourself from
  two independent `BenchmarkResult`s: `new ComparisonResult(name, baseline,
  candidate)` still reports the ratio of the two averages, still returns `1.0`
  for an all-zero 0/0 pair, and has **no interval** (`Interval == null`).
- Budget for the extra cost in A/B runs: lower `SampleCount` /
  `WarmupIterations` there if 20 `Run`s is too slow.

## 2. A result below the resolution is published as a bound

Every synchronous run measures the harness' own per-operation cost with the same
iteration count and deducts it, then publishes the smallest value it can still
resolve. A workload at or below that floor is now printed as an **upper bound**
(`<limit`) instead of a too-small number.

**Affected API / output**

- `Statistics.OverheadNs` (new) — the harness cost **one sample** carries (delegate
  dispatch, loop bookkeeping, clock and counter reads), as a per-sample total that
  was measured and deducted; divide it by the result's iteration count for the
  per-operation figure. It is `0` for asynchronous runs in this batch, whose
  overhead was not deducted yet (Batch 0 · 0.1b adds the asynchronous deduction).
- `Statistics.ResolutionLimitNanoseconds` (new) — the smallest per-operation
  value this run can resolve: the largest of one clock tick spread over the
  iterations, the spread of the deducted overhead, and the per-operation size of
  that overhead. It is a **real upper bound**, so a published `<limit` can no
  longer understate the floor the same run deducted.
- `Statistics.ResolutionLimited` (new) — `Avg` is at or below that limit, or at
  least one sample could not be measured at all.
- Formatter output: per-operation cells print `<limit` in **all four formats**
  (CSV, Markdown, HTML, Console). The number after `<` is the limit, not a
  measurement. The formats differ only in the extra marker they attach: Console
  adds `[below resolution]`, Markdown and HTML add `(below resolution)`, CSV
  carries the state in the schema line and in the cell itself.

**Migration**

- A per-operation column is no longer guaranteed numeric. Handle the `<limit`
  form explicitly wherever you parse `Avg_ns` / `P*_ns` / `Min_ns` /
  `Max_ns` / `StdDev_ns` / `StdErr_ns`.
- Do not read `<limit` as "0" or as the limit itself: it means "true value is at
  most this". For a comparison across runs, treat it as a censored observation
  and do not average it with real numbers.
- If a number you used to get is now `<limit`, the previous number was below the
  harness floor; raise `IterationsPerSample` (or use `Precise`) if you need a
  resolved value.
- Your dashboards that grepped for a decimal in these cells need a second
  pattern for `<`; the Console `Overhead:` / `limit` lines are new text as well.

## 3. `CpuOnly` zero is not zero

In `CpuOnly` mode the timing source is a CPU cycle counter, and it may not
advance for a sample shorter than its granularity. Such a sample used to be
published as `0.000` — an unmeasurable result that looked like the fastest one.

**Affected API / output**

- `Statistics.Unmeasured` and `Statistics.UnmeasuredSampleCount` (new) — whether,
  and how many, samples the timing source could not advance for.
- Per-operation cells of such a result print `N/A` (Console: `N/A (unmeasured)`
  plus the `[unmeasured]` marker; Markdown/HTML add `(unmeasured)`), and CSV
  writes `N/A` — never `0.000`.
- `Statistics.ResolutionLimited` is also `true` in that situation (at least one
  sample unmeasured), so a result can be "unmeasured" and "limited" at once; the
  two flags answer different questions (`N/A` vs `<limit`).
- Iteration counts are **not** silently inflated to make a result resolvable:
  auto-calibration stays opt-in (`AutoCalibrateIterations` defaults to `false`).

**Migration**

- Replace "`Avg == 0` means it is fast" with `Unmeasured` / `UnmeasuredSampleCount`
  checks; a zero `Avg` with `Unmeasured == true` carries no measurement at all.
- Handle `N/A` in every per-operation cell (same parser path as `<limit`).
- If you relied on auto-calibration raising iterations for tiny workloads, set
  `AutoCalibrateIterations = true` explicitly.

## 4. CPU cycles are thread-scoped, or flagged

`Statistics.CpuCyclesPerOp` means different things on different paths, and Batch 0
makes that visible instead of letting one number imply the other.

**Affected API / output**

- Synchronous benchmarks: **thread-scoped** cycles (Windows
  `QueryThreadCycleTime`; Linux `perf_event_open` per thread where the OS allows
  unprivileged access).
- Asynchronous benchmarks: a **process-wide** counter, because a per-thread
  counter cannot be followed across thread hops. `Statistics.CpuCyclesAreApproximate`
  (new) is `true` and every formatter prefixes the cell with `~`.
- macOS: there is no cycle counter. `mach_absolute_time` is a monotonic **time**
  proxy, not cycles; the column is kept and flagged `CpuCyclesAreApproximate` and
  printed with `~`.
- `TimingSample.CpuCyclesAreApproximate` (new) carries the same flag per sample;
  `Statistics` aggregates it with logical OR, so one approximate sample makes the
  aggregate approximate.
- The four formats print `~<count>` — a cell is therefore no longer always
  numeric.

**Migration**

- Strip the `~` before parsing `CpuCycles`; `~1234` is "approximately 1234, not
  thread cycles".
- Do not compare an asynchronous `CpuCyclesPerOp` against a synchronous one, or
  apples-to-apples across processes: the async value includes work done by other
  threads.
- On macOS, treat the column as elapsed-time-derived; use `Avg` when you want a
  duration.
- Branch on `Statistics.CpuCyclesAreApproximate` (or `GcInfo.IsApproximate` for
  the GC columns) rather than on the platform name.

## 5. CSV has a versioned schema (v2)

The CSV file now describes itself: the **first line of a results file** is a
contract line that begins `# Schema: v2; section=results; columns=…` and then
spells out the cell states of the columns below it; every line starting with `#`
is a comment. A comparisons section writes its own contract line (`section=comparisons`)
just before `# Comparisons`. The `columns=` list is derived from the header row that
is really written, so it never names a column an option removed; the exact bytes of
both lines are in the README (*CSV Column Contract*) and in the file itself.

**Affected API / output**

- `CsvFormatter` results section: **20 columns** by default, with **`GCApproximate`
  appended right after the GC counters** — position 17, between `GC2` and
  `SampleCount`. `1` = approximate counters (async path), `0` = exact, `N/A` =
  GC not collected.
- Per-operation cells have **three states**: a number in nanoseconds (invariant
  culture), `<limit` (upper bound, at the harness floor) or `N/A` (not measured).
- `Speedup` (comparisons section, candidate row only) is a **ratio number** in
  the invariant `G` form (`2.5`, `2`), `Infinity` for an infinite ratio (∞ — the
  candidate measured effectively zero, reachable on a comparison built from two
  independent results, and reported as `Unknown`), or `NaN` when no round
  produced a usable ratio (a 0/0 round, or any direct comparison with fewer than
  six usable paired rounds — see §1). It is **not** `N/A`: that state belongs to a
  per-operation cell the run did not measure, and the baseline row leaves this
  cell empty.
- `CpuCycles` may be `~count`; GC counters may be `N/A`.
- New comment lines exist and must be skipped: `# Schema:` (per section) and, when
  the suite is noisy (RSD ≥ 10%), `# NoiseNote: high variance …`.

**Migration from v1 (minimal)**

1. Skip every line that starts with `#` — `pandas.read_csv(path, comment='#')`
   does it, and you still have to split the `# Results` and `# Comparisons`
   sections yourself (each has its own header row).
2. Read the column order from the `columns=` list of the `# Schema:` line, or from
   the header row names — **never by position**. `GCApproximate` was inserted at
   position 17, so a positional reader that used `SampleCount` /
   `IterationsPerSample` must shift by one; name-based readers are unaffected.
3. Expect `<limit` and `N/A` in every per-operation column (so pandas infers
   `object` for those columns), `~` in `CpuCycles` and `NaN` / `Infinity` in
   `Speedup` — in whichever section a row sits; the schema line above the row is
   what tells you which states are possible.
4. Keep the schema version (`v2`) with the data you archive, so a future v3 can be
   told apart from today's files.

## 6. Compatibility

**Affected API / output**

- **Source generator:** `PicoBench.Generators` ships compiled against **Roslyn
  4.8**, the oldest host it promises to support, so it loads on the **.NET 8 SDK
  (8.0.4xx)** and anything newer — those compilers ship Roslyn 4.8+. The code it
  emits also compiles on every supported target framework now: it no longer
  depends on a static factory that is missing on older TFMs.
- **netstandard2.0 consumers:** the minimum supported .NET Framework is now
  **4.7.2+** — earlier packages advertised a lower floor. `RuntimeInformation` is
  used unconditionally, so older .NET Framework versions are not supported. .NET
  Core 2.0+, .NET 5+ and later are unchanged.

**Migration**

- Build with the .NET 8 SDK (8.0.4xx) or newer. A host whose compiler ships Roslyn
  older than 4.8 cannot load the generator: the compiler skips it (a `CS9057`
  warning) and the generated `RunBenchmarksAsync` / typed entry points are missing
  (`CS1061`-style errors). Pinning an older `Microsoft.CodeAnalysis.CSharp` in the
  same build has the same effect — the rule belongs to the host, not to PicoBench.
- If you target .NET Framework, retarget to 4.7.2 or newer before taking this
  package version; builds on older .NET Framework targets keep working only on the
  previous package version.

## 7. Known limitations (deferred to Batch 1)

These are known, disclosed, and **not** fixed in Batch 0. They are listed so that
nobody reads a number as more precise than it is.

**Affected API / output**

- `Statistics.Avg` and the other point estimates at nanosecond scale (optimistic),
  `Statistics.OverheadNs` (always `0` on the async path), `GcInfo` / CSV GC
  columns (counts, never bytes), the formatted cells that are not invariant
  (Console / Markdown / HTML text and the CSV `# Duration` comment), and
  `CpuCyclesPerOp` on macOS (a time proxy).

| Limitation | What you see today | Owner |
|---|---|---|
| Nanosecond point estimates are still optimistic | An empty delegate can be folded by the JIT, so a tiny per-operation point estimate can read systematically low; only the `<limit` **upper bound** is trustworthy at that scale | Batch 1 |
| Asynchronous runs do not deduct harness overhead | `Statistics.OverheadNs == 0` and async per-operation values still carry their per-sample wrapper cost | Batch 1 |
| GC **bytes** are not collected | Only Gen0/Gen1/Gen2 collection **counts**, per process, approximate on the async path (`GcInfo.IsApproximate`, CSV `GCApproximate`) | Batch 1 |
| Output is not culture-locked | Data cells in the CSV are invariant, but several Console / Markdown / HTML cells and comment lines (`RSD`, `CpuCycles`, duration, the Markdown timestamp) still use the current culture, so a non-en-US machine can mix decimal separators (`1500.0` next to `12,3`) and print local-calendar dates | Batch 1 |
| macOS cycle column is a time proxy | The column is kept and marked approximate (`~`, `CpuCyclesAreApproximate`), but it advances with wall time, not with executed instructions | Batch 1 |

**Migration**: none for these — just do not treat a sub-nanosecond point
estimate, an async overhead of zero, a GC count as a byte count, a formatted
number outside the CSV data cells, or a macOS cycle value as authoritative. Batch
1 will publish its own notes when it changes any of them.

---

## Versioning note

Nothing here is a semver-breaking API change: members were added, no member was
removed and no signature changed. The breaks are behavioural — a `bool` that used
to mean "ratio > 1", a CSV column that used to be numeric, and a column order
that used to be stable — and they are why this batch ships with a release note and
a CSV schema version rather than silently.
