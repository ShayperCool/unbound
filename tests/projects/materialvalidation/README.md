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

The reference was recorded in macOS batch mode. Two independent Linux Editor captures agree
with all 363 records. The Standard editors set blend states, render queue and its range log,
texture keywords, the emissive GI flag and `_EMISSION`. The built-in default material and
the script's material keep their original state. During the first observation, load metadata
is read before the floats, and the float values already reflect validation. The next observation
two frames later shows the validated metadata. The `modelimport` BuiltinBlend profile also
leaves Standard and Autodesk Interactive assets without a property read for three updates
and sees their saved metadata through those updates.

`run.py check` compares every record exactly. Four project-editor values remain reported.
Unbound validates these materials when their files load, so their first keyword reads already
include the validated keyword. The Editor's counting shaders also record another validation
after the first access, observed two frames later as `_Value` 3 and 21. The timing of
validation for URP Shader Graph materials remains open (tests/projects/graphqueue).
