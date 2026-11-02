#!/usr/bin/env python3
"""Synthetic controls of gate.py: no Editor. Each control is a capture directory under OUT.

    test_gate.py OUT [--release R]

`valid` (every predeclared key, plausible finite values, a clean exit-0 NVIDIA result, a
launch record of this checkout's witness) must pass. Every other control changes one thing and
must be refused for that reason: complete-only, a missing settled sample, a duplicate key, NaN in
a settled sample, plus a diagnostic error record, a shader error in the log, error-shader output
(all slots equal), the dump placeholder, a forced stop, a non-NVIDIA renderer, two markers, a
gamma result under the linear profile and a launch of another witness source.
"""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gate  # noqa: E402

RELEASE = '6000.0.71f1'


def valid_values(slots, settings, space='Linear'):
    rows = [('format', 'indirectspec 1'), ('activeColorSpace', space), ('graphicsDeviceType', 'OpenGLCore'),
            ('graphics activeTier', 'Tier3'), ('activeBuildTarget', 'StandaloneLinux64 Standalone'),
            ('tier standardShaderQuality', 'High'), ('lightingDataAsset', 'LightingData'),
            ('lightingData m_BakedAmbientProbeInLinear', '27: '+' '.join(['0.18028377']+['0']*26)),
            ('lightingData properties', 'm_Name m_BakedAmbientProbeInLinear'),
            ('defaultTexture', 'ReflectionProbe-0 Cubemap Cube 128x128 mips 8 RGBAHalf'),
            ('skybox', 'Default-Skybox shader Skybox/Procedural'), ('customReflectionTexture', 'null'),
            ('sun', 'null'),
            ('standard material', 'Standard keywords _GLOSSYREFLECTIONS_OFF _SPECULARHIGHLIGHTS_OFF '
             'color 1 1 1 1 metallic 0 glossiness 0'),
            ('custom SH', ' '.join(['0']*27))]

    def record(prefix, ambient, reflection):
        out = [(f'{prefix} RenderSettings', f'ambientMode Trilight ambientIntensity {gate.fmt(ambient)} '
                f'reflectionIntensity {gate.fmt(reflection)} defaultReflectionMode Skybox fog False'),
               (f'{prefix} ambientProbe', ' '.join(['0.1']*27)),
               (f'{prefix} defaultTextureHDRDecodeValues', '1 1 0 0'),
               (f'{prefix} activeColorSpace', space)]
        out += [(f'{prefix} dump {s}', f'{0.01*(i+1)} 0.2 0.3 1') for i, s in enumerate(slots)]
        return out
    rows += record('lights enabled', 1.0, 1.0)
    for name, ambient, reflection in settings:
        for when in ('immediate', 'settled'):
            prefix = f'{name} {when}'
            rows += record(prefix, ambient, reflection)
            rows += [(f'{prefix} standard bytes centre', '25 32 39 255'),
                     (f'{prefix} standard bytes histogram', '3000x 0 0 0 0; 1096x 25 32 39 255'),
                     (f'{prefix} standard floats centre', '0.0036 0.0045 0.0061 1'),
                     (f'{prefix} standard floats histogram', '3000x 0 0 0 0; 1096x 0.0036 0.0045 0.0061 1')]
    rows.append(('lights disabled', 'Lamp=False Point Light=False Sun=False'))
    return rows


def write_capture(directory, rows, *, marker_rows=1, result=None, log_extra='', witness=None,
                  color=b'm_ActiveColorSpace: 1'):
    directory.mkdir(parents=True)
    lines = [json.dumps({'k': k, 'v': v}, separators=(',', ':')) for k, v in rows]
    lines += ['{"k":"complete","v":true}']*marker_rows
    (directory/gate.OUT).write_text('\n'.join(lines)+'\n')
    base = {'exit_code': 0, 'forced_exit': None, 'forced_detail': None, 'signal': None,
            'renderer': 'NVIDIA GeForce RTX 4060/PCIe/SSE2', 'renderer_ok': True}
    base.update(result or {})
    (directory/'editor-run.json').write_text(json.dumps(base))
    source = witness if witness is not None else gate.WITNESS.read_bytes()
    settings = b'PlayerSettings:\n  '+color+b'\n'
    (directory/'prelaunch-inputs.json').write_text(json.dumps({'selected_release': RELEASE, 'files': {
        gate.WITNESS_KEY: {'sha256': hashlib.sha256(source).hexdigest()},
        'project/ProjectSettings/ProjectSettings.asset': {'sha256': hashlib.sha256(settings).hexdigest(),
                                                          'bytes_base64': base64.b64encode(settings).decode()}}}))
    (directory/'editor.log').write_text('Renderer: NVIDIA GeForce RTX 4060/PCIe/SSE2\n'+log_extra
                                        + 'indirectspec witness written\nUnityEngine.Debug:Log (object)\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('out', type=Path)
    args = parser.parse_args()
    if args.out.exists():
        sys.exit(f'refusing to reuse {args.out}')
    slots, settings = gate.witness_layout()
    shader = (gate.PROBE/'Assets/Resources/IndirectSpecDump.shader').read_text()
    if max(int(n) for n in __import__('re').findall(r'slot == (\d+)', shader))+1 != len(slots):
        sys.exit('the dump shader and the witness Slots disagree')
    rows = valid_values(slots, settings)
    settled = f'{settings[-1][0]} settled dump unity_IndirectSpecColor'
    controls = {
        'valid': (dict(rows=rows), None),
        'complete-only': (dict(rows=[]), 'missing key'),
        'missing-settled-sample': (dict(rows=[r for r in rows if r[0] != settled]), 'missing key'),
        'duplicate-key': (dict(rows=rows[:20]+[rows[19]]+rows[20:]), 'duplicate key'),
        'nan-settled': (dict(rows=[(k, 'NaN 0.2 0.3 1' if k == settled else v) for k, v in rows]),
                        'is not 4 finite floats'),
        'diagnostic-error-record': (dict(rows=rows+[('ambient 1 reflection 1 settled error',
                                                     'NullReferenceException: x')]), 'diagnostic error record'),
        'shader-error-log': (dict(rows=rows, log_extra="Shader error in 'Hidden/IndirectSpecDump': x at line 3\n"),
                             'Shader error in'),
        'error-shader-output': (dict(rows=[(k, '1 0 1 1' if ' dump ' in k and k.startswith(settings[0][0])
                                            else v) for k, v in rows]), 'every dump slot'),
        'placeholder-slot': (dict(rows=[(k, '-1 -1 -1 -1' if k == settled else v) for k, v in rows]),
                             'placeholder'),
        'forced-stop': (dict(rows=rows, result={'forced_exit': 'shutdown-guard'}), 'forced stop'),
        'wrong-renderer': (dict(rows=rows, result={'renderer': 'llvmpipe', 'renderer_ok': False}), 'renderer'),
        'two-markers': (dict(rows=rows, marker_rows=2), 'completion markers'),
        'wrong-color-space': (dict(rows=valid_values(slots, settings, 'Gamma')), 'the profile is Linear'),
        'other-witness-source': (dict(rows=rows, witness=b'// another source\n'), 'not this checkout'),
    }
    failures = 0
    report = {}
    for name, (spec, expected) in controls.items():
        directory = args.out/name
        write_capture(directory, **spec)
        found = gate.problems(directory, 'linear', RELEASE, (slots, settings))
        ok = (not found) if expected is None else any(expected in p for p in found)
        failures += not ok
        report[name] = {'expected': expected or 'pass', 'problems': found, 'ok': ok}
        print(f"{'ok  ' if ok else 'FAIL'} {name}: {'pass' if not found else found[0]}"
              + (f' (+{len(found)-1} more)' if len(found) > 1 else ''))
    (args.out/'controls.json').write_text(json.dumps(report, indent=1)+'\n')
    print(f'{len(controls)-failures}/{len(controls)} controls behave as declared')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
