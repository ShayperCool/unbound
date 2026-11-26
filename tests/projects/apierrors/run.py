#!/usr/bin/env python3
"""API argument and lookup error texts; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

# The engine's own frames name the Unity build machine's source root, which the OS decides (an
# installation's paths): the Reference's, the Linux Editor's, is read as the one this OS's
# installation has (/Users/bokken/... on macOS); the capture stays as it is, and the rest of each
# frame compares exactly.
REFERENCE_ROOT = '/home/bokken/build/output/unity/unity/'
HOST_ROOT = '/Users/bokken/build/output/unity/unity/' if sys.platform == 'darwin' else REFERENCE_ROOT


def build_root(text):
    return text.replace(REFERENCE_ROOT, HOST_ROOT)


# Both sides compile the scripts with Release code optimization (the Editor's default, which the
# Reference was recorded in): a machine whose Editor preference starts in Debug
# (`ScriptDebugInfoEnabled`) would place the IL offsets of the frames elsewhere.
def release(out):
    return {'UNBOUND_CODE_OPTIMIZATION': 'release'}


if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'APIERRORS_OUT',
                              tolerance=0, engine_args=['--frames', '20', '--size', '96x64'], macos_batch=True,
                              engine_env=release, normalize=build_root,
                              editor_args=['-releaseCodeOptimization']))
