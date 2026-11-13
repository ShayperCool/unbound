using System;
using System.Globalization;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    // The base map. In perspective views a patch whose bounds lie basemapDistance or farther from
    // the camera is drawn with the material template's BaseMapShader instead of the splat passes.
    // That material reads textures the template's BaseMapGenShader makes once: one render texture
    // for each of its passes, named by the pass's "Name" tag. The "_NormalMap" pass is skipped, as
    // Unity's terrain shaders state. The "Format" and "Size" tags (a fraction of
    // TerrainData.baseMapResolution) give the texture's format and size. Each pass is drawn once
    // per four layers, with the splat pass's properties: the first group replaces, the others add
    // through _DstBlend. The mip maps are then made. Orthographic views never use the base map.
    // Matches the Unity 6000.0.71f1 Editor, tests/projects/terrainlod.
    internal sealed partial class TerrainRenderer
    {
        /// <summary>The hide flags the Editor's base maps report: HideAndDontSave and one more bit
        /// (tests/projects/terrainlod).</summary>
        const HideFlags BaseMapHideFlags = (HideFlags)125;
        const string SkippedBaseMap = "_NormalMap";
        static readonly ShaderTagId s_NameTag = new ShaderTagId("Name"), s_FormatTag = new ShaderTagId("Format"), s_SizeTag = new ShaderTagId("Size");
        static readonly int s_DstBlend = Shader.PropertyToID("_DstBlend"), s_MainTexId = Shader.PropertyToID("_MainTex");

        /// <summary>The base map material, null when the template names no BaseMapShader or
        /// BaseMapGenShader.</summary>
        Material m_BaseMaterial;
        /// <summary>The generation material per four layers.</summary>
        Material[] m_BaseGen = Array.Empty<Material>();
        /// <summary>Per generation pass, its render texture (null for a skipped pass), the property
        /// it is bound to (null: the pass makes none) and its size over the base map resolution.</summary>
        RenderTexture[] m_BaseMaps = Array.Empty<RenderTexture>();
        string[] m_BaseMapNames = Array.Empty<string>();
        float[] m_BaseMapSizes = Array.Empty<float>();
        GraphicsFormat[] m_BaseMapFormats = Array.Empty<GraphicsFormat>();
        Shader m_BaseMapShader;
        /// <summary>What the maps were made from: the template, the data and its layers' version.</summary>
        (Material, TerrainData, int) m_BaseMapKey;
        bool m_BaseMapsDirty;
        /// <summary>The splat passes, then the base map material: patches draw a range of them.</summary>
        Material[] m_DrawMaterials = Array.Empty<Material>();

        /// <summary>Makes the base map materials for the splat passes just built.</summary>
        void UpdateBaseMaterials(Material template, TerrainData data)
        {
            DestroyBaseMaterials();
            var shader = template.shader;
            var baseShader = shader != null ? shader.GetDependency("BaseMapShader") : null;
            var genShader = shader != null ? shader.GetDependency("BaseMapGenShader") : null;
            if (baseShader == null || genShader == null)
            {
                ReleaseBaseMaps();
                m_DrawMaterials = m_Passes;
                return;
            }
            m_BaseGen = new Material[m_Passes.Length];
            for (int p = 0; p < m_Passes.Length; p++)
            {
                var gen = new Material(genShader) { name = genShader.name + " (Terrain)", hideFlags = HideFlags.HideAndDontSave };
                gen.CopyPropertiesFromMaterial(m_Passes[p]);
                gen.SetFloat(s_DstBlend, p == 0 ? (float)BlendMode.Zero : (float)BlendMode.One);
                m_BaseGen[p] = gen;
            }
            m_BaseMaterial = new Material(baseShader) { name = baseShader.name + " (Terrain)", hideFlags = HideFlags.HideAndDontSave };
            m_BaseMaterial.CopyPropertiesFromMaterial(template);
            SetHoles(m_BaseMaterial, data);
            m_DrawMaterials = new Material[m_Passes.Length + 1];
            Array.Copy(m_Passes, m_DrawMaterials, m_Passes.Length);
            m_DrawMaterials[m_Passes.Length] = m_BaseMaterial;
            ReadBaseMapPasses(genShader);
            // Heights alone leave the maps as they are; new materials still take them.
            var key = (template, data, data.m_SplatVersion);
            if (m_BaseMapKey != key) m_BaseMapsDirty = true;
            m_BaseMapKey = key;
            BindBaseMaps();
        }

        /// <summary>Reads the generation passes' tags: which make a base map, bound to what, of
        /// which size; the maps of another shader go.</summary>
        void ReadBaseMapPasses(Shader shader)
        {
            int passes = shader.passCount;
            if (m_BaseMaps.Length == passes && m_BaseMapShader == shader) return;
            ReleaseBaseMaps();
            m_BaseMapShader = shader;
            m_BaseMaps = new RenderTexture[passes];
            m_BaseMapNames = new string[passes];
            m_BaseMapSizes = new float[passes];
            m_BaseMapFormats = new GraphicsFormat[passes];
            for (int i = 0; i < passes; i++)
            {
                string name = shader.FindPassTagValue(i, s_NameTag).name;
                string formatTag = shader.FindPassTagValue(i, s_FormatTag).name;
                if (string.IsNullOrEmpty(name) || name == SkippedBaseMap) continue;
                var format = BaseMapFormat(formatTag);
                if (format == GraphicsFormat.None)
                {
                    Warn($"shader '{shader.name}' pass {i} asks for a base map of format '{formatTag}', which Unbound does not make: '{name}' stays unset.");
                    continue;
                }
                m_BaseMapNames[i] = name;
                m_BaseMapFormats[i] = format;
                m_BaseMapSizes[i] = SizeOf(shader.FindPassTagValue(i, s_SizeTag).name);
            }
        }

        /// <summary>Makes the base maps again when the layers changed, the resolution changed or a
        /// map is gone.</summary>
        void UpdateBaseMaps(TerrainData data)
        {
            if (m_BaseMaterial == null) return;
            int resolution = data.baseMapResolution;
            if (!m_BaseMapsDirty)
            {
                bool current = true;
                for (int i = 0; i < m_BaseMaps.Length && current; i++)
                    if (m_BaseMapNames[i] != null)
                        current = m_BaseMaps[i] is RenderTexture map && map.IsCreated() && map.width == BaseMapSize(i, resolution);
                if (current) return;
            }
            m_BaseMapsDirty = false;
            var saved = GfxStream.Current;
            for (int i = 0; i < m_BaseMaps.Length; i++)
            {
                var map = BaseMapFor(i, resolution);
                if (map == null) continue;
                ulong target = map.EnsureCreated();
                foreach (var gen in m_BaseGen) Immediate.BlitWith(target, gen, i, null);
                map.GenerateMips();
            }
            GfxStream.Restore(saved);
            BindBaseMaps();
        }

        /// <summary>Binds the base maps made so far to the base material, and the _MainTex one to
        /// the splat passes too.</summary>
        void BindBaseMaps()
        {
            for (int i = 0; i < m_BaseMaps.Length; i++)
            {
                var map = m_BaseMaps[i];
                if (map == null) continue;
                m_BaseMaterial.SetTexture(m_BaseMapNames[i], map);
                if (m_BaseMapNames[i] == "_MainTex")
                    foreach (var pass in m_Passes) pass.SetTexture(s_MainTexId, map);
            }
        }

        int BaseMapSize(int pass, int resolution) => Math.Max(1, (int)(m_BaseMapSizes[pass] * resolution));

        /// <summary>The render texture of a generation pass at the resolution, made or remade to
        /// its tags; null for a pass that makes none.</summary>
        RenderTexture BaseMapFor(int pass, int resolution)
        {
            string name = m_BaseMapNames[pass];
            if (name == null) return null;
            int size = BaseMapSize(pass, resolution);
            var map = m_BaseMaps[pass];
            if (map != null && map.width == size && map.IsCreated()) return map;
            if (map != null) DestroyImmediate(map);
            var desc = new RenderTextureDescriptor(size, size, m_BaseMapFormats[pass], 0) { useMipMap = true, autoGenerateMips = false };
            map = new RenderTexture(desc) { name = $"BaseMap-{m_BaseMapShader.name}-{name}", hideFlags = BaseMapHideFlags };
            m_BaseMaps[pass] = map;
            return map;
        }

        /// <summary>A "Format" tag's linear format: a render texture format's name (URP's "ARGB32")
        /// or a texture format's (the built-in shaders' "RGBA32"); None for another.</summary>
        internal static GraphicsFormat BaseMapFormat(string tag)
        {
            // Names only: Enum.TryParse would read a number as a format too.
            if (string.IsNullOrEmpty(tag) || !char.IsLetter(tag[0])) return GraphicsFormat.None;
            if (Enum.TryParse(tag, out RenderTextureFormat target) && Enum.IsDefined(target))
                return GraphicsFormatUtility.GetGraphicsFormat(target, RenderTextureReadWrite.Linear);
            if (Enum.TryParse(tag, out TextureFormat texture) && Enum.IsDefined(texture))
                return GraphicsFormatUtility.GetGraphicsFormat(texture, false);
            return GraphicsFormat.None;
        }

        /// <summary>A "Size" tag: a number or a fraction ("1/4"); 1 when absent or unreadable.</summary>
        internal static float SizeOf(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return 1f;
            int slash = tag.IndexOf('/');
            if (slash < 0) return float.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out float whole) ? whole : 1f;
            return float.TryParse(tag.AsSpan(0, slash), NumberStyles.Float, CultureInfo.InvariantCulture, out float num)
                && float.TryParse(tag.AsSpan(slash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float den) && den != 0f ? num / den : 1f;
        }

        void ReleaseBaseMaps()
        {
            foreach (var map in m_BaseMaps) if (map != null) DestroyImmediate(map);
            m_BaseMaps = Array.Empty<RenderTexture>();
            m_BaseMapNames = Array.Empty<string>();
            m_BaseMapSizes = Array.Empty<float>();
            m_BaseMapFormats = Array.Empty<GraphicsFormat>();
            m_BaseMapShader = null;
        }

        void DestroyBaseMaterials()
        {
            foreach (var gen in m_BaseGen) if (gen != null) DestroyImmediate(gen);
            m_BaseGen = Array.Empty<Material>();
            if (m_BaseMaterial != null) DestroyImmediate(m_BaseMaterial);
            m_BaseMaterial = null;
            m_DrawMaterials = Array.Empty<Material>();
        }
    }
}
