using UnityEngine;

/// <summary>Writes the player loop's Update and LateUpdate into the probe's event list.</summary>
public class HooksFrameLogger : MonoBehaviour
{
    void Update() => EditorHooksProbe.Log("Update");

    void LateUpdate() => EditorHooksProbe.Log("LateUpdate");
}
