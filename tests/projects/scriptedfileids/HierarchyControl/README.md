# Imported hierarchy identity

A project importer (`Assets/Editor/HierarchyIdImporter.cs`) imports two assets. `Root.hierarchyid`
gets a main texture and a GameObject identified as `Root`, named `root`; its modern name table maps
class 1 and `Root` to 9876543. `Deep.hierarchyid` gets a main GameObject `Tree` (identifier `Top`)
with a Rigidbody and a LODGroup, a child `LOD0` (MeshFilter, MeshRenderer) with a grandchild `Leaf`
(BoxCollider), two children both named `Twin` (the second with a SphereCollider), a child `Extra`
(CapsuleCollider) that is also added under its own identifier `Extra`, a second added GameObject
`Second` and a Mesh. `Script.hierarchyid` gets a GameObject `Holder` (identifier `Top`) with two
`HierarchyMarker` script components and a child `Inner` with one. `HierarchyIdControl` records the local file id of every object of both assets
and whether `AssetDatabase.LoadAllAssetsAtPath` lists it.

Measured in the Unity 6000.0.71f1 Editor (two identical captures on the NVIDIA GPU display): an
added GameObject without an added ancestor roots a path made of its identifier and name
(`Root/root`, `Top/Tree`); a child adds its name, a component its class name
(`Top/Tree/LOD0/MeshRenderer`), a script component `MonoBehaviour-` and its class name
(`Top/Holder/MonoBehaviour-HierarchyMarker`, class `MonoBehaviour`). Each object's id is xxHash64 of `Type:<class>-><path><index>`, the
index the first one not taken (same-named siblings and their components get 0, then 1). Names are
the importer's (before the main object takes the asset's name); a child added under its own
identifier keeps its path id, and the name table's entry for the GameObject is not used. All objects
of both assets are listed. Unmeasured: a script class in a namespace, a child whose ancestors are not added, and
objects the importer reparents or renames after adding them.

```bash
python3 tests/projects/scriptedfileids/hierarchy_control.py editor --release 6000.0.71f1 --label fresh-label
python3 tests/projects/scriptedfileids/hierarchy_control.py publish --release 6000.0.71f1 --label fresh-label
UNBOUND_CLI=/immutable/runtime/unbound python3 tests/projects/scriptedfileids/hierarchy_control.py check --release 6000.0.71f1
```
