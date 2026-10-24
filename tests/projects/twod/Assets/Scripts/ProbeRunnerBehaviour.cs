using UnityEngine;

/// <summary>Runs the probe from its first frame's Start.</summary>
public class ProbeRunnerBehaviour : MonoBehaviour
{
    void Start() => StartCoroutine(ProbeEntry.Run());
}
