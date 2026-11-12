# PicoBench release notes — Batch 3 (GC allocation bytes)

These notes are for **package consumers**: they describe what a package built
from `main` after the GC-allocation change does differently, and what you have
to change, if anything, to keep reading the output correctly. They say nothing
about how the change is implemented — the reference manual is the README:
*Async semantics* (the *GC attribution* bullet), *CSV Column Contract* and
*Result Model*.

The change closes the gap review item **B1-H2 / T3** measured: the library
published *how many* collections a run caused (`GC0`/`GC1`/`GC2`) but never *how
much it allocated*, so a benchmark that allocated 100 MB without triggering a
collection and a benchmark that allocated nothing were both reported as `0/0/0`.

## At a glance

| # | Change | Section |
|---|---|---|
| 1 | `GcInfo` gains the allocation bytes: `AllocatedBytes`, `AllocatedBytesAreApproximate` | [1](#1-allocation-bytes-are-published) |
| 2 | A new `AllocBytes` column in CSV (results and comparisons), Console, Markdown and HTML | [2](#2-a-new-column-in-all-four-formats) |
| 3 | "Not collected" is `N/A`, a measured run that allocated nothing is `0` — three states, never a fabricated zero | [3](#3-the-tri-state-a-measured-zero-is-not-not-collected) |

---

## 1. Allocation bytes are published

`GcInfo` now carries the number of bytes the measured window allocated:

| Member | Meaning |
|---|---|
| `GcInfo.AllocatedBytes` (`long?`) | bytes allocated during the measured window, or `null` when the counter could not be read |
| `GcInfo.AllocatedBytesAreApproximate` (`bool`) | `true` when the reading came from a **process-wide** counter (the asynchronous path) and therefore also counts work done by other threads |

**How each path reads it**

- **Synchronous** benchmarks read the **thread-scoped**
  `GC.GetAllocatedBytesForCurrentThread` delta: the number is the measured work's
  own allocation, and `AllocatedBytesAreApproximate` is `false`.
- **Asynchronous** benchmarks read the **process-wide**
  `GC.GetTotalAllocatedBytes` delta instead, because an `await` may resume on
  another thread where a thread-scoped counter is not valid; the reading is
  flagged approximate (`~` in the output), exactly like the process-wide CPU
  cycle counter.
- The .NET Standard 2.0 reference surface declares **neither** method, so the
  library resolves them at run time and degrades explicitly. A **synchronous**
  run uses the thread-scoped counter wherever the runtime exposes one — verified
  on .NET 10 and on .NET Framework 4.7.2 and 4.8, where **one sample** of a
  `new byte[1024] x1000` workload reports **~1.0 MB**: 1000 arrays × 1048 B
  (1024 B payload + a 24 B object header) = 1,048,000 B — measured 1,048,040 B
  on .NET 10 and 1,048,224 B on .NET Framework 4.7.2 / 4.8. An
  **asynchronous** run needs the process-wide counter, which .NET Framework does
  not expose, so on net4x the
  async path publishes `null` ("not collected", rendered `N/A`) rather than a
  fabricated `0`.

**Migration**

- Nothing to change to keep reading existing columns. If you consume `GcInfo`
  directly, `AllocatedBytes` is a new nullable member; the migration is the same
  as for any added property.
- The metadata of a run is now more informative, not differently shaped: the
  counts keep their meaning and only gain a companion.
- A **negative** delta (a cumulative counter that appears to run backwards) is
  published as `N/A`, not as `0`: it is a failed measurement, not a measurement
  of zero.

---

## 2. A new column in all four formats

`AllocBytes` is written by every formatter and sits directly after
`GCApproximate` (CSV) / the GC column (Console, Markdown, HTML).

| Format | Where |
|---|---|
| CSV (results) | column `AllocBytes`, between `GCApproximate` and `SampleCount` — **positional readers must shift `SampleCount`/`IterationsPerSample`/`Timestamp` by one** |
| CSV (comparisons) | column `AllocBytes`, between `GCApproximate` and `SampleCount` |
| Console | a `GC B` column beside `GC (0/1/2)`; the single-result view adds a `GC Allocated:` line |
| Markdown | a `GC B` column beside `GC (0/1/2)` |
| HTML | a `gc-info` cell beside the `GC (0/1/2)` cell |

The CSV `# Schema:` line names the new column and its allowed cell shapes, and
the column order is derived from the header that is really written, so it cannot
drift:

```
... ; GC counters = count | "N/A" (not collected); GCApproximate = 1 (approximate counters) | 0 (exact) | "N/A" (not collected); AllocBytes = bytes allocated | "~bytes" (approximate: process-wide counter) | "N/A" (not collected)
```

**Migration**

- A name-based reader is unaffected beyond seeing one extra column.
- A **positional** reader must shift the three columns that follow
  `AllocBytes`. This is the same break `GCApproximate` introduced at position 17
  (Batch 0), and it is called out in the README's CSV contract.
- In the machine-readable CSV the raw byte count is written (for example `0`,
  `~2048`); in the three human-readable formats it is scaled (for example
  `0 B`, `~2.0 KB`, `3.0 MB`). Those strings are format examples, not measured
  values.

---

## 3. The tri-state: a measured zero is not "not collected"

`AllocBytes` follows the same three-state contract the GC counters and the CPU
cycle column already have, so a consumer can always tell "the counter ran and
did not move" from "the counter could not be read":

| Cell | Meaning |
|---|---|
| `0` / `0 B` | the counter was read and the measured work allocated nothing — a real measurement (a workload that allocates nothing still shows the harness's own ~40 B floor on the synchronous path; see §4) |
| `~n` / `~2.0 KB` | a process-wide reading (asynchronous path), so it includes other threads' allocations |
| `N/A` | the counter could not be read (no thread/process counter on this runtime) — **never** written as `0` |

The aggregation preserves the same rule: the bytes are summed across the
samples of a run, but if **any** sample could not report a reading the run's
total is `N/A` rather than a partial sum that would understate the allocation.
The same applies to the merge of the interleaved comparison rounds.

**Migration**

- If you treated a missing `GcInfo` as "zero allocation", stop: `null` means the
  counter was not collected, and the output says `N/A`. The distinction was the
  point of the change — a fabricated `0` is the one answer that is always wrong.

---

## 4. What the byte figures in these notes rest on

Every byte figure quoted above is either **one sample** (a single measured
window), an **explicitly named sample sum** (a run total over `SampleCount`
samples), or marked as a format example. None of them is a mixture of the two.

| Figure | Workload | How it was measured |
|---|---|---|
| 1,048,040 B | **one sample**, `new byte[1024] x1000` | synchronous thread-scoped counter, .NET 10.0.12 / win-x64. The same loop read without the harness (the counter around the bare loop) is 1,048,000 B; the 40 B difference is the harness's own per-sample `Stopwatch` |
| 3,144,120 B | **sample sum**, `SampleCount = 3`, `new byte[1024] x1000` | the run's total: 3 x 1,048,040 B. A sample sum is what a multi-sample run publishes; it is **not** what one window allocated |
| 40 B | **one sample**, empty delegate x 10,000 | the floor of the synchronous path: the bare loop reads 0 B, and `Stopwatch.StartNew() + Stop()` on its own allocates 40 B per instance, so this 40 B is harness overhead inside the window, not allocation by the measured work |
| 1048 B per array | `new byte[1024]` | 1024 B payload + a 24 B object header, rounded up to 8-byte alignment (per-array readings: 32 B for `byte[1]`, 1048 B for `byte[1024]`, 2024 B for `byte[2000]`) |
| `~n` values, e.g. 56,952 / 57,144 / 112,760 B | **one sample each**, `await Task.Yield()` + `new byte[1024] x50` (52,400 B of payload) | three successive samples of the process-wide counter. `~` means order of magnitude, not exact: that counter also counts other threads and the runtime reports it in batches, so it **lags** — a single 1,048 B allocation can leave the reading unchanged |
| ~1.0 MB on net4x | **one sample**, `new byte[1024] x1000` | thread-scoped counter on .NET Framework 4.7.2 / 4.8, 1,048,224 B single sample |

Two reading rules follow: compare a **single sample** only with another single
sample (or with the per-sample payload), and treat a `~` value as a magnitude
diagnosis rather than an auditable count — the same meaning `~` already carries
on the CPU-cycle and GC columns.
