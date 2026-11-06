using System.Collections.Generic;
using UnityEngine;

/// <summary>In OnParticleTrigger, triggers the system's Manual sub-emitter on the particles that
/// entered the trigger volume, recording the entered particles and the child after the call.</summary>
public class SubEmitterTriggerCallback : MonoBehaviour
{
    public static int Frame;
    readonly List<ParticleSystem.Particle> m_Enter = new List<ParticleSystem.Particle>();

    void OnParticleTrigger()
    {
        var ps = GetComponent<ParticleSystem>();
        int n = ps.GetTriggerParticles(ParticleSystemTriggerEventType.Enter, m_Enter);
        if (n == 0) return;
        string f = Frame.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var p = m_Enter[0].position;
        SubEmittersProbe.Record("played/trigger/enter/" + f, n, p.x, p.y, p.z);
        ps.TriggerSubEmitter(0, m_Enter);
        SubEmittersProbe.RecordParticles("played/trigger/callback/" + f, ps.subEmitters.GetSubEmitterSystem(0));
    }
}
