using System;

namespace UnityEngine
{
    public sealed partial class ParticleSystem
    {
        // Measured by particleseedaxis: each emitting call rounds its initialization
        // to four lanes. Unused lanes advance too; they are not reused by the next call.
        readonly Random.State[] m_InitialRandom = new Random.State[4];
        bool m_InitialRandomReady;
        bool m_AxisOfRotationAllocated;
        InitialSamples[] m_InitialSamples = Array.Empty<InitialSamples>();

        struct InitialSamples
        {
            internal uint seed;
            internal float lifetime, sizeX, sizeY, sizeZ, rotationX, rotationY, rotationZ, color;
        }

        void ResetInitialRandom(uint seed)
        {
            for (int lane = 0; lane < 4; lane++)
                m_InitialRandom[lane] = Random.State.FromSeed(unchecked((int)(seed + 367u * (uint)lane)));
            m_InitialRandomReady = true;
        }

        uint InitialWord(int lane)
        {
            m_InitialRandom[lane].NextValue();
            return m_InitialRandom[lane].S3;
        }

        static float InitialValue(uint word) => (word & 0x007fffffu) * (1f / 8388607f);

        // Speed and flip use samples owned by the stored particle seed, independent
        // of the initialization lanes. The randomized probe checks their outputs.
        static float InitialSeedValue(uint seed, uint offset)
        {
            var state = Random.State.FromSeed(unchecked((int)(seed + offset)));
            return state.NextValue();
        }

        InitialSamples InitialSamplesAt(int index, int count, bool external, ref EmitParams o)
        {
            if (index == 0)
            {
                if (!m_InitialRandomReady)
                    ResetInitialRandom(m_Main.autoRandomSeed ? NextAutoSeed() : m_Main.randomSeed);
                int rounded = (count + 3) & ~3;
                if (m_InitialSamples.Length < rounded) Array.Resize(ref m_InitialSamples, rounded);
                if (external)
                {
                    // EmitParams initializes each unset field across the batch. Set
                    // fields do not consume samples, including an explicit seed.
                    for (int start = 0; start < rounded; start += 4)
                        for (int lane = 0; lane < 4; lane++)
                        {
                            ref var s = ref m_InitialSamples[start + lane];
                            s = default;
                            if (!o.m_RotationSet)
                            {
                                if (m_Main.startRotation3D)
                                {
                                    s.rotationX = InitialValue(InitialWord(lane));
                                    s.rotationY = InitialValue(InitialWord(lane));
                                }
                                s.rotationZ = InitialValue(InitialWord(lane));
                            }
                        }
                    for (int start = 0; start < rounded; start += 4)
                        for (int lane = 0; lane < 4; lane++)
                        {
                            ref var s = ref m_InitialSamples[start + lane];
                            if (!o.m_StartSizeSet) SampleInitialSize(ref s, lane);
                        }
                    for (int start = 0; start < rounded; start += 4)
                        for (int lane = 0; lane < 4; lane++)
                            if (!o.m_StartColorSet) m_InitialSamples[start + lane].color = InitialValue(InitialWord(lane));
                    for (int start = 0; start < rounded; start += 4)
                        for (int lane = 0; lane < 4; lane++)
                            m_InitialSamples[start + lane].seed = o.m_RandomSeedSet ? o.m_Particle.m_RandomSeed : InitialWord(lane);
                    for (int start = 0; start < rounded; start += 4)
                        for (int lane = 0; lane < 4; lane++)
                            if (!o.m_StartLifetimeSet) m_InitialSamples[start + lane].lifetime = InitialValue(InitialWord(lane));
                }
                else
                {
                    for (int start = 0; start < rounded; start += 4)
                        for (int lane = 0; lane < 4; lane++)
                        {
                            ref var s = ref m_InitialSamples[start + lane];
                            s = default;
                            s.seed = InitialWord(lane);
                            s.lifetime = InitialValue(InitialWord(lane));
                            SampleInitialSize(ref s, lane);
                            s.rotationZ = InitialValue(InitialWord(lane));
                            if (m_Main.startRotation3D)
                            {
                                s.rotationX = InitialValue(InitialWord(lane));
                                s.rotationY = InitialValue(InitialWord(lane));
                            }
                            s.color = InitialValue(InitialWord(lane));
                        }
                }
            }
            return m_InitialSamples[index];
        }

        void SampleInitialSize(ref InitialSamples s, int lane)
        {
            s.sizeX = InitialValue(InitialWord(lane));
            if (m_Main.startSize3D)
            {
                s.sizeY = InitialValue(InitialWord(lane));
                s.sizeZ = InitialValue(InitialWord(lane));
            }
        }

        void MaterializeAxisOfRotation()
        {
            if (m_AxisOfRotationAllocated) return;
            m_AxisOfRotationAllocated = true;
            for (int i = 0; i < m_Count; i++) m_Particles[i].m_AxisOfRotation = Vector3.up;
        }
    }
}
