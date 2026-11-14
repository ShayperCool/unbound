using System;
using System.Text.Json;

namespace UnityEngine
{
    /// <summary>
    /// Reads the floats of the native lighting data and realtime GI replies
    /// (`crates/unbound-runtime/src/lighting.rs`, `realtime_gi.rs`):
    /// numbers, and a non-finite value as the string "NaN", "Infinity" or "-Infinity", since JSON has
    /// no literal for them. The Editor's bakes hold such values: the lightmap scale and offset of a
    /// renderer whose lightmap UVs have no area (infinite scale, NaN offset; tests/projects/lightingdatanan),
    /// the unused hull rays and matrix rows of light probe cells (NaN).
    /// </summary>
    internal static class LightingDataReply
    {
        internal static float Single(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.String) return value.GetSingle();
            switch (value.GetString())
            {
                case "NaN": return float.NaN;
                case "Infinity": return float.PositiveInfinity;
                case "-Infinity": return float.NegativeInfinity;
                default: throw new FormatException($"the lighting data holds {value.GetRawText()} where a float belongs");
            }
        }

        internal static float[] Singles(JsonElement array)
        {
            var values = new float[array.GetArrayLength()];
            int i = 0;
            foreach (var v in array.EnumerateArray()) values[i++] = Single(v);
            return values;
        }

        /// <summary>The first four floats of <paramref name="array"/> (missing ones 0).</summary>
        internal static Vector4 Vector(JsonElement array)
        {
            var v = Vector4.zero;
            int i = 0;
            foreach (var x in array.EnumerateArray()) if (i < 4) v[i++] = Single(x);
            return v;
        }
    }
}
