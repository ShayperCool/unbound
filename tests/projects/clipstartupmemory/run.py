#!/usr/bin/env python3
"""Profile the first script-created clip in fresh Unity and Unbound sessions."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402
from probe_paths import inside_git_checkout, run_root, unity_version  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent / 'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])
PROFILES = ('PCM', 'Callback', 'Stream', 'Reset', 'ResetOwner', 'Disabled')
CAPTURE = 'CLIP_STARTUP_OUT'
PROFILE_RECORD = 'clip-startup-profile.json'


def configure_disabled(project):
    settings = project / 'ProjectSettings/AudioManager.asset'
    text = settings.read_text()
    if text.count('m_DisableAudio: 0') != 1:
        raise ValueError(f'{settings}: expected enabled audio')
    settings.write_text(text.replace('m_DisableAudio: 0', 'm_DisableAudio: 1'))


def private_run(label):
    if not label or label in ('.', '..') or Path(label).name != label:
        raise SystemExit(f'{label}: expected a single run directory name')
    base = run_root(PROBE, '').resolve()
    root = run_root(PROBE, label).resolve()
    if root == base or not root.is_relative_to(base) or inside_git_checkout(root):
        raise SystemExit(f'{label}: run directory must stay in the probe cache outside any checkout')
    return root


def capture_record(run, profile):
    capture = run / 'capture'
    metadata = capture / value_probe.editor_runner.RESULT
    values = capture / 'values.jsonl'
    identity = capture / 'profile-release.txt'
    for path in (capture, metadata, values, identity):
        if path.resolve() != path:
            raise ValueError(f'{path}: capture files must stay in their run directory')
    if metadata.exists() and metadata.stat().st_nlink != 1:
        raise ValueError(f'{metadata}: capture metadata must have a single filesystem link')
    editor_profile, editor_release = identity.read_text().splitlines()
    actual_version = (run / 'project/ProjectSettings/ProjectVersion.txt').read_text().splitlines()
    if editor_profile != profile or editor_release != unity_version() or not actual_version or (
            actual_version[0] != 'm_EditorVersion: ' + editor_release):
        raise ValueError('Editor record does not match the selected profile and release')
    return {
        'profile': editor_profile,
        'release': editor_release,
        'values_sha256': hashlib.sha256(values.read_bytes()).hexdigest(),
        'launch_sha256': hashlib.sha256((run / value_probe.editor_runner.RESULT).read_bytes()).hexdigest(),
    }


def publish(profile, labels):
    labels = [f'{label}-{profile}' for label in labels]
    roots = [private_run(label) for label in labels]
    if len(roots) < 2 or len(roots) != len(set(roots)):
        raise SystemExit('publish requires at least two distinct Editor runs')
    results = set()
    for run in roots:
        try:
            recorded = json.loads((run / PROFILE_RECORD).read_text())
            actual = capture_record(run, profile)
        except (OSError, ValueError) as error:
            raise SystemExit(f'{run}: missing or invalid Editor record: {error}') from error
        if actual != recorded:
            raise SystemExit(f'{run}: captured values or launch metadata changed after recording')
        result = actual['launch_sha256']
        if result in results:
            raise SystemExit(f'{run}: duplicate Editor launch record')
        results.add(result)
    return value_probe.publish(PROBE, [str(root) for root in roots], profile=profile)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--profile', choices=PROFILES)
    parser.add_argument('--label', default=f'run-{os.getpid()}')
    args = parser.parse_intermixed_args()
    if args.mode != 'gate' and not args.profile:
        parser.error(f'{args.mode} requires --profile')
    if args.mode == 'publish':
        return publish(args.profile, args.labels)

    statuses = []
    for profile in (args.profile,) if args.profile else PROFILES:
        os.environ['CLIP_STARTUP_PROFILE'] = profile
        label = f'{args.label}-{profile}'
        out = private_run(label)
        if out.exists():
            raise SystemExit(f'{out}: expected a fresh run directory')
        configure = configure_disabled if profile == 'Disabled' else None
        if args.mode == 'record':
            value_probe.record(PROBE, 'RecordStartupMemory.Record', CAPTURE, str(out), batch=True,
                               macos_batch=True, base=BASE, configure=configure)
            (out / PROFILE_RECORD).write_text(json.dumps(capture_record(out, profile)) + '\n')
            return 0
        statuses.append(value_probe.gate(PROBE, CAPTURE, str(out), 0, ['--headless', '12'], base=BASE,
                                         profile=profile, configure=configure))
    return max(statuses)


if __name__ == '__main__':
    sys.exit(main())
