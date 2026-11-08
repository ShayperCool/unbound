using UnityEngine;

/// <summary>What the post-processing scenarios load (Assets/Resources/PostFxAssets.asset,
/// authored by Editor/PostFxSetup): a Color Lookup texture and the Single Channel controls of
/// texture-samples.jsonl.</summary>
public class PostFxAssets : ScriptableObject
{
    public Texture lut;
    public Texture singleAlphaCompressed, singleRedCompressed, singleRedSrgb;
}
