using UnityEngine.Experimental.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// Terrain.normalmapTexture as the Unity 6000.0.71f1 Editor makes it (Linux, OpenGL Core,
    /// NVIDIA: tests/projects/terrainnormalmap). An instanced terrain (drawInstanced) has it from
    /// the moment instancing is turned on, already filled; turning instancing off destroys it; a
    /// terrain drawn without instancing has none. It is a render texture named "TerrainNormalMap",
    /// one texel per heightmap sample, A2B10G10R10_UNormPack32 (not sRGB), with mip levels made
    /// from the first, bilinear, clamped, hidden and not saved (HideFlags 125). Each texel holds the
    /// heightmap's normal at that sample: the Sobel gradient of the heights (weights 1, 2, 1 across,
    /// edges clamped) in world units (the terrain's height over its cell size on each axis), the
    /// normal (-dx, 1, -dz) normalized, stored as n * 0.5 + 0.5 in 8 bits (R x, G up, B z, alpha
    /// one). Height changes reach it when the terrain is next rendered, not when they are made.
    /// The Editor draws it on its GPU: one value in about seven is one 8-bit step from this rule
    /// (the probe compares within that step). The other devices and releases are unmeasured.
    /// </summary>
    public sealed partial class Terrain
    {
        RenderTexture m_Normalmap;
        Texture2D m_NormalmapSource;
        TerrainData m_NormalmapData;
        internal int m_NormalmapVersion;

        /// <summary>The normal map of an instanced terrain (null otherwise; see the class).</summary>
        public RenderTexture normalmapTexture
        {
            get
            {
                if (!m_DrawInstanced) return null;
                if (m_Normalmap == null) MakeNormalmap();
                return m_Normalmap;
            }
        }

        void SetDrawInstanced(bool value)
        {
            if (m_DrawInstanced == value) return;
            m_DrawInstanced = value;
            if (value) MakeNormalmap();
            else ReleaseNormalmap();
        }

        /// <summary>Before the terrain is rendered: the heights changed since the normal map was
        /// made reach it.</summary>
        internal void UpdateNormalmap()
        {
            if (!m_DrawInstanced || m_TerrainData == null) return;
            if (m_Normalmap == null || !ReferenceEquals(m_NormalmapData, m_TerrainData)
                || m_Normalmap.width != m_TerrainData.heightmapResolution)
                MakeNormalmap();
            else if (m_NormalmapVersion != m_TerrainData.m_HeightsVersion)
                FillNormalmap();
        }

        void MakeNormalmap()
        {
            var data = m_TerrainData;
            if (data == null) return;
            int resolution = data.heightmapResolution;
            if (m_Normalmap == null || m_Normalmap.width != resolution)
            {
                ReleaseNormalmap();
                m_Normalmap = new RenderTexture(resolution, resolution, 0, GraphicsFormat.A2B10G10R10_UNormPack32)
                {
                    name = "TerrainNormalMap",
                    useMipMap = true,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 1,
                    hideFlags = (HideFlags)125,
                };
                m_Normalmap.Create();
                // The 8-bit normals, widened to the render texture's 10 bits by the copy.
                m_NormalmapSource = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            m_NormalmapData = data;
            FillNormalmap();
        }

        void FillNormalmap()
        {
            var data = m_TerrainData;
            int resolution = m_Normalmap.width;
            m_NormalmapSource.SetPixelData(NormalmapTexels(data, resolution), 0);
            m_NormalmapSource.Apply(false);
            Graphics.Blit(m_NormalmapSource, m_Normalmap);
            m_Normalmap.GenerateMips();
            m_NormalmapVersion = data.m_HeightsVersion;
        }

        void ReleaseNormalmap()
        {
            if (m_Normalmap != null) Object.DestroyImmediate(m_Normalmap);
            if (m_NormalmapSource != null) Object.DestroyImmediate(m_NormalmapSource);
            m_Normalmap = null;
            m_NormalmapSource = null;
            m_NormalmapData = null;
        }

        /// <summary>The normal map's texels in 8 bits (RGBA) for the heights of
        /// <paramref name="data"/> (see the class).</summary>
        internal static Color32[] NormalmapTexels(TerrainData data, int resolution)
        {
            var texels = new Color32[resolution * resolution];
            var size = data.size;
            float cells = Mathf.Max(1, resolution - 1);
            float scaleX = size.y / (size.x / cells), scaleZ = size.y / (size.z / cells);
            float H(int x, int y) => data.RawHeight(x, y) / (float)TerrainData.MaxHeight;
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dx = (H(x + 1, y - 1) + 2 * H(x + 1, y) + H(x + 1, y + 1) - H(x - 1, y - 1) - 2 * H(x - 1, y) - H(x - 1, y + 1)) / 8;
                    float dz = (H(x - 1, y + 1) + 2 * H(x, y + 1) + H(x + 1, y + 1) - H(x - 1, y - 1) - 2 * H(x, y - 1) - H(x + 1, y - 1)) / 8;
                    var n = new Vector3(-dx * scaleX, 1, -dz * scaleZ).normalized;
                    texels[y * resolution + x] = new Color32(Channel(n.x), Channel(n.y), Channel(n.z), 255);
                }
            }
            return texels;
        }

        /// <summary>A normal component as the Editor stores it: 8 bits of n * 0.5 + 0.5.</summary>
        static byte Channel(float component) => (byte)(Mathf.Clamp01(component * 0.5f + 0.5f) * 255 + 0.5f);
    }
}
