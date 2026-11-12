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
                copy = new Material(material) { hideFlags = HideFlags.HideAndDontSave, parent = null };
            // Preserve existing replacement sorting across the public shader queue reset.
            // Custom-queue replacement ordering has not been measured.
            int queue = copy.rawRenderQueue;
            copy.shader = replacement;
            copy.renderQueue = queue;
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
        /// properties stay. With no custom queue it sorts at the replacement's queue (measured:
        /// tests/projects/builtinfx replace_*); custom-queue ordering is unmeasured and preserved.</summary>
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
                if (!ObjectMotion(item, out var previous, out bool moving, out bool force) || !moving) continue;
                var info = ShaderPassInfo.Of(item.Material.shader);
                int own = -1;
                if (info != null && !item.Material.PassDisabled("MotionVectors"))
                    for (int p = 0; p < info.Passes.Count; p++)
                        if (info.LightMode(p) == "MotionVectors") { own = p; break; }
                // Set right before the draw: no other draw may take them.
                if (own >= 0)
                {
                    ulong material = item.Material.Sync();
                    GfxStream.NextDrawBuiltinMotion(previous, force);
                    EmitDraw(item, material, own, false, false);
                }
                else
                {
                    var draw = item;
                    draw.Material = s_MotionVectors;
                    GfxStream.NextDrawBuiltinMotion(previous, force);
                    EmitDraw(draw, internalPass, 0, false, false);
                }
            }
            GfxStream.GlobalTexture("_CameraMotionVectorsTexture", TextureHandles.Of(texture));
            if (s_CameraDepth != null) GfxStream.GlobalTexture("_CameraDepthTexture", TextureHandles.Of(s_CameraDepth));
            GfxStream.Restore(saved);
        }

        /// <summary>Whether a draw has object motion (a mesh renderer, or a skinned mesh renderer
        /// without active cloth, not a draw set up by a script, whose motion is not the camera's),
        /// with its previous matrix and whether it moved since the matrices were last kept
        /// (<see cref="EndFrameMotion"/>) or forces no motion. A renderer is taken to be still the
        /// first time it is drawn. On the Editor's OpenGL Core device (the one Unbound reports) a
        /// bone-skinned mesh moves as its object matrix (<see cref="Renderer.MotionMatrix"/>, its
        /// root bone's) does and its bones' deformation adds no motion, with or without
        /// skinnedMotionVectors and with CPU or GPU mesh deformation (Unity 6000.0.71f1,
        /// tests/projects/urp motion_blur_skinned*). Unbound draws such a skin with another matrix
        /// than that one (the identity: in world space); its previous matrix places this frame's
        /// skin where the object matrix was (the kept matrix times the current inverse).</summary>
        internal static bool ObjectMotion(in DrawItem item, out Matrix4x4 previous, out bool moving, out bool forceNoMotion)
        {
            previous = default;
            moving = forceNoMotion = false;
            if (item.Setup.HasValue) return false;
            var renderer = item.Renderer;
            var skin = renderer as SkinnedMeshRenderer;
            if (skin != null)
            {
                var cloth = skin.GetComponent<Cloth>();
                if (cloth != null && cloth.enabled && cloth.m_Active) return false;
            }
            else if (renderer is not MeshRenderer) return false;
            var mode = renderer.motionVectorGenerationMode;
            if (mode == MotionVectorGenerationMode.Camera) return false;
            // A skin's object motion is drawn in this skinning pass: its buffers alternate (with
            // Camera motion only, unmeasured, they do not).
            if (skin != null) skin.m_MotionEpoch = SkinnedMeshRenderer.SkinPass;
            bool boned = skin != null && skin.IsBoneSkinned;
            var model = boned ? skin.MotionMatrix : item.Model;
            if (!renderer.m_HasPreviousModel)
            {
                renderer.m_PreviousModel = model;
                renderer.m_HasPreviousModel = true;
            }
            forceNoMotion = mode == MotionVectorGenerationMode.ForceNoMotion;
            moving = forceNoMotion || renderer.m_PreviousModel != model;
            previous = boned ? renderer.m_PreviousModel * model.inverse * item.Model : renderer.m_PreviousModel;
            return true;
        }

        /// <summary>A draw of a renderer list with <see cref="PerObjectData.MotionVectors"/> that
        /// moved (or forces no motion): it draws its motion vector pass even when its material
        /// disables that pass (as URP's materials do by default); a still one only when the
        /// material enables it (URP 17's MaterialPostprocessor notes both).</summary>
        internal static bool MovingObject(in DrawItem item) => ObjectMotion(item, out _, out bool moving, out _) && moving;

        /// <summary>A renderer list with per-object motion vectors keeps the draws that have
        /// object motion (<see cref="ObjectMotion"/>), in place.</summary>
        internal static void KeepObjectMotion(List<DrawItem> items)
        {
            int kept = 0;
            for (int i = 0; i < items.Count; i++)
                if (ObjectMotion(items[i], out _, out _, out _)) items[kept++] = items[i];
            items.RemoveRange(kept, items.Count - kept);
        }

        /// <summary>Before a renderer list draw with per-object motion vectors: the engine's
        /// values for it (<see cref="GfxStream.NextDrawMotion"/>; depth bias and camera-only
        /// unmeasured, 0).</summary>
        static void NextDrawMotion(in DrawItem item)
        {
            ObjectMotion(item, out var previous, out _, out bool force);
            GfxStream.NextDrawMotion(previous, new Vector4(0f, force ? 0f : 1f, 0f, 0f));
        }

        static readonly Vector2[] s_MotionQuadCorners = { new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f) };
        static readonly Vector3[] s_MotionQuadPositions = new Vector3[4];
        static readonly Vector3[] s_MotionQuadNormals = new Vector3[4];
        static readonly int[] s_MotionQuadTriangles = { 0, 1, 2, 2, 3, 0 };
        /// <summary>The projection <see cref="s_MotionQuad"/> holds the view rays of.</summary>
        static Matrix4x4 s_MotionQuadProjection;

        /// <summary>A screen-covering quad in clip space whose normals are the view rays to the
        /// far plane (Unity's camera space, +z forward), as the camera motion pass reads them;
        /// rebuilt only when the projection changes.</summary>
        static Mesh MotionQuad(Matrix4x4 projection)
        {
            if (s_MotionQuad != null && s_MotionQuadProjection.Equals(projection)) return s_MotionQuad;
            s_MotionQuad ??= new Mesh { name = "Camera motion", hideFlags = HideFlags.HideAndDontSave };
            s_MotionQuadProjection = projection;
            var inverse = projection.inverse;
            for (int i = 0; i < 4; i++)
            {
                var corner = s_MotionQuadCorners[i];
                s_MotionQuadPositions[i] = new Vector3(corner.x, corner.y, 0f);
                var far = inverse * new Vector4(corner.x, corner.y, 1f, 1f);
                far /= far.w;
                s_MotionQuadNormals[i] = new Vector3(far.x, far.y, -far.z);
            }
            s_MotionQuad.vertices = s_MotionQuadPositions;
            s_MotionQuad.normals = s_MotionQuadNormals;
            s_MotionQuad.SetTriangles(s_MotionQuadTriangles, 0);
            return s_MotionQuad;
        }

        /// <summary>After a frame whose player-loop cameras drew motion vectors, the renderers'
        /// matrices become the previous ones.</summary>
        internal static void EndFrameMotion()
        {
            if (!s_MotionRendered) return;
            s_MotionRendered = false;
            foreach (var renderer in RenderRegistry.Renderers)
            {
                if (!renderer) continue;
                renderer.m_PreviousModel = renderer.MotionMatrix;
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
