# SpeedTree 9 import under URP 17

Two of `tests/projects/speedtree9`'s synthetic trees (`Full` and `Settings`, with their textures)
in a project that renders with the Universal Render Pipeline 17 (the Editor's built-in package; the
pipeline asset and renderer of `tests/projects/urp`), in linear colour space. The importer gives
the materials URP's SpeedTree 9 shader graph and runs URP's `[MaterialSettingsCallback]`, which
sets the culling from `_TwoSided` and the shader graph's keywords and passes.

`Assets/Scripts/SpeedTreeUrpProbe.cs` records, before its first `Camera.Render` (the scene has no camera),
`Shader.globalRenderPipeline`, the shader's queue, sub-shader tags and pass count, a material made
from it at run time, and the imported materials' queue, tags and enabled passes; then, after one
`Camera.Render`, the same for the shader and each material in full (shader, keywords, queue, enabled
passes, tags, every shader property), the materials of each level's renderer, and each tree
texture's sRGB state, format and mip count. Floats are the hex of their bits; values compare
exactly.

Measured in a windowed Unity 6000.0.71f1 Editor importing the project afresh (two identical
captures, OpenGL Core, NVIDIA driver): at the first Play-mode frame the pipeline exists
(`Shader.globalRenderPipeline` is set, the URP sub-shader is active, the imported materials keep
URP's MotionVectors pass on, so their import ran with it active, inferred), and every material's shader, keywords, queue, tags and properties
match in Unbound before and after the render. A batch-mode Editor differs (until something
renders it has not created the pipeline, so its imports ran with the shader's fallback sub-shader
active and turned the MotionVectors pass off); the probe records the windowed Editor, as a game's
project is opened. On OpenGL Core the Editor drops the GBuffer pass whose program excludes
glcore (9 passes), as Unbound does. `residuals.json` lists, by cause, what does not match: the
Editor imports the tree's extra map without sRGB (its post-processing of a tree's textures, not
reproduced by decision).

    python3 tests/projects/speedtree9urp/run.py record --label A     # windowed Editor capture (GPU)
    python3 tests/projects/speedtree9urp/run.py publish A B          # two identical captures
    python3 tests/projects/speedtree9urp/run.py gate                 # the engine against Reference/
