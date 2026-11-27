# Presets probe

What `UnityEditor.Presets.Preset`, `PresetType` and `DefaultPreset` answer in Unity 6000.0.71f1
(recorded with the macOS Editor in batch mode; two identical captures `m17c` == `m17d`).

`Assets/Scripts/PresetsProbe.cs` exercises:

* Loading `.preset` YAML assets (`Assets/Presets/Transform.preset`, `Tuned.preset`, `Box.preset`,
  `Material.preset`): `name`, `GetTargetFullTypeName`, `GetTargetTypeName`, `IsValid`,
  `GetPresetType`, `PropertyModifications`, `excludedProperties`.
* `CanBeAppliedTo` and `ApplyTo` (full and partial `selectedPropertyPaths`, wrong target type,
  null target) on `Transform`, `MonoBehaviour` (`Tuned`), `BoxCollider`, and `Material`.
* `Preset.GetDefaultPresetsForObject` and `Preset.GetDefaultPresetsForType` reading
  `ProjectSettings/PresetManager.asset` (unfiltered, `glob:"Named*"` match/non-match, disabled
  entries, null target).
* `PresetType` (`IsValid`, `IsValidDefault`, `GetManagedTypeName`, equality) across `Transform`,
  `MonoBehaviour`, `Material`, `GameObject`, `default(PresetType)`, `ScriptableObject`,
  `TextureImporter`, `AnimationClip`, `Texture2D`, `null`, and `Preset`.
* `DefaultPreset` constructors and defaults.
* `new Preset(Object source)` on live `Transform`, `MonoBehaviour` (`Tuned`), and excluded
  `GameObject`.

Record and gate:

    python3 tests/projects/presets/run.py record --label a   # twice, then
    python3 tests/projects/presets/run.py publish a b
    python3 tests/projects/presets/run.py gate
