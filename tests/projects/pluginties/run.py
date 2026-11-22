#!/usr/bin/env python3
"""Make disposable plug-in projects, record their Unity graph, and check Unbound's."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import difflib
import gzip
from hashlib import sha256
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tarfile
import time

HERE = Path(__file__).resolve().parent
sys.path[:0] = [str(HERE.parents[1]), str(HERE.parent/'compilesetup')]
import editor_runner
import probe_paths
from value_probe import engine_cli
from build_assets import csc, meta_text, write_metas

PACKAGE = 'com.example.archive'
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
    'ExplicitPackage': ('Assets/A', PACKAGE, 'e5', '1a'),
    'ExplicitAssets': ('Assets/A', PACKAGE, '1b', 'e4'),
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
DUPLICATE = re.compile(r"Duplicate assembly '(ProbeTie[^']+)' with different versions detected, "
                       r"using '([^']+)' and ignoring '([^']+)'\.")
CONFLICT = re.compile(r"GUID \[([\da-f]{32})\] for asset '([^']+)' conflicts with:\s*"
                      r"'([^']+)'(?: \(current owner\))?\s*Assigning a new guid\.")


def make_tarball(staging, destination):
    destination.parent.mkdir()
    with destination.open('wb') as file, gzip.GzipFile(fileobj=file, mode='wb', mtime=0) as compressed:
        with tarfile.open(fileobj=compressed, mode='w') as archive:
            for path in sorted(staging.iterdir()):
                data = path.read_bytes()
                entry = tarfile.TarInfo('package/'+path.name)
                entry.size = len(data)
                archive.addfile(entry, io.BytesIO(data))


def compile_copy(project, staging, compiler, reference, case, location, prefix, index, conflicts):
    target = (staging if location == PACKAGE else project/location)/(DLL_PREFIX + case + '.dll')
    target.parent.mkdir(parents=True, exist_ok=True)
    # The compiler embeds the output filename as the assembly name.
    defines = []
    if (case, index) in (('PackageNewer', 1), ('AssetsNewer', 0)) or (
        case in ('VersionTie', 'MixedVersionTie') and index > 0
    ):
        defines.append('-define:VERSION_TWO')
    if index:
        defines.append('-define:PACKAGE_API')
    if case == 'ArchiveSecond':
        defines.append('-define:ARCHIVE_SECOND')
    subprocess.run([*compiler, '-nologo', '-noconfig', '-nostdlib', '-deterministic', '-optimize+',
                    '-target:library', f'-r:{reference}', f'-out:{target}', *defines,
                    str(HERE/'Plugin.cs')], check=True)
    guid = prefix + sha256(case.encode()).hexdigest()[len(prefix):32]
    if conflicts and case == 'ArchiveSecond':
        guid = '70000000000000000000000000000000'
    allowed = (case, index) not in (('AssetsExcluded', 0), ('PackageExcluded', 1))
    importer = LEGACY_IMPORTER.replace('ANY', str(int(allowed)))
    explicit = (('ExplicitPackage', 1), ('ExplicitAssets', 0), ('MixedVersionTie', 1))
    if case.startswith('BothExplicit') or (case, index) in explicit:
        importer += '  isExplicitlyReferenced: 1\n'
    target.with_suffix('.dll.meta').write_text(meta_text(target, target.name, guid, importer))


def generate(project, release, conflicts=False):
    probe_paths.project_copy(HERE, project, package_cache=False)
    settings = project/'ProjectSettings'
    settings.mkdir(exist_ok=True)
    (settings/'ProjectVersion.txt').write_text(f'm_EditorVersion: {release}\n')
    (project/'Assets/CompilerControl.cs').write_text('public static class CompilerControl {}\n')
    for name in ('com.example.first', 'com.example.second'):
        embed = project/'Packages'/name
        embed.mkdir()
        (embed/'package.json').write_text(json.dumps({'name': name, 'version': '1.0.0'}))
    archive = project.parent/'archive'
    archive.mkdir()
    manifest = archive/'package.json'
    manifest.write_text(json.dumps({'name': PACKAGE, 'version': '1.0.0'}))
    manifest.with_suffix('.json.meta').write_text(meta_text(manifest, PACKAGE+'/package.json'))
    editor = probe_paths.editor_path()
    editor_data = editor.parents[1] if probe_paths.MACOS_ARM64 else editor.parent/'Data'
    reference = editor_data/'NetStandard/ref/2.1.0/netstandard.dll'
    compiler = csc()
    with ThreadPoolExecutor(max_workers=4) as pool:
        tasks = [pool.submit(compile_copy, project, archive, compiler, reference, case, location, prefix, i, conflicts)
                 for case, row in CASES.items()
                 for i, (location, prefix) in enumerate(zip(row[:2], row[2:]))]
        tasks.extend(pool.submit(compile_copy, project, archive, compiler, reference, case, PACKAGE, '21', 2, conflicts)
                     for case in ('VersionTie', 'MixedVersionTie'))
        for task in tasks:
            task.result()
    make_tarball(archive, project/'Packages/LocalPackages/com.example.archive-1.0.0.tgz')
    for folder, override in [('Automatic', False), ('Explicit', True)]:
        root = project/'Assets'/folder
        root.mkdir()
        asmdef = {'name': 'Probe.'+folder}
        if override:
            asmdef.update(overrideReferences=True, precompiledReferences=[DLL_PREFIX+case+'.dll' for case in CASES])
        (root/(folder+'.asmdef')).write_text(json.dumps(asmdef))
        body = '' if conflicts else 'public static int Value() => ProbeTieArchiveSecond.Marker.PackageOnly();'
        (root/(folder+'Control.cs')).write_text('public static class '+folder+'Control {'+body+'}\n')
    write_metas(project/'Assets', project, set())
    write_metas(project/'Packages', project, set())


def normalize(path, project):
    path = path.replace('\\', '/').removeprefix(project.as_posix()+'/')
    return re.sub(r'^.*(?:PackageCache|tarballs)/'+re.escape(PACKAGE)+r'@[^/]+/', 'Packages/'+PACKAGE+'/', path)


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


def record(run, release, conflicts):
    jobs = []
    for repeat in ('native1', 'native2'):
        out = run/repeat
        project = out/'project'
        generate(project, release, conflicts)
        jobs.append(editor_runner.EditorJob(
            project, out/'editor.log', out, ['-force-glcore', '-quit', '-executeMethod', 'RecordPluginReferences.Record'],
            batch=True, graphics=True, timeout=600, env={'PLUGIN_TIES_OUT': str(out/'graph.tsv')},
        ))
    for job in jobs:
        result = editor_runner.run(job)
        if result['exit_code'] != 0 or result['forced_exit'] or not result['renderer_ok'] or not result['shutdown_logged']:
            raise RuntimeError(f'Editor did not exit normally: {job.out}')
        data = {'graph': read_editor_graph(job.out/'graph.tsv', job.project),
                **diagnostics(job.log.read_text(errors='replace'), job.project)}
        (job.out/'record.json').write_text(json.dumps(data, indent=2)+'\n')


def gate(run, release):
    project = run/'project'
    generate(project, release)
    cli = engine_cli()
    log = run/'engine.log'
    graph = run/'graph.json'
    env = {**os.environ, 'UNBOUND_LOG': 'info', 'UNBOUND_OFFLINE': '1'}
    with graph.open('w') as output, log.open('w') as errors:
        subprocess.run([str(cli), 'graph', '-p', str(project), '--json'],
                       stdout=output, stderr=errors, env=env, check=True)
    actual = {'graph': read_engine_graph(graph, project),
              **diagnostics(log.read_text(errors='replace'), project)}
    candidate = json.dumps(actual, indent=2)+'\n'
    (run/'record.json').write_text(candidate)
    reference = HERE/'Reference'/f'unity-{release}'/'record.json'
    native = reference.read_text()
    if native != candidate:
        print(''.join(difflib.unified_diff(native.splitlines(True), candidate.splitlines(True),
                                           fromfile=str(reference), tofile=str(run/'record.json'))), end='')
        return 1
    command = [str(cli), 'compile', '-p', str(project), '--editor-assemblies']
    if editor_runner.LAUNCHER.is_file():
        command = [str(editor_runner.LAUNCHER), '--kind', 'tool', *command]
    with (run/'compile.log').open('w') as log:
        result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, env=env)
    if result.returncode:
        print(f'pluginties: compilation failed, see {run / "compile.log"}')
        return result.returncode
    print(f"pluginties: {len(actual['graph'])} references, {len(actual['versions'])} version diagnostics, compile succeeded")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish', 'gate'))
    parser.add_argument('--release', default=probe_paths.unity_version())
    parser.add_argument('--label', default=f'run-{time.time_ns()}')
    parser.add_argument('--conflicts', action='store_true', help='record an unresolved GUID collision')
    args = parser.parse_args()
    run = probe_paths.run_root(HERE, args.label).resolve()
    target = HERE/'Reference'/f'unity-{args.release}'/'record.json'
    if args.mode == 'publish':
        if args.conflicts:
            parser.error('collision captures are diagnostic')
        first, second = [(run/repeat/'record.json').read_bytes() for repeat in ('native1', 'native2')]
        if first != second:
            raise RuntimeError(f'independent Editor captures differ: {run}')
        if json.loads(first)['conflicts']:
            raise RuntimeError(f'GUID collision captures are diagnostic: {run}')
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('xb') as output:
            output.write(first)
        return 0
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    run.mkdir(parents=True)
    if args.mode == 'editor':
        record(run, args.release, args.conflicts)
        return 0
    return gate(run, args.release)


if __name__ == '__main__':
    sys.exit(main())
