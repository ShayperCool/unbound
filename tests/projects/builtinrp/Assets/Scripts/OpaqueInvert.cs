using UnityEngine;

/// <summary>An image effect after the opaque objects: inverts the image.</summary>
public class OpaqueInvert : MonoBehaviour
{
    public Material material;

    [ImageEffectOpaque]
    void OnRenderImage(RenderTexture source, RenderTexture destination) => Graphics.Blit(source, destination, material, 0);
}
