using UnityEngine;

/// <summary>A behaviour with only OnApplicationFocus() (the probe records whether its titlebar has the enabled toggle).</summary>
public class TitlebarWithOnApplicationFocus : MonoBehaviour
{
    void OnApplicationFocus(bool focused) { }
}
