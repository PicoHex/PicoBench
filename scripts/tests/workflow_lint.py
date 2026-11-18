#!/usr/bin/env python3
"""Structural checks for the GitHub Actions workflows.

The workflows are the only part of the release chain that nothing compiles, so
the mistakes they can carry - an expression interpolated into a shell script, a
third-party action on a mutable tag, a validation step that runs after something
already happened - are checked here instead (review items B5-H2, B5-M2, B5-L1).

The allowlist file carries a review record per entry (period, last-reviewed
date, resolve command, note) and this module rejects a malformed line, so a
mutable tag cannot be parked without one; the quarterly procedure that fills the
record is `scripts/tests/action_pin_review.py` and docs/releasing.md (R6).

A third class of mistake is a guard that is present in the file but no longer
executed: a `re.search(...)` over the whole yml is satisfied by a comment, by an
`echo "skipped: ./scripts/x.sh"` or by a `grep` pattern, so the check stays green
while the step does nothing (H2-A8' of .review-2026-11/ablation-consolidated.md).
The checks that pin a *script invocation* therefore parse the step structure and
look for the path in command position inside some step's `run:` body
(`step_runs` / `invoked_scripts` below), never in the file's text.

Usage: workflow_lint.py [--workflows-dir <dir>]. The directory override exists
so scripts/tests/workflow-lint.test.sh can drive this module red and green on
throwaway fixtures.

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
ALLOWLIST_NAME = "unpinned-actions.allowlist"

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


STEPS_KEY = re.compile(r"^(\s*)(?:-\s+)?steps:\s*$")


def step_blocks(lines: list[str]) -> list[list[str]]:
    """Splits the `steps:` sequences of a workflow into per-step line lists.

    Only sequences under a `steps:` key are steps, and the step indent is taken
    from the first `- ` below that key. Without both rules the `matrix.include:`
    list - which is also a `- ` sequence, sitting above `steps:` and two levels
    deeper - would be read as the first "step" and every real step would be
    swallowed into it, so a `run:` would never be seen (measured: the AOT job
    reported that it never runs its probe script).
    """
    steps: list[list[str]] = []
    current: list[str] = []
    step_indent: int | None = None
    in_steps = False
    for raw in lines:
        indent = len(raw) - len(raw.lstrip())
        if STEPS_KEY.match(raw):
            in_steps = True
            step_indent = None
            if current:
                steps.append(current)
                current = []
            continue
        if not in_steps:
            continue
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


# `run: ...` inside a step. Group 2 is the block-scalar indicator, group 3 the
# inline command of a single-line `run:`.
RUN_KEY = re.compile(r"^(\s*)(?:-\s*)?run:\s*(\||>|>-|\+|-)?\s*(.*)$")
STEP_NAME = re.compile(r"^\s*(?:-\s*)?name:\s*(.*)$")
# A shell line is cut at the operators that start a new command.
SEGMENT = re.compile(r"&&|\|\||;|\|")
ASSIGNMENT = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*=")
# Words in front of the real command that are not the command.
COMMAND_PREFIXES = {"sudo", "env", "exec", "command", "nohup", "time"}
# Interpreters: `bash scripts/x.sh`, `python3 scripts/tests/y.py` and
# `./scripts/x.sh` all execute the same script and all have to count.
INTERPRETERS = {
    "bash",
    "sh",
    "dash",
    "zsh",
    "ksh",
    "pwsh",
    "powershell",
    "python",
    "python3",
    "py",
}


def step_runs(text: str) -> list[tuple[str, str]]:
    """Every `run:` body of a workflow, as (step name, body).

    The structural counterpart of `shell_regions`: the step boundaries are kept,
    so a check can ask what a *step* runs instead of what the file mentions
    somewhere. Both spellings are handled - a `run: |` block scalar (de-indented
    the way a shell receives it) and a single-line `run: ./scripts/x.sh`.
    """
    runs: list[tuple[str, str]] = []
    for block in step_blocks(text.splitlines()):
        name = ""
        for raw in block:
            match = STEP_NAME.match(raw)
            if match:
                name = match.group(1).strip().strip("\"'")
                break
        body = run_body(block)
        if body is not None:
            runs.append((name, body))
    return runs


def run_body(block: list[str]) -> str | None:
    """The shell body of one step block, or None when the step has no `run:`."""
    for index, raw in enumerate(block):
        match = RUN_KEY.match(raw)
        if not match:
            continue
        inline = match.group(3).strip()
        if inline:
            return inline + chr(10)
        if not match.group(2):
            return ""  # `run:` with nothing under it runs nothing
        body: list[str] = []
        child_indent: int | None = None
        for child in block[index + 1 :]:
            if not child.strip():
                body.append("")
                continue
            indent = len(child) - len(child.lstrip())
            child_indent = indent if child_indent is None else child_indent
            if indent < child_indent:
                break
            body.append(child[child_indent:])
        return chr(10).join(body).rstrip() + chr(10)
    return None


def invoked_scripts(body: str) -> set[str]:
    """The repository scripts a shell body actually *executes*.

    Only command position counts: each segment's command word (after `sudo`/`env`,
    variable assignments and an interpreter, and after the `./`) is compared with
    the path. A path that merely appears - in a comment, in the message of an
    `echo`, or as a `grep` pattern - is not an invocation, and treating it as one
    is exactly how a disabled guard used to stay green (H2-A8').
    """
    invoked: set[str] = set()
    for raw in body.splitlines():
        for segment in SEGMENT.split(strip_comment(raw)):
            words = segment.split()
            index = 0
            while index < len(words):
                word = words[index].strip("\"'")
                if ASSIGNMENT.match(word) or word in COMMAND_PREFIXES or word in INTERPRETERS:
                    index += 1
                    continue
                if index > 0 and word.startswith("-"):
                    index += 1  # an interpreter option, e.g. `bash -euo pipefail`
                    continue
                break
            if index >= len(words):
                continue
            command = words[index].strip("\"'")
            if command.startswith("./"):
                command = command[2:]
            if command.startswith(("scripts/", "tools/")):
                invoked.add(command)
    return invoked


def job_invoked_scripts(text: str, job: str) -> set[str] | None:
    """Scripts the `run:` bodies of one job execute; None when the job is absent."""
    block = job_block(text, job)
    if block is None:
        return None
    invoked: set[str] = set()
    for _name, body in step_runs(block):
        invoked |= invoked_scripts(body)
    return invoked


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

    invoked: set[str] = set()
    for _step_name, body in step_runs(text):
        invoked |= invoked_scripts(body)

    script_path = REPO_ROOT / "scripts" / "validate-release-tag.sh"
    script = script_path.read_text(encoding="utf-8") if script_path.exists() else ""
    if "scripts/validate-release-tag.sh" in invoked:
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

    # H2-A8': this used to be `re.search(r"verify-release-commit-on-main\.sh", text)`
    # - a scan of the whole file, comments included. Renaming a step's `run:` to
    # `echo "skipped: ./scripts/verify-release-commit-on-main.sh"` therefore kept
    # the lint green and the suite at 742/742 while the ancestry guard was dead:
    # the release would pack a side-branch commit and publish it to nuget.org.
    # The path now has to be the command word of some step's `run:` body.
    verify_script = "scripts/verify-release-commit-on-main.sh"
    if verify_script not in invoked:
        if verify_script.rsplit("/", 1)[-1] in text:
            error(
                f"{path.name}: {verify_script} is mentioned but never executed - no step's "
                f"`run:` invokes it (a comment or an `echo` that spells the path out is not "
                f"an invocation), so nothing forces the release commit onto main (B5-M2)"
            )
        else:
            error(
                f"{path.name}: never calls {verify_script} - "
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
    "consumer-netstandard20": ["ConsumerNetStandard20", "--source"],
    "samples": ["AttributeBased", "StringVsStringBuilder", "AsyncBenchmarks"],
    "release-scripting": [
        "release-ps1-exit-codes.ps1",
        "shell-lint.test.sh",
        "action_pin_review.py",
        "readme_translation_probe.py",
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


# The scripts below are what each job's gate *is*. `REQUIRED_JOBS` asks whether a
# job still names its guarantees, and that is a text question - a job whose step
# became `run: echo "bash scripts/tests/x.test.sh"` still names it. For a script
# the question is structural instead: it has to be in command position in some
# step's `run:` body (H2-A8').
REQUIRED_SCRIPT_INVOCATIONS = {
    "api-guard": ["scripts/release.ps1"],
    "aot-validation": ["scripts/tests/aot-negative-probe.test.sh"],
    "release-scripting": [
        "scripts/tests/workflow_lint.py",
        "scripts/tests/workflow-lint.test.sh",
        "scripts/tests/shell-lint.test.sh",
        "scripts/tests/workflow-script-mode.test.sh",
        "scripts/tests/action_pin_review.py",
        "scripts/tests/readme_translation_probe.py",
        "scripts/tests/release-tag-validation.test.sh",
        "scripts/tests/release-build-exit-codes.test.sh",
        "scripts/tests/release-ps1-exit-codes.ps1",
        "scripts/tests/packaging-guard.test.sh",
    ],
}


def check_ci_job_scripts(path: pathlib.Path) -> None:
    """Every gate script a job promises to run has to be executed, not quoted."""
    text = path.read_text(encoding="utf-8")
    for job, scripts in REQUIRED_SCRIPT_INVOCATIONS.items():
        invoked = job_invoked_scripts(text, job)
        if invoked is None:
            continue  # check_ci_jobs already reported the missing job
        for script in scripts:
            if script not in invoked:
                error(
                    f"{path.name}: job `{job}` never runs {script} - no step's `run:` invokes "
                    f"it, and a comment or an `echo` that spells the path out is not an "
                    f"invocation"
                )


ALLOWLIST_PERIODS = {"monthly", "quarterly", "yearly"}
ALLOWLIST_FIELDS = 5


def load_allowlist(workflows_dir: pathlib.Path) -> set[str]:
    """Reads the unpinned-action allowlist, which is TAB separated.

    Field 1 is the `uses:` value; fields 2-5 are the review record (period,
    last-reviewed date, resolve command, note) that R6 asks for. A malformed
    line is an error rather than a silent skip, because a skipped line silently
    widens the allowlist. Note the file uses `#` for its own comments, so the
    first field is taken verbatim, tabs and all.
    """
    allowlist = workflows_dir / ALLOWLIST_NAME
    allowed: set[str] = set()
    if not allowlist.exists():
        return allowed
    for number, raw in enumerate(read(allowlist), 1):
        line = raw.rstrip(chr(13))
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        fields = line.split(chr(9))
        if len(fields) != ALLOWLIST_FIELDS:
            error(
                f"{allowlist.name}:{number}: expected {ALLOWLIST_FIELDS} tab-separated "
                f"fields (action, period, last-reviewed, resolve command, note), got {len(fields)}"
            )
            continue
        action, period, reviewed, command, note = (field.strip() for field in fields)
        if not action:
            error(f"{allowlist.name}:{number}: the action field is empty")
            continue
        if period not in ALLOWLIST_PERIODS:
            error(
                f"{allowlist.name}:{number}: review period {period!r} is not one of "
                f"{sorted(ALLOWLIST_PERIODS)} - the R6 review has no schedule otherwise"
            )
            continue
        if not reviewed or not command or not note:
            error(
                f"{allowlist.name}:{number}: '{action}' is missing part of its review record "
                f"(last-reviewed date, resolve command and note are all required)"
            )
            continue
        allowed.add(action)
    return allowed


def main(argv: list[str] | None = None) -> int:
    """Runs every check. `--workflows-dir <dir>` lints another directory.

    The override exists so scripts/tests/workflow-lint.test.sh can drive this
    module red and green on throwaway fixtures without editing the repository's
    own workflows. REPO_ROOT stays the real repository, so a fixture's inline tag
    validation is still compared against the checked-in script.
    """
    args = list(sys.argv[1:] if argv is None else argv)
    workflows_dir = WORKFLOW_DIR
    if args:
        if len(args) == 2 and args[0] in ("--workflows-dir", "--workflows"):
            workflows_dir = pathlib.Path(args[1]).resolve()
        else:
            print(__doc__)
            return 2

    allowlist = load_allowlist(workflows_dir)

    workflows = sorted(workflows_dir.glob("*.yml"))
    if not workflows:
        error(f"no workflows found in {workflows_dir}")
    for path in workflows:
        check_no_expressions_in_shell(path)
        check_actions_pinned(path, allowlist)

    ci = workflows_dir / "ci.yml"
    if ci.exists():
        check_ci_jobs(ci)
        check_ci_job_scripts(ci)
    else:
        error("ci.yml is missing")

    release = workflows_dir / "release.yml"
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
