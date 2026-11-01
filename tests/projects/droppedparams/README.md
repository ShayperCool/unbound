# Dropped-parameters probe

What overloads with extra parameters, and answers that are easy to get wrong without a warning,
give in the Editor: shader texture property dimensions, `Debug.LogFormat` with
`LogOption.NoStacktrace`, `GL.GetGPUProjectionMatrix`, `Graphics.Blit` and `CommandBuffer.Blit`
with source and destination depth slices, render target identifiers with a slice (and -1, every
slice), the `Clear` load action, `Resources.UnloadAsset`, and a scriptable pipeline's visible
lights and `CullingResults.GetShadowCasterBounds`. `DroppedParamsProbe` records each value (floats
as the hex of their bits) as JSON lines in `DROPPED_PARAMS_PROBE_OUT` and exits.

The project's default pipeline is `ProbePipelineAsset`, which draws nothing; the probe culls a
camera through it (`Camera.Render`) with eleven cubes as casters (one at a time, then all) and ten
lights: two directional (one without shadows), point and spot lights in and out of the view,
beyond the shadow distance, with a culling mask that excludes the casters, and a spot whose range
reaches into the view but whose cone points away.

    python3 tests/projects/droppedparams/run.py create --release <version> --label <L>   # assets, scene, pipeline
    python3 tests/projects/droppedparams/run.py editor --release <version> --label <L>   # two Editor runs (GPU)
    python3 tests/projects/droppedparams/run.py publish --release <version> --label <L>  # checks they agree
    UNBOUND_CLI=<unbound> python3 tests/projects/droppedparams/run.py unbound --release <version> --label <L>
    python3 tests/projects/droppedparams/compare.py tests/projects/droppedparams/Reference/unity-6000.0.71f1.jsonl \
        ~/.cache/unbound/validation/runs/droppedparams/<L>/unbound/capture.jsonl

`compare.py` compares every value exactly. One normalisation: CoreCLR words an
ArgumentOutOfRangeException's parameter as `(Parameter 'x')` where Mono writes
`\nParameter name: x`.

## What the Editor does (6000.0.71f1)

Two independent captures agree byte for byte (Renderer: NVIDIA GeForce RTX 4060).

* **Texture properties.** `GetPropertyTextureDimension` answers the declared dimension (`2D`,
  `3D`, `Cube`, `2DArray`, `CubeArray`, `Any`); the keywords work in lower case too, and
  `CubeArray` and `Any` are textures. Another property type throws `ArgumentException: Property
  type is not TexEnv.`; an index out of range throws `ArgumentOutOfRangeException` for
  `propertyIndex`.
* **Logs.** `LogOption.NoStacktrace` gives log callbacks an empty stack trace for logs, warnings
  and errors.
* **GPU projection.** On the Editor's GL device `GL.GetGPUProjectionMatrix` returns the matrix
  unchanged, rendering into a texture or not.
* **Blit slices.** The destination slice is drawn into and an array source's slice is read, by
  both `Graphics.Blit` and `CommandBuffer.Blit`. `Graphics.Blit(array, array)` without slices copies
  every slice; `CommandBuffer.Blit` of an array without slices reads grey (0.5 in every channel).
  `Graphics.Blit(source, material, pass, destDepthSlice)` draws into the active target as bound,
  whatever the slice. A blit into a render target identifier with a slice draws into slice 0.
* **Target slices.** `SetRenderTarget` with an identifier draws into its slice; slice -1 binds
  every slice, and a clear then clears them all.
* **Load actions.** `RenderBufferLoadAction.Clear` in `CommandBuffer.SetRenderTarget` throws
  `ArgumentException: RenderBufferLoadAction.Clear is not supported`.
* **Resources.UnloadAsset.** A GameObject or component logs the error `UnloadAsset may only be
  used on individual assets and can not be used on GameObject's / Components / AssetBundles or
  GameManagers`, a runtime object `UnloadAsset can only be used on assets;`, null nothing. An
  unloaded material is still alive and reads its saved colour again; loading it again gives
  another wrapper with the same instance ID. An unloaded ScriptableObject is destroyed (its
  managed fields keep their values) and loads anew from its file.
* **Visible lights.** Directional lights come first; the order of the rest changes from run to run
  (the probe records them by name). A point light whose range is outside the view is not visible,
  nor is a spot light whose cone is, even when its range sphere reaches into the view.
* **Shadow caster bounds.** The union of the world bounds of the light's casters: renderers the
  camera's mask lets through that cast shadows (ShadowsOnly too, not Off, not disabled), on a
  layer of the light's culling mask, inside the light's range or cone, and inside the view's
  frustum to the shadow distance swept toward the light (casters behind the camera, beyond the
  shadow distance, and above or beside the view whose shadows fall outside it are not). A light
  without shadows has none, and so has a point light beyond the shadow distance.

## What differs

Unbound matches 251 of the 252 values. Open:

* `unload.material.reload`: the Editor hands a material that was unloaded and loaded again out
  as a new wrapper of the same object (`ReferenceEquals` false, same instance ID); Unbound keeps
  the one object, which reads the saved state again in place.
* Unbound copies an array slice texel for texel, so a blit from a texture array needs a target of
  the array's size and format without scale or offset, a source slice within the array, a
  destination slice within the layers Unbound allocates (one for 3D and cube-array targets) and
  no multisampling; other array blits warn and are skipped (unmeasured in the Editor).
* `Resources.UnloadAsset` of textures, meshes, audio and other asset kinds, and of an object that
  shares its file with others, still warns and does nothing.
* Draws into slice -1 go into the first layer; shaders that pick a layer are not supported.
