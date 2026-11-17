#!/usr/bin/env python3
"""Native plugin bundles on macOS: which [DllImport] name loads a bundle; see README.md.

    run.py native --label L     two macOS Editor captures (batchmode), L-1 and L-2
    run.py publish L-1 L-2      two identical captures -> Reference/unity-<release>/macos.jsonl
    run.py check                an Unbound capture compared with the reference (the land gate)

A plugin bundle is a macOS file: on another OS `check` prints that it does not apply and passes.
Every run uses a fresh copy of the probe under $XDG_CACHE_HOME/unbound/validation/runs/pluginbundle,
with the bundles built there by clang; nothing is written here except by publish.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import reference_root, validation_cache  # noqa: E402
import macos_editor  # noqa: E402

OUT = 'values.jsonl'
WORK = validation_cache()/'runs'/PROBE.name
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
# (bundle, its executable, the answer it is built with, the bundle folder's .meta guid)
BUNDLES = [('Renamed', 'RenamedLib', 42, '5f1c0a3e9b2d4c7e8a6f1b3d5e7c9a21'),
           ('Same', 'Same', 7, '8d2e4f6a1c3b5d7e9f0a2c4e6b8d0f13')]
PLUGINS_GUID = '2b4d6f8a0c1e3a5c7e9b1d3f5a7c9e02'
INFO_PLIST = '''<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleExecutable</key>
	<string>{executable}</string>
	<key>CFBundleIdentifier</key>
	<string>com.unbound.probe.{bundle}</string>
	<key>CFBundlePackageType</key>
	<string>BNDL</string>
</dict>
</plist>
'''
# The Editor's settings: an Editor plugin for macOS on any CPU.
BUNDLE_META = '''fileFormatVersion: 2
guid: {guid}
folderAsset: yes
PluginImporter:
  externalObjects: {{}}
  serializedVersion: 2
  iconMap: {{}}
  executionOrder: {{}}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any: 
    second:
      enabled: 0
      settings: {{}}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: AnyCPU
        DefaultValueInitialized: true
        OS: OSX
  userData: 
  assetBundleName: 
  assetBundleVariant: 
'''
FOLDER_META = '''fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
'''


def reference():
    return reference_root(PROBE)/'macos.jsonl'


def copy_project(target):
    """The probe's project with its bundles built into Assets/Plugins."""
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, target/part)
    plugins = target/'Assets'/'Plugins'
    plugins.mkdir()
    (target/'Assets'/'Plugins.meta').write_text(FOLDER_META.format(guid=PLUGINS_GUID))
    for bundle, executable, answer, guid in BUNDLES:
        contents = plugins/f'{bundle}.bundle'/'Contents'
        (contents/'MacOS').mkdir(parents=True)
        (contents/'Info.plist').write_text(INFO_PLIST.format(executable=executable, bundle=bundle))
        subprocess.run(['clang', '-bundle', '-arch', 'arm64', '-O2', f'-DPLUGINBUNDLE_ANSWER={answer}',
                        '-o', str(contents/'MacOS'/executable), str(PROBE/'Plugins'/'pluginbundle.c')], check=True)
        (plugins/f'{bundle}.bundle.meta').write_text(BUNDLE_META.format(guid=guid))


def rows(path):
    return {r['k']: r['v'] for r in (json.loads(line) for line in Path(path).read_text().splitlines() if line.strip())}


def native(label):
    ok = True
    for repeat in (f'{label}-1', f'{label}-2'):
        out = WORK/repeat
        copy_project(out/'project')
        code = macos_editor.capture(out/'project', out, ['-executeMethod', 'ProbeRunner.Record'],
                                    {'PLUGIN_BUNDLE_OUT': str(out)})
        print(repeat, 'exit', code)
        ok = ok and code == 0 and (out/OUT).is_file()
    return 0 if ok else 1


def check():
    out = WORK/f'check-{time.time_ns()}'
    copy_project(out/'project')
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    launcher = [str(HEAVY), '--kind', 'engine-gpu'] if HEAVY.is_file() else []
    command = [*launcher, 'timeout', '600', cli, 'run', '-p', str(out/'project'), '--no-watch', '--frames', '30',
               '--size', '64x64']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=dict(os.environ, PLUGIN_BUNDLE_OUT=str(out)), stdout=log,
                              stderr=subprocess.STDOUT).returncode
    if code or not (out/OUT).is_file():
        print(f'pluginbundle: engine exit {code}, no capture; see {out}')
        return 1
    want, got = rows(reference()), rows(out/OUT)
    failures = 0
    for key in sorted(set(want) | set(got)):
        if want.get(key) != got.get(key):
            print(f'differs {key}: unity {json.dumps(want.get(key))} unbound {json.dumps(got.get(key))}')
            failures += 1
    keys = set(want) | set(got)
    print(f'pluginbundle: {len(keys) - failures}/{len(keys)} values match, {failures} failures')
    return 1 if failures else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'publish', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    args = parser.parse_args()
    if args.mode == 'check' and sys.platform != 'darwin':
        print('pluginbundle: macOS plugin bundles; not applicable on this OS (0 failures)')
        return 0
    if sys.platform != 'darwin':
        raise SystemExit('pluginbundle: the captures need the macOS Editor')
    if args.mode == 'native':
        if not args.label:
            parser.error('native needs --label')
        return native(args.label)
    if args.mode == 'publish':
        if len(args.labels) != 2:
            parser.error('publish takes two capture labels')
        return macos_editor.publish([WORK/label for label in args.labels], reference(), OUT)
    return check()


if __name__ == '__main__':
    sys.exit(main())
