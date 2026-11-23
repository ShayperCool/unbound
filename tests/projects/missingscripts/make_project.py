#!/usr/bin/env python3
"""Writes the missing-script probe's assets (scene, prefab, assets, scripts' .meta files).

The missing script is GUID MISSING, which no file of the project has. KnownData (a
ScriptableObject) and KnownHolder (a MonoBehaviour with object fields) are the probe's own scripts.
"""
from pathlib import Path
import hashlib

PROBE = Path(__file__).resolve().parent
ROOT = PROBE/'Assets'
MISSING = '0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e01'
HEADER = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'


def guid(name):
    return hashlib.md5(('missingscripts/' + name).encode()).hexdigest()


def missing_behaviour(file_id, game_object, name=''):
    return (f'--- !u!114 &{file_id}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n'
            f'  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {game_object}}}\n'
            f'  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {{fileID: 11500000, guid: {MISSING}, type: 3}}\n'
            f'  m_Name: {name}\n  m_EditorClassIdentifier: \n  value: 7\n')


def known_data(file_id, name, other=0):
    return (f'--- !u!114 &{file_id}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n'
            f'  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: 0}}\n'
            f'  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {{fileID: 11500000, guid: {guid("KnownData.cs")}, type: 3}}\n'
            f'  m_Name: {name}\n  m_EditorClassIdentifier: \n  number: 3\n  other: {{fileID: {other}}}\n')


def game_object(file_id, transform, components, name):
    listed = ''.join(f'  - component: {{fileID: {c}}}\n' for c in [transform, *components])
    return (f'--- !u!1 &{file_id}\nGameObject:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n'
            f'  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  serializedVersion: 6\n  m_Component:\n{listed}'
            f'  m_Layer: 0\n  m_Name: {name}\n  m_TagString: Untagged\n  m_Icon: {{fileID: 0}}\n  m_NavMeshLayer: 0\n'
            f'  m_StaticEditorFlags: 0\n  m_IsActive: 1\n'
            f'--- !u!4 &{transform}\nTransform:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n'
            f'  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {file_id}}}\n'
            f'  serializedVersion: 2\n  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}\n  m_LocalPosition: {{x: 0, y: 0, z: 0}}\n'
            f'  m_LocalScale: {{x: 1, y: 1, z: 1}}\n  m_ConstrainProportionsScale: 0\n  m_Children: []\n  m_Father: {{fileID: 0}}\n'
            f'  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}\n')


def holder(file_id, game_object_id):
    return (f'--- !u!114 &{file_id}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n'
            f'  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {game_object_id}}}\n'
            f'  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {{fileID: 11500000, guid: {guid("KnownHolder.cs")}, type: 3}}\n'
            f'  m_Name: \n  m_EditorClassIdentifier: \n'
            f'  missingAsset: {{fileID: 11400000, guid: {guid("Data/MissingMain.asset")}, type: 2}}\n'
            f'  missingSubAsset: {{fileID: 5000, guid: {guid("Data/Mixed.asset")}, type: 2}}\n'
            f'  knownMain: {{fileID: 11400000, guid: {guid("Data/Mixed.asset")}, type: 2}}\n')


def meta(name, body):
    return f'fileFormatVersion: 2\nguid: {guid(name)}\n{body}'


NATIVE = 'NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: MAIN\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
PREFAB = 'PrefabImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
SCENE = 'DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
SCRIPT = ('MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n'
          '  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
FOLDER = 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'


def write(path, text, meta_text):
    target = ROOT/path
    target.parent.mkdir(parents=True, exist_ok=True)
    if text is not None:
        target.write_text(text)
    Path(str(target) + '.meta').write_text(meta_text)


def main():
    base_scene = (PROBE.parent/'animatorapi/Assets/Scenes/Probe.unity').read_text()
    roots_at = base_scene.index('--- !u!1660057539')
    scene = (base_scene[:roots_at] + game_object(100, 101, [102, 103], 'Holder') + missing_behaviour(102, 100)
             + holder(103, 100) + base_scene[roots_at:].replace('m_Roots: []', 'm_Roots:\n  - {fileID: 101}'))
    write('Missing/Missing.unity', scene, meta('Missing/Missing.unity', SCENE))
    write('Missing/Data/MissingMain.asset', HEADER + missing_behaviour(11400000, 0, 'MissingMain'),
          meta('Data/MissingMain.asset', NATIVE.replace('MAIN', '11400000')))
    write('Missing/Data/Mixed.asset', HEADER + known_data(11400000, 'Mixed') + missing_behaviour(5000, 0, 'Sub'),
          meta('Data/Mixed.asset', NATIVE.replace('MAIN', '11400000')))
    write('Missing/Data/Linked.asset', HEADER + known_data(11400000, 'Linked', 5000) + missing_behaviour(5000, 0, 'Sub'),
          meta('Data/Linked.asset', NATIVE.replace('MAIN', '11400000')))
    write('Missing/Data/Twice.asset', HEADER + known_data(11400000, 'Twice') + missing_behaviour(5000, 0, 'One')
          + missing_behaviour(6000, 0, 'Two'), meta('Data/Twice.asset', NATIVE.replace('MAIN', '11400000')))
    write('Resources/MissingResource.asset', HEADER + missing_behaviour(11400000, 0, 'MissingResource'),
          meta('Resources/MissingResource.asset', NATIVE.replace('MAIN', '11400000')))
    write('Resources/MissingPrefab.prefab', HEADER + game_object(200, 201, [202], 'MissingPrefab') + missing_behaviour(202, 200),
          meta('Resources/MissingPrefab.prefab', PREFAB))
    for script in ('KnownData.cs', 'KnownHolder.cs'):
        write('Missing/Scripts/' + script, None, meta(script, SCRIPT))
    for folder in ('Missing', 'Missing/Data', 'Missing/Scripts', 'Resources', 'Editor'):
        Path(str(ROOT/folder) + '.meta').write_text(meta('folder/' + folder, FOLDER))
    write('Editor/MissingScriptsProbe.cs', None, meta('MissingScriptsProbe.cs', SCRIPT))


if __name__ == '__main__':
    main()
