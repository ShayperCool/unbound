namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>
    /// Particle colours as bytes, the way the particle system computes them in the 6000.0.71f1
    /// Editor (tests/projects/particlecurves gradient/particles, particlemeshshape texture/*): a
    /// colour becomes bytes rounded half up (not to even, as the Color32 conversion of scripts
    /// does); the shape module's tints multiply bytes with rounding, colour over lifetime and by
    /// speed modulate the colour by (factor + 1) / 256, truncated; two gradients blend as bytes
    /// with the random factor in 1/256 steps of its byte.
    /// </summary>
    internal static class ParticleColors
    {
        internal static byte ToByte(float value) => (byte)(Mathf.Clamp01(value) * 255f + 0.5f);

        internal static Color32 ToBytes(Color c) => new Color32(ToByte(c.r), ToByte(c.g), ToByte(c.b), ToByte(c.a));

        internal static byte Multiply(byte a, byte b) => (byte)((a * b + 127) / 255);

        internal static Color32 Multiply(Color32 a, Color32 b) =>
            new Color32(Multiply(a.r, b.r), Multiply(a.g, b.g), Multiply(a.b, b.b), Multiply(a.a, b.a));

        /// <summary><paramref name="c"/> modulated by <paramref name="factor"/>: c · (factor + 1) / 256,
        /// truncated (white keeps the colour).</summary>
        internal static Color32 Modulate(Color32 c, Color32 factor) =>
            new Color32(Modulate(c.r, factor.r), Modulate(c.g, factor.g), Modulate(c.b, factor.b), Modulate(c.a, factor.a));

        static byte Modulate(byte c, byte factor) => (byte)((c * (factor + 1)) >> 8);

        /// <summary>Between two byte colours by <paramref name="t"/> in [0, 1], taken as the
        /// truncated byte s = t·255 and blended as (min·(256 − s) + max·s + 128) / 256.</summary>
        internal static Color32 Lerp(Color32 min, Color32 max, float t)
        {
            int s = (int)(Mathf.Clamp01(t) * 255f);
            return new Color32(Blend(min.r, max.r, s), Blend(min.g, max.g, s), Blend(min.b, max.b, s), Blend(min.a, max.a, s));

            static byte Blend(byte a, byte b, int s) => (byte)((a * (256 - s) + b * s + 128) >> 8);
        }
    }
}
