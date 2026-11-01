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
        public partial struct PlaybackState
        {
            internal Random.State m_InitialRandom0, m_InitialRandom1, m_InitialRandom2, m_InitialRandom3;
            internal bool m_InitialRandomReady;
        }
        readonly InitialSamples[] m_InitialSamples = new InitialSamples[4];
        readonly Random.State[] m_InitialCursors = new Random.State[20];

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
                // Widen before rounding: int.MaxValue still describes a valid request.
                ulong groups = ((ulong)count + 3) / 4;
                for (int lane = 0; lane < 4; lane++)
                {
                    var cursor = m_InitialRandom[lane];
                    if (external)
                    {
                        SetInitialCursor(0, lane, ref cursor, groups * (o.m_RotationSet ? 0u : m_Main.startRotation3D ? 3u : 1u));
                        SetInitialCursor(1, lane, ref cursor, groups * (o.m_StartSizeSet ? 0u : m_Main.startSize3D ? 3u : 1u));
                        SetInitialCursor(2, lane, ref cursor, groups * (o.m_StartColorSet ? 0u : 1u));
                        SetInitialCursor(3, lane, ref cursor, groups * (o.m_RandomSeedSet ? 0u : 1u));
                        SetInitialCursor(4, lane, ref cursor, groups * (o.m_StartLifetimeSet ? 0u : 1u));
                    }
                    else
                    {
                        m_InitialCursors[lane] = cursor;
                        cursor = InitialJump.Advance(cursor, groups * (5u + (m_Main.startSize3D ? 2u : 0u) + (m_Main.startRotation3D ? 2u : 0u)));
                    }
                    // Preserve the complete requested batch's advancement even when
                    // capacity rejects its tail. Samples themselves need only four slots.
                    m_InitialRandom[lane] = cursor;
                }
            }
            if ((index & 3) == 0)
                for (int lane = 0; lane < 4; lane++)
                {
                    ref var s = ref m_InitialSamples[lane];
                    s = default;
                    if (external)
                    {
                        if (!o.m_RotationSet)
                        {
                            if (m_Main.startRotation3D)
                            {
                                s.rotationX = CursorValue(0, lane);
                                s.rotationY = CursorValue(0, lane);
                            }
                            s.rotationZ = CursorValue(0, lane);
                        }
                        if (!o.m_StartSizeSet) SampleCursorSize(ref s, 1, lane);
                        if (!o.m_StartColorSet) s.color = CursorValue(2, lane);
                        s.seed = o.m_RandomSeedSet ? o.m_Particle.m_RandomSeed : CursorWord(3, lane);
                        if (!o.m_StartLifetimeSet) s.lifetime = CursorValue(4, lane);
                    }
                    else
                    {
                        s.seed = CursorWord(0, lane);
                        s.lifetime = CursorValue(0, lane);
                        SampleCursorSize(ref s, 0, lane);
                        s.rotationZ = CursorValue(0, lane);
                        if (m_Main.startRotation3D)
                        {
                            s.rotationX = CursorValue(0, lane);
                            s.rotationY = CursorValue(0, lane);
                        }
                        s.color = CursorValue(0, lane);
                    }
                }
            return m_InitialSamples[index & 3];
        }

        void SetInitialCursor(int field, int lane, ref Random.State cursor, ulong draws)
        {
            m_InitialCursors[field * 4 + lane] = cursor;
            cursor = InitialJump.Advance(cursor, draws);
        }

        uint CursorWord(int field, int lane)
        {
            ref var cursor = ref m_InitialCursors[field * 4 + lane];
            cursor.NextValue();
            return cursor.S3;
        }

        float CursorValue(int field, int lane) => InitialValue(CursorWord(field, lane));

        void SampleCursorSize(ref InitialSamples s, int field, int lane)
        {
            s.sizeX = CursorValue(field, lane);
            if (m_Main.startSize3D)
            {
                s.sizeY = CursorValue(field, lane);
                s.sizeZ = CursorValue(field, lane);
            }
        }

        // A xorshift step is linear over bits. Squared transition matrices skip
        // rejected samples in logarithmic time without changing any lane's words.
        static class InitialJump
        {
            static readonly Random.State[][] Powers = MakePowers();

            static Random.State Apply(Random.State[] matrix, Random.State state)
            {
                uint x = 0, y = 0, z = 0, w = 0;
                for (int word = 0; word < 4; word++)
                {
                    uint bits = word == 0 ? state.S0 : word == 1 ? state.S1 : word == 2 ? state.S2 : state.S3;
                    while (bits != 0)
                    {
                        int bit = System.Numerics.BitOperations.TrailingZeroCount(bits);
                        var column = matrix[word * 32 + bit];
                        x ^= column.S0; y ^= column.S1; z ^= column.S2; w ^= column.S3;
                        bits &= bits - 1;
                    }
                }
                return new Random.State(x, y, z, w);
            }

            static Random.State[][] MakePowers()
            {
                // Nine consumers per group, at most ceil(int.MaxValue / 4) groups.
                var powers = new Random.State[33][];
                powers[0] = new Random.State[128];
                for (int bit = 0; bit < 128; bit++)
                {
                    uint value = 1u << (bit & 31);
                    var state = new Random.State(bit < 32 ? value : 0u, bit >= 32 && bit < 64 ? value : 0u,
                        bit >= 64 && bit < 96 ? value : 0u, bit >= 96 ? value : 0u);
                    state.NextValue();
                    powers[0][bit] = state;
                }
                for (int power = 1; power < powers.Length; power++)
                {
                    powers[power] = new Random.State[128];
                    for (int bit = 0; bit < 128; bit++)
                        powers[power][bit] = Apply(powers[power - 1], powers[power - 1][bit]);
                }
                return powers;
            }

            internal static Random.State Advance(Random.State state, ulong draws)
            {
                // Small batches avoid matrix work and the static tables altogether.
                if (draws < 32)
                {
                    for (ulong i = 0; i < draws; i++) state.NextValue();
                    return state;
                }
                int power = 0;
                while (draws != 0)
                {
                    if ((draws & 1) != 0) state = Apply(Powers[power], state);
                    draws >>= 1;
                    power++;
                }
                return state;
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
