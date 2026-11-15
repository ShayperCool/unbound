using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;

namespace Unbound.UIElements
{
    // Atlas copies include one texel beyond each source edge. The source sampler supplies the
    // clamp/repeat/mirror border, including the four corners. Resources survive subsequent copies.
    internal static class UaAtlasBlitter
    {
        static Mesh s_Mesh;
        static Material s_Material;
        static readonly Vector3[] s_Positions = new Vector3[4];
        static readonly Vector4[] s_Uvs = new Vector4[4];
        static readonly Color32[] s_Colors = { Color.white, Color.white, Color.white, Color.white };
        static readonly int[] s_Indices = { 0, 1, 2, 0, 2, 3 };
        static readonly Dictionary<int, object> s_Properties = new Dictionary<int, object>();
        static readonly int s_MainTex = Shader.PropertyToID("_MainTex");

        internal static void Copy(Texture source, RenderTexture destination, RectInt rect, bool border)
        {
            if (s_Material == null)
            {
                var shader = Shader.Find("Hidden/BlitCopy");
                if (shader == null) throw new System.InvalidOperationException("UI Toolkit dynamic atlas copy shader is unavailable.");
                using (ObjectRegistry.AsAssets())
                {
                    s_Material = new Material(shader) { name = "UI Toolkit atlas copy" };
                    s_Mesh = new Mesh { name = "UI Toolkit atlas copy quad" };
                }
                s_Material.IsPersistentAsset = true;
                s_Mesh.MarkDynamic();
                s_Mesh.AdoptVertices(s_Positions);
                s_Mesh.m_UVs[0] = s_Uvs; s_Mesh.m_UVDimensions[0] = 3;
                s_Mesh.m_Colors = s_Colors; s_Mesh.m_SubMeshes[0].Indices = s_Indices;
            }
            float padding = border ? UaAtlasAllocator.Border : 0;
            float x0 = rect.x - padding, x1 = rect.xMax + padding;
            float y0 = rect.y - padding, y1 = rect.yMax + padding;
            float u0 = -padding / source.width, u1 = 1f + padding / source.width;
            float v0 = -padding / source.height, v1 = 1f + padding / source.height;
            s_Positions[0] = new Vector3(x0, y0, 0); s_Uvs[0] = new Vector4(u0, v0, 0, 0);
            s_Positions[1] = new Vector3(x0, y1, 0); s_Uvs[1] = new Vector4(u0, v1, 0, 0);
            s_Positions[2] = new Vector3(x1, y1, 0); s_Uvs[2] = new Vector4(u1, v1, 0, 0);
            s_Positions[3] = new Vector3(x1, y0, 0); s_Uvs[3] = new Vector4(u1, v0, 0, 0);
            s_Mesh.AdoptVertices(s_Positions);
            s_Mesh.FinishStreamedWrite(4, 6, false);
            s_Properties[s_MainTex] = source;
            var saved = GfxStream.Current;
            try
            {
                GfxStream.Scissor(null);
                GfxStream.SetTarget(destination.EnsureCreated());
                GfxStream.Viewport(new Rect(0, 0, destination.width, destination.height));
                GfxStream.Camera(Matrix4x4.identity, Matrix4x4.Ortho(0, destination.width, 0, destination.height, -1, 1));
                GfxStream.Draw(s_Mesh.Sync(), 0, s_Material.Sync(), 0, Matrix4x4.identity, Color.white, properties: s_Properties);
            }
            finally { GfxStream.Restore(saved); }
        }
    }
}
