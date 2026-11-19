#!/usr/bin/env python3
"""Rebuilds the probe's generated inputs (they are committed; run this only after changing a
source): the precompiled plugins from PluginSources/, the local tarball package
packages Packages/LocalPackages/<name>-1.0.0.tgz from PackageSources/<name>, and the `.meta` of
every file and folder under Assets/ and Packages/ (GUIDs derived from the path, so a rebuild
writes the same files). Uses the C# compiler of the .NET SDK and the Editor's netstandard 2.1
reference assembly, deterministically.

Plugins sharing an assembly name (README.md): ProbeDup 1.0.0.0 in Assets/Plugins/Dup and 2.0.0.0
in the embedded package, whose `.meta` has no importer settings; ProbeSame 1.0.0.0 in
Assets/Plugins/Same and in the embedded package; ProbeOrder 1.0.0.0 in Assets/Plugins/Order1
(GUID ff...) and Assets/Plugins/Order2 (GUID 00...); ProbeAlias 1.0.0.0 as Assets/Plugins/Alias/ProbeAlias.dll
and 2.0.0.0 as the package's ProbeAliasNew.dll, both with Auto Reference off."""
import gzip
import hashlib
import io
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1]))
import probe_paths  # noqa: E402

PACKAGE = HERE/'Packages/com.probe.setup'
TARBALLS = ['com.probe.consumer']
# (source, output, `.meta` GUID or None for a path-derived one, importer settings or not or
# 'explicit' for Auto Reference off)
PLUGINS = [
    ('ProbeDup.v1.cs', HERE/'Assets/Plugins/Dup/ProbeDup.dll', None, True),
    ('ProbeDup.v2.cs', PACKAGE/'Plugins/ProbeDup.dll', None, False),
    ('ProbeSame.Assets.cs', HERE/'Assets/Plugins/Same/ProbeSame.dll', None, True),
    ('ProbeSame.Package.cs', PACKAGE/'Plugins/ProbeSame.dll', None, True),
    ('ProbeOrder.Order1.cs', HERE/'Assets/Plugins/Order1/ProbeOrder.dll', 'f'*32, True),
    ('ProbeOrder.Order2.cs', HERE/'Assets/Plugins/Order2/ProbeOrder.dll', '0'*31+'1', True),
    # One assembly name under two file names (version 2 is ProbeAlias.dll renamed), referenced
    # only where an assembly names them: both in one assembly do not compile (CS1704).
    ('ProbeAlias.v1.cs', HERE/'Assets/Plugins/Alias/ProbeAlias.dll', None, 'explicit'),
    ('ProbeAlias.v2.cs', PACKAGE/'Plugins/ProbeAliasNew.dll', None, 'explicit'),
]

PLUGIN_IMPORTER = '''PluginImporter:
  externalObjects: {}
  serializedVersion: 3
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
    Any:
      enabled: 1
      settings: {}
  userData:
  assetBundleName:
  assetBundleVariant:
'''
IMPORTERS = {
    '.cs': '''MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
''',
    '.asmdef': '''AssemblyDefinitionImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
''',
    '.json': '''PackageManifestImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
''',
    '.unity': '''DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
''',
}
FOLDER = '''folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
'''


def netstandard():
    editor = probe_paths.editor_path()
    root = editor.parents[1] if sys.platform == 'darwin' else editor.parent/'Data'
    return root/'NetStandard/ref/2.1.0/netstandard.dll'


def csc():
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    sdk = sorted((Path(dotnet).resolve().parent/'sdk').glob('10.*'))[-1]
    return [dotnet, str(sdk/'Roslyn/bincore/csc.dll')]


def build(source, output):
    """Compiles `source` into `output`; the assembly is named after the source's first part (the
    file can be named otherwise: ProbeAliasNew.dll holds ProbeAlias)."""
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        built = Path(tmp)/(source.name.split('.')[0]+'.dll')
        subprocess.run(csc() + ['-nologo', '-noconfig', '-nostdlib', '-deterministic', '-optimize+', '-debug-',
                                '-target:library', f'-out:{built}', f'-r:{netstandard()}', str(source)], check=True)
        shutil.copyfile(built, output)


def guid_of(relative):
    return hashlib.md5(('compilesetup/'+relative).encode()).hexdigest()


def meta_text(path, relative, guid=None, importer=None):
    if importer is None:
        importer = FOLDER if path.is_dir() else IMPORTERS.get(path.suffix, IMPORTERS['.unity'])
    return f'fileFormatVersion: 2\nguid: {guid or guid_of(relative)}\n' + importer


def write_metas(root, base, skip):
    """A `.meta` beside every file and folder under `root` that has none of its own yet."""
    for path in sorted(root.rglob('*')):
        if path.suffix in ('.meta', '.tgz') or path.name == 'manifest.json' or path in skip:
            continue
        if path.is_dir() and (path.name == 'LocalPackages' or (path/'package.json').exists()):
            continue  # a package root or a folder of packages: not an asset
        meta = path.with_name(path.name+'.meta')
        if not meta.exists():
            meta.write_text(meta_text(path, str(path.relative_to(base))))


def tarball(source, output):
    """An npm tarball (`package/` prefix) of `source` with a `.meta` for every entry (a package in
    the cache is read-only: the Editor ignores assets without one), the same bytes every run."""
    raw = io.BytesIO()
    entries = []
    for path in sorted(source.rglob('*')):
        relative = path.relative_to(source).as_posix()
        if path.is_file():
            entries.append((relative, path.read_bytes()))
        entries.append((relative+'.meta', meta_text(path, 'tarball/'+relative).encode()))
    with tarfile.open(fileobj=raw, mode='w', format=tarfile.USTAR_FORMAT) as tar:
        for name, data in sorted(entries):
            info = tarfile.TarInfo('package/'+name)
            info.size, info.mtime, info.mode = len(data), 0, 0o644
            tar.addfile(info, io.BytesIO(data))
    output.write_bytes(gzip.compress(raw.getvalue(), mtime=0))


def main():
    for meta in [*(HERE/'Assets').rglob('*.meta'), *(HERE/'Packages').rglob('*.meta')]:
        if meta.name != 'Probe.unity.meta':
            meta.unlink()
    plugin_files = set()
    for source, output, guid, settings in PLUGINS:
        build(HERE/'PluginSources'/source, output)
        relative = str(output.relative_to(HERE))
        importer = PLUGIN_IMPORTER if settings else ''
        if settings == 'explicit':
            importer = importer.replace('isExplicitlyReferenced: 0', 'isExplicitlyReferenced: 1')
        meta = f'fileFormatVersion: 2\nguid: {guid or guid_of(relative)}\n' + importer
        output.with_name(output.name+'.meta').write_text(meta)
        plugin_files.add(output)
    for name in TARBALLS:
        tarball(HERE/'PackageSources'/name, HERE/f'Packages/LocalPackages/{name}-1.0.0.tgz')
    write_metas(HERE/'Assets', HERE, plugin_files)
    write_metas(HERE/'Packages', HERE, plugin_files)


if __name__ == '__main__':
    main()
