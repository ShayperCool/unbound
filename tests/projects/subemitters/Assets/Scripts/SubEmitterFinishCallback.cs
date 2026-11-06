using UnityEngine;

/// <summary>Records the frame in which a system whose stop action is Callback reports itself
/// stopped, with its particle count and its first sub-emitter's.</summary>
public class SubEmitterFinishCallback : MonoBehaviour
{
    public static int Frame;

    void OnParticleSystemStopped()
    {
        var ps = GetComponent<ParticleSystem>();
        string f = Frame.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SubEmittersProbe.Record("played/finish/stopped/" + f, ps.particleCount, ps.subEmitters.GetSubEmitterSystem(0).particleCount);
    }
}
