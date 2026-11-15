# Graphics tier settings and the scene's Lighting Data asset

Unity 6000.0.71f1, built-in render pipeline, Standalone build target, batchmode Editor on the
GPU (Linux, OpenGL Core, NVIDIA driver). `Assets/Scripts/TierSettingsProbe.cs` records
`EditorGraphicsSettings.GetTierSettings` (every field) for Tier1-3 of the platform groups
Standalone (1), iOS (4), Android (7), WebGL (13), Windows Store (14) and tvOS (25), keyed by the
group's number, and through `NamedBuildTarget` for Standalone Tier3 and Android Tier1; then
`Lightmapping.lightingDataAsset` of `Probe.unity`, which names the built-in lighting data as
scenes made in Unity 6 do, and of `NoData.unity`, which names none.

The project's GraphicsSettings have the default rendering path DeferredShading and the mobile
one VertexLit, so the automatic tiers show which they take. `create.py` ran `ProbeRunner.Create`
once in the Editor: it set Standalone Tier2 by hand, every field away from its automatic value,
and the GraphicsSettings it saved are committed.

## What the Editor does (two identical captures)

* iOS, Android, tvOS, and Windows Store's Tier1 are low-end: R11G11B10 HDR mode, every feature
  off (box projection, blending, HDR, detail normal maps, cascades, LPPV, semitransparent
  shadows), the mobile rendering path. Every other tier of these and of the other groups has
  FP16, every feature on and the default rendering path; 32-bit shadow maps are off everywhere.
* The Standard shader quality is Low for Tier1 of iOS, Android and Windows Store, Medium for
  the other iOS and Android tiers and every tvOS tier, else High.
* Realtime GI CPU usage is Medium for Standalone Tier3, else Low.
* A tier set by hand gives its own values.
* `lightingDataAsset` is the built-in `LightingData` for a scene naming it, null for a scene
  naming none.

Not measured: a project in 2D mode (whose automatic HDR is taken as off), other platform groups
(taken as Standalone's but for its Tier3 GI usage), SetTierSettings at run time, a project
Lighting Data asset (tests/projects/indirectspec names its own, `LightingData`).

```sh
python3 tests/projects/tiersettings/create.py --label <L>       # once: the hand-set tier
python3 tests/projects/tiersettings/run.py record --label <L>   # Editor capture
python3 tests/projects/tiersettings/run.py publish <L1> <L2>    # two identical captures
python3 tests/projects/tiersettings/run.py gate                 # engine vs Reference
```

The gate compares every value exactly.
