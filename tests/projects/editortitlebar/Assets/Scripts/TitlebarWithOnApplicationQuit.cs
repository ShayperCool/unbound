using UnityEngine;

/// <summary>A behaviour with only OnApplicationQuit() (the probe records whether its titlebar has the enabled toggle).</summary>
public class TitlebarWithOnApplicationQuit : MonoBehaviour
{
    void OnApplicationQuit() { }
}
