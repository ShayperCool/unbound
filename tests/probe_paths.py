"""Portable locations for Unity references and disposable probe run artifacts."""
import os
from pathlib import Path
import platform
import sys

DEFAULT_UNITY_VERSION = '6000.0.71f1'
# The engine's native library in its managed directory (crates/unbound-host/build.rs names it).
NATIVE_LIBRARY = 'libunbound_physx.dylib' if sys.platform == 'darwin' else 'libunbound_physx.so'


def unity_version():
    return os.environ.get('UNBOUND_PROBE_UNITY_VERSION', DEFAULT_UNITY_VERSION)


def reference_root(probe):
    """Multi-file reference set: one directory per Editor release."""
    return Path(probe)/'Reference'/('unity-'+unity_version())


def reference_file(probe, suffix='json'):
    """Single-file reference following the original probe convention."""
    return Path(probe)/'Reference'/('unity-'+unity_version()+'.'+suffix)


# macOS arm64, where the Editor and Unbound draw with Metal: the graphics device's answers and
# rounding follow the macOS Editor there (AGENTS.md Platforms), so a probe whose values the device
# decides records and checks a Metal reference next to its Linux ones.
MACOS_ARM64 = sys.platform == 'darwin' and platform.machine() == 'arm64'


def metal_counterpart(path, probe, release=None):
    """The Metal reference standing for `path`, one of `probe`'s references of `release`
    (default: the probe release): Reference/unity-R.EXT is Reference/unity-R/metal.EXT,
    Reference/unity-R itself (a probe whose whole directory is one reference set, compared as a
    directory) is Reference/unity-R/metal, and Reference/unity-R/PATH is
    Reference/unity-R/metal/PATH; for a probe in the original flat layout (no unity-R directory),
    Reference and Reference/PATH are Reference/metal and Reference/metal/PATH. None for any other
    path, the Metal reference itself included."""
    root = Path(probe)/'Reference'/f'unity-{release or unity_version()}'
    path = Path(path)
    if path.parent == root.parent and path.stem == root.name and path.suffix:
        return root/('metal'+path.suffix)
    if path == root:
        return root/'metal'
    # The original flat layout (no release directory): Reference/X is Reference/metal/X.
    flat = Path(probe)/'Reference'
    if flat.is_dir() and not any(child.name.startswith('unity-') for child in flat.iterdir()):
        if path == flat:
            return flat/'metal'
        if path.is_relative_to(flat) and path.relative_to(flat).parts[0] != 'metal' \
                and not path.relative_to(flat).parts[0].startswith('unity-'):
            return flat/'metal'/path.relative_to(flat)
    if path.is_relative_to(root) and path != root and path.relative_to(root).parts[0] != 'metal':
        return root/'metal'/path.relative_to(root)
    return None


def device_reference(path, probe, publishing=False):
    """The reference a probe reads or (`publishing`) writes for `path` on this machine: on macOS
    arm64 its Metal counterpart (to read: where it exists, else `path` as before); elsewhere,
    and for paths without a counterpart, `path`."""
    metal = metal_counterpart(path, probe) if MACOS_ARM64 else None
    return Path(metal) if metal is not None and (publishing or metal.exists()) else Path(path)


def device_reference_root(probe, publishing=False):
    """device_reference for a probe whose whole reference_root is one reference set: on macOS
    arm64 Reference/unity-R/metal (to read: where it exists)."""
    root = reference_root(probe)
    return root/'metal' if MACOS_ARM64 and (publishing or (root/'metal').is_dir()) else root


def validation_cache():
    return Path(os.environ.get('XDG_CACHE_HOME', str(Path.home()/'.cache')))/'unbound/validation'


def cache_artifact_path(path):
    """Validate disposable output, including the validation cache's disk symlink."""
    path = Path(path)
    cache = Path(os.environ.get('XDG_CACHE_HOME', str(Path.home()/'.cache')))/'unbound'
    roots = [cache.resolve(), (cache/'validation').resolve()]
    if not any(path.resolve().is_relative_to(root) for root in roots):
        raise ValueError('Run artifacts must be under $XDG_CACHE_HOME/unbound')
    return path


def run_root(probe, label='current'):
    return validation_cache()/'runs'/Path(probe).name/label


def archived_root(probe, relative=''):
    """Read-only migration archive; never a source of canonical Unity references."""
    return validation_cache()/'archive-20261031/tests/projects'/Path(probe).name/relative


def editor_path():
    """The Hub's Editor of the probe release ($UNBOUND_PROBE_UNITY overrides)."""
    override = os.environ.get('UNBOUND_PROBE_UNITY')
    if override:
        return Path(override)
    if sys.platform == 'darwin':
        return Path('/Applications/Unity/Hub/Editor')/unity_version()/'Unity.app/Contents/MacOS/Unity'
    return Path.home()/'Unity/Hub/Editor'/unity_version()/'Editor/Unity'


def shared_runtime(cli, destination, managed=None, manifest=None):
    """Reference one immutable build snapshot; all project/cache/output state stays in the run."""
    import sys
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'tools/agents'))
    from runtime_snapshot import snapshot_runtime, managed_for
    return snapshot_runtime(cli, managed or managed_for(cli, prefer_environment=False), destination, expected=manifest)
