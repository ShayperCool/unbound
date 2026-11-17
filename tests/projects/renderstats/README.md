# Render counters

`Assets/Scripts/RenderStatsProbe.cs` reads the render counters a script can record through
`ProfilerRecorder` ("Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count",
"Vertices Count", category Render) for frames in which a disabled camera is rendered by hand into
a 64x64 render texture: nothing in view; three cubes of one unlit material; two unlit spheres of
another material beside them; the camera rendered twice; a skybox clear; the shapes lit by a
directional light (Standard); a point light over two of them; a frame that renders nothing. A
frame's values are read in the next frame (`LastValue`), and written into values.jsonl in
`$RENDER_STATS_OUT` (tests/value_probe.py).

Measured in the Unity 6000.0.71f1 Editor (built-in render pipeline, two identical Linux GPU
captures, NVIDIA driver). The Editor counts its own work: the colour clear of the render texture
is a draw (two triangles), the three cubes are batched into one draw, each lit sphere gets a
forward-add draw for the point light, and SetPass calls count material and pass changes.

Unbound counts the draw commands its renderer writes: each is one draw call and one batch, and a
SetPass call when its material or pass differs from the draw before it. `residuals.json` lists
where the work differs (the Editor's batching of small meshes) and the counters Unbound does not
provide (triangles and vertices). The Scripting API documents the counters' names
([ProfilerRecorder](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html),
Manual "Rendering Profiler module"), not how a given scene is counted.

    export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
    python3 tests/projects/renderstats/run.py record --label A
    python3 tests/projects/renderstats/run.py record --label B
    python3 tests/projects/renderstats/run.py publish A B
    python3 tests/projects/renderstats/run.py gate
