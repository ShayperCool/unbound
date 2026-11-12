#!/usr/bin/env python3
"""Fetch and prepare the FreeType and libpng sources of the pinned freetype-sys crate.

vendor/freetype-sys builds these sources. Its upstream build script prepares them inside the
crate's own directory (it copies libpng's prebuilt configuration header there), which is the
shared Cargo registry; here that happens once in Unbound's dependency cache instead.
"""
from pathlib import Path
import shutil
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import pinned_source  # noqa: E402

VERSION = '0.23.0'
# The published crate archive, as Cargo.lock recorded it for the registry package.
SHA256 = 'eab537ce43cab850c64b4cdc390ce7e4f47f877485ddc323208e268280c308ae'
URL = f'https://static.crates.io/crates/freetype-sys/freetype-sys-{VERSION}.crate'


def adjust(root):
    # From libpng/png.h: "If pnglibconf.h is missing, you can copy
    # scripts/pnglibconf.h.prebuilt to pnglibconf.h" (what the upstream build script does).
    shutil.copyfile(root / 'libpng/scripts/pnglibconf.h.prebuilt', root / 'libpng/pnglibconf.h')


def prepare():
    return pinned_source.prepare(
        name='freetype-sys', label=VERSION, url=URL, sha256=SHA256,
        archive_name=f'freetype-sys-{VERSION}.crate', archive_variable='UNBOUND_FREETYPE_ARCHIVE',
        limit=16 * 1024 * 1024, top=f'freetype-sys-{VERSION}',
        select=lambda relative: relative.parts[0] in ('freetype2', 'libpng') or relative.name == 'LICENSE',
        adjust=adjust, inputs=[__file__])


if __name__ == '__main__':
    try:
        print(prepare())
    except Exception as error:
        sys.exit(f'FreeType dependency: {error}')
