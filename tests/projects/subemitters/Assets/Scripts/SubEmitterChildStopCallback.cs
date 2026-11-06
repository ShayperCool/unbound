using UnityEngine;

/// <summary>On a sub-emitter child whose stop action is Callback: records the frame in which it
/// reports itself stopped, with its particle count.</summary>
public class SubEmitterChildStopCallback : MonoBehaviour
{
    void OnParticleSystemStopped()
    {
        string f = SubEmitterFinishCallback.Frame.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SubEmittersProbe.Record("played/finish/child_stopped/" + f, GetComponent<ParticleSystem>().particleCount);
    }
}
