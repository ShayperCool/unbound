using UnityEngine;

/// <summary>The probe's cookie textures (Assets/Resources/LightingTextures.asset), which the
/// cookie scenarios load.</summary>
public class LightingTextures : ScriptableObject
{
    public Texture cookieGray, cookieOpaque, cookieColor;
    public Cubemap cookieCube;
}
