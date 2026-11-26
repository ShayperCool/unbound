#!/usr/bin/env python3
"""Fetch and prepare the pinned libxmp-lite release for tracker module decoding (native/unbound_tracker.c).

libxmp-lite (MIT licence) decodes the four tracker module formats Unity supports: ProTracker (.mod),
FastTracker II (.xm), Scream Tracker 3 (.s3m) and Impulse Tracker (.it). Only the library sources,
public header and README (with licence) are kept; nothing is changed.
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import pinned_source  # noqa: E402

VERSION = '4.7.4'
SHA256 = 'd4b6108b6e8c7ecc106403bc4bab28fb46686867a7b5247567371ad557634c12'
URL = f'https://github.com/libxmp/libxmp/releases/download/libxmp-{VERSION}/libxmp-lite-{VERSION}.tar.gz'


def select(relative):
    return relative.parts[0] in ('src', 'include') or relative.as_posix() == 'README'


def prepare():
    return pinned_source.prepare(
        name='libxmp-lite', label=VERSION, url=URL, sha256=SHA256,
        archive_name=f'libxmp-lite-{VERSION}.tar.gz', archive_variable='UNBOUND_LIBXMP_LITE_ARCHIVE',
        limit=16 * 1024 * 1024, top=f'libxmp-lite-{VERSION}',
        select=select, adjust=lambda root: None, inputs=[__file__])


if __name__ == '__main__':
    try:
        print(prepare())
    except Exception as error:
        sys.exit(f'libxmp-lite dependency: {error}')
