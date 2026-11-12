#!/usr/bin/env python3
"""Probe the nine translated READMEs for structural drift from the English one (R7).

The READMEs are edited by hand in ten languages and nothing compiles them, so
they rot in ways only a reader notices: a section that silently disappears, a
table that loses a column, a badge block that only the English file has. The
release review could only confirm *falsifiable facts* (U7) and left prose quality
to native speakers - this script is the half that can be mechanised.

Two severities:

  FAIL    structural facts that must match the English file exactly and can be
          fixed without writing prose: section count, heading depth, code-fence
          languages and block lengths, table row/column shapes, the byte-equal
          `# Schema:` contract lines, the navigation bar and the badge block.

  REVIEW  identifier-level divergences that need a human: a backticked token the
          English section carries and the translation does not. Some are
          legitimate (a translated inline literal), some are dropped content -
          the "and `SummaryFormatter` provides a separate summary helper" clause
          is gone in seven languages. Deciding which is which needs a native
          reader, so these are printed as evidence and do NOT fail the build.
          The point is that the list is produced mechanically instead of by eye.

Exit code: 0 when no FAIL was found, 1 otherwise.
"""

from __future__ import annotations

import pathlib
import re
import sys

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
ENGLISH = "README.md"
TRANSLATIONS = [
    "README.zh.md",
    "README.zh-tw.md",
    "README.ja.md",
    "README.ko.md",
    "README.ru.md",
    "README.de.md",
    "README.es.md",
    "README.fr.md",
    "README.pt.md",
]

# Language-neutral strings every translation must carry verbatim. Every one of
# these is present in README.md; the assertion is that a translation may change
# the prose around them but never the identifier itself. (An earlier draft also
# listed `net10.0`, which is not in the English file at all - a reminder that a
# requirement invented here, rather than read from README.md, is noise.)
REQUIRED_ANYWHERE = [
    "PicoBench",
    "netstandard2.0",
    "net6.0",
    "4.7.2",
    "SetupAsync",
    "BenchmarkConfig.Quick",
    "BenchmarkConfig.Precise",
    "BenchmarkRunner.Run",
    "P50",
    "P90",
    "P99",
    "RSD_pct",
    "IterationsPerSample",
    "SampleCount",
    "CpuCycles",
    "GCApproximate",
    "# Schema:",
    "# key,value",
    "pandas.read_csv",
]

# Identifiers (and dotted names) are language-neutral everywhere: they appear in
# backticks in every README. These are collected from README.md and required,
# unquoted or quoted, in each translation - the automatically-derived half of
# the check, which is why the list is not written out above.
IDENTIFIER = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")
BADGE_URLS = [
    "actions/workflows/ci.yml/badge.svg",
    "img.shields.io/nuget/v/PicoBench.svg",
    "badge/License-MIT-yellow.svg",
]
NAV_MARKER = "[English](README.md)"

BACKTICK = re.compile(r"`([^`\n]+)`")
HEADING = re.compile(r"^(#{1,6})\s+(.*)$")

FAILURES: list[str] = []
REVIEWS: list[str] = []


def fail(message: str) -> None:
    FAILURES.append(message)


def review(message: str) -> None:
    REVIEWS.append(message)


def read(name: str) -> list[str]:
    return (REPO_ROOT / name).read_text(encoding="utf-8").replace("\r\n", "\n").split("\n")


def sections(lines: list[str]) -> list[tuple[int, str, list[str]]]:
    """(heading depth, heading text, body lines) - the preamble has depth 0."""
    out: list[tuple[int, str, list[str]]] = []
    depth, title, body = 0, "<preamble>", []
    in_code = False
    for line in lines:
        if line.startswith("```"):
            in_code = not in_code
            body.append(line)
            continue
        match = None if in_code else HEADING.match(line)
        if match:
            out.append((depth, title, body))
            depth, title, body = len(match.group(1)), match.group(2).strip(), []
        else:
            body.append(line)
    out.append((depth, title, body))
    return out


def code_blocks(lines: list[str]) -> list[list[str]]:
    out: list[list[str]] = []
    current: list[str] | None = None
    for line in lines:
        if line.startswith("```"):
            if current is None:
                current = [line]
            else:
                current.append(line)
                out.append(current)
                current = None
            continue
        if current is not None:
            current.append(line)
    return out


def heading_lines(lines: list[str]) -> list[int]:
    """Line number of each section heading, outside code fences (preamble -> 1)."""
    out = [1]
    in_code = False
    for number, line in enumerate(lines, 1):
        if line.startswith("```"):
            in_code = not in_code
            continue
        if not in_code and HEADING.match(line):
            out.append(number)
    return out


def table_shapes(lines: list[str]) -> list[list[int]]:
    """Pipe counts per row, one list per contiguous table."""
    out: list[list[int]] = []
    current: list[int] = []
    for line in lines:
        if line.startswith("|"):
            current.append(line.count("|"))
        elif current:
            out.append(current)
            current = []
    if current:
        out.append(current)
    return out


def prose_tokens(body: list[str]) -> list[str]:
    """Backticked tokens outside code fences, in order."""
    out: list[str] = []
    in_code = False
    for line in body:
        if line.startswith("```"):
            in_code = not in_code
            continue
        if not in_code:
            out.extend(BACKTICK.findall(line))
    return out


def check(translation: str) -> None:
    english = read(ENGLISH)
    lines = read(translation)
    text = "\n".join(lines)

    for token in REQUIRED_ANYWHERE:
        if token not in text:
            fail(f"{translation}: required identifier {token!r} is absent from the whole file")

    for url in BADGE_URLS:
        if url not in text:
            fail(
                f"{translation}: the badge block is incomplete - {url!r} is missing "
                f"(the English, zh and ko READMEs carry all three)"
            )
    nav_lines = [line for line in lines if NAV_MARKER in line]
    if len(nav_lines) != 1:
        fail(f"{translation}: the language navigation bar appears {len(nav_lines)} time(s), expected 1")

    en_sections = sections(english)
    tx_sections = sections(lines)
    if len(en_sections) != len(tx_sections):
        fail(f"{translation}: {len(tx_sections)} sections vs {len(en_sections)} in {ENGLISH}")
    else:
        en_depths = [depth for depth, _, _ in en_sections]
        tx_depths = [depth for depth, _, _ in tx_sections]
        if en_depths != tx_depths:
            differ = [
                f"[{index}] {en_sections[index][1]!r} depth {tx_depths[index]} vs {en_depths[index]}"
                for index in range(len(en_depths))
                if en_depths[index] != tx_depths[index]
            ]
            fail(f"{translation}: heading depth sequence differs: " + "; ".join(differ))

        tx_heading_lines = heading_lines(lines)
        for index, ((_, en_title, en_body), (_, _, tx_body)) in enumerate(
            zip(en_sections, tx_sections)
        ):
            tx_heading_line = tx_heading_lines[index]
            if table_shapes(en_body) != table_shapes(tx_body):
                fail(
                    f"{translation} [{en_title}]: table shape differs - "
                    f"{table_shapes(tx_body)} vs {table_shapes(en_body)} (rows, then pipes per row)"
                )
            missing = sorted(set(prose_tokens(en_body)) - set(prose_tokens(tx_body)))
            if missing:
                review(
                    f"{translation}:{tx_heading_line} [{en_title}]: English carries {missing} "
                    f"in backticks, the translation does not"
                )

    en_code, tx_code = code_blocks(english), code_blocks(lines)
    if len(en_code) != len(tx_code):
        fail(f"{translation}: {len(tx_code)} code blocks vs {len(en_code)} in {ENGLISH}")
    else:
        for index, (a, b) in enumerate(zip(en_code, tx_code)):
            if a[0] != b[0]:
                fail(f"{translation}: code block {index} opens with {b[0]!r} vs {a[0]!r}")
            if len(a) != len(b):
                fail(
                    f"{translation}: code block {index} ({b[0]!r}) has {len(b) - 2} content lines "
                    f"vs {len(a) - 2} - a translated comment may differ in text, a lost line may not"
                )

    english_text = chr(10).join(english)
    derived = sorted({t for t in BACKTICK.findall(english_text) if IDENTIFIER.match(t)})
    absent = [t for t in derived if t not in text]
    if absent:
        fail(f"{translation}: identifiers README.md uses are absent from the whole file: {absent}")

    en_schema = [line for line in english if line.startswith("# Schema:")]
    tx_schema = [line for line in lines if line.startswith("# Schema:")]
    if en_schema != tx_schema:
        fail(
            f"{translation}: the `# Schema:` contract lines are not byte-equal to {ENGLISH} "
            f"({len(tx_schema)} lines vs {len(en_schema)}) - the formatter writes these literally"
        )


def main() -> int:
    if not (REPO_ROOT / ENGLISH).exists():
        print(f"FAIL {ENGLISH} not found under {REPO_ROOT}")
        return 1
    for translation in TRANSLATIONS:
        if not (REPO_ROOT / translation).exists():
            fail(f"{translation} is missing")
            continue
        check(translation)

    for message in FAILURES:
        print(f"FAIL {message}")
    for message in REVIEWS:
        print(f"REVIEW {message}")
    print()
    if FAILURES:
        print(
            f"readme translations: {len(FAILURES)} structural failure(s), "
            f"{len(REVIEWS)} item(s) for native review across {len(TRANSLATIONS)} files"
        )
        return 1
    print(
        f"readme translations: {len(TRANSLATIONS)} files structurally aligned, "
        f"{len(REVIEWS)} item(s) for native review"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
