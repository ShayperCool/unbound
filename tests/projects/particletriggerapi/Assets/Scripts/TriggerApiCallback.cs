using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>OnParticleTrigger of a TriggerApiProbe system: records every event type's list (both
/// GetTriggerParticles overloads, the collider data) and, by <see cref="Mode"/>, writes changes
/// back: <c>write</c> recolours Enter, halves Inside velocities and shrinks Exit particles
/// (whole lists); <c>subset</c> writes Inside entries 1..2 only; <c>kill</c> zeroes the remaining
/// lifetime of Enter particles; <c>twice</c> writes Inside twice, the second list with another size.</summary>
public class TriggerApiCallback : MonoBehaviour
{
    public string Mode;
    readonly List<ParticleSystem.Particle> m_List = new List<ParticleSystem.Particle>();
    readonly List<ParticleSystem.Particle> m_Plain = new List<ParticleSystem.Particle>();
    static readonly HashSet<ParticleSystemTriggerEventType> s_Logged = new HashSet<ParticleSystemTriggerEventType>();

    void OnParticleTrigger()
    {
        var ps = GetComponent<ParticleSystem>();
        string key = TriggerApiProbe.Current + "/callback/" + TriggerApiProbe.Frame;
        foreach (ParticleSystemTriggerEventType type in Enum.GetValues(typeof(ParticleSystemTriggerEventType)))
        {
            int safe = ps.GetSafeTriggerParticlesSize(type);
            int plain = ps.GetTriggerParticles(type, m_Plain);
            // The collider data overload: -1 when it throws (the list is then the plain one).
            int n;
            bool hasData = true;
            ParticleSystem.ColliderData data = default;
            int message = 0;
            try { n = ps.GetTriggerParticles(type, m_List, out data); }
            catch (InvalidOperationException e)
            {
                hasData = false;
                n = ps.GetTriggerParticles(type, m_List);
                message = e.Message.Length;
                if (s_Logged.Add(type)) Debug.Log("collider data " + type + ": " + e.Message);
            }
            var v = new List<float> { safe, plain, hasData ? n : -1, message };
            for (int i = 0; i < n; i++)
            {
                TriggerApiProbe.AddParticle(v, m_List[i]);
                if (!hasData) continue;
                int colliders = data.GetColliderCount(i);
                v.Add(colliders);
                for (int c = 0; c < colliders; c++) v.Add(TriggerApiProbe.ColliderId(data.GetCollider(i, c)));
            }
            // Past the last collider: the exception is recorded as -2.
            if (n > 0 && hasData)
            {
                try { v.Add(TriggerApiProbe.ColliderId(data.GetCollider(0, data.GetColliderCount(0)))); }
                catch (Exception) { v.Add(-2); }
            }
            TriggerApiProbe.Record(key + "/" + type.ToString().ToLowerInvariant(), v);
            if (n == 0) continue;
            switch (Mode)
            {
                case "write":
                    for (int i = 0; i < n; i++)
                    {
                        var p = m_List[i];
                        if (type == ParticleSystemTriggerEventType.Enter) p.startColor = new Color32(255, 0, 0, 255);
                        else if (type == ParticleSystemTriggerEventType.Inside) p.velocity *= 0.5f;
                        else if (type == ParticleSystemTriggerEventType.Exit) p.startSize = 0.3f;
                        else continue;
                        m_List[i] = p;
                    }
                    ps.SetTriggerParticles(type, m_List);
                    break;
                case "subset" when type == ParticleSystemTriggerEventType.Inside:
                    for (int i = 0; i < n; i++)
                    {
                        var p = m_List[i];
                        p.startSize += 0.01f;
                        m_List[i] = p;
                    }
                    ps.SetTriggerParticles(type, m_List, 1, 2);
                    break;
                case "kill" when type == ParticleSystemTriggerEventType.Enter:
                    for (int i = 0; i < n; i++)
                    {
                        var p = m_List[i];
                        p.remainingLifetime = 0f;
                        m_List[i] = p;
                    }
                    ps.SetTriggerParticles(type, m_List);
                    break;
                case "twice" when type == ParticleSystemTriggerEventType.Inside:
                    for (int i = 0; i < n; i++)
                    {
                        var p = m_List[i];
                        p.startSize = 0.2f;
                        p.position += new Vector3(0f, 0f, 0.01f);
                        m_List[i] = p;
                    }
                    ps.SetTriggerParticles(type, m_List);
                    for (int i = 0; i < n; i++)
                    {
                        var p = m_List[i];
                        p.startSize = 0.25f;
                        m_List[i] = p;
                    }
                    ps.SetTriggerParticles(type, m_List);
                    break;
            }
        }
    }
}
