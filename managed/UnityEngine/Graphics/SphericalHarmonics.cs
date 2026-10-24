using System;

namespace UnityEngine.Rendering
{
    /// <summary>
    /// Second order spherical harmonics, per color channel nine coefficients stored so that a
    /// direction n = (x, y, z) evaluates to c0 + c1 y + c2 z + c3 x + c4 xy + c5 yz + c6 (3z² − 1)
    /// + c7 xz + c8 (x² − y²) (the Editor's storage, measured: tests/projects/urp CapsProbe).
    /// </summary>
    [Serializable]
    public partial struct SphericalHarmonicsL2 : IEquatable<SphericalHarmonicsL2>
    {
        float shr0, shr1, shr2, shr3, shr4, shr5, shr6, shr7, shr8;
        float shg0, shg1, shg2, shg3, shg4, shg5, shg6, shg7, shg8;
        float shb0, shb1, shb2, shb3, shb4, shb5, shb6, shb7, shb8;

        public unsafe float this[int rgb, int coefficient]
        {
            get
            {
                int index = rgb * 9 + coefficient;
                if (rgb < 0 || rgb > 2 || coefficient < 0 || coefficient > 8) throw new IndexOutOfRangeException("Invalid index!");
                fixed (float* p = &shr0) return p[index];
            }
            set
            {
                int index = rgb * 9 + coefficient;
                if (rgb < 0 || rgb > 2 || coefficient < 0 || coefficient > 8) throw new IndexOutOfRangeException("Invalid index!");
                fixed (float* p = &shr0) p[index] = value;
            }
        }

        public void Clear() => this = default;

        public void AddAmbientLight(Color color)
        {
            shr0 += color.r;
            shg0 += color.g;
            shb0 += color.b;
        }

        /// <summary>A directional light's irradiance (the clamped cosine's bands π, 2π/3, π/4),
        /// normalized by 16/17 as Unity's are; the color counts twice its intensity.</summary>
        public void AddDirectionalLight(Vector3 direction, Color color, float intensity)
        {
            var c = color * (2f * intensity);
            const float k = 16f / 17f;
            float x = direction.x, y = direction.y, z = direction.z;
            float[] basis =
            {
                k / 4f, k / 2f * y, k / 2f * z, k / 2f * x,
                k * 15f / 16f * x * y, k * 15f / 16f * y * z, k * 5f / 64f * (3f * z * z - 1f), k * 15f / 16f * x * z,
                k * 15f / 64f * (x * x - y * y),
            };
            for (int i = 0; i < 9; i++)
            {
                this[0, i] += c.r * basis[i];
                this[1, i] += c.g * basis[i];
                this[2, i] += c.b * basis[i];
            }
        }

        public void Evaluate(Vector3[] directions, Color[] results)
        {
            if (directions == null) throw new ArgumentNullException(nameof(directions));
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (directions.Length == 0) return;
            if (directions.Length != results.Length) throw new ArgumentException("Length of the directions array and the results array must match.");
            for (int d = 0; d < directions.Length; d++)
            {
                var n = directions[d];
                float[] basis = { 1f, n.y, n.z, n.x, n.x * n.y, n.y * n.z, 3f * n.z * n.z - 1f, n.x * n.z, n.x * n.x - n.y * n.y };
                float r = 0, g = 0, b = 0;
                for (int i = 0; i < 9; i++)
                {
                    r += this[0, i] * basis[i];
                    g += this[1, i] * basis[i];
                    b += this[2, i] * basis[i];
                }
                results[d] = new Color(r, g, b, 1f);
            }
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) hash.Add(this[c, i]);
            return hash.ToHashCode();
        }

        public override bool Equals(object other) => other is SphericalHarmonicsL2 sh && Equals(sh);

        public bool Equals(SphericalHarmonicsL2 other)
        {
            for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) if (this[c, i] != other[c, i]) return false;
            return true;
        }

        public static SphericalHarmonicsL2 operator *(SphericalHarmonicsL2 lhs, float rhs)
        {
            var r = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) r[c, i] = lhs[c, i] * rhs;
            return r;
        }

        public static SphericalHarmonicsL2 operator *(float lhs, SphericalHarmonicsL2 rhs) => rhs * lhs;

        public static SphericalHarmonicsL2 operator +(SphericalHarmonicsL2 lhs, SphericalHarmonicsL2 rhs)
        {
            var r = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) r[c, i] = lhs[c, i] + rhs[c, i];
            return r;
        }

        public static bool operator ==(SphericalHarmonicsL2 lhs, SphericalHarmonicsL2 rhs) => lhs.Equals(rhs);
        public static bool operator !=(SphericalHarmonicsL2 lhs, SphericalHarmonicsL2 rhs) => !lhs.Equals(rhs);

        /// <summary>The shader constants of ShadeSH9 (unity_SHAr..unity_SHC): SHA·(n, 1) +
        /// SHB·(xy, yz, z², xz) + SHC·(x² − y²).</summary>
        internal void ShaderConstants(out Vector4 shAr, out Vector4 shAg, out Vector4 shAb, out Vector4 shBr, out Vector4 shBg, out Vector4 shBb, out Vector4 shC)
        {
            var sh = this;
            Vector4 A(int c) => new Vector4(sh[c, 3], sh[c, 1], sh[c, 2], sh[c, 0] - sh[c, 6]);
            Vector4 B(int c) => new Vector4(sh[c, 4], sh[c, 5], 3f * sh[c, 6], sh[c, 7]);
            shAr = A(0); shAg = A(1); shAb = A(2);
            shBr = B(0); shBg = B(1); shBb = B(2);
            shC = new Vector4(sh[0, 8], sh[1, 8], sh[2, 8], 1f);
        }
    }
}
