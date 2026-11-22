#!/usr/bin/env python3
"""Make disposable plug-in projects, record their Unity graph, and check Unbound's."""
import argparse
import base64
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager, nullcontext
import ctypes
import difflib
import gzip
from hashlib import sha1, sha256, sha512
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
from threading import Thread
import time

HERE = Path(__file__).resolve().parent
sys.path[:0] = [str(HERE.parents[1]), str(HERE.parent/'compilesetup')]
import editor_runner
import probe_paths
from value_probe import engine_cli
from build_assets import csc, meta_text, write_metas

PACKAGE = 'com.example.archive'
REGISTRY = 'com.example.guid-registry'
DLL_PREFIX = 'ProbeTie'
# Each row places two copies. *Second controls compare against path order.
# LowNibble and GuidWord controls separate textual and byte ordering.
CASES = {
    'EmbeddedFirst': ('Assets/A', 'Packages/com.example.first', '1e', 'ee'),
    'EmbeddedSecond': ('Assets/A', 'Packages/com.example.first', 'ef', '1f'),
    'ArchiveFirst': ('Assets/A', PACKAGE, '12', 'ec'),
    'ArchiveSecond': ('Assets/A', PACKAGE, 'ed', '13'),
    'AssetsFirst': ('Assets/A', 'Assets/B', '14', 'eb'),
    'AssetsSecond': ('Assets/A', 'Assets/B', 'ea', '15'),
    'PackagesFirst': ('Packages/com.example.first', 'Packages/com.example.second', '16', 'e9'),
    'PackagesSecond': ('Packages/com.example.first', 'Packages/com.example.second', 'e8', '17'),
    'WordBoundary': ('Assets/A', 'Assets/B', '18000000ef', '19ffffff1f'),
    'FirstByte': ('Assets/A', 'Assets/B', '120000ee', '13000011'),
    'LowNibbleFirst': ('Assets/A', 'Assets/B', '31', '22'),
    'LowNibbleSecond': ('Assets/A', 'Assets/B', '22', '31'),
    'GuidWord2': ('Assets/A', 'Assets/B', '500000007fffffff', '5000000080000000'),
    'GuidWord3': ('Assets/A', 'Assets/B', '500000007000008080000000', '50000000700000807fffffff'),
    'GuidWord4': ('Assets/A', 'Assets/B', '5000000070000080700000807fffffff', '50000000700000807000008080000000'),
    'PackageNewer': ('Assets/A', PACKAGE, '1a', 'e7'),
    'AssetsNewer': ('Assets/A', PACKAGE, 'e6', '1b'),
    'PackageExcluded': ('Assets/A', PACKAGE, 'e5', '1c'),
    'AssetsExcluded': ('Assets/A', PACKAGE, '1d', 'e4'),
    'VersionTie': ('Assets/A', 'Assets/B', '1e', 'ef'),
    'MixedVersionTie': ('Assets/A', 'Assets/B', '1e', 'ef'),
    'ExplicitVersionTie': ('Assets/A', 'Assets/B', '1e', '80'),
    'ExplicitPackage': ('Assets/A', PACKAGE, 'e5', '1a'),
    'ExplicitAssets': ('Assets/A', PACKAGE, '1b', 'e4'),
    'ExplicitPackageEarlier': ('Assets/A', PACKAGE, '2e', '80'),
    'ExplicitAssetsEarlier': ('Assets/A', PACKAGE, '80', '2e'),
    'BothExplicit': ('Assets/A', PACKAGE, 'e3', '1c'),
    'BothExplicitAssets': ('Assets/A', 'Assets/B', '7f', '80'),
    'BothExplicitEmbedded': ('Assets/A', 'Packages/com.example.first', '7f', '80'),
    'BothExplicitPackages': ('Packages/com.example.first', 'Packages/com.example.second', '7f', '80'),
    'BothExplicitArchive': ('Assets/A', PACKAGE, '7f', '80'),
}
LEGACY_IMPORTER = """PluginImporter:
  serializedVersion: 2
  isPreloaded: 0
  isOverridable: 0
  platformData:
  - first:
      Any:
    second:
      enabled: ANY
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
"""
COLLISION_CASES = {
    # The two ends of GUID ordering distinguish GUID reassignment followed by
    # normal tie-breaking from always selecting the original GUID's owner.
    'ArchiveKeepsReference': ('Assets/A', PACKAGE, '0'*31+'1'),
    'ArchiveLosesReference': ('Assets/A', PACKAGE, 'f'*32),
    'RegistryKeepsReference': ('Assets/A', REGISTRY, '0'*30+'02'),
    'EmbeddedWithArchive': ('Packages/com.example.first', PACKAGE, 'a1'*16),
    'EmbeddedWithRegistry': ('Packages/com.example.first', REGISTRY, 'a5'*16),
    'RegistryThreeWay': ('Assets/A', 'Packages/com.example.first', REGISTRY, 'a6'*16),
    'ArchiveThreeWay': ('Assets/A', 'Packages/com.example.first', PACKAGE, 'a7'*16),
    'EmbeddedWithAssets': ('Assets/A', 'Packages/com.example.first', 'a2'*16),
    'BothInAssets': ('Assets/A', 'Assets/B', 'a3'*16),
    'BothEmbedded': ('Packages/com.example.first', 'Packages/com.example.second', 'a4'*16),
}
ESTABLISHED_CASES = {
    'RegistryKeepsReference': COLLISION_CASES['RegistryKeepsReference'],
    'RegistryLosesReference': ('Assets/A', REGISTRY, 'f'*32),
}
DUPLICATE = re.compile(r"Duplicate assembly '(ProbeTie[^']+)' with different versions detected, "
                       r"using '([^']+)' and ignoring '([^']+)'\.")
CONFLICT = re.compile(r"GUID \[([\da-f]{32})\] for asset '([^']+)' conflicts with:\s*"
                      r"'([^']+)'(?: \(current owner\))?\s*Assigning a new guid\.")


def collision_filename(linked_assets):
    return 'guid-import-linked.json' if linked_assets else 'guid-import.json'


def make_tarball(staging, destination):
    destination.parent.mkdir(exist_ok=True, parents=True)
    with destination.open('wb') as file, gzip.GzipFile(fileobj=file, mode='wb', mtime=0) as compressed:
        with tarfile.open(fileobj=compressed, mode='w') as archive:
            for path in sorted(staging.iterdir()):
                data = path.read_bytes()
                entry = tarfile.TarInfo('package/'+path.name)
                entry.size = len(data)
                archive.addfile(entry, io.BytesIO(data))


def compile_copy(project, staging, compiler, reference, case, location, prefix, index, conflicts):
    target = staging.get(location, project/location)/(DLL_PREFIX + case + '.dll')
    target.parent.mkdir(parents=True, exist_ok=True)
    # The compiler embeds the output filename as the assembly name.
    defines = []
    if (case, index) in (('PackageNewer', 1), ('AssetsNewer', 0)) or (
        case in ('VersionTie', 'MixedVersionTie', 'ExplicitVersionTie') and index > 0
    ):
        defines.append('-define:VERSION_TWO')
    if index:
        defines.append('-define:PACKAGE_API')
    if case in ('ArchiveSecond', 'ArchiveKeepsReference'):
        defines.append('-define:ARCHIVE_SECOND')
    if case == 'RegistryKeepsReference':
        defines.append('-define:REGISTRY_CONTROL')
    subprocess.run([*compiler, '-nologo', '-noconfig', '-nostdlib', '-deterministic', '-optimize+',
                    '-target:library', f'-r:{reference}', f'-out:{target}', *defines,
                    str(HERE/'Plugin.cs')], check=True)
    guid = prefix + sha256(case.encode()).hexdigest()[len(prefix):32]
    allowed = (case, index) not in (('AssetsExcluded', 0), ('PackageExcluded', 1))
    importer = LEGACY_IMPORTER.replace('ANY', str(int(allowed)))
    explicit = (
        ('ExplicitPackage', 1), ('ExplicitAssets', 0), ('MixedVersionTie', 1),
        ('ExplicitPackageEarlier', 1), ('ExplicitAssetsEarlier', 0), ('ExplicitVersionTie', 1),
    )
    if case.startswith('BothExplicit') or (case, index) in explicit:
        importer += '  isExplicitlyReferenced: 1\n'
    metadata = meta_text(target, target.name, guid, importer)
    if conflicts and case == 'ArchiveLosesReference' and index == 0:
        metadata = metadata.replace('\n', '\r\n')
    target.with_suffix('.dll.meta').write_bytes(metadata.encode())


def generate(project, release, conflicts=False, linked_assets=False, established=False):
    probe_paths.project_copy(HERE, project, package_cache=False)
    settings = project/'ProjectSettings'
    settings.mkdir(exist_ok=True)
    (settings/'ProjectVersion.txt').write_text(f'm_EditorVersion: {release}\n')
    (project/'Assets/CompilerControl.cs').write_text('public static class CompilerControl {}\n')
    for name in ('com.example.first', 'com.example.second'):
        embed = project/'Packages'/name
        embed.mkdir()
        (embed/'package.json').write_text(json.dumps({'name': name, 'version': '1.0.0'}))
    staging = {PACKAGE: project.parent/'archive'}
    if conflicts:
        staging[REGISTRY] = project.parent/'registry'
    for name, directory in staging.items():
        directory.mkdir()
        manifest = directory/'package.json'
        manifest.write_text(json.dumps({'name': name, 'version': '1.0.0'}))
        manifest.with_suffix('.json.meta').write_text(meta_text(manifest, name+'/package.json'))
    editor = probe_paths.editor_path()
    editor_data = editor.parents[1] if probe_paths.MACOS_ARM64 else editor.parent/'Data'
    reference = editor_data/'NetStandard/ref/2.1.0/netstandard.dll'
    compiler = csc()
    collisions = ESTABLISHED_CASES if established else COLLISION_CASES
    if conflicts:
        cases = {case: [(location, guid) for location in locations]
                 for case, (*locations, guid) in collisions.items()}
    else:
        cases = {case: list(zip(row[:2], row[2:])) for case, row in CASES.items()}
    with ThreadPoolExecutor(max_workers=4) as pool:
        tasks = [pool.submit(compile_copy, project, staging, compiler, reference, case, location, prefix, i, conflicts)
                 for case, copies in cases.items()
                 for i, (location, prefix) in enumerate(copies)]
        if not conflicts:
            tasks.extend(pool.submit(compile_copy, project, staging, compiler, reference, case, PACKAGE, '21', 2, conflicts)
                         for case in ('VersionTie', 'MixedVersionTie', 'ExplicitVersionTie'))
        for task in tasks:
            task.result()
    make_tarball(staging[PACKAGE], project/'Packages/LocalPackages/com.example.archive-1.0.0.tgz')
    if conflicts:
        make_tarball(staging[REGISTRY], project.parent/'registry.tgz')
    for folder, override in [('Automatic', False), ('Explicit', True)]:
        root = project/'Assets'/folder
        root.mkdir()
        asmdef = {'name': 'Probe.'+folder}
        if override:
            asmdef.update(overrideReferences=True, precompiledReferences=[DLL_PREFIX+case+'.dll' for case in cases])
        (root/(folder+'.asmdef')).write_text(json.dumps(asmdef))
        body = 'public static int Value() => ProbeTieArchiveSecond.Marker.PackageOnly();'
        if conflicts:
            body = 'public static int Value() => ProbeTieRegistry.Marker.PackageOnly();'
        (root/(folder+'Control.cs')).write_text('public static class '+folder+'Control {'+body+'}\n')
    write_metas(project/'Assets', project, set())
    write_metas(project/'Packages', project, set())
    if linked_assets:
        (project/'Assets').rename(project.parent/'external-assets')
        (project/'Assets').symlink_to('../external-assets', target_is_directory=True)
    return collision_metadata(project, cases=collisions) if conflicts else None


GUID_LINE = re.compile(rb'(?m)^guid: ([0-9a-f]{32})')


def collision_metadata(project, packages=None, cases=COLLISION_CASES):
    result = {}
    if packages is None:
        package_cache = project/'Library/PackageCache'
        packages = {
            name: next(package_cache.glob(name+'@*'), project.parent/staging)
            for name, staging in ((PACKAGE, 'archive'), (REGISTRY, 'registry'))
        }
    for case, (*locations, _) in cases.items():
        filename = DLL_PREFIX+case+'.dll.meta'
        for location in locations:
            if location in packages:
                file = packages[location]/filename
                path = 'Packages/'+location+'/'+filename
            else:
                path = location+'/'+filename
                file = project/path
            result[path] = file.read_bytes()
    return result


def collision_record(before, after, graph, log):
    metas = []
    guid_by_path = {}
    counts = Counter(GUID_LINE.search(data)[1] for data in after.values())
    for path, old in sorted(before.items()):
        new = after[path]
        guid = GUID_LINE.search(new)[1]
        guid_by_path[path.removesuffix('.meta')] = guid
        before_guid = GUID_LINE.search(old)[1]
        metas.append({
            'path': path,
            'same_guid': before_guid == guid,
            'distinct_guid': counts[guid] == 1,
            'before': sha256(GUID_LINE.sub(b'guid: <GUID>', old)).hexdigest(),
            'after': sha256(GUID_LINE.sub(b'guid: <GUID>', new)).hexdigest(),
        })
    # The GUIDs are randomly assigned. Each graph row must still select the
    # earlier GUID in Unity's ordering. Keep the full capture in the run.
    def guid_order(guid):
        return [guid[i+1:i+2]+guid[i:i+1] for i in range(0, len(guid), 2)]
    for assembly, path in graph:
        filename = path.split('/')[-1]
        tied = [candidate for candidate in guid_by_path if candidate.split('/')[-1] == filename]
        winner = min(tied, key=lambda candidate: guid_order(guid_by_path[candidate]))
        if path != winner:
            raise RuntimeError(f'{assembly} references {path}, expected {winner} from assigned GUIDs')
    # Preserve the known low/high GUID reference choices and verify counts
    # for the cases whose choice can vary between fresh imports.
    random_winners = {DLL_PREFIX+case+'.dll' for case in COLLISION_CASES
                      if case not in ('ArchiveKeepsReference', 'ArchiveLosesReference', 'RegistryKeepsReference')}
    references = sorted((assembly, Path(path).name if Path(path).name in random_winners else path)
                        for assembly, path in graph)
    messages = re.findall(r"GUID \[([0-9a-f]{32})\] for asset '([^']*ProbeTie[^']*)' conflicts with:\n"
                          r"((?:  '[^']+'(?: \(current owner\))?\n)+)"
                          r"Assigning a new guid\.", log)
    # The peer paths form an unordered list. Independent linked-root captures vary
    # in their order. Keep every path, the owner label and all other message text.
    conflicts = sorted({f"GUID [{guid}] for asset '{path}' conflicts with:\n"
                        + ''.join(sorted(peers.splitlines(keepends=True))) + 'Assigning a new guid.'
                        for guid, path, peers in messages})
    return {'references': references, 'metas': metas, 'conflicts': conflicts}


def normalize(path, project):
    path = path.replace('\\', '/').removeprefix(project.as_posix()+'/')
    packages = '|'.join(map(re.escape, (PACKAGE, REGISTRY)))
    return re.sub(r'^.*(?:PackageCache|tarballs|packages)/('+packages+r')@[^/]+/', r'Packages/\1/', path)


@contextmanager
def registry_package(project):
    work = project.parent
    tarball = (work/'registry.tgz').read_bytes()
    routes = {}

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            route = routes.get(self.path.split('?')[0])
            if route is None:
                self.send_error(404)
                return
            content_type, body = route
            self.send_response(200)
            self.send_header('Content-Type', content_type)
            self.send_header('Content-Length', len(body))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *args):
            pass

    with ThreadingHTTPServer(('127.0.0.1', 0), Handler) as server:
        url = f'http://127.0.0.1:{server.server_port}'
        document = {
            '_id': REGISTRY, 'name': REGISTRY, 'dist-tags': {'latest': '1.0.0'},
            'versions': {'1.0.0': {'name': REGISTRY, 'version': '1.0.0', 'dist': {
                'tarball': url+'/registry.tgz', 'shasum': sha1(tarball).hexdigest(),
                'integrity': 'sha512-'+base64.b64encode(sha512(tarball).digest()).decode(),
            }}},
        }
        routes['/'+REGISTRY] = ('application/json', json.dumps(document).encode())
        routes['/registry.tgz'] = ('application/octet-stream', tarball)
        manifest = project/'Packages/manifest.json'
        data = json.loads(manifest.read_text())
        data['dependencies'][REGISTRY] = '1.0.0'
        data['scopedRegistries'] = [{'name': 'Probe Registry', 'url': url, 'scopes': [REGISTRY]}]
        manifest.write_text(json.dumps(data))
        config = work/'upmconfig.toml'
        config.write_text('')
        thread = Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            yield {'UPM_USER_CONFIG_FILE': str(config), 'UPM_CACHE_ROOT': str(work/'upm-cache')}
        finally:
            server.shutdown()
            thread.join()


def diagnostics(log, project):
    def describe(value):
        return normalize(value, project).replace(', PublicKeyToken=null', '')
    return {
        'versions': sorted({(dll, describe(winner), describe(other))
                            for dll, winner, other in DUPLICATE.findall(log)}),
        'conflicts': sorted({(guid, normalize(path, project), normalize(owner, project))
                             for guid, path, owner in CONFLICT.findall(log) if DLL_PREFIX in path}),
    }


def read_editor_graph(path, project):
    rows = path.read_text().splitlines()
    if len(rows) < 2 or rows.pop() != 'complete':
        raise RuntimeError(f'incomplete Editor graph: {path}')
    return sorted((name, normalize(reference, project)) for name, reference in
                  (line.split('\t', 1) for line in rows))


def read_engine_graph(path, project):
    return sorted((asm['name'], normalize(ref, project))
                  for asm in json.loads(path.read_text()) for ref in asm['plugins']
                  if Path(ref).name.startswith(DLL_PREFIX))


def record(run, release, conflicts, linked_assets, established=False):
    jobs = []
    original_metadata = {}
    for repeat in ('native1', 'native2'):
        out = run/repeat
        project = out/'project'
        original_metadata[project] = generate(project, release, conflicts, linked_assets, established)
        jobs.append(editor_runner.EditorJob(
            project, out/'editor.log', out, ['-force-glcore', '-quit', '-executeMethod', 'RecordPluginReferences.Record'],
            batch=True, graphics=True, timeout=600, env={'PLUGIN_TIES_OUT': str(out/'graph.tsv')},
        ))
    for job in jobs:
        with registry_package(job.project) if conflicts else nullcontext({}) as env:
            job.env.update(env)
            run_editor(job)
            if established:
                record_established(job, original_metadata[job.project])
                continue
        data = {'graph': read_editor_graph(job.out/'graph.tsv', job.project),
                **diagnostics(job.log.read_text(errors='replace'), job.project)}
        (job.out/'record.json').write_text(json.dumps(data, indent=2)+'\n')
        if conflicts:
            lock = json.loads((job.project/'Packages/packages-lock.json').read_text())
            if lock['dependencies'][REGISTRY]['source'] != 'registry':
                raise RuntimeError('Editor did not resolve the registry package')
            result = collision_record(original_metadata[job.project],
                                      collision_metadata(job.project),
                                      data['graph'], job.log.read_text(errors='replace'))
            (job.out/collision_filename(linked_assets)).write_text(json.dumps(result, indent=2)+'\n')


def run_editor(job):
    result = editor_runner.run(job)
    if result['exit_code'] != 0 or result['forced_exit'] or not result['renderer_ok'] or not result['shutdown_logged']:
        raise RuntimeError(f'Editor did not exit normally: {job.out}')


def record_established(first, before):
    project = first.project
    for path, authored in before.items():
        if path.startswith('Assets/'):
            # Reproduce a restored authored .meta with the original file stamp and existing
            # imported records for both paths. Neither DLL nor its importer settings change.
            file = project/path
            stamp = file.stat()
            if stamp.st_size != len(authored) or file.read_bytes() == authored:
                raise RuntimeError(f'fresh import did not replace just the GUID: {path}')
            file.write_bytes(authored)
            os.utime(file, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
    out = first.out/'reopen'
    out.mkdir()
    paths = [path.removesuffix('.meta') for path in sorted(before)]
    env = {**first.env, 'PLUGIN_TIES_OUT': str(out/'graph.tsv'),
           'PLUGIN_TIES_GUID_OUT': str(out/'guids.tsv'),
           'PLUGIN_TIES_GUID_PATHS': '\n'.join(paths)}
    run_editor(editor_runner.EditorJob(
        project, out/'editor.log', out, first.args, batch=True, graphics=True, timeout=600, env=env,
    ))
    after = collision_metadata(project, cases=ESTABLISHED_CASES)
    rows = (out/'guids.tsv').read_text().splitlines()
    if not rows or rows.pop() != 'complete':
        raise RuntimeError('incomplete established asset GUID capture')
    guid_rows = sorted(line.split('\t') for line in rows)
    if [path for path, _, _ in guid_rows] != paths or any(path != owner for path, _, owner in guid_rows):
        raise RuntimeError(f'inconsistent registered asset paths: {guid_rows}')
    recorded_guids = {path: guid for path, guid, _ in guid_rows}
    result = established_record(before, after, read_editor_graph(out/'graph.tsv', project),
                                (out/'editor.log').read_text(errors='replace'), recorded_guids)
    (first.out/'guid-import-established.json').write_text(json.dumps(result, indent=2)+'\n')


def established_record(before, after, graph, log, registered):
    return {
        'references': graph,
        'metas': [{
            'path': path,
            'authored_sha256': sha256(data).hexdigest(),
            'reopened_sha256': sha256(after[path]).hexdigest(),
            'registered_uses_authored_guid': bytes.fromhex(registered[path.removesuffix('.meta')])
            == bytes.fromhex(GUID_LINE.search(data)[1].decode()),
        } for path, data in sorted(before.items())],
        'new_guid_diagnostics': [line for line in log.splitlines()
                                 if "GUID [" in line and DLL_PREFIX in line and "conflicts with:" in line],
    }


def seed_registered_guids(lmdb, project, registered):
    """Create the small read-only input for replaying the established GUID control."""
    class Value(ctypes.Structure):
        _fields_ = [('size', ctypes.c_size_t), ('data', ctypes.c_char_p)]

    handle = ctypes.c_void_p
    env, txn = handle(), handle()
    mdb = {
        'env_create': [ctypes.POINTER(handle)],
        'env_set_maxdbs': [handle, ctypes.c_uint],
        'env_open': [handle, ctypes.c_char_p, ctypes.c_uint, ctypes.c_uint],
        'txn_begin': [handle, handle, ctypes.c_uint, ctypes.POINTER(handle)],
        'dbi_open': [handle, ctypes.c_char_p, ctypes.c_uint, ctypes.POINTER(ctypes.c_uint)],
        'put': [handle, ctypes.c_uint, ctypes.POINTER(Value), ctypes.POINTER(Value), ctypes.c_uint],
        'txn_commit': [handle],
        'txn_abort': [handle],
        'env_close': [handle],
    }
    for name, inputs in mdb.items():
        api = getattr(lmdb, 'mdb_'+name)
        api.argtypes = inputs
        api.restype = None if name in ('txn_abort', 'env_close') else ctypes.c_int
        mdb[name] = api

    def call(name, *args):
        result = mdb[name](*args)
        if result:
            raise RuntimeError(f'LMDB {name} failed: {result}')

    database = project/'Library/SourceAssetDB'
    database.parent.mkdir(exist_ok=True)
    call('env_create', ctypes.byref(env))
    try:
        call('env_set_maxdbs', env, 2)
        call('env_open', env, os.fsencode(database), 0x4000, 0o600)
        call('txn_begin', env, None, 0, ctypes.byref(txn))
        for table, to_guid in ((b'PathToGuid', True), (b'GuidToPath', False)):
            db = ctypes.c_uint()
            call('dbi_open', txn, table, 0x40000, ctypes.byref(db))
            for path, guid in registered.items():
                binary_guid = bytes.fromhex(''.join(guid[i+1]+guid[i] for i in range(0, 32, 2)))
                if to_guid:
                    key, data = path.lower().encode()+b'\0', binary_guid+bytes(32)
                else:
                    key, data = binary_guid, path.encode()+b'\0'
                call('put', txn, db, Value(len(key), key), Value(len(data), data), 0)
        outcome = mdb['txn_commit'](txn)
        txn = handle()
        if outcome:
            raise RuntimeError(f'LMDB txn_commit failed: {outcome}')
    finally:
        if txn:
            mdb['txn_abort'](txn)
        mdb['env_close'](env)
    return database


def engine_graph(cli, project, graph, log, env):
    command = tool_command(cli, 'graph', '-p', str(project), '--json')
    with graph.open('w') as output, log.open('w') as errors:
        subprocess.run(command, stdout=output, stderr=errors, env=env, check=True)


def compare(actual, reference, output):
    candidate = json.dumps(actual, indent=2)+'\n'
    output.write_text(candidate)
    native = reference.read_text()
    if native != candidate:
        print(''.join(difflib.unified_diff(native.splitlines(True), candidate.splitlines(True),
                                           fromfile=str(reference), tofile=str(output))), end='')
        return False
    return True


def tool_command(cli, *args):
    command = [str(cli), *args]
    if editor_runner.LAUNCHER.is_file():
        command = [str(editor_runner.LAUNCHER), '--kind', 'tool', *command]
    return command


def compile_engine(cli, project, log, env):
    command = tool_command(cli, 'compile', '-p', str(project), '--editor-assemblies')
    with log.open('w') as output:
        result = subprocess.run(command, stdout=output, stderr=subprocess.STDOUT, env=env)
    if result.returncode:
        print(f'pluginties: compilation failed, see {log}')
    return result.returncode


def gate(run, release):
    project = run/'project'
    generate(project, release)
    cli = engine_cli()
    log = run/'engine.log'
    graph = run/'graph.json'
    env = {**os.environ, 'UNBOUND_LOG': 'info', 'UNBOUND_OFFLINE': '1'}
    engine_graph(cli, project, graph, log, env)
    actual = {'graph': read_engine_graph(graph, project),
              **diagnostics(log.read_text(errors='replace'), project)}
    reference = HERE/'Reference'/f'unity-{release}'/'record.json'
    if not compare(actual, reference, run/'record.json'):
        return 1
    if (result := compile_engine(cli, project, run/'compile.log', env)):
        return result
    print(f"pluginties: {len(actual['graph'])} references, {len(actual['versions'])} version diagnostics, compile succeeded")
    return 0


def gate_conflicts(run, release, linked_assets=False):
    project = run/'project'
    before = generate(project, release, conflicts=True, linked_assets=linked_assets)
    cli = engine_cli()
    cache = run/'cache'
    with registry_package(project) as config:
        return compare_import(run, project, release, before, cli, cache, config, linked_assets)


def gate_established(run, release):
    if not sys.platform.startswith('linux'):
        print('pluginties: skipping established database replay outside Linux')
        return 0
    try:
        lmdb = ctypes.CDLL('liblmdb.so.0')
    except OSError:
        print('pluginties: established database replay requires liblmdb.so.0 on Linux')
        return 1
    project = run/'project'
    before = generate(project, release, conflicts=True, established=True)
    registered = {path.removesuffix('.meta'):
                  ('59'*16 if 'KeepsReference' in path else '5a'*16) if path.startswith('Assets/')
                  else GUID_LINE.search(data)[1].decode()
                  for path, data in before.items()}
    cli = engine_cli()
    cache = run/'cache'
    with registry_package(project) as config:
        env = {**os.environ, **config, 'UNBOUND_CACHE': str(cache), 'UNBOUND_LOG': 'info'}
        env.pop('UNBOUND_OFFLINE', None)
        # Resolve package files before seeding the database, as happens on an established project.
        engine_graph(cli, project, run/'unregistered-graph.json', run/'unregistered.log', env)
        database = seed_registered_guids(lmdb, project, registered)
        lock = database.with_name(database.name+'-lock')
        database_before = database.read_bytes(), lock.read_bytes()
        log = run/'compile.log'
        if (result := compile_engine(cli, project, log, env)):
            return result
        graph = run/'graph.json'
        engine_graph(cli, project, graph, run/'engine.log', env)
        unregistered = read_engine_graph(run/'unregistered-graph.json', project)
        refreshed_meta = next(project/path for path in before
                              if path.startswith('Assets/') and 'KeepsReference' in path)
        stamp = refreshed_meta.stat()
        os.utime(refreshed_meta, ns=(stamp.st_atime_ns, database.stat().st_mtime_ns + 1_000_000_000))
        stale_graph = run/'stale-graph.json'
        stale_log = run/'stale.log'
        engine_graph(cli, project, stale_graph, stale_log, env)
        os.utime(refreshed_meta, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
        stale = [row for row in read_engine_graph(stale_graph, project) if 'KeepsReference' in row[1]]
        if (stale != [row for row in unregistered if 'KeepsReference' in row[1]]
                or 'changed after registration' not in stale_log.read_text()):
            raise RuntimeError('an asset changed after registration did not use the existing selection')
    packages = {REGISTRY: cache/'packages'/f'{REGISTRY}@1.0.0'}
    after = collision_metadata(project, packages, ESTABLISHED_CASES)
    actual = established_record(before, after, read_engine_graph(graph, project),
                                log.read_text(errors='replace'), registered)
    filename = 'guid-import-established.json'
    reference = HERE/'Reference'/f'unity-{release}'/filename
    if not compare(actual, reference, run/filename):
        return 1
    if database_before != (database.read_bytes(), lock.read_bytes()):
        raise RuntimeError('compilation altered the Unity asset database')
    check_snapshot_cleanup(project)
    print(f"pluginties: {len(actual['references'])} established references and {len(actual['metas'])} unchanged metadata files match, compile succeeded")
    return 0


def check_snapshot_cleanup(project):
    copies = list((project/'Library/Unbound').glob('plugin-import-*.tmp'))
    if copies:
        raise RuntimeError(f'leftover private database or metadata copies: {copies}')


def fallback_project(project, release, second_asset, second_guid, guid):
    probe_paths.project_copy(HERE, project, package_cache=False)
    settings = project/'ProjectSettings'
    settings.mkdir(exist_ok=True)
    (settings/'ProjectVersion.txt').write_text(f'm_EditorVersion: {release}\n')
    manifest = project/'Packages/manifest.json'
    document = json.loads(manifest.read_text())
    document['dependencies'].pop(PACKAGE)
    manifest.write_text(json.dumps(document))
    plugin = HERE.parent/'compilesetup/Assets/Plugins/Dup/ProbeDup.dll'
    metas = []
    for name, value in [('Assets/A/ProbeDup.dll', guid), (second_asset, second_guid)]:
        asset = project/name
        asset.parent.mkdir(parents=True, exist_ok=True)
        importer = ''
        if asset.suffix == '.dll':
            shutil.copyfile(plugin, asset)
            importer = LEGACY_IMPORTER.replace('ANY', '1')
        else:
            asset.write_text('plain test asset')
        meta = asset.with_name(asset.name+'.meta')
        meta.write_text(f'fileFormatVersion: 2\nguid: {value}\n'+importer)
        metas.append(meta)
    return metas


def gate_fallbacks(run, release):
    guid = '1a'*16
    duplicate = 'Assets/B/ProbeDup.dll'
    linux = sys.platform.startswith('linux')
    bad_database = ('invalid asset database file' if linux
                    else 'the established Unity asset database is unmeasured on this platform')
    cases = [
        ('bad-database', duplicate, guid, bad_database),
        ('quoted', duplicate, f"'{guid}'", 'needs an unmeasured metadata rewrite'),
        ('mixed-text', 'Assets/B/Shared.txt', guid, 'the group includes other asset types'),
        ('mixed-native', 'Assets/B/Shared.so', guid, 'the group includes other asset types'),
        ('read-only', duplicate, guid, 'is read-only'),
    ]
    if linux:
        cases.append(('truncated-database', duplicate, guid, 'truncated Unity asset database'))
    cli = engine_cli()
    env = {**os.environ, 'UNBOUND_LOG': 'info', 'UNBOUND_OFFLINE': '1'}
    for case, second_asset, second_guid, message in cases:
        out = run/case
        project = out/'project'
        metas = fallback_project(project, release, second_asset, second_guid, guid)
        database = project/'Library/SourceAssetDB'
        if case == 'read-only':
            metas[1].chmod(0o444)
        if case == 'bad-database':
            database.parent.mkdir(exist_ok=True)
            database.touch()
        if case == 'truncated-database':
            seed_registered_guids(ctypes.CDLL('liblmdb.so.0'), project, {
                'Assets/A/ProbeDup.dll': guid, duplicate: '2b'*16,
            })
            with database.open('r+b') as file:
                if file.seek(0, os.SEEK_END) <= 8192:
                    raise RuntimeError('the valid database does not extend beyond its metadata pages')
                file.truncate(8192)
        before = [meta.read_bytes() for meta in metas]
        database_before = database.read_bytes() if database.exists() else None
        compile_log = out/'compile.log'
        if (result := compile_engine(cli, project, compile_log, env)):
            return result
        graph_log = out/'engine.log'
        engine_graph(cli, project, out/'graph.json', graph_log, env)
        for log in (compile_log, graph_log):
            if message not in log.read_text(errors='replace'):
                raise RuntimeError(f'{case}: expected fallback diagnostic in {log}: {message}')
        if before != [meta.read_bytes() for meta in metas]:
            raise RuntimeError(f'{case}: fallback altered the plugin metadata')
        if database_before is not None and database_before != database.read_bytes():
            raise RuntimeError(f'{case}: fallback altered the Unity asset database')
        check_snapshot_cleanup(project)
        print(f'pluginties: {case} falls back in compile and graph with unchanged metadata')
    return 0


def compare_import(run, project, release, before, cli, cache, config, linked_assets):
    env = {**os.environ, **config, 'UNBOUND_CACHE': str(cache), 'UNBOUND_LOG': 'info'}
    env.pop('UNBOUND_OFFLINE', None)
    log = run/'compile.log'
    if (result := compile_engine(cli, project, log, env)):
        return result
    graph = run/'graph.json'
    engine_graph(cli, project, graph, run/'engine.log', env)
    archive, = (cache/'packages/tarballs').glob(PACKAGE+'@*')
    packages = {PACKAGE: archive, REGISTRY: cache/'packages'/f'{REGISTRY}@1.0.0'}
    after = collision_metadata(project, packages)
    actual = collision_record(before, after, read_engine_graph(graph, project), log.read_text(errors='replace'))
    filename = collision_filename(linked_assets)
    reference = HERE/'Reference'/f'unity-{release}'/filename
    if not compare(actual, reference, run/filename):
        return 1
    if (result := compile_engine(cli, project, run/'recompile.log', env)):
        return result
    if after != collision_metadata(project, packages):
        raise RuntimeError('compiling again changed the imported metadata')
    print(f"pluginties: {len(actual['metas'])} metadata files and {len(actual['conflicts'])} GUID conflicts match, compile succeeded")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish', 'gate'))
    parser.add_argument('--release', default=probe_paths.unity_version())
    parser.add_argument('--label', default=f'run-{time.time_ns()}')
    parser.add_argument('--conflicts', action='store_true', help='test duplicate GUID import')
    parser.add_argument('--linked-assets', action='store_true', help='put the Assets root outside the project')
    parser.add_argument('--established', action='store_true', help='reopen an established database')
    args = parser.parse_args()
    if args.linked_assets and not args.conflicts:
        parser.error('--linked-assets requires --conflicts')
    if args.established and (not args.conflicts or args.linked_assets):
        parser.error('--established requires --conflicts with an ordinary Assets root')
    run = probe_paths.run_root(HERE, args.label).resolve()
    filename = collision_filename(args.linked_assets) if args.conflicts else 'record.json'
    if args.established:
        filename = 'guid-import-established.json'
    target = HERE/'Reference'/f'unity-{args.release}'/filename
    if args.mode == 'publish':
        first, second = [(run/repeat/filename).read_bytes() for repeat in ('native1', 'native2')]
        if first != second:
            raise RuntimeError(f'independent Editor captures differ: {run}')
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('xb') as output:
            output.write(first)
        return 0
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    run.mkdir(parents=True)
    if args.mode == 'editor':
        record(run, args.release, args.conflicts, args.linked_assets, args.established)
        return 0
    if args.established:
        return gate_established(run, args.release)
    if args.conflicts:
        return gate_conflicts(run, args.release, args.linked_assets)
    return (gate(run, args.release)
            or gate_conflicts(run/'guid-import', args.release)
            or gate_conflicts(run/'linked-assets', args.release, linked_assets=True)
            or gate_established(run/'established', args.release)
            or gate_fallbacks(run/'fallbacks', args.release))


if __name__ == '__main__':
    sys.exit(main())
