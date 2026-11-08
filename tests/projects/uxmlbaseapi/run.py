#!/usr/bin/env python3
"""Measure legacy UXML base API defaults and replay the same calls in Unbound."""
import argparse
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import run_root, reference_root, unity_version
import editor_runner

HEAVY = Path.home()/'.cache/compat/with-heavy-job'

# These six errors are present in all three pre-Q85 API audit reports. They stay visible and
# belong to the particles follow-up, rather than the five legacy UXML findings measured here.
BASELINE_ACCESS_MESSAGES = {
    f"{field} is private, Unity's is internal" for field in (
        "UnityEngine.AnimationCurve m_CurveMax", "UnityEngine.AnimationCurve m_CurveMin",
        "UnityEngine.ParticleSystemCurveMode m_Mode", "float m_ConstantMax",
        "float m_ConstantMin", "float m_CurveMultiplier",
    )
}


def baseline_issue(issue):
    return (issue['category'] == 'access'
        and issue['type'] == 'UnityEngine.ParticleSystem+MinMaxCurve'
        and issue['message'] in BASELINE_ACCESS_MESSAGES)


def parse_api_audit(text, code):
    """Reject truncated, unparsed or unexpectedly terminated API audits."""
    issues, groups = [], []
    current, type_name, declared = None, None, None
    for line in text.splitlines():
        category = re.fullmatch(r'(\S+) \((\d+)\)', line)
        summary = re.match(r'^(\d+) errors in \d+ hand-written types ', line)
        if category:
            current = {'count': int(category[2]), 'actual': 0, 'category': category[1]}
            groups.append(current)
            type_name = None
        elif summary:
            declared = int(summary[1])
            current = None
        elif current and line.startswith('    '):
            if not type_name:
                raise ValueError('API issue has no type')
            issues.append({'category': current['category'], 'type': type_name, 'message': line.strip()})
            current['actual'] += 1
        elif current and line.startswith('  '):
            type_name = line.strip()
    if declared is None or declared != len(issues) or any(g['count'] != g['actual'] for g in groups):
        raise ValueError('incomplete API audit: summary or category counts disagree with diagnostics')
    if code != (1 if issues else 0):
        raise ValueError(f'API audit exit {code} disagrees with its diagnostics')
    return issues


def project(out):
    target = out/'project'
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT.parent/'lightingassets'/folder, target/folder)
    for folder in ('Editor', 'Scripts'):
        shutil.copytree(ROOT/'Assets'/folder, target/'Assets'/folder, dirs_exist_ok=True)
    manifest = target/'Packages/manifest.json'
    packages = json.loads(manifest.read_text())
    packages['dependencies']['com.unity.modules.uielements'] = '1.0.0'
    manifest.write_text(json.dumps(packages, indent=2)+'\n')
    (target/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
    return target


def values(directory):
    result = {}
    for line in (directory/'values.tsv').read_text().splitlines():
        key, value = line.split('\t', 1)
        if key in result:
            raise ValueError(f'duplicate record {key}')
        result[key] = value
    return result


def complete(directory):
    try:
        records = values(Path(directory))
        if len(records) != 15 or 'control.children.builtin' not in records:
            return 'incomplete base API capture'
    except (OSError, ValueError) as error:
        return str(error)
    return None


def record(label):
    out = run_root(ROOT, label)
    path = project(out)
    job = editor_runner.EditorJob(path, out/'editor.log', out,
        ['-executeMethod', 'UxmlBaseApiRunner.Record'], batch=True, graphics=True,
        env={'UXML_BASE_API_OUT': str(out)}, timeout=900, label='uxmlbaseapi-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    problem = editor_runner.result_problem(out, gpu=True) or complete(out)
    if problem:
        raise SystemExit(problem)


def gate(label):
    out = run_root(ROOT, label)
    path = project(out)
    cli = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI') or str(REPO/'target/release/unbound')
    env = {**os.environ, 'UXML_BASE_API_OUT': str(out/'engine')}
    with (out/'engine.log').open('w') as log:
        engine = subprocess.run([str(HEAVY), '--kind', 'engine-cpu', 'timeout', '600', cli,
            'run', '-p', str(path), '--headless', '1000', '--no-watch'], env=env,
            stdout=log, stderr=subprocess.STDOUT).returncode
    expected = json.loads((reference_root(ROOT)/'values.json').read_text())
    actual = values(out/'engine') if (out/'engine/values.tsv').exists() else {}
    failed = []
    for key in sorted(expected.keys() | actual.keys()):
        if key not in actual:
            failed.append({'key': key, 'missing': expected[key]})
        elif key not in expected:
            failed.append({'key': key, 'extra': actual[key]})
        elif expected[key] != actual[key]:
            failed.append({'key': key, 'expected': expected[key], 'actual': actual[key]})
    for failure in failed:
        print(json.dumps(failure))
    with (out/'api-check.log').open('w') as log:
        audit = subprocess.run([str(HEAVY), '--kind', 'tool', cli, 'api-check', '-p', str(path)],
            stdout=log, stderr=subprocess.STDOUT).returncode
    try:
        issues = parse_api_audit((out/'api-check.log').read_text(), audit)
    except ValueError as error:
        print(f'API audit incomplete: {error}; full log {out}/api-check.log')
        return 1
    unexpected = [issue for issue in issues if not baseline_issue(issue)]
    for issue in issues:
        print(json.dumps({'apiAudit': issue, 'knownParticlesBaseline': baseline_issue(issue)}))
    print(f'uxmlbaseapi: {len(expected)-len(failed)}/{len(expected)} records, {len(failed)} failures; engine exit {engine}, API audit {len(unexpected)} unexpected, {len(issues)-len(unexpected)} existing particle field-access errors; logs {out}')
    return 1 if failed or engine or unexpected else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['record', 'publish', 'gate'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default='check-'+str(time.time_ns()))
    args = parser.parse_args()
    if args.mode == 'record':
        record(args.label)
    elif args.mode == 'gate':
        return gate(args.label)
    else:
        def write(path, first):
            path.mkdir(parents=True)
            (path/'values.json').write_text(json.dumps(values(first), indent=2, sort_keys=True)+'\n')
        return editor_runner.publish([run_root(ROOT, label) for label in args.labels], reference_root(ROOT),
            lambda _: ['values.tsv'], write, complete=[complete], gpu=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())
