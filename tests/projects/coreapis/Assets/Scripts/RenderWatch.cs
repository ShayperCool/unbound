using UnityEngine;
using UnityEngine.Rendering;

/// <summary>On the probe cameras and the cube: the render messages a frame sends, recorded only in
/// the OnDemandRendering window.</summary>
public class RenderWatch : MonoBehaviour
{
    void Ev(string e) => CoreProbe.RenderEvent(name, e);
    void OnPreCull() => Ev("OnPreCull");
    void OnPreRender() => Ev("OnPreRender");
    void OnPostRender() => Ev("OnPostRender");
    void OnRenderObject() => Ev("OnRenderObject cam=" + (Camera.current != null ? Camera.current.name : "null"));
    void OnWillRenderObject() => Ev("OnWillRenderObject cam=" + (Camera.current != null ? Camera.current.name : "null"));
}
