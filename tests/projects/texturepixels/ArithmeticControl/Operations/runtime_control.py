#!/usr/bin/env python3
"""Run the same arithmetic assembly under explicit Mono and CoreCLR modes."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[4]
sys.path.insert(0, str(REPO / 'tests'))
from probe_paths import run_root


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--mono', required=True, type=Path)
    p.add_argument('--mcs', required=True, type=Path)
    p.add_argument('--dotnet', required=True, type=Path)
    p.add_argument('--framework-version', required=True)
    p.add_argument('--label', default=str(time.time_ns()))
    a = p.parse_args()
    work = run_root(PROBE, 'runtime-' + a.label)
    work.mkdir(parents=True)
    assembly = work / 'RuntimeControl.exe'
    lease = [str(Path.home() / '.cache/compat/with-heavy-job'), '--kind', 'test']

    def execute(name, args, extra_env=None):
        import os
        with (work / (name + '.log')).open('w') as log:
            result = subprocess.run(lease + args, stdout=log, stderr=subprocess.STDOUT,
                                    env=dict(os.environ, **(extra_env or {})))
        print(name + ': exit ' + str(result.returncode))
        print((work / (name + '.log')).read_text(), end='')
        if result.returncode:
            raise SystemExit(result.returncode)

    execute('compile', [str(a.mono), str(a.mcs), '-optimize+', '-out:' + str(assembly),
                        str(PROBE / 'RuntimeControl.cs')])
    for name, flags in [('mono-default', []), ('mono-single', ['--optimize=float32']),
                        ('mono-wide', ['--optimize=-float32']), ('mono-interpreter', ['--interpreter'])]:
        execute(name, [str(a.mono), *flags, str(assembly)])
    assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
        'tfm': 'net' + a.framework_version.split('.')[0] + '.0',
        'framework': {'name': 'Microsoft.NETCore.App', 'version': a.framework_version}}}))
    for name, env in [('core-default', {}), ('core-no-tiering', {'DOTNET_TieredCompilation': '0'}),
                      ('core-minopts', {'DOTNET_JitMinOpts': '1'})]:
        execute(name, [str(a.dotnet), str(assembly)], env)
    print('logs: ' + str(work))


if __name__ == '__main__':
    main()
