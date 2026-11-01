using Unbound.Runtime;
using R = Unbound.Runtime.ParticleRandom;

namespace UnityEngine
{
    public sealed partial class ParticleSystem
    {
        /// <summary>Birth sub-emitters emit with their own rate and bursts over the parent particle's life.</summary>
        void BirthSubEmitters(ref Particle parent, float ageBefore, float ageAfter, float dt)
        {
            int slot = 0;
            foreach (var e in m_SubEmitters.emitters)
            {
                if (e.type != ParticleSystemSubEmitterType.Birth) continue;
                var child = e.system;
                if (child == null || child == this || child.m_GameObject == null) { slot++; continue; }
                if (e.emitProbability < 1f && ParticleRandom.Value(parent.m_RandomSeed, R.SubEmit + (uint)slot) >= e.emitProbability) { slot++; continue; }
                float age01 = parent.Age01;
                int n = 0;
                if (child.m_Emission.enabled)
                {
                    float rate = child.m_Emission.rateOverTime.Evaluate(age01, child.NextFloat());
                    if (slot == 0)
                    {
                        parent.m_EmitAccumulator0 += rate * dt;
                        n = (int)parent.m_EmitAccumulator0;
                        parent.m_EmitAccumulator0 -= n;
                    }
                    else
                    {
                        parent.m_EmitAccumulator1 += rate * dt;
                        n = (int)parent.m_EmitAccumulator1;
                        parent.m_EmitAccumulator1 -= n;
                    }
                    foreach (var burst in child.m_Emission.bursts)
                    {
                        float at = burst.m_Time;
                        bool first = ageBefore <= 0f && at <= 0f;
                        if (first || (at > ageBefore && at <= ageAfter))
                            n += child.BurstCount(burst.m_Count, age01);
                    }
                }
                if (n > 0) child.EmitFromParent(this, ref parent, e.properties, n, 0f);
                slot++;
            }
        }
    }
}
