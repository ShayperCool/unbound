#!/usr/bin/env python3
"""Stage the UI fixture in cache, run sampler bias controls, and wrap the heavy child.

--device selects the graphics device of both sides: opengl (the default: the Linux Editor's own
device on a private display through VirtualGL; Unbound with the project's device) or vulkan (the
Editor with -force-vulkan on the GPU-backed display :0, Unbound with --force-vulkan)."""
import argparse, hashlib, json, os, shutil, subprocess, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['native', 'engine'])
p.add_argument('--release', required=True)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--cli', type=Path)
p.add_argument('--managed', type=Path)
p.add_argument('--anisotropy', choices=['Force', 'Enable', 'Disable'], default='Force')
p.add_argument('--bilinear-control', action='store_true')
p.add_argument('--live-control', action='store_true')
p.add_argument('--cache-control', action='store_true')
p.add_argument('--device', choices=['opengl', 'vulkan'], default='opengl')
a = p.parse_args()
if a.cache_control and (a.live_control or a.bilinear_control):
    p.error('--cache-control is a separate profile')
output = cache_artifact_path(a.output.expanduser().resolve())
if output.exists(): p.error('output must be fresh')
output.mkdir(parents=True)
project = output/'project'
for name in ['Assets', 'Packages', 'ProjectSettings']:
    shutil.copytree(ROOT.parent/'uitk'/name, project/name)
(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+a.release+'\n')
if a.anisotropy != 'Force':
    import re
    quality = project/'ProjectSettings/QualitySettings.asset'
    quality.write_text(re.sub(r'anisotropicTextures: \d+', 'anisotropicTextures: ' + ('1' if a.anisotropy == 'Enable' else '0'), quality.read_text()))
shutil.copy2(ROOT/'Assets/Scripts/SamplerBiasProbe.cs', project/'Assets/Scripts/UitkProbe.cs')
shutil.copytree(ROOT/'Assets/Shaders', project/'Assets/Shaders')
env = dict(os.environ, SAMPLER_BIAS_OUT=str(output/'capture'))
if a.cache_control:
    env['SAMPLER_BIAS_CACHE'] = '1'
else:
    env.pop('SAMPLER_BIAS_CACHE', None)
if a.live_control:
    env['SAMPLER_BIAS_LIVE'] = '1'
else:
    env.pop('SAMPLER_BIAS_LIVE', None)
if a.bilinear_control:
    env['SAMPLER_BIAS_BILINEAR'] = '1'
else:
    env.pop('SAMPLER_BIAS_BILINEAR', None)
env.pop('WT', None)
if a.mode == 'native':
    editor = [str(Path.home()/f'Unity/Hub/Editor/{a.release}/Editor/Unity'), '-batchmode', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record', '-logFile', str(output/'editor.log')]
    if a.device == 'vulkan':
        from editor_runner import vulkan_display_env
        env.update(vulkan_display_env())
        command = [editor[0], '-force-vulkan', *editor[1:]]
    else:
        command = ['xvfb-run', '-a', 'vglrun', '-d', 'egl', *editor]
else:
    if not a.cli or not a.managed: p.error('engine requires cli and managed')
    env['UNBOUND_MANAGED_DIR'] = str(a.managed.resolve())
    env['LD_LIBRARY_PATH'] = str(a.managed.resolve())
    command = [str(a.cli.resolve()), 'run', '-p', str(project), '--frames', '35', '--size', '960x540', '--no-watch',
               *(['--force-vulkan'] if a.device == 'vulkan' else [])]
hashes = {str(x.resolve()): hashlib.sha256(x.read_bytes()).hexdigest() for x in [a.cli, a.managed/'UnityEngine.CoreModule.dll', a.managed/'libunbound_physx.so']} if a.mode == 'engine' else {}
sources = {str(x.relative_to(project)): hashlib.sha256(x.read_bytes()).hexdigest()
           for x in sorted(project.rglob('*')) if x.is_file()}
(output/'execution.json').write_text(json.dumps(dict(command=command, sha256=hashes, sources=sources, release=a.release,
    anisotropy=a.anisotropy, bilinear_control=a.bilinear_control, live_control=a.live_control,
    cache_control=a.cache_control, device=a.device,
    sampler_environment={k: v for k, v in env.items() if k.startswith('SAMPLER_BIAS_')}), indent=2)+'\n')
with (output/'process.log').open('w') as log:
    subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '600', *command], env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
for path, digest in hashes.items():
    assert hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest, path
assert (output/'capture/values.jsonl').is_file(), 'missing capture; inspect editor/process log'
