#!/usr/bin/env python3
"""Prints the `run:` body of the first workflow step whose name matches.

Some steps have to run before `actions/checkout`, so their shell body lives in
the workflow instead of a checked-in script - and that body still needs a test.
This helper extracts it so the test can execute exactly what ships.

Usage: workflow_step.py <workflow.yml> <step-name-substring>

Exit code 2 when the step or its `run:` block cannot be found.
"""

from __future__ import annotations

import pathlib
import re
import sys


def step_run_block(text: str, needle: str) -> str:
    lines = text.splitlines()
    start = None
    for index, line in enumerate(lines):
        stripped = line.strip()
        if stripped.startswith("- name:") and needle in stripped:
            start = index
            break
    if start is None:
        raise SystemExit(f"step not found: {needle}")

    step_indent = len(lines[start]) - len(lines[start].lstrip())
    for index in range(start + 1, len(lines)):
        raw = lines[index]
        stripped = raw.strip()
        indent = len(raw) - len(raw.lstrip())
        if indent <= step_indent and stripped.startswith("- "):
            break
        match = re.match(r"^\s*run:\s*(\||>|>-|\+)?\s*$", raw)
        if not match:
            continue
        body: list[str] = []
        child_indent = None
        for child in lines[index + 1 :]:
            if not child.strip():
                body.append("")
                continue
            child_indent = len(child) - len(child.lstrip()) if child_indent is None else child_indent
            if len(child) - len(child.lstrip()) < child_indent:
                break
            body.append(child[child_indent:])
        return "\n".join(body).rstrip() + "\n"

    raise SystemExit(f"no run: block scalar in step: {needle}")


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        print(__doc__)
        return 2
    text = pathlib.Path(argv[1]).read_text(encoding="utf-8")
    sys.stdout.write(step_run_block(text, argv[2]))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
