#!/usr/bin/env python3
"""Record and compare the PluginImporter's answers for plug-ins whose .meta holds only a GUID
(Unity 6000.0)."""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import time

PROBE = Path(__file__).resolve().parent
sys.path[:0] = [str(PROBE.parents[1]), str(PROBE.parent/'compilesetup')]
import value_probe  # noqa: E402
from build_assets import build  # noqa: E402

BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('ProjectSettings',))]
VARIABLE = 'PLUGIN_DEFAULTS_OUT'
PACKAGE = 'com.unbound.plugindefaults'

# What each plug-in file is: a managed library (compiled), a folder plug-in, or a file whose
# kind its extension gives (a native library, a native source, an Android or web library). Every
# other managed plug-in has a name of its own (Managed<Folder>.dll): the Editor compiles against
# one precompiled assembly per file name.
KINDS = {
    'Managed.dll': 'managed', 'Other.DLL': 'managed', 'native.dll': 'file', 'libnative.so': 'file',
    'libmac.dylib': 'file', 'libstatic.a': 'file', 'library.jar': 'file', 'library.aar': 'file',
    'Source.c': 'file', 'Source.cc': 'file', 'Source.cpp': 'file', 'Header.h': 'file', 'Bridge.m': 'file',
    'Bridge.mm': 'file', 'Native.java': 'file', 'Native.kt': 'file', 'Native.swift': 'file',
    'bridge.jslib': 'file', 'prelude.jspre': 'file', 'Kit.bundle': 'folder', 'Kit.plugin': 'folder',
    'Kit.framework': 'folder', 'Kit.xcframework': 'folder', 'Lib.androidlib': 'folder',
}

# Plug-ins with a .meta of their GUID alone: every kind in Assets/Plugins, then kinds in the
# folders whose names the Editor reads (and some it does not), in a package, in Editor folders.
# No managed plug-in is in a folder of a 64-bit CPU: whether the Editor compiles against it depends
# on the machine's CPU (x86 is no Editor's).
FOLDERS = {
    'Assets/Plugins': list(KINDS),
    'Assets/Other': ['ManagedOther.dll', 'native.dll', 'libnative.so', 'libmac.dylib', 'Kit.bundle', 'Source.cpp',
                     'library.aar', 'libstatic.a', 'bridge.jslib', 'Bridge.mm', 'Kit.framework'],
    'Assets/Plugins/x86': ['native.dll', 'libnative.so', 'ManagedX86.dll', 'libmac.dylib', 'Kit.bundle'],
    'Assets/Plugins/x86_64': ['native.dll', 'libnative.so', 'libmac.dylib', 'Kit.bundle', 'Source.cpp',
                              'library.jar', 'Bridge.mm', 'bridge.jslib'],
    'Assets/Plugins/ARM64': ['native.dll', 'libnative.so', 'libmac.dylib'],
    'Assets/Plugins/Android': ['libnative.so', 'ManagedAndroid.dll', 'native.dll', 'Source.cpp', 'library.jar', 'libmac.dylib',
                               'library.aar', 'Bridge.mm', 'Kit.framework'],
    'Assets/Plugins/Android/libs/arm64-v8a': ['libnative.so'],
    'Assets/Plugins/Android/libs/armeabi-v7a': ['libnative.so'],
    'Assets/Plugins/Android/libs/x86_64': ['libnative.so'],
    'Assets/Plugins/Android/libs/x86': ['libnative.so'],
    'Assets/Plugins/Android/libs/armeabi': ['libnative.so'],
    'Assets/Plugins/Android/arm64-v8a': ['libnative.so'],
    'Assets/Plugins/Android/x86_64': ['libnative.so'],
    'Assets/Libs/arm64-v8a': ['libnative.so'],
    'Assets/Plugins/iOS': ['libstatic.a', 'Bridge.mm', 'ManagediOS.dll', 'native.dll', 'Kit.framework', 'libmac.dylib',
                           'Kit.bundle', 'Kit.xcframework', 'libnative.so', 'library.jar'],
    'Assets/Plugins/WebGL': ['bridge.jslib', 'ManagedWebGL.dll', 'native.dll', 'libnative.so'],
    'Assets/Plugins/macOS': ['libmac.dylib', 'native.dll', 'Kit.bundle'],
    'Assets/Plugins/Editor': ['native.dll', 'ManagedPluginsEditor.dll', 'libnative.so', 'libmac.dylib', 'Kit.bundle', 'library.jar',
                              'Bridge.mm', 'bridge.jslib', 'Source.cpp'],
    'Assets/Editor': ['ManagedEditor.dll', 'native.dll', 'libnative.so', 'libmac.dylib', 'Kit.bundle'],
    'Assets/Foo/Editor': ['ManagedFooEditor.dll', 'native.dll'],
    'Assets/Foo/Plugins/x86_64': ['native.dll'],
    'Assets/Foo/x86_64': ['native.dll', 'libnative.so'],
    'Assets/Plugins/Foo/x86_64': ['native.dll'],
    'Assets/Plugins/WSA': ['native.dll', 'ManagedWSA.dll'],
    'Assets/Plugins/Metro': ['native.dll'],
    'Assets/plugins/x86_64': ['native.dll'],
    'Assets/Plugins/android': ['libnative.so'],
    'Assets/Plugins/arm64': ['native.dll'],
    'Assets/editor': ['ManagedLowerEditor.dll'],
    'Assets/Plugins/x86_64/Editor': ['native.dll'],
    'Assets/Plugins/Editor/x86_64': ['native.dll'],
    'Assets/Editor/Plugins/x86_64': ['native.dll'],
    'Assets/EditorTools': ['native.dll'],
    'Assets/Plugins/Linux/x86_64': ['libnative.so'],
    'Assets/Plugins/Windows/x86_64': ['native.dll'],
    'Assets/Plugins/tvOS': ['native.dll'],
    'Assets/Plugins/visionOS': ['native.dll'],
    'Assets/Plugins/OSX': ['native.dll'],
    'Assets/Plugins/x64': ['native.dll'],
    'Assets/Plugins/Win64': ['native.dll'],
    'Assets/Plugins/Linux64': ['libnative.so'],
    'Assets/Plugins/Switch': ['native.dll'],
    'Assets/Plugins/Editor/Foo/x86_64': ['native.dll'],
    'Assets/Foo/Editor/x86_64': ['native.dll'],
    'Assets/Plugins/Android/Editor': ['libnative.so'],
    'Assets/Plugins/iOS/Editor': ['libstatic.a'],
    'Assets/Foo/Plugins/Android': ['libnative.so'],
    'Assets/Foo/Plugins/iOS': ['libstatic.a'],
    'Assets/Foo/Plugins/WebGL': ['bridge.jslib', 'native.dll'],
    'Assets/Foo/Plugins/WSA': ['native.dll'],
    'Assets/Foo/Plugins/visionOS': ['native.dll'],
    'Assets/Plugins/Foo/Android': ['libnative.so'],
    'Assets/Plugins/Android/libs/mips': ['libnative.so'],
    'Assets/Plugins/Android/libs/arm64': ['libnative.so'],
    'Assets/Plugins/Android/libs/armeabi-v7a/x86_64': ['libnative.so'],
    'Assets/Plugins/Android/libs/arm64-v8a': ['libstatic.a', 'Source.cpp'],
    'Assets/Plugins/iOS/More': ['library.aar', 'Native.java', 'Lib.androidlib', 'bridge.jslib'],
    'Assets/Plugins/WebGL/More': ['library.jar', 'Bridge.mm'],
    'Assets/Plugins/WSA/More': ['library.jar', 'Bridge.mm'],
    'Assets/Plugins/visionOS/More': ['library.jar', 'Bridge.mm', 'Kit.framework'],
    'Assets/Plugins/Android/More': ['bridge.jslib'],
    f'Packages/{PACKAGE}/Runtime': ['ManagedPackage.dll', 'native.dll', 'libmac.dylib'],
    f'Packages/{PACKAGE}/Runtime/Plugins/x86_64': ['native.dll'],
    f'Packages/{PACKAGE}/Editor': ['ManagedPackageEditor.dll'],
    f'Packages/{PACKAGE}/Plugins/Android': ['libnative.so'],
    f'Packages/{PACKAGE}/Runtime/Plugins/Android': ['libnative.so'],
    f'Packages/{PACKAGE}/Plugins/iOS': ['libstatic.a'],
    f'Packages/{PACKAGE}/Plugins/WebGL': ['bridge.jslib', 'native.dll'],
    f'Packages/{PACKAGE}/Plugins/x86_64': ['native.dll'],
}


def entry(name, enabled, settings=None):
    text = f'    {name}:\n      enabled: {enabled}\n      settings:'
    if not settings:
        return text + ' {}\n'
    return text + '\n' + ''.join(f'        {key}: {value}\n' for key, value in settings.items())


EDITOR_OFF = entry('Editor', 0, {'DefaultValueInitialized': 'true'})
# Plug-ins whose .meta was written by hand (as the Editor writes it), for what the importer
# answers about "Any Platform", the Editor and exclusions, and which ones the Editor compiles
# against; one with a section that has no platform settings, one that names another importer, and
# a shared object whose section lacks the Android entry.
CONTROLS = {
    'Assets/Controls/AnyOnly.dll': entry('Any', 1) + EDITOR_OFF,
    'Assets/Controls/AnyExcluding.dll': entry('Any', 1, {'Exclude Android': 1, 'Exclude Editor': 1}) + EDITOR_OFF,
    'Assets/Controls/EditorOnly.dll': entry('Any', 0) + entry('Editor', 1, {'DefaultValueInitialized': 'true'}),
    'Assets/Controls/AnyAndAndroid.dll': entry('Android', 1) + entry('Any', 1, {'Exclude Win64': 1}) + EDITOR_OFF,
    # The Editor's own settings: a CPU and an OS that are no Editor's here (on Linux or macOS).
    'Assets/Controls/EditorX86.dll': entry('Any', 0) + entry('Editor', 1, {'CPU': 'x86', 'DefaultValueInitialized': 'true'}),
    'Assets/Controls/EditorWindows.dll': entry('Any', 0) + entry('Editor', 1, {'DefaultValueInitialized': 'true', 'OS': 'Windows'}),
    'Assets/Controls/libsection.so': entry('Any', 1) + EDITOR_OFF,
    'Assets/Controls/NoPlatforms.dll': None,
    'Assets/Controls/DefaultImporter.dll': 'DefaultImporter',
}
SECTION_HEAD = ('PluginImporter:\n  externalObjects: {}\n  serializedVersion: 3\n  iconMap: {}\n  executionOrder: {}\n'
                '  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n'
                '  validateReferences: 1\n')
SECTION_TAIL = '  userData: \n  assetBundleName: \n  assetBundleVariant: \n'


def cases():
    """The plug-ins' paths. A native library outside Assets/Plugins gets a number after its name: the
    Editor reports two libraries of one name that it could load as an error."""
    paths, seen = [], {}
    for folder, names in FOLDERS.items():
        for name in names:
            stem, dot, extension = name.partition('.')
            if extension.lower() in ('dll', 'so', 'dylib', 'bundle') and not name.startswith('Managed') \
                    and name != 'Other.DLL' and folder != 'Assets/Plugins':
                seen[name] = seen.get(name, 0) + 1
                name = f'{stem}{seen[name]}{dot}{extension}'
            paths.append(f'{folder}/{name}')
    return paths + list(CONTROLS)


def kind(name):
    """A managed library, a folder plug-in or a file, by the plug-in's name (as KINDS gives it)."""
    if name.startswith('Managed'):
        return 'managed'
    stem, _, extension = name.partition('.')
    return KINDS[stem.rstrip('0123456789') + '.' + extension]


def guid(path):
    return hashlib.md5(('plugindefaults/'+path).encode()).hexdigest()


def stage(project):
    """The plug-ins, their .meta files and the case list in a fresh copy of the probe."""
    managed = []
    for path in cases():
        name = path.rsplit('/', 1)[1]
        target = project/path
        target.parent.mkdir(parents=True, exist_ok=True)
        what = ('managed' if name.endswith('.dll') else 'file') if path in CONTROLS else kind(name)
        if what == 'managed':
            managed.append((path, target))
        elif what == 'folder':
            (target/'Contents').mkdir(parents=True)
            (target/'Contents/Info.plist').write_text('<plist/>\n')
        else:
            target.write_text('native plug-in\n')
        header = f'fileFormatVersion: 2\nguid: {guid(path)}'
        control = CONTROLS.get(path, '')
        if control == 'DefaultImporter':
            meta = header + '\nDefaultImporter:\n  externalObjects: {}\n' + SECTION_TAIL
        elif control is None:
            meta = header + '\nPluginImporter:\n  externalObjects: {}\n  serializedVersion: 3\n' + SECTION_TAIL
        elif control:
            meta = header + '\n' + SECTION_HEAD + '  platformData:\n' + control + SECTION_TAIL
        else:
            # The Editor ends a plug-in's new .meta after its GUID, without a line break.
            meta = header
        target.with_name(name+'.meta').write_text(meta)
    # Each managed plug-in is an assembly of its own name (the Editor refuses two of one name).
    with tempfile.TemporaryDirectory() as tmp:
        for i, (path, target) in enumerate(managed):
            source = Path(tmp)/f'PluginDefaults{i:02}.cs'
            source.write_text(f'public static class PluginDefaults{i:02} {{ public const string Path = "{path}"; }}\n')
            build(source, target)
    (project/'Packages'/PACKAGE/'package.json').write_text(json.dumps({'name': PACKAGE, 'version': '1.0.0'}) + '\n')
    (project/'PluginDefaultsCases.txt').write_text('\n'.join(cases()) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=f'check-{time.time_ns()}')
    args = parser.parse_args()
    if any(Path(label).name != label for label in (args.label, *args.labels)):
        parser.error('labels must be single directory names')
    if args.mode == 'record':
        value_probe.record(PROBE, 'ImguiSkinRunner.Record', VARIABLE, args.label, batch=True, base=BASE,
                           macos_batch=True, configure=stage)
        return 0
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels)
    return value_probe.gate(PROBE, VARIABLE, args.label, 0, ['--headless', '120'], base=BASE, configure=stage)


if __name__ == '__main__':
    sys.exit(main())
