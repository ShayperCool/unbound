using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    /// <summary>
    /// Draws runtime panels with Unity's UI shader (Hidden/Internal-UIRDefault, compiled from the
    /// install's UnityUIE.cginc): screen panels over the scene among the overlay canvases by
    /// sorting order, panels with a target texture into it (cleared as their settings say).
    /// </summary>
    internal static partial class PanelRendering
    {
        static bool s_Installed;
        static Material s_Material, s_GammaMaterial;
        static bool s_ShaderMissing;
        static readonly Dictionary<BaseVisualElementPanel, PanelGpu> s_Gpu = new Dictionary<BaseVisualElementPanel, PanelGpu>();
        static readonly UaPainter s_Painter = new UaPainter();
        static readonly Dictionary<int, object> s_Properties = new Dictionary<int, object>();
        static readonly int[] s_TextureIds = new int[UaPainter.TextureSlots];
        static readonly int s_TextureInfoId = Shader.PropertyToID("_TextureInfo");
        static readonly int s_ShaderInfoId = Shader.PropertyToID("_ShaderInfoTex");
        static readonly int s_GradientSettingsId = Shader.PropertyToID("_GradientSettingsTex");
        static bool s_IntoCurrentTarget;

        sealed class PanelGpu
        {
            internal Mesh Mesh;
            internal Texture2D ShaderInfo;
        }

        static PanelRendering()
        {
            for (int i = 0; i < UaPainter.TextureSlots; i++) s_TextureIds[i] = Shader.PropertyToID("_Texture" + i);
        }

        internal static void InstallFrameHook()
        {
            if (s_Installed) return;
            s_Installed = true;
            FrameBuilder.OverlayPanels = new OverlayPanels();
            // Unity repaints panels after the frame's scripts and renders those with a target
            // texture before the cameras (which may show the texture).
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.PlayerEmitCanvasGeometry), () =>
            {
                UIElementsRuntimeUtility.RepaintPanels(false);
                UIElementsRuntimeUtility.RenderOffscreenPanels();
            });
        }

        internal static void Render(BaseRuntimePanel panel)
        {
            try
            {
                RenderPanel(panel);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static Material MaterialFor(bool forceGamma)
        {
            if (s_Material == null && !s_ShaderMissing)
            {
                var shader = Shader.Find("Hidden/Internal-UIRDefault");
                if (shader == null)
                {
                    s_ShaderMissing = true;
                    Debug.LogError("UI Toolkit: Unity's UI shader Hidden/Internal-UIRDefault is not available; panels are not drawn.");
                    return null;
                }
                using (ObjectRegistry.AsAssets())
                {
                    s_Material = new Material(shader) { name = "UIR Default" };
                    s_GammaMaterial = new Material(shader) { name = "UIR Default (force gamma)" };
                }
                s_Material.IsPersistentAsset = true;
                s_GammaMaterial.IsPersistentAsset = true;
                s_GammaMaterial.EnableKeyword("UIE_FORCE_GAMMA");
            }
            return forceGamma ? s_GammaMaterial : s_Material;
        }

        static readonly Dictionary<(bool, UaPainter.StencilKind, int), Material> s_Masked = new Dictionary<(bool, UaPainter.StencilKind, int), Material>();
        static Shader s_MaskedShader;
        static bool s_MaskedMissing;

        /// <summary>The material drawing a batch of a panel with stencil masks: the stencil state
        /// Unity's renderer sets (test equal to the mask depth; masks increment it where they
        /// cover, and decrement it again after their content, drawing no color).</summary>
        static Material MaskedMaterial(bool forceGamma, UaPainter.StencilKind kind, int reference)
        {
            if (s_Masked.TryGetValue((forceGamma, kind, reference), out var m)) return m;
            if (s_MaskedShader == null && !s_MaskedMissing)
            {
                s_MaskedShader = Shader.Find("Hidden/Unbound/Masked/Hidden/Internal-UIRDefault");
                if (s_MaskedShader == null)
                {
                    s_MaskedMissing = true;
                    Debug.LogError("UI Toolkit: the stencil variant of Hidden/Internal-UIRDefault is not available; rounded clipping is not applied.");
                }
            }
            if (s_MaskedShader == null) return null;
            using (ObjectRegistry.AsAssets()) m = new Material(s_MaskedShader) { name = $"UIR Default ({kind} {reference})" };
            m.IsPersistentAsset = true;
            if (forceGamma) m.EnableKeyword("UIE_FORCE_GAMMA");
            m.SetFloat("_UIRStencilRef", reference);
            m.SetFloat("_UIRStencilComp", (float)UnityEngine.Rendering.CompareFunction.Equal);
            m.SetFloat("_UIRStencilPass", kind switch
            {
                UaPainter.StencilKind.PushMask => (float)UnityEngine.Rendering.StencilOp.IncrementSaturate,
                UaPainter.StencilKind.PopMask => (float)UnityEngine.Rendering.StencilOp.DecrementSaturate,
                _ => (float)UnityEngine.Rendering.StencilOp.Keep,
            });
            m.SetFloat("_UIRColorMask", kind == UaPainter.StencilKind.Draw ? 15f : 0f);
            s_Masked[(forceGamma, kind, reference)] = m;
            return m;
        }

        static void RenderPanel(BaseRuntimePanel panel)
        {
            if (panel.disposed || panel.drawsInCameras) return;
            var material = MaterialFor(panel.uaForceGamma);
            if (material == null) return;
            var saved = GfxStream.Current;
            var rt = panel.targetTexture;
            ulong target;
            int width, height;
            if (rt != null)
            {
                target = rt.EnsureCreated();
                width = rt.width;
                height = rt.height;
            }
            else
            {
                target = s_IntoCurrentTarget ? saved.Color : 0;
                width = Screen.width;
                height = Screen.height;
            }

            s_Painter.Paint(panel, height);
            if (!s_Gpu.TryGetValue(panel, out var gpu))
            {
                gpu = new PanelGpu();
                s_Gpu[panel] = gpu;
                ReleaseDisposed();
            }
            UploadMesh(gpu);
            UploadShaderInfo(gpu);

            if (rt != null || !s_IntoCurrentTarget) GfxStream.SetTarget(target);
            GfxStream.Viewport(new Rect(0, 0, width, height));
            var clear = panel.uaClear;
            byte flags = 0;
            if (clear.clearColor) flags |= GfxStream.ClearColor;
            if (clear.clearDepthStencil) flags |= GfxStream.ClearDepth | GfxStream.ClearStencil;
            if (flags != 0) GfxStream.Clear(flags, clear.color);
            GfxStream.Camera(Matrix4x4.identity, Matrix4x4.Ortho(0f, width, height, 0f, -0.001f, 1.001f));
            if (s_Painter.Batches.Count > 0)
            {
                ulong mesh = gpu.Mesh.Sync();
                ulong materialHandle = material.Sync();
                bool forceGamma = panel.uaForceGamma;
                float ppp = panel.scaledPixelsPerPoint;
                var model = Matrix4x4.Scale(new Vector3(ppp, ppp, 1f));
                for (int i = 0; i < s_Painter.Batches.Count; i++)
                {
                    var batch = s_Painter.Batches[i];
                    if (batch.IndexCount == 0) continue;
                    BatchProperties(batch, gpu.ShaderInfo);
                    Rect? scissor = batch.Scissor.HasValue
                        ? new Rect(batch.Scissor.Value.x, batch.Scissor.Value.y, batch.Scissor.Value.width, batch.Scissor.Value.height)
                        : (Rect?)null;
                    ulong handle = materialHandle;
                    if (s_Painter.UsesStencil)
                    {
                        var masked = MaskedMaterial(forceGamma, batch.Stencil, batch.StencilRef);
                        if (masked != null) handle = masked.Sync();
                    }
                    GfxStream.Draw(mesh, i, handle, 0, model, Color.white, 0, scissor, s_Properties);
                }
            }
            GfxStream.Restore(saved);
        }

        static readonly List<BaseVisualElementPanel> s_Disposed = new List<BaseVisualElementPanel>();

        /// <summary>Frees the meshes and textures of panels disposed since.</summary>
        static void ReleaseDisposed()
        {
            s_Disposed.Clear();
            foreach (var p in s_Gpu.Keys)
                if (p.disposed) s_Disposed.Add(p);
            foreach (var p in s_Disposed)
            {
                var gpu = s_Gpu[p];
                s_Gpu.Remove(p);
                if (gpu.Mesh != null) UnityEngine.Object.Destroy(gpu.Mesh);
                if (gpu.ShaderInfo != null) UnityEngine.Object.Destroy(gpu.ShaderInfo);
            }
        }

        static void UploadMesh(PanelGpu gpu)
        {
            var p = s_Painter;
            if (gpu.Mesh == null)
            {
                using (ObjectRegistry.AsAssets()) gpu.Mesh = new Mesh { name = "UI Toolkit panel" };
                gpu.Mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                gpu.Mesh.MarkDynamic();
            }
            var mesh = gpu.Mesh;
            mesh.Clear();
            mesh.SetVertices(p.Positions);
            mesh.SetColors(p.Colors);
            for (int i = 0; i < 8; i++) mesh.SetUVs(i, p.Uvs[i]);
            mesh.subMeshCount = Math.Max(1, p.Batches.Count);
            var indices = p.Indices;
            for (int i = 0; i < p.Batches.Count; i++)
            {
                var b = p.Batches[i];
                var range = indices.GetRange(b.IndexStart, b.IndexCount);
                mesh.SetIndices(range, MeshTopology.Triangles, i, false);
            }
        }

        static void UploadShaderInfo(PanelGpu gpu)
        {
            var p = s_Painter;
            int pages = Math.Max(UaShaderInfo.OpacityPage(Math.Max(0, p.Opacities.Count - 1)), UaShaderInfo.TextPage(Math.Max(0, p.TextSettings.Count - 1))) + 1;
            int width = pages * UaShaderInfo.PageWidth, height = UaShaderInfo.TextureHeight;
            if (gpu.ShaderInfo == null || gpu.ShaderInfo.width != width)
            {
                if (gpu.ShaderInfo != null) UnityEngine.Object.Destroy(gpu.ShaderInfo);
                using (ObjectRegistry.AsAssets())
                    gpu.ShaderInfo = new Texture2D(width, height, TextureFormat.RGBAFloat, false) { name = "UIR shader info", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            var pixels = new Color[width * height];
            void Set(int x, int y, Color c) => pixels[y * width + x] = c;
            // The one transform (identity: vertices are in panel space) and the infinite clip rect.
            Set(UaShaderInfo.TransformPage * UaShaderInfo.PageWidth, 0, new Color(1, 0, 0, 0));
            Set(UaShaderInfo.TransformPage * UaShaderInfo.PageWidth, 1, new Color(0, 1, 0, 0));
            Set(UaShaderInfo.TransformPage * UaShaderInfo.PageWidth, 2, new Color(0, 0, 1, 0));
            Set(UaShaderInfo.ClipPage * UaShaderInfo.PageWidth, 0, new Color(0, 0, 0, 0));
            for (int id = 0; id < p.Opacities.Count; id++)
            {
                var (x, y) = UaShaderInfo.Texel(UaShaderInfo.OpacityPage(id), UaShaderInfo.InPage(id), 1);
                Set(x, y, new Color(1, 1, 1, p.Opacities[id]));
            }
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int id = 0; id < p.TextSettings.Count; id++)
            {
                var s = p.TextSettings[id];
                var (x, y) = UaShaderInfo.Texel(UaShaderInfo.TextPage(id), UaShaderInfo.InPage(id), 4);
                Set(x, y, linear ? s.faceColor.linear : s.faceColor);
                Set(x, y + 1, linear ? s.outlineColor.linear : s.outlineColor);
                Set(x, y + 2, linear ? s.underlayColor.linear : s.underlayColor);
                Set(x, y + 3, new Color(-s.underlayOffset.x, s.underlayOffset.y, s.underlaySoftness, s.outlineWidth));
            }
            gpu.ShaderInfo.SetPixels(pixels);
            gpu.ShaderInfo.Apply(false, false);
        }

        static readonly Vector4[] s_TextureInfo = new Vector4[UaPainter.TextureSlots * 2];

        static void BatchProperties(UaPainter.Batch batch, Texture2D shaderInfo)
        {
            s_Properties.Clear();
            for (int i = 0; i < UaPainter.TextureSlots; i++)
            {
                var t = i < batch.TextureCount ? batch.Textures[i] : null;
                s_Properties[s_TextureIds[i]] = t != null ? t : Texture2D.whiteTexture;
                if (t != null)
                {
                    float w = t.width, h = t.height;
                    s_TextureInfo[i * 2] = new Vector4(batch.Ids[i], 1f / w, 1f / h, batch.SdfScales[i]);
                    s_TextureInfo[i * 2 + 1] = new Vector4(w, h, batch.Sharpness[i], 0f);
                }
                else
                {
                    s_TextureInfo[i * 2] = new Vector4(-1f, 0f, 0f, 0f);
                    s_TextureInfo[i * 2 + 1] = Vector4.zero;
                }
            }
            s_Properties[s_TextureInfoId] = s_TextureInfo;
            s_Properties[s_ShaderInfoId] = shaderInfo;
            s_Properties[s_GradientSettingsId] = Texture2D.blackTexture;
        }

        /// <summary>Screen panels among the overlay canvases (Unity's Canvas.externRenderOverlaysBefore):
        /// before a canvas, the panels of a lower sorting order not drawn yet.</summary>
        sealed class OverlayPanels : FrameBuilder.IOverlayPanels
        {
            int m_Next = -1;

            public bool Any
            {
                get
                {
                    foreach (var p in UIElementsRuntimeUtility.GetSortedPlayerPanels())
                        if (p is BaseRuntimePanel r && r.targetTexture == null && !r.drawsInCameras && !r.disposed) return true;
                    return false;
                }
            }

            public void Begin() => m_Next = 0;

            public void RenderBefore(float maxPriority, bool intoCurrentTarget)
            {
                if (m_Next < 0) return;
                var panels = UIElementsRuntimeUtility.GetSortedPlayerPanels();
                while (m_Next < panels.Count)
                {
                    if (panels[m_Next] is BaseRuntimePanel panel)
                    {
                        if (panel.sortingPriority >= maxPriority) break;
                        if (panel.targetDisplay == 0 && !panel.drawsInCameras && panel.targetTexture == null && !panel.disposed)
                        {
                            s_IntoCurrentTarget = intoCurrentTarget;
                            try
                            {
                                panel.Render();
                            }
                            finally
                            {
                                s_IntoCurrentTarget = false;
                            }
                        }
                    }
                    m_Next++;
                }
                if (maxPriority == float.MaxValue) m_Next = -1;
            }
        }
    }

    /// <summary>Where UIR's shader reads per-vertex data (UnityUIE.cginc): pages of 32×8 texels;
    /// the transform page, the clip rect page, then opacity and text settings pages in turn, 256
    /// entries each.</summary>
    internal static class UaShaderInfo
    {
        internal const int PageWidth = 32, PageHeight = 8, TextureHeight = 32;
        internal const int TransformPage = 0, ClipPage = 1;

        internal static int OpacityPage(int id) => 2 + 2 * (id / 256);
        internal static int TextPage(int id) => 3 + 2 * (id / 256);
        internal static int InPage(int id) => id % 256;

        /// <summary>The texel of entry <paramref name="id"/> of a page whose entries take
        /// <paramref name="stride"/> rows (uie_decode_shader_info_texel_pos).</summary>
        internal static (int X, int Y) Texel(int page, int id, int stride) =>
            (page * PageWidth + id % PageWidth, id / PageWidth * stride);
    }
}
