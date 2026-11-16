# Material validation on load

What the Unity 6000.0.71f1 Editor does to a material it loads: it validates it with its shader's
editor ([`ShaderGUI.ValidateMaterial`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderGUI.ValidateMaterial.html):
"When the user loads a Material using this ShaderGUI into memory or changes a value in the
Inspector, the Editor calls this method"), the editor named for the active render pipeline, else
the shader's `CustomEditor`, a `ShaderGUI` class whose full name is that name or `UnityEditor.`
and that name.

`make_materials.py` writes `Assets/Resources`: 21 materials, each saved in a state its shader's
editor corrects, and the texture they use:

| materials | what is saved |
| --- | --- |
| `Emission*NoKeyword` | an emission colour with each GI flag (realtime, baked, none, both) and no `_EMISSION` |
| `Black*Keyword`, `EmissiveIsBlackKeyword`, `BlackBakedFlagsSix` | a black emission or the black flag, with `_EMISSION` |
| `CutoutLowQueue`, `FadeQueueInRange`, `TransparentShaderQueue`, `OpaqueHighQueue` | blend modes with render queues inside and outside their range, stray blend states and keywords |
| `NormalMapNoKeyword`, `DetailNormalNoKeyword`, `MetallicMapNoKeyword`, `SmoothnessAlbedo`, `StrayKeywords` | texture and channel keywords missing or stray |
| `SpecularMapNoKeyword` | the specular workflow (Standard (Specular setup)) |
| `ProjectGui`, `ProjectGuiNamespaced` | a project shader whose editor is named without and with its namespace; each validation adds 1 (10) to `_Value` |
| `NoGui` | a shader without an editor |

`MaterialValidationProbe` loads them in Play mode (`Resources.Load`) and records each one's
keywords, render queue, GI flags, RenderType tag, blend floats and `_Value` right after the load
(`*.load.*`) and two frames later, the messages logged while it loads, and two controls: a
material made by a script and the built-in default material.

    python3 tests/projects/materialvalidation/make_materials.py
    python3 tests/projects/materialvalidation/run.py native --label L1   # and L2: macOS batch mode
    python3 tests/projects/materialvalidation/run.py publish L1 L2
    python3 tests/projects/materialvalidation/run.py check

The reference is two byte-identical captures of the macOS Editor in batch mode (validation does not
depend on the OS or the graphics device). Measured: every material of the project's files is
validated as its editor says (the Standard editor's blend states, render queue range with its
logged message, texture keywords, the emissive GI flag and `_EMISSION`; the project's editors by
either name); the built-in default material (GI flags 5, which the Standard editor would make 1
with `_EMISSION`) and the script's material are not. Read right after its load in Play mode a
material shows its saved keywords, render queue, tag and GI flags, but the validated float values;
two frames later everything its editor sets, and the counting editors show a second validation
(`_Value` 3 and 21). Exploratory runs outside the probe found the same for materials loaded in
Edit mode (`AssetDatabase.LoadAssetAtPath`) and read in Play mode.

`run.py check` compares every record exactly; the reads right after the load and the two
`_Value` records are reported, not gated: Unbound validates each material once, when its file has
loaded (34 reported values). The materials of a model's import are not validated on load
(tests/projects/modelimport).
