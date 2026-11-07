using System;

namespace UnityEngine
{
    public sealed partial class ParticleSystem
    {
        readonly Random.State[] m_ShapeRandom = new Random.State[4];
        readonly Random.State[] m_ShapeCursors = new Random.State[4];
        bool m_ShapeRandomReady;
        int m_ShapeLane;

        public partial struct PlaybackState
        {
            internal Random.State m_ShapeRandom0, m_ShapeRandom1, m_ShapeRandom2, m_ShapeRandom3;
            internal bool m_ShapeRandomReady;
        }

        void ResetShapeRandom(uint seed)
        {
            for (int lane = 0; lane < 4; lane++)
                m_ShapeRandom[lane] = Random.State.FromSeed(unchecked((int)(seed + 367u * (uint)lane)));
            m_ShapeRandomReady = true;
        }

        void BeginShapeSample(int index)
        {
            m_ShapeLane = index & 3;
            if (m_ShapeLane == 0)
                Array.Copy(m_ShapeRandom, m_ShapeCursors, 4);
        }

        // Shape samplers advance packed groups independently of initialization and
        // emission. The first lane advances all four, including temporary lanes;
        // retained later lanes read from their copy of the group's starting state.
        float ShapeValue()
        {
            if (m_ShapeLane != 0) return m_ShapeCursors[m_ShapeLane].NextValue();
            for (int lane = 0; lane < 4; lane++) m_ShapeRandom[lane].NextValue();
            return InitialValue(m_ShapeRandom[0].S3);
        }

        // A fixed-length offset; azimuth precedes height in the shared shape stream.
        Vector3 ShapeModifierDirection()
        {
            float angle = ShapeValue() * (Mathf.PI * 2f);
            return UnitVector(ShapeValue() * 2f - 1f, angle);
        }

        /// <summary>The unit vector at height <paramref name="z"/> in [-1, 1] and azimuth
        /// <paramref name="angle"/> (radians) about +Z.</summary>
        static Vector3 UnitVector(float z, float angle)
        {
            float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(ring * Mathf.Cos(angle), ring * Mathf.Sin(angle), z);
        }

        // The radial sampler evaluates a polynomial log2/exp2 approximation.
        // Replacing it with Pow changes sphere positions beyond the probe tolerance.
        static float ShapeCubeRoot(float value)
        {
            uint bits = BitConverter.SingleToUInt32Bits(value);
            float m = BitConverter.UInt32BitsToSingle((bits & 0x007fffffu) | 0x3f800000u) - 1f;
            float polynomial = (m * 0.19438113272190094f - 0.6322882175445557f) * (m * m);
            float exponent = (int)(bits >> 23) - 127f;
            float power = ((m * 1.437907099723816f + exponent) + polynomial) * (1f / 3f);
            power = Math.Max(power, -127f);
            float integral = MathF.Floor(power);
            float fraction = power - integral;
            float scale = BitConverter.UInt32BitsToSingle(unchecked((uint)((int)integral << 23) + 0x3f800000u));
            return scale * ((fraction * 0.6822701096534729f + 1f) + fraction * fraction * 0.3177299201488495f);
        }
    }
}
