#!/usr/bin/env python3
"""Writes the scene activation witness fixture offline, in the YAML Unity 6000.0 saves.

    make_fixture.py            writes Assets/Prefabs, Assets/Scenes, the folder/script metas and
                               ProjectSettings/EditorBuildSettings.asset of this probe

No Editor makes or imports these files. The scene settings documents (fileIDs 1-4) are copied
from the Unity-saved tests/projects/editorcallbacks/Assets/Scenes/Second.unity; the GameObject,
Transform, MonoBehaviour, PrefabInstance, stripped and SceneRoots documents follow the forms of
that probe's Unity-saved Main.unity, Plain.prefab and Outer.prefab. Unlike a Unity save, the
scene's documents are deliberately NOT in ascending fileID order, so document order, numeric
fileID order, root order and hierarchy pre-order all differ (see README.md). WakeCopy.unity is a
byte copy of Wake.unity under another GUID. fixture.py reads the written files back
independently (it does not import this table) and derives the bindings and predictions.
"""
import hashlib
from pathlib import Path

PROBE = Path(__file__).resolve().parent
TEMPLATE = PROBE.parent/'editorcallbacks/Assets/Scenes/Second.unity'
TEMPLATE_SHA256 = '5d2290145dbdaf7e3c43ec107cc9d60924703fa1138c07c1a926682d927ea3d9'
HEADER = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'


def guid(name):
    return hashlib.md5(('unbound/tests/projects/sceneactivation/'+name).encode()).hexdigest()


SCRIPT = {c: guid('Assets/Scripts/'+c+'.cs') for c in ('W', 'W2', 'WE', 'WakeLog', 'WakeDriver')}
EDITOR_SCRIPT = guid('Assets/Editor/WakeRunner.cs')
X_GUID, Y_GUID = guid('Assets/Prefabs/X.prefab'), guid('Assets/Prefabs/Y.prefab')
WAKE_GUID, COPY_GUID = guid('Assets/Scenes/Wake.unity'), guid('Assets/Scenes/WakeCopy.unity')
FOLDERS = {f: guid('Assets/'+f) for f in ('Editor', 'Prefabs', 'Scenes', 'Scripts')}

# X.prefab: root X (W) with child Xc (W). Y.prefab: root Y (W) with a nested X instance.
X = dict(go=1460000000000000101, t=1460000000000000102, mb=1460000000000000103)
XC = dict(go=2730000000000000201, t=2730000000000000202, mb=2730000000000000203)
Y = dict(go=3810000000000000301, t=3810000000000000302, mb=3810000000000000303)
Y_NESTED_PI = 5920000000000000401
Y_NESTED_STRIPPED_T = (Y_NESTED_PI ^ X['t']) & 0x7FFFFFFFFFFFFFFF  # Unity's prefab convention

# Wake.unity plain objects: name -> (GameObject fileID base, class, father name or prefab
# instance). GameObject = base, Transform = base+1, MonoBehaviour = base+2.
PLAIN = {'C': (3000, 'W', None), 'D': (2000, 'W2', None), 'B1': (500, 'W', 'B'), 'A': (5000, 'W', None),
         'B': (1000, 'W', None), 'E': (4000, 'WE', None), 'Q': (1500, 'W', None), 'Added': (800, 'W', 'P1')}
INSTANCES = {'P3': (6000, 'X'), 'P1': (9000, 'X'), 'P2': (7000, 'Y')}
P1_STRIPPED_T = 9001  # Unity's scene convention: the PrefabInstance id + 1
DOCUMENT_ORDER = ['C', 'D', 'B1', 'A', 'B', 'E', 'P3', 'P1', 'P2', 'Q', 'Added']
ROOT_ORDER = ['A', 'D', 'B', 'C', 'E', 'P1', 'Q', 'P2', 'P3']


def ref(file_id, g=None):
    return '{fileID: %d}' % file_id if g is None else '{fileID: %d, guid: %s, type: 3}' % (file_id, g)


def game_object(file_id, name, components):
    return (f'--- !u!1 &{file_id}\nGameObject:\n  m_ObjectHideFlags: 0\n'
            '  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n'
            '  m_PrefabAsset: {fileID: 0}\n  serializedVersion: 6\n  m_Component:\n'
            + ''.join(f'  - component: {ref(c)}\n' for c in components)
            + f'  m_Layer: 0\n  m_Name: {name}\n  m_TagString: Untagged\n  m_Icon: {{fileID: 0}}\n'
            '  m_NavMeshLayer: 0\n  m_StaticEditorFlags: 0\n  m_IsActive: 1\n')


def transform(file_id, go, children, father):
    kids = '  m_Children:\n'+''.join(f'  - {ref(c)}\n' for c in children) if children else '  m_Children: []\n'
    return (f'--- !u!4 &{file_id}\nTransform:\n  m_ObjectHideFlags: 0\n'
            '  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n'
            f'  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {ref(go)}\n  serializedVersion: 2\n'
            '  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n  m_LocalPosition: {x: 0, y: 0, z: 0}\n'
            '  m_LocalScale: {x: 1, y: 1, z: 1}\n  m_ConstrainProportionsScale: 0\n'
            + kids + f'  m_Father: {ref(father)}\n  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}\n')


def behaviour(file_id, go, cls):
    return (f'--- !u!114 &{file_id}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n'
            '  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n'
            f'  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {ref(go)}\n  m_Enabled: 1\n'
            f'  m_EditorHideFlags: 0\n  m_Script: {{fileID: 11500000, guid: {SCRIPT[cls]}, type: 3}}\n'
            '  m_Name: \n  m_EditorClassIdentifier: \n')


def prefab_instance(file_id, parent, source_guid, root_t, root_go, name, added=()):
    mods = []
    for prop, value in (('m_LocalPosition.x', 0), ('m_LocalPosition.y', 0), ('m_LocalPosition.z', 0),
                        ('m_LocalRotation.w', 1), ('m_LocalRotation.x', 0), ('m_LocalRotation.y', 0),
                        ('m_LocalRotation.z', 0), ('m_LocalEulerAnglesHint.x', 0),
                        ('m_LocalEulerAnglesHint.y', 0), ('m_LocalEulerAnglesHint.z', 0)):
        mods.append((root_t, prop, value))
    mods.append((root_go, 'm_Name', name))
    text = (f'--- !u!1001 &{file_id}\nPrefabInstance:\n  m_ObjectHideFlags: 0\n  serializedVersion: 2\n'
            f'  m_Modification:\n    serializedVersion: 3\n    m_TransformParent: {ref(parent)}\n'
            '    m_Modifications:\n')
    for target, prop, value in mods:
        text += (f'    - target: {ref(target, source_guid)}\n      propertyPath: {prop}\n'
                 f'      value: {value}\n      objectReference: {{fileID: 0}}\n')
    text += '    m_RemovedComponents: []\n    m_RemovedGameObjects: []\n'
    if added:
        text += '    m_AddedGameObjects:\n'
        for target, obj in added:
            text += (f'    - targetCorrespondingSourceObject: {ref(target, source_guid)}\n'
                     f'      insertIndex: -1\n      addedObject: {ref(obj)}\n')
    else:
        text += '    m_AddedGameObjects: []\n'
    text += f'    m_AddedComponents: []\n  m_SourcePrefab: {{fileID: 100100000, guid: {source_guid}, type: 3}}\n'
    return text


def stripped_transform(file_id, source, source_guid, instance):
    return (f'--- !u!4 &{file_id} stripped\nTransform:\n'
            f'  m_CorrespondingSourceObject: {ref(source, source_guid)}\n'
            f'  m_PrefabInstance: {ref(instance)}\n  m_PrefabAsset: {{fileID: 0}}\n')


def plain_docs(name, base, cls, children, father):
    return (game_object(base, name, [base+1, base+2]) + transform(base+1, base, children, father)
            + behaviour(base+2, base, cls))


def x_prefab():
    return (HEADER + game_object(X['go'], 'X', [X['t'], X['mb']]) + transform(X['t'], X['go'], [XC['t']], 0)
            + behaviour(X['mb'], X['go'], 'W') + game_object(XC['go'], 'Xc', [XC['t'], XC['mb']])
            + transform(XC['t'], XC['go'], [], X['t']) + behaviour(XC['mb'], XC['go'], 'W'))


def y_prefab():
    return (HEADER + game_object(Y['go'], 'Y', [Y['t'], Y['mb']])
            + transform(Y['t'], Y['go'], [Y_NESTED_STRIPPED_T], 0) + behaviour(Y['mb'], Y['go'], 'W')
            + prefab_instance(Y_NESTED_PI, Y['t'], X_GUID, X['t'], X['go'], 'X')
            + stripped_transform(Y_NESTED_STRIPPED_T, X['t'], X_GUID, Y_NESTED_PI))


def wake_scene():
    template = TEMPLATE.read_bytes()
    if hashlib.sha256(template).hexdigest() != TEMPLATE_SHA256:
        raise SystemExit(f'{TEMPLATE} is not the recorded Unity-saved template')
    text = template.decode()
    settings = text[:text.index('--- !u!1001 ')]
    if not settings.startswith(HEADER) or settings.count('--- !u!') != 4:
        raise SystemExit('unexpected settings documents in the template')
    docs = []
    for name in DOCUMENT_ORDER:
        if name in PLAIN:
            base, cls, father = PLAIN[name]
            children = [PLAIN[c][0]+1 for c, (_, _, f) in PLAIN.items() if f == name]
            father_t = 0 if father is None else (P1_STRIPPED_T if father == 'P1' else PLAIN[father][0]+1)
            docs.append(plain_docs(name, base, cls, children, father_t))
        else:
            pid, source = INSTANCES[name]
            g, root = (X_GUID, X) if source == 'X' else (Y_GUID, Y)
            added = [(X['t'], PLAIN['Added'][0]+1)] if name == 'P1' else ()
            docs.append(prefab_instance(pid, 0, g, root['t'], root['go'], name, added))
            if name == 'P1':
                docs.append(stripped_transform(P1_STRIPPED_T, X['t'], X_GUID, pid))
    roots = [PLAIN[r][0]+1 if r in PLAIN else INSTANCES[r][0] for r in ROOT_ORDER]
    scene_roots = ('--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_ObjectHideFlags: 0\n  m_Roots:\n'
                   + ''.join(f'  - {ref(r)}\n' for r in roots))
    return settings + ''.join(docs) + scene_roots


def default_meta(g, folder=False):
    return (f'fileFormatVersion: 2\nguid: {g}\n' + ('folderAsset: yes\n' if folder else '')
            + 'DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')


def prefab_meta(g):
    return (f'fileFormatVersion: 2\nguid: {g}\nPrefabImporter:\n  externalObjects: {{}}\n  userData: \n'
            '  assetBundleName: \n  assetBundleVariant: \n')


def build_settings():
    return (HEADER + '--- !u!1045 &1\nEditorBuildSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 2\n'
            '  m_Scenes:\n'
            f'  - enabled: 1\n    path: Assets/Scenes/Wake.unity\n    guid: {WAKE_GUID}\n'
            f'  - enabled: 1\n    path: Assets/Scenes/WakeCopy.unity\n    guid: {COPY_GUID}\n'
            '  m_configObjects: {}\n  m_UseUCBPForAssetBundles: 0\n')


def main():
    assets = PROBE/'Assets'
    files = {}
    for folder, g in FOLDERS.items():
        files[assets/(folder+'.meta')] = default_meta(g, folder=True)
    # Script metas in the minimal form of the editorcallbacks probe (no trailing newline),
    # which the Editor kept byte for byte.
    for cls, g in SCRIPT.items():
        files[assets/'Scripts'/(cls+'.cs.meta')] = f'fileFormatVersion: 2\nguid: {g}'
    files[assets/'Editor/WakeRunner.cs.meta'] = f'fileFormatVersion: 2\nguid: {EDITOR_SCRIPT}'
    files[assets/'Prefabs/X.prefab'] = x_prefab()
    files[assets/'Prefabs/X.prefab.meta'] = prefab_meta(X_GUID)
    files[assets/'Prefabs/Y.prefab'] = y_prefab()
    files[assets/'Prefabs/Y.prefab.meta'] = prefab_meta(Y_GUID)
    scene = wake_scene()
    files[assets/'Scenes/Wake.unity'] = scene
    files[assets/'Scenes/Wake.unity.meta'] = default_meta(WAKE_GUID)
    files[assets/'Scenes/WakeCopy.unity'] = scene
    files[assets/'Scenes/WakeCopy.unity.meta'] = default_meta(COPY_GUID)
    files[PROBE/'ProjectSettings/EditorBuildSettings.asset'] = build_settings()
    for path, text in files.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(text.encode())
        print(hashlib.sha256(text.encode()).hexdigest(), path.relative_to(PROBE))


if __name__ == '__main__':
    main()
