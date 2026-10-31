"""Portable locations for Unity references and disposable probe run artifacts."""
import os
from pathlib import Path

DEFAULT_UNITY_VERSION = '6000.0.71f1'


def unity_version():
    return os.environ.get('UNBOUND_PROBE_UNITY_VERSION', DEFAULT_UNITY_VERSION)


def reference_root(probe):
    """Multi-file reference set: one directory per Editor release."""
    return Path(probe)/'Reference'/('unity-'+unity_version())


def reference_file(probe, suffix='json'):
    """Single-file reference following the original probe convention."""
    return Path(probe)/'Reference'/('unity-'+unity_version()+'.'+suffix)


def validation_cache():
    return Path(os.environ.get('XDG_CACHE_HOME', str(Path.home()/'.cache')))/'unbound/validation'


def run_root(probe, label='current'):
    return validation_cache()/'runs'/Path(probe).name/label


def archived_root(probe, relative=''):
    """Read-only migration archive; never a source of canonical Unity references."""
    return validation_cache()/'archive-20261031/tests/projects'/Path(probe).name/relative


def editor_path():
    return Path(os.environ.get('UNBOUND_PROBE_UNITY', str(
        Path.home()/'Unity/Hub/Editor'/unity_version()/'Editor/Unity')))
