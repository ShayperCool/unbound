#!/usr/bin/env python3
"""Recompile measured SDK units into a private copy of a matching archive, then link the shim.

Run under with-heavy-job. Shared archives and build outputs are read-only inputs.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--physx', type=Path, required=True)
    parser.add_argument('--nvcloth', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--shim', type=Path, help='Optional private diagnostic shim source')
    parser.add_argument('--unit', action='append', default=[])
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    sdk = root / 'native/physx'
    includes = sorted(d for d in (sdk / 'source').rglob('*') if d.is_dir())
    includes += [sdk / 'source', sdk / 'include', sdk / 'pxshared/include']
    flags = ['-std=c++14', '-O2', '-fPIC', '-fno-rtti', '-fno-exceptions',
             '-fno-strict-aliasing', '-ffp-contract=off', '-fvisibility=hidden',
             '-DNDEBUG', '-DPX_SUPPORT_PVD=0', '-DPX_PHYSX_STATIC_LIB',
             '-DDISABLE_CUDA_PHYSX', '-DPX_NVTX=0', '-DPX_COOKING']
    flags += ['-I' + str(d) for d in includes]
    args.output.mkdir(parents=True, exist_ok=True)
    archive = args.output / 'libunbound_physx_sdk.a'
    shutil.copy2(args.physx, archive)
    members = subprocess.check_output(['ar', 't', str(archive)], text=True).splitlines()
    commands = []
    sources = [args.shim or root / 'native/unbound_physx.cpp', root / 'native/unbound_cloth.cpp']
    for unit in args.unit:
        source = sdk / unit
        choices = [m for m in members if m.endswith('-' + source.stem + '.o')]
        if len(choices) != 1:
            raise ValueError(f'expected one archive member for {unit}: {choices}')
        obj = args.output / choices[0]
        command = ['c++', *flags, '-c', str(source), '-o', str(obj)]
        subprocess.run(command, check=True); commands.append(command)
        command = ['ar', 'r', str(archive), str(obj)]
        subprocess.run(command, check=True); commands.append(command)
        sources.append(source)
    subprocess.run(['ar', 's', str(archive)], check=True)
    cloth = root / 'native/nvcloth'
    cloth_flags = ['-I' + str(cloth / p) for p in ['include', 'extensions/include', 'src', 'extensions/src']]
    command = ['c++', *flags, *cloth_flags, '-DNV_CLOTH_ENABLE_CUDA=0', '-DNV_CLOTH_ENABLE_DX11=0',
               '-shared', str(sources[0]), str(sources[1]), str(archive), str(args.nvcloth),
               '-lpthread', '-ldl', '-lm', '-o', str(args.output / 'libunbound_physx.so')]
    subprocess.run(command, check=True); commands.append(command)
    digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    inputs = [args.physx, args.nvcloth, *sources]
    result = dict(commands=commands, inputs_sha256={str(p): digest(p) for p in inputs},
                  output_sha256=digest(args.output / 'libunbound_physx.so'))
    (args.output / 'build.json').write_text(json.dumps(result, indent=2) + '\n')


if __name__ == '__main__':
    main()
