#!/usr/bin/env python3
"""Fetch and prepare the pinned Mbed TLS release for the Unity TLS binding (native/unbound_tls.c).

The Unity TLS binding (Unity.TLS.LowLevel.Binding, which Unity Transport drives) is a layer over
Mbed TLS 3: its handshake states are Mbed TLS 3's (`UNITYTLS_SSL_HANDSHAKE_OVER` is 27, after the
TLS 1.3 states) and its transport callbacks return Mbed TLS's own codes (`-0x6900` would block).
Only the library sources, headers and licence are kept; nothing is changed.
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import pinned_source  # noqa: E402

VERSION = '3.6.7'
# The release archive's SHA-256, as the release's mbedtls-3.6.7-sha256sum.txt lists it.
SHA256 = 'a7e8bcbec0e6f761b4af24f25677626b35f762f68eef79c08677a363212d11f6'
URL = f'https://github.com/Mbed-TLS/mbedtls/releases/download/mbedtls-{VERSION}/mbedtls-{VERSION}.tar.bz2'


def prepare():
    return pinned_source.prepare(
        name='mbedtls', label=VERSION, url=URL, sha256=SHA256,
        archive_name=f'mbedtls-{VERSION}.tar.bz2', archive_variable='UNBOUND_MBEDTLS_ARCHIVE',
        limit=64 * 1024 * 1024, top=f'mbedtls-{VERSION}',
        select=lambda relative: relative.parts[0] in ('library', 'include') or relative.as_posix() == 'LICENSE',
        adjust=lambda root: None, inputs=[__file__])


if __name__ == '__main__':
    try:
        print(prepare())
    except Exception as error:
        sys.exit(f'Mbed TLS dependency: {error}')
