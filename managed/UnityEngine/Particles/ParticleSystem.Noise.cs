using System;

namespace UnityEngine
{
    public sealed partial class ParticleSystem
    {
        float m_NoiseScroll;
        internal bool m_NoiseSumEnabled;
        // Unity's gradient potentials use the fixed Perlin permutation. Only the system offset
        // and curve interpolation use private seeded generators; script-global Random is untouched.
        static readonly byte[] s_NoisePermutation =
        {
            151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
            140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
            247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
            57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
            74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
            60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
            65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
            200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
            52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
            207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
            119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
            129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
            218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
            81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
            184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
            222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180,
        };
        static readonly Vector3[] s_NoiseGradients2 =
        {
            new Vector3(1.0f, 0.0f, 0f),
            new Vector3(-1.0f, 0.0f, 0f),
            new Vector3(0.0f, 1.0f, 0f),
            new Vector3(0.0f, -1.0f, 0f),
            new Vector3(0.7071067690849304f, 0.7071067690849304f, 0f),
            new Vector3(-0.7071067690849304f, 0.7071067690849304f, 0f),
            new Vector3(0.7071067690849304f, -0.7071067690849304f, 0f),
            new Vector3(-0.7071067690849304f, -0.7071067690849304f, 0f),
        };
        static readonly Vector3[] s_NoiseGradients3 =
        {
            new Vector3(1.0f, 1.0f, 0.0f),
            new Vector3(-1.0f, 1.0f, 0.0f),
            new Vector3(1.0f, -1.0f, 0.0f),
            new Vector3(-1.0f, -1.0f, 0.0f),
            new Vector3(1.0f, 0.0f, 1.0f),
            new Vector3(-1.0f, 0.0f, 1.0f),
            new Vector3(1.0f, 0.0f, -1.0f),
            new Vector3(-1.0f, 0.0f, -1.0f),
            new Vector3(0.0f, 1.0f, 1.0f),
            new Vector3(0.0f, -1.0f, 1.0f),
            new Vector3(0.0f, 1.0f, -1.0f),
            new Vector3(0.0f, -1.0f, -1.0f),
            new Vector3(1.0f, 1.0f, 0.0f),
            new Vector3(-1.0f, 1.0f, 0.0f),
            new Vector3(0.0f, -1.0f, 1.0f),
            new Vector3(0.0f, -1.0f, -1.0f),
        };
        static int NoiseHash(int x) => s_NoisePermutation[x & 255];
        static float NoiseRandom(uint seed, uint salt)
        {
            var rng = Random.State.FromSeed(unchecked((int)(seed + salt)));
            return rng.NextValue();
        }

        Vector3 NoiseVelocity(ref Particle p, float age, float scroll, float dt)
        {
            var n = m_Noise;
            float frequency = Mathf.Max(n.frequency, .000001f);
            var rng = Random.State.FromSeed(unchecked((int)m_Main.randomSeed));
            var offset = new Vector3(rng.NextValue() * 100f, rng.NextValue() * 100f, rng.NextValue() * 100f);
            var q = p.m_Position + offset;
            float u = q.x + 100f;
            Vector3 field;
            if (n.quality == ParticleSystemNoiseQuality.Low)
                field = new Vector3(NoiseDerivative(new Vector3(q.y + scroll, 0, 0), frequency, 1).x,
                    NoiseDerivative(new Vector3(q.z + scroll, 0, 0), frequency, 1).x,
                    NoiseDerivative(new Vector3(u + scroll, 0, 0), frequency, 1).x);
            else
            {
                bool high = n.quality == ParticleSystemNoiseQuality.High;
                int dimension = high ? 3 : 2;
                var a = NoiseDerivative(high ? new Vector3(q.z, q.y, q.x + scroll) : new Vector3(q.z, q.y + scroll, 0), frequency, dimension);
                var b = NoiseDerivative(high ? new Vector3(u, q.z, q.y + scroll) : new Vector3(u, q.z + scroll, 0), frequency, dimension);
                var c = NoiseDerivative(high ? new Vector3(q.y, u, q.z + scroll) : new Vector3(q.y, u + scroll, 0), frequency, dimension);
                field = new Vector3(c.x - b.y, a.x - c.y, b.x - a.y);
            }
            if (n.remapEnabled)
            {
                field.x = 2f * frequency * n.remapX.Evaluate(Mathf.Clamp01(field.x / (4f * frequency) + .5f), 1f);
                field.y = 2f * frequency * (n.separateAxes ? n.remapY : n.remapX).Evaluate(Mathf.Clamp01(field.y / (4f * frequency) + .5f), 1f);
                field.z = 2f * frequency * (n.separateAxes ? n.remapZ : n.remapX).Evaluate(Mathf.Clamp01(field.z / (4f * frequency) + .5f), 1f);
            }
            rng = Random.State.FromSeed(unchecked((int)(p.m_RandomSeed + 0x3edcba94u)));
            float sx = n.strengthX.Evaluate(age, rng.NextValue());
            var strength = n.separateAxes ? new Vector3(sx, n.strengthY.Evaluate(age, rng.NextValue()), n.strengthZ.Evaluate(age, rng.NextValue())) : Vector3.one * sx;
            field = Vector3.Scale(field, strength);
            if (n.damping) field /= frequency;
            p.m_NoiseImpulse = field;
            if (m_NoiseSumEnabled) p.m_NoiseSum += field * dt;
            var rotation = field * (n.rotationAmount.Evaluate(age, NoiseRandom(p.m_RandomSeed, 0xa92bedcbu)) * dt * .008726646192371845f);
            if (!has3DParticleRotations) rotation = new Vector3(0, 0, rotation.z);
            p.m_Rotation += rotation;
            return field * n.positionAmount.Evaluate(age, NoiseRandom(p.m_RandomSeed, 0xb77ce39au));
        }

        Vector3 NoiseSize(ref Particle p, Vector3 size)
        {
            float amount = .5f * m_Noise.sizeAmount.Evaluate(p.Age01, NoiseRandom(p.m_RandomSeed, 0x38683aafu));
            var n = p.m_NoiseImpulse;
            var factor = hasNonUniformParticleSizes ? new Vector3(Mathf.Max(0f, 1f + amount * n.x), Mathf.Max(0f, 1f + amount * n.y), Mathf.Max(0f, 1f + amount * n.z)) : Vector3.one * Mathf.Max(0f, 1f + amount * n.x);
            return Vector3.Scale(size, factor);
        }

        Vector3 NoiseDerivative(Vector3 q, float frequency, int dimensions)
        {
            Vector3 sum = Vector3.zero;
            float amplitude = 1f, norm = 0f;
            for (int octave = 0; octave < m_Noise.octaveCount; octave++)
            {
                sum += PerlinDerivative(q, frequency, dimensions) * amplitude;
                norm += amplitude;
                frequency *= m_Noise.octaveScale;
                amplitude *= m_Noise.octaveMultiplier;
            }
            return sum / norm;
        }

        // Differentiate the multilinear interpolation of gradient dot products analytically.
        // Coordinates are multiplied in single precision before the polynomial evaluation.
        static Vector3 PerlinDerivative(Vector3 q, float frequency, int dimensions)
        {
            if (dimensions == 1)
            {
                float coordinate = q.x * frequency;
                int cell = Mathf.FloorToInt(coordinate);
                float x = coordinate - cell;
                float g0 = (NoiseHash(cell) & 1) == 0 ? 1f : -1f;
                float g1 = (NoiseHash(cell + 1) & 1) == 0 ? 1f : -1f;
                float df = ((x - 2f) * x + 1f) * ((30f * x) * x);
                float f = ((6f * x - 15f) * x + 10f) * ((x * x) * x);
                float delta = (x - 1f) * g1 - x * g0;
                float d = (df * delta + g0) + (g1 - g0) * f;
                return new Vector3((frequency + frequency) * d, 0f, 0f);
            }
            q *= frequency;
            Span<int> lattice = stackalloc int[3];
            Span<double> t = stackalloc double[3], fade = stackalloc double[3], derivative = stackalloc double[3];
            for (int j = 0; j < dimensions; j++)
            {
                lattice[j] = (int)Math.Floor(q[j]);
                double v = t[j] = (double)q[j] - lattice[j];
                fade[j] = v * v * v * (v * (v * 6 - 15) + 10);
                derivative[j] = 30 * v * v * (v * (v - 2) + 1);
            }
            Span<double> result = stackalloc double[3]; result.Clear();
            Span<double> weight = stackalloc double[3];
            for (int corner = 0; corner < (1 << dimensions); corner++)
            {
                int hash = NoiseHash(lattice[0] + (corner & 1));
                for (int j = 1; j < dimensions; j++) hash = NoiseHash(hash + lattice[j] + ((corner >> j) & 1));
                Vector3 gradient = dimensions == 1 ? new Vector3((hash & 1) == 0 ? 1 : -1, 0, 0) : dimensions == 2 ? s_NoiseGradients2[hash & 7] : s_NoiseGradients3[hash & 15];
                double dot = 0, product = 1;
                for (int j = 0; j < dimensions; j++)
                {
                    int bit = (corner >> j) & 1;
                    dot += gradient[j] * (t[j] - bit);
                    weight[j] = bit != 0 ? fade[j] : 1 - fade[j];
                    product *= weight[j];
                }
                for (int j = 0; j < dimensions; j++)
                {
                    double other = 1;
                    for (int k = 0; k < dimensions; k++) if (k != j) other *= weight[k];
                    result[j] += gradient[j] * product + dot * (((corner >> j) & 1) != 0 ? derivative[j] : -derivative[j]) * other;
                }
            }
            double scale = frequency * (dimensions == 1 ? 2.0 : dimensions == 2 ? 1.4142135381698608 : 1.0);
            return new Vector3((float)(result[0] * scale), (float)(result[1] * scale), (float)(result[2] * scale));
        }
    }
}
