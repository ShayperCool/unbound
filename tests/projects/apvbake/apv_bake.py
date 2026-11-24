#!/usr/bin/env python3
"""The bake of an Adaptive Probe Volume (Q724): what Lightmapping.Bake writes and in which order
for a URP scene with a global probe volume.

    apv_bake.py create            # makes Assets/Scenes (the Editor, batch mode)
    apv_bake.py native --label L  # an Editor capture: bakes the scene, keeps what it wrote
    apv_bake.py publish L1 L2     # two agreeing captures -> Reference/unity-<version>/
    apv_bake.py engine --label L  # Unbound (unbound test, Edit mode)
    apv_bake.py compare --label L # an engine run's APV data against the reference
    apv_bake.py check             # engine run + compare

`Assets/Editor/ApvRecord.cs` bakes the scene and keeps every file the bake wrote under Assets/
(`written/<path>`) and the bake's events in their order (`log.txt`). Publishing decodes the
Baking Set asset and all five `.bytes` cell data streams into `Reference/unity-<version>/`
(`log.txt`, `baking_set.json`, `probes.jsonl`) after verifying two independent captures agree."""
import argparse
import json
import math
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
sys.dont_write_bytecode = True  # nothing is written into the checkout
import baked_files  # noqa: E402
import editor_runner  # noqa: E402
from probe_paths import private_label, project_copy, reference_root, run_root  # noqa: E402
sys.path.insert(0, str(REPO/'tests/projects/bakedbuffers'))
from bake_buffers import read_exr  # noqa: E402

WORK = run_root(PROBE, 'apvbake')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'

CHUNK_BRICKS = 128
BRICK_DIM = 4
BRICK_PROBES = BRICK_DIM ** 3
CHUNK_PROBES = CHUNK_BRICKS * BRICK_PROBES
LOC_SIZE_X = CHUNK_BRICKS * BRICK_DIM
LOC_SIZE_Y = BRICK_DIM

POS_TOLERANCE = 1e-4
VALIDITY_TOLERANCE = 0.05
SH_TOLERANCE = 0.05
L1L2_U8_TOLERANCE = 8
AMBIENT_SH_TOLERANCE = 1e-3
LIGHTMAP_TOLERANCE = 0.02


def within(expected, actual, tol):
    return math.isfinite(actual) and abs(expected - actual) <= tol


def cli():
    return os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')


def editor(out, method, env, label, *, vulkan=False):
    job = editor_runner.EditorJob(out/'project', out/'editor.log', out, ['-executeMethod', method],
                                  batch=True, graphics=True, vulkan=vulkan, env=env, timeout=1800, label='apvbake-' + label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    return int(result.get('exit_code') != 0 or bool(result.get('forced_exit')))


def parse_baking_set(text):
    """Extract cell descriptors and streamable descriptors from Apv Baking Set.asset YAML."""
    cells = []
    m_descs = re.search(r'  cellDescs:\n(.*?)\n  m_SerializedPerSceneCellList:', text, re.S)
    if m_descs:
        for block in re.split(r'\n    - position: ', '\n' + m_descs.group(1))[1:]:
            pm = re.match(r'\{x: (-?\d+), y: (-?\d+), z: (-?\d+)\}', block)
            idx = int(re.search(r'index: (\d+)', block).group(1))
            pc = int(re.search(r'probeCount: (\d+)', block).group(1))
            ms = int(re.search(r'minSubdiv: (\d+)', block).group(1))
            ic = int(re.search(r'indexChunkCount: (\d+)', block).group(1))
            sc = int(re.search(r'shChunkCount: (\d+)', block).group(1))
            bc = int(re.search(r'bricksCount: (\d+)', block).group(1))
            cells.append({
                'index': idx,
                'position': [int(pm.group(1)), int(pm.group(2)), int(pm.group(3))],
                'probeCount': pc,
                'minSubdiv': ms,
                'indexChunkCount': ic,
                'shChunkCount': sc,
                'bricksCount': bc,
            })

    def int_field(name):
        m = re.search(rf'  {name}: (-?\d+)', text)
        return int(m.group(1)) if m else None

    def vec3_field(name):
        m = re.search(rf'  {name}: \{{x: (-?\d+), y: (-?\d+), z: (-?\d+)\}}', text)
        return [int(m.group(1)), int(m.group(2)), int(m.group(3))] if m else None

    return {
        'chunkSizeInBricks': int_field('chunkSizeInBricks'),
        'minCellPosition': vec3_field('minCellPosition'),
        'maxCellPosition': vec3_field('maxCellPosition'),
        'L0ChunkSize': int_field('L0ChunkSize'),
        'L1ChunkSize': int_field('L1ChunkSize'),
        'L2TextureChunkSize': int_field('L2TextureChunkSize'),
        'sharedDataChunkSize': int_field('sharedDataChunkSize'),
        'supportDataChunkSize': int_field('supportDataChunkSize'),
        'cells': cells,
    }


def probe_gpu_index(brick_in_chunk, x, y, z):
    ix = brick_in_chunk * BRICK_DIM + x
    return ix + LOC_SIZE_X * (y + LOC_SIZE_Y * z)


def decode_capture(raw_dir):
    scenes = raw_dir/'written/Assets/Scenes'
    asset_path = scenes/'Apv Baking Set.asset'
    if not asset_path.is_file():
        raise FileNotFoundError(f'missing {asset_path}')
    meta = parse_baking_set(asset_path.read_text())

    bricks_b = (scenes/'Apv Baking Set.CellBricksData.bytes').read_bytes()
    shared_b = (scenes/'Apv Baking Set.CellSharedData.bytes').read_bytes()
    data_b = (scenes/'Apv Baking Set-Default.CellData.bytes').read_bytes()
    opt_b = (scenes/'Apv Baking Set-Default.CellOptionalData.bytes').read_bytes()
    sup_b = (scenes/'Apv Baking Set.CellSupportData.bytes').read_bytes()

    l0_chunk = 2 * 4 * CHUNK_PROBES
    l1_chunk = 4 * CHUNK_PROBES
    l0l1_chunk = l0_chunk + 2 * l1_chunk
    l2_tex_chunk = 4 * CHUNK_PROBES
    l2_chunk = 4 * l2_tex_chunk
    shared_chunk = CHUNK_PROBES
    pos_chunk = 12 * CHUNK_PROBES
    val_chunk = 4 * CHUNK_PROBES
    sup_chunk = pos_chunk + 2 * val_chunk

    brick_cursor = 0
    chunk_cursor = 0
    probes = []
    for cell in meta['cells']:
        bc = cell['bricksCount']
        sc = cell['shChunkCount']
        cell_bricks = []
        for bi in range(bc):
            bx, by, bz, subdiv = struct.unpack_from('<iiii', bricks_b, (brick_cursor + bi) * 16)
            cell_bricks.append([bx, by, bz, subdiv])
        cell['bricks'] = cell_bricks
        brick_cursor += bc

        l0l1_base = chunk_cursor * l0l1_chunk
        l1g_base = l0l1_base + sc * l0_chunk
        l1b_base = l1g_base + sc * l1_chunk
        l2_base = chunk_cursor * l2_chunk
        shared_base = chunk_cursor * shared_chunk
        sup_base = chunk_cursor * sup_chunk
        val_base = sup_base + sc * pos_chunk

        for bi in range(bc):
            ci = bi // CHUNK_BRICKS
            bi_in_chunk = bi % CHUNK_BRICKS
            for z in range(BRICK_DIM):
                for y in range(BRICK_DIM):
                    for x in range(BRICK_DIM):
                        idx = probe_gpu_index(bi_in_chunk, x, y, z)
                        px, py, pz = struct.unpack_from('<fff', sup_b, sup_base + ci * pos_chunk + idx * 12)
                        val = struct.unpack_from('<f', sup_b, val_base + ci * val_chunk + idx * 4)[0]
                        mask = struct.unpack_from('<B', shared_b, shared_base + ci * shared_chunk + idx)[0]
                        l0 = list(struct.unpack_from('<eeee', data_b, l0l1_base + ci * l0_chunk + idx * 8))
                        l1g = list(struct.unpack_from('<4B', data_b, l1g_base + ci * l1_chunk + idx * 4))
                        l1b = list(struct.unpack_from('<4B', data_b, l1b_base + ci * l1_chunk + idx * 4))
                        l2 = []
                        for t in range(4):
                            off = l2_base + (sc * t + ci) * l2_tex_chunk + idx * 4
                            l2.extend(struct.unpack_from('<4B', opt_b, off))
                        probes.append({
                            'cell': cell['index'],
                            'brick': bi,
                            'local': [x, y, z],
                            'pos': [round(px, 5), round(py, 5), round(pz, 5)],
                            'validity': round(val, 6),
                            'mask': mask,
                            'l0': [round(v, 5) for v in l0],
                            'l1g': l1g,
                            'l1b': l1b,
                            'l2': l2[:15],
                        })
        chunk_cursor += sc

    log_text = (raw_dir/'log.txt').read_text()
    return log_text, meta, probes


def create():
    """Makes the scene and its Lighting Settings; nothing that exists is replaced."""
    target = PROBE/'Assets/Scenes'
    if any(target.glob('*.unity')):
        sys.exit(f'refusing to overwrite {target}')
    out = WORK/private_label('create')
    project_copy(PROBE, out/'project', package_cache=False)
    if editor(out, 'ApvSetup.Create', {}, 'create'):
        return 1
    for path in sorted((out/'project/Assets/Scenes').iterdir()):
        if not (target/path.name).exists():
            shutil.copy2(path, target/path.name)
    return 0


def native(label):
    out = WORK/label
    if out.exists():
        sys.exit(f'refusing to overwrite {out}')
    project_copy(PROBE, out/'project', package_cache=False)
    (out/'raw').mkdir()
    code = editor(out, 'ApvRecord.Record', {'APV_OUT': str(out/'raw')}, label, vulkan=True)
    if code or not (out/'raw/log.txt').is_file():
        return code or 1
    return 0


def decode_lighting_data(raw_dir):
    folder = raw_dir/'written/Assets/Scenes/Apv'
    ld_path = folder/'LightingData.asset'
    lm_path = folder/'Lightmap-0_comp_light.exr'
    if not ld_path.is_file():
        raise FileNotFoundError(f'missing {ld_path}')
    if not lm_path.is_file():
        raise FileNotFoundError(f'missing {lm_path}')
    fields = baked_files._by_renderer(baked_files.lighting_data_fields(ld_path, folder, cli()))
    return fields, lm_path.read_bytes()


def compare_lighting_data(ref_ld, got_ld, path='lighting_data'):
    diffs = []
    if type(ref_ld) is not type(got_ld):
        diffs.append(f'  {path}: type expected={type(ref_ld).__name__} actual={type(got_ld).__name__}')
        return diffs
    if isinstance(ref_ld, dict):
        for key in sorted(set(ref_ld) | set(got_ld)):
            if key not in ref_ld or key not in got_ld:
                diffs.append(f'  {path}.{key}: expected={key in ref_ld} actual={key in got_ld}')
            else:
                diffs.extend(compare_lighting_data(ref_ld[key], got_ld[key], f'{path}.{key}'))
    elif isinstance(ref_ld, list):
        if len(ref_ld) != len(got_ld):
            diffs.append(f'  {path}: len expected={len(ref_ld)} actual={len(got_ld)}')
        else:
            for idx, (rv, gv) in enumerate(zip(ref_ld, got_ld)):
                diffs.extend(compare_lighting_data(rv, gv, f'{path}[{idx}]'))
    elif ref_ld != got_ld:
        if 'm_BakedAmbientProbeInLinear' in path:
            try:
                rf, gf = float(ref_ld), float(got_ld)
                if within(rf, gf, AMBIENT_SH_TOLERANCE):
                    return diffs
            except (TypeError, ValueError):
                pass
        diffs.append(f'  {path}: expected={ref_ld!r} actual={got_ld!r}')
    return diffs


def compare_lightmap(ref_exr, got_exr, tol=LIGHTMAP_TOLERANCE):
    rw, rh, rch = read_exr(ref_exr)
    gw, gh, gch = read_exr(got_exr)
    if (rw, rh) != (gw, gh):
        return [f'  lightmap0.exr size expected={rw}x{rh} actual={gw}x{gh}']
    if sorted(rch) != sorted(gch):
        return [f'  lightmap0.exr channels expected={sorted(rch)} actual={sorted(gch)}']
    alpha = rch.get('A')
    got_alpha = gch.get('A')
    bad = 0
    worst = 0.0
    for i in range(rw * rh):
        ra = alpha[i] if alpha is not None else 1.0
        ga = got_alpha[i] if got_alpha is not None else 1.0
        if (ra > 0.5) != (ga > 0.5):
            bad += 1
            continue
        if ra <= 0.5:
            continue
        for c in ('R', 'G', 'B'):
            if c in rch:
                d = abs(rch[c][i] - gch[c][i])
                if not math.isfinite(gch[c][i]) or d > tol:
                    bad += 1
                    worst = max(worst, d)
                    break
    if bad:
        return [f'  lightmap0.exr: {bad} occupied texels exceed tolerance {tol} (max diff {worst:.6f})']
    return []


def publish(label1, label2):
    log1, meta1, probes1 = decode_capture(WORK/label1/'raw')
    log2, meta2, probes2 = decode_capture(WORK/label2/'raw')
    ld1, lm1 = decode_lighting_data(WORK/label1/'raw')
    ld2, lm2 = decode_lighting_data(WORK/label2/'raw')
    if log1 != log2:
        sys.exit('publish: log.txt differs between the two captures')
    if meta1 != meta2:
        sys.exit('publish: baking_set.json differs between the two captures')
    if probes1 != probes2:
        sys.exit('publish: decoded probes differ between the two captures')
    if ld1 != ld2:
        sys.exit('publish: lighting_data.json differs between the two captures')
    lm_diffs = compare_lightmap(lm1, lm2, tol=1e-5)
    if lm_diffs:
        sys.exit('publish: lightmap0.exr differs between the two captures: ' + '; '.join(lm_diffs))
    ref = reference_root(PROBE)
    ref.mkdir(parents=True, exist_ok=True)
    (ref/'log.txt').write_text(log1)
    (ref/'baking_set.json').write_text(json.dumps(meta1, indent=2) + '\n')
    with (ref/'probes.jsonl').open('w') as f:
        for row in probes1:
            f.write(json.dumps(row, separators=(',', ':')) + '\n')
    (ref/'lighting_data.json').write_text(json.dumps(ld1, indent=2, sort_keys=True) + '\n')
    (ref/'lightmap0.exr').write_bytes(lm1)
    print(f'published {len(meta1["cells"])} cells, {sum(c["bricksCount"] for c in meta1["cells"])} bricks, '
          f'{len(probes1)} probes to {ref}')
    return 0


def engine(label):
    out = WORK/label
    if out.exists():
        sys.exit(f'refusing to overwrite {out}')
    project = out/'project'
    project_copy(PROBE, project, package_cache=False)
    (project/'Assets/Editor/ApvTestEntry.cs').write_text(
        'using NUnit.Framework;\n'
        'public static class ApvTestEntry { [Test] public static void Run() { ApvRecord.Record(); } }\n'
    )
    (out/'raw').mkdir()
    cmd = [cli(), 'test', '--graphics', '-p', str(project), '--filter', 'ApvTestEntry.Run']
    if HEAVY.is_file():
        cmd = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '600'] + cmd
    result = subprocess.run(cmd, env=dict(os.environ, APV_OUT=str(out/'raw')), capture_output=True, text=True)
    (out/'engine.stdout.log').write_text(result.stdout)
    (out/'engine.stderr.log').write_text(result.stderr)
    print(f'apvbake-{label}: engine exit {result.returncode}')
    return result.returncode


def compare(label):
    ref = reference_root(PROBE)
    ref_log = (ref/'log.txt').read_text().splitlines()
    ref_meta = json.loads((ref/'baking_set.json').read_text())
    ref_probes = [json.loads(line) for line in (ref/'probes.jsonl').read_text().splitlines() if line.strip()]
    raw = WORK/label/'raw'
    if not (raw/'log.txt').is_file():
        print(f'FAIL: missing {raw/"log.txt"}')
        return 1
    got_log = (raw/'log.txt').read_text().splitlines()
    failures = []
    if got_log != ref_log:
        failures.append(f'log.txt mismatch: got {len(got_log)} lines vs ref {len(ref_log)} lines')
        for idx in range(max(len(got_log), len(ref_log))):
            rl = ref_log[idx] if idx < len(ref_log) else '<missing>'
            gl = got_log[idx] if idx < len(got_log) else '<missing>'
            if rl != gl:
                failures.append(f'  log[{idx}]: expected={rl!r} actual={gl!r}')
    try:
        _, got_meta, got_probes = decode_capture(raw)
    except (FileNotFoundError, struct.error) as exc:
        failures.append(str(exc))
        got_meta, got_probes = None, []
    if got_meta is not None:
        if got_meta != ref_meta:
            failures.append('baking_set.json mismatch')
            for key in sorted(set(ref_meta.keys()) | set(got_meta.keys())):
                rv = ref_meta.get(key)
                gv = got_meta.get(key)
                if rv == gv:
                    continue
                if key == 'cells' and isinstance(rv, list) and isinstance(gv, list):
                    failures.append(f'  baking_set.cells: got {len(gv)} vs ref {len(rv)}')
                    for ci in range(max(len(rv), len(gv))):
                        rc = rv[ci] if ci < len(rv) else None
                        gc = gv[ci] if ci < len(gv) else None
                        if rc != gc:
                            failures.append(f'    cell[{ci}]: expected={rc!r} actual={gc!r}')
                else:
                    failures.append(f'  baking_set.{key}: expected={rv!r} actual={gv!r}')
        if len(got_probes) != len(ref_probes):
            failures.append(f'probe count mismatch: got {len(got_probes)} vs ref {len(ref_probes)}')
        else:
            probe_failures = []
            for idx, (rp, gp) in enumerate(zip(ref_probes, got_probes)):
                diffs = []
                if rp['cell'] != gp['cell'] or rp['brick'] != gp['brick'] or rp['local'] != gp['local']:
                    diffs.append(
                        f'id expected=({rp["cell"]},{rp["brick"]},{rp["local"]}) '
                        f'actual=({gp["cell"]},{gp["brick"]},{gp["local"]})'
                    )
                else:
                    if any(not within(a, b, POS_TOLERANCE) for a, b in zip(rp['pos'], gp['pos'])):
                        diffs.append(f'pos expected={rp["pos"]} actual={gp["pos"]} (tol={POS_TOLERANCE})')
                    if not within(rp['validity'], gp['validity'], VALIDITY_TOLERANCE):
                        diffs.append(f'validity expected={rp["validity"]} actual={gp["validity"]} (tol={VALIDITY_TOLERANCE})')
                    if rp['mask'] != gp['mask']:
                        diffs.append(f'mask expected=0x{rp["mask"]:02x} actual=0x{gp["mask"]:02x}')
                    if any(not within(a, b, SH_TOLERANCE) for a, b in zip(rp['l0'], gp['l0'])):
                        diffs.append(f'l0 expected={rp["l0"]} actual={gp["l0"]} (tol={SH_TOLERANCE})')
                    if len(rp['l1g']) != len(gp['l1g']) or any(abs(a - b) > L1L2_U8_TOLERANCE for a, b in zip(rp['l1g'], gp['l1g'])):
                        diffs.append(f'l1g expected={rp["l1g"]} actual={gp["l1g"]} (tol={L1L2_U8_TOLERANCE})')
                    if len(rp['l1b']) != len(gp['l1b']) or any(abs(a - b) > L1L2_U8_TOLERANCE for a, b in zip(rp['l1b'], gp['l1b'])):
                        diffs.append(f'l1b expected={rp["l1b"]} actual={gp["l1b"]} (tol={L1L2_U8_TOLERANCE})')
                    if len(rp['l2']) != len(gp['l2']) or any(abs(a - b) > L1L2_U8_TOLERANCE for a, b in zip(rp['l2'], gp['l2'])):
                        diffs.append(f'l2 expected={rp["l2"]} actual={gp["l2"]} (tol={L1L2_U8_TOLERANCE})')
                if diffs:
                    probe_failures.append(
                        f'  probe[{idx}] cell={rp["cell"]} brick={rp["brick"]} local={rp["local"]}: ' + '; '.join(diffs)
                    )
            if probe_failures:
                failures.append(f'{len(probe_failures)}/{len(ref_probes)} probes exceed tolerance')
                failures.extend(probe_failures)
    if (ref/'lighting_data.json').is_file() and (ref/'lightmap0.exr').is_file():
        try:
            got_ld, got_lm = decode_lighting_data(raw)
        except (FileNotFoundError, ValueError, subprocess.CalledProcessError) as exc:
            failures.append(str(exc))
        else:
            ref_ld = json.loads((ref/'lighting_data.json').read_text())
            ld_diffs = compare_lighting_data(ref_ld, got_ld)
            if ld_diffs:
                failures.append(f'lighting_data.json mismatch ({len(ld_diffs)} diffs)')
                failures.extend(ld_diffs)
            lm_diffs = compare_lightmap((ref/'lightmap0.exr').read_bytes(), got_lm)
            if lm_diffs:
                failures.extend(lm_diffs)
    if failures:
        for msg in failures:
            print('FAIL:', msg)
        return 1
    print(f'PASS: {len(ref_meta["cells"])} cells, {len(ref_probes)} probes match reference')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['create', 'native', 'publish', 'engine', 'compare', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    args = parser.parse_args()
    for name in [args.label, *args.labels]:
        if name and (Path(name).name != name or name in ('.', '..')):
            sys.exit('labels are plain names')
    if args.mode == 'create':
        return create()
    if args.mode == 'native':
        return native(args.label or private_label('native'))
    if args.mode == 'publish':
        if len(args.labels) != 2:
            sys.exit('publish takes two capture labels')
        return publish(args.labels[0], args.labels[1])
    if args.mode == 'engine':
        return engine(args.label or private_label('engine'))
    if args.mode == 'compare':
        if not args.label:
            sys.exit('compare needs --label')
        return compare(args.label)
    label = args.label or private_label('check')
    code = engine(label)
    return compare(label) if code == 0 else code


if __name__ == '__main__':
    sys.exit(main())
