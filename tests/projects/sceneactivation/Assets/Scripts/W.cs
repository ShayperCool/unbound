using UnityEngine;

/// <summary>A marker of the scene activation witness (class W): records its Awake, OnEnable and
/// Start, nothing else. It has no serialized fields.</summary>
public class W : MonoBehaviour
{
    void Awake() => WakeLog.Callback(this, "Awake");
    void OnEnable() => WakeLog.Callback(this, "OnEnable");
    void Start() => WakeLog.Callback(this, "Start");
}
