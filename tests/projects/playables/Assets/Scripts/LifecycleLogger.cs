using UnityEngine;

/// <summary>Says in which player loop phase it is disabled and destroyed (Object.Destroy timing).</summary>
public class LifecycleLogger : MonoBehaviour
{
    void OnDisable() => ProbeLog.Event(FramePhases.Prefix() + name + ".OnDisable");

    void OnDestroy() => ProbeLog.Event(FramePhases.Prefix() + name + ".OnDestroy");
}
