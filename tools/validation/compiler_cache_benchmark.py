#!/usr/bin/env python3
"""Real Rust/C++ compiles in isolated synthetic sessions, with output checks.

Uses the installed build pool, never an AI provider. Outputs belong in a NEW
cache directory. Each cold/warm case has new sources, targets and intermediates.
The cache alone survives between cases. No target copying or Cargo freshness reuse.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import socket
import statistics
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build as build_tool
import build_benchmark


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value)


def fixture(root, units):
    write(root/'Cargo.toml', '''[package]
name = "cache-synthetic"
version = "0.1.0"
edition = "2024"
[build-dependencies]
cc = "=1.9.0"
[profile.release]
opt-level = 2
debug = false
incremental = false
''')
    write(root/'build.rs', '''fn main() {
    // unbound-assets uses this compile-time path to find native sources.
    assert_eq!(env!("CARGO_MANIFEST_DIR"), std::env::var("CARGO_MANIFEST_DIR").unwrap());
    println!("cargo:rerun-if-changed=native");
    let mut b = cc::Build::new();
    b.cpp(true).std("c++14").opt_level(2).debug(false);
    for i in 0..UNITS { b.file(format!("native/unit{i}.cpp")); }
    b.compile("synthetic");
    // Models unbound-host's required side effect outside OUT_DIR.
    let target = std::env::var("CARGO_TARGET_DIR").unwrap();
    std::fs::write(std::path::Path::new(&target).join("side-effect.txt"), b"required").unwrap();
}
'''.replace('UNITS', str(units)))
    write(root/'native/value.h', '#define VALUE 7\n#ifndef FLAG\n#define FLAG 0\n#endif\n')
    for unit in range(units):
        # Instantiate real optimization work. No sleeps in measured jobs.
        cpp = ['#include <array>\n#include <algorithm>\n#include "value.h"\n',
               'template<int N> __attribute__((noinline)) unsigned long long mix(unsigned long long x) {\n',
               'std::array<unsigned long long,N> a{};\n',
               'for (int i=0;i<N;++i) a[i]=(x+i)*6364136223846793005ULL;\n',
               'std::sort(a.begin(),a.end()); return a[N/2]; }\n']
        cpp += [f'extern "C" unsigned long long native{unit}() {{ return VALUE + FLAG; }}\n']
        cpp += [f'extern "C" unsigned long long work{unit}_{i}(unsigned long long x) {{ return mix<{i+8}>(x); }}\n'
                for i in range(32)]
        write(root/f'native/unit{unit}.cpp', ''.join(cpp))
    rust = []
    for unit in range(units):
        rust.append(f'pub mod unit{unit};\n')
        functions = []
        for i in range(100):
            functions.append(f'#[inline(never)] pub fn work{i}(mut x:u64)->u64 {{\n')
            functions += [f'x = x.wrapping_mul({6364136223846793005+j*2}).rotate_left({j%63+1}) ^ {i+j};\n'
                          for j in range(24)]
            functions.append('x }\n')
        write(root/f'src/unit{unit}.rs', ''.join(functions))
    rust.append('pub fn value()->u64 { 11 }\n')
    write(root/'src/lib.rs', ''.join(rust))
    write(root/'src/main.rs', '''unsafe extern "C" { fn native0()->u64; }
fn main() { println!("{}", cache_synthetic::value()+unsafe { native0() }+u64::from(cfg!(cache_flag))); }
''')
    env = dict(os.environ, PATH=str(Path.home()/'.cargo/bin')+os.pathsep+os.environ.get('PATH', ''))
    subprocess.run(['cargo', 'generate-lockfile', '--offline'], cwd=root, env=env,
                   check=True, capture_output=True)


def check_job(source, target, expected):
    binary = target/'release/cache-synthetic'
    actual = subprocess.check_output([str(binary)], text=True).strip()
    assert actual == str(expected), (source, actual, expected)
    assert (target/'side-effect.txt').read_text() == 'required', 'build.rs side effect lost'
    # Cargo must have compiled this source tree; build outputs must be private.
    depfiles = list((target/'build/release/deps').glob('cache_synthetic-*.d'))
    assert depfiles, 'no private dep-info'
    # rustc emits relative source names when Cargo invokes it from the package.
    assert any('src/lib.rs' in p.read_text() for p in depfiles), 'missing source dep-info'
    for p in depfiles:
        text = p.read_text()
        assert str(target) in text, 'dep-info points at a foreign target'
        assert '/sources/' not in text or str(source) in text, 'foreign source dep-info'
    for directory in (target/'build/release/build').glob('*/out'):
        assert directory.resolve().is_relative_to(target), 'shared OUT_DIR'
    return {'stdout': actual, 'sha256': hashlib.sha256(binary.read_bytes()).hexdigest(),
            'side_effect': True, 'private_dep_info': True}


def stats(backend, env):
    if backend == 'none':
        return {}
    command = [env['RUSTC_WRAPPER'], 'stats', '--json'] if backend == 'kache' else [
        env['RUSTC_WRAPPER'], '--show-stats', '--stats-format=json']
    result = subprocess.run(command, env=env, text=True, capture_output=True, check=True, timeout=45)
    return json.loads(result.stdout)


def run(args):
    if 'sccache' in args.backends:
        with socket.socket() as probe:
            if probe.connect_ex(('127.0.0.1', args.sccache_port)) == 0:
                raise ValueError('benchmark sccache port is in use; choose a private --sccache-port')
    out = args.out.expanduser().resolve()
    out.mkdir(parents=True, exist_ok=False)
    donor = out/'fixture'
    fixture(donor, args.units)
    cases = []
    metadata = {'toolchain': subprocess.check_output([str(Path.home()/'.cargo/bin/rustc'), '-V'], text=True).strip(),
                'units': args.units, 'sessions': args.sessions, 'workers': args.workers,
                'repeats': args.repeats, 'backends': args.backends,
                'source_sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    metadata['build_wrapper_sha256'] = hashlib.sha256(Path(build_tool.__file__).read_bytes()).hexdigest()
    write(out/'metadata.json', json.dumps(metadata, indent=2)+'\n')
    # An entire backend is cold once. Alternate warm case order across repeats.
    phases = [('cold', 0, b) for b in args.backends]
    phases += [('warm', r, b) for r in range(args.repeats)
               for b in (args.backends if r % 2 == 0 else list(reversed(args.backends)))]
    phases += [(change, 0, b) for change in ['rust-edit', 'header-edit', 'divergent', 'flags', 'error'] for b in args.backends]
    environments = {}
    try:
        for phase, repeat, backend in phases:
            name = f'{phase}-{repeat}-{backend}'
            jobs, checks = [], []
            for index in range(args.sessions):
                source = out/'sources'/name/f'session{index}'
                shutil.copytree(donor, source)
                target = out/'targets'/name/f'session{index}'
                env = dict(os.environ, CARGO_TARGET_DIR=str(target))
                for key in list(env):
                    if key.startswith(('KACHE_', 'SCCACHE_')) or key in (
                            'CC', 'CXX', 'RUSTC_WRAPPER', 'RUSTC_WORKSPACE_WRAPPER', 'RUSTFLAGS', 'CARGO_ENCODED_RUSTFLAGS'):
                        env.pop(key)
                env, _ = build_tool.environment(source, out/'stores', backend, env)
                # Every backend gets an isolated store. Do not reset a live server.
                env['SCCACHE_DIR'] = str(out/'stores/sccache')
                env['SCCACHE_SERVER_PORT'] = str(args.sccache_port)
                env['SCCACHE_CACHE_SIZE'] = '2G'
                env['SCCACHE_BASEDIRS'] = str(source)
                env['KACHE_BASE_DIR'] = str(source)
                if phase == 'rust-edit':
                    p = source/'src/lib.rs'
                    p.write_text(p.read_text().replace('value()->u64 { 11 }', 'value()->u64 { 13 }'))
                if phase in ('header-edit', 'divergent'):
                    p = source/'native/value.h'
                    stamp = p.stat()
                    p.write_text(p.read_text().replace('VALUE 7', f'VALUE {9 if phase=="header-edit" else index}'))
                    os.utime(p, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
                if phase == 'flags':
                    env['RUSTFLAGS'] = '--cfg cache_flag'
                    env['CXXFLAGS'] = '-DFLAG=2'
                if phase == 'error':
                    with (source/'src/lib.rs').open('a') as handle:
                        handle.write('\ncompile_error!("cache negative control");\n')
                jobs.append({'name': f'session{index}', 'cwd': str(source),
                             'resource_key': str(target), 'env': env, 'timeout_s': 300,
                             'command': ['cargo', 'build', '--release', '--locked', '--offline']})
                expected = 20 if phase in ('rust-edit', 'header-edit') else 21 if phase=='flags' else 11+index if phase=='divergent' else 18
                checks.append((source, target, expected))
                environments[backend] = env
            before = stats(backend, env)
            case = {'name': name, 'parallelism': args.workers, 'jobs': jobs}
            report = build_benchmark.run_case(case, out/name)
            report['phase'], report['backend'], report['repeat'] = phase, backend, repeat
            report['stats_before'], report['stats_after'] = before, stats(backend, env)
            if phase == 'error':
                assert all(j['launcher_exit'] != 0 for j in report['jobs']), 'cached success hid compile error'
                assert all('cache negative control' in (out/name/(j['name']+'.log')).read_text()
                           for j in report['jobs']), 'missing compiler diagnostic'
            else:
                assert len(report['jobs']) == args.sessions
                assert all(j['launcher_exit'] == 0 for j in report['jobs']), f'failed build: {out/name}'
                report['outputs'] = [check_job(*check) for check in checks]
            cases.append(report)
            write(out/'results.json', json.dumps({'metadata': metadata, 'cases': cases}, indent=2)+'\n')
            print(name, round(report['wall_s'], 3), 's, outputs checked', flush=True)
    finally:
        for backend, env in environments.items():
            if backend == 'kache':
                subprocess.run([env['RUSTC_WRAPPER'], 'daemon', 'stop'], env=env, capture_output=True)
            elif backend == 'sccache':
                subprocess.run([env['RUSTC_WRAPPER'], '--stop-server'], env=env, capture_output=True)
    summary = {b: statistics.median(c['wall_s'] for c in cases if c['backend']==b and c['phase']=='warm')
               for b in args.backends}
    write(out/'summary.json', json.dumps(summary, indent=2)+'\n')
    print(json.dumps({'warm_median_batch_s': summary}, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--backends', nargs='+', choices=['none', 'sccache', 'kache'], default=['none', 'sccache', 'kache'])
    parser.add_argument('--sessions', type=int, default=4)
    parser.add_argument('--workers', type=int, choices=[1, 2], default=2)
    parser.add_argument('--units', type=int, default=4)
    parser.add_argument('--repeats', type=int, default=3)
    parser.add_argument('--sccache-port', type=int, default=44327)
    args = parser.parse_args()
    def interrupted(number, frame):
        raise KeyboardInterrupt('benchmark cancelled')
    signal.signal(signal.SIGTERM, interrupted)
    assert 1 <= args.sessions <= 16 and 1 <= args.units <= 16 and args.repeats >= 1
    run(args)


if __name__ == '__main__':
    main()
