#!/usr/bin/env python3
"""Records the model import probe in the Unity Editor or runs it in Unbound.

    run.py editor [--label L]    two independent Editor captures per color space
    run.py publish [--label L]   checks the two captures agree, copies them to Reference/
    run.py unbound [--label L]   Unbound captures per color space (UNBOUND_CLI: the binary)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings in the
validation cache, with ProjectSettings switched to the color space; nothing is written here.
Editors run through the shared heavy-job launcher, on the GPU (a private Xvfb and VirtualGL).
"""
import argparse
import filecmp
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_root, run_root

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
# Profile → color space. URP: the urp probe's pipeline asset, renderer and packages, so that the
# render pipeline's own material preprocessors run.
SPACES = {'Gamma': 0, 'Linear': 1, 'URP': 1, 'URPHookRecord': 1, 'BuiltinBlend': 0}
# Profiles staged as URP. URPHookRecord adds Hook/MaterialPostprocessRecord.cs to Assets/Editor:
# a project postprocessor whose OnPostprocessMaterial reads and records the Exotic materials'
# state (MaterialStages stage "postprocess"), leaves the others untouched and changes nothing.
URP_PROFILES = ('URP', 'URPHookRecord')
HOOKS = {'URPHookRecord': 'MaterialPostprocessRecord.cs'}
URP = PROBE.parent/'urp'
URP_FILES = ['Assets/Settings/ProbeURP.asset', 'Assets/Settings/ProbeRenderer.asset',
             'Assets/UniversalRenderPipelineGlobalSettings.asset', 'Assets/DefaultVolumeProfile.asset',
             'ProjectSettings/GraphicsSettings.asset', 'ProjectSettings/QualitySettings.asset']
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def copy_project(dest, space, material_slots=False, mtl=False, smoothing=False):
    if dest.exists():
        shutil.rmtree(dest)
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    if not mtl:
        shutil.rmtree(dest/'Assets/Resources/Mtl')
        (dest/'Assets/Resources/Mtl.meta').unlink()
    if not smoothing:
        shutil.rmtree(dest/'Assets/Resources/Smoothing')
        (dest/'Assets/Resources/Smoothing.meta').unlink()
    if not material_slots:
        shutil.rmtree(dest/'Assets/Resources/Slots')
        (dest/'Assets/Resources/Slots.meta').unlink()
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    if space in HOOKS:
        shutil.copyfile(PROBE/'Hook'/HOOKS[space], dest/'Assets/Editor'/HOOKS[space])
    if space == 'BuiltinBlend':
        shutil.copyfile(PROBE/'Hook/BlendEditor.shader', dest/'Assets/BlendEditor.shader')
        source = (PROBE/'Hook/InteractiveValidation.mat').read_text()
        for name, shader in (('InteractiveValidation', 47), ('InteractiveUntouched', 47), ('StandardUntouched', 46)):
            asset = source.replace('m_Name: InteractiveValidation', f'm_Name: {name}')
            asset = asset.replace('m_Shader: {fileID: 47,', 'm_Shader: {fileID: '+str(shader)+',')
            (dest/'Assets/Resources'/f'{name}.mat').write_text(asset)
    if space in URP_PROFILES:
        (dest/'Assets/Settings').mkdir(parents=True, exist_ok=True)
        for name in URP_FILES:
            shutil.copyfile(URP/name, dest/name)
            if name.startswith('Assets/'):
                shutil.copyfile(URP/(name + '.meta'), dest/(name + '.meta'))
        shutil.copyfile(URP/'Assets/Settings.meta', dest/'Assets/Settings.meta')
        manifest = json.loads((URP/'Packages/manifest.json').read_text())
        manifest['dependencies'].update(json.loads((PROBE/'Packages/manifest.json').read_text())['dependencies'])
        (dest/'Packages/manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        shutil.copyfile(URP/'Packages/packages-lock.json', dest/'Packages/packages-lock.json')
    settings = dest/'ProjectSettings/ProjectSettings.asset'
    text = settings.read_text()
    assert 'm_ActiveColorSpace: 0' in text
    settings.write_text(text.replace('m_ActiveColorSpace: 0', f'm_ActiveColorSpace: {SPACES[space]}'))
    return dest


def profiles():
    """MODEL_IMPORT_PROFILES (comma-separated) limits a run to some profiles."""
    wanted = os.environ.get('MODEL_IMPORT_PROFILES')
    return [p for p in SPACES if not wanted or p in wanted.split(',')]


def display():
    """A private X server for the Editors, which draw with the GPU through VirtualGL."""
    for number in range(91, 120):
        if not Path(f'/tmp/.X{number}-lock').exists():
            server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return f':{number}', server
    sys.exit('no free X display')


def editor(work):
    name, server = display()
    try:
        return run_editors(work, name)
    finally:
        server.terminate()


def run_editors(work, display_name):
    jobs = {}
    for space in profiles():
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/f'{space}-{repeat}', space)
            out = work/repeat/f'{space}.jsonl'
            out.parent.mkdir(parents=True, exist_ok=True)
            env = dict(os.environ, DISPLAY=display_name, MODEL_IMPORT_PROBE_OUT=str(out),
                       MODEL_IMPORT_DESCRIPTIONS_OUT=str(work/repeat/f'{space}.descriptions.txt'),
                       MODEL_IMPORT_BUILTIN_BLEND='1' if space == 'BuiltinBlend' else '0')
            log = work/repeat/f'{space}.log'
            jobs[f'{space}/{repeat}'] = subprocess.Popen(
                [str(LAUNCHER), 'timeout', '-s', 'KILL', '900', 'vglrun', '-d', 'egl', str(editor_path()),
                 '-batchmode', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record',
                 '-logFile', str(log)], env=env)
    codes = {key: job.wait() for key, job in jobs.items()}
    (work/'editor-exits.json').write_text(json.dumps(codes) + '\n')
    print('editor exits', codes)
    return int(any(codes.values()))


def validate_builtin_blend(work, capture):
    exits = json.loads((work/'editor-exits.json').read_text())
    if any(exits.get(f'BuiltinBlend/{repeat}') != 0 for repeat in ('native1', 'native2')):
        sys.exit('BuiltinBlend: both Editor repeats must exit normally')
    rows = [json.loads(line) for line in capture.read_text().splitlines()]
    keys = {row['k'] for row in rows}
    seed_blends = [row for row in rows if row['k'].endswith('.seed._DstBlend')]
    loaded_blends = [row for row in rows if row['k'].endswith('.loaded._DstBlend')]
    shaders = {row['k']: row['v'] for row in rows if row['k'].endswith('.loaded.shader')}
    if (len(rows) != len(keys) or len(rows) != 262 or not all(key.startswith('blend.') for key in keys)
            or len(seed_blends) != 10 or len(loaded_blends) != 10 or 'blend.playEdit._DstBlend' not in keys
            or shaders.get('blend.M_TransparentTex.loaded.shader') != 'Probe/ModelBlend'
            or not {'blend.standalone.before.rawQueue', 'blend.standalone.after.rawQueue',
                    'blend.standardUntouched.metadataAfterThreeFrames.rawQueue',
                    'blend.interactiveUntouched.metadataAfterThreeFrames.rawQueue'} <= keys
            or not any(row['k'] == 'blend.untouchedFrames' and row['v'] == 3 for row in rows)):
        sys.exit('BuiltinBlend: incomplete or invalid Editor capture')


def publish(work):
    target = reference_root(PROBE)
    target.mkdir(parents=True, exist_ok=True)
    for space in profiles():
        a, b = work/'native1'/f'{space}.jsonl', work/'native2'/f'{space}.jsonl'
        if not filecmp.cmp(a, b, shallow=False):
            sys.exit(f'{space}: the two Editor captures differ; not publishing')
        if space == 'BuiltinBlend':
            validate_builtin_blend(work, a)
        shutil.copyfile(a, target/f'{space}.jsonl')
        print('published', target/f'{space}.jsonl')
    # The descriptions do not depend on the color space or pipeline.
    if 'Gamma' not in profiles():
        return 0
    a, b = work/'native1'/'Gamma.descriptions.txt', work/'native2'/'Gamma.descriptions.txt'
    if a.read_text() != b.read_text():
        sys.exit('the two Editor description dumps differ; not publishing')
    shutil.copyfile(a, target/'descriptions.txt')
    print('published', target/'descriptions.txt')
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    code = 0
    for space in profiles():
        project = copy_project(work/'projects'/f'{space}-unbound', space)
        out = work/'unbound'/f'{space}.jsonl'
        out.parent.mkdir(parents=True, exist_ok=True)
        descriptions = work/'unbound'/f'{space}.descriptions.txt'
        descriptions.unlink(missing_ok=True)
        env = dict(os.environ, MODEL_IMPORT_PROBE_OUT=str(out), MODEL_IMPORT_DESCRIPTIONS_OUT=str(descriptions),
                   MODEL_IMPORT_BUILTIN_BLEND='1' if space == 'BuiltinBlend' else '0')
        with (work/'unbound'/f'{space}.log').open('w') as log:
            result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                     '--no-watch', '--frames', '10', '--size', '64x64'],
                                    env=env, stdout=log, stderr=subprocess.STDOUT)
        print(space, 'unbound exit', result.returncode)
        code |= result.returncode
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    return {'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
