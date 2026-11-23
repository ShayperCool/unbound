using UnityEngine;
using UnityEngine.Profiling;

public sealed class SpriteMemoryCallbacks : MonoBehaviour
{
    internal Sprite Measured;
    internal long BecameVisible = -1;
    internal long WillRender = -1;

    void OnBecameVisible()
    {
        if (BecameVisible < 0) BecameVisible = Profiler.GetRuntimeMemorySizeLong(Measured);
    }

    void OnWillRenderObject()
    {
        if (WillRender < 0) WillRender = Profiler.GetRuntimeMemorySizeLong(Measured);
    }
}
