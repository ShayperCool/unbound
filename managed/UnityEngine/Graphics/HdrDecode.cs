using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>
    /// How shaders decode the HDR radiance a texture stores as bytes, by its usage mode: the
    /// decode values (<c>unity_Lightmap_HDR</c>, <c>unity_SpecCube0_HDR</c>, a reflection probe's
    /// <c>textureHDRDecodeValues</c>). Double LDR (lightmap usage 1, usage 7) is twice the stored
    /// value, RGBM (lightmap usage 2, usage 5) five times it scaled by its alpha; an sRGB texture
    /// in a linear project raises the factor (and RGBM's exponent) to 2.2. Anything else decodes
    /// as stored. Matches the Unity 6000.0.71f1 Editor (tests/projects/lightmapencoding).
    /// </summary>
    internal static class HdrDecode
    {
        internal const int LightmapDoubleLdr = 1, LightmapRgbm = 2, Rgbm = 5, DoubleLdr = 7;

        // Boxed once: draws bind them without allocating.
        static readonly object s_AsStored = new Vector4(1f, 1f, 0f, 0f);
        static readonly object s_DoubleLdrGamma = new Vector4(2f, 1f, 0f, 0f);
        static readonly object s_DoubleLdrLinear = new Vector4(Mathf.Pow(2f, 2.2f), 1f, 0f, 0f);
        static readonly object s_RgbmGamma = new Vector4(5f, 1f, 0f, 1f);
        static readonly object s_RgbmLinear = new Vector4(Mathf.Pow(5f, 2.2f), 2.2f, 0f, 1f);

        /// <summary>The decode values of <paramref name="usage"/>, boxed; <paramref name="srgb"/>:
        /// the texture's data is sRGB.</summary>
        internal static object Boxed(int usage, bool srgb)
        {
            bool linear = srgb && QualitySettings.activeColorSpace == ColorSpace.Linear;
            return usage switch
            {
                LightmapDoubleLdr or DoubleLdr => linear ? s_DoubleLdrLinear : s_DoubleLdrGamma,
                LightmapRgbm or Rgbm => linear ? s_RgbmLinear : s_RgbmGamma,
                _ => s_AsStored,
            };
        }

        internal static Vector4 Of(int usage, bool srgb) => (Vector4)Boxed(usage, srgb);

        /// <summary>The decode values of a texture, boxed: an imported or serialized 2D texture's
        /// or cubemap's usage mode; any other texture decodes as stored.</summary>
        internal static object Of(Texture texture) => texture switch
        {
            Texture2D t => Boxed(t.m_UsageMode, !t.m_Linear),
            Cubemap c => Boxed(c.m_UsageMode, c.m_UsageSrgb),
            _ => s_AsStored,
        };

        /// <summary>Whether <paramref name="texture"/> stores encoded HDR radiance.</summary>
        internal static bool Encoded(Texture texture) => texture switch
        {
            Texture2D t => IsEncoding(t.m_UsageMode),
            Cubemap c => IsEncoding(c.m_UsageMode),
            _ => false,
        };

        static bool IsEncoding(int usage) => usage is LightmapDoubleLdr or LightmapRgbm or Rgbm or DoubleLdr;

        // MaterialPropertyBlock setters may run on any thread: the caches are locked.
        static readonly object s_Lock = new();
        static readonly Dictionary<string, string> s_Names = new();
        static readonly Dictionary<int, int> s_Ids = new();
        static readonly Dictionary<int, int> s_Owners = new();

        /// <summary>The property ID of a texture property's <c>&lt;name&gt;_HDR</c> (made once per
        /// property), remembered with its texture property (<see cref="TextureOf"/>).</summary>
        internal static int IdOf(int texture)
        {
            lock (s_Lock)
            {
                if (!s_Ids.TryGetValue(texture, out int id))
                {
                    s_Ids[texture] = id = Shader.PropertyToID(NameOf(Shader.IdToName(texture) ?? ""));
                    s_Owners[id] = texture;
                }
                return id;
            }
        }

        /// <summary>The texture property whose <c>&lt;name&gt;_HDR</c> <paramref name="id"/> is,
        /// among those <see cref="IdOf"/> has named.</summary>
        internal static bool TextureOf(int id, out int texture)
        {
            lock (s_Lock) return s_Owners.TryGetValue(id, out texture);
        }

        /// <summary><c>&lt;name&gt;_HDR</c>, the decode values Unity gives a texture property
        /// alongside it (made once per property name).</summary>
        internal static string NameOf(string texture)
        {
            lock (s_Lock)
            {
                if (!s_Names.TryGetValue(texture, out var name)) s_Names[texture] = name = texture + "_HDR";
                return name;
            }
        }
    }
}
