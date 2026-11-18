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
import ssl
import subprocess
import tarfile
import tempfile
import urllib.error
import urllib.request

# The system's CA bundles, for a Python whose own has none (the python.org macOS build until its
# "Install Certificates" step runs): macOS and most Linux distributions, Debian, Fedora.
SYSTEM_CA_BUNDLES = ('/etc/ssl/cert.pem', '/etc/ssl/certs/ca-certificates.crt', '/etc/pki/tls/certs/ca-bundle.crt')


def cache_root(name):
    return Path(os.environ.get('XDG_CACHE_HOME') or Path.home() / '.cache') / 'unbound/dependencies' / name


def offline():
    # UNBOUND_OFFLINE: any value, as the engine reads it; CARGO_NET_OFFLINE as Cargo reads it.
    return ('UNBOUND_OFFLINE' in os.environ
            or os.environ.get('CARGO_NET_OFFLINE', '').lower() in ('1', 'true'))


def certificate_failure(error):
    reason = getattr(error, 'reason', error)
    return isinstance(reason, ssl.SSLCertVerificationError) or 'CERTIFICATE_VERIFY_FAILED' in str(reason)


def fetch(url, output, limit, context=None):
    request = urllib.request.Request(url, headers={'User-Agent': 'Unbound-build'})
    with urllib.request.urlopen(request, timeout=60, context=context) as response:
        size = 0
        while chunk := response.read(256 * 1024):
            size += len(chunk)
            if size > limit:
                raise RuntimeError(f'archive exceeds its download limit: {url}')
            output.write(chunk)


def fetch_with_system_trust(url, output, limit, error):
    """When Python cannot verify the server's certificate: the system's CA bundle, else curl
    (which uses the system's trust store). The archive's checksum is checked either way."""
    for bundle in SYSTEM_CA_BUNDLES:
        if Path(bundle).is_file():
            output.seek(0)
            output.truncate()
            try:
                fetch(url, output, limit, ssl.create_default_context(cafile=bundle))
                return
            except urllib.error.URLError as again:
                if not certificate_failure(again):
                    raise
    curl = shutil.which('curl')
    if curl is None:
        raise RuntimeError(f'{url}: the certificate cannot be verified ({error}) and no system CA bundle '
                           f'or curl is available; set SSL_CERT_FILE to a CA bundle') from error
    output.seek(0)
    output.truncate()
    output.flush()
    result = subprocess.run([curl, '-fsSL', '--max-time', '300', '--max-filesize', str(limit), '-o', output.name, url],
                            capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f'{url}: curl failed: {result.stderr.strip()}') from error
    output.seek(0, os.SEEK_END)


def download(url, sha256, archive, limit):
    with tempfile.NamedTemporaryFile(dir=archive.parent, delete=False) as output:
        temporary = Path(output.name)
        try:
            try:
                fetch(url, output, limit)
            except urllib.error.URLError as error:
                if not certificate_failure(error):
                    raise
                fetch_with_system_trust(url, output, limit, error)
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
