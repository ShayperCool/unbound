# Skinned bounds probe

Unity 6000.0.71f1, built-in rendering, Linux GPU Editor, Game view cameras only.
This probe isolates Q75 from the animation clock and playable composition probes.
Its authored quad has explicit weights and bind poses. The renderer stays at x=20
while its separate bone moves through the camera edge and back. Each case has its
own camera, layer and render texture. Records include local/world bounds in Update
and LateUpdate, the previous render's image presence and `isVisible`, and Animator
pose/time for all three culling modes, with offscreen updates enabled and disabled.
Static controls measure translation, rotation, nonuniform scale, a missing root,
root reassignment, explicit and default bounds, custom world bounds, and resets.
The committed serialized prefab exercises `m_AABB` and cloning.

## Unity contract and cause table

The following are Unity 6000.0 documentation guarantees, reviewed for this task.
The root-space choices and callback timing in the capture are release/context
observations, not promises about other releases or rendering paths.

| Source | Applicable guarantee / limitation |
| --- | --- |
| [Skinned Mesh Renderer manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-SkinnedMeshRenderer.html) | Bounds determine offscreen culling and follow the root bone. Imported model animation bounds are precomputed; offscreen updates recalculate bounds each frame. |
| [SkinnedMeshRenderer API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SkinnedMeshRenderer.html) and root bone in the component manual | The skeleton root is a Transform. The individual `SkinnedMeshRenderer-rootBone.html` endpoint returned HTTP 404 during review; the component manual supplies the applicable bounds guarantee. |
| [Renderer.localBounds](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-localBounds.html) | Local AABB; a skinned renderer's default includes associated imported animations and can exceed the mesh. Offscreen updates recompute it each frame. Runtime overrides are not serialized. The former `SkinnedMeshRenderer-localBounds.html` endpoint returned HTTP 404; this inherited Renderer API is the current page. |
| [updateWhenOffscreen](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SkinnedMeshRenderer-updateWhenOffscreen.html) | Enables updates offscreen. Its summary also mentions animations; the measured Animator culling behavior remains in `animculling`, rather than assuming this overrides Animator.cullingMode. |
| [Renderer.bounds](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-bounds.html) | World AABB. A custom world bound overrides a local override and stops following transforms. It is not serialized. |
| [ResetBounds](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer.ResetBounds.html), [ResetLocalBounds](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer.ResetLocalBounds.html) | Remove custom overrides and restore the default. The skinned local-reset result below is an observation distinct from this general description. |

The raw captures preserve all measured results. Two independent Native captures
must match byte for byte before publishing one canonical reference. No prior
`animculling` key, assertion, tolerance or reference changes.

```
python3 tests/projects/skinnedbounds/run.py editor --release 6000.0.71f1 --label <new-label>
python3 tests/projects/skinnedbounds/run.py publish --release 6000.0.71f1 --label <same-label>
python3 tests/projects/skinnedbounds/run.py check --release 6000.0.71f1
```

`check` is the engine-only gate and uses the committed release reference. `create`
is only for explicit Unity-compatible fixture authoring; it is never part of a gate.
