using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class CanvasRenderer
    {
        const string ClipKeyword = "UNITY_UI_CLIP_RECT";
        static readonly int s_ClipRectProperty = Shader.PropertyToID("_ClipRect");
        static readonly int s_SoftnessXProperty = Shader.PropertyToID("_UIMaskSoftnessX");
        static readonly int s_SoftnessYProperty = Shader.PropertyToID("_UIMaskSoftnessY");
        readonly Dictionary<int, object> m_CanvasProperties = new Dictionary<int, object>();
        readonly Dictionary<Material, ClipMaterial> m_ClipMaterials = new Dictionary<Material, ClipMaterial>();

        sealed class ClipMaterial
        {
            internal Material Material;
            internal int Version = -1;
        }

        // Renderer data wins over material properties, including when clipping is disabled.
        internal Dictionary<int, object> CanvasProperties(bool alwaysGamma)
        {
            var rect = m_ClipRect;
            var clip = m_HasRectClipping
                ? new Vector4(rect.xMin, rect.yMin, rect.xMax, rect.yMax)
                : new Vector4(float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var softness = m_HasRectClipping ? m_ClippingSoftness : Vector2.zero;
            // Boxed anew only when a value changed (the draws read them every frame).
            if (m_CanvasPropertiesSet && m_CanvasGamma == alwaysGamma && FloatBits.Same(m_CanvasClip, clip) && FloatBits.Same(m_CanvasSoftness, softness))
                return m_CanvasProperties;
            m_CanvasPropertiesSet = true;
            (m_CanvasGamma, m_CanvasClip, m_CanvasSoftness) = (alwaysGamma, clip, softness);
            m_CanvasProperties[s_GammaProperty] = alwaysGamma ? 1 : 0;
            m_CanvasProperties[s_ClipRectProperty] = clip;
            m_CanvasProperties[s_SoftnessXProperty] = softness.x;
            m_CanvasProperties[s_SoftnessYProperty] = softness.y;
            return m_CanvasProperties;
        }

        // The values m_CanvasProperties hold.
        bool m_CanvasPropertiesSet, m_CanvasGamma;
        Vector4 m_CanvasClip;
        Vector2 m_CanvasSoftness;

        // A private draw material overrides the local keyword without changing the assigned
        // material, its users outside Canvas, or Material.Get*/shaderKeywords readback.
        internal Material CanvasMaterial(Material source)
        {
            source.Inherit();
            if (!m_ClipMaterials.TryGetValue(source, out var prepared))
            {
                using (ObjectRegistry.AsAssets())
                    prepared = new ClipMaterial { Material = new Material(source) { hideFlags = HideFlags.HideAndDontSave, parent = null } };
                m_ClipMaterials.Add(source, prepared);
            }
            if (prepared.Version != source.m_Version)
            {
                prepared.Material.shader = source.shader;
                prepared.Material.CopyPropertiesFromMaterial(source);
                prepared.Version = source.m_Version;
            }
            if (m_HasRectClipping) prepared.Material.EnableKeyword(ClipKeyword);
            else prepared.Material.DisableKeyword(ClipKeyword);
            return prepared.Material;
        }

        void ReleaseClipMaterials()
        {
            foreach (var prepared in m_ClipMaterials.Values) DestroyImmediate(prepared.Material, true);
            m_ClipMaterials.Clear();
        }
    }
}
