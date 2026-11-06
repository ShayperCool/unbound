#!/usr/bin/env python3
"""Run every Unity-reference probe on one engine build and compare it with its own comparator.

    python3 tools/agents/suite.py [--cli PATH] [--label L] [--only P,...] [--jobs N]
                                  [--against DIR|none] [--update-baseline]
    python3 tools/agents/suite.py --promote LABEL      make an earlier run the baseline

Every directory tests/projects/<probe> with a Reference/ is a probe. Each one has a recipe below,
taken from its README, its run.py modes (unbound/engine, then its comparator or verifier) and the
old regress script. A probe without a working recipe is listed as "no recipe" with the reason;
none is skipped silently. Comparators, references and tolerances are used as they are.

The engine is --cli, else $UNBOUND, else this checkout's release build (the target that
`tools/build.py --print-env` selects). Its managed directory is $UNBOUND_MANAGED_DIR, else the
`managed/` next to or above the executable. Engine runs go through ~/.cache/compat/with-heavy-job;
drivers that already use the launcher are not wrapped again. Probes run in parallel (--jobs,
default: the pool's engine slots + 2); the launcher's pools bound the machine.

Statuses:
  pass       every step exited 0 and the comparator accepted the capture
  fail       a step failed (the line names it) or the comparator rejected the capture
  report     the comparator printed a measurement but has no verdict (some image comparators)
  no recipe  not run; the reason is listed

Run output goes to $XDG_CACHE_HOME/unbound/validation/suite/<label>/<probe>/ (one log per step).
The summary goes to $XDG_CACHE_HOME/unbound/agents/suite/<label>/summary.json. --against compares
with an earlier summary (default: agents/baseline/ when it exists) and lists every probe whose
status or summary changed. --update-baseline copies this run's summary to agents/baseline/;
--promote LABEL does that for an earlier run without running anything.

Exit codes: 0 when no probe has a new failure against the baseline and no failing probe got worse
(fewer passing or more failing checks or counted failures, or an earlier failing step), or, without
a baseline, when none failed; 1 otherwise; 2 for usage errors.
"""
import argparse
import concurrent.futures
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import signal
import subprocess
import sys
import threading
import time

REPO = Path(__file__).resolve().parents[2]
PROJECTS = REPO/'tests/projects'
CACHE = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound'
AGENTS = CACHE/'agents'
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
LIMITS = Path.home()/'.cache/compat/job-limits.json'
ENGINE_TIMEOUT = 1200
DRIVER_TIMEOUT = 5400
TOOL_TIMEOUT = 1800


class Step:
    """One command of a recipe.

    kind: 'engine' wraps argv in the launcher with a timeout; 'driver' runs a script that admits
    its own heavy jobs; 'compare' runs a comparator, whose output gives the probe's summary;
    'identical' compares two files byte for byte (args: reference, capture), after replacing each
    (old, new) pair of `replace` in the capture.
    verdict (compare steps): 'exit' uses the exit code; 'summary' derives pass/fail from the
    comparator's own counts (for comparators that always exit 0); 'differences' requires every
    printed `'differences': N` to be 0; 'none' records a measurement. With any verdict, output
    naming a MISSING capture fails the step.
    args_from: a command whose output (split on whitespace) is appended to argv.
    """

    def __init__(self, kind, argv, env=None, verdict='exit', ok=(0,), args_from=None, launcher_kind=None,
                 timeout=None, replace=()):
        self.replace = list(replace)
        self.kind, self.argv, self.env, self.verdict = kind, list(argv), dict(env or {}), verdict
        self.ok, self.args_from, self.launcher_kind = tuple(ok), args_from, launcher_kind
        self.timeout = timeout or {'engine': ENGINE_TIMEOUT, 'driver': DRIVER_TIMEOUT}.get(kind, TOOL_TIMEOUT)


class NoRecipe:
    def __init__(self, reason):
        self.reason = reason


def run(*args, env=None, timeout=None):
    """`unbound run -p <probe> ARGS` with OUT variables in env."""
    return Step('engine', ['{cli}', 'run', '-p', '{probe}', *args], env, timeout=timeout)


def py(script, *args, env=None, verdict='exit', ok=(0,)):
    """A comparator or verifier: script is relative to the probe, or to tests/projects with a '/'."""
    path = '{projects}/'+script if '/' in script else '{probe}/'+script
    return Step('compare', ['{python}', path, *args], env, verdict=verdict, ok=ok)


def driver(script, *args, env=None):
    path = '{projects}/'+script if '/' in script else '{probe}/'+script
    return Step('driver', ['{python}', path, *args], env)


def ref(name=''):
    return '{probe}/Reference' + ('/'+name if name else '')


def classic(variable, out, run_args, compare=None, images=None, verdict='summary', extra_env=None):
    """The original probe layout: an *_OUT variable, `unbound run`, compare.py REFERENCE OUT."""
    target = '{work}/'+out
    steps = [run(*run_args, env={variable: target, **(extra_env or {})})]
    steps.append(py('compare.py', *(compare or [ref(), target]), verdict=verdict))
    if images:
        # compare_images.py prints means but always exits 0: a measurement, not a verdict.
        steps.append(py('particles/compare_images.py', ref(images if images is not True else ''), target,
                        verdict='none'))
    return steps


def guarded(probe_compare, capture='unbound', *, extra=()):
    """Drivers with `run.py unbound --release R --label L` writing validation/runs/<probe>/<L>/."""
    return [driver('run.py', 'unbound', '--release', '{release}', '--label', '{label}', *extra,
                   env={'UNBOUND_CLI': '{cli}', 'UNBOUND_MANAGED_DIR': '{managed}'}),
            py('compare.py', *probe_compare(f'{{runs}}/{capture}'))]


def single_ref(suffix='jsonl'):
    return '{probe}/Reference/unity-{release}.'+suffix


def particle_family(verifier='verify.py', extra_verify=()):
    """prepare, engine, then verify.py --actual in the probe's private work directory."""
    common = ('--unity-version', '{release}', '--work', '{work}', '--label', 'engine')
    return [driver('run.py', 'prepare', *common),
            driver('run.py', 'engine', *common, '--cli', '{cli}', '--runtime', '{managed}'),
            py(verifier, '--unity-version', '{release}', '--actual', '{work}/engine.jsonl',
               '--output', '{work}/report.json', *extra_verify)]


def query_family(variable):
    """2D query probes: SCENE_SETTINGS_CLI + <X>_WORK, prepare/engine --label, verify.py --actual."""
    env = {variable: '{work}', 'SCENE_SETTINGS_CLI': '{cli}', 'UNBOUND_MANAGED_DIR': '{managed}'}
    return [driver('run.py', 'prepare', '--label', 'engine', env=env),
            driver('run.py', 'engine', '--label', 'engine', env=env),
            py('verify.py', '--actual', '{work}/engine.json', '--report', '{work}/report.json')]


def physics2d(prefix, suite):
    env = {prefix+'_WORK': '{work}', prefix+'_PROJECT': 'project-suite', 'UNBOUND_CLI': '{cli}',
           'UNBOUND_MANAGED_DIR': '{managed}'}
    return [driver('run.py', 'prepare', '--label', 'suite', env=env),
            driver('run.py', 'engine', '--label', 'final', env=env),
            py('verify.py', '--suite', suite, '--candidate', '{work}/final.json',
               '--output', '{work}/report.json')]


def trigger2d(probes):
    env = {'TRIGGER_WORK': '{work}', 'UNBOUND_CLI': '{cli}', 'UNBOUND_MANAGED_DIR': '{managed}'}
    steps = [driver('run.py', 'prepare', '--probe', name, env=env) for name in probes]
    steps += [driver('run.py', 'engine', '--probe', name, '--label', 'final', env=env) for name in probes]
    steps.append(py('verify.py', '--work', '{work}', '--output', '{work}/report.json', env=env))
    return steps


NOTES = {}


def recipes(release):
    """Recipe per probe name. Built per run, as some expand over the probe's reference files."""
    R = {}
    headless = ('--headless', '300000')

    # Probes recorded through *_PROBE_OUT and compared by compare.py (README / regress recipes).
    R['graphics'] = classic('GRAPHICS_PROBE_OUT', 'out.json', ['--no-watch', '--frames', '30'],
                            [single_ref('json'), '{work}/out.json'], verdict='exit')
    R['sorting'] = [run('--size', '960x540', '--no-watch', '--screenshot', '{work}/out.png', '--frames', '10',
                        env={'SORTING_PROBE_OUT': '{work}/out.json', 'SORTING_PROBE_SCREENSHOT': '1'}),
                    py('compare.py', single_ref('json'), '{work}/out.json', '{work}/out.png')]
    R['playables'] = classic('PLAYABLES_PROBE_OUT', 'out.json', ['--headless', '1200'],
                             [single_ref('json'), '{work}/out.json'], verdict='exit')
    R['physics'] = classic('PHYSICS_PROBE_OUT', 'out.json', ['--headless', '600'],
                           [single_ref('json'), '{work}/out.json'], verdict='exit')
    R['editor'] = classic('EDITOR_PROBE_OUT', 'out.json', ['--frames', '20', '--size', '320x180', '--no-watch'],
                          [single_ref('json'), '{work}/out.json'], verdict='exit')
    R['jobs'] = classic('JOBS_PROBE_OUT', 'out.json', ['--headless', '2000'],
                        [single_ref('json'), '{work}/out.json'], verdict='exit')
    R['properties'] = classic('PROPERTIES_PROBE_OUT', 'out.json', ['--headless', '5', '--no-watch'],
                              [single_ref('json'), '{work}/out.json'], verdict='exit')
    R['input'] = [Step('engine', ['sh', '{probe}/run-unbound.sh', single_ref(), '{work}/out.jsonl', '{cli}'],
                       launcher_kind='engine-cpu'),
                  py('compare.py', single_ref(), '{work}/out.jsonl')]
    R['messages'] = [run('--headless', '445', '--size', '960x540', '--no-watch',
                         env={'MESSAGES_PROBE_OUT': '{work}/out.jsonl'},),
                     py('compare.py', single_ref(), '{work}/out.jsonl')]
    R['messages'][0].args_from = ['{python}', '{probe}/compare.py', 'args', single_ref()]
    R['lifecycle'] = [run('--headless', '12', '--no-watch', env={'LIFECYCLE_PROBE_OUT': '{work}/out.jsonl'}),
                      Step('identical', [single_ref(), '{work}/out.jsonl'])]
    # The regress script's normalisation of one exception text, kept as it was.
    R['platform'] = [run('--headless', '5', '--no-watch', env={'PLATFORM_PROBE_OUT': '{work}/out.jsonl'}),
                     Step('identical', [single_ref(), '{work}/out.jsonl'],
                          replace=[('instance of an object.', 'instance of an object')])]
    R['particles'] = [run('--frames', '60', '--size', '320x180', '--no-watch',
                          env={'PARTICLES_PROBE_OUT': '{work}/out.jsonl', 'PARTICLES_IMAGE_DIR': '{work}/images'}),
                      py('compare.py', single_ref(), '{work}/out.jsonl', verdict='summary'),
                      py('compare_images.py', ref('images'), '{work}/images', verdict='none')]
    R['lighting'] = [run('--frames', '10', '--size', '320x180', '--no-watch', env={'LIGHTING_PROBE_OUT': '{work}/out'}),
                     py('particles/compare_images.py', ref('images'), '{work}/out', verdict='none')]
    R['materials'] = classic('MATERIALS_PROBE_OUT', 'out.json', ['--no-watch', '--frames', '10'],
                             [single_ref('json'), '{work}/out.json'], verdict='exit')
    R['builtinrp'] = classic('BUILTIN_PROBE_OUT', 'out', ['--frames', '30', '--size', '640x480', '--no-watch'],
                             [ref(), '{work}/out', '-q'])
    R['builtinfx'] = classic('BUILTINFX_PROBE_OUT', 'out', ['--frames', '2000', '--size', '640x480', '--no-watch'],
                             [ref(), '{work}/out', '-q'], verdict='exit')
    R['resources'] = classic('RESOURCES_PROBE_OUT', 'out', ['--frames', '2000', '--size', '640x480', '--no-watch'],
                             [ref(), '{work}/out', '-q'], verdict='exit')
    R['display'] = [run('--no-watch', '--size', '320x180', '--snapshot', '4:{work}/display.png',
                        env={'DISPLAY_PROBE_OUT': '{work}/out'}),
                    # display/compare.py always exits 0: its values count decides, its image is measured.
                    py('compare.py', ref(), '{work}/out', '{work}/display.png')]
    R['baked'] = [run('--no-watch', '--frames', '20', '--size', '320x180', env={'BAKED_PROBE_OUT': '{work}/out.png'}),
                  py('urp/compare.py', single_ref('png'), '{work}/out.png', verdict='none')]
    R['shadergraph'] = [run('--no-watch', '--frames', '30', '--size', '320x180',
                            env={'SHADERGRAPH_PROBE_OUT': '{work}/out.png'}),
                        py('urp/compare.py', single_ref('png'), '{work}/out.png', verdict='none')]
    R['uitk'] = classic('UITK_PROBE_OUT', 'out.jsonl', ['--no-watch', '--frames', '30'],
                        [single_ref(), '{work}/out.jsonl'], verdict='exit')
    R['twod'] = classic('TWOD_PROBE_OUT', 'out.json', ['--no-watch', '--frames', '40'],
                        [single_ref('json'), '{work}/out.json'], verdict='exit')
    R['navmesh'] = classic('NAVMESH_PROBE_OUT', 'out.json', ['--no-watch', '--frames', '300'],
                           [single_ref('json'), '{work}/out.json'], verdict='exit')
    for name, variable, frames in [('urp', 'URP_PROBE_DIR', '30'), ('urp2d', 'URP2D_PROBE_DIR', '40')]:
        scenarios = sorted((PROJECTS/name/'Reference/scenarios').glob('*.png'))
        R[name] = [run('--no-watch', '--frames', frames, '--size', '320x180', env={variable: '{work}/out'})]
        R[name] += [py('urp/compare.py', str(png), '{work}/out/'+png.name, verdict='none') for png in scenarios]
    R['terrain'] = classic('TERRAIN_PROBE_OUT', 'out', ['--frames', '300', '--size', '320x180', '--no-watch'], images=True)
    R['lod'] = classic('LOD_PROBE_OUT', 'out', ['--frames', '20000', '--size', '320x180', '--no-watch'])
    R['assetbundles'] = classic('BUNDLE_PROBE_OUT', 'out', ['--frames', '600', '--size', '320x180', '--no-watch'],
                                images=True)
    R['video'] = classic('VIDEO_PROBE_OUT', 'out', ['--frames', '30000', '--size', '320x180', '--no-watch'], images=True)
    R['blendshapes'] = classic('BLEND_PROBE_OUT', 'out', ['--frames', '600', '--size', '320x180', '--no-watch'],
                               images=True)
    R['grass'] = classic('GRASS_PROBE_OUT', 'out', ['--frames', '600', '--size', '320x180', '--no-watch'], images=True)
    R['ambient'] = classic('AMBIENT_PROBE_OUT', 'out', ['--frames', '600', '--size', '320x180', '--no-watch'],
                           images=True)
    R['xr'] = classic('XR_PROBE_OUT', 'out', ['--frames', '300', '--size', '320x180', '--no-watch'])
    R['wind'] = classic('WIND_PROBE_OUT', 'out', ['--frames', '100000', '--size', '320x180', '--no-watch'])
    for name, variable in [('vehicles', 'VEHICLES_PROBE_OUT'), ('joints', 'JOINTS_PROBE_OUT'),
                           ('controller', 'CONTROLLER_PROBE_OUT'), ('cloth', 'CLOTH_PROBE_OUT'),
                           ('bundleanim', 'ANIM_PROBE_OUT')]:
        R[name] = classic(variable, 'out', headless)
    for name, variable, verdict in [('bundleaudio', 'AUDIO_PROBE_OUT', 'exit'),
                                    ('settings', 'SETTINGS_PROBE_OUT', 'exit'),
                                    ('editorui', 'EDITORUI_PROBE_OUT', 'exit')]:
        R[name] = classic(variable, 'out', [*headless, '--fixed-fps', '60'], verdict=verdict)
    gamepad = ('GAMEPAD_PROBE_OUT={work}/out {cli} run -p {probe} --headless 300000 --fixed-fps 60')
    R['gamepad'] = [Step('engine', ['sg', 'input', '-c', gamepad], launcher_kind='engine-cpu'),
                    py('compare.py', ref('gamepad.jsonl'), '{work}/out/gamepad.jsonl')]
    R['avoidance'] = []
    for config in sorted((PROJECTS/'avoidance/Configs').glob('*.txt')):
        scene = ['--scene', 'Assets/Scenes/Walls.unity'] if config.stem.startswith('walls-') else []
        R['avoidance'] += [run(*scene, '--headless', '100000', '--no-watch',
                               env={'AVOIDANCE_CONFIG': str(config), 'AVOIDANCE_OUT': f'{{work}}/{config.name}'}),
                           py('compare.py', ref(config.name), f'{{work}}/{config.name}')]
    for name, variable in [('animator', 'ANIMATOR_PROBE_OUT'), ('constraints', 'CONSTRAINT_PROBE_OUT'),
                           ('effectors', 'EFFECTORS_PROBE_OUT'), ('forces', 'FORCES_PROBE_OUT'),
                           ('joints2d', 'JOINTS2D_PROBE_OUT'), ('rayhits', 'RAYHITS_PROBE_OUT')]:
        R[name] = classic(variable, 'out', ['--headless', '30000', '--no-watch'], verdict='exit')
    R['particlestreams'] = classic('PARTICLE_STREAMS_OUT', 'out', ['--frames', '8', '--size', '320x180', '--no-watch'],
                                   verdict='exit')
    R['particlenoise'] = classic('PARTICLE_NOISE_OUT', 'out', ['--frames', '8', '--size', '320x180', '--no-watch'],
                                 verdict='exit')
    # Linear only: the Gamma reference needs the project switched to Gamma (README).
    R['uicolors'] = classic('UI_COLORS_OUT', 'out', ['--frames', '8', '--size', '128x128', '--no-watch'],
                            [ref('Linear'), '{work}/out'], verdict='exit')
    R['textcore'] = classic('TEXTCORE_PROBE_OUT', 'out.jsonl', ['--headless', '5', '--no-watch'],
                            [single_ref('jsonl.gz'), '{work}/out.jsonl'], verdict='exit')
    R['physicsdefaults'] = [run('--headless', '30000', '--no-watch', env={'PHYSICS_DEFAULTS_OUT': '{work}/out.jsonl'}),
                            py('verify.py', '--candidate', '{work}/out.jsonl')]
    # `unbound test` exits like the Editor: 2 when tests fail, as some do on purpose.
    R['testrunner'] = []
    for platform, stem in [('EditMode', 'editmode'), ('PlayMode', 'playmode')]:
        R['testrunner'] += [Step('engine', ['{cli}', 'test', '-p', '{probe}', '--platform', platform,
                                            '--results', f'{{work}}/{stem}.xml'], ok=(0, 2)),
                            py('compare.py', ref(f'{stem}-{{release}}.xml'), f'{{work}}/{stem}.xml')]
    R['networkstreaming'] = [
        driver('run-probe.py', '{work}/engine', '--', 'env', 'UNBOUND_MANAGED_DIR={managed}', '{cli}', 'run',
               '-p', '{probe}', '--scene', 'Assets/Scenes/Probe.unity', '--frames', '200000', '--size', '320x180',
               '--no-watch'),
        py('compare.py', single_ref(), '{work}/engine/records.jsonl')]
    # run.sh scripts whose engine modes hard-code an old executable: the same command, current build.
    R['canvaslifecycle'] = classic('CANVAS_LIFECYCLE_OUT', 'out', ['--frames', '24', '--size', '640x480', '--no-watch'],
                                   verdict='exit')
    # Linear only: Gamma and LinearMSAA need Editor-side project setting changes (README).
    R['uirectclipping'] = classic('RECT_CLIP_OUT', 'out', ['--frames', '24', '--size', '128x128', '--no-watch'],
                                  [ref('Linear'), '{work}/out'], verdict='exit')
    R['cameramsaa'] = []
    for profile, extra in [('Linear', {}), ('Gamma', {'CAMERA_MSAA_COLOR_SPACE': 'Gamma'}),
                           ('FocusedLinear', {'CAMERA_MSAA_FOCUSED': '1'}),
                           ('FocusedGamma', {'CAMERA_MSAA_FOCUSED': '1', 'CAMERA_MSAA_COLOR_SPACE': 'Gamma'})]:
        env = {'CAMERA_MSAA_OUTPUT': '{work}/'+profile, 'CAMERA_MSAA_CLI': '{cli}', 'UNBOUND_MANAGED_DIR': '{managed}',
               'UNBOUND_NATIVE_DIR': '{managed}', **extra}
        R['cameramsaa'] += [Step('driver', ['bash', '{probe}/run.sh', 'engine', 'suite'], env),
                            py('compare.py', '{probe}/Reference/unity-{release}/'+profile, '{work}/'+profile)]
    R['materialcolortransfer'] = []
    R['terrainlightmaps'] = []
    for space in ('Linear', 'Gamma'):
        R['materialcolortransfer'] += [
            Step('driver', ['bash', '{probe}/run.sh', 'engine', 'suite'],
                 {'MATERIAL_COLOR_TRANSFER_OUT': '{work}/'+space, 'MATERIAL_COLOR_TRANSFER_CLI': '{cli}',
                  'MATERIAL_COLOR_TRANSFER_COLOR_SPACE': space, 'UNBOUND_MANAGED_DIR': '{managed}'}),
            py('compare.py', '{probe}/Reference/unity-{release}/'+space, '{work}/'+space)]
        R['terrainlightmaps'] += [
            Step('driver', ['bash', '{probe}/run.sh', 'engine', 'suite', space],
                 {'TERRAIN_LIGHTMAP_OUT': '{work}/'+space, 'TERRAIN_LIGHTMAP_CLI': '{cli}',
                  'UNBOUND_MANAGED_DIR': '{managed}', 'UNBOUND_NATIVE_DIR': '{managed}'}),
            py('compare.py', '{probe}/Reference/unity-{release}/'+space, '{work}/'+space)]

    # run.py drivers: `unbound` mode into validation/runs/<probe>/<label>, then the comparator.
    jsonl = lambda name: (lambda runs: [single_ref(), f'{runs}/{name}'])
    folder = lambda runs: ['{probe}/Reference/unity-{release}', runs, '--report', '{work}/report.json']
    for name in ('activationorder', 'destroycontract', 'shaderpasscount', 'shaderkeywords'):
        R[name] = guarded(lambda runs: [single_ref(), runs, '--report', '{work}/report.json'])
    for name in ('animatoradditive', 'coreapis', 'editorcallbacks', 'humanoid', 'instantiateasync', 'modelclips',
                 'modelroot', 'navlinks'):
        R[name] = guarded(jsonl('values.jsonl'))
    R['droppedparams'] = guarded(jsonl('capture.jsonl'))
    R['spriteimport'] = guarded(jsonl('sprites.jsonl'))
    for name in ('appfacts', 'customrt', 'flares', 'gpubuffers', 'lines', 'rtdepth'):
        R[name] = guarded(folder)
    R['assetclones'] = guarded(lambda runs: ['{probe}/Reference/unity-{release}/Main.jsonl', f'{runs}/values.jsonl',
                                             '--report', '{work}/report.json'])
    R['lightprobes'] = guarded(lambda runs: ['{probe}/Reference/unity-{release}/probe.jsonl', f'{runs}/probe.jsonl'])
    # These drivers compare inside `unbound` mode and exit nonzero on a difference.
    for name in ('cullinggroup', 'inputframes'):
        R[name] = [driver('run.py', 'unbound', '--release', '{release}', '--label', '{label}',
                          env={'UNBOUND_CLI': '{cli}', 'UNBOUND_MANAGED_DIR': '{managed}'})]
        R[name][0].kind = 'compare'
        R[name][0].timeout = DRIVER_TIMEOUT
    R['screencapture'] = []
    for space in ('Linear', 'Gamma'):
        R['screencapture'] += [
            driver('run.py', 'unbound', '--release', '{release}', '--space', space, '--label', '{label}-'+space,
                   env={'UNBOUND_CLI': '{cli}', 'UNBOUND_MANAGED_DIR': '{managed}'}),
            py('compare.py', '{probe}/Reference/unity-{release}/'+space, f'{{runs}}-{space}/unbound',
               '--report', f'{{work}}/report-{space}.json')]
    R['indirectspec'] = []
    for profile in ('linear', 'gamma'):
        R['indirectspec'] += [
            driver('run.py', 'unbound', '--release', '{release}', '--label', '{label}-'+profile, '--profile', profile,
                   env={'UNBOUND_CLI': '{cli}', 'UNBOUND_MANAGED_DIR': '{managed}'}),
            py('compare.py', f'{{probe}}/Reference/unity-{{release}}/{profile}.jsonl',
               f'{{runs}}-{profile}/{profile}/unbound/indirectspec.jsonl', '--profile', profile,
               '--report', f'{{work}}/report-{profile}.json')]
    for name, comparator, profiles in [('modelimport', 'compare.py', ('Gamma', 'Linear', 'URP')),
                                       ('texturearrays', 'compare.py', ('Android', 'Gamma', 'Linear')),
                                       ('textureimport', 'compare.py', ('Android', 'Gamma', 'Linear')),
                                       ('texturepixels', 'texturearrays/compare.py', ('Gamma', 'Linear'))]:
        R[name] = [driver('run.py', 'unbound', '--release', '{release}', '--label', '{label}',
                          env={'UNBOUND_CLI': '{cli}', 'UNBOUND_MANAGED_DIR': '{managed}'})]
        R[name] += [py(comparator, f'{{probe}}/Reference/unity-{{release}}/{p}.jsonl', f'{{runs}}/unbound/{p}.jsonl')
                    for p in profiles]
    R['modelimport'].append(py('compare.py', '{probe}/Reference/unity-{release}/descriptions.txt',
                               '{runs}/unbound/Gamma.descriptions.txt'))
    R['humanfirstpose'] = [driver('run.py', 'unbound', '--release', '{release}', '--label', '{label}',
                                  '--frozen-runtime', '{runtime}'),
                           py('compare.py', single_ref(), '{runs}/unbound/values.jsonl',
                              '--json', '{work}/full-union.json')]
    R['textureraw'] = [driver('run.py', 'engine', '--release', '{release}', '--label', '{label}',
                              '--runtime', '{runtime}', '--manifest', '{runtime}/runtime-manifest.json'),
                       py('compare.py', single_ref(), '{runs}/engine.jsonl', '--report', '{work}/report.json')]

    # prepare/engine/verify drivers with an explicit work directory.
    for name in ('particleemission', 'particlelifetime', 'particleseedaxis', 'particletrigger',
                 'particletriggerboundary', 'particletriggerlifetime'):
        R[name] = particle_family()
    R['particleforcefield'] = particle_family('compare.py')
    R['particleforcefield'][-1].argv = ['{python}', '{probe}/compare.py', '--unity-version', '{release}',
                                        '--actual', '{work}/engine.jsonl', '--output', '{work}/report.json']
    R['geometryframe2d'] = [
        driver('run.py', 'prepare', '--unity-version', '{release}', '--work', '{work}', '--label', 'engine'),
        driver('run.py', 'engine', '--unity-version', '{release}', '--work', '{work}', '--label', 'engine',
               '--cli', '{cli}', env={'UNBOUND_MANAGED_DIR': '{managed}'}),
        py('verify.py', '--unity-version', '{release}', '--actual', '{work}/engine.json',
           '--report', '{work}/report.json')]
    R['colliderskin2d'] = query_family('SKIN_WORK')
    R['query2dties'] = query_family('QUERY_TIES_WORK')
    R['querycontactfeatures'] = query_family('QUERY_CONTACT_WORK')
    R['query2dcontinuation'] = query_family('QUERY_WORK')
    R['physics2ddynamics'] = physics2d('DYNAMICS', 'dynamics')
    R['physics2dmotion'] = physics2d('MOTION', 'motion')
    R['trigger2dtiming'] = trigger2d(['timing', 'order'])
    R['trigger2dlifecycle'] = trigger2d(['lifecycle', 'receivers'])
    for name in ('solverbodyorder', 'solverpairorder'):
        R[name] = [driver('run.py', 'prepare', '--release', '{release}', '--work', '{work}', '--label', 'engine'),
                   driver('run.py', 'engine', '--release', '{release}', '--work', '{work}', '--label', 'engine',
                          '--project', 'engine', '--runtime', '{runtime}'),
                   py('verify.py' if name == 'solverbodyorder' else 'public.py', '--release', '{release}',
                      '--candidate', '{work}/engine.json', '--output', '{work}/report.json')]
    R['quaternioneuler'] = [
        driver('run.py', 'prepare', '--release', '{release}', '--work', '{work}', '--label', 'engine'),
        driver('run.py', 'engine', '--release', '{release}', '--work', '{work}', '--label', 'engine',
               '--project', 'engine', '--runtime', '{runtime}'),
        py('run.py', 'compare', '--release', '{release}', '--work', '{work}', '--label', 'compare',
           '--candidate', '{work}/engine.json', '--output', '{work}/report.json')]
    R['captureclock'] = []
    for profile, duration, scale in [('slow', '3', '1'), ('fps60', '0.01666666753590107', '1'),
                                     ('fps60-half', '0.01666666753590107', '0.5'),
                                     ('dsp48000', '0.02133333310484886', '1'),
                                     ('dsp48000-scaled', '0.02133333310484886', '1.5')]:
        R['captureclock'] += [
            driver('run.py', 'engine', '--release', '{release}', '--work', '{work}', '--label', profile+'-engine',
                   '--duration', duration, '--scale', scale, '--cli', '{cli}', '--runtime', '{managed}'),
            py('verify.py', '--release', '{release}', '--profile', profile,
               '--candidate', f'{{work}}/{profile}-engine/capture.jsonl', '--report', f'{{work}}/{profile}.json')]
    R['trailautodestruct'] = [
        driver('run.py', 'engine', '--release', '{release}', '--work', '{work}', '--label', 'candidate',
               '--cli', '{cli}', '--runtime', '{managed}'),
        py('compare.py', single_ref(), '{work}/candidate/engine/events.jsonl', '--report', '{work}/report.json')]
    for name, capture in [('uitextmetrics', 'metrics.jsonl'), ('uitkpixels', ''), ('samplerbias', 'values.jsonl'),
                          ('uitransitioncallbacks', 'callbacks.jsonl'), ('uitransitioninline', 'inline.jsonl')]:
        candidate = '{work}/engine/capture' + ('/'+capture if capture else '')
        R[name] = [driver('run.py', 'engine', '--release', '{release}', '--output', '{work}/engine',
                          '--cli', '{cli}', '--managed', '{managed}'),
                   py('verify.py', '--release', '{release}', '--candidate', candidate, '--output', '{work}/report.json')]
    R['uitransitions'] = [driver('run.py', 'engine', '--release', '{release}', '--output', '{work}/engine',
                                 '--cli', '{cli}', '--managed', '{managed}'),
                          py('compare.py', single_ref(), '{work}/engine/capture/transitions.jsonl')]
    R['modules'] = []
    # editor-play is compared with the Play reference too (README).
    for scenario in ('compile', 'play', 'editor-play'):
        R['modules'] += [driver('run.py', 'engine', scenario, '--release', '{release}', '--label', 'engine-'+scenario,
                                '--work', '{work}', '--cli', '{cli}', '--runtime', '{managed}'),
                         py('compare.py', f'{{work}}/engine-{scenario}/result.json')]

    # Profiles with a reference that the recipe does not run; listed with the result.
    NOTES['uicolors'] = 'Gamma reference not run: it needs the project switched to Gamma in the Editor'
    NOTES['uirectclipping'] = 'Gamma and LinearMSAA references not run: they need Editor-side project settings'
    NOTES['contactevents3d'] = 'controls measured only: Native repeats differ in two process-dependent hashes'

    # Audio probes whose engine run needs the project their Native run authors in the Editor: the
    # committed authored set (tests/authored_project.py) assembled into the work directory.
    def native_project(name, sets):
        return [Step('driver', ['{python}', '{repo}/tests/authored_project.py', 'assemble', name, item,
                                '--release', '{release}', '--out', '{work}/native-'+item]) for item in sets]
    R['mixerecho'] = native_project('mixerecho', ['default'])
    for rate in ('48000', '44100'):
        R['mixerecho'] += [
            driver('engine.py', '--release', '{release}', '--rate', rate, '--native-project', '{work}/native-default',
                   '--runtime', '{runtime}', '--work', '{work}/engine-'+rate),
            py('compare.py', '--release', '{release}', '--rate', rate, '--candidate', f'{{work}}/engine-{rate}/capture',
               '--report', f'{{work}}/report-{rate}.jsonl.gz')]
    # Reference profile, authored set, engine.py flags, compare.py selection (README's profile table).
    filters = [('rate-48000', 'default', ['--rate', '48000', '--renderer', '--listener'], ['--rate', '48000']),
               ('rate-44100', 'default', ['--rate', '44100', '--renderer', '--listener'], ['--rate', '44100']),
               ('renderer-api', 'default', ['--api', '--renderer'], ['--profile', 'renderer-api']),
               ('startup', 'startup', ['--startup', '--renderer'], ['--profile', 'startup']),
               ('renderer-pcm', 'pcm', ['--pcm', '--listener', '--renderer'], ['--profile', 'renderer-pcm']),
               ('gain', 'gain', ['--gain', '--listener', '--renderer'], ['--profile', 'gain']),
               ('callbacks', 'callbacks', ['--callbacks', '--listener', '--renderer'], ['--profile', 'callbacks'])]
    R['mixerfilters'] = native_project('mixerfilters', sorted({item for _, item, _, _ in filters}))
    for profile, item, flags, select in filters:
        R['mixerfilters'] += [
            driver('engine.py', '--release', '{release}', *flags, '--native-project', '{work}/native-'+item,
                   '--runtime', '{runtime}', '--work', '{work}/engine-'+profile),
            py('compare.py', '--release', '{release}', *select, '--candidate', f'{{work}}/engine-{profile}/capture',
               '--report', f'{{work}}/report-{profile}.json')]
    suspend = [('suspension', 'default', [], rates) for rates in ('48000', '44100')]
    suspend += [(profile, profile, ['--'+profile], rate) for profile in ('submission', 'lifecycle')
                for rate in ('48000', '44100')]
    suspend.append(('generators', 'generators', ['--generators'], '48000'))
    R['mixersuspend'] = native_project('mixersuspend', sorted({item for _, item, _, _ in suspend}))
    for profile, item, flags, rate in suspend:
        label = f'{profile}-{rate}'
        R['mixersuspend'] += [
            driver('engine.py', '--release', '{release}', *flags, '--rate', rate, '--native-project',
                   '{work}/native-'+item, '--runtime', '{runtime}', '--work', '{work}/engine-'+label),
            py('compare.py', '--release', '{release}', '--rate', rate, '--profile', profile,
               '--candidate', f'{{work}}/engine-{label}/capture', '--report', f'{{work}}/report-{label}.jsonl.gz')]
    for name in ('audiogeneratorboundaries', 'audiogeneratorlifecycle'):
        R[name] = native_project(name, ['default']) + [
            driver('run.py', 'engine', '--release', '{release}', '--work', '{work}/engine', '--runtime', '{runtime}',
                   '--native-project', '{work}/native-default'),
            py('compare.py', '--release', '{release}', '--candidate', '{work}/engine/capture',
               '--report', '{work}/report.jsonl.gz')]
    # Every suite and contact-pairs profile that has a reference, with the README's original absolute
    # 1e-5 bound on physical data (--numeric). Controls are measured only: Native repeats themselves
    # differ in two process-dependent hash rows (README).
    R['contactevents3d'] = []
    for suite, profiles in [('contacts', range(4)), ('controls', [0]), ('geometry', [0]), ('capsule', [0]),
                            ('sweeps', [0])]:
        for profile in profiles:
            reference = f'{{probe}}/Reference/unity-{{release}}/{suite}' + (f'-p{profile}' if suite == 'contacts' else '')
            R['contactevents3d'] += [
                driver('run.py', 'engine', '--suite', suite, '--unity-version', '{release}', '--work', '{work}',
                       '--label', 'engine', '--profile', str(profile), '--cli', '{cli}',
                       env={'UNBOUND_MANAGED_DIR': '{managed}'}),
                py('compare.py', reference+'.jsonl', f'{{work}}/{suite}-engine-p{profile}.jsonl',
                   '--output', f'{{work}}/{suite}-p{profile}.json', *(() if suite == 'controls' else ('--numeric',)),
                   verdict='none' if suite == 'controls' else 'differences')]
    # report.py reads <probe>-<label>.json for each query suite from QUERY_WORK.
    env = {'QUERY_WORK': '{work}', 'SCENE_SETTINGS_CLI': '{cli}', 'UNBOUND_MANAGED_DIR': '{managed}'}
    R['query2dparity'] = []
    for suite in ('queries', 'geometry', 'selection', 'selection-fine', 'boundary', 'tests'):
        R['query2dparity'] += [driver('run.py', 'prepare', '--probe', suite, '--label', suite+'-final', env=env),
                               driver('run.py', 'engine', '--probe', suite, '--label', suite+'-final', env=env)]
    R['query2dparity'].append(py('report.py', '--work', '{work}', '--label', 'final', '--output', '{work}/reports'))
    # engine.py prepares the project; the README runs the engine with PARTICLES_IMAGE_DIR=<work>/capture.
    R['particletrailwitness'] = [
        driver('engine.py', '--release', '{release}', '--work', '{work}/engine'),
        Step('engine', ['{cli}', 'run', '-p', '{work}/engine/project', '--frames', '600', '--size', '320x180',
                        '--no-watch'], {'PARTICLES_IMAGE_DIR': '{work}/engine/capture'}),
        py('compare_public.py', '--release', '{release}', '--actual', '{work}/engine/capture/witness.jsonl',
           '--out', '{work}/report.json')]
    env = {'SCENE_WORK': '{work}', 'SCENE_SETTINGS_CLI': '{cli}', 'UNBOUND_MANAGED_DIR': '{managed}'}
    R['physicsscenesettings'] = [driver('run.py', 'prepare', env=env), driver('run.py', 'final', env=env),
                                 driver('contactpairs.py', 'final', env=env),
                                 driver('contactpairs.py', 'final', '--noise', env=env),
                                 py('verify.py', '--matrix-output', '{work}', env=env)]
    # The checked-in Assets are the project that `create` authors in the Editor: `checkout` copies them.
    env = {'LOADING_WORK': '{work}', 'SCENE_SETTINGS_CLI': '{cli}', 'UNBOUND_MANAGED_DIR': '{managed}'}
    R['inactive3dloading'] = [driver('run.py', 'checkout', env=env)]
    for order in ('transform-first', 'rigidbody-first'):
        R['inactive3dloading'] += [driver('run.py', 'prepare', '--order', order, '--label', 'final', env=env),
                                   driver('run.py', 'engine', '--order', order, '--label', 'final', env=env)]
    R['inactive3dloading'].append(py('verify.py', '--captures', '{work}', '--label', 'final'))
    R['oroorder'] = [driver('run.py', 'unbound', '--release', '{release}', '--label', '{label}',
                            env={'UNBOUND_CLI': '{cli}', 'UNBOUND_MANAGED_DIR': '{managed}'}),
                     py('compare.py', single_ref(), '{runs}/unbound/values.jsonl', '--report', '{work}/report.json')]
    return R


# --- verdicts -------------------------------------------------------------------------------------

def summary_verdict(text):
    """pass/fail from a print-only comparator's own counts, clause by clause; None without counts.

    A clause with 'N differ' passes when every such N is 0. Otherwise 'A/B ... match|within' passes
    when A equals B (comparators that accept expected differences count them as matched). A clause
    with only a mean (an image measurement) has no verdict: then the result is None unless another
    clause failed.
    """
    if missing_capture(text):
        return False
    verdicts = []
    for clause in re.split(r'[;\n]', text):
        differ = [int(n) for n in re.findall(r'\b(\d+) (?:record\(s\) )?differ\b', clause)]
        if differ:
            verdicts.append(all(n == 0 for n in differ))
            continue
        ratios = re.findall(r'\b(\d+)/(\d+)\b[^,;()]*?\b(?:match|within)', clause)
        verdicts += [int(a) >= int(b) for a, b in ratios]
        if not ratios and re.search(r'\bmean\b', clause):
            verdicts.append(None)  # an image measurement without a bound: no verdict
    if False in verdicts:
        return False
    return None if None in verdicts or not verdicts else True


def measurement(text):
    """The aggregate line of a comparator without a verdict: its last 'worst' line, else its first."""
    lines = [line.strip() for line in text.splitlines() if line.strip()]
    worst = [line for line in lines if re.search(r'\bworst\b', line)]
    return (worst[-1] if worst else lines[0] if lines else '')[:300]


def missing_capture(text):
    return bool(re.search(r'\bMISSING\b', text))


def differences_verdict(text):
    counts = [int(n) for n in re.findall(r"['\"]differences['\"]: (\d+)", text)]
    return bool(counts) and all(n == 0 for n in counts)


def last_lines(text, count=1):
    # A comparator that ends with an indented JSON report: summarize it on one line.
    start = text.rfind('\n{')
    if text.rstrip().endswith('}') and (start >= 0 or text.startswith('{')):
        try:
            return json.dumps(json.loads(text[start+1 if start >= 0 else 0:]), separators=(',', ':'))[:300]
        except ValueError:
            pass
    # Lines of only JSON brackets carry no information (comparators that print a JSON report).
    lines = [line.strip() for line in text.splitlines() if line.strip(' \t[]{},')
             and not re.fullmatch(r'/\S+', line.strip())]
    return ' | '.join(lines[-count:])[:300]


# --- running --------------------------------------------------------------------------------------

def substitute(value, values):
    if isinstance(value, dict):
        return {key: substitute(item, values) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return type(value)(substitute(item, values) for item in value)
    return value.format_map(values) if isinstance(value, str) else value


def run_step(step, values, log, base_env):
    """Exit code and output text of one step; the output also goes to log."""
    argv = substitute(step.argv, values)
    env = dict(base_env, **substitute(step.env, values))
    log.parent.mkdir(parents=True, exist_ok=True)
    if step.kind == 'identical':
        reference, capture = argv
        try:
            actual = Path(capture).read_text()
        except OSError as error:
            text = f'missing capture: {error}'
            log.write_text(text+'\n')
            return 1, text
        for old, new in step.replace:
            actual = actual.replace(old, new)
        same = Path(reference).read_text() == actual
        text = 'identical' if same else 'DIFFERENT'
        log.write_text(text+'\n')
        return (0 if same else 1), text
    if step.args_from:
        extra = subprocess.run(substitute(step.args_from, values), env=env, capture_output=True, text=True)
        if extra.returncode:
            log.write_text(extra.stdout + extra.stderr)
            return extra.returncode, 'args command failed: ' + last_lines(extra.stderr or extra.stdout)
        argv = [*argv, *extra.stdout.split()]
    if step.kind == 'engine':
        prefix = [str(LAUNCHER)] + (['--kind', step.launcher_kind] if step.launcher_kind else [])
        argv = [*prefix, 'timeout', str(step.timeout), *argv]
    with log.open('w') as out:
        out.write('$ ' + shlex.join(argv) + '\n')
        out.flush()
        try:
            # Own process group: a timeout stops the launcher, the engine and a driver's children.
            process = subprocess.Popen(argv, cwd=REPO, env=env, stdout=out, stderr=subprocess.STDOUT,
                                       stdin=subprocess.DEVNULL, start_new_session=True)
            with RUNNING_LOCK:
                RUNNING.add(process.pid)
            try:
                code = process.wait(timeout=step.timeout + 600)
            except subprocess.TimeoutExpired:
                stop_tree(process)
                out.write('\nsuite: step timed out\n')
                code = 124
            finally:
                with RUNNING_LOCK:
                    RUNNING.discard(process.pid)
        except OSError as error:
            out.write(f'\nsuite: {error}\n')
            code = 127
    text = log.read_text(errors='replace').split('\n', 1)[-1]
    return code, text


RUNNING = set()
RUNNING_LOCK = threading.Lock()


class Tree:
    """A step's process group and every descendant, held by pidfd so that a process that leaves
    the group (`timeout` makes its own) or is reparented when its parent dies is still reached."""

    def __init__(self, root):
        self.root, self.fds = root, {}

    def collect(self):
        parents = {}
        for stat in Path('/proc').glob('[0-9]*/stat'):
            try:
                fields = stat.read_text().rsplit(')', 1)[1].split()
            except (OSError, IndexError):
                continue
            parents.setdefault(int(fields[1]), []).append(int(stat.parent.name))
        pending = [self.root]
        while pending:
            pid = pending.pop()
            if pid not in self.fds:
                try:
                    self.fds[pid] = os.pidfd_open(pid)
                except OSError:
                    continue
            pending += parents.get(pid, [])

    def signal(self, number):
        self.collect()
        try:
            os.killpg(self.root, number)
        except OSError:
            pass
        for fd in self.fds.values():
            try:
                signal.pidfd_send_signal(fd, number)
            except OSError:
                pass

    def alive(self):
        alive = False
        for fd in self.fds.values():
            try:
                signal.pidfd_send_signal(fd, 0)
                alive = True
            except OSError:
                pass
        return alive

    def close(self):
        for fd in self.fds.values():
            os.close(fd)
        self.fds = {}


def stop_tree(process, grace=30):
    """SIGTERM to the step's whole tree; SIGKILL to whatever of it is left after the grace period."""
    tree = Tree(process.pid)
    try:
        tree.signal(signal.SIGTERM)
        deadline = time.time() + grace
        while time.time() < deadline:
            if process.poll() is not None and not tree.alive():
                break
            time.sleep(0.5)
        tree.signal(signal.SIGKILL)
        process.wait()
    finally:
        tree.close()


def stop_children(*_):
    """Steps run in their own process groups; stop them all when the suite itself is stopped."""
    with RUNNING_LOCK:
        groups = list(RUNNING)
    for pid in groups:
        tree = Tree(pid)
        tree.signal(signal.SIGTERM)
        tree.close()
    os._exit(130)


def run_probe(name, recipe, context):
    started = time.time()
    result = {'probe': name}
    if isinstance(recipe, NoRecipe):
        result.update(status='no recipe', reason=recipe.reason, summary=recipe.reason, seconds=0)
        return result
    work = context['work_root']/name
    if work.exists():
        shutil.rmtree(work)
    work.mkdir(parents=True)
    values = dict(context['values'], probe=str(PROJECTS/name), work=str(work),
                  runs=str(CACHE/'validation/runs'/name/context['values']['label']))
    steps, summaries, verdicts, status = [], [], [], None
    for index, step in enumerate(recipe, 1):
        log = work/f'step{index:02d}-{step.kind}.log'
        code, text = run_step(step, values, log, context['env'])
        record = {'kind': step.kind, 'exit': code, 'log': str(log),
                  'command': shlex.join(substitute(step.argv, values))[:400]}
        steps.append(record)
        if step.kind in ('compare', 'identical'):
            summary = measurement(text) if step.verdict == 'none' else last_lines(text)
            summaries.append(summary)
            if code not in step.ok or missing_capture(text):
                verdicts.append(False)
            elif step.verdict == 'summary':
                verdicts.append(summary_verdict(text))
            elif step.verdict == 'differences':
                verdicts.append(differences_verdict(text))
            elif step.verdict == 'none':
                verdicts.append(None)
            else:
                verdicts.append(True)
            record['verdict'] = verdicts[-1]
        elif code not in step.ok:
            status = 'fail'
            summaries.append(f'step {index} ({step.kind}) exited {code}: {last_lines(text)}')
            break
    if status is None:
        if any(v is False for v in verdicts):
            status = 'fail'
        elif any(v is None for v in verdicts):
            status = 'report'
        else:
            status = 'pass'
    if name in NOTES:
        summaries.append('partial: ' + NOTES[name])
        result['note'] = NOTES[name]
    result.update(status=status, seconds=round(time.time() - started, 1), summary=' || '.join(summaries)[:1000],
                  checks={'passed': sum(v is True for v in verdicts), 'failed': sum(v is False for v in verdicts),
                          'report': sum(v is None for v in verdicts), 'total': len(verdicts)},
                  steps=steps)
    return result


def discover():
    return sorted(path.name for path in PROJECTS.iterdir() if (path/'Reference').is_dir())


def managed_for(cli):
    if os.environ.get('UNBOUND_MANAGED_DIR'):
        return Path(os.environ['UNBOUND_MANAGED_DIR']).resolve()
    for candidate in (cli.parent/'managed', cli.parent.parent/'managed'):
        if (candidate/'Unbound.Host.dll').is_file():
            return candidate.resolve()
    raise SystemExit(f'suite: no managed directory for {cli}; set UNBOUND_MANAGED_DIR')


def make_runtime(root, cli, managed):
    """A runtime directory (unbound + managed/) with a hash manifest, for drivers that want one."""
    runtime = root/'runtime'
    runtime.mkdir(parents=True, exist_ok=True)
    (runtime/'unbound').symlink_to(cli)
    (runtime/'managed').symlink_to(managed)
    files = ['unbound'] + sorted('managed/'+str(f.relative_to(managed)) for f in managed.rglob('*') if f.is_file())
    manifest = {name: hashlib.sha256((runtime/name).read_bytes()).hexdigest() for name in files}
    (runtime/'runtime-manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
    return runtime


def default_cli():
    if os.environ.get('UNBOUND'):
        return Path(os.environ['UNBOUND'])
    selected = subprocess.run([sys.executable, str(REPO/'tools/build.py'), '--print-env'], cwd=REPO,
                              capture_output=True, text=True)
    if selected.returncode:
        return None
    return Path(json.loads(selected.stdout)['CARGO_TARGET_DIR'])/'release/unbound'


def promote(summary):
    baseline = AGENTS/'baseline'
    baseline.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(summary, baseline/'summary.json')
    print(f'suite: baseline is now {summary}')


def python_with_pil():
    """An interpreter that can import PIL and numpy (image comparators need them), else this one."""
    for candidate in (sys.executable, '/usr/bin/python3', shutil.which('python3')):
        if candidate and subprocess.run([candidate, '-c', 'import PIL, numpy'], capture_output=True).returncode == 0:
            return candidate
    return sys.executable


def default_jobs():
    try:
        return json.loads(LIMITS.read_text()).get('engine_slots', 2) + 2
    except (OSError, ValueError):
        return 4


FAILURE_COUNT = re.compile(r'''\b(\d+) (?:record\(s\) )?(?:differences|differ|failures|failed|missing|extra)\b'''
                           r'''|["']?(?:differences|failures|failed|missing|extra)["']?: (\d+)''')


def worse(old, new):
    """Why a probe that failed before fails worse now (None if it does not): a step that ran before
    fails now, fewer checks pass or more fail, or its comparators count more failures."""
    step = re.compile(r'^step (\d+) \(')
    old_step, new_step = step.match(old.get('summary', '')), step.match(new.get('summary', ''))
    if new_step and (not old_step or int(new_step[1]) < int(old_step[1])):
        return f'now fails at step {new_step[1]}'
    old_checks, new_checks = old.get('checks') or {}, new.get('checks') or {}
    if new_checks.get('failed', 0) > old_checks.get('failed', 0):
        return f'{new_checks["failed"]} failing checks, was {old_checks.get("failed", 0)}'
    if new_checks.get('passed', 0) < old_checks.get('passed', 0):
        return f'{new_checks["passed"]} passing checks, was {old_checks["passed"]}'
    count = lambda text: sum(int(a or b) for a, b in FAILURE_COUNT.findall(text or ''))
    if count(new.get('summary')) > count(old.get('summary')):
        return f'{count(new.get("summary"))} counted failures, was {count(old.get("summary"))}'
    return None


def compare_runs(current, previous):
    """Lines describing what changed since the previous summary; and whether a failure is new or
    an earlier failure got worse."""
    lines, regressed = [], False
    before = previous.get('probes', {})
    for name, result in sorted(current['probes'].items()):
        old = before.get(name)
        if old is None:
            lines.append(f'new       {name}: {result["status"]}')
            regressed |= result['status'] == 'fail'
        elif old['status'] != result['status']:
            lines.append(f'changed   {name}: {old["status"]} -> {result["status"]}')
            regressed |= result['status'] == 'fail'
        elif result['status'] == 'fail' and worse(old, result):
            lines.append(f'worse     {name}: {worse(old, result)}\n'
                         f'       was {old.get("summary", "")[:150]}\n'
                         f'       ->  {result.get("summary", "")[:150]}')
            regressed = True
        elif old.get('summary') != result.get('summary') and result['status'] != 'no recipe':
            lines.append(f'summary   {name}: {old.get("summary", "")[:150]}\n'
                         f'       ->  {result.get("summary", "")[:150]}')
    for name in sorted(set(before) - set(current['probes'])):
        lines.append(f'gone      {name} (was {before[name]["status"]})')
    return lines, regressed


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--cli', type=Path, help='the unbound executable (default: $UNBOUND, else this '
                        "checkout's release build)")
    parser.add_argument('--label', help='run name (default: date-time and short commit)')
    parser.add_argument('--release', default=os.environ.get('UNBOUND_PROBE_UNITY_VERSION', '6000.0.71f1'))
    parser.add_argument('--only', help='comma-separated probe names')
    parser.add_argument('--jobs', type=int, default=default_jobs())
    parser.add_argument('--against', type=Path, help='earlier summary.json or its directory '
                        '(default: ~/.cache/unbound/agents/baseline/ when it exists; "none" for no comparison)')
    parser.add_argument('--update-baseline', action='store_true', help='copy this summary to agents/baseline/')
    parser.add_argument('--promote', metavar='LABEL', help="make run LABEL's summary the baseline; run nothing")
    parser.add_argument('--list', action='store_true', help='print each probe and its recipe, run nothing')
    args = parser.parse_args()
    if args.promote:
        summary = AGENTS/'suite'/args.promote/'summary.json'
        if not summary.is_file():
            parser.error(f'no summary for run {args.promote}: {summary}')
        if json.loads(summary.read_text()).get('only'):
            parser.error(f'run {args.promote} ran only some probes (--only); promote a full run')
        promote(summary)
        return 0

    probes = discover()
    table = recipes(args.release)
    if args.only:
        wanted = args.only.split(',')
        unknown = sorted(set(wanted) - set(probes))
        if unknown:
            parser.error('not a probe with a Reference/: ' + ', '.join(unknown))
        probes = [name for name in probes if name in wanted]
    missing = NoRecipe('no recipe in tools/agents/suite.py yet')
    if args.list:
        for name in probes:
            recipe = table.get(name, missing)
            if isinstance(recipe, NoRecipe):
                print(f'{name}: no recipe: {recipe.reason}')
            else:
                print(f'{name}:')
                for step in recipe:
                    print(f'    {step.kind:8} {shlex.join(step.argv)[:200]}')
        return 0
    if args.update_baseline and args.only:
        parser.error('--update-baseline needs a full run, not --only')
    cli = args.cli or default_cli()
    if not cli:
        parser.error('give --cli or set UNBOUND')
    cli = cli.resolve()
    if not os.access(cli, os.X_OK):
        parser.error(f'not an executable: {cli}')
    managed = managed_for(cli)
    commit = subprocess.run(['git', 'rev-parse', '--short=10', 'HEAD'], cwd=REPO, capture_output=True,
                            text=True).stdout.strip()
    label = args.label or f'{datetime.datetime.now():%Y%m%d-%H%M%S}-{commit}'
    if Path(label).name != label:
        parser.error('label must be a single directory name')
    summary_dir = AGENTS/'suite'/label
    work_root = CACHE/'validation/suite'/label
    if summary_dir.exists() or work_root.exists():
        parser.error(f'label {label} was used already: choose another')
    work_root.mkdir(parents=True)
    summary_dir.mkdir(parents=True)
    runtime = make_runtime(work_root, cli, managed)
    env = dict(os.environ, UNBOUND=str(cli), UNBOUND_PROBE_UNITY_VERSION=args.release, PYTHONDONTWRITEBYTECODE='1')
    env.setdefault('DISPLAY', ':0')
    context = {'work_root': work_root, 'env': env,
               'values': {'cli': str(cli), 'managed': str(managed), 'runtime': str(runtime), 'repo': str(REPO),
                          'projects': str(PROJECTS), 'release': args.release, 'label': 'suite-'+label,
                          'python': python_with_pil()}}

    for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(number, stop_children)
    print(f'suite: {len(probes)} probes, {args.jobs} at a time; cli {cli}; work {work_root}', flush=True)
    started = time.time()
    results = {}
    lock = threading.Lock()
    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, args.jobs)) as pool:
        futures = {pool.submit(run_probe, name, table.get(name, missing), context): name for name in probes}
        for future in concurrent.futures.as_completed(futures):
            name = futures[future]
            try:
                result = future.result()
            except Exception as error:  # a broken recipe must not stop the other probes
                result = {'probe': name, 'status': 'fail', 'seconds': 0, 'summary': f'suite error: {error!r}'}
            with lock:
                results[name] = result
                print(f'{result["status"]:9} {name:26} {result["seconds"]:6.0f}s  {result["summary"][:220]}',
                      flush=True)

    counts = {status: sum(r['status'] == status for r in results.values())
              for status in ('pass', 'fail', 'report', 'no recipe')}
    summary = {'label': label, 'release': args.release, 'only': args.only, 'commit': commit, 'cli': str(cli), 'managed': str(managed),
               'work': str(work_root), 'started': datetime.datetime.fromtimestamp(started).isoformat(timespec='seconds'),
               'seconds': round(time.time() - started), 'counts': counts,
               'probes': {name: results[name] for name in sorted(results)}}
    (summary_dir/'summary.json').write_text(json.dumps(summary, indent=2)+'\n')
    print(f'suite: {counts["pass"]} pass, {counts["fail"]} fail, {counts["report"]} report, '
          f'{counts["no recipe"]} no recipe in {summary["seconds"]}s; {summary_dir/"summary.json"}', flush=True)

    against = args.against or (AGENTS/'baseline' if (AGENTS/'baseline/summary.json').is_file() else None)
    if against and str(against) == 'none':
        against = None
    regressed = counts['fail'] > 0
    if against:
        path = against/'summary.json' if against.is_dir() else against
        previous = json.loads(path.read_text())
        if args.only:
            previous['probes'] = {k: v for k, v in previous.get('probes', {}).items() if k in results}
        changes, regressed = compare_runs(summary, previous)
        print(f'suite: against {path}: ' + (f'{len(changes)} change(s)' if changes else 'no changes'))
        for line in changes:
            print('  ' + line)
    if args.update_baseline:
        promote(summary_dir/'summary.json')
    return 1 if regressed else 0


if __name__ == '__main__':
    raise SystemExit(main())
