# File ids of imported model objects

`make_models.py` writes two ASCII FBX files: `Nested.fbx` (a mesh three levels down, two sibling
nodes named `Leaf`, a mesh under an empty node, two `_LOD` levels) and `Single.fbx` (one top-level
mesh node, which becomes the root). Their metas are the Editor's (`fileIdsGeneration: 2`, no name
table). `NamedTable.fbx`, `OldMeta.fbx` and `OldMetaGen2.fbx` are copies of Nested.fbx: the first's meta lists the
root and `Body` by node name and `Arm` and its renderer by hierarchy path, the second's has the
importer serializedVersion 20100 and no `fileIdsGeneration`, the third's 19301 with
`fileIdsGeneration: 2`. `Editor/ProbeSetup.Create` authored `Resources/NestedVariant.prefab` (a variant of
Nested.fbx overriding a child renderer's cast shadows, another's material, a LOD renderer's
enabled state, a GameObject's active state, a Transform and the root's LODGroup) and
`Resources/Outer.prefab` (that variant and Single.fbx nested, with overrides of their objects in
turn, which target the variant's objects by their derived ids).

`Assets/Scripts/ModelFileIdProbe.cs` writes the local file id of every object
`AssetDatabase.LoadAllAssetsAtPath` lists for each model (objects sharing a key by sorted id), and
the state of each prefab's instance: every renderer's enabled state, cast shadows, receive shadows
and material, every GameObject's active state, every Transform's position and scale, the LODGroup.

Measured in the Unity 6000.0.71f1 Editor (two identical captures on the NVIDIA driver): a
GameObject's id is the xxHash64 of `Type:GameObject->` followed by its hierarchy path and the hash
index 0 (the root's path is `//RootNode/root` whatever the file is called, a child's its parent's
path, `/` and its unique name); a component's is that of `Type:<class>->` with its GameObject's
path, `/` and its class name. A name-table entry applies when it names that path; entries naming
the node alone are ignored. A meta without `fileIdsGeneration` of importer serializedVersion 20100
hashes node names (`//RootNode` for the root), as `fileIdsGeneration: 1` does; one of 19301 that
sets `fileIdsGeneration: 2` hashes paths. Meshes of same-named siblings keep the node's name (`Leaf` twice,
indices 0 and 1). The listing includes an `ImportLog` sub-object Unbound does not create
(residuals.json).

    export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
    python3 tests/projects/modelfileids/make_models.py          # then let the Editor import them
    python3 tests/projects/modelfileids/run.py record --label A
    python3 tests/projects/modelfileids/run.py record --label B
    python3 tests/projects/modelfileids/run.py publish A B
    python3 tests/projects/modelfileids/run.py gate

The gate runs the engine on a fresh copy and compares every value exactly with `Reference/`.
