using UnityEngine;

/// <summary>A behaviour with only OnTransformParentChanged() (the probe records whether its titlebar has the enabled toggle).</summary>
public class TitlebarWithOnTransformParentChanged : MonoBehaviour
{
    void OnTransformParentChanged() { }
}
