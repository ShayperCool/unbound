using UnityEngine;

/// <summary>Counts the camera's renders (OnPreRender).</summary>
public class RenderCounter : MonoBehaviour
{
    public int renders;

    void OnPreRender() => renders++;
}
