# Warm-up of built-in shaders without published source

`Assets/Resources/Prewarm.shadervariants` is a ShaderVariantCollection that lists built-in shaders
by their file ids, as collections that Unity saves list them: `Hidden/Internal-UIRAtlasBlitCopy`
(9101) and `Hidden/UIElements/EditorUIE` (9103), whose ShaderLab source Unity does not publish,
beside `UI/Default` (two variants) and `Sprites/Default`. `Assets/Scripts/WarmupBuiltinProbe.cs`
records whether `Shader.Find` finds the four shaders, the collection's counts and `Contains`, its
state after three `WarmUpProgressively(1)` steps, a full `WarmUp` and one more step, and after
`Shader.WarmupAllShaders`. Values compare exactly.

Measured in the Unity 6000.0.71f1 Editor (two identical captures, NVIDIA driver): every variant
warms, one per progressive step, with no error; `WarmUp` leaves all five warm and
`WarmupAllShaders` completes without changing the collection's progress. Unbound's renderer
cannot compile a shader without source: warm-up skips it with one warning per shader and counts
its variants as warm, as the Editor's counts do.

    python3 tests/projects/warmupbuiltin/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/warmupbuiltin/run.py publish A B        # two identical captures
    python3 tests/projects/warmupbuiltin/run.py gate               # the engine against Reference/
