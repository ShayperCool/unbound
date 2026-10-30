using UnityEngine;

/// <summary>Copies the event log's clock when the camera sends OnPreRender, OnRenderObject and
/// OnPostRender, so their place among the camera events can be read back.</summary>
public class ClockMessages : MonoBehaviour
{
    public RenderTexture clock, tmp, preRender, renderObject, postRender;
    public Material increment;

    void Tick(RenderTexture slot)
    {
        if (clock == null) return;
        var active = RenderTexture.active;
        Graphics.Blit(clock, slot);
        Graphics.Blit(clock, tmp, increment, 0);
        Graphics.Blit(tmp, clock);
        RenderTexture.active = active;
    }

    void OnPreRender() => Tick(preRender);
    void OnRenderObject() => Tick(renderObject);
    void OnPostRender() => Tick(postRender);
}
