using UnityEditor;
using UnityEngine;

/// <summary>Staged into the URPHookRecord profile's Assets/Editor only (run.py): a project
/// postprocessor whose OnPostprocessMaterial records each Exotic material's state
/// (MaterialStages, stage "postprocess": its shader, GI flags, HasProperty and GetFloat) and
/// changes nothing itself.</summary>
public class MaterialPostprocessRecord : AssetPostprocessor
{
    public void OnPostprocessMaterial(Material material) => MaterialStages.Record(assetPath, material, "postprocess");
}
