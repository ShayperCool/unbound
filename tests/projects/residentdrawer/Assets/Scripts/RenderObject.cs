using UnityEngine;

/// <summary>A script receiving OnRenderObject (the Manual's example of a callback that keeps a
/// GameObject from the GPU Resident Drawer).</summary>
public class RenderObject : MonoBehaviour
{
    public int calls;

    void OnRenderObject() => calls++;
}
