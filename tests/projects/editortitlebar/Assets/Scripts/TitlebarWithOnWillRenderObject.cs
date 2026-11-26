using UnityEngine;

/// <summary>A behaviour with only OnWillRenderObject() (the probe records whether its titlebar has the enabled toggle).</summary>
public class TitlebarWithOnWillRenderObject : MonoBehaviour
{
    void OnWillRenderObject() { }
}
