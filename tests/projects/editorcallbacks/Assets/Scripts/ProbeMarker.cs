using UnityEngine;

/// <summary>A behaviour of the probe's prefabs and scenes: its messages are recorded with the
/// serialized values they see.</summary>
public class ProbeMarker : MonoBehaviour
{
    public int value;
    public string label = "";

    void Awake() => Message("Awake");
    void OnEnable() => Message("OnEnable");
    void Start() => Message("Start");

    void Message(string name) =>
        ProbeLog.Event(name).Add("object", ProbeLog.PathOf(this)).Add("value", value).Add("label", label).Frame().Write();
}
