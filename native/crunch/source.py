#!/usr/bin/env python3
"""Fetch and prepare the pinned public Crunch dependency for a native build."""
import fcntl
import hashlib
import os
from pathlib import Path
import shutil
import sys
import tarfile
import tempfile
import urllib.request

REVISION = '8708900eca8ec609d279270e72936258f81ddfb7'
SHA256 = '93068c7a541532b5ce4568ccac945e7cf43c2ac5a01d6a98b8c64ae53175cebf'
URL = f'https://codeload.github.com/Unity-Technologies/crunch/tar.gz/{REVISION}'
PATCH_VERSION = 1


def patch(root, name, replacements):
    path = root / 'crnlib' / name
    text = path.read_text()
    for before, after in replacements:
        if text.count(before) != 1:
            raise RuntimeError(f'{name}: pinned source does not match the expected patch')
        text = text.replace(before, after)
    path.write_text('// Unbound: portability adjustments; see native/crunch/README.md.\n' + text)


def prepare():
    cache = Path(os.environ.get('XDG_CACHE_HOME', Path.home() / '.cache')) / 'unbound/dependencies/crunch'
    cache.mkdir(parents=True, exist_ok=True)
    with (cache / 'prepare.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        patch_hash = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()[:16]
        destination = cache / f'{REVISION}-{PATCH_VERSION}-{patch_hash}'
        if (destination / '.ready').is_file():
            return destination
        supplied = os.environ.get('UNBOUND_CRUNCH_ARCHIVE')
        archive = Path(supplied) if supplied else cache / f'{REVISION}.tar.gz'
        if not archive.is_file():
            offline = os.environ.get('UNBOUND_OFFLINE', '').lower() in ('1', 'true', 'yes')
            offline |= os.environ.get('CARGO_NET_OFFLINE', '').lower() in ('1', 'true')
            if supplied or offline:
                raise RuntimeError(f'Crunch archive is unavailable: {archive}; source: {URL}')
            request = urllib.request.Request(URL, headers={'User-Agent': 'Unbound-build'})
            with tempfile.NamedTemporaryFile(dir=cache, delete=False) as output:
                temporary = Path(output.name)
                try:
                    with urllib.request.urlopen(request, timeout=60) as response:
                        size = 0
                        while chunk := response.read(256 * 1024):
                            size += len(chunk)
                            if size > 16 * 1024 * 1024:
                                raise RuntimeError(f'Crunch archive exceeds its download limit: {URL}')
                            output.write(chunk)
                    output.flush()
                    if hashlib.sha256(temporary.read_bytes()).hexdigest() != SHA256:
                        raise RuntimeError(f'Crunch archive checksum mismatch: {URL}')
                    temporary.replace(archive)
                finally:
                    temporary.unlink(missing_ok=True)
        if hashlib.sha256(archive.read_bytes()).hexdigest() != SHA256:
            raise RuntimeError(f'Crunch archive checksum mismatch: {archive}; source: {URL}')
        temporary = Path(tempfile.mkdtemp(prefix='source-', dir=cache))
        try:
            with tarfile.open(archive) as source:
                for member in source.getmembers():
                    # Only regular source files are needed. Reject links and path traversal.
                    parts = Path(member.name).parts
                    if not parts or parts[0] != f'crunch-{REVISION}' or '..' in parts:
                        raise RuntimeError(f'Invalid Crunch archive path: {member.name}')
                    if not member.isfile() or len(parts) == 1:
                        continue
                    relative = Path(*parts[1:])
                    if relative.parts[0] not in ('inc', 'crnlib') and relative.name != 'license.txt':
                        continue
                    target = temporary / relative
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with source.extractfile(member) as data, target.open('wb') as output:
                        shutil.copyfileobj(data, output)
            patch(temporary, 'crn_vector.cpp', [
                ('math::is_power_of_2(new_capacity)', 'math::is_power_of_2(static_cast<uint64>(new_capacity))'),
                ('math::next_pow2(new_capacity)', 'math::next_pow2(static_cast<uint64>(new_capacity))'),
            ])
            patch(temporary, 'crn_platform.h', [
                ('#ifdef __GNUC__\n#define crn_fopen', '#if defined(__GNUC__) && !defined(__APPLE__)\n#define crn_fopen'),
                ('asm("int $3");', '__builtin_trap();'),
            ])
            patch(temporary, 'crn_mem.cpp', [
                ('#include <malloc.h>', '#if defined(__APPLE__)\n#include <malloc/malloc.h>\n#else\n#include <malloc.h>\n#endif'),
                ('#define _msize malloc_usable_size', '#if defined(__APPLE__)\n#define _msize malloc_size\n#else\n#define _msize malloc_usable_size\n#endif'),
            ])
            patch(temporary, 'crn_console.h', [
                ('#include <conio.h>\n#endif\nnamespace crnlib {',
                 '#include <conio.h>\n#elif defined(__GNUC__)\n#include <termios.h>\n#include <unistd.h>\n#endif\nnamespace crnlib {'),
            ])
            patch(temporary, 'crn_timer.cpp', [('#include <sys/timex.h>', '#include <sys/time.h>')])
            patch(temporary, 'crn_jpge.cpp', [
                ('#include <malloc.h>', '#if defined(__APPLE__)\n#include <malloc/malloc.h>\n#else\n#include <malloc.h>\n#endif'),
            ])
            patch(temporary, '../inc/crn_decomp.h', [
                ('#include <malloc.h>', '#if defined(__APPLE__)\n#include <malloc/malloc.h>\n#define CRND_ALLOCATION_SIZE malloc_size\n#else\n#include <malloc.h>\n#define CRND_ALLOCATION_SIZE malloc_usable_size\n#endif'),
                ('p_new ? malloc_usable_size(p_new)', 'p_new ? CRND_ALLOCATION_SIZE(p_new)'),
                ('::malloc_usable_size(p_final_block)', '::CRND_ALLOCATION_SIZE(p_final_block)'),
                ('p ? malloc_usable_size(p)', 'p ? CRND_ALLOCATION_SIZE(p)'),
            ])
            (temporary / '.ready').write_text(SHA256 + '\n')
            temporary.replace(destination)
        finally:
            if temporary.exists():
                shutil.rmtree(temporary)
        return destination


if __name__ == '__main__':
    try:
        print(prepare())
    except Exception as error:
        sys.exit(f'Crunch dependency: {error}')
