using UnityEngine;

/// <summary>A behaviour with only OnApplicationPause() (the probe records whether its titlebar has the enabled toggle).</summary>
public class TitlebarWithOnApplicationPause : MonoBehaviour
{
    void OnApplicationPause(bool paused) { }
}
