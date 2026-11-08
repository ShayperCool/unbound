# Stub defaults probe

Records what the Editor answers in Play mode for members whose generated stub would otherwise
return its type's default (0, false, null, an enum's 0). `Assets/Scripts/StubDefaultsProbe.cs`
reads each member five frames after the scene loads and writes one JSON line per value: floats as
G9 (identical on Mono and CoreCLR), colors and matrices as comma-separated floats, enums as
`Name=value`. The members' answers feed the stub generator's table
(`managed/Unbound.ApiTools/StubDefaults.cs`). The probe also guards members that hand-written code
already answers (`ScalableBufferManager` scale factors, `Microphone.devices` being an array,
`Camera.CalculateObliqueMatrix`).

The canonical Native oracle is `Reference/unity-6000.0.71f1.jsonl` (58 records, Unity 6000.0.71f1
on Linux, GPU Editor in batchmode with graphics). Two independent captures were byte-identical.

## Running

From the repository root, with the release in `UNBOUND_PROBE_UNITY_VERSION`:

```sh
python3 tests/projects/stubdefaults/run.py native --label native1   # and native2
python3 tests/projects/stubdefaults/run.py publish native1 native2
python3 tests/projects/stubdefaults/run.py check                    # the land gate (gate.json)
```

`check` runs the engine (`UNBOUND_CLI`, else `$UNBOUND`, else `target/release/unbound`) and
compares the run with the reference. `check_tooling.py` also runs the generator unit tests and
checks doctor twice against uninstantiated message-control scripts, including warm-cache analysis.
A positive control declares an unsupported message below a user-defined MonoBehaviour base, then
inherits it in another script. Doctor must report its declaration and source line exactly once.
Both commands are declared in `gate.json`. Run output goes to
`$XDG_CACHE_HOME/unbound/validation/runs/stubdefaults/runs/<label>/`.

## Comparison

`compare.py` compares every key exactly, except `Camera.CalculateObliqueMatrix`, a computed
matrix. Its floats may differ by 1e-6 relative (1e-6 absolute near zero). The engine differs there
by at most 4.5e-7 relative (float rounding of a different evaluation order). Matches within that
tolerance are printed.

## Measured (6000.0.71f1)

- `Gizmos.color` is white and `Gizmos.matrix` is the zero matrix outside `OnDrawGizmos`.
  `Gizmos.probeSize` is 0.29999998f, one ulp under 0.3f.
- `DynamicGI`: updateThreshold 1, materialUpdateTimeSlice 8, synchronousMode false, isConverged
  true. The scene has no realtime GI; a scene with realtime GI is neither measured nor supported.
- `GarbageCollector.GCMode` Enabled, incrementalTimeSliceNanoseconds 3000000.
- `ReflectionProbe` baked cubemap resolution range 16..2048; `SplashScreen.isFinished` true;
  `MuscleHandle.muscleHandleCount` 95; `Caching.compressionEnabled` true;
  `QualitySettings.maxQueuedFrames` 2; `Graphics.activeTier` Tier3, `activeColorGamut` sRGB;
  `PixelPerfectRendering.pixelSnapSpacing` 0; `RenderTexture.GetTexelOffset` (0, 0);
  `AccessibilitySettings.fontScale` 1; `AvatarMask.humanoidBodyPartCount` 13.
- Obsolete members whose message states the answer: `SystemInfo.supportsRenderTextures` true,
  `supportsStencil` 1, `supportsVertexPrograms` true, `graphicsPixelFillrate` -1,
  `RenderTexture.enabled` true, `Application.isPlayer` false. (`Light.attenuate` and
  `Application.webSecurityEnabled` are compile errors in Unity, so no script reads them.)

Not recorded, because the answer is not a constant: `DynamicGI.indirectScale` is the scene's
Indirect Intensity; `GarbageCollector.isIncremental` follows the
project's `gcIncremental` player setting, and `Caching.cacheCount` (1 in the Editor) would claim a
cache that the stubbed `Caching` API cannot hand out.

## Setter controls (6000.0.71f1)

The same Play-mode script writes each settable table member, reads it immediately, and restores
its original value. Two independent captures agree. Existing default-value records are unchanged.

| Member | Measured writes |
| --- | --- |
| DynamicGI.updateThreshold | 0.25, -2 and 5 are retained |
| DynamicGI.materialUpdateTimeSlice | 3 and 25 are retained; -1 reads back as 0 |
| GarbageCollector.GCMode | Disabled, Manual and undefined 99 throw InvalidOperationException; Enabled succeeds |
| GarbageCollector.incrementalTimeSliceNanoseconds | 1234567, 0 and 9000000 are retained |
| Caching.compressionEnabled | false/true/false are retained |
| QualitySettings.maxQueuedFrames | 1, 4 and -1 are retained |
| Graphics.activeTier | Tier1/Tier2/Tier3 are retained |
| RenderTexture.enabled | false/true/false all read back as true |

The Unity 6000.0 Scripting API documents the get/set contract for
[GCMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Scripting.GarbageCollector.GCMode.html),
[incrementalTimeSliceNanoseconds](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Scripting.GarbageCollector-incrementalTimeSliceNanoseconds.html),
[materialUpdateTimeSlice](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/DynamicGI-materialUpdateTimeSlice.html),
[activeTier](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics-activeTier.html), and
[compressionEnabled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Caching-compressionEnabled.html).
[maxQueuedFrames](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/QualitySettings-maxQueuedFrames.html)
is documented as applying only to Direct3D 11/12 and Vulkan. These captures establish getter and
setter answers in this Editor context; generated members still warn that their engine effects
are unsupported. They do not claim to control CoreCLR collection, cache compression or GPU queues.
