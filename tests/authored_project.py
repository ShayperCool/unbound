#!/usr/bin/env python3
"""Editor-authored probe inputs, committed once, assembled into private projects without an Editor.

    authored_project.py record   PROBE SET --release R --project DIR --command 'native.py ...'
    authored_project.py assemble PROBE SET --release R --out DIR
    authored_project.py check    [PROBE ...] [--release R]

Some probes author their inputs in the Editor (mixer assets, scenes, the Editor's normalized
ProjectSettings, .meta GUIDs) in the same private project in which they capture the Native
reference. Their engine runs need that project. `record` keeps every file of such a project's
Assets/, Packages/ and ProjectSettings/ that the Editor created or changed, under
tests/projects/<probe>/Authored/unity-<release>/<set>/, with authored.json: the command that
authored it and the SHA-256 of every fixture file it started from and of its authoring driver. These are authored inputs, not
run output; the capture made in the same Editor session stays in the cache.

`assemble` copies the fixture's Assets/, Packages/ and ProjectSettings/ to a new directory and
overlays the set: the project the Editor would have authored. It refuses a set whose fixture or
driver hashes no longer match (a stale set is never used): re-run the recorded command in a fresh cache
directory and `record` its project again. Helpers the driver imports (verifiers, packers,
the Editor runner) are not hashed: they check or run a capture but do not choose what is authored;
the C# runners that author are fixture files. `check` verifies every set (or those of PROBE).
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys

PROJECTS = Path(__file__).resolve().parent/'projects'
PARTS = ('Assets', 'Packages', 'ProjectSettings')
FORMAT = 1


def files(root):
    """Relative path -> SHA-256 of every file under root's Assets/, Packages/, ProjectSettings/."""
    out = {}
    for part in PARTS:
        for path in sorted((root/part).rglob('*')):
            if path.is_file():
                out[path.relative_to(root).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return out


def inputs(probe, command):
    """The fixture files and the authoring driver named in command (it chooses the authored cases)."""
    out = files(PROJECTS/probe)
    for word in command.split():
        if word.startswith('tests/projects/') and word.endswith('.py'):
            driver = PROJECTS.parent.parent/word
            if not driver.is_file():
                raise SystemExit(f'{probe}: authoring driver {word} does not exist')
            out[word] = hashlib.sha256(driver.read_bytes()).hexdigest()
    return out


def set_dir(probe, release, name):
    return PROJECTS/probe/'Authored'/('unity-'+release)/name


def stale(probe, release, name):
    """Why the set cannot be used (None when it matches the current fixture)."""
    directory = set_dir(probe, release, name)
    manifest_path = directory/'authored.json'
    if not manifest_path.is_file():
        return f'no authored set {directory}'
    manifest = json.loads(manifest_path.read_text())
    if manifest.get('format') != FORMAT or manifest.get('release') != release:
        return f'{manifest_path}: format {manifest.get("format")} release {manifest.get("release")}'
    current = inputs(probe, manifest['command'])
    recorded = manifest['fixture']
    changed = sorted(k for k in current.keys() | recorded.keys() if current.get(k) != recorded.get(k))
    if changed:
        return (f'{probe}/{name}: the fixture or its driver changed since the set was authored ({", ".join(changed[:8])}'
                f'{", ..." if len(changed) > 8 else ""}); re-run `{manifest["command"]}` in a fresh cache '
                f'directory and record its project again')
    overlay = files(directory)
    if overlay != manifest['files']:
        bad = sorted(k for k in overlay.keys() | manifest['files'].keys() if overlay.get(k) != manifest['files'].get(k))
        return f'{probe}/{name}: authored files differ from authored.json: {", ".join(bad[:8])}'
    return None


def record(probe, release, name, project, command):
    fixture = PROJECTS/probe
    if (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: '+release:
        raise SystemExit(f'{probe}: the fixture is not release {release}')
    base, authored = files(fixture), files(project)
    hashed = inputs(probe, command)
    if hashed.keys() == base.keys():
        raise SystemExit(f'{probe}: the command names no authoring driver under tests/projects/')
    removed = sorted(set(base) - set(authored))
    if removed:
        raise SystemExit(f'{project}: the Editor removed fixture files, which an overlay cannot express: {removed}')
    keep = {k: v for k, v in authored.items() if base.get(k) != v}
    directory = set_dir(probe, release, name)
    if directory.exists():
        shutil.rmtree(directory)
    for relative in keep:
        target = directory/relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(project/relative, target)
    manifest = {'format': FORMAT, 'release': release, 'probe': probe, 'set': name, 'command': command,
                'fixture': hashed, 'files': keep}
    (directory/'authored.json').write_text(json.dumps(manifest, indent=1, sort_keys=True)+'\n')
    print(f'{probe}/{name}: {len(keep)} authored files in {directory}')


def assemble(probe, release, name, out):
    problem = stale(probe, release, name)
    if problem:
        raise SystemExit('authored project: ' + problem)
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    for part in PARTS:
        shutil.copytree(PROJECTS/probe/part, out/part)
    directory = set_dir(probe, release, name)
    for relative in json.loads((directory/'authored.json').read_text())['files']:
        (out/relative).parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(directory/relative, out/relative)
    print(f'{probe}/{name}: assembled {out}')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='mode', required=True)
    for mode in ('record', 'assemble'):
        p = sub.add_parser(mode)
        p.add_argument('probe')
        p.add_argument('set')
        p.add_argument('--release', required=True)
        if mode == 'record':
            p.add_argument('--project', type=Path, required=True, help='the private project the Editor authored')
            p.add_argument('--command', required=True, help='the command that authored it (repository-relative)')
        else:
            p.add_argument('--out', type=Path, required=True)
    p = sub.add_parser('check')
    p.add_argument('probes', nargs='*')
    p.add_argument('--release')
    args = parser.parse_args()
    if args.mode == 'record':
        record(args.probe, args.release, args.set, args.project.resolve(), args.command)
    elif args.mode == 'assemble':
        assemble(args.probe, args.release, args.set, args.out)
    else:
        problems, count = [], 0
        for manifest in sorted(PROJECTS.glob('*/Authored/unity-*/*/authored.json')):
            probe, release, name = manifest.parts[-5], manifest.parts[-3][len('unity-'):], manifest.parts[-2]
            if (args.probes and probe not in args.probes) or (args.release and release != args.release):
                continue
            count += 1
            problem = stale(probe, release, name)
            if problem:
                problems.append(problem)
        for problem in problems:
            print(problem)
        print(f'{count} authored set(s), {len(problems)} stale')
        return int(bool(problems))
    return 0


if __name__ == '__main__':
    sys.exit(main())
