# SpeedTree 9 import under URP 17

Two of `tests/projects/speedtree9`'s synthetic trees (`Full` and `Settings`, with their textures)
in a project that renders with the Universal Render Pipeline 17 (the Editor's built-in package; the
pipeline asset and renderer of `tests/projects/urp`), in linear colour space. The importer gives
the materials URP's SpeedTree 9 shader graph and runs URP's `[MaterialSettingsCallback]`, which
sets the culling from `_TwoSided` and the shader graph's keywords and passes.

`Assets/Scripts/SpeedTreeUrpProbe.cs` records each material (shader, keywords, queue, enabled
passes, tags, every shader property), the materials of each level's renderer, and each tree
texture's sRGB state, format and mip count, and controls for the shader: its queue, each
sub-shader's tags, and a material made from it at run time. Floats are the hex of their bits; values
compare exactly.

Measured in the Unity 6000.0.71f1 Editor (two identical captures, OpenGL Core, NVIDIA driver):
every material's shader, keywords and properties match in Unbound. `residuals.json` lists what does
not: in this Editor run the shader's active sub-shader is its second, the fallback (queue 2000, no
tags, one pass), so the materials' queue, tags and MotionVectors pass follow it, while Unbound uses
the URP sub-shader; and the Editor imports the tree's extra map without sRGB (its post-processing of
a tree's textures), which Unbound does not reproduce.

    python3 tests/projects/speedtree9urp/run.py record --label A     # Editor capture (GPU)
    python3 tests/projects/speedtree9urp/run.py publish A B          # two identical captures
    python3 tests/projects/speedtree9urp/run.py gate                 # the engine against Reference/
