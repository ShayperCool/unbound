#!/usr/bin/env python3
"""Refuse unflagged changes to Unity references, comparator tolerances, accepted failure counts
and removed tests.

Usage: oracle_guard.py BASE HEAD

A change in BASE..HEAD that edits or deletes an existing file under
tests/projects/<probe>/Reference/, that changes a tolerance in a comparator, or that
declares an accepted failure count in tests/projects/<probe>/gate-accepted.json
(see tools/agents/suite.py; withdrawing one is free), needs a commit trailer in the range:

    Oracle-Change: <why; the new reference was recorded twice in the Editor and matched>

A net decrease of test cases needs:

    Test-Removal: <why the removed tests no longer apply>

Adding new references and new tests needs no trailer. Exit 0 = ok, 4 = refused.
"""
import json
import re
import subprocess
import sys

REFERENCE = re.compile(r'^tests/projects/[^/]+/Reference/')
COMPARATOR = re.compile(r'^tests/.*(compare|verif|check|regress)[^/]*\.py$', re.I)
ACCEPTED = re.compile(r'^tests/projects/[^/]+/gate-accepted\.json$')
TOLERANCE = re.compile(r'\b\w*(tol|tolerance|eps|epsilon|atol|rtol|threshold|max_?diff|allowed?_?diff|margin)\w*\b', re.I)
TEST_FILE = re.compile(r'(^managed/.*Tests?/.*\.cs$)|(^crates/.*\.rs$)|(^tests/.*test[^/]*\.py$)|(^tools/.*test[^/]*\.py$)')
TEST_MARKER = re.compile(r'\[(Test|TestCase|UnityTest|TestCaseSource)\b|#\[test\]|^\s*def test_')


def git(*args):
    return subprocess.run(['git', *args], check=True, capture_output=True, text=True).stdout


def accepted_failures(revision, path):
    """The accepted failure counts declared at a revision: a list (empty when the file is not
    there), or None when what is there is not a list in JSON."""
    shown = subprocess.run(['git', 'show', f'{revision}:{path}'], capture_output=True, text=True)
    if shown.returncode:
        return []
    try:
        entries = json.loads(shown.stdout)
    except ValueError:
        return None
    return entries if isinstance(entries, list) else None


def main():
    if len(sys.argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    base, head = sys.argv[1], sys.argv[2]
    body = git('log', '--format=%B', f'{base}..{head}')
    oracle_ok = re.search(r'^Oracle-Change:\s*\S', body, re.M) is not None
    removal_ok = re.search(r'^Test-Removal:\s*\S', body, re.M) is not None

    oracle, removed_tests = [], {}
    fork = git('merge-base', base, head).strip()
    for line in git('diff', '--name-status', '--no-renames', f'{base}...{head}').splitlines():
        status, path = line.split('\t', 1)
        if REFERENCE.match(path) and status[0] in 'MD':
            oracle.append(f'{path}: existing Unity reference {"deleted" if status[0] == "D" else "changed"}')
        if ACCEPTED.match(path) and status[0] != 'D':
            before, after = accepted_failures(fork, path) or [], accepted_failures(head, path)
            if after is None:
                oracle.append(f'{path}: accepted failure counts are not a JSON list')
            for entry in after or []:
                if entry not in before:
                    oracle.append(f'{path}: accepted failure count declared: '
                                  f'{json.dumps(entry, ensure_ascii=False)[:160]}')
        if COMPARATOR.match(path) and status[0] == 'M':
            for diff_line in git('diff', '-U0', f'{base}...{head}', '--', path).splitlines():
                if diff_line.startswith('-') and not diff_line.startswith('---') and TOLERANCE.search(diff_line):
                    oracle.append(f'{path}: tolerance line changed: {diff_line[1:].strip()[:100]}')
        if TEST_FILE.search(path):
            delta = 0
            for diff_line in git('diff', '-U0', f'{base}...{head}', '--', path).splitlines():
                if diff_line.startswith(('---', '+++')):
                    continue
                if diff_line[:1] in '+-' and TEST_MARKER.search(diff_line[1:]):
                    delta += 1 if diff_line[0] == '+' else -1
            if delta < 0:
                removed_tests[path] = -delta

    problems = []
    if oracle and not oracle_ok:
        problems += oracle
    if sum(removed_tests.values()) > 0 and not removal_ok:
        problems += [f'{path}: {count} test case(s) removed' for path, count in removed_tests.items()]
    if oracle and oracle_ok:
        print('oracle_guard: flagged oracle changes (Oracle-Change trailer present):')
        for item in oracle:
            print('  ' + item)
    if problems:
        print('oracle_guard: REFUSED. These changes need a commit trailer '
              '(Oracle-Change: / Test-Removal:) explaining why; never edit an oracle to pass:')
        for item in problems:
            print('  ' + item)
        return 4
    print('oracle_guard: ok')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
