using System;
using UnityEngine;

/// <summary>Renders <see cref="Camera"/> in LateUpdate (after the frame's particle update) and then
/// runs <see cref="Late"/>, which records what the probe needs.</summary>
public class LateRender : MonoBehaviour
{
    public Camera Camera;
    public Action Late;

    void LateUpdate()
    {
        if (Camera != null) Camera.Render();
        Late?.Invoke();
    }
}
