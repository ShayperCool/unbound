using UnityEngine;

/// <summary>A behaviour with only OnTransformChildrenChanged() (the probe records whether its titlebar has the enabled toggle).</summary>
public class TitlebarWithOnTransformChildrenChanged : MonoBehaviour
{
    void OnTransformChildrenChanged() { }
}
