#!/usr/bin/env python3
"""Measure the actual host build script with private outputs and real compilers.

Synthetic SDKs exercise invalidation without changing the authored native tree.
--real uses the repository's actual PhysX/NvCloth/shims, with no source edits.
The driver takes no lease; every preparation/build job takes one existing lease.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import statistics
import subprocess
import sys
import tomllib

import build_benchmark

REPO = Path(__file__).resolve().parents[2]
PHASE = re.compile(r'unbound-build: phase=(\S+) seconds=([\d.]+) result=(\S+)')


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def fixture(root, value):
    for directory in ['crates/unbound-host', 'managed', 'native/physx/source',
                      'native/physx/include', 'native/physx/pxshared/include',
                      'native/nvcloth/include/NvCloth/ps', 'native/nvcloth/extensions/include',
                      'native/nvcloth/extensions/src', 'native/nvcloth/src/ps/unix']:
        (root/directory).mkdir(parents=True, exist_ok=True)
    write(root/'native/physx/include/value.h', f'#define SDK_VALUE {value}\n')
    write(root/'native/physx/source/value.cpp', '#include "value.h"\nextern "C" int sdk_value(){return SDK_VALUE;}\n')
    write(root/'native/nvcloth/include/NvCloth/ps/PsAllocator.cpp', 'int allocator_fixture;\n')
    write(root/'native/nvcloth/src/ps/unix/PsUnixAtomic.cpp', 'int atomic_fixture;\n')
    write(root/'native/shim.h', '#define SHIM_VALUE 3\n')
    write(root/'native/unbound_physx.cpp', '''#include "shim.h"
#ifndef BONUS
#define BONUS 0
#endif
extern "C" int sdk_value();
extern "C" __attribute__((visibility("default"))) int native_value(){return sdk_value()+SHIM_VALUE+BONUS;}
''')
    write(root/'native/unbound_cloth.cpp', 'int cloth_fixture;\n')
    write(root/'managed/Fixture.cs', '// managed-only input\n')


def driver(out, env):
    root = out/'driver'
    write(root/'Cargo.toml', '''[package]
name="native-link-driver"
version="0.1.0"
edition="2024"
[workspace]
[dependencies]
cc={version="=1.9.0",features=["parallel"]}
blake3="1"
[[bin]]
name="native-link-driver"
path="src/build.rs"
''')
    (root/'src').mkdir(parents=True)
    shutil.copy2(REPO/'crates/unbound-host/build.rs', root/'src/build.rs')
    shutil.copytree(REPO/'crates/unbound-host/build', root/'src/build')
    command = [str(build_benchmark.LAUNCHER), '--kind', 'build', 'cargo', 'build', '--release', '--offline',
               '--manifest-path', str(root/'Cargo.toml'), '--target-dir', str(out/'driver-target')]
    with (out/'driver-build.log').open('x') as log:
        subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=300)
    return out/'driver-target/release/native-link-driver'


def library(target):
    return target/'managed'/('libunbound_physx.dylib' if sys.platform=='darwin' else 'libunbound_physx.so')


def verify(path, expected, real):
    # A separate process avoids dlopen returning a previously loaded library
    # after a same-path rebuild, and never overwrites a loaded mapping.
    name = 'ua3_version' if real else 'native_value'
    code = 'import ctypes,sys; print(getattr(ctypes.CDLL(sys.argv[1]),sys.argv[2])())'
    actual = int(subprocess.check_output([sys.executable, '-c', code, str(path), name], text=True))
    assert actual == expected, (path, actual, expected)
    return {'sha256': digest(path), 'value': actual, 'mtime_ns': path.stat().st_mtime_ns}


def cargo_controls(out, source, env):
    package=source/'crates/unbound-host'
    write(package/'Cargo.toml', '[package]\nname="native-cargo-control"\nversion="0.1.0"\nedition="2024"\n'+
          'build='+json.dumps(str(out/'driver/src/build.rs'))+'\n[workspace]\n'+
          '[build-dependencies]\ncc={version="=1.9.0",features=["parallel"]}\nblake3="1"\n')
    write(package/'src/lib.rs','// Cargo integration control\n')
    target=out/'cargo-target'
    env=dict(env,CARGO_TARGET_DIR=str(target),CARGO_BUILD_BUILD_DIR=str(target/'build'),UNBOUND_NATIVE_CACHE='1')
    reports=[]

    def one(name,expected,extra=None,want=None):
        path=library(target)
        previous=path.stat().st_mtime_ns if path.exists() else None
        command=[str(build_benchmark.LAUNCHER),'--kind','build','cargo','build','--release','--offline','-vv',
                 '--manifest-path',str(package/'Cargo.toml')]
        log=out/f'cargo-{name}.log'
        with log.open('x') as handle:
            result=subprocess.run(command,env=dict(env,**(extra or {})),stdout=handle,stderr=subprocess.STDOUT,timeout=300)
        assert result.returncode==0,log
        output=verify(path,expected,False)
        phases={n:{'seconds':float(s),'result':r} for n,s,r in PHASE.findall(log.read_text())}
        if want:
            assert phases['native-total']['result']==want,(name,phases)
        if want=='hit' or name=='noop':
            assert previous==output['mtime_ns'],'Cargo control rewrote an unchanged library'
        reports.append({'name':name,'output':output,'phases':phases,'command':command})
        write(out/'cargo-controls.json',json.dumps(reports,indent=2)+'\n')
        print('cargo-'+name,'checked',flush=True)

    one('prepare',14,want='built')
    one('noop',14)
    write(source/'managed/Fixture.cs','// second managed-only change\n')
    one('managed',14,want='hit')
    one('flags',16,{'CXXFLAGS':'-DBONUS=2'},want='built')
    one('flags-reset',14,want='built')
    library(target).unlink()
    one('missing-library',14,want='built')
    external=out/'external-include'
    write(external/'external.h','#define EXTERNAL_VALUE 4\n')
    shim=source/'native/unbound_physx.cpp'
    shim.write_text('#include "external.h"\n'+shim.read_text().replace('+BONUS;','+BONUS+EXTERNAL_VALUE;'))
    one('external-prepare',18,{'CPATH':str(external)},want='built')
    write(external/'external.h','#define EXTERNAL_VALUE 6\n')
    one('external-edit',20,{'CPATH':str(external)},want='built')


def main():
    def interrupted(number, frame):
        raise KeyboardInterrupt('benchmark cancelled')
    signal.signal(signal.SIGTERM,interrupted)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--real', action='store_true')
    parser.add_argument('--seed-native-out', type=Path, help='Read-only SDK archives to copy, not Cargo state')
    parser.add_argument('--sessions', type=int, default=4)
    parser.add_argument('--workers', type=int, choices=[1, 2], default=2)
    parser.add_argument('--repeats', type=int, default=3)
    parser.add_argument('--compiler-cache', action='store_true', help='also use installed kache for C++')
    args = parser.parse_args()
    assert 1 <= args.sessions <= 8 and args.repeats >= 1
    out = args.out.expanduser().resolve()
    out.mkdir(parents=True, exist_ok=False)
    env = dict(os.environ, PATH=str(Path.home()/'.cargo/bin')+os.pathsep+os.environ['PATH'],
               RUSTUP_TOOLCHAIN=tomllib.loads((REPO/'rust-toolchain.toml').read_text())['toolchain']['channel'],
               RUSTC_WRAPPER='', CARGO_BUILD_JOBS='6')
    env.pop('CARGO_BUILD_BUILD_DIR', None)
    executable = driver(out, env)
    host = subprocess.check_output(['rustc', '-vV'], env=env, text=True).split('host: ')[1].splitlines()[0]
    env.update(HOST=host, TARGET=host, CARGO_CFG_TARGET_OS='macos' if sys.platform=='darwin' else 'linux',
               CARGO_CFG_TARGET_ARCH=host.split('-')[0], OPT_LEVEL='2', DEBUG='false', PROFILE='release',
               NUM_JOBS='6', UNBOUND_SKIP_MANAGED_BUILD='1', UNBOUND_BUILD_TIMINGS='1')
    if args.compiler_cache:
        env.update(RUSTC_WRAPPER=str(Path.home()/'.cache/unbound/tools/kache-2.0.0/kache'),
                   KACHE_LOCAL_ONLY='1', KACHE_CACHE_DIR=str(out/'compiler-cache'), KACHE_BUILD_SCRIPT_CACHE='0')
    else:
        env['CC'], env['CXX'] = 'cc', 'c++'
    targets, sources = {}, []
    for index in range(args.sessions):
        source = REPO if args.real else out/'sources'/f'session{index}'
        if not args.real:
            fixture(source, 7+index)
        sources.append(source)
        for mode in ['baseline', 'cached']:
            target = out/'targets'/mode/f'session{index}'
            (target/'native-out').mkdir(parents=True)
            if args.seed_native_out:
                for archive in args.seed_native_out.glob('*-*/libunbound*_sdk.a'):
                    destination = target/'native-out'/archive.parent.name/archive.name
                    destination.parent.mkdir(exist_ok=True)
                    shutil.copy2(archive, destination)
                    assert digest(archive)==digest(destination)
            targets[mode, index] = target
    reports = []

    def batch(label, mode, indices=None, extra=None, expected=None, want=None, failure=False):
        indices = list(range(args.sessions)) if indices is None else indices
        jobs = []
        previous = {}
        for index in indices:
            target, source = targets[mode, index], sources[index]
            path = library(target)
            if path.exists():
                previous[index] = (digest(path), path.stat().st_mtime_ns)
            job_env = dict(env, CARGO_MANIFEST_DIR=str(source/'crates/unbound-host'),
                           CARGO_TARGET_DIR=str(target), OUT_DIR=str(target/'native-out'),
                           UNBOUND_NATIVE_CACHE='0' if mode=='baseline' else '1', **(extra or {}))
            jobs.append(dict(name=f'session{index}', cwd=str(source/'crates/unbound-host'),
                             resource_key=str(target), command=[str(executable)], env=job_env, timeout_s=1200))
        report = build_benchmark.run_case(dict(name=label, parallelism=args.workers, jobs=jobs), out/label)
        report['mode'] = mode
        for index, job in zip(indices, report['jobs']):
            text = (out/label/(job['name']+'.log')).read_text()
            job['phases'] = {name: {'seconds': float(seconds), 'result': result}
                             for name, seconds, result in PHASE.findall(text)}
            if failure:
                assert job['launcher_exit'] != 0, 'invalid native input accepted'
                continue
            assert job['launcher_exit']==0, f'failed job: {out/label}'
            job['output'] = verify(library(targets[mode,index]), expected if expected is not None else
                                   (9 if args.real else 10+index), args.real)
            if want:
                assert job['phases']['native-total']['result']==want, (label, job['phases'])
            if want=='hit':
                assert previous[index]==(job['output']['sha256'],job['output']['mtime_ns']), 'hit rewrote native output'
                assert 'shim-compile' not in job['phases'] and 'native-link' not in job['phases']
        reports.append(report)
        write(out/'results.json', json.dumps(reports, indent=2)+'\n')
        print(label, round(report['wall_s'],3), 's, checked', flush=True)
        return report

    batch('prepare-baseline', 'baseline', want='built')
    batch('prepare-cached', 'cached', want='built')
    for repeat in range(args.repeats):
        for mode in (['baseline','cached'] if repeat%2==0 else ['cached','baseline']):
            batch(f'warm-{repeat}-{mode}', mode, want='hit' if mode=='cached' else 'built')
    if not args.real:
        source, target = sources[0], targets['cached',0]
        write(source/'managed/Fixture.cs', '// managed-only change\n')
        batch('managed-edit','cached',[0],want='hit')
        header = source/'native/shim.h'
        stamp = header.stat()
        header.write_text('#define SHIM_VALUE 5\n')
        os.utime(header,ns=(stamp.st_atime_ns,stamp.st_mtime_ns))
        batch('header-same-mtime','cached',[0],expected=12,want='built')
        batch('changed-header-hit','cached',[0],expected=12,want='hit')
        batch('flags','cached',[0],extra={'CXXFLAGS':'-DBONUS=2'},expected=14,want='built')
        batch('flags-reset','cached',[0],expected=12,want='built')
        path=library(target)
        path.unlink()
        batch('missing-library','cached',[0],expected=12,want='built')
        path.write_bytes(b'corrupted output')
        batch('corrupt-library','cached',[0],expected=12,want='built')
        write(target/'native-out/native-link-v1.receipt','bad receipt\n')
        batch('corrupt-receipt','cached',[0],expected=12,want='built')
        shim=source/'native/unbound_physx.cpp'
        original=shim.read_text()
        shim.write_text(original+'\n#error negative native control\n')
        batch('compile-error','cached',[0],failure=True)
        shim.write_text(original)
        batch('restored-input','cached',[0],expected=12,want='hit')
        write(source/'native/physx/source/value.cpp','#include "value.h"\nextern "C" int sdk_value(){return SDK_VALUE+2;}\n')
        batch('sdk-source','cached',[0],expected=14,want='built')
        batch('sdk-source-hit','cached',[0],expected=14,want='hit')
        text=(out/'sdk-source-hit/session0.log').read_text()
        archive=next(Path(line.split('=',1)[1]) for line in text.splitlines()
                     if line.startswith('cargo:rerun-if-changed=') and line.endswith('libunbound_physx_sdk.a'))
        original_archive=archive.read_bytes()
        try:
            archive.write_bytes(b'corrupted SDK archive')
            batch('link-error','cached',[0],failure=True)
            assert not (target/'native-out/native-link-v1.receipt').exists(), 'failed link retained a success receipt'
        finally:
            archive.write_bytes(original_archive)
        batch('link-recovery','cached',[0],expected=14,want='built')
        batch('link-recovery-hit','cached',[0],expected=14,want='hit')
        # Same-path compiler replacement changes the driver bytes, even when
        # its --version and generated code remain identical.
        compiler=out/'compiler'
        write(compiler,'#!/bin/sh\nexec c++ "$@"\n');compiler.chmod(0o755)
        batch('compiler-path','cached',[0],extra={'CXX':str(compiler)},expected=14,want='built')
        compiler.write_text('#!/bin/sh\n# changed compiler wrapper\nexec c++ "$@"\n')
        batch('compiler-bytes','cached',[0],extra={'CXX':str(compiler)},expected=14,want='built')
        cargo_controls(out,source,env)
    summary={mode:statistics.median(r['wall_s'] for r in reports if r['mode']==mode and r['name'].startswith('warm-'))
             for mode in ['baseline','cached']}
    write(out/'summary.json',json.dumps(summary,indent=2)+'\n')
    write(out/'metadata.json',json.dumps({'real':args.real,'sessions':args.sessions,'workers':args.workers,
        'repeats':args.repeats,'compiler_cache':args.compiler_cache,'host':host,
        'build_rs_sha256':digest(REPO/'crates/unbound-host/build.rs'),
        'native_cache_sha256':digest(REPO/'crates/unbound-host/build/native_cache.rs')},indent=2)+'\n')
    print(json.dumps(summary),flush=True)


if __name__=='__main__':
    main()
