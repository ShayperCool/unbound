#!/usr/bin/env python3
"""Which tests the Test Framework's command-line filters select: `-testFilter`, `-testCategory`
and `-assemblyNames` (com.unity.test-framework 1.6.0) over two Edit Mode test assemblies.

    run.py editor  --release R [--label L]   two GPU batch Editors, each running every case on its
                                             own copy of the project (one -runTests launch a case)
    run.py publish --release R --label L     publishes the identical repeats to Reference/unity-R.jsonl
    run.py check   --release R [--label L]   runs every case in Unbound (`unbound test`, UNBOUND_CLI)
                                             and compares with the reference exactly

A case records the run's exit code and every executed test case's full name and result, in the
order of the results file. Run output goes to $XDG_CACHE_HOME/unbound/validation/runs/testfilter/<label>.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from editor_runner import EditorJob, result_problem, run_parallel, summary, vulkan_display_env  # noqa: E402
from probe_paths import private_label, project_copy, run_root  # noqa: E402

# (case, -testFilter, -testCategory, -assemblyNames)
CASES = (
    ('all', None, None, None),
    ('class-name', 'Alpha', None, None),
    ('full-name', 'Probe.Alpha.One', None, None),
    ('anchored-fixture', r'^Probe\.Alpha$', None, None),
    ('list', 'Probe.Alpha.One;Other.Gamma', None, None),
    ('exclusion', '!Alpha', None, None),
    ('assembly-name', 'ProbeTests', None, None),
    ('parameterized', 'Probe.Beta.Sum(2)', None, None),
    ('parameterized-escaped', r'Probe\.Beta\.Sum\(2\)', None, None),
    ('explicit', 'Probe.Beta.Hidden', None, None),
    ('no-match', 'Nothing', None, None),
    ('category', None, 'Slow', None),
    ('filter-and-category', 'Probe', 'Slow', None),
    ('assemblies', None, None, 'OtherTests'),
)
REPEATS = ('native1', 'native2')


def options(test_filter, category, assemblies):
    pairs = (('-testFilter', test_filter), ('-testCategory', category), ('-assemblyNames', assemblies))
    return [part for option, value in pairs if value is not None for part in (option, value)]


def record(case, code, results):
    """One reference line: the case, the exit code and the executed test cases."""
    tests = []
    if results.is_file():
        tests = [[t.get('fullname'), t.get('result')] for t in ET.parse(results).getroot().iter('test-case')]
    return {'case': case, 'exit': code, 'tests': tests}


def prepare(project, release):
    project_copy(PROBE, project, package_cache=False)
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+release+'\n')
    return project


def editor(work, release):
    jobs = []
    for repeat in REPEATS:
        project = prepare(work/'projects'/repeat, release)
        for case, *selection in CASES:
            out = work/repeat/case
            out.mkdir(parents=True)
            args = ['-force-glcore', '-runTests', '-testPlatform', 'EditMode', *options(*selection),
                    '-testResults', str(out/'results.xml')]
            jobs.append(EditorJob(project, out/'editor.log', out, args, batch=True, graphics=True,
                                  private_display=False, env=vulkan_display_env(), timeout=900,
                                  label=f'testfilter-{repeat}-{case}'))
    # The cases of one repeat share its project copy, so they run one after another; the two
    # repeats' runs of a case run side by side.
    for wave in (jobs[i::len(CASES)] for i in range(len(CASES))):
        for result in run_parallel(wave):
            print(summary(result))
    failed = 0
    for repeat in REPEATS:
        lines = []
        for case, *_ in CASES:
            out = work/repeat/case
            problem = result_problem(out, gpu=True)
            result = json.loads((out/'editor-run.json').read_text()) if (out/'editor-run.json').is_file() else {}
            if problem and not (problem == 'the Editor did not exit with 0' and not result.get('forced_exit')):
                print(f'{repeat}/{case}: {problem}')
                failed = 1
            lines.append(json.dumps(record(case, result.get('exit_code'), out/'results.xml')))
        (work/repeat/'testfilter.jsonl').write_text('\n'.join(lines)+'\n')
    first, second = [(work/r/'testfilter.jsonl').read_bytes() for r in REPEATS]
    if first != second:
        print('testfilter: the Editor repeats differ')
        failed = 1
    return failed


def check(work, release, reference):
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND')
    if not cli:
        raise SystemExit('set UNBOUND_CLI to an immutable runtime')
    expected = {row['case']: row for row in map(json.loads, reference.read_text().splitlines())}
    project = prepare(work/'projects'/'unbound', release)
    failed = matched = 0
    for case, *selection in CASES:
        out = work/'unbound'/case
        out.mkdir(parents=True)
        command = [str(Path.home()/'.cache/compat/with-heavy-job'), '--kind', 'engine-gpu', 'timeout', '600', cli,
                   'test', '-p', str(project), '--platform', 'EditMode', '--results', str(out/'results.xml')]
        for option, value in zip(('--filter', '--category', '--assembly-names'), selection):
            if value is not None:
                command += [option, value]
        with (out/'engine.log').open('w') as log:
            code = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT).returncode
        actual = record(case, code, out/'results.xml')
        want = expected.get(case)
        if want == actual:
            matched += 1
            continue
        failed += 1
        print(f'testfilter {case}: Editor {json.dumps(want)}')
        print(f'testfilter {case}: engine {json.dumps(actual)} (log {out/"engine.log"})')
    for case in sorted(set(expected) - {c for c, *_ in CASES}):
        failed += 1
        print(f'testfilter {case}: in the reference, not run')
    print(f'testfilter: {matched}/{len(CASES)} cases match')
    return int(failed > 0)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('mode', choices=('editor', 'publish', 'check'))
    p.add_argument('--release', required=True)
    p.add_argument('--label')
    a = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
    if a.mode == 'publish' and not a.label:
        p.error('publish needs the --label of an editor run')
    work = run_root(PROBE, a.label or private_label(a.mode))
    reference = PROBE/'Reference'/f'unity-{a.release}.jsonl'
    if a.mode == 'editor':
        return editor(work, a.release)
    if a.mode == 'publish':
        first, second = [(work/r/'testfilter.jsonl').read_bytes() for r in REPEATS]
        if first != second:
            raise SystemExit('the Editor repeats differ; not publishing')
        reference.parent.mkdir(exist_ok=True)
        with reference.open('xb') as f:
            f.write(first)
        return 0
    return check(work, a.release, reference)


if __name__ == '__main__':
    raise SystemExit(main())
