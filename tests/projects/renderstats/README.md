# Render counters

`Assets/Scripts/RenderStatsProbe.cs` reads the render counters a script can record through
`ProfilerRecorder` ("Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count",
"Vertices Count", category Render) for frames in which a disabled camera is rendered by hand into
a 64x64 render texture: nothing in view; three cubes of one unlit material; two unlit spheres of
another material beside them; the camera rendered twice; a skybox clear; the shapes lit by a
directional light (Standard); a point light whose range reaches four of them; a frame that
renders nothing; one sphere of a shader with two untagged passes. A
frame's values are read in the next frame (`LastValue`), and written into values.jsonl in
`$RENDER_STATS_OUT` (tests/value_probe.py).

Measured in the Unity 6000.0.71f1 Editor (built-in render pipeline, two identical Linux GPU
captures, NVIDIA driver). The Editor counts its own work: a render with nothing in view is one
draw (two triangles, a full-screen quad; what it draws was not measured), the three cubes are batched into one
draw (the point light ends that batching), each object within the point light's range gets a
forward-add draw, a two-pass shader draws twice, and SetPass calls count material and pass
changes.

Unbound counts the draw commands its renderer writes: each is one draw call and one batch per pass
it draws, and a SetPass call when its material or pass differs from the draw before it. A render
with nothing in view is one draw in Unbound too (the copy of the camera's intermediate target into
the render texture; a clear is no draw). Unbound's choices, unmeasured: the instances of one instancing call
drawn one after another with the same material and pass count once (whatever their number), and
UI (Canvas, IMGUI, UI Toolkit) counts its own draw commands. `residuals.json` lists
where the work differs (the Editor's batching of small meshes) and the counters Unbound does not
provide (triangles and vertices). Not covered: static batching, automatic GPU instancing of renderers
sharing a mesh and material (both lower the Editor's counts), and URP's SRP Batcher. The Scripting API documents the counters' names
([ProfilerRecorder](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html),
Manual "Rendering Profiler module"), not how a given scene is counted.

    export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
    python3 tests/projects/renderstats/run.py record --label A
    python3 tests/projects/renderstats/run.py record --label B
    python3 tests/projects/renderstats/run.py publish A B
    python3 tests/projects/renderstats/run.py gate
