using UnityEngine;

/// <summary>Records Awake, OnEnable, OnValidate and Start: when Play mode calls OnValidate on
/// instantiated prefabs, added components and clones.</summary>
public class Validated : MonoBehaviour
{
    void Awake() => LifecycleProbe.Record(name, "Awake");
    void OnEnable() => LifecycleProbe.Record(name, "OnEnable");
    void OnValidate() => LifecycleProbe.Record(name, "OnValidate");
    void Start() => LifecycleProbe.Record(name, "Start");
}
