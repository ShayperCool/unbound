#!/usr/bin/env python3
"""What the Editor writes into a project and when (see README.md).

    run.py record --label L     one capture with the macOS Editor in batch mode (the caller holds
                                the machine's heavy-job lock)
    run.py publish A B          two identical captures -> Reference/unity-<release>/values.jsonl
    run.py gate [--label L]     Unbound on a fresh copy, compared with the reference

Both hosts run on a copy staged here: the probe's project, files without a .meta under
Assets/Startup (the Editor imports them when it opens the project) and no packages-lock.json (the
Editor writes it then). The probe records the files each step may touch right after its call;
after the run this driver adds `exit.<path>` for every file of Assets/, Packages/ and
ProjectSettings/ that differs from the staged copy, and `exit.guids` (the GUIDs of new .meta
files are 32 lowercase hex digits). The comparison is exact; residuals.json lists known
differences with their cause.
"""
import argparse
import base64
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import editor_runner  # noqa: E402
from probe_paths import run_root, unity_version  # noqa: E402
import value_probe  # noqa: E402

VARIABLE = 'PROJECT_WRITES_OUT'
METHOD = 'ProjectWritesProbe.Record'
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
COMPLETE = {'k': 'complete', 'v': True}
PNG = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAL0lEQVR42hXKMQEAMBACMYQhjBFR+PtepyyR1Fq94FByz26DQymMMHD5Y4wxcPgAuL0fObgpuyQAAAAASUVORK5CYII=')
def wav():
    """A 0.01 s mono 16-bit PCM WAV of silence at 8 kHz."""
    samples = b'\0\0' * 80
    return (b'RIFF' + (36 + len(samples)).to_bytes(4, 'little') + b'WAVEfmt ' + (16).to_bytes(4, 'little')
            + (1).to_bytes(2, 'little') + (1).to_bytes(2, 'little') + (8000).to_bytes(4, 'little')
            + (16000).to_bytes(4, 'little') + (2).to_bytes(2, 'little') + (16).to_bytes(2, 'little')
            + b'data' + len(samples).to_bytes(4, 'little') + samples)


def tga():
    """A 2x2 uncompressed 32-bit TGA."""
    return bytes([0, 0, 2] + [0] * 9 + [2, 0, 2, 0, 32, 8]) + bytes([255, 0, 0, 255, 0, 255, 0, 255] * 2)


PROJECTS = PROBE.parent
# Files without a .meta: the Editor imports them when it opens the project and writes their
# .meta files (the importer each extension gets, with its defaults). Committed probe files
# stand in for the formats this driver does not write itself.
STARTUP = {
    'Assets/Startup/Tex.png': PNG,
    'Assets/Startup/Model.obj': b'o Tri\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n',
    'Assets/Startup/Notes.txt': b'notes\n',
    'Assets/Startup/Data.json': b'{"a": 1}\n',
    'Assets/Startup/Startup.cs': b'public class Startup { }\n',
    'Assets/Startup/Sub/Shader.shader': b'Shader "Probe/Startup" { SubShader { Pass { } } }\n',
    'Assets/Startup/Sound.wav': wav(),
    'Assets/Startup/Image.tga': tga(),
    'Assets/Startup/Data.xml': b'<a/>\n',
    'Assets/Startup/Data.bytes': b'\x00\x01\x02',
    'Assets/Startup/Data.csv': b'a,b\n1,2\n',
    'Assets/Startup/Readme.md': b'# readme\n',
    'Assets/Startup/Data.yaml': b'a: 1\n',
    'Assets/Startup/Page.html': b'<html></html>\n',
    'Assets/Startup/Unknown.xyz': b'xyz\n',
}
COPIED = {
    'Assets/Startup/Model.fbx': 'modelclips/Assets/Resources/AxisTies/U0pF1pC2p.fbx',
    'Assets/Startup/Sound.ogg': 'networkcaching/Content/audio/stereo.ogg',
    'Assets/Startup/Sound.mp3': 'networkcaching/Content/audio/low.mp3',
    'Assets/Startup/Font.ttf': 'textcore/Assets/SubsetFonts/TextCoreNoCaps.ttf',
    'Assets/Startup/Image.psd': 'textureimport/Assets/Textures/Psd16Gray.psd',
    'Assets/Startup/Image.exr': 'terrainlightmaps/Assets/Scenes/Probe/Lightmap-0_comp_light.exr',
    'Assets/Startup/Image.hdr': 'cubeblocks/Assets/Textures/HdrHigh.hdr',
    'Assets/Startup/Material.mat': 'instanceids/Assets/Resources/Material.mat',
    'Assets/Startup/Orphan.prefab': 'flarelayerdependency/Assets/Resources/Orphan.prefab',
    'Assets/Startup/Scene.unity': 'eofstartup/Assets/Scenes/Probe.unity',
    'Assets/Startup/Move.anim': 'gameinterop/Assets/Resources/Move.anim',
    'Assets/Startup/Control.controller': 'assetinstantiate/Assets/Resources/Control.controller',
    'Assets/Startup/Empty.overrideController': 'gameinterop/Assets/Resources/OverrideEmpty.overrideController',
    'Assets/Startup/Upper.mask': 'humanoid/Assets/Resources/MaskUpper.mask',
    'Assets/Startup/Probe.uss': 'uxmlattributes/Assets/UI/Probe.uss',
    'Assets/Startup/Row.uxml': 'uxmltemplates/Assets/Resources/Row.uxml',
    'Assets/Startup/Theme.tss': 'uitk/Assets/UI/UnityDefaultRuntimeTheme.tss',
    'Assets/Startup/Control.compute': 'assetinstantiate/Assets/Resources/Control.compute',
    'Assets/Startup/Common.hlsl': 'editor/Assets/Probe/DoublerCommon.hlsl',
    'Assets/Startup/Include.cginc': 'shaderkeywords/Assets/Shaders/KwPlainInclude.cginc',
    'Assets/Startup/Payload.physicMaterial': 'assetadapters/Authored/unity-6000.0.71f1/default/Assets/Resources/Payload3D.physicMaterial',
    'Assets/Startup/Payload.physicsMaterial2D': 'assetadapters/Authored/unity-6000.0.71f1/default/Assets/Resources/Payload2D.physicsMaterial2D',
    'Assets/Startup/Probe.renderTexture': 'uxmlattributes/Assets/UI/ProbeRT.renderTexture',
    'Assets/Startup/S5.flare': 'flares/Assets/Resources/S5.flare',
    'Assets/Startup/Mono.mixer': 'audiodistortion/Authored/unity-6000.0.71f1/live/Assets/Resources/dist_mono.mixer',
    'Assets/Startup/Variants.shadervariants': 'classids/Assets/Lighting/Variants.shadervariants',
    'Assets/Startup/Sand.terrainlayer': 'terrain/Assets/Terrain/Sand.terrainlayer',
    'Assets/Startup/Room.giparams': 'realtimegi/Assets/Settings/RoomSystemsSystemTag1.giparams',
}
PARTS = ('Assets', 'Packages', 'ProjectSettings')
GUID = re.compile(rb'\b[0-9a-f]{32}\b')
TIME = re.compile(rb'timeCreated: [0-9]+')
FILE_ID = re.compile(rb'(&|fileID: )(-?[0-9]{5,})')
KEPT_IDS = {b'11400000', b'11500000', b'10900000'}
# A per-platform settings entry of a platform module: which ones a new .meta lists is a fact of
# the Editor installation (the probe checks them against the installed modules in the run).
PLATFORM_ENTRY = re.compile(rb'(?m)^( *)- serializedVersion: [0-9]+\n\1  buildTarget: (?!DefaultTexturePlatform\b|Standalone\b)\S+\n(?:\1  .*\n)*')
# Files compared as lines gone and added: settings and package files the Editor rewrites.
LINE_DIFFED = re.compile(r'^(ProjectSettings/.*|Packages/.*\.json)$')


def stage(out):
    """The probe's project for one run, as both hosts get it; returns (project, initial files)."""
    project = value_probe.assemble(PROBE, out)
    (project/'Packages/packages-lock.json').unlink(missing_ok=True)
    startup = {**STARTUP, **{path: (PROJECTS/source).read_bytes() for path, source in COPIED.items()}}
    for path, data in startup.items():
        (project/path).parent.mkdir(parents=True, exist_ok=True)
        (project/path).write_bytes(data)
    return project, files(project)


def files(project):
    return {path.relative_to(project).as_posix(): path.read_bytes()
            for part in PARTS for path in sorted((project/part).rglob('*')) if path.is_file()}


def guids(project):
    found = {}
    for meta in (project/'Assets').rglob('*.meta'):
        match = re.search(rb'^guid: ([0-9a-f]{32})', meta.read_bytes(), re.M)
        if match:
            found[match.group(1)] = meta.relative_to(project).as_posix()[:-len('.meta')]
    return found


def main_document_first(data):
    """A file's documents with the main object's first: the others' file ids are random and the
    Editor sorts documents by file id (`exit.<path>.idsAscending` records that order)."""
    parts = data.split(b'\n--- ')
    if len(parts) < 3:
        return data
    documents = parts[1:]
    main = next((i for i, d in enumerate(documents) if d.startswith(b'!u!114 &11400000\n')), 0)
    documents.insert(0, documents.pop(main))
    return parts[0] + b''.join(b'\n--- ' + d for d in documents)


DOCUMENT_ID = re.compile(rb'(?m)^--- !u![0-9]+ &(-?[0-9]+)')


def normalize(data, known):
    data = main_document_first(data)
    data = GUID.sub(lambda m: b'<guid:' + known.get(m.group(0), '?').encode() + b'>', data)
    data = TIME.sub(b'timeCreated: <time>', data)
    data = PLATFORM_ENTRY.sub(b'', data)
    ids = {}

    def local(m):
        if m.group(2) in KEPT_IDS:
            return m.group(0)
        return m.group(1) + ids.setdefault(m.group(2), b'<id%d>' % (len(ids)+1))
    return FILE_ID.sub(local, data).decode('utf-8', 'replace')


def lines_changed(before, after):
    gone = list(before)
    added = []
    for line in after:
        if line in gone:
            gone.remove(line)
        else:
            added.append(line)
    return gone, added


def exit_values(project, initial):
    """What the run left on disk that differs from the staged copy."""
    now = files(project)
    known = guids(project)
    values = {}
    for path in sorted(initial.keys() | now.keys()):
        before, after = initial.get(path), now.get(path)
        if before == after:
            continue
        key = 'exit.' + path
        if after is None:
            values[key] = '<removed>'
        elif path.endswith('.png'):
            values[key] = 'png ' + ('as written' if after == PNG else 'changed')
        elif LINE_DIFFED.match(path) and before is not None:
            gone, added = lines_changed(normalize(before, known).split('\n'), normalize(after, known).split('\n'))
            values[key + '.gone'] = '\n'.join(gone)
            values[key + '.added'] = '\n'.join(added)
            values[key + '.crlf'] = 'true' if b'\r\n' in after else 'false'
        else:
            values[key] = normalize(after, known)
            ids = [int(i) for i in DOCUMENT_ID.findall(after)]
            if len(ids) > 1:
                values[key + '.idsAscending'] = 'true' if ids == sorted(ids) else 'false'
    new_guids = [GUID.search(now[path]) for path in now if path.endswith('.meta') and path not in initial]
    values['exit.guids'] = 'all 32 lowercase hex' if all(new_guids) and new_guids else 'missing'
    return values


MODES = ('play', 'edit')


def exit_rows(mode, project, initial):
    return [{'k': f'{mode}.{k}', 'v': v} for k, v in exit_values(project, initial).items()]


def read_rows(path):
    rows = [json.loads(line) for line in path.read_text().splitlines() if line.strip()] if path.exists() else []
    if not rows or rows[-1] != COMPLETE:
        raise ValueError(f'{path}: the probe did not finish')
    return rows[:-1]


def combine(capture, parts):
    """capture/values.jsonl: each mode's rows and what it left on disk, then the completion marker."""
    rows = [row for part in parts for row in part]
    (capture/'values.jsonl').write_text(''.join(json.dumps(row, ensure_ascii=False) + '\n' for row in rows + [COMPLETE]))


def editor_job(mode, out, project, capture):
    if mode == 'play':
        arguments = ['-executeMethod', METHOD]
    else:
        arguments = ['-runTests', '-testPlatform', 'EditMode', '-testResults', str(out/'results.xml')]
    return editor_runner.EditorJob(project, out/'editor.log', out, arguments, batch=True, graphics=True,
                                   env={VARIABLE: str(capture)}, timeout=900, label=f'projectwrites-{out.parent.name}-{mode}')


def record(label):
    if sys.platform != 'darwin':
        raise SystemExit('projectwrites records with the macOS Editor in batch mode')
    root = run_root(PROBE, label)
    parts = []
    for mode in MODES:
        out = root/mode
        project, initial = stage(out)
        capture = out/'capture'
        capture.mkdir(parents=True)
        print(editor_runner.summary(editor_runner.run(editor_job(mode, out, project, capture))))
        problem = editor_runner.result_problem(out, gpu=True)
        if problem:
            raise SystemExit(f'{out}: {problem}')
        parts.append(read_rows(capture/'values.jsonl') + exit_rows(mode, project, initial))
    capture = root/'capture'
    capture.mkdir()
    combine(capture, parts)
    # publish() checks each capture's Editor result next to it.
    shutil.copyfile(root/'play'/editor_runner.RESULT, root/editor_runner.RESULT)
    print('captured', capture)
    return 0


def engine(mode, out, project, capture, cli):
    if mode == 'play':
        command = [cli, 'run', '-p', str(project), '--no-watch', '--headless', '600']
    else:
        command = [cli, 'test', '-p', str(project), '--platform', 'EditMode', '--results', str(out/'results.xml')]
    with (out/'engine.log').open('w') as log:
        return subprocess.run([str(LAUNCHER), '--kind', 'engine-cpu', 'timeout', '600', *command],
                              env={**os.environ, VARIABLE: str(capture)}, stdout=log, stderr=subprocess.STDOUT).returncode


def gate(label):
    root = run_root(PROBE, label)
    cli = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI') or str(PROBE.parents[2]/'target/release/unbound')
    parts, codes = [], []
    for mode in MODES:
        out = root/mode
        project, initial = stage(out)
        capture = out/'engine'
        capture.mkdir(parents=True)
        codes.append(engine(mode, out, project, capture, cli))
        try:
            parts.append(read_rows(capture/'values.jsonl') + exit_rows(mode, project, initial))
        except ValueError as error:
            print(f'projectwrites {mode}: no complete engine capture ({error}); engine exit {codes[-1]}; log {out}/engine.log')
            parts.append(exit_rows(mode, project, initial))
    combine(root, parts)
    actual = value_probe.read_values(root/'values.jsonl')
    expected = value_probe.read_values(value_probe.reference_dir(PROBE)/'values.jsonl')
    matched, failures = value_probe.compare(expected, actual, 0)
    known = value_probe.residuals(PROBE)
    residual = [f for f in failures if f['key'] in known]
    failures = [f for f in failures if f['key'] not in known]
    for failure in failures:
        print(json.dumps(failure, ensure_ascii=False))
    for failure in residual:
        print(json.dumps({**failure, 'residual': known[failure['key']]}, ensure_ascii=False))
    differing = {f['key'] for f in residual}
    for key in sorted(k for k in known if k not in differing and k in expected):
        print(f'residual resolved (remove it from residuals.json): {key}')
    # `unbound test` exits 2 when a test fails, as the Editor does; the probe's test does not fail.
    print(f'projectwrites: {matched}/{len(expected)} values match, {len(failures)} failures, '
          f'{len(residual)} known residuals; engine exits {codes}; logs {root}')
    return 1 if failures or any(codes) else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
    if args.mode == 'record':
        return record(label)
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels)
    return gate(label)


if __name__ == '__main__':
    sys.exit(main())
