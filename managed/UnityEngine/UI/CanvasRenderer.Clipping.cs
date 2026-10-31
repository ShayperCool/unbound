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
            m_CanvasProperties[s_GammaProperty] = alwaysGamma ? 1 : 0;
            var rect = m_ClipRect;
            m_CanvasProperties[s_ClipRectProperty] = m_HasRectClipping
                ? new Vector4(rect.xMin, rect.yMin, rect.xMax, rect.yMax)
                : new Vector4(float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity);
            m_CanvasProperties[s_SoftnessXProperty] = m_HasRectClipping ? m_ClippingSoftness.x : 0f;
            m_CanvasProperties[s_SoftnessYProperty] = m_HasRectClipping ? m_ClippingSoftness.y : 0f;
            return m_CanvasProperties;
        }

        // A private draw material overrides the local keyword without changing the assigned
        // material, its users outside Canvas, or Material.Get*/shaderKeywords readback.
        internal Material CanvasMaterial(Material source)
        {
            source.Inherit();
            if (!m_ClipMaterials.TryGetValue(source, out var prepared))
            {
                using (ObjectRegistry.AsAssets())
                    prepared = new ClipMaterial { Material = new Material(source) { hideFlags = HideFlags.HideAndDontSave } };
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
