#!/usr/bin/env python3
"""The buffers of baked lightmaps before they are put together: which texels of each lightmap are
used, the chart of each, the surface normal at each of its samples and the direct light of each,
and the bake's lights, after a bake in Edit mode.

    bake_buffers.py create [--scene S,..]  # authoring: the Editor makes Assets/Scenes (BuffersSetup.Create), or adds scenes
    bake_buffers.py native --label L    # an Editor capture (batch mode on the GPU runner)
    bake_buffers.py publish L1 L2       # two agreeing captures -> Reference/unity-<version>/ (what is there stays
                                        # and must be the captures' too; what is new is added)
    bake_buffers.py engine --label L    # Unbound (unbound test, Edit mode)
    bake_buffers.py compare --label L   # an engine run's buffers against the reference
    bake_buffers.py check               # engine run + compare
    bake_buffers.py replay L            # development: the Editor capture L's own bake input through Unbound's
                                        # rasterizer alone (cargo example baked_gbuffer), against the capture

`Assets/Editor/BuffersRecord.cs` opens each scene of Assets/Scenes and bakes it. In the Editor the
bake goes through its `UnityEditor.LightBaking` API, which writes the scene's bake input and
lightmap requests and each lightmap's unprocessed buffers; the reference keeps, per scene, digests
of the first two (`scene.json`: the instances in the bake's order, each lightmap's size, samples
per texel side and instances; `lights.json`: the lights in the bake's order and the Lighting Mode)
and the buffers `occupancy<N>.r2d`, `chartIndex<N>.int`, `normal<N>.r2d` and
`irradianceDirect<N>.r2d` as the Editor wrote them. Unbound's bake (Lightmapping.Bake) writes the
same buffers of its own where UNBOUND_BAKED_GI_BUFFERS says, and its layout to
Library/Unbound/BakedGI/<scene GUID>/atlas.json.

Compared exactly: the instances and their order, the lightmaps' sizes, samples and instances, which
texels are used, every texel's chart, which samples have a surface and the number the bake gives
each (its instance's place in the lightmap plus its triangle's in its submesh), the lights and
their order. Normals are unit vectors compared within NORMAL_TOLERANCE per component, a light's
numbers within LIGHT_TOLERANCE, a texel's direct light within DIRECT_TOLERANCE. What is logged
while the bake's input is made is compared too (the Editor's messages while it extracts the bake
input; of Unbound's during Lightmapping.Bake, those that are not its own notes of what its bake
does not make yet)."""
import argparse
from array import array
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
sys.path.insert(0, str(PROBE))
sys.dont_write_bytecode = True  # nothing is written into the checkout
import editor_bake  # noqa: E402
import editor_runner  # noqa: E402
from probe_paths import editor_path, private_label, project_copy, reference_root, run_root  # noqa: E402

WORK = run_root(PROBE, 'bakedbuffers')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
# The Editor turns the normals into world space in an order of operations Unbound does not
# repeat; both are unit vectors of 32-bit floats.
NORMAL_TOLERANCE = 1e-5
# A sample without a surface has this as its fourth value.
EMPTY = -1.0e6
UNBOUND_NOTE = 'Unbound'
BUFFERS = ('occupancy{}.r2d', 'chartIndex{}.int', 'normal{}.r2d', 'irradianceDirect{}.r2d')
# The direct light of a texel is compared within this, times its value where that is above 1.
# The Editor's and Unbound's 32-bit arithmetic differ in their last bits, which a narrow spot
# cone's falloff multiplies (a 20 degree cone: by about 65 before its table's slope); the worst
# seen is 2.5e-5 (`Lamps`).
DIRECT_TOLERANCE = 1e-4
# Two Editor bakes of one scene give a texel's light within this (see `repeats`).
REPEAT_TOLERANCE = 2e-6
# Of a bake input's light: what Unbound's bake takes of it, the numbers within this (relative).
LIGHT_FIELDS = ('type', 'mode', 'color', 'indirectColor', 'position', 'orientation', 'range', 'coneAngle', 'castsShadows')
LIGHT_TOLERANCE = 2e-6


def scenes():
    return sorted(p.stem for p in (PROBE/'Assets/Scenes').glob('*.unity'))


def editor(out, method, env, label, graphics=True, progress=None):
    job = editor_runner.EditorJob(out/'project', out/'editor.log', out, ['-executeMethod', method],
                                  batch=True, graphics=graphics, env=env,
                                  progress_files=[progress] if progress else [],
                                  progress_timeout=1200 if progress else None, timeout=2400,
                                  label='bakedbuffers-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    return int(result.get('exit_code') != 0 or bool(result.get('forced_exit')))


def create(names=None):
    """Makes all the scenes, or adds those named (comma-separated); nothing that exists is replaced."""
    target = PROBE/'Assets/Scenes'
    names = names.split(',') if names else None
    if names is None and target.exists():
        sys.exit(f'refusing to overwrite {target}')
    for name in names or []:
        if (target/(name + '.unity')).exists():
            sys.exit(f'refusing to overwrite {name}')
    out = WORK/private_label('create')
    project_copy(PROBE, out/'project', package_cache=False)
    env = {'BAKED_BUFFERS_SCENES': ','.join(names)} if names else {}
    if editor(out, 'BuffersSetup.Create', env, 'create', graphics=False):
        return 1
    made = out/'project/Assets/Scenes'
    if names is None:
        shutil.copytree(made, target)
        shutil.copy2(out/'project/Assets/Scenes.meta', PROBE/'Assets/Scenes.meta')
        return 0
    # Only the new scenes' files (their names first).
    for path in sorted(made.iterdir()):
        if any(path.name.startswith(name) for name in names) and not (target/path.name).exists():
            shutil.copy2(path, target/path.name)
    return 0


def digest(folder, log):
    """What a scene's bake input and lightmap requests say of its lightmaps."""
    out = {'bake': log, 'messages': [], 'instances': [], 'lightmaps': []}
    if (folder/'messages.txt').is_file():
        out['messages'] = (folder/'messages.txt').read_text().splitlines()
    if not (folder/'lightmapRequests.bin').is_file():
        return out
    out['instances'] = (folder/'instances.txt').read_text().splitlines()
    atlassing = editor_bake.read_lightmap_requests(folder/'lightmapRequests.bin')['atlassing']
    gbuffer_of, gbuffers = dict(atlassing['gbufferOfHash']), dict(atlassing['gbuffers'])
    for atlas in atlassing['lightmapped']:
        instances = gbuffers[gbuffer_of[atlas['hash']]]['instances']
        if [i['transformIndex'] for i in instances] != list(range(len(instances))):
            raise ValueError(f'{folder}: a lightmap whose instances are not numbered in their order')
        out['lightmaps'].append({'width': atlassing['sizes'][2 * atlas['id']], 'height': atlassing['sizes'][2 * atlas['id'] + 1],
                                 'supersampling': atlas['supersampling'], 'instances': [i['object'][0] for i in instances]})
    return out


def lights_digest(folder):
    """A scene's lights as its bake input has them, by their names, and its Lighting Mode."""
    if not (folder/'bakeInput.bin').is_file():
        return {'mixedMode': None, 'lights': []}
    bake = editor_bake.read_bake_input(folder/'bakeInput.bin')
    names = (folder/'lights.txt').read_text().splitlines()
    if len(names) != len(bake['lights']):
        raise ValueError(f'{folder}: {len(names)} names for {len(bake["lights"])} lights')
    return {'mixedMode': bake['settings']['mixedMode'],
            'lights': [dict(light, name=name) for name, light in zip(names, bake['lights'])]}


def native(label):
    out = WORK/label
    if out.exists():
        sys.exit(f'refusing to overwrite {out}')
    project_copy(PROBE, out/'project', package_cache=False)
    (out/'raw').mkdir()
    # The Editor loads its lightmapper's plug-ins by bare name from its Data/Tools folder, which
    # must be on the library path.
    tools = str(editor_path().parent/'Data/Tools')
    env = {'BAKED_BUFFERS_OUT': str(out/'raw'),
           'LD_LIBRARY_PATH': tools + (':' + os.environ['LD_LIBRARY_PATH'] if os.environ.get('LD_LIBRARY_PATH') else '')}
    code = editor(out, 'BuffersRecord.Record', env, label, progress=out/'raw/log.txt')
    if code or not (out/'raw/log.txt').is_file():
        return code or 1
    log = dict(line.split(': ', 1) for line in (out/'raw/log.txt').read_text().splitlines() if ': ' in line)
    for scene in scenes():
        target = out/'reference'/scene
        target.mkdir(parents=True)
        scene_digest = digest(out/'raw'/scene, log.get(scene, 'not baked'))
        (target/'scene.json').write_text(json.dumps(scene_digest, indent=1) + '\n')
        (target/'lights.json').write_text(json.dumps(lights_digest(out/'raw'/scene), indent=1) + '\n')
        for index in range(len(scene_digest['lightmaps'])):
            for name in BUFFERS:
                shutil.copy2(out/'raw'/scene/'lightmapRequest0'/name.format(index), target/name.format(index))
    return 0


def tree(root):
    return {str(p.relative_to(root)): p.read_bytes() for p in sorted(root.rglob('*')) if p.is_file()}


def repeats(name, first, other):
    """Whether two captures' file is the same result: byte for byte, but for a buffer of light,
    which the Editor sums in an order that varies from bake to bake (the same used texels, and
    values within REPEAT_TOLERANCE, times the value where that is above 1)."""
    if first == other:
        return True
    if other is None or not Path(name).name.startswith('irradiance'):
        return False
    a, b = editor_bake.parse_r2d(first), editor_bake.parse_r2d(other)
    if a[:3] != b[:3]:
        return False
    return all(x == y if k % 4 == 3 else abs(x - y) <= REPEAT_TOLERANCE * max(1.0, abs(x)) for k, (x, y) in enumerate(zip(a[3], b[3])))


def publish(labels):
    if len(labels) < 2:
        sys.exit('publishing needs two independent repeats')
    captures = [tree(WORK/label/'reference') for label in labels]
    differing = [name for capture in captures[1:] for name in sorted(captures[0].keys() | capture.keys())
                 if not repeats(name, captures[0].get(name, b''), capture.get(name))]
    if not captures[0] or differing:
        sys.exit(f'the repeats differ in {differing}; not publishing')
    target = reference_root(PROBE)
    # What the reference has stays as it is: the captures must repeat it.
    present = tree(target) if target.exists() else {}
    changed = [name for name, data in present.items() if not repeats(name, data, captures[0].get(name))]
    if changed:
        sys.exit(f'the captures differ from the reference in {changed}; not publishing')
    for name, data in captures[0].items():
        if name not in present:
            (target/name).parent.mkdir(parents=True, exist_ok=True)
            (target/name).write_bytes(data)
    return 0


def expected(root):
    """The reference's scenes: each one's digest and its lightmaps' buffers."""
    out = {}
    for scene in scenes():
        folder = root/scene
        entry = json.loads((folder/'scene.json').read_text())
        for index, lightmap in enumerate(entry['lightmaps']):
            width, height, channels, occupancy = editor_bake.read_r2d(folder/f'occupancy{index}.r2d')
            if (width, height) != (lightmap['width'], lightmap['height']):
                raise ValueError(f'{folder}: occupancy{index} of {width}x{height}')
            lightmap['occupancy'] = [int(occupancy[k] > 0) for k in range(0, len(occupancy), channels)]
            lightmap['charts'] = list(editor_bake.read_chart_indices(folder/f'chartIndex{index}.int'))
            width, height, channels, normals = editor_bake.read_r2d(folder/f'normal{index}.r2d')
            side = lightmap['supersampling']
            if (width, height, channels) != (side * lightmap['width'], side * lightmap['height'], 4):
                raise ValueError(f'{folder}: normal{index} of {width}x{height}')
            lightmap['normals'] = normals
            width, height, channels, direct = editor_bake.read_r2d(folder/f'irradianceDirect{index}.r2d')
            if (width, height, channels) != (lightmap['width'], lightmap['height'], 4):
                raise ValueError(f'{folder}: irradianceDirect{index} of {width}x{height}')
            lightmap['direct'] = direct
        entry['baked'] = 'Success' in entry['bake']
        entry['lights'] = json.loads((folder/'lights.json').read_text())
        out[scene] = entry
    return out


def guid_of(project, scene):
    meta = (project/'Assets/Scenes'/(scene + '.unity.meta')).read_text()
    return re.search(r'(?m)^guid: ([0-9a-f]{32})$', meta).group(1)


def numbers(path, code):
    values = array(code)
    values.frombytes(path.read_bytes())
    return values


def actual(out):
    """An engine run's scenes, in the reference's form."""
    project = out/'project'
    log = dict(line.split(': ', 1) for line in (out/'raw/log.txt').read_text().splitlines() if ': ' in line)
    result = {}
    for scene in scenes():
        guid = guid_of(project, scene)
        entry = {'bake': log.get(scene, 'not baked'), 'messages': [], 'instances': [], 'lightmaps': [], 'lights': {'lights': []}}
        entry['baked'] = entry['bake'] == 'bake True'
        if (out/'raw'/scene/'messages.txt').is_file():
            # Unbound's own notes of what its bake does not make yet are not the Editor's messages.
            entry['messages'] = [line for line in (out/'raw'/scene/'messages.txt').read_text().splitlines() if UNBOUND_NOTE not in line]
        layout = project/'Library/Unbound/BakedGI'/guid/'atlas.json'
        dump = out/'buffers'/guid
        if layout.is_file() and (dump/'buffers.json').is_file():
            entry['instances'] = [r['path'] for r in json.loads(layout.read_text())['renderers']]
            description = json.loads((dump/'buffers.json').read_text())
            entry['lights'] = {'lights': description['lights']}
            for index, lightmap in enumerate(description['lightmaps']):
                lightmap['occupancy'] = [int(v > 0) for v in (dump/f'occupancy{index}.u8').read_bytes()]
                lightmap['charts'] = list(numbers(dump/f'chartIndex{index}.i32', 'i'))
                lightmap['normals'] = numbers(dump/f'normal{index}.f32', 'f')
                lightmap['direct'] = numbers(dump/f'irradianceDirect{index}.f32', 'f')
                entry['lightmaps'].append(lightmap)
        result[scene] = entry
    return result


def engine(label):
    out = WORK/label
    if out.exists():
        sys.exit(f'refusing to overwrite {out}')
    project = project_copy(PROBE, out/'project')
    (out/'raw').mkdir()
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, BAKED_BUFFERS_OUT=str(out/'raw'), BAKED_BUFFERS_AUTO='1', UNBOUND_BAKED_GI_BUFFERS=str(out/'buffers'))
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '1200', cli, 'test', '-p', str(project),
               '--platform', 'EditMode']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print('engine exit', code, 'log', out/'engine.log')
    return code or int(not (out/'raw/log.txt').is_file())


def differing(left, right, limit=6):
    """How many values differ, and where the first of them are."""
    if len(left) != len(right):
        return f'{len(left)} values, expected {len(right)}'
    where = [k for k in range(len(left)) if left[k] != right[k]]
    return f'{len(where)} of {len(left)} differ, first at {where[:limit]}' if where else None


def normal_difference(got, want):
    """Samples with a surface and their numbers exactly, their normals within the tolerance."""
    if len(got) != len(want):
        return f'{len(got) // 4} samples, expected {len(want) // 4}'
    surfaces = numbers_wrong = off = 0
    worst = 0.0
    for k in range(0, len(want), 4):
        if (got[k + 3] == EMPTY) != (want[k + 3] == EMPTY):
            surfaces += 1
        elif got[k + 3] != want[k + 3]:
            numbers_wrong += 1
        elif want[k + 3] != EMPTY:
            error = max(abs(got[k + c] - want[k + c]) for c in range(3))
            worst = max(worst, error)
            off += not error <= NORMAL_TOLERANCE
    if surfaces or numbers_wrong or off:
        return (f'{surfaces} samples differ in having a surface, {numbers_wrong} in their number, {off} in their normal '
                f'(worst {worst:.3g}, tolerance {NORMAL_TOLERANCE:g})')
    return None


def light_difference(got, want):
    """The bake's lights by their names in its order, and of each what Unbound's bake takes."""
    if [light['name'] for light in got] != [light['name'] for light in want]:
        return f"expected {[light['name'] for light in want]} actual {[light['name'] for light in got]}"
    problems = []
    for mine, theirs in zip(got, want):
        for field in LIGHT_FIELDS:
            a, b = mine[field], theirs[field]
            pairs = list(zip(a, b)) if isinstance(b, list) else [(a, b)]
            if isinstance(b, list) and len(a) != len(b) or any(
                    abs(x - y) > LIGHT_TOLERANCE * max(1.0, abs(y)) if isinstance(y, float) else x != y for x, y in pairs):
                problems.append(f"{theirs['name']}.{field}: expected {b} actual {a}")
    return '; '.join(problems) or None


def direct_difference(got, want):
    """Used texels exactly (the fourth value), their light within the tolerance."""
    if len(got) != len(want):
        return f'{len(got) // 4} texels, expected {len(want) // 4}'
    used = off = 0
    worst = 0.0
    for k in range(0, len(want), 4):
        used += got[k + 3] != want[k + 3]
        for c in range(3):
            error = abs(got[k + c] - want[k + c]) / max(1.0, abs(want[k + c]))
            worst = max(worst, error)
            off += not error <= DIRECT_TOLERANCE
    if used or off:
        return f'{used} texels differ in being used, {off} values in their light (worst {worst:.3g}, tolerance {DIRECT_TOLERANCE:g})'
    return None


def compare(label):
    want, got = expected(reference_root(PROBE)), actual(WORK/label)
    records = []

    def record(key, problem):
        records.append((key, problem))
        if problem:
            print('FAIL', key, problem)

    def same(key, a, b):
        record(key, None if a == b else f'expected {b!r} actual {a!r}')

    for scene in scenes():
        e, a = want[scene], got[scene]
        same(scene + '.baked', a['baked'], e['baked'])
        same(scene + '.messages', a['messages'], e['messages'])
        same(scene + '.instances', a['instances'], e['instances'])
        record(scene + '.lights', light_difference(a['lights']['lights'], e['lights']['lights']))
        form = lambda entry: ' '.join(f"{m['width']}x{m['height']}@{m['supersampling']}" for m in entry['lightmaps'])  # noqa: E731
        same(scene + '.lightmaps', form(a), form(e))
        for index, lightmap in enumerate(e['lightmaps']):
            key = f'{scene}.lightmap{index}.'
            mine = a['lightmaps'][index] if index < len(a['lightmaps']) else None
            if mine is None or (mine['width'], mine['height'], mine['supersampling']) != (lightmap['width'], lightmap['height'], lightmap['supersampling']):
                for part in ('instances', 'occupancy', 'charts', 'normals', 'direct'):
                    record(key + part, 'no such lightmap of that size')
                continue
            names = lambda entry, m: [entry['instances'][i] for i in m['instances']]  # noqa: E731
            same(key + 'instances', names(a, mine), names(e, lightmap))
            record(key + 'occupancy', differing(mine['occupancy'], lightmap['occupancy']))
            record(key + 'charts', differing(mine['charts'], lightmap['charts']))
            record(key + 'normals', normal_difference(mine['normals'], lightmap['normals']))
            record(key + 'direct', direct_difference(mine['direct'], lightmap['direct']))
    failed = sum(problem is not None for _, problem in records)
    print(f'bakedbuffers: {len(records) - failed}/{len(records)} records match; {failed} differ')
    return int(failed != 0)


def replay(label):
    """The capture's bake inputs through Unbound's rasterizer alone."""
    example = os.environ.get('UNBOUND_BAKED_GBUFFER')
    if not example:
        sys.exit('UNBOUND_BAKED_GBUFFER names the built example (cargo build --release -p unbound-gi --example baked_gbuffer)')
    out = WORK/label
    failed = 0
    for scene in scenes():
        folder = out/'raw'/scene
        if not (folder/'lightmapRequests.bin').is_file():
            continue
        bake = editor_bake.read_bake_input(folder/'bakeInput.bin')
        atlassing = editor_bake.read_lightmap_requests(folder/'lightmapRequests.bin')['atlassing']
        gbuffer_of, gbuffers = dict(atlassing['gbufferOfHash']), dict(atlassing['gbuffers'])
        for index, atlas in enumerate(atlassing['lightmapped']):
            width, height = atlassing['sizes'][2 * atlas['id']], atlassing['sizes'][2 * atlas['id'] + 1]
            data = bytearray(struct.pack('<4I', width, height, atlas['supersampling'], len(bake['meshes'])))
            for mesh in bake['meshes']:
                data += struct.pack('<I', mesh['vertex']['count'])
                for which in (0, 1, 3):
                    for value in editor_bake.channel(mesh, which):
                        data += struct.pack(f'<{len(value)}f', *value)
                data += struct.pack('<4f', *mesh['uvScaleOffset']) + struct.pack('<I', len(mesh['submeshCounts']))
                for offset, count in zip(mesh['submeshOffsets'], mesh['submeshCounts']):
                    data += struct.pack('<I', count // 3) + mesh['indices'][offset:offset + count].tobytes()
            instances = gbuffers[gbuffer_of[atlas['hash']]]['instances']
            data += struct.pack('<I', len(instances))
            for instance in instances:
                data += struct.pack('<I4fi', bake['instances'][instance['object'][0]]['mesh'], *instance['scaleOffset'], instance['transformIndex'])
            work = out/'replay'/scene/str(index)
            work.mkdir(parents=True, exist_ok=True)
            (work/'input.bin').write_bytes(data)
            subprocess.run([example, str(work/'input.bin'), str(work)], check=True)
            _, _, channels, occupancy = editor_bake.read_r2d(folder/'lightmapRequest0'/f'occupancy{index}.r2d')
            problems = {
                'occupancy': differing([int(v > 0) for v in (work/'occupancy.u8').read_bytes()],
                                       [int(occupancy[k] > 0) for k in range(0, len(occupancy), channels)]),
                'charts': differing(list(numbers(work/'charts.i32', 'i')),
                                    list(editor_bake.read_chart_indices(folder/'lightmapRequest0'/f'chartIndex{index}.int'))),
            }
            # The example's normals are in each renderer's own space: only which samples have a
            # surface and their numbers are compared here.
            mine = numbers(work/'normals.f32', 'f')
            theirs = editor_bake.read_r2d(folder/'lightmapRequest0'/f'normal{index}.r2d')[3]
            problems['samples'] = differing([mine[k] for k in range(3, len(mine), 4)], [theirs[k] for k in range(3, len(theirs), 4)])
            for part, problem in problems.items():
                print('FAIL' if problem else 'ok', f'{scene}.lightmap{index}.{part}', problem or '')
                failed += problem is not None
    return int(failed != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['create', 'native', 'engine', 'publish', 'compare', 'check', 'replay'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--scene', help='create: add these scenes only (comma-separated)')
    args = parser.parse_args()
    labels = args.labels + ([args.label] if args.label else [])
    if any(Path(label).name != label or label in ('.', '..') for label in labels):
        sys.exit('labels are plain names')
    if args.mode == 'create':
        return create(args.scene)
    if args.mode == 'native':
        return native(labels[0] if labels else private_label('native'))
    if args.mode == 'publish':
        return publish(labels)
    if args.mode == 'replay':
        return replay(labels[0])
    if args.mode in ('engine', 'compare') and not labels:
        labels = [private_label(args.mode)]
    if args.mode == 'engine':
        return engine(labels[0])
    if args.mode == 'compare':
        return compare(labels[0])
    label = private_label('check')
    return engine(label) or compare(label)


if __name__ == '__main__':
    sys.exit(main())
