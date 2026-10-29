# Materials probe

What the Material API reports, recorded by `MaterialsProbe` in Play mode: which names a material
has (`HasProperty` and the typed `Has*`) for a material asset whose saved properties include names
its shader does not declare, after setting values from script, after a shader change and for a new
material; the values the getters return and the errors they log for names the material does not
have; the main texture and colour of a shader that names them with `[MainTexture]`/`[MainColor]`;
texture slot names and their order; `GetPropertyNames`; shader property types, flags and
attributes; keywords (saved invalid ones, keyword drawers' defaults on new materials).

Record Unity's reference:

    MATERIALS_PROBE_OUT=$PWD/tests/projects/materials/Reference/unity-6000.0.71f1.json DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/materials \
        -executeMethod ProbeRunner.Record -logFile /tmp/materials.log

Run Unbound and compare:

    MATERIALS_PROBE_OUT=/tmp/unbound.json unbound run -p tests/projects/materials --no-watch --frames 10
    python3 tests/projects/materials/compare.py tests/projects/materials/Reference/unity-6000.0.71f1.json /tmp/unbound.json

What Unity 6 does (the reference): a material has the properties its shader declares plus the
ones set from script; the values saved for other names stay in the material, unseen by the getters,
and come back when a shader declaring them is set. Texture getters read every texture slot the
material has (saved ones, those of shaders it had). A getter for a name the material lacks logs
"Material '…' with Shader '…' doesn't have a … property '…'" and returns the default. Legacy `Int`
properties are floats. `new Material(shader)` is named after the shader and starts with the
keywords of its `[Toggle]`, `[ToggleOff]` and `[KeywordEnum]` drawers at their defaults; the Editor
may also give them to the first material asset it loads with the shader (not modelled, not checked).
