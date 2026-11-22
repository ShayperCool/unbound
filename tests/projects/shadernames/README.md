# Shader files of one name

Four `.shader` files declare one name, `Unbound/Dup`: `Assets/Shaders/A`, `B`, `C` and `Z/Dup.shader`.
Each draws a flat colour of its own (red, green, blue, white) over its whole target and declares a
property only it has (`_OnlyA` .. `_OnlyZ`). Their GUIDs sort B, C, A, Z, unlike their paths.
`Assets/Resources/DupB.mat` is a material asset of B's shader.

`Assets/Scripts/ShaderNamesProbe.cs` records which file `Shader.Find("Unbound/Dup")` answers
(before anything is loaded, after the material asset, after every file is loaded through
`AssetDatabase.LoadAssetAtPath`), each file's shader name, and the colour a material of each draws
into a 4x4 render texture (a command buffer's mesh draw, read back), also through the material
asset. Values compare exactly.

Measured in the Unity 6000.0.71f1 Editor (two identical captures): every material draws with its
own file's shader, and `Shader.Find` answers the file first by asset path (A), whatever the
GUIDs' order and whatever was loaded. Not measured: a project file against a package file of one
name, paths that differ only in case.

    python3 tests/projects/shadernames/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/shadernames/run.py publish A B        # two identical captures
    python3 tests/projects/shadernames/run.py gate               # the engine against Reference/
