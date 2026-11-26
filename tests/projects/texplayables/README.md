# Experimental texture playables

What `UnityEngine.Experimental.Playables` answers: `CameraPlayable`, `MaterialEffectPlayable`,
`TextureMixerPlayable`, `TexturePlayableOutput` and `TexturePlayableBinding`. Unity 6000.0.71f1,
recorded with the macOS Editor in batch mode (two identical captures); the values depend on neither
the OS nor the graphics device. `Assets/Scripts/TexturePlayablesProbe.cs` records in Play mode
into values.jsonl in `$TEXPLAYABLES_OUT` (tests/value_probe.py); values compare exactly.

* `camera.*`, `effect.*`, `mixer.*`: each playable's type, validity, input and output counts,
  play state, duration and speed, its getters after creation and after its setters (null
  included), creation with null, creation on an invalid graph, equality.
* `cast.*`: casting a playable or the null playable to another kind.
* `output.*`: the texture output's validity, type, target (set and cleared), the graph's output
  and playable counts, the null output, creation on an invalid graph, casting the null output,
  and its source playable after `SetSourcePlayable`.
* `binding*`: `TexturePlayableBinding.Create`'s stream name, source object and output target type.

What it shows: a camera or material effect playable has no inputs and one output, a texture
mixer two inputs; a material effect's pass is -1 unless given; every creation on an invalid
graph throws a NullReferenceException ("The PlayableGraph is null."); a cast to another kind
throws an InvalidCastException naming it ("the playable is not an CameraPlayable."); a binding's
output target type is RenderTexture. Not recorded: what evaluating such a graph draws into the
target (the Editor draws into it: its pixels change; they are the graphics device's). Unbound does
not draw and reports that once as not implemented.

Record and gate:

    python3 tests/projects/texplayables/run.py record --label a   # twice, then
    python3 tests/projects/texplayables/run.py publish a b
    python3 tests/projects/texplayables/run.py gate
