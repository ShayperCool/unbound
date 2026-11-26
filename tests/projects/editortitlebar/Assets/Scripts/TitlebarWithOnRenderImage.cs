using UnityEngine;

/// <summary>A behaviour with only OnRenderImage() (the probe records whether its titlebar has the enabled toggle).</summary>
public class TitlebarWithOnRenderImage : MonoBehaviour
{
    void OnRenderImage(RenderTexture source, RenderTexture destination) { }
}
