using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// The albedo and emission images global illumination takes materials from: renderers'
    /// meta passes (<c>LightMode</c> <c>Meta</c>, the shaders' own) drawn over lightmap
    /// coordinates, filled and then again as wireframe, so that texels the triangles only touch
    /// are written too. Precomputed realtime GI draws a system's renderers at their places in the
    /// system (docs/REALTIME_GI.md, Material inputs); the bake of baked lightmaps draws each
    /// renderer over the bounds of its own lightmap UVs (tests/projects/bakedbuffers).
    /// </summary>
    internal static class GIMaterialImages
    {
        /// <summary>The meta pass's largest albedo (unity_MaxOutputValue).</summary>
        internal const float MaxAlbedo = 0.97f;

        static readonly System.Collections.Generic.Dictionary<int, object> s_Properties = new System.Collections.Generic.Dictionary<int, object>();
        static readonly int s_LightmapST = Shader.PropertyToID("unity_LightmapST");
        static readonly int s_DynamicLightmapST = Shader.PropertyToID("unity_DynamicLightmapST");

        /// <summary>
        /// Draws an image into <paramref name="target"/>: albedo over transparent black (8-bit
        /// sRGB in a linear project), or emission over opaque black. <paramref name="renderers"/>
        /// draws every renderer of the image once (<see cref="DrawRenderer"/>); it is called for
        /// the filled pass and for the wireframe one. With <paramref name="realtimeUVs"/> the
        /// meta passes place their vertices by the realtime lightmap UVs, else by the baked ones.
        /// </summary>
        internal static void Draw(RenderTexture target, int width, int height, bool emissive, bool linear, float albedoBoost, bool realtimeUVs, Action renderers)
        {
            var saved = GfxStream.Current;
            bool wireframe = GL.wireframe;
            GfxStream.SetTarget(target.EnsureCreated());
            GfxStream.Viewport(new Rect(0, 0, width, height));
            GfxStream.Clear(GfxStream.ClearColor | GfxStream.ClearDepth, emissive ? new Color(0f, 0f, 0f, 1f) : new Color(0f, 0f, 0f, 0f));
            GfxStream.Camera(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
            GfxStream.GlobalVector("unity_MetaVertexControl", realtimeUVs ? new Vector4(0f, 1f, 0f, 0f) : new Vector4(1f, 0f, 0f, 0f));
            GfxStream.GlobalVector("unity_MetaFragmentControl", emissive ? new Vector4(0f, 1f, 0f, 0f) : new Vector4(1f, 0f, 0f, 0f));
            GfxStream.GlobalFloat("unity_OneOverOutputBoost", 1f / albedoBoost);
            GfxStream.GlobalFloat("unity_MaxOutputValue", MaxAlbedo);
            GfxStream.GlobalFloat("unity_UseLinearSpace", linear ? 1f : 0f);
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    GL.wireframe = pass == 1;
                    renderers();
                }
            }
            finally
            {
                GL.wireframe = wireframe;
                GfxStream.Restore(saved);
            }
        }

        /// <summary>
        /// Draws a renderer's submeshes with their materials' meta passes, its lightmap UVs
        /// scaled and offset by <paramref name="scaleOffset"/>. A material's emission is drawn
        /// where <paramref name="emits"/> says so. <paramref name="purpose"/> names what the
        /// image is for in the notes about a renderer or material that cannot be drawn. A submesh
        /// whose material slot is empty is drawn with <paramref name="emptySlot"/>, or not at all
        /// without one; a submesh past the renderer's slots is not drawn.
        /// </summary>
        internal static void DrawRenderer(Renderer renderer, Mesh mesh, Vector4 scaleOffset, bool emissive, Func<Material, bool> emits, string purpose, Action<string, string> warn, Material emptySlot = null)
        {
            if (renderer == null) return;
            if (mesh == null)
            {
                warn("mesh:" + renderer.name, $"'{renderer.name}' ({renderer.GetType().Name}) has no mesh Unbound can render for {purpose}: it adds no albedo or emission.");
                return;
            }
            var materials = renderer.sharedMaterials;
            s_Properties.Clear();
            s_Properties[s_LightmapST] = scaleOffset;
            s_Properties[s_DynamicLightmapST] = scaleOffset;
            int submeshes = Math.Min(materials.Length, mesh.subMeshCount);
            for (int i = 0; i < submeshes; i++)
            {
                var material = materials[i] ?? emptySlot;
                if (material == null) continue;
                if (emissive && !emits(material)) continue;
                int pass = MetaPass(material);
                if (pass < 0)
                {
                    warn("meta:" + material.name, $"GI: setting up material '{material.name}' failed: its shader has no Meta pass. '{renderer.name}' does not affect {purpose}.");
                    continue;
                }
                GfxStream.Draw(mesh.Sync(), i, material.Sync(), pass, renderer.localToWorldMatrix, Color.white, properties: s_Properties);
            }
        }

        static int MetaPass(Material material)
        {
            var info = ShaderPassInfo.Of(material.shader);
            if (info == null) return -1;
            for (int i = 0; i < info.Passes.Count; i++)
                if (info.Passes[i].Tags.TryGetValue("LightMode", out var mode) && string.Equals(mode, "Meta", StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }
    }
}
