using UnityEngine;

/// <summary>What the renderer feature scenarios load (Assets/Resources/FeatureAssets.asset,
/// authored by Editor/FeatureSetup): decal materials and Single Channel textures.</summary>
public class FeatureAssets : ScriptableObject
{
    public Material decal, decalAngleFade;
    public Texture singleAlpha, singleRed;
}
