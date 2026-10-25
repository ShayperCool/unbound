using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    // The built-in pipeline's main light shadows, as the Editor draws them (values measured with
    // tests/projects/lighting's Probe/GlobalsDump): the camera's depth texture (its opaque objects'
    // ShadowCaster passes), the shadow casters into a cascade atlas, then Unity's own collector
    // (Hidden/Internal-ScreenSpaceShadows) into a screen-space shadow mask that the forward base
    // passes read with SHADOWS_SCREEN.
    internal static partial class FrameBuilder
    {
        static readonly List<DrawItem> s_Casters = new List<DrawItem>();
        static readonly Dictionary<string, int> s_ShadowCasterPass = new Dictionary<string, int>();
        static readonly List<RenderTexture> s_ShadowTemporaries = new List<RenderTexture>();
        static Material s_Collector;
        static Mesh s_CollectorQuad;
        /// <summary>The camera being drawn has a screen-space shadow mask (SHADOWS_SCREEN).</summary>
        static bool s_ScreenShadows;

        static int ShadowCasterPass(Shader shader)
        {
            if (shader == null) return -1;
            if (s_ShadowCasterPass.TryGetValue(shader.name, out int pass)) return pass;
            pass = -1;
            var info = ShaderPassInfo.Of(shader);
            if (info != null)
                for (int i = 0; i < info.Passes.Count; i++)
                    if (info.LightMode(i) == "ShadowCaster") { pass = i; break; }
            s_ShadowCasterPass[shader.name] = pass;
            return pass;
        }

        /// <summary>A light's shadow map size, as the built-in pipeline picks it: the light's pixel
        /// size (the larger side of what it reaches on screen) times the quality's multiplier (Very
        /// High 1, High 0.5, Medium 0.25, Low 0.125) and the type's factor (directional 3.8, spot 2,
        /// point 1), as a power of two, at most 4096, 2048 and 1024 (Unity manual, "Configure shadow
        /// resolution in the Built-In Render Pipeline"; measured: 512 for a 200x150 directional
        /// atlas, 256 for a spot covering it, at High).</summary>
        static int ShadowMapSize(Light light, float pixelSize)
        {
            if (light.shadowCustomResolution > 0) return Mathf.NextPowerOfTwo(light.shadowCustomResolution);
            var resolution = light.shadowResolution == LightShadowResolution.FromQualitySettings
                ? (int)QualitySettings.shadowResolution
                : (int)light.shadowResolution;
            float multiplier = resolution switch { 0 => 0.125f, 1 => 0.25f, 2 => 0.5f, _ => 1f };
            (float factor, int max) = light.type switch
            {
                LightType.Directional => (3.8f, 4096),
                LightType.Spot => (2f, 2048),
                _ => (1f, 1024),
            };
            int size = Mathf.NextPowerOfTwo(Mathf.Max(1, Mathf.CeilToInt(pixelSize * multiplier * factor)));
            return Mathf.Clamp(size, 16, max);
        }

        /// <summary>Draws the main light's shadows for a camera into its screen-space mask and
        /// makes it the forward base passes' _ShadowMapTexture; false when the light casts none.</summary>
        static bool RenderMainLightShadows(Camera camera, CameraContext context, int width, int height)
        {
            s_ScreenShadows = false;
            var light = LightingGlobals.MainLightOf();
            if (light == null || light.shadows == LightShadows.None || QualitySettings.shadows == ShadowQuality.Disable) return false;
            float distance = Mathf.Min(QualitySettings.shadowDistance, camera.farClipPlane);
            if (distance <= camera.nearClipPlane) return false;
            int cascades = QualitySettings.shadowCascades >= 4 ? 4 : QualitySettings.shadowCascades >= 2 ? 2 : 1;
            var ratios = cascades == 4 ? QualitySettings.shadowCascade4Split : new Vector3(QualitySettings.shadowCascade2Split, 0f, 0f);
            int atlas = ShadowMapSize(light, Mathf.Max(width, height));
            int tile = cascades == 1 ? atlas : atlas / 2;
            bool soft = light.shadows == LightShadows.Soft && QualitySettings.shadows == ShadowQuality.All;

            // The casters: every shadow-casting mesh renderer the light reaches.
            s_Casters.Clear();
            foreach (var renderer in RenderRegistry.Renderers)
            {
                if (!renderer || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
                if (!renderer.enabled || !renderer.m_GameObject.activeInHierarchy || renderer.shadowCastingMode == ShadowCastingMode.Off) continue;
                if ((light.cullingMask & (1 << renderer.m_GameObject.m_Layer)) == 0) continue;
                try { renderer.CollectDraws(context, s_Casters); }
                catch (Exception e) { Debug.LogException(e, renderer); }
            }
            for (int i = 0; i < s_Casters.Count; i++) s_Casters[i].Material.Sync();

            // The camera's depth texture first, unbiased: its opaque objects' ShadowCaster passes.
            GfxStream.Keyword("SHADOWS_DEPTH", true);
            GfxStream.GlobalVector("unity_LightShadowBias", Vector4.zero);
            var depth = Temporary(new RenderTextureDescriptor(width, height, GraphicsFormat.None, GraphicsFormat.D24_UNorm_S8_UInt));
            SetupCamera(TextureHandles.Of(depth), new Rect(0, 0, width, height), CameraClearFlags.Depth, Color.black, context.View, context.Projection, 0);
            foreach (var item in s_Items)
            {
                if (item.Queue > 2500 || !(item.Renderer is MeshRenderer || item.Renderer is SkinnedMeshRenderer)) continue;
                int pass = ShadowCasterPass(item.Material.shader);
                if (pass >= 0) EmitDraw(item, item.Material.Sync(), pass, false, false);
            }

            // The cascades, each leaving its bias set (the last one's stays, as in the Editor).
            var shadowMap = Temporary(new RenderTextureDescriptor(atlas, atlas, GraphicsFormat.None, GraphicsFormat.D24_UNorm_S8_UInt) { shadowSamplingMode = ShadowSamplingMode.CompareDepths });
            ulong shadowTarget = TextureHandles.Of(shadowMap);
            var lightToWorld = light.transform.localToWorldMatrix;
            var forward = light.transform.forward;
            var worldToShadow = new float[64];
            var spheres = new float[16];
            Vector4 splitsNear = default, splitsFar = default, radii = default;
            float near = camera.nearClipPlane;
            float scale = cascades == 1 ? 1f : 0.5f;
            // The casters draw with a hardware slope-scaled bias besides their shaders' (measured:
            // a slope factor of 1 matches the Editor's screen-space masks; the constant shows no
            // effect at these depths).
            GfxStream.DepthBias(1f, 1f);
            // The whole atlas is cleared, then each cascade draws into its tile.
            SetupCamera(shadowTarget, new Rect(0, 0, atlas, atlas), CameraClearFlags.Depth, Color.black, Matrix4x4.identity, Matrix4x4.identity, 0);
            for (int c = 0; c < cascades; c++)
            {
                if (!CullingResults.DirectionalShadowSlice(camera, lightToWorld, c, cascades, ratios, tile, distance, out var view, out var projection, out var sphere, out _, true))
                    continue;
                var rect = cascades == 1 ? new Rect(0, 0, atlas, atlas) : new Rect((c % 2) * tile, (c / 2) * tile, tile, tile);
                SetupCamera(shadowTarget, rect, CameraClearFlags.Nothing, Color.black, view, projection, 0);
                float half = 1f / Mathf.Max(projection.m00, 1e-8f);
                float range = 2f / Mathf.Max(-projection.m22, 1e-8f);
                float texel = 2f * half / tile;
                // The normal offset is the bias in texels, 3.65 times wider for the soft filter's
                // kernel (measured: tests/projects/lighting's shadow_globals_*).
                float normalBias = light.shadowNormalBias * texel * (soft ? 3.65f : 1f);
                GfxStream.GlobalVector("unity_LightShadowBias", new Vector4(light.shadowBias * 2f / range, 1f, normalBias, 0f));
                GfxStream.GlobalVector("_WorldSpaceLightPos0", new Vector4(-forward.x, -forward.y, -forward.z, 0f));
                foreach (var item in s_Casters)
                {
                    int pass = ShadowCasterPass(item.Material.shader);
                    if (pass >= 0) EmitDraw(item, item.Material.Sync(), pass, false, false);
                }
                // Clip space to the cascade's tile of the atlas (OpenGL depth to [0, 1]).
                float ox = cascades == 1 ? 0f : (c % 2) * 0.5f, oy = cascades == 1 ? 0f : (c / 2) * 0.5f;
                var toTexture = new Matrix4x4(
                    new Vector4(0.5f * scale, 0f, 0f, 0f),
                    new Vector4(0f, 0.5f * scale, 0f, 0f),
                    new Vector4(0f, 0f, 0.5f, 0f),
                    new Vector4(0.5f * scale + ox, 0.5f * scale + oy, 0.5f, 1f));
                var m = toTexture * projection * view;
                for (int k = 0; k < 16; k++) worldToShadow[c * 16 + k] = m[k % 4, k / 4];
                spheres[c * 4] = sphere.x;
                spheres[c * 4 + 1] = sphere.y;
                spheres[c * 4 + 2] = sphere.z;
                spheres[c * 4 + 3] = sphere.w * sphere.w;
                radii[c] = sphere.w * sphere.w;
                float Ratio(int i) => i >= cascades - 1 ? 1f : i switch { 0 => ratios.x, 1 => ratios.y, _ => ratios.z };
                splitsNear[c] = c == 0 ? near : near + Ratio(c - 1) * (distance - near);
                splitsFar[c] = near + Ratio(c) * (distance - near);
            }

            GfxStream.Keyword("SHADOWS_DEPTH", false);

            GfxStream.DepthBias(0f, 0f);

            // Collect into the screen-space mask.
            s_Collector ??= new Material(Shader.Find("Hidden/Internal-ScreenSpaceShadows")) { hideFlags = HideFlags.HideAndDontSave };
            var mask = Temporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) { sRGB = false });
            SetupCamera(TextureHandles.Of(mask), new Rect(0, 0, width, height), CameraClearFlags.SolidColor, Color.white, context.View, context.Projection, 0);
            GfxStream.GlobalTexture("_CameraDepthTexture", TextureHandles.Of(depth));
            GfxStream.GlobalTexture("_ShadowMapTexture", shadowTarget);
            GfxStream.GlobalArray("unity_WorldToShadow", 16, worldToShadow);
            GfxStream.GlobalArray("unity_ShadowSplitSpheres", 4, spheres);
            GfxStream.GlobalVector("unity_ShadowSplitSqRadii", radii);
            GfxStream.GlobalVector("_LightSplitsNear", splitsNear);
            GfxStream.GlobalVector("_LightSplitsFar", splitsFar);
            // Strength, far over distance, and the fade from half to 0.7 of the distance away from
            // a center 0.3 of the distance along the view (measured).
            GfxStream.GlobalVector("_LightShadowData", new Vector4(1f - light.shadowStrength, camera.farClipPlane / distance, 1f / (0.2f * distance), -2.5f));
            var fadeCenter = context.Position + context.Forward * (0.3f * distance);
            GfxStream.GlobalVector("unity_ShadowFadeCenterAndType", new Vector4(fadeCenter.x, fadeCenter.y, fadeCenter.z, 1f));
            bool spheresFit = QualitySettings.shadowProjection == ShadowProjection.StableFit;
            GfxStream.Keyword("SHADOWS_SPLIT_SPHERES", spheresFit);
            GfxStream.Keyword("SHADOWS_SINGLE_CASCADE", cascades == 1);
            var quad = CollectorQuad(context.Projection);
            var model = (context.Projection * context.View).inverse;
            GfxStream.Draw(quad.Sync(), 0, s_Collector.Sync(), soft ? 2 : 0, model, Color.white);
            GfxStream.Keyword("SHADOWS_SPLIT_SPHERES", false);
            GfxStream.Keyword("SHADOWS_SINGLE_CASCADE", false);
            GfxStream.GlobalTexture("_ShadowMapTexture", TextureHandles.Of(mask));
            s_ScreenShadows = true;
            return true;
        }

        /// <summary>A screen-covering quad in clip space whose second uv is the view ray to the far
        /// plane (Unity's camera space, +z forward), as the collector's vertex program reads it.</summary>
        static Mesh CollectorQuad(Matrix4x4 projection)
        {
            s_CollectorQuad ??= new Mesh { name = "Shadow collector", hideFlags = HideFlags.HideAndDontSave };
            var inverse = projection.inverse;
            var corners = new[] { new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f) };
            var positions = new Vector3[4];
            var uvs = new List<Vector2>();
            var rays = new List<Vector3>();
            for (int i = 0; i < 4; i++)
            {
                positions[i] = new Vector3(corners[i].x, corners[i].y, 0f);
                uvs.Add(new Vector2(corners[i].x * 0.5f + 0.5f, corners[i].y * 0.5f + 0.5f));
                var far = inverse * new Vector4(corners[i].x, corners[i].y, 1f, 1f);
                far /= far.w;
                rays.Add(new Vector3(far.x, far.y, -far.z));
            }
            s_CollectorQuad.vertices = positions;
            s_CollectorQuad.SetUVs(0, uvs);
            s_CollectorQuad.SetUVs(1, rays);
            s_CollectorQuad.SetTriangles(new[] { 0, 1, 2, 2, 3, 0 }, 0);
            return s_CollectorQuad;
        }

        static RenderTexture Temporary(RenderTextureDescriptor descriptor)
        {
            var rt = RenderTexture.GetTemporary(descriptor);
            s_ShadowTemporaries.Add(rt);
            return rt;
        }

        static void ReleaseShadowTemporaries()
        {
            foreach (var rt in s_ShadowTemporaries) RenderTexture.ReleaseTemporary(rt);
            s_ShadowTemporaries.Clear();
            s_ScreenShadows = false;
        }
    }
}
