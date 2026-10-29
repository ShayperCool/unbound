using UnityEngine;

/// <summary>Application messages on a second object (scene order).</summary>
public class AppMessages : MonoBehaviour
{
    void Awake() => ProbeLog.Add("life", "o", ProbeLog.Q(name), "m", "\"Awake\"");
    void OnEnable() => ProbeLog.Add("life", "o", ProbeLog.Q(name), "m", "\"OnEnable\"");
    void Start() => ProbeLog.Add("life", "o", ProbeLog.Q(name), "m", "\"Start\"");
    void OnApplicationFocus(bool focus) => ProbeLog.Add("app", "o", ProbeLog.Q(name), "m", "\"Focus\"", "v", focus ? "true" : "false",
        "p", ProbeLog.Q(MessagesProbe.Phase), "isFocused", Application.isFocused ? "true" : "false");
    void OnApplicationPause(bool pause) => ProbeLog.Add("app", "o", ProbeLog.Q(name), "m", "\"Pause\"", "v", pause ? "true" : "false",
        "p", ProbeLog.Q(MessagesProbe.Phase), "isFocused", Application.isFocused ? "true" : "false");
}
