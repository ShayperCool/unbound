#!/usr/bin/env python3
"""One public Native witness for the existing image_trails trial, with exact raw repeats."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys

HERE = Path(__file__).resolve().parent
TESTS = HERE.parents[1]
sys.path.insert(0, str(TESTS))
from editor_runner import EditorJob, run, publish, result_problem
from probe_paths import cache_artifact_path


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def save(p, value):
    p.write_text(json.dumps(value, indent=2) + '\n')


def fixture():
    original = HERE.parent/'particles/Assets/Scripts/ParticlesProbe.cs'
    source = original.read_text()
    # Exact helper and trial slices. Fail on source drift rather than approximate the fixture.
    make = source[source.index('    static ParticleSystem Make('):source.index('    static void RecordState(')]
    helpers = source[source.index('    const int ImageSize = 160;'):source.index('    void Images()')]
    assert helpers.count('        camera.Render();') == 1
    helpers = helpers.replace('        camera.Render();',
        '        TrailWitness.Inspect(GameObject.Find("image_trails").GetComponent<ParticleSystem>(), camera, target);\n        camera.Render();')
    helpers = helpers.replace('        texture.Apply();', '        texture.Apply();\n        File.WriteAllBytes(Path.Combine(dir, "trails.rgba"), texture.GetRawTextureData<byte>().ToArray());')
    trial = source[source.index('        ps = Make("image_trails"'):source.index('\n    void Simulated()')]
    assert trial.endswith('    }\n')
    trial = trial[:-6].replace('        ps = Make(', '        var ps = Make(', 1)
    return ('using System;\nusing System.IO;\nusing UnityEngine;\n'
            'public class ParticlesProbe : MonoBehaviour {\n'
            '[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]\n'
            'static void Run() => new GameObject("ParticlesProbe").AddComponent<ParticlesProbe>();\n'
            + make + helpers + '\nstatic void Done(ParticleSystem ps) => DestroyImmediate(ps.transform.root.gameObject);\n'
            'void Start() {\n'
            'var alpha = ParticleMaterial("Legacy Shaders/Particles/Alpha Blended", SoftTexture());\n'
            + trial + '\n#if UNITY_EDITOR\nUnityEditor.EditorApplication.Exit(0);\n#endif\n}\n}\n')


def authored(project):
    return {str(p.relative_to(project)): sha(p) for d in ('Assets', 'Packages', 'ProjectSettings')
            for p in sorted((project/d).rglob('*')) if p.is_file()}


def prepare(work, release):
    source = HERE.parent/'particles'
    assert (source/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] == 'm_EditorVersion: ' + release
    for side in ('native-a', 'native-b'):
        root = work/side
        root.mkdir(parents=True, exist_ok=False)
        project = root/'project'
        for d in ('Assets', 'Packages', 'ProjectSettings'):
            shutil.copytree(source/d, project/d)
        (project/'Assets/Scripts/ParticlesProbe.cs').write_text(fixture())
        shutil.copy2(HERE/'TrailWitness.cs', project/'Assets/Scripts/TrailWitness.cs')
        (root/'capture').mkdir()
        save(root/'authored-before.json', authored(project))
    save(work/'plan.json', {
        'release': release, 'fixture': 'existing image_trails only', 'seed': 12345,
        'simulation': '30 Simulate(1f/30f, true, i==0, false)',
        'max_complete_pair': 1, 'variants': [],
        'inspection': 'immediately before existing final camera.Render',
        'original_source_sha256': sha(source/'Assets/Scripts/ParticlesProbe.cs'),
        'original_image_sha256': sha(source/'Reference/images/trails.png'),
        'generated_source_sha256': sha(work/'native-a/project/Assets/Scripts/ParticlesProbe.cs'),
        'diagnostic_source_sha256': sha(HERE/'TrailWitness.cs'),
        'stop_if_raw_padding_differs': True,
    })


def capture(work, release, side):
    root = work/side
    assert not (root/'editor-run.json').exists(), 'capture is write-once'
    project = (root/'project').resolve()
    result = run(EditorJob(project, root/'editor.log', root,
        args=['-executeMethod', 'ProbeRunner.Record'], batch=True, graphics=True,
        env={'PARTICLES_IMAGE_DIR': str(root/'capture'), 'UNBOUND_PROBE_UNITY_VERSION': release},
        timeout=600, progress_files=[root/'capture/witness.jsonl'], progress_timeout=240,
        shutdown_grace=15, kill_grace=5, late_sweeps=(), label=side))
    save(root/'authored-after.json', authored(project))
    print(json.dumps({'side': side, 'problem': result_problem(root, gpu=True), 'exit': result.get('exit_code')}))


def rows(p):
    result = {}
    for line in p.read_text().splitlines():
        r = json.loads(line)
        assert r['k'] not in result, r['k']
        result[r['k']] = r['v']
    assert result.get('complete') is True
    return result


def compare(work):
    # Full exact union, including unused/raw fields. No tolerance or mask for padding.
    a, b = [rows(work/s/'capture/witness.jsonl') for s in ('native-a', 'native-b')]
    union = [{'key': k, 'a_present': k in a, 'b_present': k in b, 'a': a.get(k), 'b': b.get(k),
              'equal': k in a and k in b and a[k] == b[k]} for k in sorted(a.keys() | b.keys())]
    save(work/'native-full-union.json', union)
    from PIL import Image
    import numpy as np
    original = np.array(Image.open(HERE.parent/'particles/Reference/images/trails.png').convert('RGBA'))
    images = {}
    for side in ('native-a', 'native-b'):
        current = np.array(Image.open(work/side/'capture/trails.png').convert('RGBA'))
        assert current.shape == original.shape
        raw = np.frombuffer((work/side/'capture/trails.rgba').read_bytes(), dtype=np.uint8).reshape(current.shape)
        raw_matches_png = np.array_equal(raw[::-1], current)
        changed = np.any(current != original, axis=2)
        images[side] = {'raw_matches_png_bottom_first': raw_matches_png,
            'changed_pixels': int(changed.sum()), 'raw_rgba_union': [
                {'x': int(x), 'y': int(y), 'canonical': original[y,x].tolist(), 'observed': current[y,x].tolist()}
                for y,x in np.argwhere(changed)]}
    save(work/'original-image-full-union.json', images)
    differences = [r for r in union if not r['equal']]
    problems = {s: result_problem(work/s, gpu=True) for s in ('native-a', 'native-b')}
    summary = {'records': len(union), 'differences': len(differences), 'different_keys': [r['key'] for r in differences],
        'editor_problems': problems, 'images': {s: {k:v for k,v in r.items() if k != 'raw_rgba_union'} for s,r in images.items()}}
    summary['publishable'] = not differences and not any(problems.values()) and all(
        r['changed_pixels'] == 0 and r['raw_matches_png_bottom_first'] for r in images.values())
    save(work/'repeat-summary.json', summary)
    print(json.dumps(summary, indent=2))
    return summary['publishable']


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('command', choices=['prepare', 'native-a', 'native-b', 'compare', 'publish'])
    p.add_argument('--release', required=True)
    p.add_argument('--work', required=True, type=Path)
    args = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = cache_artifact_path(args.work).resolve()
    work.mkdir(parents=True, exist_ok=True)
    if args.command == 'prepare': prepare(work, args.release)
    elif args.command in ('native-a', 'native-b'): capture(work, args.release, args.command)
    elif args.command == 'compare': return 0 if compare(work) else 1
    else:
        if not compare(work): return 1
        def write(target, first):
            target.mkdir(parents=True)
            for name in ('witness.jsonl', 'trails.rgba', 'trails.png'):
                shutil.copy2(first/'capture'/name, target/name)
        publish([work/s for s in ('native-a', 'native-b')], HERE/'Reference'/('unity-'+args.release),
                ['capture/witness.jsonl', 'capture/trails.rgba', 'capture/trails.png'], write, gpu=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())
