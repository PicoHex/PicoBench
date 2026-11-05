# PicoBench release notes — Batch 2 (output fidelity)

These notes are for **package consumers**: they describe what a package built
from `main` after Batch 2 does differently, and what you have to change, if
anything, to keep reading the output correctly. They say nothing about how the
changes are implemented — the reference manual is the README: *Output
Formatters* (including *Precision profile: raw versus display*), *CSV Column
Contract*, *Comparing Two Implementations*, *Measurement Fidelity*, *Result
Model*.

Batch 0 made the *numbers* honest. Batch 2 makes the **file** honest: the same
measurement now reads the same everywhere, a number that does not exist is
flagged instead of printed, a CSV is a CSV, and a benchmark name can no longer
rewrite the document it appears in.

Every change below is either a **behaviour** change in the output or an
**addition** to the public surface. No member was removed and no existing
signature changed.

## At a glance

| # | Change | Section |
|---|---|---|
| 1 | All numbers and timestamps are invariant: two machines, two locales, one byte-identical report | [1](#1-output-is-culture-locked) |
| 2 | A non-finite speedup is `N/A`, and one `∞` scenario can no longer poison the summary | [2](#2-a-non-finite-ratio-is-na-not-a-number) |
| 3 | CSV metadata is `# key,value` on one physical line; comparisons carry their run | [3](#3-the-csv-is-a-legal-csv) |
| 4 | Precision profile: `Raw` versus `Display`, one unit per table, Console gains `StdErr`/`RSD` columns | [4](#4-one-precision-profile-raw-versus-display) |
| 5 | User text (names, descriptions, groups) is escaped before it reaches the document | [5](#5-user-text-cannot-rewrite-the-document) |
| 6 | Percentiles are defined (type 7 / linear interpolation) and the statistics layer is tested directly | [6](#6-percentiles-have-a-definition) |

---

## 1. Output is culture-locked

Every metric, ratio, duration and timestamp in Console, Markdown, HTML, CSV and
the summary used to be formatted with `CurrentCulture` — and dates with
`CurrentCulture.Calendar`. On a machine running `de-DE`, `th-TH` or `ar-SA` the
same run produced different text: `12,3` next to `1500.0` in one table, and a
timestamp in a Buddhist or Hijri year.

**Affected output**

- Every figure the five formatters print: RSD, CPU cycles, duration, speedup and
  timestamps, in addition to the per-operation values that were already
  invariant.
- The HTML footer clock, which now also prints a Gregorian UTC timestamp.

**Migration**

- Reports are comparable across machines: if you diff or archive output, the
  bytes no longer depend on the machine locale. If any downstream script was
  parsing a locale-specific comma, it now sees `.` — the invariant form.

## 2. A non-finite ratio is `N/A`, not a number

`FormatSpeedup` appended `x` to a value formatted with `F2`; under the invariant
culture that turns `+∞` into the literal word `Infinity`, so a comparison with a
zero candidate printed `Infinityx`. Worse, the summary averaged *all* ratios
including the non-finite ones, so a single un-computable scenario turned the
whole summary into `∞`.

**Affected output**

- A non-finite speedup (`+∞`, `-∞`, `NaN`) is rendered as `N/A` in **all four
  formats** and in the comparison summary, and no winner marker is attached to a
  ratio that does not exist. CSV writes `N/A` in the `Speedup` cell.
- The summary aggregates only the finite ratios and **discloses the count it had
  to skip** (`1 scenario not computable (non-finite speedup)`), so a `2.00x`
  aggregate next to one `∞` scenario cannot be read as "of all scenarios".

**Migration**

- `Speedup` itself is unchanged (`NaN`/`∞` still come out of the API for the
  cases Batch 0 documented); only the *rendering* changed. Guard the value with
  `IsFasterKnown` before formatting it yourself — see the Batch 0 notes.

## 3. The CSV is a legal CSV

The suite metadata block was `# Key: value`, and the value was escaped as an
ordinary CSV field. A `Description` containing a line break therefore produced a
quoted **multi-line** field: a continuation line that does not start with `#`,
which a `comment='#'` reader cannot survive — pandas took it for the header row.
The comparisons section also had no `SampleCount` / `IterationsPerSample`, so a
ratio could not be traced back to the run that produced it.

**Affected output**

- Metadata is a **`# key,value`** table: one row per key, two fields.
- A line break inside a value becomes the two characters `\n` (CRLF, CR and LF
  alike), so every comment line is exactly one physical line. Nothing else about
  the values changed.
- The comparisons section gains **`SampleCount`** and **`IterationsPerSample`**
  (same names, same order as the results section), and the `# Schema:` line above
  it lists them and documents the `Speedup` cell.
- Verified by an RFC 4180 reader behind a `comment='#'`-equivalent scan, and with
  `pandas.read_csv(path, comment='#')` on a description that spans lines.

**Migration**

- If you parsed the metadata block, split each `#` line on the **first comma**
  instead of on `: ` — and turn a literal `\n` back into a line break if you need
  the original text.
- If you read comparison columns positionally, append two columns (name-based
  readers are unaffected); read the order from `columns=` in the `# Schema:` line.
- `# Description: "line1` / `line2"` pairs no longer exist: a reader that relied
  on the continuation line must be updated rather than silently fed a header.

## 4. One precision profile: raw versus display

One figure had two jobs — be readable in a table, be exact in a data file — and
each formatter picked its own compromise: a five-second benchmark printed
`5000000000.0` under a header that said `(ns)`, while Console had no `StdErr` or
`RSD` column at all.

**Affected output**

- New `MetricPrecision` enum (`Raw`, `Display`) and
  `FormatterOptions.PrecisionProfile` (`MetricPrecision?`, `null` = each format's
  default).
- **Display** (Console, Markdown, HTML default): the table picks **one** unit for
  all of its per-operation columns — `ns` below 1 000 ns, `µs` below 1 000 µs,
  `ms` below 1 000 ms, seconds above — and names it in the header (`Avg (µs)`).
  One unit per table, not per cell, so a column stays comparable; the bound
  `<limit` and `N/A` keep working in the scaled unit.
- **Raw** (CSV always): nanoseconds, with `_ns` in the column name (`Avg_ns`,
  `StdErr_ns`, …). A data file whose unit changes with the magnitude of the data
  is not machine-readable, so CSV does not scale at all.
- Console suite table **gains `StdErr (<unit>)` and `RSD (%)` columns** (same
  position as Markdown/HTML, after the percentiles), and every per-operation
  column of the comparison tables now carries its unit instead of `Avg (ns)`
  alone being labelled.
- Console, Markdown and HTML render **the same text** for the same value (a test
  asserts it character for character); the raw number is always in the CSV.

**Migration**

- Read the unit from the **header**, not from a constant: a header may now say
  `(µs)` or `(s)`. For CSV the unit is still nanoseconds and the column name still
  ends in `_ns`.
- If a dashboard scraped `Avg (ns)`, match the header prefix (`Avg (`) instead.
- Pin `PrecisionProfile = MetricPrecision.Raw` if you want the old unscaled
  display in Console/Markdown/HTML.

## 5. User text cannot rewrite the document

A benchmark name, suite description, category or group name is **data**. It used
to be written into Markdown heading position verbatim, so a name such as
`S\r\n## Injected heading` produced a real heading, a description containing
a code fence closed the fence around it, and a category name broke the table that
followed. In the Console the same characters broke the frame (`\r` swallowed the
rest of the line) and an ESC byte in a name reached the terminal as an ANSI
sequence.

**Affected output**

- Markdown: backslash, backtick, pipe and `#` in user text are backslash-escaped,
  and a line break in heading or paragraph position collapses to a space (a table
  cell keeps its `<br>`, which cannot escape the table).
- Console: C0 controls (including ESC) become the matching Unicode **control
  picture** (`␛`, `␊`, `␍`, `␉`), DEL becomes `␡`, C1 becomes `U+FFFD`, and
  U+2028/U+2029 are treated as line breaks — the cell never spans two lines and
  no escape sequence survives.
- Column padding and the summary frame are computed in **display cells**, not
  chars: a CJK name is two cells wide but one char, so the box closes correctly
  (it used to be 4 cells too wide).
- HTML is unchanged: HTML escaping already prevented structural injection.

**Migration**

- A name that contained a control character now shows a picture character instead
  of it. If you match on names in generated Markdown, expect the escaped form
  (escaped pipe, escaped hash, escaped backtick, `<br>` inside cells).
- Console output for such a name is one line, not two.

## 6. Percentiles have a definition

The percentile method and the variance denominator were not documented, so a
`P99` from PicoBench could not be compared with `PERCENTILE.INC`, with NumPy's
default, or with a hand computation.

**Affected output**

- Percentiles use **linear interpolation between the two nearest samples** — the
  method statistics packages call *type 7* (Excel `PERCENTILE.INC`, NumPy's
  default) — with `position = p/100 × (N-1)`.
- The spread is the **sample** variance (`N-1`, Bessel), so `StdDev` is the value
  that makes `StandardError = StdDev / √N` hold.
- Below about 20 samples `P99` sits very close to `Max` (at `N = 10`,
  `P99 = 9.91` against `Max = 10`), so the tail columns do not describe a stable
  tail. The README's statistics bullet now says so in all ten languages.
- The values are covered by direct tests of the statistics layer
  (`[1,2,3,4]` gives mean 2.5, sample variance 5/3 and `P50` 2.5; N=1, N=2,
  all-equal and all-zero each have a case), so the definition cannot drift
  silently.

**Migration**

- Reproduce expected values with `PERCENTILE.INC` or `numpy.percentile`
  (default `linear`). A reference built on `PERCENTILE.EXC` or nearest-rank will
  differ in the tail, especially at small `N`.

---

## Versioning note

Nothing here is a semver-breaking API change: `MetricPrecision` and
`FormatterOptions.PrecisionProfile` were added, nothing was removed, and no
signature changed. The breaks are behavioural and all of them are in the
**output**: a scaled table unit, two new Console columns, an escaped name, an
`N/A` where `Infinityx` used to be, a metadata block that is now `# key,value`,
and two extra comparison columns. Archive one sample report per format (with its
`# Schema:` line) and compare it after upgrading.
