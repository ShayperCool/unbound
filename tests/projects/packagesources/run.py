#!/usr/bin/env python3
"""Packages from an authenticated scoped registry, a local tarball and a git repository; see
README.md.

    run.py record --label L [--no-lock]   one Editor capture (a fresh project, registry and repo)
    run.py publish A B                    two identical captures -> Reference/unity-<release>/
    run.py gate [--label L]               the engine on a fresh project, compared with Reference/
"""
import argparse
import base64
import gzip
import hashlib
import http.server
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import threading
import time

ROOT = Path(__file__).resolve().parent
TESTS = ROOT.parents[1]
sys.path.insert(0, str(TESTS))
import editor_runner  # noqa: E402
import value_probe  # noqa: E402
from probe_paths import reference_root, run_root  # noqa: E402

VARIABLE = 'PACKAGE_SOURCES_OUT'
METHOD = 'PackageSourcesProbe.Record'

# The local test registry: one scoped registry per package, each a question about which
# Authorization header the Package Manager sends: the scoped registry's URL path and where the
# package document puts the tarball ('registry' is the registry's own host and port, 'other' the
# same server under another host name, 'port' the registry's host on a second port).
REGISTRIES = [
    # (package, registry path, tarball host, tarball path)
    ('com.probe.one', '/r1', 'registry', '/r1/-/one.tgz'),
    ('com.probe.two', '/r2/', 'registry', '/r2/-/two.tgz'),
    ('com.probe.three', '/r3/deep', 'registry', '/r3/deep/-/three.tgz'),
    ('com.probe.four', '/r3/other', 'registry', '/r3/other/-/four.tgz'),
    ('com.probe.five', '/r5', 'other', '/t5/five.tgz'),
    ('com.probe.six', '/r6', 'other', '/t6/six.tgz'),
    ('com.probe.seven', '/r7', 'registry', '/t7/seven.tgz'),
    ('com.probe.eight', '/r8', 'registry', '/r8/-/eight.tgz'),
    ('com.probe.nine', '/r9', 'registry', '/r9/-/nine.tgz'),
    ('com.probe.ten', '/r10', 'port', '/t10/ten.tgz'),
]
# The user's configuration (UPM_USER_CONFIG_FILE): `[npmAuth."<registry>"]` sections. The tokens
# are this probe's own test values.
NPM_AUTH = [
    ('/r1', {'token': 'token-one'}),
    ('/r2', {'_auth': base64.b64encode(b'two:secret').decode()}),
    ('/r3', {'token': 'token-three-short'}),
    ('/r3/deep', {'token': 'token-three-deep'}),
    ('/r5', {'token': 'token-five', 'alwaysAuth': False}),
    ('/r6', {'token': 'token-six', 'alwaysAuth': True}),
    ('/r7', {'token': 'token-seven'}),
    ('/r9/', {'token': 'token-nine'}),
    ('/r10', {'token': 'token-ten'}),
]
TARBALL = 'com.probe.tarball'
GIT = 'com.probe.git'
GIT_PATH = 'pkgs/gitpkg'
# Fixed identities and dates make the repository's commits the same in every run.
GIT_ENV = {'GIT_AUTHOR_NAME': 'probe', 'GIT_AUTHOR_EMAIL': 'probe@example.invalid',
           'GIT_COMMITTER_NAME': 'probe', 'GIT_COMMITTER_EMAIL': 'probe@example.invalid',
           'GIT_CONFIG_GLOBAL': os.devnull, 'GIT_CONFIG_NOSYSTEM': '1'}


def package_json(name, version='1.0.0'):
    return json.dumps({'name': name, 'version': version, 'displayName': name, 'unity': '6000.0'}, indent=2)+'\n'


def npm_tarball(files):
    """A gzipped npm tarball (`package/` prefix) of `files` {path: text}, the same bytes every run."""
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode='w', format=tarfile.USTAR_FORMAT) as tar:
        for path, text in sorted(files.items()):
            data = text.encode()
            info = tarfile.TarInfo('package/'+path)
            info.size, info.mtime, info.mode = len(data), 0, 0o644
            tar.addfile(info, io.BytesIO(data))
    return gzip.compress(raw.getvalue(), mtime=0)


def package_files(name, marker, version='1.0.0'):
    return {'package.json': package_json(name, version), 'Marker.txt': marker+'\n'}


class Registry(http.server.ThreadingHTTPServer):
    """The local test registry: serves every package document and tarball whatever the request's
    Authorization (so each request shows what was sent) and records each request."""

    def __init__(self):
        super().__init__(('127.0.0.1', 0), Handler)
        self.owner = self
        self.port = self.server_address[1]
        # The second port: the same routes, recorded here.
        self.second = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        self.second.owner = self
        self.requests = []
        self.lock = threading.Lock()
        self.routes = {}
        for name, registry, host, path in REGISTRIES:
            data = npm_tarball(package_files(name, 'registry '+name))
            dist = {'tarball': f'{self.origin(host)}{path}', 'shasum': hashlib.sha1(data).hexdigest(),
                    'integrity': 'sha512-'+base64.b64encode(hashlib.sha512(data).digest()).decode()}
            document = {'_id': name, 'name': name, 'dist-tags': {'latest': '1.0.0'},
                        'versions': {'1.0.0': {**json.loads(package_json(name)), 'dist': dist}}}
            self.routes[registry.rstrip('/')+'/'+name] = ('application/json', json.dumps(document).encode())
            self.routes[path] = ('application/octet-stream', data)

    def origin(self, host):
        return {'registry': f'http://127.0.0.1:{self.port}', 'other': f'http://localhost:{self.port}',
                'port': f'http://127.0.0.1:{self.second.server_address[1]}'}[host]

    def record(self, handler):
        host = next((name for name in ('registry', 'other', 'port')
                     if self.origin(name) == 'http://'+handler.headers.get('Host', '')), 'unknown')
        with self.lock:
            self.requests.append({'host': host, 'path': handler.path,
                                  'authorization': handler.headers.get('Authorization') or '<none>'})


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        self.server.owner.record(self)
        route = self.server.owner.routes.get(self.path.split('?')[0])
        if route is None:
            self.send_error(404)
            return
        kind, body = route
        self.send_response(200)
        self.send_header('Content-Type', kind)
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass


def git(repo, *args):
    return subprocess.run(['git', *args], cwd=repo, env={**os.environ, **GIT_ENV}, check=True,
                          capture_output=True, text=True).stdout.strip()


def git_repository(work):
    """A bare repository whose tag v1 ships the package with marker `git v1`, and whose main has
    moved on to `git v2`; returns (url, v1 commit)."""
    source = work/'git-source'
    bare = work/'git/repo.git'
    source.mkdir(parents=True)
    git(source, 'init', '-q', '-b', 'main')
    commits = []
    for n, date in ((1, '2026-01-01T00:00:00Z'), (2, '2026-01-02T00:00:00Z')):
        folder = source/GIT_PATH
        folder.mkdir(parents=True, exist_ok=True)
        for path, text in package_files(GIT, f'git v{n}', f'1.0.{n - 1}').items():
            (folder/path).write_text(text)
        (source/'README.md').write_text(f'probe repository {n}\n')
        git(source, 'add', '-A')
        subprocess.run(['git', 'commit', '-q', '-m', f'v{n}'], cwd=source, check=True,
                       env={**os.environ, **GIT_ENV, 'GIT_AUTHOR_DATE': date, 'GIT_COMMITTER_DATE': date})
        commits.append(git(source, 'rev-parse', 'HEAD'))
        if n == 1:
            git(source, 'tag', 'v1')
    bare.parent.mkdir(parents=True)
    git(work, 'clone', '-q', '--bare', str(source), str(bare))
    return f'file://{bare}', commits[0]


def assemble(work, registry, lock):
    """The probe project under `work` with its registry, tarball and repository; returns (project,
    environment)."""
    project = work/'project'
    for folder in ('Assets', 'ProjectSettings'):
        shutil.copytree(ROOT/folder, project/folder)
    (project/'Packages').mkdir(parents=True)
    (project/'LocalPackages').mkdir()
    tarball = f'{TARBALL}-1.0.0.tgz'
    (project/'LocalPackages'/tarball).write_bytes(npm_tarball(package_files(TARBALL, 'tarball')))
    url, commit = git_repository(work)
    base = registry.origin('registry')
    dependencies = {name: '1.0.0' for name, *_ in REGISTRIES}
    dependencies[TARBALL] = f'file:../LocalPackages/{tarball}'
    git_version = f'git+{url}?path=/{GIT_PATH}#main'
    dependencies[GIT] = git_version
    manifest = {'dependencies': dependencies,
                'scopedRegistries': [{'name': name, 'url': base+path, 'scopes': [name]}
                                     for name, path, *_ in REGISTRIES]}
    (project/'Packages/manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
    if lock:
        locked = {name: {'version': '1.0.0', 'depth': 0, 'source': 'registry', 'dependencies': {},
                         'url': base+path} for name, path, *_ in REGISTRIES}
        locked[TARBALL] = {'version': dependencies[TARBALL], 'depth': 0, 'source': 'local-tarball', 'dependencies': {}}
        locked[GIT] = {'version': git_version, 'depth': 0, 'source': 'git', 'dependencies': {}, 'hash': commit}
        (project/'Packages/packages-lock.json').write_text(json.dumps({'dependencies': locked}, indent=2)+'\n')
    config = work/'upmconfig.toml'
    sections = []
    for path, entries in NPM_AUTH:
        lines = [f'[npmAuth."{base}{path}"]']
        for key, value in entries.items():
            lines.append(f'{key} = {str(value).lower() if isinstance(value, bool) else json.dumps(value)}')
        sections.append('\n'.join(lines))
    config.write_text('\n\n'.join(sections)+'\n')
    return project, {'UPM_USER_CONFIG_FILE': str(config), 'UPM_CACHE_ROOT': str(work/'upm-cache')}


def add_requests(capture, registry):
    """The registry's requests appended to the capture's values: for each host and path, the
    Authorization values sent (sorted, distinct)."""
    seen = {}
    for request in registry.requests:
        seen.setdefault(f'request.{request["host"]}{request["path"]}', set()).add(request['authorization'])
    path = capture/value_probe.VALUES
    lines = [line for line in path.read_text().splitlines() if line and json.loads(line)['k'] != 'complete']
    lines += [json.dumps({'k': key, 'v': ' | '.join(sorted(values))}) for key, values in sorted(seen.items())]
    lines.append(json.dumps(value_probe.COMPLETE))
    path.write_text('\n'.join(lines)+'\n')


def serve():
    registry = Registry()
    for server in (registry, registry.second):
        threading.Thread(target=server.serve_forever, daemon=True).start()
    return registry


def stop(registry):
    registry.shutdown()
    registry.second.shutdown()


def record(label, lock):
    work = run_root(ROOT, label)
    registry = serve()
    try:
        project, env = assemble(work, registry, lock)
        capture = work/'capture'
        capture.mkdir(parents=True)
        job = editor_runner.EditorJob(project, work/'editor.log', work, ['-executeMethod', METHOD], batch=True,
                                      graphics=True, env={**env, VARIABLE: str(capture)}, timeout=900,
                                      label='packagesources-'+label)
        print(editor_runner.summary(editor_runner.run(job)))
    finally:
        stop(registry)
        (work/'requests.json').write_text(json.dumps(registry.requests, indent=1)+'\n')
    problem = editor_runner.result_problem(work, gpu=True) or editor_runner.jsonl_complete()(capture)
    if problem:
        raise SystemExit(f'{work}: {problem}')
    add_requests(capture, registry)
    lock_file = project/'Packages/packages-lock.json'
    if lock_file.exists():
        shutil.copyfile(lock_file, work/'packages-lock.after.json')
    print('captured', capture)
    return 0


def private_cache(work):
    """A cache root for the engine whose package store is empty: every other entry of the user's
    cache is linked, so nothing else is rebuilt."""
    real = Path(os.environ['UNBOUND_CACHE']) if os.environ.get('UNBOUND_CACHE') else \
        Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound'
    cache = work/'cache'
    cache.mkdir(parents=True)
    for entry in real.iterdir() if real.is_dir() else ():
        if entry.name != 'packages':
            (cache/entry.name).symlink_to(entry)
    return cache


def gate(label):
    label = label or time.strftime('gate-%Y%m%d-%H%M%S-')+str(os.getpid())
    work = run_root(ROOT, label)
    registry = serve()
    try:
        project, env = assemble(work, registry, lock=True)
        capture = work/'engine'
        capture.mkdir(parents=True)
        cli = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI') or str(TESTS.parent/'target/release/unbound')
        env = {**os.environ, **env, VARIABLE: str(capture), 'UNBOUND_CACHE': str(private_cache(work))}
        env.pop('UNBOUND_OFFLINE', None)
        with (work/'engine.log').open('w') as log:
            code = subprocess.run([str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run',
                                   '-p', str(project), '--no-watch', '--headless', '30'],
                                  env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    finally:
        stop(registry)
    expected = value_probe.read_values(reference_root(ROOT)/value_probe.VALUES)
    try:
        add_requests(capture, registry)
        actual = value_probe.read_values(capture/value_probe.VALUES)
    except (OSError, ValueError) as error:
        print(f'packagesources: no complete engine capture ({error}); engine exit {code}; log {work}/engine.log')
        return 1
    log_text = (work/'engine.log').read_text(errors='replace')
    leaked = [token for _, entries in NPM_AUTH for key, token in entries.items()
              if key in ('token', '_auth') and token in log_text]
    matched, failures = value_probe.compare(expected, actual, 0)
    for failure in failures:
        print(json.dumps(failure))
    for token in leaked:
        print(f'the engine log holds a registry credential ({token[:6]}...)')
    print(f'packagesources: {matched}/{len(expected)} values match, {len(failures) + len(leaked)} failures; '
          f'engine exit {code}; logs {work}')
    return 1 if failures or leaked or code else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['record', 'publish', 'gate'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--no-lock', action='store_true', help='record without Packages/packages-lock.json')
    args = parser.parse_args()
    if args.mode == 'record':
        if not args.label:
            parser.error('--label is required')
        return record(args.label, not args.no_lock)
    if args.mode == 'publish':
        return value_probe.publish(ROOT, args.labels)
    return gate(args.label)


if __name__ == '__main__':
    sys.exit(main())
