#!/usr/bin/env python3
"""Write THIRD_PARTY_NOTICES.md for a release archive.

Collects the license texts of everything the archive ships besides Unbound's own code:

  - the vendored native libraries under native/ (PhysX, NvCloth, libvorbis, libtess2, ...);
  - third-party code ported into managed/ and crates/ (Box2D, DotRecast, Yoga, ...);
  - the Rust crates linked into the `unbound` binary (normal dependencies only, for the build's
    target platform), including the C/C++ libraries that -sys crates compile in;
  - the NuGet packages copied into managed/ (from the managed build's project.assets.json).

Identical license texts are printed once with the list of components using them.

    python3 tools/release/notices.py --out THIRD_PARTY_NOTICES.md
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LICENSE_NAME = re.compile(r'^(licen[cs]e|copying|notice|unlicense|copyright|third-party-notices)', re.I)
NOT_TEXT = {'.rtf', '.html', '.htm', '.rs', '.py', '.c', '.h', '.cpp', '.json', '.toml', '.yml', '.sh', '.cmake'}
SKIP_DIRS = {'test', 'tests', 'testdata', 'examples', 'docs', 'doc', 'benches', '.git'}


def license_files(directory, depth):
    """License files in `directory`, searching `depth` levels of subdirectories."""
    found = []
    try:
        entries = sorted(os.scandir(directory), key=lambda e: e.name)
    except OSError:
        return found
    for entry in entries:
        if entry.is_file() and LICENSE_NAME.match(entry.name) \
                and Path(entry.name).suffix.lower() not in NOT_TEXT:
            found.append(Path(entry.path))
        elif entry.is_dir() and depth > 0 and entry.name.lower() not in SKIP_DIRS:
            found.extend(license_files(entry.path, depth - 1))
    return found


def read_text(path):
    return path.read_text(encoding='utf-8', errors='replace').strip()


class Section:
    def __init__(self, title, intro):
        self.title = title
        self.intro = intro
        self.components = []  # (name, detail)
        self.texts = {}  # hash -> (text, [component names])

    def add(self, name, detail, texts):
        self.components.append((name, detail))
        for text in texts:
            key = hashlib.sha256(text.encode()).hexdigest()
            self.texts.setdefault(key, (text, []))[1].append(name)

    def render(self):
        out = [f'## {self.title}', '', self.intro, '']
        for name, detail in self.components:
            out.append(f'- {name}' + (f': {detail}' if detail else ''))
        out.append('')
        for text, users in self.texts.values():
            out.append(f'### {", ".join(users)}')
            out.append('')
            out.append('```')
            out.append(text.replace('```', "'''"))
            out.append('```')
            out.append('')
        return '\n'.join(out)


def native_section():
    section = Section('Vendored native libraries',
                      'Compiled from `native/` into the `unbound_physx` library in `managed/`.')
    for directory in sorted((ROOT / 'native').iterdir()):
        if not directory.is_dir():
            continue
        files = license_files(directory, 2)
        if not files:
            sys.exit(f'notices: native/{directory.name} has no license file')
        section.add(directory.name, None, [read_text(f) for f in files])
    return section


# Third-party code translated into Unbound's own sources whose notice is a comment in the file:
# (component, file, the comment's first line, MIT copyright holder or None). The comment paragraph
# starting there is quoted; for MIT-licensed code the MIT License text follows with that holder.
IN_FILE_NOTICES = [
    ('Yoga 1.9.0 (C# port)', 'managed/UnityEngine/UIElements/Layout/Yoga.cs',
     '// Yoga is Copyright', 'Facebook, Inc. and its affiliates (2014-present)'),
    ('Ooura FFT (fft4g, Rust port)', 'crates/unbound-runtime/src/audio/pitch.rs',
     '// Copyright Takuya OOURA', None),
]


def mit_license(holder):
    text = read_text(ROOT / 'LICENSE')
    return re.sub(r'^Copyright \(c\) .*$', f'Copyright (c) {holder}', text, count=1, flags=re.M)


def ported_section():
    section = Section('Ported source code',
                      'Third-party code translated into `managed/` (compiled into '
                      '`UnityEngine.CoreModule.dll`) or `crates/` (linked into `bin/unbound`).')
    tracked = subprocess.run(['git', 'ls-files', 'managed', 'crates'], check=True,
                             capture_output=True, text=True, cwd=ROOT).stdout.splitlines()
    by_directory = {}
    for path in tracked:
        if LICENSE_NAME.match(Path(path).name) and Path(path).suffix.lower() not in NOT_TEXT:
            by_directory.setdefault(str(Path(path).parent), []).append(ROOT / path)
    for directory, files in sorted(by_directory.items()):
        section.add(directory, None, [read_text(f) for f in sorted(files)])
    for name, path, first, mit_holder in IN_FILE_NOTICES:
        lines = (ROOT / path).read_text().splitlines()
        start = next((i for i, line in enumerate(lines) if line.startswith(first)), None)
        if start is None:
            sys.exit(f'notices: the {name} notice ("{first}") is no longer in {path}')
        end = start
        while end < len(lines) and lines[end].startswith('//') and lines[end].strip() != '//':
            end += 1
        notice = '\n'.join(lines[start:end])
        if mit_holder is not None:
            notice += '\n\n' + mit_license(mit_holder)
        section.add(name, path, [notice])
    return section


def host_triple():
    out = subprocess.run(['rustc', '-vV'], check=True, capture_output=True, text=True,
                         cwd=ROOT).stdout
    return next(line.split(': ', 1)[1] for line in out.splitlines() if line.startswith('host: '))


def rust_section(triple):
    metadata = json.loads(subprocess.run(
        ['cargo', 'metadata', '--format-version', '1', '--locked', '--filter-platform', triple],
        check=True, capture_output=True, text=True, cwd=ROOT).stdout)
    packages = {p['id']: p for p in metadata['packages']}
    nodes = {n['id']: n for n in metadata['resolve']['nodes']}
    root = next(p['id'] for p in metadata['packages']
                if p['name'] == 'unbound' and p['source'] is None)
    workspace = set(metadata['workspace_members'])
    seen, stack = set(), [root]
    while stack:
        current = stack.pop()
        if current in seen:
            continue
        seen.add(current)
        for dep in nodes[current]['deps']:
            if any(kind['kind'] is None for kind in dep['dep_kinds']):
                stack.append(dep['pkg'])
    section = Section('Rust crates',
                      'Linked into `bin/unbound`. A -sys crate\'s entry includes the licenses of '
                      'the C/C++ library it compiles in.')
    for package in sorted((packages[i] for i in seen - workspace),
                          key=lambda p: (p['name'], p['version'])):
        directory = Path(package['manifest_path']).parent
        depth = 4 if package['name'].endswith('-sys') else 0
        texts = [read_text(f) for f in license_files(directory, depth)]
        license = package.get('license') or 'see license file'
        if not texts:
            authors = ', '.join(package.get('authors') or []) or 'the authors'
            texts = [f'{package["name"]} is licensed under {license}. Copyright {authors}.']
        section.add(f'{package["name"]} {package["version"]}', license, texts)
    return section


def nuget_section():
    # managed/Directory.Build.props keeps the managed build under the checkout's target/, whatever
    # CARGO_TARGET_DIR is.
    assets = ROOT / 'target/managed-build/obj/Unbound.Host/project.assets.json'
    if not assets.is_file():
        sys.exit(f'notices: {assets} is missing; build the managed engine first')
    data = json.loads(assets.read_text())
    folders = list(data['packageFolders'])
    section = Section('.NET packages', 'Copied into `managed/` from NuGet.')
    for key, library in sorted(data['libraries'].items()):
        if library['type'] != 'package':
            continue
        directory = next((Path(f) / library['path'] for f in folders
                          if (Path(f) / library['path']).is_dir()), None)
        if directory is None:
            sys.exit(f'notices: NuGet package {key} is not in {folders}')
        license, copyright = 'see license file', ''
        nuspec = next(directory.glob('*.nuspec'), None)
        if nuspec is not None:
            tree = ET.parse(nuspec)
            for element in tree.iter():
                tag = element.tag.rsplit('}', 1)[-1]
                if tag == 'license' and element.text:
                    license = element.text.strip()
                elif tag == 'copyright' and element.text:
                    copyright = element.text.strip()
        texts = [read_text(f) for f in license_files(directory, 0)]
        if not texts:
            texts = [f'{key.split("/")[0]} is licensed under {license}. {copyright}'.strip()]
        section.add(key.replace('/', ' '), license, texts)
    return section


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--triple', help='Rust target triple (default: the host)')
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    sections = [native_section(), ported_section(), rust_section(args.triple or host_triple()),
                nuget_section()]
    header = ('# Third-party notices\n\nUnbound itself is licensed under the MIT License '
              '(`LICENSE`). This archive also contains the following third-party software, '
              'each under its own license.\n')
    args.out.write_text(header + '\n' + '\n'.join(s.render() for s in sections))
    print(f'notices: {sum(len(s.components) for s in sections)} components -> {args.out}')


if __name__ == '__main__':
    main()
