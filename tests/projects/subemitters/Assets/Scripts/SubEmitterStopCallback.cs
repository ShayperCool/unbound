using System.Collections.Generic;
using UnityEngine;

/// <summary>On a system whose stop action is Callback: in OnParticleSystemStopped, triggers its
/// Manual sub-emitter on all its particles (none are left) and on the particles saved during the
/// last Update, recording the child's count after each call.</summary>
public class SubEmitterStopCallback : MonoBehaviour
{
    public static int Frame;
    static readonly List<ParticleSystem.Particle> s_Saved = new List<ParticleSystem.Particle>();
    static ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[64];

    public static void Save(ParticleSystem ps)
    {
        int n = ps.GetParticles(s_Buffer);
        if (n == 0) return;
        s_Saved.Clear();
        for (int i = 0; i < n; i++) s_Saved.Add(s_Buffer[i]);
    }

    void OnParticleSystemStopped()
    {
        var ps = GetComponent<ParticleSystem>();
        var child = ps.subEmitters.GetSubEmitterSystem(0);
        string f = Frame.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ps.TriggerSubEmitter(0);
        SubEmittersProbe.Record("played/stopped/callback_all/" + f, ps.particleCount, child.particleCount, s_Saved.Count);
        ps.TriggerSubEmitter(0, s_Saved);
        SubEmittersProbe.RecordParticles("played/stopped/callback_list/" + f, child);
    }
}
