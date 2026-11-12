#!/usr/bin/env python3
"""Fetch and prepare the pinned public Crunch dependency for a native build."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import pinned_source  # noqa: E402

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


def adjust(root):
    patch(root, 'crn_vector.cpp', [
        ('math::is_power_of_2(new_capacity)', 'math::is_power_of_2(static_cast<uint64>(new_capacity))'),
        ('math::next_pow2(new_capacity)', 'math::next_pow2(static_cast<uint64>(new_capacity))'),
    ])
    patch(root, 'crn_platform.h', [
        ('#ifdef __GNUC__\n#define crn_fopen', '#if defined(__GNUC__) && !defined(__APPLE__)\n#define crn_fopen'),
        ('asm("int $3");', '__builtin_trap();'),
    ])
    patch(root, 'crn_mem.cpp', [
        ('#include <malloc.h>', '#if defined(__APPLE__)\n#include <malloc/malloc.h>\n#else\n#include <malloc.h>\n#endif'),
        ('#define _msize malloc_usable_size', '#if defined(__APPLE__)\n#define _msize malloc_size\n#else\n#define _msize malloc_usable_size\n#endif'),
    ])
    patch(root, 'crn_console.h', [
        ('#include <conio.h>\n#endif\nnamespace crnlib {',
         '#include <conio.h>\n#elif defined(__GNUC__)\n#include <termios.h>\n#include <unistd.h>\n#endif\nnamespace crnlib {'),
    ])
    patch(root, 'crn_timer.cpp', [('#include <sys/timex.h>', '#include <sys/time.h>')])
    patch(root, 'crn_jpge.cpp', [
        ('#include <malloc.h>', '#if defined(__APPLE__)\n#include <malloc/malloc.h>\n#else\n#include <malloc.h>\n#endif'),
    ])
    patch(root, '../inc/crn_decomp.h', [
        ('#include <malloc.h>', '#if defined(__APPLE__)\n#include <malloc/malloc.h>\n#define CRND_ALLOCATION_SIZE malloc_size\n#else\n#include <malloc.h>\n#define CRND_ALLOCATION_SIZE malloc_usable_size\n#endif'),
        ('p_new ? malloc_usable_size(p_new)', 'p_new ? CRND_ALLOCATION_SIZE(p_new)'),
        ('::malloc_usable_size(p_final_block)', '::CRND_ALLOCATION_SIZE(p_final_block)'),
        ('p ? malloc_usable_size(p)', 'p ? CRND_ALLOCATION_SIZE(p)'),
    ])


def prepare():
    return pinned_source.prepare(
        name='crunch', label=f'{REVISION}-{PATCH_VERSION}', url=URL, sha256=SHA256,
        archive_name=f'{REVISION}.tar.gz', archive_variable='UNBOUND_CRUNCH_ARCHIVE',
        limit=16 * 1024 * 1024, top=f'crunch-{REVISION}',
        select=lambda relative: relative.parts[0] in ('inc', 'crnlib') or relative.name == 'license.txt',
        adjust=adjust, inputs=[__file__])


if __name__ == '__main__':
    try:
        print(prepare())
    except Exception as error:
        sys.exit(f'Crunch dependency: {error}')
