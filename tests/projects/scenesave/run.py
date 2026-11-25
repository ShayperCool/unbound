#!/usr/bin/env python3
"""Scene and asset saves, compared with the Unity Editor's files.

Each case runs the probe's tests (Unity Test Framework) on a fresh copy of the project, in the
Editor (`editor`) or in Unbound (`unbound`), and keeps the rows the tests log and every project
file the run added, changed or removed (Library, Logs, Temp and UserSettings aside). `compare`
checks a capture against `Reference/unity-<release>/` (or another capture); `gate` runs Unbound and
compares it with the reference.

Cases: `edit` (Edit Mode SaveEditTests), `play-nosave` and `play-save` (Play Mode SavePlayTests
NoSave and Save, each alone), `prefab-api` (Edit Mode PrefabApiTests), `prefab-writes` (Edit Mode
PrefabWriteTests), `create-assets` (Edit Mode CreateAssetTests), `engine-assets` (Edit Mode
EngineAssetTests), `prefab-nested` (Edit Mode NestedPrefabTests), `settings-writes` (Edit Mode
SettingsWriteTests), `build-identity` and `build-identity-defaults` (Edit Mode BuildIdentityTests), `mesh-bounds` (Edit Mode
MeshBoundsTests), `native-saves` (Edit Mode NativeSaveTests), `clip-assets` (Edit Mode ClipAssetTests), `controller-assets` (Edit Mode
ControllerAssetTests), `controller-additions` (Edit Mode ControllerAdditionTests), `modification-timing` (Edit Mode ModificationTimingTests), `scene-api-rest`
(Edit Mode SceneApiRestTests), `build-scenes` (Edit Mode BuildSceneTests), `generic-refs` (Edit Mode GenericRefTests),
`session-imports` (Edit Mode SessionImportTests), `build-index-edit` (Edit Mode BuildIndexTests) and
`build-index-play` (Play Mode BuildIndexPlayTests), `label-metas` (Edit Mode LabelMetaTests),
`refresh-changes` (Edit Mode RefreshChangeTests).
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import wave

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/"tests"))
sys.path.insert(0, str(PROBE))
from probe_paths import editor_path, reference_root, run_root  # noqa: E402
import scenesave_compare  # noqa: E402

CASES = {
    'edit': ('EditMode', 'SaveEditTests.SaveFlow'),
    'play-nosave': ('PlayMode', 'SavePlayTests.NoSave'),
    'play-save': ('PlayMode', 'SavePlayTests.Save'),
    'prefab-api': ('EditMode', 'PrefabApiTests.PrefabApi'),
    'prefab-writes': ('EditMode', 'PrefabWriteTests.PrefabWrites'),
    'create-assets': ('EditMode', 'CreateAssetTests.CreateAssets'),
    'engine-assets': ('EditMode', 'EngineAssetTests.EngineAssets'),
    'prefab-nested': ('EditMode', 'NestedPrefabTests.NestedPrefabs'),
    'settings-writes': ('EditMode', 'SettingsWriteTests.SettingsWrites'),
    'build-identity': ('EditMode', 'BuildIdentityTests.Persist'),
    'build-identity-defaults': ('EditMode', 'BuildIdentityTests.DefaultsAndPunctuation'),
    'mesh-bounds': ('EditMode', 'MeshBoundsTests.MeshBounds'),
    'native-saves': ('EditMode', 'NativeSaveTests.NativeSaves'),
    'clip-assets': ('EditMode', 'ClipAssetTests.ClipAssets'),
    'controller-assets': ('EditMode', 'ControllerAssetTests.ControllerAssets'),
    'controller-additions': ('EditMode', 'ControllerAdditionTests.ControllerAdditions'),
    'modification-timing': ('EditMode', 'ModificationTimingTests.ModificationTiming'),
    'scene-api-rest': ('EditMode', 'SceneApiRestTests.SceneApiRest'),
    'build-scenes': ('EditMode', 'BuildSceneTests.BuildScenes'),
    'generic-refs': ('EditMode', 'GenericRefTests.GenericRefs'),
    'session-imports': ('EditMode', 'SessionImportTests.SessionImports'),
    'build-index-edit': ('EditMode', 'BuildIndexTests.EditMode'),
    'build-index-play': ('PlayMode', 'BuildIndexPlayTests.PlayMode'),
    'label-metas': ('EditMode', 'LabelMetaTests.LabelMetas'),
    'refresh-changes': ('EditMode', 'RefreshChangeTests.RefreshChanges'),
}
SKIPPED = {'Library', 'LibraryUnbound', 'Logs', 'Temp', 'UserSettings', 'obj', 'run.py', 'README.md', 'Reference',
           'gate.json', 'scenesave_compare.py', 'test_compare.py', '__pycache__'}


def project_files(root):
    files = {}
    for path in sorted(root.rglob('*')):
        rel = path.relative_to(root)
        if rel.parts[0] in SKIPPED or not path.is_file():
            continue
        files[rel.as_posix()] = path.read_bytes()
    return files


def copy_project(destination):
    """The probe's authored files (no Library: each run imports the project afresh)."""
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        source = PROBE/name
        if source.exists():
            shutil.copytree(source, destination/name, symlinks=True)


def seed_identifier_defaults(project):
    settings = project/'ProjectSettings/ProjectSettings.asset'
    text = settings.read_bytes()
    for old, new in ((b'  productName: testrunner\n', b'  productName: My project\n'),
                     (b'  applicationIdentifier: {}\n', b'  applicationIdentifier:\n    Server: \n')):
        if text.count(old) != 1:
            raise ValueError(f'{settings}: expected one {old!r}')
        text = text.replace(old, new)
    settings.write_bytes(text)


def seed_stale_build_scene(project):
    """An EditorBuildSettings.asset listing Base.unity under a path it no longer has (a scene moved
    outside the Editor)."""
    settings = project/'ProjectSettings/EditorBuildSettings.asset'
    text = settings.read_bytes()
    old = b'  m_Scenes: []\n'
    if text.count(old) != 1:
        raise ValueError(f'{settings}: expected an empty scene list')
    settings.write_bytes(text.replace(old, b'  m_Scenes:\n  - enabled: 1\n    path: Assets/Old/Moved.unity\n'
                                           b'    guid: e0fd96c04ed5f4230bfb1fc1aa060f13\n'))


# A TextureImporter section as the Editor writes it, DefaultTexturePlatform, Android and iOS listed
# (the modules the recording and the gating installations may have: only a missing Standalone is
# added by either), without ignorePngGamma, swizzle, streamingMipmaps and the Standalone platform
# (as a .meta of an older release may lack them).
PARTIAL_TEXTURE_META = '''fileFormatVersion: 2
guid: GUIDHERE
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 1
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  applyGammaDecoding: 0
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: Android
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: iOS
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData: 
    physicsShape: []
    bones: []
    spriteID: 
    internalID: 0
    vertices: []
    indices: 
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {}
  mipmapLimitGroupName: 
  pSDRemoveMatte: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
'''


def seed_label_metas(project):
    """Assets/Labels: assets of each common kind with a .meta holding their GUID alone, and two
    textures whose TextureImporter section lacks some settings and the Standalone platform."""
    labels = project/'Assets/Labels'
    labels.mkdir()
    copies = {'Tex.png': 'Textures/Probe.png', 'MatEdit.mat': 'Materials/MatEdit.mat', 'Thing.prefab': 'Prefabs/Thing.prefab',
              'Level.unity': 'Scenes/Second.unity',
              'Bouncy.physicMaterial': 'Native/Bouncy.physicMaterial', 'Move.anim': 'Native/Move.anim',
              'Basic.controller': 'Existing/Basic.controller', 'DataSaved.asset': 'Data/DataSaved.asset',
              'Partial.png': 'Textures/Probe.png', 'PartialKept.png': 'Textures/Probe.png'}
    for name, source in copies.items():
        shutil.copyfile(project/'Assets'/source, labels/name)
    texts = {'Notes.txt': 'notes\n', 'Data.json': '{"a": 1}\n', 'Style.uss': '.probe { color: red; }\n', 'Other.xyz': 'xyz\n',
             'Unlit.shader': 'Shader "Probe/Unlit" { SubShader { Pass { Color (1,0,0,1) } } }\n'}
    for name, text in texts.items():
        (labels/name).write_bytes(text.encode())
    with wave.open(str(labels/'Beep.wav'), 'wb') as clip:
        clip.setnchannels(1)
        clip.setsampwidth(2)
        clip.setframerate(22050)
        clip.writeframes(bytes(2 * 2205))
    (labels/'Folder').mkdir()
    folder_meta = 'fileFormatVersion: 2\nguid: {}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    (labels.parent/'Labels.meta').write_bytes(folder_meta.format('5a1b2c3d4e5f40718293a4b5c6d7e8f9').encode())
    names = sorted(list(copies) + list(texts) + ['Beep.wav', 'Folder'])
    for index, name in enumerate(names):
        guid = f'{0x7a5e0000 + index:08x}' + '4c1b4d2e8f3a6b5c7d9e0f12'
        if name.startswith('Partial'):
            text = PARTIAL_TEXTURE_META.replace('GUIDHERE', guid)
        else:
            text = f'fileFormatVersion: 2\nguid: {guid}\n'
        (labels/(name + '.meta')).write_bytes(text.encode())


def seed_build_index_list(project, play):
    """An EditorBuildSettings.asset listing Second disabled, Base, a missing scene, Second under a
    stale path, Base disabled and Assets/Scenes/Deep/Third.unity (a copy of Second added to the
    project); for Play mode also Base enabled again."""
    settings = project/'ProjectSettings/EditorBuildSettings.asset'
    text = settings.read_bytes()
    old = b'  m_Scenes: []\n'
    if text.count(old) != 1:
        raise ValueError(f'{settings}: expected an empty scene list')
    entries = ((0, b'Assets/Scenes/Second.unity', b'6f748856d2f6b48bba33779b860ce87a'),
               (1, b'Assets/Scenes/Base.unity', b'e0fd96c04ed5f4230bfb1fc1aa060f13'),
               (1, b'Assets/Scenes/Missing.unity', b'0123456789abcdef0123456789abcdef'),
               (1, b'Assets/Old/Moved.unity', b'6f748856d2f6b48bba33779b860ce87a'),
               (0, b'Assets/Scenes/Base.unity', b'e0fd96c04ed5f4230bfb1fc1aa060f13'))
    entries += ((1, b'Assets/Scenes/Deep/Third.unity', b'9a3e5c1d7b2f4e6a8c0d1e2f3a4b5c6d'),)
    if play:
        entries += ((1, b'Assets/Scenes/Base.unity', b'e0fd96c04ed5f4230bfb1fc1aa060f13'),)
    deep = project/'Assets/Scenes/Deep'
    deep.mkdir()
    shutil.copyfile(project/'Assets/Scenes/Second.unity', deep/'Third.unity')
    importer = b'DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    (deep.parent/'Deep.meta').write_bytes(b'fileFormatVersion: 2\nguid: 3b6f0e2a9c1d4f5e8a7b6c5d4e3f2a1b\nfolderAsset: yes\n' + importer)
    (deep/'Third.unity.meta').write_bytes(b'fileFormatVersion: 2\nguid: 9a3e5c1d7b2f4e6a8c0d1e2f3a4b5c6d\n' + importer)
    seeded = b'  m_Scenes:\n' + b''.join(b'  - enabled: %d\n    path: %s\n    guid: %s\n' % entry for entry in entries)
    settings.write_bytes(text.replace(old, seeded))


def run_case(kind, case, out, cli=None):
    platform, test_filter = CASES[case]
    scratch = run_root(PROBE, 'work')
    scratch.mkdir(parents=True, exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix=f'{case}-', dir=scratch))
    project = work/'scenesave'
    copy_project(project)
    if case == 'build-identity-defaults':
        seed_identifier_defaults(project)
    if case == 'build-scenes':
        seed_stale_build_scene(project)
    if case.startswith('build-index-'):
        seed_build_index_list(project, play=case == 'build-index-play')
    if case == 'label-metas':
        seed_label_metas(project)
    rows = work/'rows.jsonl'
    env = dict(os.environ, SCENESAVE_LOG=str(rows))
    results = work/'results.xml'
    log = work/'run.log'
    if kind == 'editor':
        command = [str(editor_path()), '-batchmode', '-nographics', '-accept-apiupdate', '-projectPath', str(project),
                   '-runTests', '-testPlatform', platform, '-testFilter', test_filter,
                   '-testResults', str(results), '-logFile', str(log)]
    else:
        command = [cli or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound'), 'test', '-p', str(project),
                   '--platform', platform, '--filter', test_filter, '--results', str(results)]
    with open(log, 'ab') as output:
        code = subprocess.run(command, env=env, stdout=output, stderr=subprocess.STDOUT, timeout=1800).returncode
    before = project_files(PROBE)
    after = project_files(project)
    if out.exists():
        shutil.rmtree(out)
    (out/'files').mkdir(parents=True)
    changed = []
    for rel, data in after.items():
        if before.get(rel) != data:
            target = out/'files'/rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            changed.append(rel)
    removed = [rel for rel in before if rel not in after]
    summary = {'case': case, 'exit': code, 'changed': changed, 'removed': removed}
    (out/'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    shutil.copy(rows, out/'rows.jsonl') if rows.exists() else (out/'rows.jsonl').write_text('')
    if (work/'snapshots').is_dir():
        shutil.copytree(work/'snapshots', out/'snapshots')
    if results.exists():
        shutil.copy(results, out/'results.xml')
    shutil.copy(log, out/'run.log')
    shutil.rmtree(work, ignore_errors=True)
    print(f'{case}: exit {code}, {len(changed)} files written, {len(removed)} removed -> {out}')
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command', required=True)
    for name in ('editor', 'unbound'):
        p = sub.add_parser(name)
        p.add_argument('--case', choices=sorted(CASES), action='append')
        p.add_argument('--out', type=Path)
        p.add_argument('--release')
    c = sub.add_parser('compare')
    c.add_argument('reference', type=Path)
    c.add_argument('actual', type=Path)
    c.add_argument('--case', choices=sorted(CASES), action='append')
    f = sub.add_parser('compare-file')
    f.add_argument('reference', type=Path)
    f.add_argument('actual', type=Path)
    g = sub.add_parser('gate')
    g.add_argument('--release')
    args = parser.parse_args()
    if getattr(args, 'release', None):
        os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    if args.command in ('editor', 'unbound'):
        out = args.out or run_root(PROBE, args.command)
        for case in args.case or sorted(CASES):
            run_case(args.command, case, out/case)
        return 0
    if args.command == 'compare':
        return scenesave_compare.compare_runs(args.reference, args.actual, args.case or sorted(CASES))
    if args.command == 'compare-file':
        problem = scenesave_compare.compare_file(args.reference, args.actual, args.actual.name)
        print(problem or 'identical')
        return int(problem is not None)
    out = run_root(PROBE, 'gate')
    for case in sorted(CASES):
        run_case('unbound', case, out/case)
    return scenesave_compare.compare_runs(reference_root(PROBE), out, sorted(CASES), report=out/'report.json')


if __name__ == '__main__':
    sys.exit(main())
