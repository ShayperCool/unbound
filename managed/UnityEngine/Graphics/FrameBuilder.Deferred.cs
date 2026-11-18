using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    // The built-in pipeline's Deferred Shading path, with Unity's own shaders: the opaque objects'
    // Deferred passes fill the G-buffer (albedo and occlusion, specular and smoothness, normals,
    // and the light buffer with emission and ambient), then Hidden/Internal-DeferredReflections
    // adds the reflection probes' light and Hidden/Internal-DeferredShading each light's, before
    // the objects without a Deferred pass and the transparent ones draw forward into the light
    // buffer. The light buffer is half float (as HDR cameras have it; an LDR camera's is clamped
    // when it goes to its target). Camera events: Before/After GBuffer, Reflections, Lighting and
    // FinalPass, no depth texture events (measured: tests/projects/builtinfx paths.events.*).
    internal static partial class FrameBuilder
    {
        static Material s_DeferredShading, s_DeferredReflections, s_DeferredReflectionsOver;
        static bool s_CustomShadingReported;
        static Shader s_OwnShading;
        static readonly int s_SrcBlend = Shader.PropertyToID("_SrcBlend");
        static readonly int s_DstBlend = Shader.PropertyToID("_DstBlend");
        static readonly int s_LightAsQuad = Shader.PropertyToID("_LightAsQuad");
        static readonly int s_LightPosId = Shader.PropertyToID("_LightPos");
        static readonly int s_LightDirId = Shader.PropertyToID("_LightDir");
        static readonly int s_LightColorId = Shader.PropertyToID("_LightColor");
        static readonly int s_LightTextureB0Id = Shader.PropertyToID("_LightTextureB0");
        static readonly string[] s_DeferredLightKeywords = { "DIRECTIONAL", "DIRECTIONAL_COOKIE", "POINT", "POINT_COOKIE", "SPOT" };

        /// <summary>The stencil bit the G-buffer pass marks the scene's pixels with, which the
        /// light passes test (Unity's own bit and the stencil a camera's later passes see are
        /// unmeasured).</summary>
        const byte SceneStencilBit = 0x80;

        /// <summary>"During [the base and lighting] stages stencil state defined in the shader
        /// will be ignored" (Unity 6000.0 Manual, ShaderLab command: Stencil): the engine's own
        /// stencil marks the pixels the G-buffer pass draws, and the lights draw over those only
        /// (measured with a custom deferred shading shader: tests/projects/builtinfx
        /// settings_deferred_custom, the light pass leaves the background as it is).</summary>
        static readonly RenderStateBlock s_GBufferStencil = new RenderStateBlock(RenderStateMask.Stencil)
        {
            stencilReference = SceneStencilBit,
            stencilState = new StencilState(true, 0, SceneStencilBit, CompareFunction.Always, StencilOp.Replace, StencilOp.Keep, StencilOp.Keep),
        };

        /// <summary>The light passes: over the marked pixels, with no depth test (their quads
        /// cover the view, as before they had a depth buffer).</summary>
        static readonly RenderStateBlock s_LightStencil = new RenderStateBlock(RenderStateMask.Stencil | RenderStateMask.Depth)
        {
            depthState = new DepthState(false, CompareFunction.Always),
            stencilReference = SceneStencilBit,
            stencilState = new StencilState(true, SceneStencilBit, 0, CompareFunction.Equal, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep),
        };

        /// <summary>The pass a material fills the G-buffer with (-1: it draws forward).</summary>
        internal static int DeferredPass(Material material)
        {
            if (material == null || material.PassDisabled("Deferred")) return -1;
            var info = ShaderPassInfo.Of(material.shader);
            if (info == null) return -1;
            for (int p = 0; p < info.Passes.Count; p++)
                if (info.CanDraw(p) && string.Equals(info.LightMode(p), "Deferred", StringComparison.OrdinalIgnoreCase)) return p;
            return -1;
        }

        /// <summary>The deferred opaque part of a camera's render into <paramref name="light"/>
        /// (the light buffer) and <paramref name="depth"/>: reorders the opaque draws (the first
        /// <paramref name="opaqueCount"/>) to put those that fill the G-buffer first, draws them,
        /// the shadows, reflections and lights; returns where the forward opaque draws start. The
        /// light buffer and depth are bound again at the end, with the camera's matrices.</summary>
        static int RenderDeferredOpaque(Camera camera, CameraContext context, RenderTexture light, RenderTexture depth, Rect viewport, int opaqueCount)
        {
            int width = (int)viewport.width, height = (int)viewport.height;
            // Draws with a Deferred pass first, in their order.
            var deferred = new List<(DrawItem Item, int Pass)>();
            var forward = new List<DrawItem>();
            for (int i = 0; i < opaqueCount; i++)
            {
                var item = s_Items[i];
                int pass = IsLit(item) ? DeferredPass(item.Material) : -1;
                if (pass >= 0) deferred.Add((item, pass));
                else forward.Add(item);
            }
            for (int i = 0; i < deferred.Count; i++) s_Items[i] = deferred[i].Item;
            for (int i = 0; i < forward.Count; i++) s_Items[deferred.Count + i] = forward[i];

            // The light buffer is half float for LDR cameras too: the shaders' HDR variants.
            const bool hdr = true;
            var gbuffer0 = Temporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) { sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear });
            var gbuffer1 = Temporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) { sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear });
            var gbuffer2 = Temporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) { sRGB = false });
            ulong g0 = TextureHandles.Of(gbuffer0), g1 = TextureHandles.Of(gbuffer1), g2 = TextureHandles.Of(gbuffer2);
            ulong lightHandle = TextureHandles.Of(light), depthHandle = TextureHandles.Of(depth);

            // The G-buffer: cleared (the light buffer to the background), then the Deferred passes.
            var background = camera.clearFlags is CameraClearFlags.Skybox or CameraClearFlags.SolidColor ? camera.backgroundColor : Color.black;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) background = background.linear;
            foreach (var handle in new[] { g0, g1, g2 })
            {
                GfxStream.SetTarget(handle, GfxStream.NoTexture);
                GfxStream.Viewport(viewport);
                GfxStream.Clear(GfxStream.ClearColor, Color.clear);
            }
            GfxStream.SetTarget(lightHandle, depthHandle);
            GfxStream.Viewport(viewport);
            byte clear = camera.clearFlags == CameraClearFlags.Nothing ? (byte)0 : (byte)(GfxStream.ClearDepth | GfxStream.ClearStencil);
            if (camera.clearFlags is CameraClearFlags.Skybox or CameraClearFlags.SolidColor) clear |= GfxStream.ClearColor;
            if (clear != 0) GfxStream.Clear(clear, background);
            GfxStream.SetTargets(new[] { g0, g1, g2, lightHandle }, depthHandle);
            GfxStream.Viewport(viewport);
            GfxStream.Camera(context.View, context.Projection, GfxStream.CameraProperties);
            if (hdr) GfxStream.Keyword("UNITY_HDR_ON", true);
            RunEvent(camera, CameraEvent.BeforeGBuffer, light);
            GfxStream.SetTargets(new[] { g0, g1, g2, lightHandle }, depthHandle);
            GfxStream.Viewport(viewport);
            if (s_MaterialHandles.Length < s_Items.Count) Array.Resize(ref s_MaterialHandles, Math.Max(s_Items.Count, s_MaterialHandles.Length * 2));
            GfxStream.StateOverride(s_GBufferStencil);
            for (int i = 0; i < deferred.Count; i++)
            {
                var (item, pass) = deferred[i];
                if (item.Material.shader != null && item.Material.shader.IsLodHidden) continue;
                EmitDraw(item, item.Material.Sync(), pass);
            }
            GfxStream.StateOverride(null);
            RunEvent(camera, CameraEvent.AfterGBuffer, light);
            GfxStream.GlobalTexture("_CameraGBufferTexture0", g0);
            GfxStream.GlobalTexture("_CameraGBufferTexture1", g1);
            GfxStream.GlobalTexture("_CameraGBufferTexture2", g2);
            GfxStream.GlobalTexture("_CameraDepthTexture", depthHandle);

            // Shadows: collected against the G-buffer's depth.
            s_CameraDepth = depth;
            RenderShadows(camera, context, width, height);
            GfxStream.GlobalTexture("_CameraDepthTexture", depthHandle);

            // The lights and reflections draw as quads over the view (Unity's global).
            GfxStream.GlobalFloat("_LightAsQuad", 1f);
            var quad = MotionQuad(context.Projection);
            ulong quadMesh = quad.Sync();
            var quadModel = (context.Projection * context.View).inverseDoublePrecision;

            // Reflections: the default reflection, then each probe over it, into a buffer added to
            // the light buffer.
            RunEvent(camera, CameraEvent.BeforeReflections, light);
            // The project's reflections shader; none, or No Support, draws no reflections.
            var reflectionsShading = GraphicsSettings.DeferredReflectionsDisabled ? null
                : GraphicsSettings.RendererMaterial(BuiltinShaderType.DeferredReflections, ref s_DeferredReflections);
            var reflections = Temporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBHalf, 0) { sRGB = false });
            ulong reflectionsHandle = TextureHandles.Of(reflections);
            SetupCamera(reflectionsHandle, viewport, CameraClearFlags.Nothing, Color.black, context.View, context.Projection, 0);
            GfxStream.SetTarget(reflectionsHandle, GfxStream.NoTexture);
            GfxStream.Viewport(viewport);
            GfxStream.Clear(GfxStream.ClearColor, Color.clear);
            if (reflectionsShading != null) DrawReflections(reflectionsShading, quadMesh, quadModel);
            SetupCamera(lightHandle, viewport, CameraClearFlags.Nothing, Color.black, context.View, context.Projection, 0);
            GfxStream.SetTarget(lightHandle, GfxStream.NoTexture);
            GfxStream.Viewport(viewport);
            GfxStream.GlobalTexture("_CameraReflectionsTexture", reflectionsHandle);
            if (reflectionsShading != null) GfxStream.Draw(quadMesh, 0, reflectionsShading.Sync(), 1, quadModel, Color.white);
            RunEvent(camera, CameraEvent.AfterReflections, light);

            // Lighting: each light over the scene's pixels (outside its range it adds nothing),
            // with the project's deferred shading shader (none: no light is drawn).
            RunEvent(camera, CameraEvent.BeforeLighting, light);
            GfxStream.SetTarget(lightHandle, GfxStream.NoTexture);
            GfxStream.Viewport(viewport);
            var shading = GraphicsSettings.RendererMaterial(BuiltinShaderType.DeferredShading, ref s_DeferredShading);
            if (shading != null && !s_CustomShadingReported
                && shading.shader != (s_OwnShading ??= Shader.Find("Hidden/Internal-DeferredShading")))
            {
                // Measured difference (tests/projects/builtinfx settings_deferredLdr_custom): the
                // Editor decodes an LDR camera's light buffer with the shader's second pass;
                // Unbound's light buffer is linear.
                s_CustomShadingReported = true;
                Debug.LogWarning("Unbound: a custom deferred shading shader's final pass (an LDR camera's light buffer decode) is not drawn: Unbound's light buffer is linear");
            }
            var lights = new List<Light>();
            if (shading != null)
            {
                if (ForwardLights.Main != null) lights.Add(ForwardLights.Main);
                lights.AddRange(ForwardLights.Lights);
            }
            ulong shadingMaterial = shading != null ? shading.Sync() : 0;
            if (lights.Count > 0)
            {
                // The lights test the G-buffer's stencil, bound as their depth buffer, and read
                // its depth from a copy (a texture is not sampled while it is drawn into).
                var sampledDepth = Temporary(new RenderTextureDescriptor(width, height, GraphicsFormat.None, GraphicsFormat.D24_UNorm_S8_UInt));
                GfxStream.CopyDepth(depthHandle, TextureHandles.Of(sampledDepth));
                GfxStream.GlobalTexture("_CameraDepthTexture", TextureHandles.Of(sampledDepth));
                GfxStream.SetTarget(lightHandle, depthHandle);
                GfxStream.Viewport(viewport);
                GfxStream.StateOverride(s_LightStencil);
            }
            int mask = camera.cullingMask;
            foreach (var l in lights)
            {
                if (l == null || (l.cullingMask & mask) == 0) continue;
                var properties = new Dictionary<int, object> { [s_LightAsQuad] = 1f };
                string keyword = ForwardLights.AddPassProperties(l, 1f, properties);
                properties[s_LightColorId] = properties[s_LightColor0Id];
                var t = l.transform;
                var position = t.position;
                float range = Mathf.Max(l.range, 1e-4f);
                properties[s_LightPosId] = new Vector4(position.x, position.y, position.z, 1f / (range * range));
                var forwardDirection = t.forward;
                properties[s_LightDirId] = new Vector4(forwardDirection.x, forwardDirection.y, forwardDirection.z, 0f);
                if (keyword is "POINT" or "POINT_COOKIE" or "SPOT") properties[s_LightTextureB0Id] = ForwardLights.Attenuation;
                string shadowKeyword = null, softKeyword = null;
                if (l == ForwardLights.Main)
                {
                    if (s_ScreenShadows)
                    {
                        shadowKeyword = "SHADOWS_SCREEN";
                        properties[s_ShadowMapTextureId] = s_ScreenShadowMask;
                    }
                }
                else AddPassShadow(l, true, properties, out shadowKeyword, out softKeyword);
                foreach (var k in s_DeferredLightKeywords) GfxStream.Keyword(k, k == keyword);
                if (shadowKeyword != null) GfxStream.Keyword(shadowKeyword, true);
                if (softKeyword != null) GfxStream.Keyword(softKeyword, true);
                GfxStream.Draw(quadMesh, 0, shadingMaterial, 0, quadModel, Color.white, properties: properties);
                if (shadowKeyword != null) GfxStream.Keyword(shadowKeyword, false);
                if (softKeyword != null) GfxStream.Keyword(softKeyword, false);
            }
            if (lights.Count > 0) GfxStream.StateOverride(null);
            foreach (var k in s_DeferredLightKeywords) GfxStream.Keyword(k, false);
            GfxStream.GlobalFloat("_LightAsQuad", 0f);
            RunEvent(camera, CameraEvent.AfterLighting, light);
            if (hdr) GfxStream.Keyword("UNITY_HDR_ON", false);
            // The final pass decodes an LDR light buffer; this one is already linear.
            RunEvent(camera, CameraEvent.BeforeFinalPass, light);
            RunEvent(camera, CameraEvent.AfterFinalPass, light);

            // The forward part draws into the light buffer, tested against the G-buffer's depth.
            SetupCamera(lightHandle, viewport, CameraClearFlags.Nothing, Color.black, context.View, context.Projection, 0);
            GfxStream.SetTarget(lightHandle, depthHandle);
            GfxStream.Viewport(viewport);
            GfxStream.GlobalTexture("_CameraDepthTexture", depthHandle);
            return deferred.Count;
        }

        /// <summary>The default reflection, then each probe over it, drawn with the reflections
        /// shader's first pass into the bound reflections buffer.</summary>
        static void DrawReflections(Material shading, ulong quadMesh, Matrix4x4 quadModel)
        {
            ulong material = shading.Sync();
            // A probe blends over what is there by its falloff (the shader's blend factors).
            if (s_DeferredReflectionsOver == null || s_DeferredReflectionsOver.shader != shading.shader)
            {
                if (s_DeferredReflectionsOver != null) UnityEngine.Object.DestroyImmediate(s_DeferredReflectionsOver);
                s_DeferredReflectionsOver = new Material(shading) { hideFlags = HideFlags.HideAndDontSave };
                s_DeferredReflectionsOver.SetFloat(s_SrcBlend, (float)BlendMode.SrcAlpha);
                s_DeferredReflectionsOver.SetFloat(s_DstBlend, (float)BlendMode.OneMinusSrcAlpha);
            }
            ulong overMaterial = s_DeferredReflectionsOver.Sync();
            // The default reflection first; a scene without one reflects black, which adds nothing.
            var defaultReflection = ReflectionProbe.defaultTexture;
            if (!ReferenceEquals(defaultReflection, ReflectionProbe.Black))
            {
                var decode = ReflectionProbe.defaultTextureHDRDecodeValues;
                DrawReflection(quadMesh, quadModel, material, defaultReflection, decode,
                    new Vector3(-1e6f, -1e6f, -1e6f), new Vector3(1e6f, 1e6f, 1e6f), Vector3.zero, false, 1f);
            }
            var probes = new List<ReflectionProbe>();
            foreach (var probe in ReflectionProbe.s_Enabled)
                if (probe && probe.isActiveAndEnabled && (probe.texture != null || probe.mode == ReflectionProbeMode.Realtime)) probes.Add(probe);
            // Less important and larger probes first: smaller, more important ones blend over them.
            // The probes' placement as the frame began (ReflectionProbe.Drawn).
            probes.Sort((a, b) =>
            {
                var pa = a.Drawn;
                var pb = b.Drawn;
                int c = pa.Importance.CompareTo(pb.Importance);
                if (c != 0) return c;
                var sa = pa.Size;
                var sb = pb.Size;
                return (sb.x * sb.y * sb.z).CompareTo(sa.x * sa.y * sa.z);
            });
            foreach (var probe in probes)
            {
                var placement = probe.Drawn;
                var box = placement.Bounds;
                DrawReflection(quadMesh, quadModel, overMaterial, probe.texture ?? ReflectionProbe.Black, probe.textureHDRDecodeValues,
                    box.min, box.max, placement.Position, placement.BoxProjection, Mathf.Max(placement.BlendDistance, 1e-4f));
            }
        }

        static void DrawReflection(ulong mesh, Matrix4x4 model, ulong material, Texture cube, Vector4 decode, Vector3 min, Vector3 max,
            Vector3 position, bool boxProjection, float blendDistance)
        {
            GfxStream.Keyword("UNITY_SPECCUBE_BOX_PROJECTION", boxProjection);
            GfxStream.Draw(mesh, 0, material, 0, model, Color.white, properties: new Dictionary<int, object>
            {
                [s_LightAsQuad] = 1f,
                [Shader.PropertyToID("unity_SpecCube0")] = cube,
                [Shader.PropertyToID("unity_SpecCube0_HDR")] = decode,
                [Shader.PropertyToID("unity_SpecCube0_BoxMin")] = new Vector4(min.x, min.y, min.z, 1f),
                [Shader.PropertyToID("unity_SpecCube0_BoxMax")] = new Vector4(max.x, max.y, max.z, 0f),
                [Shader.PropertyToID("unity_SpecCube0_ProbePosition")] = new Vector4(position.x, position.y, position.z, boxProjection ? 1f : 0f),
                [Shader.PropertyToID("unity_SpecCube1_ProbePosition")] = new Vector4(0f, 0f, 0f, blendDistance),
            });
            GfxStream.Keyword("UNITY_SPECCUBE_BOX_PROJECTION", false);
        }

        /// <summary>The Vertex Lit path: every draw takes its shader's first SubShader with Vertex
        /// (or untagged) passes, else its fallback's (Standard draws as Legacy Shaders/VertexLit).</summary>
        static void VertexLitShaders(List<DrawItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var shader = item.Material?.shader;
                if (shader == null) continue;
                var variant = Shader.VertexLit(shader);
                var material = ReplacementMaterial(item.Material, variant);
                item.Material = material;
                items[i] = item;
            }
        }
    }
}
