#!/usr/bin/env python3
"""Recreate the pinned sampler dependency sources in a fresh directory."""
import argparse, hashlib, io, subprocess, tarfile, urllib.request
from pathlib import Path

VERSION = '30.0.1'
CHECKSUMS = {'wgpu': '527ccdf43dd5b2e8676eed9984ce00e2bbb0a1b85b70c1969dcb6cd2eb55ab9e', 'wgpu-core': '14c018fce9b6270aa203c2fdd56f3cce996713534bd757e4ea58c8560b121f14', 'wgpu-hal': 'b6b7fb58561a792bc237628ba0792e332de418fefe145f13b5ed8201e6d52f58'}
ROOT = Path(__file__).resolve().parent
# Applied in this order; PATCHES.md lists every change.
PATCHES = ['native-lod-bias.patch', 'gl-device.patch']

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--archive-dir', type=Path)
    a = p.parse_args()
    destination = a.output.resolve()
    if destination.exists(): p.error('output must be fresh')
    destination.mkdir(parents=True)
    for name, digest in CHECKSUMS.items():
        archive_name = f'{name}-{VERSION}.crate'
        if a.archive_dir:
            data = (a.archive_dir/archive_name).read_bytes()
        else:
            url = f'https://static.crates.io/crates/{name}/{archive_name}'
            with urllib.request.urlopen(url) as response: data = response.read()
        if hashlib.sha256(data).hexdigest() != digest:
            raise SystemExit(f'archive checksum mismatch: {name}')
        with tarfile.open(fileobj=io.BytesIO(data), mode='r:gz') as archive:
            for member in archive.getmembers():
                parts = Path(member.name).parts
                if not parts or parts[0] != f'{name}-{VERSION}' or '..' in parts:
                    raise SystemExit(f'unsafe archive member: {member.name}')
                if not member.isfile():
                    if member.isdir(): continue
                    raise SystemExit(f'unsupported archive member: {member.name}')
                relative = Path(*parts[1:])
                if relative.name in {'.cargo-checksum.json', '.cargo_vcs_info.json', '.cargo-ok'}: continue
                target = destination/name/relative
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(archive.extractfile(member).read())
                target.chmod(member.mode & 0o777)
    for patch in PATCHES:
        subprocess.run(['patch','--batch','--forward','-p1','-i',str(ROOT/patch)], cwd=destination, check=True)
    print(destination)

if __name__ == '__main__': main()
