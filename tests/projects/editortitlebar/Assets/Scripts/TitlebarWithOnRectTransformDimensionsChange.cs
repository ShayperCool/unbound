using UnityEngine;

/// <summary>A behaviour with only OnRectTransformDimensionsChange() (the probe records whether its titlebar has the enabled toggle).</summary>
public class TitlebarWithOnRectTransformDimensionsChange : MonoBehaviour
{
    void OnRectTransformDimensionsChange() { }
}
