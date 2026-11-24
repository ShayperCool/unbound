# lightmappinghooks

Measures Unity 6000.0.71f1 Editor behaviour for:

- `Lightmapping.GetLightingDataAssetForScene(Scene)` across `default`, active unbaked, non-active additive baked, active baked, closed (kept unloaded), and removed scenes.
- `Lightmapping.AdditionalBakeDelegate` (`GetAdditionalBakeDelegate`, `SetAdditionalBakeDelegate`, `ResetAdditionalBakeDelegate`) and multi-step `Lightmapping.Bake()` event ordering (`bakeStarted`, `lightingDataUpdated`, additional bake steps, `bakeCompleted`).
- `Lightmapping.Cancel()` and `Lightmapping.bakeCancelled` outside a bake, inside `AdditionalBakeDelegate`, inside `bakeStarted`, inside `lightingDataUpdated`, and in Play mode.
