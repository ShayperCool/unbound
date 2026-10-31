"""Constrain all disposable captures and reports to the Unbound cache."""
import os
from pathlib import Path


def artifact_path(path):
    path=Path(path)
    cache=Path(os.environ.get('XDG_CACHE_HOME',str(Path.home()/'.cache')))/'unbound'
    # validation may be a symlink to a separate disk.
    roots=[cache.resolve(),(cache/'validation').resolve()]
    if not any(path.resolve().is_relative_to(root) for root in roots):
        raise ValueError('run artifacts must be under $XDG_CACHE_HOME/unbound: '+str(path))
    return path
