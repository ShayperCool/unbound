# SpeedTree 8 import

The probe imports the SpeedTree 8 (`.st`) sample trees of Unity's Graphics test projects
(`Tests/SRPTests/Projects/UniversalGraphicsTest_Terrain/Assets/CommonAssets/SpeedTree/SpeedTreeV8` in
github.com/Unity-Technologies/Graphics): `Broadleaf_Desktop_Field.st` and its byte-identical copies
`Broadleaf_Desktop_Field_OptionsOn.st` and `_OptionsOff.st`, and `Card_Tree_Sample/card-tree-sample.st`,
with their maps, material files and `.meta` files as the samples ship them. They are under the Unity
Companion License and are **never part of this repository**: every run copies them into its private
project copy from `$UNBOUND_SPEEDTREE_SAMPLES` (a directory with `v8/`), else
`$XDG_CACHE_HOME/unbound/speedtree-samples` (`~/.cache` by default), and stops when they are not there.
The Reference holds measured facts only: names, file ids, settings, bounds and hashes of the meshes'
channels, no vertex values or texture data.

What the samples cover: files of version 8.0 (one-byte booleans, a billboard level) and 10.0 (four-byte
booleans, camera-facing leaves); `.meta` files of serializedVersion 7 (InPrefab materials, one remapped by
`externalObjects`, `fileIDType` 2) and 6 (External materials in `<name> Materials/LOD<i>/`, `fileIDType` 1,
every level overriding the settings, one with shadows, light probes and the effects off).

`run.py` also stages copies of two samples beside them (`VARIANTS`) with their `.meta` edited for what the
samples do not combine: `Card_Units0` (units left as they are, embedded materials, cross-fading not animated
with a fade-out width of its own, a selected wind quality the levels do not have), `Card_Inches` (inches,
no smooth transitions), `Card_Custom` (a custom scale), `Card_Dangling` (a material remap to no asset),
`Card_Spheres` (its collision objects' ends moved: one meeting, one 0.001 apart) and `Field_Embedded`
(the leaves, bark and billboard materials embedded, cross-fading not animated with both widths set).

`Assets/Scripts/SpeedTree8Probe.cs` records, in Play mode, for each tree the importer's public settings,
every object of the asset (class, name, local file id, hide flags), the main object's hierarchy with its
components (transforms, LODGroup levels, renderers' materials and lighting, Tree wind assets, colliders,
Rigidbody), each mesh (layout, sub-meshes and their bounds, a hash of every vertex channel and of the
indices) and each material it draws with (shader, keywords, queue, every shader property); the SpeedTree
callbacks each tree's import made (`Assets/Editor/SpeedTree8Events.cs`), the trees' textures' import
settings, and the scene's instances of two trees (`Assets/Scenes/Probe.unity`: the card tree and
`OptionsOn`, moved). Floats are the hex of their bits; values compare exactly. The project uses the
built-in pipeline (`Nature/SpeedTree8`; the samples' URP material files have no shader there).

Import behaviour does not depend on the OS or the graphics device, so the probe records with the macOS
Editor in batch mode. `residuals.json` lists the values Unbound does not reproduce yet with their causes.
The trees' first import without `.meta` files (the Editor writes them and its maps' texture settings, then
imports every tree again) is not covered.

    python3 tests/projects/speedtree8/run.py record --label A     # Editor capture
    python3 tests/projects/speedtree8/run.py publish A B          # two identical captures
    python3 tests/projects/speedtree8/run.py gate                 # the engine against Reference/
