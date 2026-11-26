using UnityEngine;

/// <summary>A behaviour with only OnPreCull() (the probe records whether its titlebar has the enabled toggle).</summary>
public class TitlebarWithOnPreCull : MonoBehaviour
{
    void OnPreCull() { }
}
