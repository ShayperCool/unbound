# Engine members render pipelines and packages call

`Assets/Scripts/RuntimeApisProbe.cs` writes values.jsonl in `$RUNTIMEAPIS_OUT`
(tests/value_probe.py): for every action, what it returns or throws and the log messages it
writes with their log type (`.logs`).

* `camera.*`: a camera's stereo projection, non-jittered projection and view matrices per eye
  without an XR device: unset, after SetStereoProjectionMatrix / SetStereoViewMatrix, after
  CopyStereoDeviceProjectionMatrixToNonJittered and the resets, next to the camera's own
  matrices; the scene view filter mode.
* `gamut.*`: ColorGamutUtility's primaries, white point and transfer function of every
  ColorGamut.
* `shader_mode.*`: GraphicsSettings.GetShaderMode of every BuiltinShaderType with the default
  graphics settings.
* `transform.*`: Transform.rotationOrder and GetLocalEulerAngles in every rotation order, after
  Euler hints (SetLocalEulerHint), with fractions and wide angles and after localEulerAngles is
  set; that neither the hint nor the order changes the rotation.
* `quality.*`, `screen.*`: Screen.msaaSamples under QualitySettings.antiAliasing levels and
  Screen.SetMSAASamples with the built-in renderer; `srp.*` the same after a scriptable render
  pipeline (one that renders nothing) is assigned as GraphicsSettings.defaultRenderPipeline, a
  frame later, and after it is unassigned.
* `texture.*`, `command_buffer.*`, `context.*`: Texture.SetStreamingTextureMaterialDebugProperties,
  CommandBuffer.SwitchIntoFastMemory / SwitchOutOfFastMemory and sizeInBytes,
  ScriptableRenderContext.EmitGeometryForCamera / EmitWorldGeometryForSceneView (also with null).
* `scripting_runtime.*`, `sorting_layer.*`, `attribute.*`, `exception.*`: the project's script
  assemblies (ScriptingRuntime.GetAllUserAssemblies, sorted), SortingLayer.onLayerChanged, and
  attributes and an exception the render pipelines construct.

Measured in the Unity 6000.0.71f1 Editor on Linux with an NVIDIA GPU, windowed (two
byte-identical captures). The project has only its predefined script assemblies: whether package
assemblies count as user assemblies is not measured. `run.py record` / `publish` / `gate`
(tests/value_probe.py).

Recorded residuals (`residuals.json`): CommandBuffer.sizeInBytes counts 64 bytes per recorded
command, not the size of Unity's own encoding of each command.
