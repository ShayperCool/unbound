using UnityEngine;

/// <summary>A marker of the scene activation witness with script execution order -10: records
/// its Awake, OnEnable and Start, nothing else. It has no serialized fields.</summary>
[DefaultExecutionOrder(-10)]
public class WE : MonoBehaviour
{
    void Awake() => WakeLog.Callback(this, "Awake");
    void OnEnable() => WakeLog.Callback(this, "OnEnable");
    void Start() => WakeLog.Callback(this, "Start");
}
