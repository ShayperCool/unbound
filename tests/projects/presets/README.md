# Presets probe

What `UnityEditor.Presets.Preset`, `PresetType` and `DefaultPreset` answer in Unity 6000.0.71f1
(recorded with the macOS Editor in batch mode; two identical captures `r8m18a` == `r8m18b`, 101 rows).

`Assets/Scripts/PresetsProbe.cs` exercises:

* Loading `.preset` YAML assets (`Assets/Presets/Transform.preset`, `Tuned.preset`, `Box.preset`,
  `Material.preset`, `MissingScript.preset`): `name`, `GetTargetFullTypeName`, `GetTargetTypeName`,
  `IsValid`, `GetPresetType`, `PropertyModifications`, `excludedProperties`.
* `CanBeAppliedTo`, `ApplyTo` (full and partial `selectedPropertyPaths`, wrong target type, null
  target), `DataEquals` (including `Material.preset` and `excludedProperties`), `excludedProperties`,
  and `UpdateProperties` on `Transform`, `MonoBehaviour` (`Tuned`), `BoxCollider`, and `Material`.
* `Preset.GetDefaultPresetsForObject`, `Preset.GetDefaultPresetsForType`, `Preset.GetAllDefaultTypes`,
  and `Preset.RemoveFromDefault(null)` reading `ProjectSettings/PresetManager.asset` (unfiltered,
  single-token `glob:"Named*"` case-sensitive/child/slash controls, disabled entries, empty type
  entry filtered out by `GetAllDefaultTypes`, null target).
* `PresetType` (`IsValid`, `IsValidDefault`, `GetManagedTypeName`, equality) across `Transform`,
  `MonoBehaviour`, `Material`, `GameObject`, `default(PresetType)`, `ScriptableObject`,
  `TextureImporter`, `ScriptedImporter` (`TuneImporter`), `AnimationClip`, `Texture2D`, `null`,
  `Preset`, and a missing script preset.
* `DefaultPreset` constructors and defaults.
* `new Preset(Object source)` on live `Transform`, `MonoBehaviour` (`Tuned`), `ScriptableObject`
  (`TunedData`), and excluded `GameObject`.

Record and gate:

    python3 tests/projects/presets/run.py record --label a   # twice, then
    python3 tests/projects/presets/run.py publish a b
    python3 tests/projects/presets/run.py gate
