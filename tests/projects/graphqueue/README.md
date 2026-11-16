# Render queues of Shader Graph materials as Play mode starts

`ProbeRunner.Create` (run once; its outputs are committed) makes a transparent URP Unlit Shader
Graph without material override, as URP's "Unlit Shader Graph" menu makes one with the surface
set to Transparent, materials of it and the probe scene, whose quad draws `Graphs/SceneStale`. The
Editor saved each material's queue as its URP editor computed it while the graph's shader did not
exist yet (Geometry 2000 plus the material's `_QueueOffset`); `OffsetStored` was then set to 2950
in its file, the state a material saved in a working project has (Transparent 3000 - 50).
`StrayState` is `OffsetStored` with a `RenderType` override tag and its `DepthOnly` pass disabled,
which URP's editor would clear on validation.

`ProbeRunner.Record` imports the graph and the materials again, renders the scene once in Edit
mode (as an Editor that has drawn its views: the pipeline is made and the quad's shader compiled)
and plays the scene. `GraphQueueProbe` records, before the scene loads (`early`) and at its first
`Start`, the active pipeline tag, whether a pipeline exists and the queues of URP shaders; the graph
shader's queue; each material's queue, stored queue, `RenderType` tag and `DepthOnly` and
`MotionVectors` pass states right after `Resources.Load` and two frames later; and the scene
quad's material at both times. Values compare exactly.

Measured in the Unity 6000.0.71f1 Editor (windowed, two identical captures, NVIDIA driver): before
the first scene loads the configured pipeline's tag (`UniversalPipeline`) is active while no
pipeline exists yet, so URP's Terrain/Lit answers its own queue (Geometry-100, 1900) and the graph
its Transparent 3000; the scene's material holds 3000 - 50 = 2950. Without the render before Play
(a capture that enters Play right after the Editor starts) the tag is empty and those queues read
2000: an Editor that has drawn its views is the case Unbound plays. The materials loaded with
`Resources.Load` keep their saved queues, tags and passes at load and two frames later; Unbound
validates every loaded project material (tests/projects/materialvalidation), see `residuals.json`.

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath <copy of this probe> \
        -executeMethod ProbeRunner.Create -logFile create.log   # once; copy Assets/ back
    python3 tests/projects/graphqueue/run.py record --label A   # Editor capture (GPU, windowed)
    python3 tests/projects/graphqueue/run.py publish A B        # two identical captures
    python3 tests/projects/graphqueue/run.py gate               # the engine against Reference/
