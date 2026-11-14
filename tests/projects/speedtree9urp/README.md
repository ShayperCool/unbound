# SpeedTree 9 import under URP 17

Two of `tests/projects/speedtree9`'s synthetic trees (`Full` and `Settings`, with their textures)
in a project that renders with the Universal Render Pipeline 17 (the Editor's built-in package; the
pipeline asset and renderer of `tests/projects/urp`), in linear colour space. The importer gives
the materials URP's SpeedTree 9 shader graph and runs URP's `[MaterialSettingsCallback]`, which
sets the culling from `_TwoSided` and the shader graph's keywords and passes.

`Assets/Scripts/SpeedTreeUrpProbe.cs` records, before anything renders (the Editor runs in batch mode and the scene has no camera),
`Shader.globalRenderPipeline`, the shader's queue, sub-shader tags and pass count, a material made
from it at run time, and the imported materials' queue, tags and enabled passes; then, after one
`Camera.Render`, the same for the shader and each material in full (shader, keywords, queue, enabled
passes, tags, every shader property), the materials of each level's renderer, and each tree
texture's sRGB state, format and mip count. Floats are the hex of their bits; values compare
exactly.

Measured in the Unity 6000.0.71f1 Editor (two identical captures, OpenGL Core, NVIDIA driver):
after the render every material's shader, keywords, queue, tags and properties match in Unbound.
`residuals.json` lists, by cause, what does not: before the first render the Editor has not set
`Shader.globalRenderPipeline`, so the shader's fallback sub-shader is active (Unbound creates the
pipeline earlier; a windowed Editor, whose views render before Play mode, is unmeasured); the imported materials keep the MotionVectors pass off, as URP's material update
ran at import with the fallback active; on OpenGL Core the Editor drops the GBuffer pass whose
program excludes glcore; and the Editor imports the tree's extra map without sRGB (its
post-processing of a tree's textures).

    python3 tests/projects/speedtree9urp/run.py record --label A     # Editor capture (GPU)
    python3 tests/projects/speedtree9urp/run.py publish A B          # two identical captures
    python3 tests/projects/speedtree9urp/run.py gate                 # the engine against Reference/
