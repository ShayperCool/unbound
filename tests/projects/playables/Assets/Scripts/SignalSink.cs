using UnityEngine;
using UnityEngine.Playables;

/// <summary>Receives the timeline's signals (through SignalReceiver reactions) and animation events,
/// and writes them to the probe's event list with the director's time.</summary>
public class SignalSink : MonoBehaviour
{
    public static PlayableDirector Director;
    public static bool Logging;

    static string Time() => Director != null ? ProbeLog.F(Director.time) : "-";

    public void OnPing(string from)
    {
        if (Logging) ProbeLog.Event($"{(TraceBehaviour.Prefix != null ? TraceBehaviour.Prefix() : "")}signal {from} on {name} at {Time()}");
    }

    public void OnProbeEvent(float value)
    {
        if (Logging) ProbeLog.Event($"{(TraceBehaviour.Prefix != null ? TraceBehaviour.Prefix() : "")}animation event {ProbeLog.F(value)} on {name}");
    }
}
