using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class CanvasRenderer
    {
        Mesh m_PreparedMesh;
        int m_MeshVersion, m_PreparedVersion = -1;
        Color m_PreparedTint;
        bool m_PreparedLinear;
        static readonly int s_GammaProperty = Shader.PropertyToID("_UIVertexColorAlwaysGammaSpace");
        static readonly Dictionary<int, object> s_GammaOff = new Dictionary<int, object> { [s_GammaProperty] = 0 };
        static readonly Dictionary<int, object> s_GammaOn = new Dictionary<int, object> { [s_GammaProperty] = 1 };

        internal static Dictionary<int, object> CanvasColorProperties(bool alwaysGamma) => alwaysGamma ? s_GammaOn : s_GammaOff;

        // Unity batches the renderer/group tint into byte vertex colors. Keeping the source
        // geometry intact lets flag, tint and alpha changes rebuild without repeated conversion.
        internal Mesh PrepareCanvasMesh(Color tint, bool convertToLinear)
        {
            bool geometryChanged = m_PreparedMesh == null || m_PreparedVersion != m_MeshVersion;
            if (!geometryChanged && m_PreparedTint.Equals(tint) && m_PreparedLinear == convertToLinear)
                return m_PreparedMesh;
            if (m_PreparedMesh == null)
                using (ObjectRegistry.AsAssets())
                    m_PreparedMesh = new Mesh { name = "CanvasRenderer prepared colors", hideFlags = HideFlags.HideAndDontSave };
            if (geometryChanged) m_PreparedMesh.CopyGeometryFrom(m_Mesh);
            m_PreparedVersion = m_MeshVersion;
            m_PreparedTint = tint;
            m_PreparedLinear = convertToLinear;
            if (convertToLinear) tint = tint.linear;
            int count = m_Mesh.m_Vertices.Length;
            var source = m_Mesh.m_Colors;
            var colors = m_PreparedMesh.m_Colors;
            if (colors == null || colors.Length != count) colors = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                Color color = source != null && i < source.Length ? (Color)source[i] : Color.white;
                if (convertToLinear) color = color.linear;
                colors[i] = QuantizeCanvasColor(color * tint);
            }
            m_PreparedMesh.m_Colors = colors;
            m_PreparedMesh.m_Dirty = true;
            return m_PreparedMesh;
        }

        static Color32 QuantizeCanvasColor(Color color) => new Color32(
            (byte)(Mathf.Clamp01(color.r) * 255f + .5f), (byte)(Mathf.Clamp01(color.g) * 255f + .5f),
            (byte)(Mathf.Clamp01(color.b) * 255f + .5f), (byte)(Mathf.Clamp01(color.a) * 255f + .5f));
    }
}
