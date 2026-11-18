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
    // the objects without a Deferred pass and the transparent ones draw forward into the camera's
    // target. An HDR camera's lights add into its target (half float); an LDR camera's go into a
    // 10-bit light buffer of their own as exp2(-light), each light multiplying it, which the
    // shading shader's second pass decodes into the target over the scene's pixels (the final
    // pass; matches the Unity 6000.0.71f1 Editor, tests/projects/builtinfx path_deferred_ldr,
    // settings_deferredLdr_*). Camera events: Before/After GBuffer, Reflections, Lighting and
    // FinalPass, no depth texture events (measured: tests/projects/builtinfx paths.events.*).
    internal static partial class FrameBuilder
    {
        static Material s_DeferredShading, s_DeferredReflections, s_DeferredReflectionsOver;
        /// <summary>The shading and reflection materials with the blend factors the camera's
        /// light buffer takes: added (HDR) or multiplied (LDR).</summary>
        static Material s_ShadingHdr, s_ShadingLdr, s_ReflectionsAddHdr, s_ReflectionsAddLdr;
        static readonly int s_SrcBlend = Shader.PropertyToID("_SrcBlend");
        static readonly int s_DstBlend = Shader.PropertyToID("_DstBlend");
        static readonly int s_LightAsQuad = Shader.PropertyToID("_LightAsQuad");
        static readonly int s_StencilNonBackground = Shader.PropertyToID("_StencilNonBackground");
        static readonly int s_LightPosId = Shader.PropertyToID("_LightPos");
        static readonly int s_LightDirId = Shader.PropertyToID("_LightDir");
        static readonly int s_LightColorId = Shader.PropertyToID("_LightColor");
        static readonly int s_LightTextureB0Id = Shader.PropertyToID("_LightTextureB0");
        static readonly string[] s_DeferredLightKeywords = { "DIRECTIONAL", "DIRECTIONAL_COOKIE", "POINT", "POINT_COOKIE", "SPOT" };

        /// <summary>The stencil bits the G-buffer pass sets on every pixel it draws, which the
        /// light passes test and the passes after the lighting see, and the bits of a Deferred
        /// pass's own stencil reference written beside them, whatever its test, operation and
        /// write mask (measured: tests/projects/builtinfx deferredStencil.*, Unity 6000.0.71f1:
        /// 0xC0 over a Standard object, Ref 2 0xC2, Ref 1 Equal Keep and Ref 1 IncrSat 0xC1,
        /// Ref 8 WriteMask 0 0xC8, Ref 16 and Ref 32 0xC0; none over the background).</summary>
        const byte SceneStencilBits = 0xC0;
        const byte PassStencilBits = 0x0F;

        /// <summary>"During [the base and lighting] stages stencil state defined in the shader
        /// will be ignored" (Unity 6000.0 Manual, ShaderLab command: Stencil): the G-buffer draws
        /// write the engine's bits and the pass's kept reference bits with no stencil test
        /// (GfxStream.EngineStencilMask), and the lights draw over the marked pixels only
        /// (tests/projects/builtinfx settings_deferred_custom: a custom light pass leaves the
        /// background as it is).</summary>
        static readonly RenderStateBlock s_GBufferStencil = new RenderStateBlock((RenderStateMask)GfxStream.EngineStencilMask)
        {
            stencilReference = SceneStencilBits,
            stencilState = new StencilState(true, PassStencilBits, 0, CompareFunction.Always, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep),
        };

        /// <summary>A light drawn over the whole view: over the G-buffer's marked pixels, with no
        /// depth test (its quad covers the view).</summary>
        static readonly RenderStateBlock s_LightStencil = new RenderStateBlock(RenderStateMask.Stencil | RenderStateMask.Depth | RenderStateMask.Raster)
        {
            rasterState = new RasterState(CullMode.Off),
            depthState = new DepthState(false, CompareFunction.Always),
            stencilReference = LightMarkBit,
            stencilState = new StencilState(true, LightMarkBit, 0, CompareFunction.Equal, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep),
        };

        /// <summary>The 10-bit buffers of the G-buffer: normals, and an LDR camera's light buffer.</summary>
        static RenderTextureDescriptor TenBit(int width, int height) =>
            new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB2101010, 0) { sRGB = false };

        /// <summary><paramref name="copy"/>: <paramref name="source"/> with these blend factors,
        /// made again when the source's shader changes.</summary>
        static Material BlendCopy(ref Material copy, Material source, BlendMode src, BlendMode dst)
        {
            if (copy == null || copy.shader != source.shader)
            {
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
                copy = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (copy.GetFloat(s_SrcBlend) != (float)src) copy.SetFloat(s_SrcBlend, (float)src);
            if (copy.GetFloat(s_DstBlend) != (float)dst) copy.SetFloat(s_DstBlend, (float)dst);
            return copy;
        }

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
        /// <param name="depthAttachment">The depth and stencil the passes draw with: <paramref name="depth"/>'s,
        /// or an LDR camera's target's own (then <paramref name="depth"/> takes a copy of it after the
        /// G-buffer, which the shadows and lights sample).</param>
        static int RenderDeferredOpaque(Camera camera, CameraContext context, RenderTexture light, RenderTexture depth, ulong depthAttachment, Rect viewport, int opaqueCount)
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

            // HDR: the lights add into the camera's target; LDR: a light buffer of their own.
            bool hdr = camera.allowHDR;
            var gbuffer0 = Temporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) { sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear });
            var gbuffer1 = Temporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) { sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear });
            var gbuffer2 = Temporary(TenBit(width, height));
            var lightBuffer = hdr ? light : Temporary(TenBit(width, height));
            ulong g0 = TextureHandles.Of(gbuffer0), g1 = TextureHandles.Of(gbuffer1), g2 = TextureHandles.Of(gbuffer2);
            ulong targetHandle = TextureHandles.Of(light), lightHandle = TextureHandles.Of(lightBuffer), depthHandle = depthAttachment;
            ulong sampledHandle = TextureHandles.Of(depth);

            // The G-buffer: cleared (an HDR camera's target to the background, an LDR camera's
            // light buffer to black), then the Deferred passes.
            var background = camera.clearFlags is CameraClearFlags.Skybox or CameraClearFlags.SolidColor ? camera.backgroundColor : Color.black;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) background = background.linear;
            foreach (var handle in hdr ? new[] { g0, g1, g2 } : new[] { g0, g1, g2, lightHandle })
            {
                GfxStream.SetTarget(handle, GfxStream.NoTexture);
                GfxStream.Viewport(viewport);
                GfxStream.Clear(GfxStream.ClearColor, Color.clear);
            }
            GfxStream.SetTarget(targetHandle, depthHandle);
            GfxStream.Viewport(viewport);
            byte clear = camera.clearFlags == CameraClearFlags.Nothing ? (byte)0 : (byte)(GfxStream.ClearDepth | GfxStream.ClearStencil);
            if (hdr && camera.clearFlags is CameraClearFlags.Skybox or CameraClearFlags.SolidColor) clear |= GfxStream.ClearColor;
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
            if (sampledHandle != depthHandle) GfxStream.CopyDepth(depthHandle, sampledHandle);
            GfxStream.GlobalTexture("_CameraGBufferTexture0", g0);
            GfxStream.GlobalTexture("_CameraGBufferTexture1", g1);
            GfxStream.GlobalTexture("_CameraGBufferTexture2", g2);
            GfxStream.GlobalTexture("_CameraDepthTexture", sampledHandle);

            // Shadows: collected against the G-buffer's depth.
            s_CameraDepth = depth;
            RenderShadows(camera, context, width, height);
            GfxStream.GlobalTexture("_CameraDepthTexture", sampledHandle);

            // The lights and reflections draw as quads over the view (Unity's global).
            GfxStream.GlobalFloat("_LightAsQuad", 1f);
            var quad = MotionQuad(context.Projection);
            ulong quadMesh = quad.Sync();
            var quadModel = (context.Projection * context.View).inverseDoublePrecision;
            // The light buffer takes each light added (HDR) or multiplied (LDR, exp2(-light)).
            var src = hdr ? BlendMode.One : BlendMode.DstColor;
            var dst = hdr ? BlendMode.One : BlendMode.Zero;

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
            if (reflectionsShading != null)
            {
                var add = hdr ? BlendCopy(ref s_ReflectionsAddHdr, reflectionsShading, src, dst) : BlendCopy(ref s_ReflectionsAddLdr, reflectionsShading, src, dst);
                GfxStream.Draw(quadMesh, 0, add.Sync(), 1, quadModel, Color.white);
            }
            RunEvent(camera, CameraEvent.AfterReflections, light);

            // Lighting: each light over its volume or the view, on the scene's pixels, with the
            // project's deferred shading shader (none: no light is drawn).
            RunEvent(camera, CameraEvent.BeforeLighting, light);
            GfxStream.SetTarget(lightHandle, GfxStream.NoTexture);
            GfxStream.Viewport(viewport);
            var shading = GraphicsSettings.RendererMaterial(BuiltinShaderType.DeferredShading, ref s_DeferredShading);
            var lights = new List<Light>();
            if (shading != null)
            {
                if (ForwardLights.Main != null) lights.Add(ForwardLights.Main);
                lights.AddRange(ForwardLights.Lights);
            }
            ulong shadingMaterial = shading == null ? 0
                : (hdr ? BlendCopy(ref s_ShadingHdr, shading, src, dst) : BlendCopy(ref s_ShadingLdr, shading, src, dst)).Sync();
            if (lights.Count > 0)
            {
                // The lights test the G-buffer's stencil and depth, bound as their depth buffer,
                // and read its depth from a copy (a texture is not sampled while it is drawn into).
                var sampledDepth = Temporary(new RenderTextureDescriptor(width, height, GraphicsFormat.None, GraphicsFormat.D24_UNorm_S8_UInt));
                GfxStream.CopyDepth(depthHandle, TextureHandles.Of(sampledDepth));
                GfxStream.GlobalTexture("_CameraDepthTexture", TextureHandles.Of(sampledDepth));
                GfxStream.SetTarget(lightHandle, depthHandle);
                GfxStream.Viewport(viewport);
            }
            int mask = camera.cullingMask;
            foreach (var l in lights)
            {
                if (l == null || (l.cullingMask & mask) == 0) continue;
                var kind = LightVolume(l, camera, context, out var volume, out var volumeModel);
                var properties = new Dictionary<int, object> { [s_LightAsQuad] = kind == VolumeKind.View ? 1f : 0f };
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
                if (kind == VolumeKind.View)
                {
                    GfxStream.StateOverride(s_LightStencil);
                    GfxStream.Draw(quadMesh, 0, shadingMaterial, 0, quadModel, Color.white, properties: properties);
                }
                else
                {
                    ulong volumeMesh = volume.Sync();
                    if (kind == VolumeKind.Marked)
                    {
                        GfxStream.StateOverride(s_VolumeMark);
                        GfxStream.Draw(volumeMesh, 0, shadingMaterial, 0, volumeModel, Color.white, properties: properties);
                        GfxStream.StateOverride(s_VolumeMarked);
                    }
                    else GfxStream.StateOverride(VolumeState(kind, LightmapModeForRender(l)));
                    GfxStream.Draw(volumeMesh, 0, shadingMaterial, 0, volumeModel, Color.white, properties: properties);
                }
                if (shadowKeyword != null) GfxStream.Keyword(shadowKeyword, false);
                if (softKeyword != null) GfxStream.Keyword(softKeyword, false);
            }
            if (lights.Count > 0) GfxStream.StateOverride(null);
            foreach (var k in s_DeferredLightKeywords) GfxStream.Keyword(k, false);
            GfxStream.GlobalFloat("_LightAsQuad", 0f);
            RunEvent(camera, CameraEvent.AfterLighting, light);
            if (hdr) GfxStream.Keyword("UNITY_HDR_ON", false);

            // The final pass: an LDR camera's target is cleared (its color, as the camera clears
            // it) and takes the light buffer decoded by the shading shader's second pass, which
            // Unity's draws over the scene's pixels (_StencilNonBackground).
            if (!hdr)
            {
                GfxStream.SetTarget(targetHandle, depthHandle);
                GfxStream.Viewport(viewport);
                GfxStream.GlobalFloat("_StencilNonBackground", LightMarkBit);
                if (camera.clearFlags is CameraClearFlags.Skybox or CameraClearFlags.SolidColor) GfxStream.Clear(GfxStream.ClearColor, background);
                GfxStream.GlobalTexture("_LightBuffer", lightHandle);
            }
            RunEvent(camera, CameraEvent.BeforeFinalPass, light);
            if (!hdr && shading != null)
            {
                GfxStream.SetTarget(targetHandle, depthHandle);
                GfxStream.Viewport(viewport);
                GfxStream.Camera(Matrix4x4.identity, Immediate.Ortho);
                // The pass's stencil reference names _StencilNonBackground, which the material
                // answers (Unity's is a global).
                var final = BlendCopy(ref s_ShadingLdr, shading, src, dst);
                if (final.GetFloat(s_StencilNonBackground) != LightMarkBit) final.SetFloat(s_StencilNonBackground, LightMarkBit);
                GfxStream.Draw(Immediate.BlitQuad.Sync(), 0, final.Sync(), 1, Matrix4x4.identity, Color.white);
                // The event after it draws with the camera's matrices.
                GfxStream.Camera(context.View, context.Projection, GfxStream.CameraProperties);
            }
            RunEvent(camera, CameraEvent.AfterFinalPass, light);

            // The forward part draws into the camera's target, tested against the G-buffer's depth.
            SetupCamera(targetHandle, viewport, CameraClearFlags.Nothing, Color.black, context.View, context.Projection, 0);
            GfxStream.SetTarget(targetHandle, depthHandle);
            GfxStream.Viewport(viewport);
            GfxStream.GlobalTexture("_CameraDepthTexture", sampledHandle);
            return deferred.Count;
        }

        /// <summary>The default reflection, then each probe over it, drawn with the reflections
        /// shader's first pass into the bound reflections buffer.</summary>
        static void DrawReflections(Material shading, ulong quadMesh, Matrix4x4 quadModel)
        {
            ulong material = shading.Sync();
            // A probe blends over what is there by its falloff (the shader's blend factors).
            ulong overMaterial = BlendCopy(ref s_DeferredReflectionsOver, shading, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha).Sync();
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
