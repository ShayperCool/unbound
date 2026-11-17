# Engine members render pipelines and packages call

`Assets/Scripts/RuntimeApisProbe.cs` writes values.jsonl in `$RUNTIMEAPIS_OUT`
(tests/value_probe.py): for every action, what it returns or throws and the log messages it
writes with their log type (`.logs`).

* `camera.*`: a camera's stereo projection, non-jittered projection and view matrices per eye
  without an XR device: unset, after SetStereoProjectionMatrix / SetStereoViewMatrix, after
  CopyStereoDeviceProjectionMatrixToNonJittered and the resets, next to the camera's own
  matrices; the scene view filter mode.
* `gamut.*`: ColorGamutUtility's primaries, white point and transfer function of every
  ColorGamut and of values outside the enum (`gamut.value*`).
* `shader_mode.*`: GraphicsSettings.GetShaderMode of every BuiltinShaderType and of values
  outside the enum; the project's graphics settings disable the deferred reflections shader and
  give the lens flare a custom one, every other type the built-in shader.
* `transform.*`: Transform.rotationOrder and GetLocalEulerAngles in every rotation order, after
  Euler hints (SetLocalEulerHint), with fractions and wide angles and after localEulerAngles is
  set; in gimbal lock (a middle angle of a quarter turn, also 89.9 and 89.9999 degrees, with and
  without a hint); that neither the hint nor the order changes the rotation.
* `quality.*`, `screen.*`: Screen.msaaSamples under QualitySettings.antiAliasing levels and
  Screen.SetMSAASamples with the built-in renderer; `srp.*` the same after a scriptable render
  pipeline (one that renders nothing) is assigned as GraphicsSettings.defaultRenderPipeline, a
  frame later, and after it is unassigned.
* `texture.*`, `command_buffer.*`, `context.*`: Texture.SetStreamingTextureMaterialDebugProperties,
  CommandBuffer.SwitchIntoFastMemory / SwitchOutOfFastMemory and sizeInBytes,
  ScriptableRenderContext.EmitGeometryForCamera / EmitWorldGeometryForSceneView (also with null).
* `custom_shader.*`, `set_mode.*`, `set_custom.*`: GraphicsSettings.GetCustomShader of every
  type and of a value outside the enum, then SetShaderMode and SetCustomShader (also with a mode
  outside the enum, null, the removed type and a value outside the enum) and what reads back.
* `size.*`: CommandBuffer.sizeInBytes after one and two of each of 40 commands (property names
  of different lengths, arrays of different lengths, keywords, targets, clears, temporaries,
  blits, copies, draws, several meshes at once, matrices, culling, depth bias and profiler
  samples by name, sampler and marker), and after Clear.
* `scripting_runtime.*`, `sorting_layer.*`, `attribute.*`, `exception.*`: the project's script
  assemblies (ScriptingRuntime.GetAllUserAssemblies, sorted), SortingLayer.onLayerChanged, and
  attributes and an exception the render pipelines construct.

Measured in the Unity 6000.0.71f1 Editor on Linux with an NVIDIA GPU, windowed (two
byte-identical captures; a third attempt that crashed while the Editor started was discarded). The project has only its predefined script assemblies: whether package
assemblies and precompiled plugin DLLs count as user assemblies is not measured. `run.py record` / `publish` / `gate`
(tests/value_probe.py).
