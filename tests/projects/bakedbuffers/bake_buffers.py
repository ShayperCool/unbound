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
                                        # rasterizer and direct light alone (cargo example baked_gbuffer)

`Assets/Editor/BuffersRecord.cs` opens each scene of Assets/Scenes and bakes it. In the Editor the
bake goes through its `UnityEditor.LightBaking` API, which writes the scene's bake input and
lightmap requests and each lightmap's unprocessed buffers; the reference keeps, per scene, digests
of the first two (`scene.json`: the instances in the bake's order, each lightmap's size, samples
per texel side and instances; `lights.json`: the lights in the bake's order and the Lighting Mode)
and the buffers `occupancy<N>.r2d`, `chartIndex<N>.int`, `normal<N>.r2d` and
`irradianceDirect<N>.r2d` as the Editor wrote them. Unbound's bake (Lightmapping.Bake) writes the
same buffers of its own where UNBOUND_BAKED_GI_BUFFERS says, and its layout to
LibraryUnbound/BakedGI/<scene GUID>/atlas.json.

Compared exactly: the instances and their order, the lightmaps' sizes, samples and instances, which
texels are used, every texel's chart, which samples have a surface and the number the bake gives
each (its instance's place in the lightmap plus its triangle's in its submesh), the lights and
their order. Normals are unit vectors compared within NORMAL_TOLERANCE per component, a light's
numbers within LIGHT_TOLERANCE, a texel's direct light within DIRECT_TOLERANCE. What is logged
while the bake's input is made is compared too (the Editor's messages while it extracts the bake
input; of Unbound's during Lightmapping.Bake, those that are not its own notes of what its bake
does not make yet)."""
import argparse
import difflib
from array import array
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import zlib

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
sys.path.insert(0, str(PROBE))
sys.dont_write_bytecode = True  # nothing is written into the checkout
import editor_bake  # noqa: E402
import editor_runner  # noqa: E402
from probe_paths import editor_path, private_label, project_copy, reference_root, run_root  # noqa: E402
from baked_files import image_difference, lighting_data_difference, lighting_data_fields, normalized  # noqa: E402

WORK = run_root(PROBE, 'bakedbuffers')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
# The Editor turns the normals into world space in an order of operations Unbound does not
# repeat; both are unit vectors of 32-bit floats.
NORMAL_TOLERANCE = 1e-5
# A sample without a surface has this as its fourth value.
EMPTY = -1.0e6
UNBOUND_NOTE = 'Unbound'
BUFFERS = ('occupancy{}.r2d', 'chartIndex{}.int', 'normal{}.r2d', 'irradianceDirect{}.r2d', 'irradianceIndirect{}.r2d',
           'irradianceEnvironment{}.r2d', 'validity{}.r2d', 'directionalityDirect{}.r2d', 'directionalityIndirect{}.r2d',
           'shadowmask{}.r2d')
# The part of a texel's samples a shadowmask channel's light reaches that see it: counts.
SHADOWMASK_TOLERANCE = 1e-6
# Where a texel's light comes from (directional lightmaps): its mean direction and how much its
# samples face it, each mapped to 0..1. The direct light's follows its light within its last
# bits (the worst seen is 4.2e-7), the indirect light's its paths, as the indirect light does.
DIRECTIONALITY_TOLERANCE = 1e-4
# The direct light of a texel is compared within this, times its value where that is above 1.
# The Editor's and Unbound's 32-bit arithmetic differ in their last bits, which a narrow spot
# cone's falloff multiplies (a 20 degree cone: by about 65 before its table's slope); the worst
# seen is 2.5e-5 (`Lamps`).
DIRECT_TOLERANCE = 1e-4
# The indirect light of a texel, likewise: its rays are the Editor's, their sums differ in the
# last bits, and a ray that just meets or misses an edge can fall on the other side (the worst
# seen with the Editor's own materials is 3e-4, one texel in thousands).
INDIRECT_TOLERANCE = 1e-3
# A texel of the sky's faces, and a number of the directions picked by its brightness (a unit
# vector, or light of the order of the sky's summed brightness). A drawn sky's faces come from
# the graphics device, which rounds a triangle's interpolated colours its own way, and the
# ambient intensity then multiplies their gamma-encoded values: the worst seen is 1.8e-5 at
# intensity 1 and 4.8e-5 at 2.5 (`SkyDrawnPicked`).
SKY_TOLERANCE = 1e-4
PICK_TOLERANCE = 1e-4
# The part of a texel's rays that met a back face: counts over the texel's samples.
VALIDITY_TOLERANCE = 1e-6
# A texel of a material image: albedo is one of 253 values, emission a half float.
MATERIAL_TOLERANCE = 1e-6
# Two Editor bakes of one scene give a texel's light within this (see `repeats`).
REPEAT_TOLERANCE = 2e-6
# And its indirect light, a sum of many more values (up to 512 samples of several bounces
# here), within this: the worst seen between two captures is 3e-6 (`BounceMany`).
INDIRECT_REPEAT_TOLERANCE = 1e-5
# And the lightmap its whole bake writes: half floats (one step is up to 2^-10 of the value) of
# a bake of its own, run after the buffers', whose sums differ in their last bits: two Editor
# bakes give a texel within this (the worst seen is 5.2e-4, `Lamps`).
LIGHTMAP_REPEAT_TOLERANCE = 1e-3
# Of a bake input's light: what Unbound's bake takes of it, the numbers within this (relative).
LIGHT_FIELDS = ('type', 'mode', 'color', 'indirectColor', 'position', 'orientation', 'range', 'coneAngle', 'castsShadows',
                'shadowMaskChannel')
# A bake input's light without a shadowmask channel has the largest 32-bit number there.
NO_CHANNEL = 2**32 - 1
LIGHT_TOLERANCE = 2e-6


# A lighting data's numbers against the Editor's (the ambient probe's tolerance,
# tests/projects/bakeenvironment AMBIENT_TOLERANCE; the lightmaps' scales and offsets within it too).
LIGHTING_DATA_TOLERANCE = 1e-3
# The directions' and the shadowmask's 8-bit images a bake writes: two Editor bakes of a scene give
# texels a level apart (the light they are made of differs within INDIRECT_REPEAT_TOLERANCE).
IMAGE_REPEAT_TOLERANCE = 1
IMAGE_TOLERANCE = 1
# The objects of a bake's lighting data without realtime GI: LightProbes and LightingDataAsset.
LIGHTING_DATA_CLASSES = {258, 1120}


def cli():
    """The Unbound CLI the runner uses (its engine runs, and dumping a bake's lighting data)."""
    return os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')


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


def materials_digest(folder):
    """Each instance's albedo and emission images as a scene's bake input has them (rows from
    the first, four numbers a texel), in the bake's order."""
    if not (folder/'bakeInput.bin').is_file():
        return []
    bake = editor_bake.read_bake_input(folder/'bakeInput.bin')
    def image(t):
        texels = [v for texel in t['data'] for v in (texel if isinstance(texel, (tuple, list)) else (texel,))]
        return {'width': t['width'], 'height': t['height'], 'texels': texels}
    return [{'albedo': image(bake['albedo'][index]), 'emissive': image(bake['emissive'][index])} for index in bake['instanceTextures']]


def sky_digest(folder, target):
    """A scene's sky as its bake input has it: the side of its six faces, their texels (one
    colour where they all have it, else three floats a texel in `sky.f32.z`, deflated), and
    the directions picked by its brightness with their light (`skyDirections.f32`: six floats
    each) and its summed brightness."""
    if not (folder/'bakeInput.bin').is_file():
        return None
    e = editor_bake.read_bake_input(folder/'bakeInput.bin')['environment']
    flat = [v for texel in e['cube'] for v in (texel if isinstance(texel, (tuple, list)) else (texel,))]
    rgb = array('f', [flat[k] for k in range(len(flat)) if k % 4 != 3])
    out = {'side': e['cubeResolution'], 'directions': e['importanceSampleCount'], 'brightness': e['importanceIntegratedMetric']}
    if all(rgb[k] == rgb[k % 3] for k in range(len(rgb))):
        out['color'] = list(rgb[:3])
    else:
        (target/'sky.f32.z').write_bytes(zlib.compress(rgb.tobytes(), 9))
    if out['directions']:
        values = lambda name: [v for item in e[name] for v in (item if isinstance(item, (tuple, list)) else (item,))]  # noqa: E731
        directions, light = values('importanceDirections'), values('importanceWeightedIntensities')
        both = array('f', [v for k in range(out['directions']) for v in directions[4 * k:4 * k + 3] + light[4 * k:4 * k + 3]])
        (target/'skyDirections.f32').write_bytes(both.tobytes())
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


# The light probes' files a capture keeps: per probe nine coefficients of red, green, blue (and
# a fourth number), coefficient by coefficient, after their count; and per probe the part of its
# rays that met a back face.
PROBE_FILES = ('radianceDirect0.shl2', 'radianceIndirect0.shl2', 'validity0.float')
# A probe's coefficients: the direct light within DIRECT_TOLERANCE, the indirect light within
# INDIRECT_TOLERANCE, both times the value where that is above 1; its validity exactly as counts.
PROBE_VALIDITY_TOLERANCE = 1e-6


def probes_digest(folder):
    """A scene's light probes as the Editor bakes them: their points (from the probe requests'
    file) and the samples the bake input gives probes; None without probes."""
    path = folder/'probeRequests.bin'
    if not path.is_file():
        return None
    data = path.read_bytes()
    count = struct.unpack_from('<Q', data, 8)[0]
    if count == 0:
        return None
    points = [list(struct.unpack_from('<3f', data, 16 + 12 * k)) for k in range(count)]
    bake = editor_bake.read_bake_input(folder/'bakeInput.bin')
    samples = bake['settings']['probe']
    return {'count': count, 'points': points, 'samples': {'indirect': samples['indirect'], 'environment': samples['environment']}}


def read_exr(data):
    """An OpenEXR image of scanlines as the Editor writes a lightmap (half or float channels,
    without compression or with ZIP): (width, height, {channel: values, rows from the top})."""
    if data[:4] != b'\x76\x2f\x31\x01':
        raise ValueError('not an OpenEXR image')
    at, header = 8, {}
    while data[at] != 0:
        end = data.index(b'\0', at)
        name = data[at:end].decode()
        end2 = data.index(b'\0', end + 1)
        size = struct.unpack_from('<i', data, end2 + 1)[0]
        header[name] = data[end2 + 5:end2 + 5 + size]
        at = end2 + 5 + size
    at += 1
    channels, raw, k = [], header['channels'], 0
    while raw[k] != 0:
        end = raw.index(b'\0', k)
        channels.append((raw[k:end].decode(), struct.unpack_from('<i', raw, end + 1)[0]))
        k = end + 17
    x0, y0, x1, y1 = struct.unpack('<4i', header['dataWindow'])
    width, height = x1 - x0 + 1, y1 - y0 + 1
    lines = {0: 1, 2: 1, 3: 16}[header['compression'][0]]
    sizes = {1: 2, 2: 4}
    formats = {1: 'e', 2: 'f'}
    offsets = struct.unpack_from(f'<{(height + lines - 1) // lines}Q', data, at)
    out = {name: [0.0] * (width * height) for name, _ in channels}
    for offset in offsets:
        y, size = struct.unpack_from('<ii', data, offset)
        rows = min(lines, y1 + 1 - y)
        block = data[offset + 8:offset + 8 + size]
        if size < rows * width * sum(sizes[kind] for _, kind in channels):
            # ZIP: deflated bytes whose differences were taken and whose halves were interleaved.
            packed = zlib.decompress(block)
            undone = bytearray(len(packed))
            value = 0
            for i, b in enumerate(packed):
                value = b if i == 0 else (value + b - 128) & 0xFF
                undone[i] = value
            half = (len(undone) + 1) // 2
            block = bytearray(len(undone))
            block[0::2] = undone[:half]
            block[1::2] = undone[half:]
        k = 0
        for row in range(rows):
            base = (y - y0 + row) * width
            for name, kind in channels:
                count = width * sizes[kind]
                out[name][base:base + width] = struct.unpack(f'<{width}{formats[kind]}', bytes(block[k:k + count]))
                k += count
    return width, height, out


def probe_requests(data):
    """The probe requests of a probe requests file (`probeRequests.bin`): per request its number,
    the place of its first probe among the points and its number of probes. Each request names
    its output folder (`.../probeRequest<N>`, a string after its length) after these fields: the
    first probe's place 28 bytes before the string and the count 20 bytes before it."""
    requests = []
    for match in re.finditer(rb'probeRequest(\d+)', data):
        end = match.end()
        for start in range(match.start(), max(match.start() - 4096, 28), -1):
            if struct.unpack_from('<Q', data, start - 8)[0] == end - start:
                requests.append((int(match.group(1)), struct.unpack_from('<Q', data, start - 28)[0],
                                 struct.unpack_from('<Q', data, start - 20)[0]))
                break
        else:
            raise ValueError(f'a probe request naming {match.group(0)!r} without its length')
    return requests


def joined_probes(folder, name, count):
    """A light probe file of all a scene's probes, in the order of their points: the Editor bakes
    them in requests of consecutive probes (one per Light Probe Group), each with its own file
    (`radianceDirect<request>.shl2` in `probeRequest<request>`), which this joins by the
    requests' places among the points, each checked to follow the one before and to hold the
    probes its file has."""
    per_probe = []
    shl2 = name.endswith('.shl2')
    stem, suffix = name.split('0.', 1)
    requests = sorted(probe_requests((folder/'probeRequests.bin').read_bytes()), key=lambda r: r[1])
    for request, first, size in requests:
        if first != len(per_probe):
            raise ValueError(f'{folder}: probe request {request} starts at probe {first}, after {len(per_probe)}')
        data = (folder/f'probeRequest{request}'/f'{stem}{request}.{suffix}').read_bytes()
        n = struct.unpack_from('<Q', data, 0)[0]
        if n != size:
            raise ValueError(f'{folder}: probe request {request} has {n} probes in {name}, expected {size}')
        if shl2:
            values = struct.unpack_from(f'<{9 * n * 4}f', data, 8)
            per_probe += [[values[4 * (c * n + p) + k] for c in range(9) for k in range(4)] for p in range(n)]
        else:
            per_probe += [[v] for v in struct.unpack_from(f'<{n}f', data, 8)]
    if len(per_probe) != count:
        raise ValueError(f'{folder}: {len(per_probe)} probes baked in {len(requests)} requests, expected {count}')
    if shl2:
        values = [per_probe[p][4 * c + k] for c in range(9) for p in range(count) for k in range(4)]
    else:
        values = [v for probe in per_probe for v in probe]
    return struct.pack('<Q', count) + struct.pack(f'<{len(values)}f', *values)


def probe_coefficients(data):
    """A probe file's coefficients as per probe 27 numbers (nine coefficients of three)."""
    count = struct.unpack_from('<Q', data, 0)[0]
    values = struct.unpack_from(f'<{9 * count * 4}f', data, 8)
    return [values[4 * (c * count + p) + k] for p in range(count) for c in range(9) for k in range(3)]


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
    missing = []
    for scene in scenes():
        target = out/'reference'/scene
        target.mkdir(parents=True)
        scene_digest = digest(out/'raw'/scene, log.get(scene, 'not baked'))
        (target/'scene.json').write_text(json.dumps(scene_digest, indent=1) + '\n')
        (target/'lights.json').write_text(json.dumps(lights_digest(out/'raw'/scene), indent=1) + '\n')
        (target/'materials.json').write_text(json.dumps(materials_digest(out/'raw'/scene), separators=(',', ':')) + '\n')
        (target/'sky.json').write_text(json.dumps(sky_digest(out/'raw'/scene, target), indent=1) + '\n')
        probes = probes_digest(out/'raw'/scene)
        (target/'probes.json').write_text(json.dumps(probes, indent=1) + '\n')
        if probes is not None:
            for name in PROBE_FILES:
                (target/name).write_bytes(joined_probes(out/'raw'/scene, name, probes['count']))
        for index in range(len(scene_digest['lightmaps'])):
            for name in BUFFERS:
                shutil.copy2(out/'raw'/scene/'lightmapRequest0'/name.format(index), target/name.format(index))
            # The lightmap the whole bake made of them (none where the bake keeps no light).
            made = out/'raw'/scene/f'Lightmap-{index}_comp_light.exr'
            if made.is_file():
                shutil.copy2(made, target/f'lightmap{index}.exr')
        # The lighting data the whole bake wrote beside the scene, as its fields (its bytes change
        # from bake to bake: tests/baked_files.py), and the .meta files of what it wrote. Not
        # kept: the lighting data of a bake with realtime GI, which holds the realtime GI's own
        # objects (named and filled anew by every bake).
        written = out/'raw'/scene/'written'
        fields = lighting_data_fields(written/'LightingData.asset', written, cli()) \
            if (written/'LightingData.asset').is_file() else None
        if fields is not None and {o['classId'] for o in fields} <= LIGHTING_DATA_CLASSES:
            (target/'lightingData.json').write_text(json.dumps(fields, indent=1, sort_keys=True) + '\n')
            (target/'written').mkdir()
            for meta in sorted(written.glob('*.meta')):
                (target/'written'/meta.name).write_bytes(normalized(meta.name, meta.read_bytes(), written))
            # The directions' and the shadowmask's images (by their texels: their bytes are
            # those of the texels).
            for image in sorted(written.glob('*.png')):
                shutil.copy2(image, target/'written'/image.name)
        # A whole bake that failed (wholeBake.txt: False, or no answer) makes the capture not one
        # to publish.
        whole = out/'raw'/scene/'wholeBake.txt'
        if not whole.is_file() or whole.read_text().strip() != 'True':
            missing.append(scene)
    if missing:
        print(f'the whole bake failed for {missing} (see the editor log)')
        return 1
    return 0


def tree(root):
    return {str(p.relative_to(root)): p.read_bytes() for p in sorted(root.rglob('*')) if p.is_file()}


def repeats(name, first, other):
    """Whether two captures' file is the same result: byte for byte, but for a buffer of light
    or of where it comes from, which the Editor sums in an order that varies from bake to bake
    (the same used texels, and values within REPEAT_TOLERANCE, or INDIRECT_REPEAT_TOLERANCE for
    the indirect light, times the value where that is above 1). A light probe file is compared
    by its coefficients the same way."""
    if first == other:
        return True
    if first and other and name.endswith('.png'):
        return image_difference(first, other, IMAGE_REPEAT_TOLERANCE) is None
    if first and other and Path(name).name == 'lightingData.json':
        return lighting_data_difference(json.loads(first), json.loads(other), INDIRECT_REPEAT_TOLERANCE) is None
    if first and other and name.endswith('.exr'):
        a, b = read_exr(first), read_exr(other)
        if a[:2] != b[:2] or a[2].keys() != b[2].keys():
            return False
        return all(abs(x - y) <= LIGHTMAP_REPEAT_TOLERANCE * max(1.0, abs(x)) for c in a[2] for x, y in zip(a[2][c], b[2][c]))
    if first and other and name.endswith('.shl2'):
        a, b = probe_coefficients(first), probe_coefficients(other)
        tolerance = INDIRECT_REPEAT_TOLERANCE if Path(name).name.startswith('radianceIndirect') else REPEAT_TOLERANCE
        return len(a) == len(b) and all(abs(x - y) <= tolerance * max(1.0, abs(x)) for x, y in zip(a, b))
    if not first or not other or not Path(name).name.startswith(('irradiance', 'directionality')):
        return False
    a, b = editor_bake.parse_r2d(first), editor_bake.parse_r2d(other)
    if a[:3] != b[:3]:
        return False
    summed = Path(name).name.startswith(('irradianceIndirect', 'irradianceEnvironment', 'directionalityIndirect'))
    tolerance = INDIRECT_REPEAT_TOLERANCE if summed else REPEAT_TOLERANCE
    # (Where the light comes from has no flag of use: all four values are numbers.)
    flagged = Path(name).name.startswith('irradiance')
    return all(x == y if flagged and k % 4 == 3 else abs(x - y) <= tolerance * max(1.0, abs(x)) for k, (x, y) in enumerate(zip(a[3], b[3])))


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
            for part, name in (('indirect', 'irradianceIndirect'), ('environment', 'irradianceEnvironment'), ('validity', 'validity'),
                               ('towardDirect', 'directionalityDirect'), ('towardIndirect', 'directionalityIndirect'),
                               ('shadowmask', 'shadowmask')):
                width, height, channels, values = editor_bake.read_r2d(folder/f'{name}{index}.r2d')
                if (width, height, channels) != (lightmap['width'], lightmap['height'], 4):
                    raise ValueError(f'{folder}: {name}{index} of {width}x{height}')
                lightmap[part] = values
            # The first of a texel's four validity numbers is the part of its rays.
            lightmap['validity'] = lightmap['validity'][0::4]
            # The lightmap the whole bake made: red, green, blue and alpha, rows from the first
            # (the image's rows run from its top).
            lightmap['lightmap'] = None
            if (folder/f'lightmap{index}.exr').is_file():
                width, height, image = read_exr((folder/f'lightmap{index}.exr').read_bytes())
                if (width, height) != (lightmap['width'], lightmap['height']):
                    raise ValueError(f'{folder}: lightmap{index} of {width}x{height}')
                lightmap['lightmap'] = [image[c][(height - 1 - k // width) * width + k % width]
                                        for k in range(width * height) for c in 'RGBA']
        entry['baked'] = 'Success' in entry['bake']
        entry['lights'] = json.loads((folder/'lights.json').read_text())
        entry['materials'] = json.loads((folder/'materials.json').read_text())
        entry['sky'] = json.loads((folder/'sky.json').read_text())
        entry['probes'] = json.loads((folder/'probes.json').read_text())
        if entry['probes'] is not None:
            entry['probes']['direct'] = probe_coefficients((folder/PROBE_FILES[0]).read_bytes())
            entry['probes']['indirect'] = probe_coefficients((folder/PROBE_FILES[1]).read_bytes())
            validity = (folder/PROBE_FILES[2]).read_bytes()
            entry['probes']['validity'] = list(struct.unpack_from(f"<{entry['probes']['count']}f", validity, 8))
        if entry['sky'] is not None:
            entry['sky']['faces'] = numbers_of(zlib.decompress((folder/'sky.f32.z').read_bytes())) if (folder/'sky.f32.z').is_file() else None
            entry['sky']['picked'] = numbers(folder/'skyDirections.f32', 'f') if (folder/'skyDirections.f32').is_file() else None
        out[scene] = entry
    return out


def guid_of(project, scene):
    meta = (project/'Assets/Scenes'/(scene + '.unity.meta')).read_text()
    return re.search(r'(?m)^guid: ([0-9a-f]{32})$', meta).group(1)


def numbers_of(data):
    values = array('f')
    values.frombytes(data)
    return values


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
        entry = {'bake': log.get(scene, 'not baked'), 'messages': [], 'instances': [], 'lightmaps': [], 'lights': {'mixedMode': None, 'lights': []},
                 'materials': [], 'sky': None, 'probes': None}
        entry['baked'] = entry['bake'] == 'bake True'
        if (out/'raw'/scene/'messages.txt').is_file():
            # Unbound's own notes of what its bake does not make yet are not the Editor's messages.
            entry['messages'] = [line for line in (out/'raw'/scene/'messages.txt').read_text().splitlines() if UNBOUND_NOTE not in line]
        layout = project/'LibraryUnbound/BakedGI'/guid/'atlas.json'
        dump = out/'buffers'/guid
        if layout.is_file() and (dump/'buffers.json').is_file():
            entry['instances'] = [r['path'] for r in json.loads(layout.read_text())['renderers']]
            description = json.loads((dump/'buffers.json').read_text())
            entry['lights'] = {'mixedMode': description['mixedMode'], 'lights': description['lights']}
            flat = lambda image: {'width': image['width'], 'height': image['height'], 'texels': [v for texel in image['texels'] for v in texel]}  # noqa: E731
            entry['materials'] = [{'albedo': flat(m['albedo']), 'emissive': flat(m['emissive'])}
                                  for m in (description.get('indirect') or {}).get('materials', [])]
            sky = (description.get('indirect') or {}).get('sky')
            if sky is not None:
                sky['faces'] = numbers(dump/'sky.f32', 'f')
                sky['picked'] = numbers(dump/'skyDirections.f32', 'f') if (dump/'skyDirections.f32').is_file() else None
            entry['sky'] = sky
            probes = description.get('probes')
            if probes is not None:
                for part in ('direct', 'indirect', 'validity'):
                    name = dump/f'probes{part.capitalize()}.f32'
                    probes[part] = numbers(name, 'f') if name.is_file() else None
            entry['probes'] = probes
            for index, lightmap in enumerate(description['lightmaps']):
                lightmap['occupancy'] = [int(v > 0) for v in (dump/f'occupancy{index}.u8').read_bytes()]
                lightmap['charts'] = list(numbers(dump/f'chartIndex{index}.i32', 'i'))
                lightmap['normals'] = numbers(dump/f'normal{index}.f32', 'f')
                lightmap['direct'] = numbers(dump/f'irradianceDirect{index}.f32', 'f')
                for part, name in (('indirect', 'irradianceIndirect'), ('environment', 'irradianceEnvironment'), ('validity', 'validity'),
                                   ('towardDirect', 'directionalityDirect'), ('towardIndirect', 'directionalityIndirect'),
                                   ('shadowmask', 'shadowmask'), ('lightmap', 'lightmap')):
                    lightmap[part] = numbers(dump/f'{name}{index}.f32', 'f') if (dump/f'{name}{index}.f32').is_file() else None
                entry['lightmaps'].append(lightmap)
        result[scene] = entry
    return result


def engine(label):
    out = WORK/label
    if out.exists():
        sys.exit(f'refusing to overwrite {out}')
    project = project_copy(PROBE, out/'project')
    (out/'raw').mkdir()
    cli_path = cli()
    env = dict(os.environ, BAKED_BUFFERS_OUT=str(out/'raw'), BAKED_BUFFERS_AUTO='1', UNBOUND_BAKED_GI_BUFFERS=str(out/'buffers'))
    # With a graphics device: the bake draws its renderers' material images.
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '1200', cli_path, 'test', '-p', str(project),
               '--platform', 'EditMode', '--graphics']
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
            a, b = mine.get(field), theirs[field]
            if field == 'shadowMaskChannel' and b == NO_CHANNEL:
                b = -1
            pairs = list(zip(a, b)) if isinstance(b, list) else [(a, b)]
            if isinstance(b, list) and len(a) != len(b) or any(
                    abs(x - y) > LIGHT_TOLERANCE * max(1.0, abs(y)) if isinstance(y, float) else x != y for x, y in pairs):
                problems.append(f"{theirs['name']}.{field}: expected {b} actual {a}")
    return '; '.join(problems) or None


def direct_difference(got, want, tolerance=DIRECT_TOLERANCE):
    """Used texels exactly (the fourth value), their light within the tolerance."""
    if got is None:
        return 'not baked'
    if len(got) != len(want):
        return f'{len(got) // 4} texels, expected {len(want) // 4}'
    used = off = 0
    worst = 0.0
    for k in range(0, len(want), 4):
        used += got[k + 3] != want[k + 3]
        for c in range(3):
            error = abs(got[k + c] - want[k + c]) / max(1.0, abs(want[k + c]))
            worst = max(worst, error)
            off += not error <= tolerance
    if used or off:
        return f'{used} texels differ in being used, {off} values in their light (worst {worst:.3g}, tolerance {tolerance:g})'
    return None


def toward_difference(got, want, tolerance):
    """Four values a texel (where its light comes from, its shadowmask) within the tolerance."""
    if got is None:
        return 'not baked'
    if len(got) != len(want):
        return f'{len(got) // 4} texels, expected {len(want) // 4}'
    errors = [abs(a - b) for a, b in zip(got, want)]
    off = sum(not e <= tolerance for e in errors)
    if off:
        texels = len({k // 4 for k, e in enumerate(errors) if not e <= tolerance})
        return f'{texels} texels, {off} values differ (worst {max(errors):.3g}, tolerance {tolerance:g})'
    return None


def probes_difference(got, want):
    """The light probes: their points and samples exactly, their light within the light's
    tolerances, their validity as counts."""
    if want is None:
        return None if got is None else f'{got["count"]} probes, expected none'
    if got is None:
        return 'no probes baked'
    problems = []
    if got['count'] != want['count'] or any(abs(a - b) > 1e-6 for p, q in zip(got['points'], want['points']) for a, b in zip(p, q)):
        problems.append(f"points {got['points']} expected {want['points']}")
    if got.get('samples') != want['samples']:
        problems.append(f"samples {got.get('samples')} expected {want['samples']}")
    for part, tolerance in (('direct', DIRECT_TOLERANCE), ('indirect', INDIRECT_TOLERANCE), ('validity', PROBE_VALIDITY_TOLERANCE)):
        mine, theirs = got.get(part), want[part]
        if mine is None or len(mine) != len(theirs):
            problems.append(f'{part}: not baked' if mine is None else f'{part}: {len(mine)} values, expected {len(theirs)}')
            continue
        errors = [abs(a - b) / max(1.0, abs(b)) for a, b in zip(mine, theirs)]
        off = sum(not e <= tolerance for e in errors)
        if off:
            problems.append(f'{part}: {off} values differ (worst {max(errors):.3g}, tolerance {tolerance:g})')
    return '; '.join(problems) or None


# The Backface Tolerance of every Lightmap Parameters here (Default-Medium's and the probe's
# own assets'), and the part of a texel's rays above which its light does not stand, in 32-bit
# floats as the bake has it.
BACKFACE_TOLERANCE = 0.9
BACKFACE_LIMIT = struct.unpack('<f', struct.pack('<f', 1.0 - struct.unpack('<f', struct.pack('<f', BACKFACE_TOLERANCE))[0]))[0]
# A lightmap's light against the Editor's: the sum of three buffers each within its own
# tolerance (the direct light's, the indirect and sky's light's), and 2^-10 of the value (the
# lightmap was taken for half floats; the Editor's file holds 32-bit floats).
LIGHTMAP_TOLERANCE = DIRECT_TOLERANCE + 2 * INDIRECT_TOLERANCE + 2 ** -10
# How far from a chart's edge (another chart or an unused texel) the stitching of seams reaches.
SEAM_REACH = 2


def lighting_padding(scene):
    """The padding of a scene's Lighting Settings (its .lighting file); and every Lightmap
    Parameters asset of the probe has BACKFACE_TOLERANCE (else the rules cannot be applied)."""
    for parameters in sorted((PROBE/'Assets/Scenes').glob('*.giparams')):
        tolerance = re.search(r'backFaceTolerance: ([0-9.]+)', parameters.read_text())
        if tolerance and float(tolerance.group(1)) != BACKFACE_TOLERANCE:
            raise ValueError(f'{parameters.name}: Backface Tolerance {tolerance.group(1)}; the lightmap rules here take {BACKFACE_TOLERANCE}')
    found = re.search(r'm_Padding: (\d+)', (PROBE/'Assets/Scenes'/f'{scene}.lighting').read_text())
    if not found:
        raise ValueError(f'{scene}.lighting: no padding')
    return int(found.group(1))


def composite(lightmap, padding):
    """The lightmap the bake makes of a capture's buffers by the measured rules (red, green, blue,
    alpha per texel): each used texel's direct, indirect and sky's light summed; a texel more of
    whose rays met back faces than the Backface Tolerance allows takes the mean of the light of its
    eight neighbours that hold their own, ring by ring; the texels around the charts likewise, as
    many rings as the padding (alpha 1). It leaves out what the Editor does next: the stitching of
    seams (which the runner uses to find the texels stitching changed)."""
    width, height = lightmap['width'], lightmap['height']
    used = lightmap['occupancy']
    out = [0.0] * (4 * width * height)
    holds = [False] * (width * height)
    for k in range(width * height):
        if used[k]:
            for c in range(3):
                out[4 * k + c] = sum(lightmap[part][4 * k + c] for part in ('direct', 'indirect', 'environment'))
            out[4 * k + 3] = 1.0
            holds[k] = not lightmap['validity'][k] > BACKFACE_LIMIT

    def spread(waiting, rings):
        for _ in range(rings):
            taken = []
            for k in waiting:
                x, y = k % width, k // width
                near = [(y + dy) * width + x + dx for dx in (-1, 0, 1) for dy in (-1, 0, 1)
                        if (dx or dy) and 0 <= x + dx < width and 0 <= y + dy < height and holds[(y + dy) * width + x + dx]]
                if near:
                    taken.append((k, [sum(out[4 * n + c] for n in near) / len(near) for c in range(3)]))
            if not taken:
                return
            done = {k for k, _ in taken}
            waiting = [k for k in waiting if k not in done]
            for k, light in taken:
                out[4 * k:4 * k + 4] = light + [1.0]
                holds[k] = True

    spread([k for k in range(width * height) if used[k] and not holds[k]], width * height)
    spread([k for k in range(width * height) if not used[k]], padding)
    return out


def lightmap_difference(got, want, padding):
    """The lightmap the bake makes (red, green, blue, alpha per texel) against the Editor's: which
    texels hold light (alpha) exactly, and the light of those within LIGHTMAP_TOLERANCE (times the
    value above 1), the stitched texels too. The texels whose light in the Editor's lightmap is not
    what the rules make of its own buffers (`composite`) are those the stitching of seams changed:
    counted, and they must lie within SEAM_REACH texels of a chart's edge. (None or the problem,
    the count.)"""
    if got is None:
        return 'not baked', 0
    width, height = want['width'], want['height']
    if len(got) != len(want['lightmap']):
        return f'{len(got) // 4} texels, expected {width * height}', 0
    theirs, rules = want['lightmap'], composite(want, padding)
    charts, used = want['charts'], want['occupancy']

    def owner(x, y):
        # (Beyond the image: an edge, as a chart may end there.)
        if not (0 <= x < width and 0 <= y < height):
            return 'outside'
        return charts[y * width + x] if used[y * width + x] else None

    def by_edge(k):
        x, y = k % width, k // width
        own = owner(x, y)
        return any(owner(x + dx, y + dy) != own
                   for dx in range(-SEAM_REACH, SEAM_REACH + 1) for dy in range(-SEAM_REACH, SEAM_REACH + 1))

    def error(a, b):
        return max(abs(x - y) / max(1.0, abs(y)) for x, y in zip(a, b))

    alpha_off, off, far, stitched, worst = [], [], [], 0, 0.0
    for k in range(width * height):
        mine, editor = got[4 * k:4 * k + 4], theirs[4 * k:4 * k + 4]
        if mine[3] != editor[3]:
            alpha_off.append(k)
            continue
        if editor[3] == 0:
            continue
        if error(rules[4 * k:4 * k + 3], editor[:3]) > LIGHTMAP_TOLERANCE:
            stitched += 1
            if not by_edge(k):
                far.append(k)
        e = error(mine[:3], editor[:3])
        if e > LIGHTMAP_TOLERANCE:
            off.append(k)
            worst = max(worst, e)
    problems = []
    if alpha_off:
        problems.append(f'{len(alpha_off)} texels differ in holding light, first at {alpha_off[:6]}')
    if off:
        problems.append(f'{len(off)} texels differ in their light (worst {worst:.3g}, tolerance {LIGHTMAP_TOLERANCE:.3g}), first at {off[:6]}')
    if far:
        problems.append(f'{len(far)} texels away from chart edges are not what the rules make of the buffers, first at {far[:6]}')
    return '; '.join(problems) or None, stitched


def validity_difference(got, want):
    if got is None:
        return 'not baked'
    if len(got) != len(want):
        return f'{len(got)} texels, expected {len(want)}'
    off = [k for k in range(len(want)) if not abs(got[k] - want[k]) <= VALIDITY_TOLERANCE]
    return f'{len(off)} of {len(want)} texels differ, first at {off[:6]}' if off else None


def material_difference(got, want):
    """Each instance's albedo and emission images: their sizes, and their texels within the tolerance."""
    if len(got) != len(want):
        return f'{len(got)} instances with material images, expected {len(want)}'
    problems = []
    for index, (mine, theirs) in enumerate(zip(got, want)):
        for part in ('albedo', 'emissive'):
            a, e = mine[part], theirs[part]
            if (a['width'], a['height']) != (e['width'], e['height']):
                problems.append(f"instance {index} {part} {a['width']}x{a['height']}, expected {e['width']}x{e['height']}")
                continue
            errors = [abs(x - y) / max(1.0, abs(y)) for x, y in zip(a['texels'], e['texels'])]
            off = sum(not error <= MATERIAL_TOLERANCE for error in errors)
            if off:
                problems.append(f"instance {index} {part} {e['width']}x{e['height']}: {off} of {len(errors)} values differ (worst {max(errors):.3g})")
    return '; '.join(problems) if problems else None


def sky_difference(got, want):
    """The sky's faces within SKY_TOLERANCE (times the value above 1), and the directions
    picked by its brightness, their light and its summed brightness within PICK_TOLERANCE."""
    if want is None:
        return None
    if got is None:
        return 'no sky'
    if got['side'] != want['side']:
        return f"faces of {got['side']}, expected {want['side']}"
    problems = []
    faces = want['faces'] if want['faces'] is not None else want['color'] * (len(got['faces']) // 3)
    if len(got['faces']) != len(faces):
        return f"{len(got['faces'])} face values, expected {len(faces)}"
    errors = [abs(x - y) / max(1.0, abs(y)) for x, y in zip(got['faces'], faces)]
    off = sum(not error <= SKY_TOLERANCE for error in errors)
    if off:
        problems.append(f'{off} of {len(errors)} face values differ (worst {max(errors):.3g}, tolerance {SKY_TOLERANCE:g})')
    if got['directions'] != want['directions']:
        problems.append(f"{got['directions']} picked directions, expected {want['directions']}")
    elif want['directions']:
        errors = [abs(x - y) / max(1.0, abs(y)) for x, y in zip(got['picked'], want['picked'])]
        off = sum(not error <= PICK_TOLERANCE for error in errors)
        if off or len(got['picked']) != len(want['picked']):
            problems.append(f'{off} of {len(errors)} values of the picked directions and their light differ (worst {max(errors):.3g})')
        if not abs(got['brightness'] - want['brightness']) <= PICK_TOLERANCE * max(1.0, abs(want['brightness'])):
            problems.append(f"summed brightness {got['brightness']!r}, expected {want['brightness']!r}")
    return '; '.join(problems) if problems else None


def residuals():
    """{record: (reason, problem)} of residuals.json next to this file (`{reason: {record:
    problem}}`): known differences, each with the exact text the comparison gives it. They are
    reported with every run; one that no longer differs, or differs otherwise, fails the run
    until its entry is corrected."""
    path = PROBE/'residuals.json'
    listed = json.loads(path.read_text()) if path.is_file() else {}
    return {key: (reason, problem) for reason, entries in listed.items() for key, problem in entries.items()}


def compare(label):
    want, got = expected(reference_root(PROBE)), actual(WORK/label)
    known = residuals()
    records = []

    stitched = [0]

    def record(key, problem):
        records.append((key, problem))
        if problem:
            print('KNOWN' if key in known and known[key][1] == problem else 'FAIL', key, problem)

    def same(key, a, b):
        record(key, None if a == b else f'expected {b!r} actual {a!r}')

    for scene in scenes():
        e, a = want[scene], got[scene]
        same(scene + '.baked', a['baked'], e['baked'])
        same(scene + '.messages', a['messages'], e['messages'])
        same(scene + '.instances', a['instances'], e['instances'])
        same(scene + '.lightingMode', a['lights']['mixedMode'], e['lights']['mixedMode'])
        record(scene + '.lights', light_difference(a['lights']['lights'], e['lights']['lights']))
        form = lambda entry: ' '.join(f"{m['width']}x{m['height']}@{m['supersampling']}" for m in entry['lightmaps'])  # noqa: E731
        same(scene + '.lightmaps', form(a), form(e))
        for index, lightmap in enumerate(e['lightmaps']):
            key = f'{scene}.lightmap{index}.'
            mine = a['lightmaps'][index] if index < len(a['lightmaps']) else None
            if mine is None or (mine['width'], mine['height'], mine['supersampling']) != (lightmap['width'], lightmap['height'], lightmap['supersampling']):
                for part in ('instances', 'occupancy', 'charts', 'normals', 'direct', 'indirect', 'environment', 'validity',
                             'towardDirect', 'towardIndirect', 'shadowmask', 'lightmap'):
                    record(key + part, 'no such lightmap of that size')
                continue
            names = lambda entry, m: [entry['instances'][i] for i in m['instances']]  # noqa: E731
            same(key + 'instances', names(a, mine), names(e, lightmap))
            record(key + 'occupancy', differing(mine['occupancy'], lightmap['occupancy']))
            record(key + 'charts', differing(mine['charts'], lightmap['charts']))
            record(key + 'normals', normal_difference(mine['normals'], lightmap['normals']))
            record(key + 'direct', direct_difference(mine['direct'], lightmap['direct']))
            record(key + 'indirect', direct_difference(mine['indirect'], lightmap['indirect'], INDIRECT_TOLERANCE))
            record(key + 'environment', direct_difference(mine['environment'], lightmap['environment'], INDIRECT_TOLERANCE))
            record(key + 'validity', validity_difference(mine['validity'], lightmap['validity']))
            record(key + 'towardDirect', toward_difference(mine['towardDirect'], lightmap['towardDirect'], DIRECTIONALITY_TOLERANCE))
            record(key + 'towardIndirect', toward_difference(mine['towardIndirect'], lightmap['towardIndirect'], INDIRECT_TOLERANCE))
            record(key + 'shadowmask', toward_difference(mine['shadowmask'], lightmap['shadowmask'], SHADOWMASK_TOLERANCE))
            if lightmap['lightmap'] is not None:
                problem, seams = lightmap_difference(mine['lightmap'], lightmap, lighting_padding(scene))
                record(key + 'lightmap', problem)
                stitched[0] += seams
        record(scene + '.materials', material_difference(a['materials'], e['materials']))
        record(scene + '.sky', sky_difference(a['sky'], e['sky']))
        record(scene + '.probes', probes_difference(a['probes'], e['probes']))
        # The lighting data the bake wrote beside the scene (its fields), and the .meta files of
        # what it wrote, where the reference keeps them.
        reference_scene, mine = reference_root(PROBE)/scene, WORK/label/'project/Assets/Scenes'/scene
        if (reference_scene/'lightingData.json').is_file():
            want_fields = json.loads((reference_scene/'lightingData.json').read_text())
            record(scene + '.lightingData', 'not written' if not (mine/'LightingData.asset').is_file() else lighting_data_difference(
                lighting_data_fields(mine/'LightingData.asset', mine, cli()), want_fields, LIGHTING_DATA_TOLERANCE))
        elif (mine/'LightingData.asset').is_file():
            record(scene + '.lightingData', 'written where the reference keeps none')
        # Files Unbound wrote that the Editor's bake did not (by their .meta files).
        kept = {m.name for m in (reference_scene/'written').glob('*.meta')} if (reference_scene/'written').is_dir() else set()
        extra = sorted(m.name for m in mine.glob('*.meta') if m.name not in kept) if mine.is_dir() else []
        if 'folder.meta' not in kept and mine.with_suffix('.meta').is_file():
            extra.append('folder.meta')
        if extra:
            record(scene + '.written', f'written where the Editor writes none: {extra}')
        for meta in sorted((reference_scene/'written').glob('*.meta')) if (reference_scene/'written').is_dir() else []:
            got_meta = mine.with_suffix('.meta') if meta.name == 'folder.meta' else mine/meta.name
            record(scene + '.written/' + meta.name, 'not written' if not got_meta.is_file()
                   else None if normalized(meta.name, got_meta.read_bytes(), mine) == meta.read_bytes()
                   else 'differs: ' + ' | '.join(l for l in difflib.unified_diff(
                       meta.read_text().splitlines(), normalized(meta.name, got_meta.read_bytes(), mine).decode().splitlines(), lineterm='', n=0)
                       if l[:1] in '+-' and l[:3] not in ('+++', '---'))[:300])
        for image in sorted((reference_scene/'written').glob('*.png')) if (reference_scene/'written').is_dir() else []:
            record(scene + '.written/' + image.name, 'not written' if not (mine/image.name).is_file()
                   else image_difference((mine/image.name).read_bytes(), image.read_bytes(), IMAGE_TOLERANCE))
    print(f'lightmaps: {stitched[0]} texels the Editor stitched along seams')
    keys = {key for key, _ in records}
    for key in sorted(set(known) - keys):
        print('FAIL', key, 'a residual of no record')
    changed = [key for key, problem in records if key in known and problem != known[key][1]]
    for key in changed:
        print('FAIL', key, f'the residual is listed as: {known[key][1]} (correct or remove it in residuals.json)')
    residual = sum(key in known and problem == known[key][1] for key, problem in records)
    failed = sum(problem is not None and key not in known for key, problem in records) + len(changed) + len(set(known) - keys)
    print(f'bakedbuffers: {len(records) - failed - residual}/{len(records)} records match; {failed} differ, {residual} known residuals')
    return int(failed != 0)


def forward_of(orientation):
    """A rotation's forward axis."""
    x, y, z, w = orientation
    return 2 * (x * z + w * y), 2 * (y * z - w * x), 1 - 2 * (x * x + y * y)


def replay(label):
    """The capture's bake inputs through Unbound's rasterizer and direct light alone."""
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
        if any(light['type'] > 2 for light in bake['lights']):
            print('skipped', scene, '(a light that is not directional, point or spot)')
            continue
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
                source = bake['instances'][instance['object'][0]]
                data += struct.pack('<I4fi', source['mesh'], *instance['scaleOffset'], instance['transformIndex'])
                data += struct.pack('<16fI', *source['transform'], int(source['receiveShadows']))
            # The scene the texels are lit from: every instance that casts shadows, the lights,
            # the Lighting Mode and the lightmap's pushoff.
            casters = [i for i in bake['instances'] if i['castShadows']]
            data += struct.pack('<I', len(casters))
            for caster in casters:
                data += struct.pack('<I16f', caster['mesh'], *caster['transform'])
                # A submesh without a material stops no ray.
                data += struct.pack(f"<I{len(caster['materials'])}I", len(caster['materials']), *(int(m >= 0) for m in caster['materials']))
            data += struct.pack('<I', len(bake['lights']))
            for light in bake['lights']:
                data += struct.pack('<II3f3f3fffI', light['type'], light['mode'], *light['color'], *light['position'],
                                    *forward_of(light['orientation']), light['range'], light['coneAngle'], int(light['castsShadows']))
            data += struct.pack('<If', bake['settings']['mixedMode'], atlas['pushoff'])
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
                'direct': direct_difference(numbers(work/'irradianceDirect.f32', 'f'),
                                            editor_bake.read_r2d(folder/'lightmapRequest0'/f'irradianceDirect{index}.r2d')[3]),
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
