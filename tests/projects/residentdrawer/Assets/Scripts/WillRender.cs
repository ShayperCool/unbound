using UnityEngine;

/// <summary>A script receiving OnWillRenderObject: the GPU Resident Drawer leaves the renderer
/// of its GameObject to the ordinary draw path, which sends the message.</summary>
public class WillRender : MonoBehaviour
{
    public int calls;

    void OnWillRenderObject() => calls++;
}
