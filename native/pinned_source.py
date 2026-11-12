"""Fetch a pinned public source archive and prepare it once in Unbound's dependency cache.

Native dependencies built from upstream sources (native/crunch, native/freetype) use this: the
archive is checked against its SHA-256, only regular files the caller selects are extracted
(links and path traversal are refused), the caller's adjustments run on a temporary copy, and
the result is published atomically under a directory keyed by the caller's and this file's
contents, so a changed preparation can never reuse a stale tree. Nothing is written elsewhere.
"""
import fcntl
import hashlib
import os
from pathlib import Path
import shutil
import tarfile
import tempfile
import urllib.request


def cache_root(name):
    return Path(os.environ.get('XDG_CACHE_HOME', Path.home() / '.cache')) / 'unbound/dependencies' / name


def offline():
    return (os.environ.get('UNBOUND_OFFLINE', '').lower() in ('1', 'true', 'yes')
            or os.environ.get('CARGO_NET_OFFLINE', '').lower() in ('1', 'true'))


def download(url, sha256, archive, limit):
    request = urllib.request.Request(url, headers={'User-Agent': 'Unbound-build'})
    with tempfile.NamedTemporaryFile(dir=archive.parent, delete=False) as output:
        temporary = Path(output.name)
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                size = 0
                while chunk := response.read(256 * 1024):
                    size += len(chunk)
                    if size > limit:
                        raise RuntimeError(f'archive exceeds its download limit: {url}')
                    output.write(chunk)
            output.flush()
            if hashlib.sha256(temporary.read_bytes()).hexdigest() != sha256:
                raise RuntimeError(f'archive checksum mismatch: {url}')
            temporary.replace(archive)
        finally:
            temporary.unlink(missing_ok=True)


def prepare(*, name, label, url, sha256, archive_name, archive_variable, limit, top, select, adjust, inputs):
    """Return the prepared source directory for `name`.

    `top` is the archive's single top-level directory; `select(relative)` says whether a regular
    file below it is kept; `adjust(root)` changes the extracted tree before it is published;
    `inputs` are the files whose contents define the preparation (the caller's script).
    `archive_variable` names an environment variable that supplies the archive instead of a
    download; UNBOUND_OFFLINE or CARGO_NET_OFFLINE forbid downloading.
    """
    cache = cache_root(name)
    cache.mkdir(parents=True, exist_ok=True)
    with (cache / 'prepare.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        digest = hashlib.sha256()
        for path in [*inputs, __file__]:
            digest.update(Path(path).read_bytes())
        destination = cache / f'{label}-{digest.hexdigest()[:16]}'
        if (destination / '.ready').is_file():
            return destination
        supplied = os.environ.get(archive_variable)
        archive = Path(supplied) if supplied else cache / archive_name
        if not archive.is_file():
            if supplied or offline():
                raise RuntimeError(f'{name} archive is unavailable: {archive}; source: {url}')
            download(url, sha256, archive, limit)
        if hashlib.sha256(archive.read_bytes()).hexdigest() != sha256:
            raise RuntimeError(f'{name} archive checksum mismatch: {archive}; source: {url}')
        temporary = Path(tempfile.mkdtemp(prefix='source-', dir=cache))
        try:
            with tarfile.open(archive) as source:
                for member in source.getmembers():
                    # Only regular source files are needed. Reject links and path traversal.
                    parts = Path(member.name).parts
                    if not parts or parts[0] != top or '..' in parts:
                        raise RuntimeError(f'Invalid {name} archive path: {member.name}')
                    if not member.isfile() or len(parts) == 1:
                        continue
                    relative = Path(*parts[1:])
                    if not select(relative):
                        continue
                    target = temporary / relative
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with source.extractfile(member) as data, target.open('wb') as output:
                        shutil.copyfileobj(data, output)
            adjust(temporary)
            (temporary / '.ready').write_text(sha256 + '\n')
            temporary.replace(destination)
        finally:
            if temporary.exists():
                shutil.rmtree(temporary)
        return destination
