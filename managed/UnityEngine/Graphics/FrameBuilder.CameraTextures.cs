using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    // The built-in pipeline's camera textures besides the depth texture (FrameBuilder.Shadows.cs):
    // depth and normals with Unity's replacement shader, motion vectors with Unity's motion vector
    // shader, as Camera.depthTextureMode asks for them (measured: tests/projects/builtinrp).
    internal static partial class FrameBuilder
    {
        /// <summary>Whether a directional light's screen-space shadows need the camera's depth
        /// texture (they collect against it; measured: its events fire for a shadowed sun).</summary>
        static bool NeedsShadowDepth(Camera camera)
        {
            if (QualitySettings.shadows == ShadowQuality.Disable) return false;
            if (Mathf.Min(QualitySettings.shadowDistance, camera.farClipPlane) <= camera.nearClipPlane) return false;
            var main = LightingGlobals.MainLightOf();
            if (main != null && main.shadows != LightShadows.None) return true;
            foreach (var light in ForwardLights.Lights)
                if (light.type == LightType.Directional && light.shadows != LightShadows.None
                    && light.renderMode != LightRenderMode.ForceVertex && light.shadowStrength > 0f)
                    return true;
            return false;
        }

        static Shader s_DepthNormalsShader;
        static bool s_DepthNormalsDrawn;
        static readonly Dictionary<(Material, Shader), (Material Copy, int Version)> s_ReplacementMaterials = new Dictionary<(Material, Shader), (Material, int)>();

        /// <summary>A material drawn with another shader: a copy of its properties under the
        /// replacement, kept until the material changes.</summary>
        static Material ReplacementMaterial(Material material, Shader replacement)
        {
            var key = (material, replacement);
            if (s_ReplacementMaterials.TryGetValue(key, out var cached) && cached.Copy && cached.Version == material.m_Version)
                return cached.Copy;
            if (cached.Copy) UnityEngine.Object.DestroyImmediate(cached.Copy);
            Material copy;
            using (ObjectRegistry.AsAssets())
                copy = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
            copy.shader = replacement;
            s_ReplacementMaterials[key] = (copy, material.m_Version);
            if (s_ReplacementMaterials.Count > 4096) PurgeReplacementMaterials();
            return copy;
        }

        static void PurgeReplacementMaterials()
        {
            var dead = new List<(Material, Shader)>();
            foreach (var pair in s_ReplacementMaterials)
                if (!pair.Key.Item1) dead.Add(pair.Key);
            foreach (var key in dead)
            {
                if (s_ReplacementMaterials[key].Copy) UnityEngine.Object.DestroyImmediate(s_ReplacementMaterials[key].Copy);
                s_ReplacementMaterials.Remove(key);
            }
        }

        /// <summary>A camera's replacement shader (SetReplacementShader, RenderWithShader): each
        /// draw takes the replacement's SubShader whose tag has the value the draw's shader has
        /// (draws without one are dropped), or with no tag the replacement's own; its material's
        /// properties stay and it sorts at the replacement's queue (measured:
        /// tests/projects/builtinfx replace_*).</summary>
        static void ReplaceShaders(Camera camera, List<DrawItem> items)
        {
            var shader = camera.m_ReplacementShader;
            if (shader == null) return;
            string tag = camera.m_ReplacementTag;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (item.Material == null) continue;
                var replacement = shader;
                if (!string.IsNullOrEmpty(tag))
                {
                    var info = ShaderPassInfo.Of(item.Material.shader);
                    if (info == null || !info.Tags.TryGetValue(tag, out var value) || string.IsNullOrEmpty(value))
                    {
                        items.RemoveAt(i);
                        continue;
                    }
                    replacement = Shader.Replacement(shader, tag, value);
                    var replaced = ShaderPassInfo.Of(replacement);
                    if (replaced == null || !replaced.Found)
                    {
                        items.RemoveAt(i);
                        continue;
                    }
                }
                var material = ReplacementMaterial(item.Material, replacement);
                item.Material = material;
                item.Queue = material.renderQueue;
                items[i] = item;
            }
        }

        /// <summary>_CameraDepthNormalsTexture: view-space normals and linear depth packed into
        /// ARGB32 by Unity's Hidden/Internal-DepthNormalsTexture, which replaces each object's
        /// shader by its SubShader of the same RenderType (objects of another RenderType, or none,
        /// are left out), cleared to the far plane facing the camera.</summary>
        static void RenderDepthNormals(Camera camera, CameraContext context, int width, int height)
        {
            s_DepthNormalsShader ??= Shader.Find("Hidden/Internal-DepthNormalsTexture");
            var texture = Temporary(new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D24_UNorm_S8_UInt));
            texture.filterMode = FilterMode.Point;
            SetupCamera(TextureHandles.Of(texture), new Rect(0, 0, width, height), CameraClearFlags.Nothing, Color.black, context.View, context.Projection, 0);
            GfxStream.Clear((byte)(GfxStream.ClearColor | GfxStream.ClearDepth | GfxStream.ClearStencil), new Color(0.5f, 0.5f, 1f, 1f));
            RunEvent(camera, CameraEvent.BeforeDepthNormalsTexture, texture);
            if (s_DepthNormalsShader != null)
            {
                foreach (var item in s_Items)
                {
                    var info = ShaderPassInfo.Of(item.Material.shader);
                    if (info == null || !info.Tags.TryGetValue("RenderType", out var type) || string.IsNullOrEmpty(type)) continue;
                    var replacement = Shader.Replacement(s_DepthNormalsShader, "RenderType", type);
                    var replaced = ShaderPassInfo.Of(replacement);
                    if (replaced == null || !replaced.Found) continue;
                    var material = ReplacementMaterial(item.Material, replacement);
                    var draw = item;
                    draw.Material = material;
                    EmitDraw(draw, material.Sync(), GfxStream.AllPasses, false, false);
                }
            }
            GfxStream.GlobalTexture("_CameraDepthNormalsTexture", TextureHandles.Of(texture));
            s_DepthNormalsDrawn = true;
            RunEvent(camera, CameraEvent.AfterDepthNormalsTexture, texture);
        }

        static Material s_MotionVectors;
        static Mesh s_MotionQuad;
        /// <summary>Per camera, its motion vector texture: kept between renders, the global stays
        /// valid after the camera (measured: the texture of the frame before is still bound
        /// before the next render).</summary>
        static readonly Dictionary<Camera, RenderTexture> s_MotionTextures = new Dictionary<Camera, RenderTexture>();
        /// <summary>A camera the player loop rendered drew motion vectors this frame.</summary>
        static bool s_MotionRendered;
        static readonly int s_PreviousMId = Shader.PropertyToID("_PreviousM");
        static readonly int s_HasLastPositionDataId = Shader.PropertyToID("_HasLastPositionData");
        static readonly int s_ForceNoMotionId = Shader.PropertyToID("_ForceNoMotion");
        static readonly int s_MotionVectorDepthBiasId = Shader.PropertyToID("_MotionVectorDepthBias");

        /// <summary>_CameraMotionVectorsTexture (RG half): the camera's motion from the depth
        /// texture and its previous view-projection (Hidden/Internal-MotionVectors' camera pass,
        /// which also writes that depth), then the opaque objects that moved since the frame
        /// before, with their own MotionVectors pass or Unity's (measured: Camera.Render keeps
        /// neither the camera's nor the objects' previous matrices, the player loop's renders do;
        /// an object is taken to be still the first time it is drawn, where the Editor sometimes
        /// moves it from where it was made).</summary>
        static void RenderMotionVectors(Camera camera, CameraContext context, int width, int height, int opaqueCount, bool automatic)
        {
            var saved = GfxStream.Current;
            s_MotionVectors ??= new Material(Shader.Find("Hidden/Internal-MotionVectors")) { hideFlags = HideFlags.HideAndDontSave };
            if (!s_MotionTextures.TryGetValue(camera, out var texture) || !texture || texture.width != width || texture.height != height)
            {
                if (texture) UnityEngine.Object.DestroyImmediate(texture);
                using (ObjectRegistry.AsAssets())
                    texture = new RenderTexture(new RenderTextureDescriptor(width, height, GraphicsFormat.R16G16_SFloat, GraphicsFormat.None))
                    {
                        name = "Camera motion vectors",
                        hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Point,
                    };
                s_MotionTextures[camera] = texture;
                if (s_MotionTextures.Count > 64) PurgeMotionTextures();
            }
            SetupCamera(TextureHandles.Of(texture), new Rect(0, 0, width, height), CameraClearFlags.Nothing, Color.black, context.View, context.Projection, 0);
            GfxStream.SetTarget(TextureHandles.Of(texture), GfxStream.NoTexture);
            GfxStream.Viewport(new Rect(0, 0, width, height));
            GfxStream.Clear(GfxStream.ClearColor, Color.clear);
            var nonJittered = camera.nonJitteredProjectionMatrix * context.View;
            GfxStream.GlobalMatrix("_NonJitteredVP", nonJittered);
            GfxStream.GlobalMatrix("_PreviousVP", camera.previousViewProjectionMatrix);
            // The camera's motion over the whole view, from the depth texture.
            if (s_CameraDepth != null) GfxStream.GlobalTexture("_CameraDepthTexture", TextureHandles.Of(s_CameraDepth));
            var quad = MotionQuad(context.Projection);
            GfxStream.Draw(quad.Sync(), 0, s_MotionVectors.Sync(), 1, (context.Projection * context.View).inverse, Color.white);
            // The moving objects test against the camera's depth texture, bound as their depth
            // buffer (as Unity binds the camera's own).
            if (s_CameraDepth != null)
            {
                // Not sampled while it is the depth buffer.
                GfxStream.GlobalTexture("_CameraDepthTexture", 0);
                GfxStream.SetTarget(TextureHandles.Of(texture), TextureHandles.Of(s_CameraDepth));
                GfxStream.Viewport(new Rect(0, 0, width, height));
            }
            ulong internalPass = s_MotionVectors.Sync();
            for (int i = 0; i < opaqueCount; i++)
            {
                var item = s_Items[i];
                if (item.Renderer is not MeshRenderer renderer || item.Setup.HasValue) continue;
                var mode = renderer.motionVectorGenerationMode;
                if (mode == MotionVectorGenerationMode.Camera) continue;
                if (!renderer.m_HasPreviousModel)
                {
                    renderer.m_PreviousModel = item.Model;
                    renderer.m_HasPreviousModel = true;
                }
                var previous = renderer.m_PreviousModel;
                bool force = mode == MotionVectorGenerationMode.ForceNoMotion;
                if (!force && previous == item.Model) continue;
                var properties = new Dictionary<int, object>
                {
                    [s_PreviousMId] = previous,
                    [s_HasLastPositionDataId] = 0f,
                    [s_ForceNoMotionId] = force ? 1f : 0f,
                    [s_MotionVectorDepthBiasId] = 0f,
                };
                var info = ShaderPassInfo.Of(item.Material.shader);
                int own = -1;
                if (info != null && !item.Material.PassDisabled("MotionVectors"))
                    for (int p = 0; p < info.Passes.Count; p++)
                        if (info.LightMode(p) == "MotionVectors") { own = p; break; }
                if (own >= 0) EmitDraw(item, item.Material.Sync(), own, false, false, properties);
                else
                {
                    var draw = item;
                    draw.Material = s_MotionVectors;
                    draw.Properties = MergeProperties(item.Properties, properties);
                    EmitDraw(draw, internalPass, 0, false, false);
                }
            }
            GfxStream.GlobalTexture("_CameraMotionVectorsTexture", TextureHandles.Of(texture));
            if (s_CameraDepth != null) GfxStream.GlobalTexture("_CameraDepthTexture", TextureHandles.Of(s_CameraDepth));
            GfxStream.Restore(saved);
        }

        static Dictionary<int, object> MergeProperties(Dictionary<int, object> a, Dictionary<int, object> b)
        {
            if (a == null) return b;
            var merged = new Dictionary<int, object>(a);
            foreach (var pair in b) merged[pair.Key] = pair.Value;
            return merged;
        }

        /// <summary>A screen-covering quad in clip space whose normals are the view rays to the
        /// far plane (Unity's camera space, +z forward), as the camera motion pass reads them.</summary>
        static Mesh MotionQuad(Matrix4x4 projection)
        {
            s_MotionQuad ??= new Mesh { name = "Camera motion", hideFlags = HideFlags.HideAndDontSave };
            var inverse = projection.inverse;
            var corners = new[] { new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f) };
            var positions = new Vector3[4];
            var normals = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                positions[i] = new Vector3(corners[i].x, corners[i].y, 0f);
                var far = inverse * new Vector4(corners[i].x, corners[i].y, 1f, 1f);
                far /= far.w;
                normals[i] = new Vector3(far.x, far.y, -far.z);
            }
            s_MotionQuad.vertices = positions;
            s_MotionQuad.normals = normals;
            s_MotionQuad.SetTriangles(new[] { 0, 1, 2, 2, 3, 0 }, 0);
            return s_MotionQuad;
        }

        /// <summary>After a frame whose player-loop cameras drew motion vectors, the renderers'
        /// matrices become the previous ones.</summary>
        static void EndFrameMotion()
        {
            if (!s_MotionRendered) return;
            s_MotionRendered = false;
            foreach (var renderer in RenderRegistry.Renderers)
            {
                if (!renderer) continue;
                renderer.m_PreviousModel = renderer.transform.localToWorldMatrix;
                renderer.m_HasPreviousModel = true;
            }
        }

        static void PurgeMotionTextures()
        {
            var dead = new List<Camera>();
            foreach (var pair in s_MotionTextures) if (!pair.Key) dead.Add(pair.Key);
            foreach (var camera in dead)
            {
                if (s_MotionTextures[camera]) UnityEngine.Object.DestroyImmediate(s_MotionTextures[camera]);
                s_MotionTextures.Remove(camera);
            }
        }

        /// <summary>After a camera: its depth and depth-normals textures are released, their
        /// globals unbound (measured: the next camera's shaders read the default gray before its
        /// own are drawn).</summary>
        static void UnbindCameraTextures()
        {
            GfxStream.GlobalTexture("_CameraDepthTexture", 0);
            GfxStream.GlobalTexture("_CameraDepthNormalsTexture", 0);
        }
    }
}
