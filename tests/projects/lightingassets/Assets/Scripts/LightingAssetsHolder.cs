using UnityEngine;

/// <summary>The assets the probe reads, referenced from the first scene.</summary>
public class LightingAssetsHolder : MonoBehaviour
{
    public LightingSettings custom;
    public ShaderVariantCollection warmup;
    public ShaderVariantCollection authored;
    public ShaderVariantCollection dupes;
    public ShaderVariantCollection missing;
    public Shader variants;
}
