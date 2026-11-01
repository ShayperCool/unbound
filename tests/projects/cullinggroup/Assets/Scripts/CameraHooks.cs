using UnityEngine;

/// <summary>A camera's own messages, in the probe's record.</summary>
public class CameraHooks : MonoBehaviour
{
    void OnPreCull() => ProbeLog.Add("OnPreCull", "cam", ProbeLog.Q(name));
    void OnPreRender() => ProbeLog.Add("OnPreRender", "cam", ProbeLog.Q(name));
    void OnPostRender() => ProbeLog.Add("OnPostRender", "cam", ProbeLog.Q(name));
}
