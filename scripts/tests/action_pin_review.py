#!/usr/bin/env python3
"""Review the third-party actions that are not pinned to a commit SHA (R6).

`workflow_lint.py` proves the allowlist *format* and that the file is
load-bearing. It cannot prove the tag a workflow uses still points at the code
we think it does: `NuGet/login@v1` is a moving reference, and the publisher can
repoint it at any time. That check needs the GitHub API, so it is a scheduled
human task rather than a build step - this script is the schedule's script.

Two modes:

    python3 scripts/tests/action_pin_review.py
        Plans only. Parses the allowlist, prints the owner/repo, the ref, the
        review period, when the review was last done and when it is next due,
        and the exact resolve command for each entry. No network, so it is safe
        in CI - it exists there so the review record cannot rot unnoticed.

    python3 scripts/tests/action_pin_review.py --resolve
        Also runs the resolve command (`gh api`, falling back to
        `git ls-remote`) and prints the commit each ref points at TODAY. Needs
        network and `gh` (or `git`). Compare the printed SHA against the note;
        paste both the SHA and today's date back into the allowlist.

`--today YYYY-MM-DD` pins the clock so the due-date logic is testable.

Exit code: 0 when every entry is well-formed (and, with --require-reviewed,
when no review is overdue), 1 otherwise.
"""

from __future__ import annotations

import argparse
import datetime as dt
import pathlib
import re
import subprocess
import sys

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
ALLOWLIST = REPO_ROOT / ".github" / "workflows" / "unpinned-actions.allowlist"

# Field 1 is `<owner>/<repo>@<ref>`. Both halves are interpolated into a URL
# before anything runs, so they are validated first and a value that does not
# match is never passed to a subprocess.
ACTION = re.compile(r"^(?P<owner>[A-Za-z0-9_.-]+)/(?P<repo>[A-Za-z0-9_.-]+)@(?P<ref>[A-Za-z0-9_.\-/]+)$")

PERIOD_DAYS = {"monthly": 30, "quarterly": 91, "yearly": 365}
PERIODS = set(PERIOD_DAYS)
FIELDS = 5


class Entry:
    def __init__(self, number: int, fields: list[str]) -> None:
        self.number = number
        self.raw = "\t".join(fields)
        self.action, self.period, self.reviewed, self.command, self.note = (
            field.strip() for field in fields
        )

    @property
    def parts(self) -> re.Match[str] | None:
        return ACTION.match(self.action)

    def resolve_command(self) -> str:
        """The canonical resolver, derived - never taken from the file.

        The allowlist's own `command` field is shown to the reader; this is what
        `--resolve` actually runs, so a data file cannot smuggle in a command.

        `git/ref/tags/<ref>` is the obvious endpoint and the WRONG one: for an
        annotated tag (NuGet/login's `v1` is one) its `object` is the *tag*
        object, not the commit, so it reports a SHA that a workflow pin would
        never use. `commits/<ref>` peels to the commit; the `^{}` suffix does the
        same for `git ls-remote`.
        """
        match = self.parts
        if match is None:
            return ""
        owner, repo, ref = match["owner"], match["repo"], match["ref"]
        return (
            f"gh api repos/{owner}/{repo}/commits/{ref} --jq .sha "
            f"|| git ls-remote https://github.com/{owner}/{repo} 'refs/tags/{ref}^{{}}' 'refs/tags/{ref}'"
        )

    def due_date(self, today: dt.date) -> dt.date | None:
        if self.reviewed == "never":
            return today  # never reviewed is due now
        try:
            done = dt.date.fromisoformat(self.reviewed)
        except ValueError:
            return None
        return done + dt.timedelta(days=PERIOD_DAYS[self.period])

    def overdue(self, today: dt.date) -> bool:
        due = self.due_date(today)
        return due is not None and due <= today


def read_entries() -> tuple[list[Entry], list[str]]:
    problems: list[str] = []
    entries: list[Entry] = []
    if not ALLOWLIST.exists():
        return entries, [f"{ALLOWLIST.name} is missing"]
    for number, raw in enumerate(ALLOWLIST.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.rstrip(chr(13))
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        fields = line.split(chr(9))
        if len(fields) != FIELDS:
            problems.append(
                f"{ALLOWLIST.name}:{number}: expected {FIELDS} tab-separated fields, got {len(fields)}"
            )
            continue
        entry = Entry(number, fields)
        if entry.period not in PERIODS:
            problems.append(
                f"{ALLOWLIST.name}:{number}: unknown review period {entry.period!r} "
                f"(expected one of {sorted(PERIODS)})"
            )
            continue
        if entry.parts is None:
            problems.append(
                f"{ALLOWLIST.name}:{number}: {entry.action!r} is not <owner>/<repo>@<ref>, so it "
                f"cannot be resolved or matched against a workflow"
            )
            continue
        if entry.reviewed != "never":
            try:
                dt.date.fromisoformat(entry.reviewed)
            except ValueError:
                problems.append(
                    f"{ALLOWLIST.name}:{number}: last-reviewed {entry.reviewed!r} is not "
                    f"an ISO date (YYYY-MM-DD) or 'never'"
                )
                continue
        entries.append(entry)
    return entries, problems


def run(command: str) -> tuple[int, str]:
    try:
        done = subprocess.run(
            command, shell=True, capture_output=True, text=True, timeout=60
        )
    except (OSError, subprocess.SubprocessError) as exc:  # pragma: no cover - env specific
        return 1, str(exc)
    return done.returncode, (done.stdout + done.stderr).strip()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--resolve", action="store_true", help="run the resolver (needs network)")
    parser.add_argument("--today", default=None, help="YYYY-MM-DD, for the due-date logic")
    parser.add_argument(
        "--require-reviewed",
        action="store_true",
        help="exit 1 when a review is overdue or has never been done",
    )
    args = parser.parse_args()

    today = dt.date.fromisoformat(args.today) if args.today else dt.date.today()
    entries, problems = read_entries()

    for problem in problems:
        print(f"FAIL {problem}")

    overdue = 0
    for entry in entries:
        match = entry.parts
        assert match is not None
        due = entry.due_date(today)
        status = "OK"
        if entry.reviewed == "never":
            status = "NEVER REVIEWED"
            overdue += 1
        elif entry.overdue(today):
            status = "OVERDUE"
            overdue += 1
        print(f"== {entry.action}")
        print(f"   repository      {match['owner']}/{match['repo']}")
        print(f"   ref             {match['ref']}")
        print(f"   period          {entry.period} ({PERIOD_DAYS[entry.period]} days)")
        print(f"   last reviewed   {entry.reviewed}")
        print(f"   next due        {due.isoformat() if due else 'unknown'}")
        print(f"   status          {status}")
        print(f"   note            {entry.note}")
        print(f"   resolve with    {entry.resolve_command()}")
        if args.resolve:
            code, output = run(entry.resolve_command())
            if code == 0 and output:
                print(f"   resolves to     {output}")
            else:
                print(f"   resolves to     FAILED (exit {code}): {output or 'no output'}")
        print()

    print(f"action pin review: {len(entries)} entry(s), {overdue} needing a review, today={today.isoformat()}")
    if not args.resolve:
        print("planning only - re-run with --resolve on a machine with network and gh/git")
    if problems:
        return 1
    if args.require_reviewed and overdue:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
