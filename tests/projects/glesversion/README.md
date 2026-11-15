# Minimum OpenGL ES version of the Android target

`Graphics.minOpenGLESVersion` in Play mode with the Android build target (Unity 6000.0.71f1,
`Sources/ProbeEntry.cs` in a copy of `tests/projects/physics`). Each variant edits the copy's
ProjectSettings.asset: `openGLRequireES31`, `openGLRequireES31AEP`, `openGLRequireES32` and the
Android entry of `m_BuildTargetGraphicsAPIs` (none, Vulkan alone, OpenGL ES 3 alone, both; one
Vulkan-only list left automatic). Every variant is its own Editor (batch, GPU) or engine process.

```bash
python3 tests/projects/glesversion/run.py editor --release 6000.0.71f1 --label <label>
python3 tests/projects/glesversion/run.py publish --release 6000.0.71f1 --label <label>
python3 tests/projects/glesversion/run.py unbound --release 6000.0.71f1
```

`editor` records two independent captures per variant and compares them; `publish` writes
`Reference/unity-6000.0.71f1.json` when they are identical (10 variants). `unbound` compares
exactly.

Measured: with OpenGL ES 3 among the Android graphics APIs (a list left automatic has it, whatever
list is stored), the answer is OpenGLES32, OpenGLES31AEP or OpenGLES31 when that requirement is
set, checked in that order (each alone suffices), else OpenGLES30; a manual list without OpenGL ES 3
answers None. The Standalone target answers None (tests/projects/srpmembers `gles.min`).
iOS and Standalone lists with OpenGL ES 3 follow the same rule, other targets answer None (unmeasured). Measured on Linux
with the NVIDIA driver.

Contract: [Graphics.minOpenGLESVersion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics-minOpenGLESVersion.html)
("the minimum OpenGL ES version", set in the Player settings).
