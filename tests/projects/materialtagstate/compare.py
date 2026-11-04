#!/usr/bin/env python3
"""Checks and compares material tag state captures (canonical JSONL rows of
MaterialTagStateDiscriminator). Diagnostics files are never compared, only hashed by the driver.

    compare.py validate CAPTURE           every expected row exactly once, a completed render with an
                                          actual URP pipeline, the documented SetOverrideTag controls
    compare.py pair A B                   both valid and every canonical row equal (missing/extra/differing listed)
    compare.py classify CAPTURE           per subject and stage: stored override class, GetTag values, pipeline
    compare.py saved CAPTURE SAVED_JSON   before-render maps of the imported subjects next to the saved Native
                                          import maps (a report; equality is not required)

Exit status: 0 valid/equal, 1 invalid or different. Every problem is printed; nothing is dropped.
"""
import json
import sys

SUBJECTS = ['fresh-opaque', 'fresh-cleared', 'fresh-transparent', 'legacy-M_Tex', 'legacy-M_Red',
            'description-M_Tex', 'package-Lit.mat']
STAGES = ['before-render', 'after-render']
PIPELINE_FIELDS = ['configured.current', 'configured.default', 'configured.quality', 'actual.isNull',
                   'actual.type', 'actual.disposed', 'globalRenderPipeline']
SUBJECT_FIELDS = ['name', 'asset', 'shader', 'shader.asset', 'shader.subshaderTags', 'stringTagMap',
                  'GetTag.false.sentinel', 'GetTag.true.sentinel', 'GetTag.false', 'renderQueue',
                  'rawRenderQueue', 'textures']
AFTER_SUBJECT_FIELDS = ['serializedUnchanged', 'rendererBound']
RENDER_FIELDS = ['supportsSingleCameraRequestAtSubmit', 'path', 'endCameraRendering.fixtureCamera',
                 'endContextRendering.fixtureCamera', 'pipelineAtEndCamera.type', 'pipelineCreatedDuringRun',
                 'completed']
RENDER_PATHS = {'SingleCameraRequest', 'SingleCameraRequest-deferred', 'render-loop'}
URP_PIPELINE = 'UnityEngine.Rendering.Universal.UniversalRenderPipeline'
URP_ASSET = 'UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset'
LIT_GUID = '933532a4fcc9baf4fa0491de14d08ed7'
# Documented SetOverrideTag outcomes of the fixture's own materials (Unity 6000.0 Scripting API).
CONTROLS = {'fresh-opaque': {'RenderType': 'Opaque'}, 'fresh-cleared': {},
            'fresh-transparent': {'RenderType': 'Transparent'}}
SAVED_NAMES = {'legacy-M_Tex': ('MaterialsLegacy', 'M_Tex'), 'legacy-M_Red': ('MaterialsLegacy', 'M_Red'),
               'description-M_Tex': ('Materials', 'M_Tex')}


def expected_keys():
    keys = ['fixture/subjects', 'fixture/stages', 'fixture/status']
    for stage in STAGES:
        keys += [f'{stage}/pipeline/{f}' for f in PIPELINE_FIELDS]
        for s in SUBJECTS:
            keys += [f'{stage}/{s}/{f}' for f in SUBJECT_FIELDS]
    keys += [f'render/{f}' for f in RENDER_FIELDS]
    keys += [f'after-render/{s}/{f}' for s in SUBJECTS for f in AFTER_SUBJECT_FIELDS]
    return keys


def load(path):
    """Rows by key plus every structural problem (bad lines, duplicates)."""
    rows, problems = {}, []
    with open(path, encoding='utf-8') as f:
        for n, line in enumerate(f, 1):
            if not line.strip():
                continue
            try:
                row = json.loads(line)
            except json.JSONDecodeError as e:
                problems.append(f'line {n}: not JSON ({e.msg})')
                continue
            if not isinstance(row, dict) or set(row) != {'k', 'v'} or not isinstance(row['k'], str):
                problems.append(f'line {n}: not a {{"k","v"}} row')
                continue
            if row['k'] in rows:
                problems.append(f'duplicate row {row["k"]}')
                continue
            rows[row['k']] = row['v']
    return rows, problems


def override_class(m):
    if not isinstance(m, dict):
        return 'unreadable'
    if '__error__' in m:
        return 'reader-error: ' + str(m['__error__'])
    rt = m.get('RenderType')
    if rt is None:
        return 'no-override'
    return {'Opaque': 'explicit-Opaque', 'Transparent': 'explicit-Transparent'}.get(rt, 'explicit-' + str(rt))


def validate(path):
    rows, problems = load(path)
    expected = expected_keys()
    for k in expected:
        if k not in rows:
            problems.append(f'missing row {k}')
    for k in sorted(set(rows) - set(expected)):
        problems.append(f'extra row {k}')
    get = rows.get
    if get('fixture/subjects') != SUBJECTS:
        problems.append(f'fixture/subjects is {get("fixture/subjects")!r}, expected the seven subjects in order')
    if get('fixture/stages') != STAGES:
        problems.append(f'fixture/stages is {get("fixture/stages")!r}')
    if get('fixture/status') != 'complete':
        problems.append(f'fixture/status is {get("fixture/status")!r}')
    # Render completion is mandatory; a failed render is never an empty-tag observation.
    if get('render/completed') is not True:
        problems.append('render not completed')
    if get('render/endCameraRendering.fixtureCamera') is not True:
        problems.append('no endCameraRendering for the fixture camera')
    if get('render/path') not in RENDER_PATHS:
        problems.append(f'render/path {get("render/path")!r} is not a supported render path')
    if str(get('render/path') or '').startswith('SingleCameraRequest') and get('render/supportsSingleCameraRequestAtSubmit') is not True:
        problems.append('SingleCameraRequest submitted without SupportsRenderRequest')
    if get('render/pipelineAtEndCamera.type') != URP_PIPELINE:
        problems.append(f'pipeline at endCameraRendering is {get("render/pipelineAtEndCamera.type")!r}')
    # Pipeline identity: configured asset and actual instance are separate observations.
    for stage in STAGES:
        cur = get(f'{stage}/pipeline/configured.current')
        if not isinstance(cur, dict) or cur.get('type') != URP_ASSET:
            problems.append(f'{stage}: configured pipeline asset is {cur!r}')
        null, typ = get(f'{stage}/pipeline/actual.isNull'), get(f'{stage}/pipeline/actual.type')
        if not isinstance(null, bool):
            problems.append(f'{stage}: actual pipeline null-state missing or not boolean')
        elif null != (typ is None):
            problems.append(f'{stage}: actual.isNull {null} inconsistent with actual.type {typ!r}')
    if get('after-render/pipeline/actual.isNull') is not False or get('after-render/pipeline/actual.type') != URP_PIPELINE:
        problems.append('after-render: no actual URP pipeline instance')
    for stage in STAGES:
        for s in SUBJECTS:
            m = get(f'{stage}/{s}/stringTagMap')
            if isinstance(m, dict) and '__error__' in m:
                problems.append(f'{stage}/{s}: stringTagMap {m["__error__"]}')
            elif not isinstance(m, dict) and f'{stage}/{s}/stringTagMap' in rows:
                problems.append(f'{stage}/{s}: stringTagMap is not a map')
        for s, want in CONTROLS.items():
            m = get(f'{stage}/{s}/stringTagMap')
            if m is not None and m != want:
                problems.append(f'{stage}/{s}: control map {m!r}, documented SetOverrideTag gives {want!r}')
            shader = get(f'{stage}/{s}/shader.asset')
            if shader is not None and (not isinstance(shader, dict) or shader.get('guid') != LIT_GUID):
                problems.append(f'{stage}/{s}: shader is not the package Lit shader ({shader!r})')
    for s in SUBJECTS:
        if get(f'after-render/{s}/rendererBound') is False:
            problems.append(f'{s}: fixture renderer lost its subject material')
    notes = [f'observation: {s} serialized state changed across the render'
             for s in SUBJECTS if get(f'after-render/{s}/serializedUnchanged') is False]
    return rows, problems, notes


def report(path, problems, notes):
    for n in notes:
        print(f'{path}: {n}')
    for p in problems:
        print(f'{path}: INVALID {p}')
    print(f'{path}: {"valid" if not problems else f"{len(problems)} problem(s)"}')


def cmd_validate(path):
    _, problems, notes = validate(path)
    report(path, problems, notes)
    return 1 if problems else 0


def cmd_pair(a, b):
    ra, pa, na = validate(a)
    rb, pb, nb = validate(b)
    report(a, pa, na)
    report(b, pb, nb)
    diffs = []
    for k in sorted(set(ra) | set(rb)):
        if k not in ra:
            diffs.append(f'only in {b}: {k}')
        elif k not in rb:
            diffs.append(f'only in {a}: {k}')
        elif ra[k] != rb[k]:
            diffs.append(f'differs {k}: {json.dumps(ra[k], sort_keys=True)} != {json.dumps(rb[k], sort_keys=True)}')
    for d in diffs:
        print(d)
    print(f'pair: {len(diffs)} canonical difference(s)')
    return 1 if pa or pb or diffs else 0


def cmd_classify(path):
    rows, problems, notes = validate(path)
    report(path, problems, notes)
    for stage in STAGES:
        null = rows.get(f'{stage}/pipeline/actual.isNull')
        state = ('actual pipeline null' if null is True else
                 f'actual pipeline {rows.get(f"{stage}/pipeline/actual.type")} (no unloaded observation at this stage)'
                 if null is False else 'actual pipeline not recorded')
        print(f'{stage}: {state}; globalRenderPipeline={rows.get(f"{stage}/pipeline/globalRenderPipeline")!r}')
        for s in SUBJECTS:
            print(f'  {s:18} {override_class(rows.get(f"{stage}/{s}/stringTagMap")):22} '
                  f'GetTag(false)={rows.get(f"{stage}/{s}/GetTag.false")!r} '
                  f'GetTag(false,sentinel)={rows.get(f"{stage}/{s}/GetTag.false.sentinel")!r} '
                  f'GetTag(true,sentinel)={rows.get(f"{stage}/{s}/GetTag.true.sentinel")!r} '
                  f'queue={rows.get(f"{stage}/{s}/renderQueue")}/{rows.get(f"{stage}/{s}/rawRenderQueue")}')
    print(f'render: path={rows.get("render/path")!r} completed={rows.get("render/completed")!r} '
          f'pipelineCreatedDuringRun={rows.get("render/pipelineCreatedDuringRun")!r}')
    return 1 if problems else 0


def cmd_saved(path, saved_path):
    rows, problems, notes = validate(path)
    report(path, problems, notes)
    saved = json.load(open(saved_path, encoding='utf-8'))
    for label, (root, name) in SAVED_NAMES.items():
        got = rows.get(f'before-render/{label}/stringTagMap')
        for repeat, files in sorted(saved['repeats'].items()):
            maps = [m['stringTagMap'] for f in files if root in f['model_root_names']
                    for m in f['materials'] if m['name'] == name]
            if len(maps) != 1:
                print(f'{label} {repeat}: saved import has {len(maps)} {root}/{name} materials')
                continue
            print(f'{label} {repeat}: {"same as" if maps[0] == got else "DIFFERS from"} saved import '
                  f'(capture {got!r}, saved {maps[0]!r})')
    return 1 if problems else 0


def main(argv):
    commands = {'validate': (cmd_validate, 1), 'pair': (cmd_pair, 2), 'classify': (cmd_classify, 1), 'saved': (cmd_saved, 2)}
    if len(argv) < 2 or argv[1] not in commands or len(argv) - 2 != commands[argv[1]][1]:
        sys.exit(__doc__)
    fn, _ = commands[argv[1]]
    return fn(*argv[2:])


if __name__ == '__main__':
    sys.exit(main(sys.argv))
