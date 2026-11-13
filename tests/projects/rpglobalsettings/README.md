# Render pipeline global settings brought to what the pipeline supports

A URP 17 project (the pipeline asset and renderer of tests/projects/urp) whose
`Assets/UniversalRenderPipelineGlobalSettings.asset` lacks the
`ScreenSpaceAmbientOcclusionPersistentResources` entry (an asset saved by an older URP has none)
and stores `UniversalRenderPipelineRuntimeShaders.m_FallbackErrorShader` as null.
`ScreenSpaceAmbientOcclusionDynamicResources` is stored whole (a control).

`Assets/Scripts/GlobalSettingsProbe.cs` records, on the third frame (the pipeline exists), what
`GraphicsSettings.TryGetRenderPipelineSettings` answers for these settings and the resources they
hold.

Measured in the Unity 6000.0.71f1 Editor (two identical captures on the NVIDIA driver): the
missing settings are there with their shader loaded from its resource path, the null shader is
loaded, the stored resources are kept. Unity's C# reference (`EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset`,
which URP calls when it makes its pipeline) populates the asset this way.

    python3 tests/projects/rpglobalsettings/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/rpglobalsettings/run.py publish A B        # two identical captures
    python3 tests/projects/rpglobalsettings/run.py gate               # the engine vs Reference/
