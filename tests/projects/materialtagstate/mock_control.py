#!/usr/bin/env python3
"""Mock controls for compare.py: a deterministic writer emits captures shaped like
MaterialTagStateDiscriminator's (good and defective), and the comparator must accept or reject
each as expected. These test the fixture's tooling, not Unity.

    mock_control.py --out <cache folder>

Also checks that every row name the C# fixture writes is one compare.py expects. Writes only
under --out; exit 0 when every scenario gets its expected verdict.
"""
import argparse
import copy
import json
import os
from pathlib import Path
import re
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE))
import compare  # noqa: E402

FIXTURE = PROBE/'Assets/Editor/MaterialTagStateDiscriminator.cs'
LIT = {'name': 'Lit', 'type': 'UnityEngine.Shader', 'path': 'Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader',
       'guid': compare.LIT_GUID, 'fileID': 4800000}
ASSET = {'name': 'ProbeURP', 'type': compare.URP_ASSET, 'path': 'Assets/Settings/ProbeURP.asset',
         'guid': '00000000000000000000000000000001', 'fileID': 11400000}
SUBSHADERS = [{'RenderType': 'Opaque', 'RenderPipeline': 'UniversalPipeline'}]


def subject_state(label, tag_map):
    asset = {'path': 'not-an-asset', 'name': label, 'type': 'UnityEngine.Material'}
    return {'name': label, 'asset': asset, 'shader': 'Universal Render Pipeline/Lit', 'shader.asset': LIT,
            'shader.subshaderTags': SUBSHADERS, 'stringTagMap': tag_map,
            'GetTag.false.sentinel': tag_map.get('RenderType', '<unset-sentinel>'),
            'GetTag.true.sentinel': tag_map.get('RenderType', 'Opaque'), 'GetTag.false': tag_map.get('RenderType', ''),
            'renderQueue': 3000 if tag_map.get('RenderType') == 'Transparent' else 2000, 'rawRenderQueue': -1,
            'textures': {'_BaseMap': None, '_EmissionMap': None, '_BumpMap': None}}


def good_state(before_loaded=False):
    maps = {'fresh-opaque': {'RenderType': 'Opaque'}, 'fresh-cleared': {}, 'fresh-transparent': {'RenderType': 'Transparent'},
            'legacy-M_Tex': {'RenderType': 'Opaque'}, 'legacy-M_Red': {}, 'description-M_Tex': {},
            'package-Lit.mat': {'RenderType': 'Opaque'}}
    stages = {}
    for stage in compare.STAGES:
        loaded = stage == 'after-render' or before_loaded
        stages[stage] = {
            'pipeline': {'configured.current': ASSET, 'configured.default': ASSET, 'configured.quality': None,
                         'actual.isNull': not loaded, 'actual.type': compare.URP_PIPELINE if loaded else None,
                         'actual.disposed': False if loaded else None,
                         'globalRenderPipeline': 'UniversalPipeline' if loaded else ''},
            'subjects': {s: subject_state(s, maps[s]) for s in compare.SUBJECTS}}
    return {'stages': stages,
            'render': {'supportsSingleCameraRequestAtSubmit': True, 'path': 'SingleCameraRequest',
                       'endCameraRendering.fixtureCamera': True, 'endContextRendering.fixtureCamera': True,
                       'pipelineAtEndCamera.type': compare.URP_PIPELINE, 'pipelineCreatedDuringRun': not before_loaded,
                       'completed': True},
            'after': {s: {'serializedUnchanged': True, 'rendererBound': True} for s in compare.SUBJECTS},
            'status': 'complete', 'write_after': True}


def rows_of(state):
    """The fixture's row order: subjects, stages, before, render, after, after checks, status."""
    rows = [('fixture/subjects', list(compare.SUBJECTS)), ('fixture/stages', list(compare.STAGES))]

    def stage_rows(stage):
        st = state['stages'][stage]
        out = [(f'{stage}/pipeline/{f}', st['pipeline'][f]) for f in compare.PIPELINE_FIELDS]
        for s in compare.SUBJECTS:
            out += [(f'{stage}/{s}/{f}', st['subjects'][s][f]) for f in compare.SUBJECT_FIELDS]
        return out
    rows += stage_rows('before-render')
    rows += [(f'render/{f}', state['render'][f]) for f in compare.RENDER_FIELDS]
    if state['write_after']:
        rows += stage_rows('after-render')
        for s in compare.SUBJECTS:
            rows += [(f'after-render/{s}/{f}', state['after'][s][f]) for f in compare.AFTER_SUBJECT_FIELDS]
    rows.append(('fixture/status', state['status']))
    return rows


def write(path, rows, extra_lines=()):
    with open(path, 'w', encoding='utf-8') as f:
        for k, v in rows:
            f.write(json.dumps({'k': k, 'v': v}, sort_keys=True, separators=(',', ':')) + '\n')
        for line in extra_lines:
            f.write(line + '\n')


def scenarios():
    """(name, mutate(state) -> rows or (rows, extra_lines), command, expected exit, required output fragments)."""
    def mut(fn):
        def build():
            s = good_state()
            r = fn(s)
            return r if r is not None else rows_of(s)
        return build

    def no_render(s):
        s['render'].update({'completed': False, 'endCameraRendering.fixtureCamera': False, 'path': 'none',
                            'pipelineAtEndCamera.type': None, 'endContextRendering.fixtureCamera': False})
        s['write_after'] = False
        s['status'] = 'failed: no-render-completion-and-request-unsupported'

    def no_end_camera(s):
        s['render']['endCameraRendering.fixtureCamera'] = False

    def camera_render(s):
        s['render']['path'] = 'Camera.Render'

    def unsupported_request(s):
        s['render']['supportsSingleCameraRequestAtSubmit'] = False

    def no_actual_pipeline(s):
        s['stages']['after-render']['pipeline'].update({'actual.isNull': True, 'actual.type': None})

    def no_configured_asset(s):
        s['stages']['before-render']['pipeline']['configured.current'] = None

    def inconsistent_pipeline(s):
        s['stages']['before-render']['pipeline']['actual.type'] = compare.URP_PIPELINE

    def drop(key):
        return lambda s: [r for r in rows_of(s) if r[0] != key]

    def extra(s):
        return rows_of(s) + [('after-render/legacy-M_Tex/unexpected', 1)]

    def duplicate(s):
        r = rows_of(s)
        return r + [r[10]]

    def cleared_as_opaque(s):
        for st in s['stages'].values():
            st['subjects']['fresh-cleared']['stringTagMap'] = {'RenderType': 'Opaque'}

    def swapped(s):
        st = s['stages']['after-render']['subjects']
        st['fresh-opaque']['stringTagMap'], st['fresh-transparent']['stringTagMap'] = {'RenderType': 'Transparent'}, {'RenderType': 'Opaque'}

    def reader_error(s):
        s['stages']['before-render']['subjects']['legacy-M_Tex']['stringTagMap'] = {'__error__': 'stringTagMap property missing'}

    def renderer_lost(s):
        s['after']['package-Lit.mat']['rendererBound'] = False

    def serialized_changed(s):
        s['after']['legacy-M_Red']['serializedUnchanged'] = False

    def malformed(s):
        return rows_of(s), ['{"k": "broken"']

    def wrong_subject_order(s):
        r = rows_of(s)
        r[0] = ('fixture/subjects', list(reversed(compare.SUBJECTS)))
        return r

    E = 'INVALID'
    return [
        ('good', mut(lambda s: None), 'validate', 0, ['valid']),
        ('good-before-loaded', lambda: rows_of(good_state(before_loaded=True)), 'classify', 0,
         ['no unloaded observation at this stage']),
        ('missing-render-completion', mut(no_render), 'validate', 1, [f'{E} render not completed', f'{E} missing row after-render/']),
        ('no-endCameraRendering', mut(no_end_camera), 'validate', 1, [f'{E} no endCameraRendering']),
        ('camera-render-path', mut(camera_render), 'validate', 1, [f'{E} render/path']),
        ('request-without-support', mut(unsupported_request), 'validate', 1, [f'{E} SingleCameraRequest submitted without']),
        ('missing-actual-pipeline', mut(no_actual_pipeline), 'validate', 1, [f'{E} after-render: no actual URP pipeline']),
        ('missing-configured-asset', mut(no_configured_asset), 'validate', 1, [f'{E} before-render: configured pipeline asset']),
        ('inconsistent-pipeline-identity', mut(inconsistent_pipeline), 'validate', 1, [f'{E} before-render: actual.isNull']),
        ('dropped-pipeline-row', mut(drop('after-render/pipeline/actual.type')), 'validate', 1,
         [f'{E} missing row after-render/pipeline/actual.type']),
        ('dropped-subject-row', mut(drop('before-render/description-M_Tex/stringTagMap')), 'validate', 1,
         [f'{E} missing row before-render/description-M_Tex/stringTagMap']),
        ('dropped-status-row', mut(drop('fixture/status')), 'validate', 1, [f'{E} missing row fixture/status']),
        ('extra-row', mut(extra), 'validate', 1, [f'{E} extra row after-render/legacy-M_Tex/unexpected']),
        ('duplicate-row', mut(duplicate), 'validate', 1, [f'{E} duplicate row']),
        ('malformed-line', mut(malformed), 'validate', 1, [f'{E} line']),
        ('wrong-subject-order', mut(wrong_subject_order), 'validate', 1, [f'{E} fixture/subjects']),
        ('cleared-not-distinguished', mut(cleared_as_opaque), 'validate', 1, [f'{E} before-render/fresh-cleared: control map']),
        ('opaque-transparent-swapped', mut(swapped), 'validate', 1,
         [f'{E} after-render/fresh-opaque: control map', f'{E} after-render/fresh-transparent: control map']),
        ('map-reader-error', mut(reader_error), 'validate', 1, [f'{E} before-render/legacy-M_Tex: stringTagMap']),
        ('renderer-lost-material', mut(renderer_lost), 'validate', 1, [f'{E} package-Lit.mat: fixture renderer']),
        ('serialized-change-is-observation', mut(serialized_changed), 'validate', 0,
         ['observation: legacy-M_Red serialized state changed']),
        ('classify-three-classes', mut(lambda s: None), 'classify', 0,
         [f'{s:18} {c}' for s, c in (('fresh-opaque', 'explicit-Opaque'), ('fresh-cleared', 'no-override'),
                                     ('fresh-transparent', 'explicit-Transparent'), ('legacy-M_Tex', 'explicit-Opaque'),
                                     ('description-M_Tex', 'no-override'))]),
    ]


def pair_scenarios():
    def differing():
        s = good_state()
        s['stages']['after-render']['subjects']['legacy-M_Tex']['GetTag.false'] = ''
        return rows_of(s)

    def reordered():
        return list(reversed(rows_of(good_state())))
    return [
        ('pair-identical', reordered, 0, ['pair: 0 canonical difference(s)']),
        ('pair-one-difference', differing, 1, ['differs after-render/legacy-M_Tex/GetTag.false', 'pair: 1 canonical difference(s)']),
    ]


def saved_scenarios():
    saved = {'repeats': {r: [
        {'model_root_names': ['MaterialsLegacy'], 'materials': [{'name': 'M_Tex', 'stringTagMap': {'RenderType': 'Opaque'}},
                                                                {'name': 'M_Red', 'stringTagMap': {}}]},
        {'model_root_names': ['Materials'], 'materials': [{'name': 'M_Tex', 'stringTagMap': {}}]}] for r in ('native1', 'native2')}}

    def cleared():
        s = good_state()
        s['stages']['before-render']['subjects']['legacy-M_Tex']['stringTagMap'] = {}
        return rows_of(s)
    return saved, [
        ('saved-same', lambda: rows_of(good_state()), 0, ['legacy-M_Tex native1: same as saved', 'description-M_Tex native2: same as saved']),
        ('saved-differs-reported', cleared, 0, ['legacy-M_Tex native1: DIFFERS from saved', 'legacy-M_Tex native2: DIFFERS from saved']),
    ]


def fixture_row_names():
    """Row names the C# writes, checked against compare.py's schema."""
    src = FIXTURE.read_text(encoding='utf-8')
    literals = set(re.findall(r'Row\("([^"]+)",', src))
    literals |= set(re.findall(r'Row\(key \+ "([^"]+)"', src))
    literals |= set(re.findall(r'Row\(stage \+ "/pipeline/([^"]+)"', src))
    literals |= set(re.findall(r'"after-render/" \+ s\.Label \+ "/([^"]+)"', src))
    return literals


# The comparator child gets an environment built from an empty mapping with these key names only.
CHILD_ENV_KEYS = ('PATH', 'HOME', 'LANG', 'LC_ALL', 'TZ')


def run(argv):
    env = {k: os.environ[k] for k in CHILD_ENV_KEYS if k in os.environ}
    env['PYTHONDONTWRITEBYTECODE'] = '1'
    return subprocess.run([sys.executable, '-B', str(PROBE/'compare.py')] + argv, capture_output=True, text=True, env=env)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--out', required=True, type=Path)
    out = ap.parse_args().out
    out.mkdir(parents=True, exist_ok=False)
    results, ok = [], True

    def check(name, proc, want_code, fragments, files):
        nonlocal ok
        text = proc.stdout + proc.stderr
        missing = [f for f in fragments if f not in text]
        passed = proc.returncode == want_code and not missing
        ok &= passed
        (out/f'{name}.out.txt').write_text(text)
        results.append({'scenario': name, 'files': files, 'exit': proc.returncode, 'expected_exit': want_code,
                        'missing_fragments': missing, 'passed': passed})
        print(('PASS ' if passed else 'FAIL ') + name)

    for name, build, command, code, fragments in scenarios():
        built = build()
        rows, extra = built if isinstance(built, tuple) else (built, ())
        path = out/f'{name}.jsonl'
        write(path, rows, extra)
        check(name, run([command, str(path)]), code, fragments, [path.name])
    base = out/'pair-base.jsonl'
    write(base, rows_of(good_state()))
    for name, build, code, fragments in pair_scenarios():
        path = out/f'{name}.jsonl'
        write(path, build())
        check(name, run(['pair', str(base), str(path)]), code, fragments, [base.name, path.name])
    saved, cases = saved_scenarios()
    saved_path = out/'saved-mock.json'
    saved_path.write_text(json.dumps(saved, indent=2) + '\n')
    for name, build, code, fragments in cases:
        path = out/f'{name}.jsonl'
        write(path, build())
        check(name, run(['saved', str(path), str(saved_path)]), code, fragments, [path.name, saved_path.name])
    fields = set(compare.PIPELINE_FIELDS + compare.SUBJECT_FIELDS + compare.AFTER_SUBJECT_FIELDS + compare.RENDER_FIELDS) | {
        f'render/{f}' for f in compare.RENDER_FIELDS} | {'fixture/subjects', 'fixture/stages', 'fixture/status'}
    written = fixture_row_names()
    unknown = sorted(written - fields)
    unwritten = sorted(fields - written - set(compare.RENDER_FIELDS))
    schema_ok = not unknown and not unwritten
    ok &= schema_ok
    results.append({'scenario': 'fixture-row-names-match-schema', 'unknown_in_fixture': unknown,
                    'schema_fields_not_written': unwritten, 'passed': schema_ok})
    print(('PASS ' if schema_ok else 'FAIL ') + 'fixture-row-names-match-schema', unknown, unwritten)
    (out/'results.json').write_text(json.dumps({'all_passed': ok, 'results': results}, indent=2) + '\n')
    print('all passed' if ok else 'SOME SCENARIOS FAILED')
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
