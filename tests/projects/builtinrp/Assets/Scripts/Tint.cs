using UnityEngine;

/// <summary>An image effect after everything: multiplies the image by the material's tint.</summary>
public class Tint : MonoBehaviour
{
    public Material material;

    void OnRenderImage(RenderTexture source, RenderTexture destination) => Graphics.Blit(source, destination, material, 1);
}
