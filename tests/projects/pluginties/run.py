#!/usr/bin/env python3
"""Make disposable plug-in projects, record their Unity graph, and check Unbound's."""
import argparse
import base64
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager, nullcontext
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


def generate(project, release, conflicts=False, linked_assets=False):
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
    if conflicts:
        cases = {case: [(location, guid) for location in locations]
                 for case, (*locations, guid) in COLLISION_CASES.items()}
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
    return collision_metadata(project) if conflicts else None


GUID_LINE = re.compile(rb'(?m)^guid: ([0-9a-f]{32})')


def collision_metadata(project, packages=None):
    result = {}
    if packages is None:
        package_cache = project/'Library/PackageCache'
        packages = {
            name: next(package_cache.glob(name+'@*'), project.parent/staging)
            for name, staging in ((PACKAGE, 'archive'), (REGISTRY, 'registry'))
        }
    for case, (*locations, _) in COLLISION_CASES.items():
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


def record(run, release, conflicts, linked_assets):
    jobs = []
    original_metadata = {}
    for repeat in ('native1', 'native2'):
        out = run/repeat
        project = out/'project'
        original_metadata[project] = generate(project, release, conflicts, linked_assets)
        jobs.append(editor_runner.EditorJob(
            project, out/'editor.log', out, ['-force-glcore', '-quit', '-executeMethod', 'RecordPluginReferences.Record'],
            batch=True, graphics=True, timeout=600, env={'PLUGIN_TIES_OUT': str(out/'graph.tsv')},
        ))
    for job in jobs:
        with registry_package(job.project) if conflicts else nullcontext({}) as env:
            job.env.update(env)
            run_editor(job)
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


def gate_conflicts(run, release, linked_assets=False, *, existing_unity_database=False):
    project = run/'project'
    before = generate(project, release, conflicts=True, linked_assets=linked_assets)
    database = project/'Library'/'SourceAssetDB'
    database_contents = b'opaque Unity asset database contents for the import control\n'
    if existing_unity_database:
        database.parent.mkdir(exist_ok=True)
        database.write_bytes(database_contents)
    cli = engine_cli()
    cache = run/'cache'
    with registry_package(project) as config:
        result = compare_import(run, project, release, before, cli, cache, config, linked_assets)
    if existing_unity_database and database.read_bytes() != database_contents:
        raise RuntimeError('plugin GUID import modified the Unity asset database')
    return result


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
    cases = [
        ('quoted', duplicate, f"'{guid}'", 'needs an unmeasured metadata rewrite'),
        ('mixed-text', 'Assets/B/Shared.txt', guid, 'the group includes other asset types'),
        ('mixed-native', 'Assets/B/Shared.so', guid, 'the group includes other asset types'),
        ('read-only', duplicate, guid, 'is read-only'),
    ]
    cli = engine_cli()
    env = {**os.environ, 'UNBOUND_LOG': 'info', 'UNBOUND_OFFLINE': '1'}
    for case, second_asset, second_guid, message in cases:
        out = run/case
        project = out/'project'
        metas = fallback_project(project, release, second_asset, second_guid, guid)
        if case == 'read-only':
            metas[1].chmod(0o444)
        before = [meta.read_bytes() for meta in metas]
        compile_log = out/'compile.log'
        if (result := compile_engine(cli, project, compile_log, env)):
            return result
        graph = out/'graph.json'
        if message not in compile_log.read_text(errors='replace'):
            raise RuntimeError(f'{case}: expected fallback diagnostic in {compile_log}: {message}')
        engine_graph(cli, project, graph, out/'engine.log', env)
        plugins = {Path(plugin) for assembly in json.loads(graph.read_text())
                   for plugin in assembly['plugins'] if Path(plugin).name == 'ProbeDup.dll'}
        if plugins != {project/'Assets/A/ProbeDup.dll'}:
            raise RuntimeError(f'{case}: graph changed the prior plugin selection: {plugins}')
        if before != [meta.read_bytes() for meta in metas]:
            raise RuntimeError(f'{case}: fallback altered the plugin metadata')
        print(f'pluginties: {case} preserves selection and metadata')
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
    args = parser.parse_args()
    if args.linked_assets and not args.conflicts:
        parser.error('--linked-assets requires --conflicts')
    run = probe_paths.run_root(HERE, args.label).resolve()
    filename = collision_filename(args.linked_assets) if args.conflicts else 'record.json'
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
        record(run, args.release, args.conflicts, args.linked_assets)
        return 0
    if args.conflicts:
        return gate_conflicts(run, args.release, args.linked_assets)
    return (gate(run, args.release)
            or gate_conflicts(run/'guid-import', args.release, existing_unity_database=True)
            or gate_conflicts(run/'linked-assets', args.release, linked_assets=True)
            or gate_fallbacks(run/'fallbacks', args.release))


if __name__ == '__main__':
    sys.exit(main())
