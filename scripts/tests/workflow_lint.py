#!/usr/bin/env python3
"""Structural checks for the GitHub Actions workflows.

The workflows are the only part of the release chain that nothing compiles, so
the mistakes they can carry - an expression interpolated into a shell script, a
third-party action on a mutable tag, a validation step that runs after something
already happened - are checked here instead (review items B5-H2, B5-M2, B5-L1).

Exit code: 0 when every check passes, 1 otherwise.
"""

from __future__ import annotations

import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from workflow_step import step_run_block  # noqa: E402

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
WORKFLOW_DIR = REPO_ROOT / ".github" / "workflows"
ALLOWLIST = WORKFLOW_DIR / "unpinned-actions.allowlist"

EXPRESSION = re.compile(r"\$\{\{")
SHA_PINNED = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}$")
FIRST_PARTY = re.compile(r"^(actions/|github/|\./)")
USES = re.compile(r"^\s*(?:-\s*)?uses:\s*(\S+)")
ERRORS: list[str] = []


def error(message: str) -> None:
    ERRORS.append(message)


def read(path: pathlib.Path) -> list[str]:
    return path.read_text(encoding="utf-8").splitlines()


def strip_comment(line: str) -> str:
    """Removes a trailing `# ...` comment that is not inside a quoted scalar."""
    out = []
    quote = None
    for index, char in enumerate(line):
        if quote:
            if char == quote:
                quote = None
        elif char in "\"'":
            quote = char
        elif char == "#" and (index == 0 or line[index - 1].isspace()):
            break
        out.append(char)
    return "".join(out)


def shell_regions(lines: list[str]) -> list[tuple[int, str]]:
    """Returns (line_number, text) for every line inside a `run:` shell block.

    Handles both block scalars (`run: |`) and single-line commands
    (`run: dotnet build`), which is all these workflows use. A block ends at the
    first line that is not indented deeper than the `run:` key.
    """
    regions: list[tuple[int, str]] = []
    block_indent: int | None = None
    for number, raw in enumerate(lines, 1):
        line = strip_comment(raw)
        indent = len(raw) - len(raw.lstrip())
        if block_indent is not None:
            if line.strip() and indent > block_indent:
                regions.append((number, line))
                continue
            block_indent = None
        match = re.match(r"^(\s*)(?:-\s*)?(?:run|script|args):\s*(\||>|>-|\+)?\s*(.*)$", line)
        if not match:
            continue
        key_indent = len(match.group(1))
        if match.group(2):
            block_indent = key_indent
            continue
        if match.group(3).strip():
            regions.append((number, line))
    return regions


def check_no_expressions_in_shell(path: pathlib.Path) -> None:
    lines = read(path)
    for number, text in shell_regions(lines):
        if EXPRESSION.search(text):
            error(
                f"{path.name}:{number}: `${{{{ ... }}}}` inside a shell block - pass the value "
                f"through `env:` and reference it as a shell variable instead: {text.strip()}"
            )


def check_actions_pinned(path: pathlib.Path, allowlist: set[str]) -> None:
    for number, raw in enumerate(read(path), 1):
        match = USES.match(strip_comment(raw))
        if not match:
            continue
        action = match.group(1).strip("\"'")
        if FIRST_PARTY.match(action) or SHA_PINNED.match(action):
            continue
        if action in allowlist:
            continue
        error(
            f"{path.name}:{number}: third-party action '{action}' is not pinned to a commit SHA "
            f"(resolve it with `git ls-remote https://github.com/<owner>/<repo> refs/tags/<tag>`)"
        )


def step_blocks(lines: list[str]) -> list[list[str]]:
    """Splits the `steps:` sequences of a workflow into per-step line lists."""
    steps: list[list[str]] = []
    current: list[str] = []
    step_indent: int | None = None
    for raw in lines:
        indent = len(raw) - len(raw.lstrip())
        is_step = re.match(r"^\s*-\s", raw) is not None
        if is_step and (step_indent is None or indent == step_indent):
            step_indent = indent
            if current:
                steps.append(current)
            current = [raw]
            continue
        if current:
            current.append(raw)
    if current:
        steps.append(current)
    return steps


def normalise(text: str) -> list[str]:
    return [
        line.strip()
        for line in text.splitlines()
        if line.strip() and not line.strip().startswith("#")
    ]


def check_release_validation_is_first(path: pathlib.Path) -> None:
    text = path.read_text(encoding="utf-8")
    steps = step_blocks(text.splitlines())
    if not steps:
        error(f"{path.name}: no steps found")
        return

    first = chr(10).join(steps[0])
    if not re.search(r"name:\s*Validate the release", first):
        error(f"{path.name}: the first step must validate the tag, but it is: " + first)

    script_path = REPO_ROOT / "scripts" / "validate-release-tag.sh"
    script = script_path.read_text(encoding="utf-8") if script_path.exists() else ""
    if "validate-release-tag.sh" in first:
        pass  # the step calls the checked-in script
    else:
        try:
            body = step_run_block(text, "Validate the release tag")
        except SystemExit as exc:
            error(f"{path.name}: {exc}")
            body = ""
        if not script:
            error(f"{path.name}: validation is inline, but scripts/validate-release-tag.sh is missing")
        elif body and normalise(body) != normalise(script):
            error(
                f"{path.name}: the inline validation body has drifted from "
                f"scripts/validate-release-tag.sh - keep the two identical"
            )

    if not re.search(r"verify-release-commit-on-main\.sh", text):
        error(
            f"{path.name}: never calls scripts/verify-release-commit-on-main.sh - "
            f"nothing forces the release commit onto main (B5-M2)"
        )

    if "workflow_dispatch:" in text:
        trigger = text.split("workflow_dispatch:", 1)[1].split("jobs:", 1)[0]
        if "version:" not in trigger:
            error(
                f"{path.name}: workflow_dispatch must declare a required `version` input "
                f"(a manual run has no tag, so GITHUB_REF resolves to refs/heads/<branch>)"
            )


REQUIRED_JOBS = {
    "build-and-test": ["StringVsStringBuilder", "AsyncBenchmarks", "dotnet test"],
    "api-guard": ["release.ps1 -DryRun"],
    "aot-validation": [
        "PublishAot=true",
        "IlcTreatWarningsAsErrors=true",
        "Benchmarks: 2",
        "aot-negative-probe.test.sh",
    ],
    "net4x-smoke": ["PicoBench.Net4xSmoke", "net472", "net48"],
    "consumer-sdk8": ["ConsumerSdk8", "8.0.x", "Benchmarks: 2"],
    "consumer-netstandard20": ["ConsumerNetStandard20", "--source"],
    "samples": ["AttributeBased", "StringVsStringBuilder", "AsyncBenchmarks"],
    "release-scripting": [
        "release-ps1-exit-codes.ps1",
        "release-build-exit-codes.test.sh",
        "release-tag-validation.test.sh",
        "packaging-guard.test.sh",
        "workflow_lint.py",
    ],
}

JOB_HEADER = re.compile(r"^  [A-Za-z0-9_-]+:" + r"[ ]*$")


def job_block(text: str, name: str) -> str | None:
    lines = text.splitlines()
    start = None
    for index, line in enumerate(lines):
        if line.rstrip() == "  " + name + ":":
            start = index
            break
    if start is None:
        return None
    for index in range(start + 1, len(lines)):
        if JOB_HEADER.match(lines[index]):
            return chr(10).join(lines[start:index])
    return chr(10).join(lines[start:])


def check_ci_jobs(path: pathlib.Path) -> None:
    text = path.read_text(encoding="utf-8")
    for job, needles in REQUIRED_JOBS.items():
        block = job_block(text, job)
        if block is None:
            error(f"{path.name}: the `{job}` job is missing")
            continue
        for needle in needles:
            if needle not in block:
                error(
                    f"{path.name}: job `{job}` no longer contains "
                    + repr(needle)
                    + " - the guarantee it encodes is untested"
                )


def main() -> int:
    allowlist: set[str] = set()
    if ALLOWLIST.exists():
        for line in read(ALLOWLIST):
            entry = strip_comment(line).strip()
            if entry:
                allowlist.add(entry)

    workflows = sorted(WORKFLOW_DIR.glob("*.yml"))
    if not workflows:
        error(f"no workflows found in {WORKFLOW_DIR}")
    for path in workflows:
        check_no_expressions_in_shell(path)
        check_actions_pinned(path, allowlist)

    ci = WORKFLOW_DIR / "ci.yml"
    if ci.exists():
        check_ci_jobs(ci)
    else:
        error("ci.yml is missing")

    release = WORKFLOW_DIR / "release.yml"
    if release.exists():
        check_release_validation_is_first(release)
    else:
        error("release.yml is missing")

    if ERRORS:
        for message in ERRORS:
            print(f"FAIL {message}")
        print(f"workflow lint: {len(ERRORS)} problem(s)")
        return 1
    print(f"workflow lint: {len(workflows)} workflow(s), all checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
