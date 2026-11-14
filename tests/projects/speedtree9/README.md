# SpeedTree 9 import

`make_trees.py` writes four small SpeedTree 9 trees of our own into `Assets/Trees` (no third-party
asset): `Full` (three levels of detail and a billboard level, bark, two-sided leaves with
camera-facing quads and a billboard material, all five wind parts, a sphere and a capsule
collision object), `Simple` (one level, no wind, no billboard, no collision objects), `Settings`
(`Full` with branch 2 wind and shimmer off, and a `.meta` that sets every importer setting away
from its default: units left as they are, hue variation on, bump mapping and subsurface off, no
shadow casting or light probes, motion vectors per camera, no colliders or Rigidbody, cross-fading
not animated, four per-level settings with their own heights, two of them overriding) and
`Remapped` (`Simple` whose `.meta` remaps its bark material to `Assets/Materials/Remap.mat` through
`externalObjects`). The script's docstring describes the file layout.

`Assets/Scripts/SpeedTreeProbe.cs` records, for each tree, every object of the asset (class, name,
local file id), the main object's hierarchy with its components (transforms, LODGroup levels and
fade settings, renderers' materials and lighting settings, Tree wind assets, colliders, Rigidbody),
each mesh (vertex layout, sub-meshes and their bounds, a hash of every vertex channel and of the
indices, the UV distribution metrics) and each material (shader, keywords, queue, instancing,
double-sided GI and every shader property). Floats are the hex of their bits; values compare
exactly. The project uses the built-in pipeline (`Nature/SpeedTree9`).

Instances: `Assets/Prefabs/FullVariant.prefab` is a variant of `Full` that adds a child to its first
level (its LODGroup's renderers), turns a level's shadows off, remaps a material slot, removes the
Rigidbody and a collision object and adds a collider (the edits a URP 17 sample's tree prefabs
make). `Probe.unity` places that variant, an instance of `Simple` (moved, turned, scaled, a level's
shadow receiving off) and one of `Assets/Boxes/Crate.crate`, which the project's own ScriptedImporter
(`Assets/Editor/CrateImporter.cs`) imports to a hierarchy with a mesh, a material, a collider and a
script (`CrateMarker`); its instance overrides the script's field and the collider. A scene object
(`Pointer`) references objects of the instances. The probe records the crate's objects and the
scene's objects in Play mode.

Measured in the Unity 6000.0.71f1 Editor (two identical captures, NVIDIA driver): see
`docs/COMPATIBILITY_GAPS.md` (SpeedTree 9 row). `residuals.json` lists the values Unbound does not
reproduce yet with their causes.

    python3 tests/projects/speedtree9/make_trees.py               # regenerate the trees
    python3 tests/projects/speedtree9/run.py record --label A     # Editor capture (GPU)
    python3 tests/projects/speedtree9/run.py publish A B          # two identical captures
    python3 tests/projects/speedtree9/run.py gate                 # the engine against Reference/
