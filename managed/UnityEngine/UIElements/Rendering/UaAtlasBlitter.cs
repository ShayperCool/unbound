using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using UnityEngine;

namespace Unbound.UIElements
{
    // Atlas copies include one texel beyond each source edge. The source sampler supplies the
    // clamp/repeat/mirror border, including the four corners. The quad is uploaded only once.
    internal static class UaAtlasBlitter
    {
        static Material s_Material;
        static readonly Dictionary<int, object> s_Properties = new Dictionary<int, object>();
        static readonly int s_MainTex = Shader.PropertyToID("_MainTex");
        static readonly int s_MainTexST = Shader.PropertyToID("_MainTex_ST");
        // The command stream reads this box before Copy returns. Reuse it when UVs change.
        static readonly object s_UvTransform = new Vector4(1, 1, 0, 0);

        internal static void Copy(Texture source, RenderTexture destination, RectInt rect, bool border)
        {
            if (s_Material == null)
            {
                var shader = Shader.Find("Hidden/BlitCopy");
                if (shader == null) throw new System.InvalidOperationException("UI Toolkit dynamic atlas copy shader is unavailable.");
                using (ObjectRegistry.AsAssets()) s_Material = new Material(shader) { name = "UI Toolkit atlas copy" };
                s_Material.IsPersistentAsset = true;
                s_Properties[s_MainTexST] = s_UvTransform;
            }
            float padding = border ? UaAtlasAllocator.Border : 0;
            float x0 = rect.x - padding, y0 = rect.y - padding;
            float u0 = -padding / source.width, v0 = -padding / source.height;
            Unsafe.Unbox<Vector4>(s_UvTransform) = new Vector4(1f - 2f * u0, 1f - 2f * v0, u0, v0);
            s_Properties[s_MainTex] = source;
            var model = Matrix4x4.identity;
            model.m00 = rect.width + 2f * padding; model.m11 = rect.height + 2f * padding;
            model.m03 = x0; model.m13 = y0;
            var saved = GfxStream.Current;
            try
            {
                GfxStream.Scissor(null);
                GfxStream.SetTarget(destination.EnsureCreated());
                GfxStream.Viewport(new Rect(0, 0, destination.width, destination.height));
                GfxStream.Camera(Matrix4x4.identity, Matrix4x4.Ortho(0, destination.width, 0, destination.height, -1, 1));
                GfxStream.Draw(Immediate.BlitQuad.Sync(), 0, s_Material.Sync(), 0, model, Color.white, properties: s_Properties);
            }
            finally { GfxStream.Restore(saved); }
        }
    }
}
