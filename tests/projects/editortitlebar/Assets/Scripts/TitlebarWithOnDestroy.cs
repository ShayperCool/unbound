using UnityEngine;

/// <summary>A behaviour with only OnDestroy() (the probe records whether its titlebar has the enabled toggle).</summary>
public class TitlebarWithOnDestroy : MonoBehaviour
{
    void OnDestroy() { }
}
