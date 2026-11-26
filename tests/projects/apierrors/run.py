#!/usr/bin/env python3
"""API argument and lookup error texts; see README.md."""
from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

# The engine's own frames name the Unity build machine's source root, which the OS decides (an
# installation's paths: /home/bokken/... in the Linux Editor, /Users/bokken/... in the macOS one):
# both read as one root, and the rest of each frame still compares exactly.
BUILD_ROOT = re.compile(r'/(?:home|Users)/bokken/build/output/unity/unity/')


def build_root(text):
    return BUILD_ROOT.sub('<unity-build>/', text)


# The Reference's scripts were compiled with Release code optimization (the Editor's default); a
# machine whose Editor preference starts in Debug would place IL offsets elsewhere.
def release(out):
    return {'UNBOUND_CODE_OPTIMIZATION': 'release'}


if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'APIERRORS_OUT',
                              tolerance=0, engine_args=['--frames', '20', '--size', '96x64'], macos_batch=True,
                              engine_env=release, normalize=build_root))
