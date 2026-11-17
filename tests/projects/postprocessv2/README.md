# Post-processing Stack v2 on a built-in pipeline camera

`Assets/Scripts/PostProcessProbe.cs` (built-in render pipeline, Linear colour space, the
`com.unity.postprocessing` 3.5.0 package) makes a camera that draws three Unlit/Color quads over a
solid background into a 64x64 render texture, and records the texture's pixels on an 8x8 grid (the
bytes at each cell's centre) two frames after each setup:

- `plain`: the camera alone (the control);
- `layer`: with a `PostProcessLayer` (its resources from the package) and no volume: the layer
  still copies the camera's image into a temporary target and draws it back through its uber
  shader, which reads it as the global `_MainTex`. The image is the camera's, up to the layer's
  dithering;
- `vignette`: the layer and a global volume (`PostProcessManager.QuickVolume`) whose only effect
  is a Vignette of intensity 0.5 and smoothness 0.4: the corners darken.

Then `global.<shader>`: a 4x4 texture of one colour bound only globally
(`Shader.SetGlobalTexture`), sampled at the centre by a fullscreen draw (`Assets/Resources`
shaders) for `_MainTex` and for `_OtherTex`, each with a shader that declares the property in its
Properties block (default `white`) and with one that does not. Matches the Unity 6000.0.71f1
Editor: a shader that does not declare the property reads the global texture; one that declares
it reads the material's texture or its default, never the global (the Scripting API's
`Shader.SetGlobalTexture`: global properties are used when the material does not define them).

Numbers compare within 2 (bytes; the GPU's rounding and the layer's dithering).

    python3 tests/projects/postprocessv2/run.py record --label A    # windowed Editor on the GPU
    python3 tests/projects/postprocessv2/run.py publish A B         # two identical captures
    python3 tests/projects/postprocessv2/run.py gate                # the engine against Reference/
