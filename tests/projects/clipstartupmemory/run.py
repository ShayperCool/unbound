#!/usr/bin/env python3
"""Profile the first script-created clip in fresh Unity and Unbound sessions."""
import argparse
import json
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402
from probe_paths import run_root, unity_version  # noqa: E402

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


def publish(profile, labels):
    labels = [f'{label}-{profile}' for label in labels]
    roots = [run_root(PROBE, label).resolve() for label in labels]
    if len(roots) < 2 or len(roots) != len(set(roots)):
        raise SystemExit('publish requires at least two distinct Editor runs')
    expected = {'profile': profile, 'release': unity_version()}
    version = 'm_EditorVersion: ' + expected['release']
    results = set()
    for run in roots:
        try:
            record = json.loads((run / PROFILE_RECORD).read_text())
            actual = (run / 'project/ProjectSettings/ProjectVersion.txt').read_text().splitlines()
            result = (run / value_probe.editor_runner.RESULT).read_bytes()
        except (OSError, ValueError) as error:
            raise SystemExit(f'{run}: missing or invalid Editor record: {error}') from error
        if record != expected or not actual or actual[0] != version:
            raise SystemExit(f'{run}: Editor record does not match the selected profile and release')
        if result in results:
            raise SystemExit(f'{run}: duplicate Editor launch record')
        results.add(result)
    return value_probe.publish(PROBE, labels, profile=profile)


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
        configure = configure_disabled if profile == 'Disabled' else None
        if args.mode == 'record':
            value_probe.record(PROBE, 'RecordStartupMemory.Record', CAPTURE, label, batch=True,
                               macos_batch=True, base=BASE, configure=configure)
            record = {'profile': profile, 'release': unity_version()}
            (run_root(PROBE, label) / PROFILE_RECORD).write_text(json.dumps(record) + '\n')
            return 0
        statuses.append(value_probe.gate(PROBE, CAPTURE, label, 0, ['--headless', '12'], base=BASE,
                                         profile=profile, configure=configure))
    return max(statuses)


if __name__ == '__main__':
    sys.exit(main())
