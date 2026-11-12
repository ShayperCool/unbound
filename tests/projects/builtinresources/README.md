# Built-in resources by path

`Assets/Scripts/BuiltinResourceProbe.cs` asks `Resources.GetBuiltinResource` (`R`) and
`AssetDatabase.GetBuiltinExtraResource` (`E`) for every path of `Assets/Resources/BuiltinPaths.txt`
with the type of the object registered there: the path each object of the install's
`unity default resources` and `unity_builtin_extra` is registered under in Unity 6000.0.71f1 (85
and 238 objects, the `path` of `data/profiles/builtin-6000.0.json`). Each value is the answer's
class, file (`default`, `extra`) and file id from `AssetDatabase.TryGetGUIDAndLocalFileIdentifier`,
its name and a few facts of its content (mesh counts, bounds and readability; texture size, format,
mips, sampler and readability; a material's shader and queue; a sprite's rect, border, pixels per
unit and texture), followed by every message the call logged.

Controls outside the table: another letter case, a missing name, a base type (`Object`), an
unrelated type, a script class's bases for the GameSkin (`MonoBehaviour`, `ScriptableObject`), each
file's paths asked of the other lookup, `Arial.ttf` (as a Font and as a Material, through both
lookups), a null and an empty path; whether two calls answer the same object; the mesh of each
`GameObject.CreatePrimitive` shape.

Measured in the Unity 6000.0.71f1 Editor (two identical captures on the NVIDIA driver): a path
matches case-sensitively and only with the object's own class; a miss logs the assertion
`Failed to find <path>` and the error `The resource <path> could not be loaded from the resource
file!` and answers null; a null or empty path throws `ArgumentException: Invalid path`; `Arial.ttf`
is looked up as `LegacyRuntime.ttf` (with that lookup's messages) and then throws; `Sphere.fbx`,
`Cylinder.fbx`, `Plane.fbx` and `Capsule.fbx` are the old meshes (pSphere1, ...) and the `New-*.fbx`
paths the primitives' meshes; the registered DXT1 texture `VR/UnitySplash-HolographicTrackingLoss.png`
is not loaded (only the error). `residuals.json` names the classes and properties Unbound does not
reproduce.

    python3 tests/projects/builtinresources/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/builtinresources/run.py publish A B        # two identical captures
    python3 tests/projects/builtinresources/run.py gate               # the engine vs Reference/
