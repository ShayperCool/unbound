#!/usr/bin/env python3
"""Pack a built `unbound` into a release archive.

Expects a finished release build (`cargo build --release --locked -p unbound`), which also builds
the managed engine into `<target>/managed`. Produces

    <out>/unbound-<platform>.tar.gz          unbound/{bin/unbound, managed/, LICENSE,
    <out>/unbound-<platform>.tar.gz.sha256            THIRD_PARTY_NOTICES.md, README.md, VERSION}

The binary finds `managed/` next to its own directory (`crates/unbound-host`), so the unpacked
directory works from any location. Debug info is stripped from the binary (symbols stay, so
panics keep function names); macOS gets an ad-hoc signature again afterwards.

    python3 tools/release/package.py --platform linux-x86_64 --out dist [--tag v0.1.0]
"""
import argparse
import hashlib
import os
import platform as host
import re
import shutil
import subprocess
import sys
import tarfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def workspace_version():
    # The [workspace.package] table's version (no tomllib: Python 3.10 has none).
    text = (ROOT / 'Cargo.toml').read_text()
    table = text.split('[workspace.package]', 1)[1].split('\n[', 1)[0]
    return re.search(r'^version\s*=\s*"([^"]+)"', table, re.M).group(1)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--platform', required=True, help='archive suffix, e.g. linux-x86_64')
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--target-dir', type=Path, default=Path(
        os.environ.get('CARGO_TARGET_DIR') or ROOT / 'target'))
    parser.add_argument('--tag', help='release tag; must be v<workspace version>')
    args = parser.parse_args()

    version = workspace_version()
    if args.tag is not None and args.tag != f'v{version}':
        sys.exit(f'package: tag {args.tag} does not match the workspace version {version} '
                 '(Cargo.toml [workspace.package] version)')
    target = args.target_dir.resolve()
    binary = target / 'release/unbound'
    managed = target / 'managed'
    for required in (binary, managed / 'Unbound.Host.dll', managed / 'Unbound.Host.runtimeconfig.json'):
        if not required.exists():
            sys.exit(f'package: {required} is missing; run a release build first')
    if not any(managed.glob('libunbound_physx.*')):
        sys.exit(f'package: the native library (libunbound_physx) is missing from {managed}')

    out = args.out.resolve()
    stage = out / 'stage'
    shutil.rmtree(stage, ignore_errors=True)
    package = stage / 'unbound'
    (package / 'bin').mkdir(parents=True)
    shutil.copy2(binary, package / 'bin/unbound')
    if host.system() == 'Darwin':
        subprocess.run(['strip', '-S', package / 'bin/unbound'], check=True)
        subprocess.run(['codesign', '--force', '--sign', '-', package / 'bin/unbound'], check=True)
    else:
        subprocess.run(['strip', '--strip-debug', package / 'bin/unbound'], check=True)
    shutil.copytree(managed, package / 'managed', symlinks=True)
    shutil.copy2(ROOT / 'LICENSE', package / 'LICENSE')
    shutil.copy2(ROOT / 'README.md', package / 'README.md')
    (package / 'VERSION').write_text(version + '\n')
    subprocess.run([sys.executable, ROOT / 'tools/release/notices.py',
                    '--out', package / 'THIRD_PARTY_NOTICES.md'], check=True)

    reported = subprocess.run([package / 'bin/unbound', '--version'], check=True,
                              capture_output=True, text=True).stdout.strip()
    if not re.fullmatch(rf'unbound {re.escape(version)}', reported):
        sys.exit(f'package: the packed binary reports "{reported}", expected unbound {version}')

    archive = out / f'unbound-{args.platform}.tar.gz'
    with tarfile.open(archive, 'w:gz', compresslevel=9) as tar:
        tar.add(package, arcname='unbound')
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    (out / f'{archive.name}.sha256').write_text(f'{digest}  {archive.name}\n')
    shutil.rmtree(stage)
    size = archive.stat().st_size / 1e6
    print(f'package: {archive} ({size:.0f} MB), unbound {version}, sha256 {digest}')


if __name__ == '__main__':
    main()
