# CRT load cycles

Two independent Unity 6000.0.71f1 Editor captures on the existing NVIDIA display
agree on each profile: normal 134 values and late-only 95. The extensions preserve
all earlier 58, then 97 and 116 normal values and all 62 and 79 earlier late-only values. The
parent customrt reference and image tolerances remain unchanged.

```bash
python3 tests/projects/crtload/run.py editor --release 6000.0.71f1 --label fresh-label
python3 tests/projects/crtload/run.py publish --release 6000.0.71f1 --label fresh-label
UNBOUND_CLI=/immutable/runtime/unbound python3 tests/projects/crtload/run.py check --release 6000.0.71f1
```

The control inserts two copies of the public UpdateCustomRenderTextures loop
system, with observations before, between and after them. Textures made before
scene loading, in Awake, Start, Update and LateUpdate stay alive through later
cycles. An explicitly created texture distinguishes GPU allocation from manager
loading. Their OnLoad initialization produces a measured red value of 0.25.

The initial Play entry cycle does not process those loads. They load on the next
eligible rendered cycle, including textures created earlier in that same cycle.
The original customrt API section destroys its startup textures before that
point, so they never reach its loaded list. Startup creation does not permanently
exclude a texture from loading.

A texture made by a loaded listener joins the same load cycle and initializes there.
A texture created between duplicate phases in a later frame waits for the next
rendered cycle. The late-only profile makes its first texture there, after empty
phase visits, and observes the same delay. A changed color and Initialize request
between the two visits retain the old red value for that cycle and show 0.75 in
the next one. With renderFrameInterval 3, a pending load survives two skipped
screen frames and completes in the next rendered frame.

[CustomRenderTexture.Initialize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CustomRenderTexture.Initialize.html)
describes deferred initialization before updates. The [inspector reference](https://docs.unity3d.com/6000.0/Documentation/Manual/class-CustomRenderTexture.html)
describes OnLoad initialization once upon creation. These controls make the
changed initialization visible in pixels. Exact startup and repeated-phase
scheduling are measured release behavior, selected through Compat.

A throwing textureLoaded listener logs one exception. Both that texture and the
following pending texture still load and initialize in the same cycle. The error
remains visible; it does not discard pending initialization work.

Other releases retain the previous unmeasured scheduling and callback exception
behavior. Forced Editor-camera updates, paused editing, custom loops without the
time-update system, and repeated Play entries with disabled domain reload remain
outside these captures.
