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
            if (s_ShadowCasterPass.TryGetValue(shader.DrawnName, out int pass)) return pass;
            pass = -1;
            var info = ShaderPassInfo.Of(shader);
            if (info != null)
                for (int i = 0; i < info.Passes.Count; i++)
                    if (info.LightMode(i) == "ShadowCaster") { pass = i; break; }
            s_ShadowCasterPass[shader.DrawnName] = pass;
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
            var mask = RenderDirectionalShadow(light, camera, context, width, height);
            if (mask == null) return false;
            GfxStream.GlobalTexture("_ShadowMapTexture", TextureHandles.Of(mask));
            s_ScreenShadows = true;
            return true;
        }

        /// <summary>The camera's depth texture (_CameraDepthTexture), drawn once per camera between
        /// its BeforeDepthTexture and AfterDepthTexture events, unbiased: its opaque objects'
        /// ShadowCaster passes.</summary>
        static RenderTexture CameraDepth(CameraContext context, int width, int height, Camera camera = null)
        {
            if (s_CameraDepth != null) return s_CameraDepth;
            s_CameraDepth = Temporary(new RenderTextureDescriptor(width, height, GraphicsFormat.None, GraphicsFormat.D24_UNorm_S8_UInt));
            camera ??= context.Camera;
            SetupCamera(TextureHandles.Of(s_CameraDepth), new Rect(0, 0, width, height), CameraClearFlags.Depth, Color.black, context.View, context.Projection, 0);
            RunEvent(camera, CameraEvent.BeforeDepthTexture, s_CameraDepth);
            GfxStream.Keyword("SHADOWS_DEPTH", true);
            GfxStream.GlobalVector("unity_LightShadowBias", Vector4.zero);
            foreach (var item in s_Items)
            {
                if (item.Queue > 2500 || !IsLit(item)) continue;
                if (!ShadowCaster(item.Material, out var material, out int pass)) continue;
                var draw = item;
                draw.Material = material;
                EmitDraw(draw, material.Sync(), pass, false, false);
            }
            GfxStream.Keyword("SHADOWS_DEPTH", false);
            GfxStream.GlobalTexture("_CameraDepthTexture", TextureHandles.Of(s_CameraDepth));
            RunEvent(camera, CameraEvent.AfterDepthTexture, s_CameraDepth);
            return s_CameraDepth;
        }

        /// <summary>The material and pass a draw casts its shadow (and writes the depth texture)
        /// with: its shader's ShadowCaster pass, else its fallbacks' (drawn with the material's
        /// properties: surface and legacy shaders get theirs from there; measured:
        /// tests/projects/builtinrp depth_shadows_color, a Transparent/Cutout/Diffuse quad casts
        /// its cutout shadow). False when none has one. The built-in pipeline draws it even where
        /// the material disables it (measured: disabled_passes, a material asset and one set from
        /// script both still cast their shadows; renderer lists leave it out).</summary>
        static bool ShadowCaster(Material material, out Material drawn, out int pass)
        {
            drawn = material;
            pass = ShadowCasterPass(material.shader);
            if (pass >= 0) return true;
            var info = ShaderPassInfo.Of(material.shader);
            for (int depth = 0; depth < 8 && info != null && info.Found && !string.IsNullOrEmpty(info.Fallback); depth++)
            {
                var fallback = Shader.Find(info.Fallback);
                if (fallback == null) return false;
                pass = ShadowCasterPass(fallback);
                if (pass >= 0)
                {
                    drawn = ReplacementMaterial(material, fallback);
                    return true;
                }
                info = ShaderPassInfo.Of(fallback);
            }
            return false;
        }

        static RenderTexture s_CameraDepth;

        /// <summary>A directional light's shadows collected into a screen-space mask (what its
        /// passes read with SHADOWS_SCREEN); null when it casts none.</summary>
        static RenderTexture RenderDirectionalShadow(Light light, Camera camera, CameraContext context, int width, int height)
        {
            float distance = Mathf.Min(QualitySettings.shadowDistance, camera.farClipPlane);
            if (distance <= camera.nearClipPlane) return null;
            int cascades = QualitySettings.shadowCascades >= 4 ? 4 : QualitySettings.shadowCascades >= 2 ? 2 : 1;
            var ratios = cascades == 4 ? QualitySettings.shadowCascade4Split : new Vector3(QualitySettings.shadowCascade2Split, 0f, 0f);
            int atlas = ShadowMapSize(light, Mathf.Max(width, height));
            int tile = cascades == 1 ? atlas : atlas / 2;
            bool soft = light.shadows == LightShadows.Soft && QualitySettings.shadows == ShadowQuality.All;

            // The camera's depth texture first, then the light's casters.
            var depth = CameraDepth(context, width, height);
            CollectCasters(light, context);
            GfxStream.Keyword("SHADOWS_DEPTH", true);

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
            RunLightEvent(camera, light, LightEvent.BeforeShadowMap, shadowMap);
            for (int c = 0; c < cascades; c++)
            {
                if (!CullingResults.DirectionalShadowSlice(camera, lightToWorld, c, cascades, ratios, tile, distance, out var view, out var projection, out var sphere, out _, true))
                    continue;
                var rect = cascades == 1 ? new Rect(0, 0, atlas, atlas) : new Rect((c % 2) * tile, (c / 2) * tile, tile, tile);
                var cascadePass = (ShadowMapPass)((int)ShadowMapPass.DirectionalCascade0 << c);
                SetupCamera(shadowTarget, rect, CameraClearFlags.Nothing, Color.black, view, projection, 0);
                RunLightEvent(camera, light, LightEvent.BeforeShadowMapPass, shadowMap, cascadePass);
                float half = 1f / Mathf.Max(projection.m00, 1e-8f);
                float range = 2f / Mathf.Max(-projection.m22, 1e-8f);
                float texel = 2f * half / tile;
                // The normal offset is the bias in texels, 3.65 times wider for the soft filter's
                // kernel (measured: tests/projects/lighting's shadow_globals_*).
                float normalBias = light.shadowNormalBias * texel * (soft ? 3.65f : 1f);
                GfxStream.GlobalVector("unity_LightShadowBias", new Vector4(light.shadowBias * 2f / range, 1f, normalBias, 0f));
                GfxStream.GlobalVector("_WorldSpaceLightPos0", new Vector4(-forward.x, -forward.y, -forward.z, 0f));
                DrawCasters();
                RunLightEvent(camera, light, LightEvent.AfterShadowMapPass, shadowMap, cascadePass);
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
            RunLightEvent(camera, light, LightEvent.AfterShadowMap, shadowMap);

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
            GfxStream.GlobalVector("_LightShadowData", LightShadowData(light, camera));
            SetShadowFade(camera, context);
            bool spheresFit = QualitySettings.shadowProjection == ShadowProjection.StableFit;
            GfxStream.Keyword("SHADOWS_SPLIT_SPHERES", spheresFit);
            GfxStream.Keyword("SHADOWS_SINGLE_CASCADE", cascades == 1);
            RunLightEvent(camera, light, LightEvent.BeforeScreenspaceMask, mask);
            var quad = CollectorQuad(context.Projection);
            var model = (context.Projection * context.View).inverse;
            GfxStream.Draw(quad.Sync(), 0, s_Collector.Sync(), soft ? 2 : 0, model, Color.white);
            RunLightEvent(camera, light, LightEvent.AfterScreenspaceMask, mask);
            GfxStream.Keyword("SHADOWS_SPLIT_SPHERES", false);
            GfxStream.Keyword("SHADOWS_SINGLE_CASCADE", false);
            return mask;
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

        /// <summary>The shadow casters a light reaches: every enabled shadow-casting mesh renderer
        /// on its culling mask that can shade what the camera sees, into <see cref="s_Casters"/>.
        /// They count as rendered (visible), after the camera's own renderers (measured:
        /// tests/projects/messages, a caster above the view and a ShadowsOnly one are visible).</summary>
        static void CollectCasters(Light light, CameraContext context)
        {
            s_Casters.Clear();
            var culling = Culler.ForCamera(context.Camera, context.Position, context.Forward);
            foreach (var renderer in RenderRegistry.Renderers)
            {
                if (!renderer || !(IsMeshLike(renderer)) || renderer.forceRenderingOff) continue;
                if (!renderer.enabled || !renderer.m_GameObject.activeInHierarchy || renderer.shadowCastingMode == ShadowCastingMode.Off) continue;
                // Only what the camera's layers show casts shadows (measured:
                // tests/projects/messages, a caster outside the culling mask is not visible).
                int layer = 1 << renderer.m_GameObject.m_Layer;
                if ((light.cullingMask & layer) == 0 || (context.Camera.cullingMask & layer) == 0) continue;
                // A LOD group casts the shadows of the level the camera draws.
                if (!LODGroup.Drawn(renderer, context.Camera, context.Position)) continue;
                bool seen = culling.SeesCaster(renderer, light);
                if (seen) renderer.MarkRendered();
                else if (Culler.TrustsBounds(renderer)) continue;
                try { renderer.CollectDraws(context, s_Casters); }
                catch (Exception e) { Debug.LogException(e, renderer); }
            }
            // Graphics.DrawMesh draws that cast shadows.
            foreach (var item in s_Queued)
            {
                if (item.Lighting.Cast == ShadowCastingMode.Off || (light.cullingMask & (1 << item.Layer)) == 0) continue;
                s_Casters.Add(item);
            }
            for (int i = 0; i < s_Casters.Count; i++) s_Casters[i].Material.Sync();
        }

        /// <summary>The casters' ShadowCaster passes; a TwoSided caster draws both faces (its
        /// pass's culling off).</summary>
        static void DrawCasters()
        {
            bool twoSided = false;
            foreach (var source in s_Casters)
            {
                if (!ShadowCaster(source.Material, out var material, out int pass)) continue;
                var item = source;
                item.Material = material;
                bool both = CastingOf(item) == ShadowCastingMode.TwoSided;
                if (both != twoSided)
                {
                    twoSided = both;
                    GfxStream.CullOverride(both ? CullMode.Off : (CullMode?)null);
                }
                EmitDraw(item, item.Material.Sync(), pass, false, false);
            }
            if (twoSided) GfxStream.CullOverride(null);
        }

        /// <summary>_LightShadowData: the light's shadow darkness (1 - strength), the far plane over
        /// the shadow distance, and the fade from half to 0.7 of the distance (measured).</summary>
        static Vector4 LightShadowData(Light light, Camera camera)
        {
            float distance = Mathf.Max(Mathf.Min(QualitySettings.shadowDistance, camera.farClipPlane), 1e-4f);
            return new Vector4(1f - light.shadowStrength, camera.farClipPlane / distance, 1f / (0.2f * distance), -2.5f);
        }

        /// <summary>The fade's center: 0.3 of the shadow distance along the view (measured).</summary>
        static void SetShadowFade(Camera camera, CameraContext context)
        {
            float distance = Mathf.Min(QualitySettings.shadowDistance, camera.farClipPlane);
            var fadeCenter = context.Position + context.Forward * (0.3f * distance);
            GfxStream.GlobalVector("unity_ShadowFadeCenterAndType", new Vector4(fadeCenter.x, fadeCenter.y, fadeCenter.z, 1f));
        }

        /// <summary>A spot or point light's shadow, for its ForwardAdd draws.</summary>
        sealed class LightShadow
        {
            internal RenderTexture Map;
            internal Matrix4x4[] WorldToShadow;
            internal Vector4 Data;
            internal bool Soft;
            /// <summary>A point light's: its cube map's projection (_LightPositionRange,
            /// _LightProjectionParams).</summary>
            internal Vector4 PositionRange, ProjectionParams;
            internal bool Cube;
            /// <summary>A directional light's: a screen-space mask (SHADOWS_SCREEN).</summary>
            internal bool Screen;
        }

        static readonly Dictionary<Light, LightShadow> s_LightShadows = new Dictionary<Light, LightShadow>();
        static readonly HashSet<string> s_ShadowWarnings = new HashSet<string>();
        static readonly int s_ShadowMapTextureId = Shader.PropertyToID("_ShadowMapTexture");
        static readonly int s_WorldToShadowId = Shader.PropertyToID("unity_WorldToShadow");
        static readonly int s_LightShadowDataId = Shader.PropertyToID("_LightShadowData");
        static readonly int s_ShadowOffsetsId = Shader.PropertyToID("_ShadowOffsets");
        static readonly int s_LightPositionRangeId = Shader.PropertyToID("_LightPositionRange");
        static readonly int s_LightProjectionParamsId = Shader.PropertyToID("_LightProjectionParams");

        /// <summary>The shadows of the camera's other shadowed lights (directional, spot and
        /// point), drawn before the main light's and the scene.</summary>
        static void RenderAdditionalShadows(Camera camera, CameraContext context, int width, int height)
        {
            s_LightShadows.Clear();
            if (QualitySettings.shadows == ShadowQuality.Disable) return;
            bool any = false;
            foreach (var light in ForwardLights.Lights)
            {
                if (light.shadows == LightShadows.None || light.renderMode == LightRenderMode.ForceVertex || light.shadowStrength <= 0f) continue;
                switch (light.type)
                {
                    case LightType.Spot:
                        RenderSpotShadow(light, camera, context, width, height);
                        any = true;
                        break;
                    case LightType.Point:
                        RenderPointShadow(light, camera, context, width, height);
                        any = true;
                        break;
                    case LightType.Directional:
                        var mask = RenderDirectionalShadow(light, camera, context, width, height);
                        if (mask != null)
                            s_LightShadows[light] = new LightShadow { Map = mask, Data = LightShadowData(light, camera), Screen = true };
                        break;
                    default:
                        if (s_ShadowWarnings.Add(light.type.ToString()))
                            Debug.LogWarning($"Unbound: shadows of {light.type} lights are not drawn by the built-in renderer ('{light.name}' casts none).");
                        break;
                }
            }
            if (any) SetShadowFade(camera, context);
        }

        static void RenderSpotShadow(Light light, Camera camera, CameraContext context, int width, int height)
        {
            var t = light.transform;
            float range = Mathf.Max(light.range, 1e-3f);
            float near = Mathf.Clamp(light.shadowNearPlane, 1e-3f, range * 0.5f);
            // The light's pixel size: its cone's outline on screen.
            float tan = Mathf.Tan(light.spotAngle * 0.5f * Mathf.Deg2Rad);
            var points = new Vector3[17];
            points[0] = t.position;
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                points[i + 1] = t.position + t.rotation * new Vector3(Mathf.Cos(a) * tan * range, Mathf.Sin(a) * tan * range, range);
            }
            int size = ShadowMapSize(light, PixelSize(context, width, height, points));
            var map = Temporary(new RenderTextureDescriptor(size, size, GraphicsFormat.None, GraphicsFormat.D24_UNorm_S8_UInt) { shadowSamplingMode = ShadowSamplingMode.CompareDepths });
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse;
            var projection = Matrix4x4.Perspective(light.spotAngle, 1f, near, range);

            CollectCasters(light, context);
            GfxStream.DepthBias(1f, 1f);
            GfxStream.Keyword("SHADOWS_DEPTH", true);
            SetupCamera(TextureHandles.Of(map), new Rect(0, 0, size, size), CameraClearFlags.Depth, Color.black, view, projection, 0);
            RunLightEvent(camera, light, LightEvent.BeforeShadowMap, map);
            RunLightEvent(camera, light, LightEvent.BeforeShadowMapPass, map, ShadowMapPass.Spotlight);
            // Twice the bias in clip space, no normal offset (measured, shadow_globals_spot_*).
            GfxStream.GlobalVector("unity_LightShadowBias", new Vector4(2f * light.shadowBias, 0f, 0f, 0f));
            var p = t.position;
            GfxStream.GlobalVector("_WorldSpaceLightPos0", new Vector4(p.x, p.y, p.z, 1f));
            DrawCasters();
            RunLightEvent(camera, light, LightEvent.AfterShadowMapPass, map, ShadowMapPass.Spotlight);
            GfxStream.Keyword("SHADOWS_DEPTH", false);
            GfxStream.DepthBias(0f, 0f);
            RunLightEvent(camera, light, LightEvent.AfterShadowMap, map);

            var toTexture = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.5f));
            s_LightShadows[light] = new LightShadow
            {
                Map = map,
                WorldToShadow = new[] { toTexture * projection * view, Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity },
                Data = LightShadowData(light, camera),
                Soft = light.shadows == LightShadows.Soft && QualitySettings.shadows == ShadowQuality.All,
            };
        }

        // The cube faces as OpenGL addresses them: +X, -X, +Y, -Y, +Z, -Z, each looked at with
        // its up vector (the view of a face keeps the world's handedness, so its draws cull the
        // other faces).
        static readonly (Vector3 Forward, Vector3 Up)[] s_CubeFaces =
        {
            (Vector3.right, Vector3.down), (Vector3.left, Vector3.down),
            (Vector3.up, Vector3.forward), (Vector3.down, Vector3.back),
            (Vector3.forward, Vector3.down), (Vector3.back, Vector3.down),
        };

        static void RenderPointShadow(Light light, Camera camera, CameraContext context, int width, int height)
        {
            var p = light.transform.position;
            float range = Mathf.Max(light.range, 1e-3f);
            // The light's pixel size: its sphere's bounds on screen.
            var points = new Vector3[8];
            for (int i = 0; i < 8; i++)
                points[i] = p + new Vector3((i & 1) != 0 ? range : -range, (i & 2) != 0 ? range : -range, (i & 4) != 0 ? range : -range);
            int size = ShadowMapSize(light, PixelSize(context, width, height, points));
            var map = Temporary(new RenderTextureDescriptor(size, size, GraphicsFormat.None, GraphicsFormat.D24_UNorm_S8_UInt)
            {
                dimension = TextureDimension.Cube,
                shadowSamplingMode = ShadowSamplingMode.CompareDepths,
            });
            // Near at the light's shadow near plane, far just past its range (measured,
            // shadow_globals_point_*: _LightProjectionParams).
            float near = Mathf.Clamp(light.shadowNearPlane, 1e-3f, range * 0.5f), far = range * 1.01f;
            var projection = Matrix4x4.Perspective(90f, 1f, near, far);
            ulong handle = TextureHandles.Of(map);

            CollectCasters(light, context);
            GfxStream.DepthBias(1f, 1f);
            GfxStream.Keyword("SHADOWS_CUBE", true);
            GfxStream.GlobalVector("unity_LightShadowBias", new Vector4(2f * light.shadowBias, 0f, 0f, 0f));
            GfxStream.GlobalVector("_WorldSpaceLightPos0", new Vector4(p.x, p.y, p.z, 1f));
            GfxStream.GlobalVector("_LightPositionRange", new Vector4(p.x, p.y, p.z, 1f / range));
            for (int face = 0; face < 6; face++)
            {
                var (f, up) = s_CubeFaces[face];
                var side = Vector3.Cross(f, up).normalized;
                var u = Vector3.Cross(side, f);
                var view = new Matrix4x4(
                    new Vector4(side.x, u.x, -f.x, 0f),
                    new Vector4(side.y, u.y, -f.y, 0f),
                    new Vector4(side.z, u.z, -f.z, 0f),
                    new Vector4(-Vector3.Dot(side, p), -Vector3.Dot(u, p), Vector3.Dot(f, p), 1f));
                SetupCamera(handle, new Rect(0, 0, size, size), CameraClearFlags.Depth, Color.black, view, projection, GfxStream.CameraInvertCulling, face);
                if (face == 0) RunLightEvent(camera, light, LightEvent.BeforeShadowMap, map);
                var facePass = (ShadowMapPass)((int)ShadowMapPass.PointlightPositiveX << face);
                RunLightEvent(camera, light, LightEvent.BeforeShadowMapPass, map, facePass);
                DrawCasters();
                RunLightEvent(camera, light, LightEvent.AfterShadowMapPass, map, facePass);
            }
            GfxStream.Keyword("SHADOWS_CUBE", false);
            GfxStream.DepthBias(0f, 0f);
            RunLightEvent(camera, light, LightEvent.AfterShadowMap, map);

            s_LightShadows[light] = new LightShadow
            {
                Map = map,
                Data = LightShadowData(light, camera),
                Soft = light.shadows == LightShadows.Soft && QualitySettings.shadows == ShadowQuality.All,
                Cube = true,
                PositionRange = new Vector4(p.x, p.y, p.z, 1f / range),
                // The receiver's depth: far/(near-far) and near*far/(near-far), its bias and the
                // 0.97 scale (measured).
                ProjectionParams = new Vector4(far / (near - far), near * far / (near - far), light.shadowBias, 0.97f),
            };
        }

        /// <summary>The larger side, in pixels, of the screen rectangle a light's outline covers
        /// (the whole screen when it reaches behind the camera).</summary>
        static float PixelSize(CameraContext context, int width, int height, Vector3[] points)
        {
            var vp = context.Projection * context.View;
            float minX = 1f, minY = 1f, maxX = -1f, maxY = -1f;
            foreach (var point in points)
            {
                var c = vp * new Vector4(point.x, point.y, point.z, 1f);
                if (c.w <= 1e-4f) return Mathf.Max(width, height);
                minX = Mathf.Min(minX, c.x / c.w); maxX = Mathf.Max(maxX, c.x / c.w);
                minY = Mathf.Min(minY, c.y / c.w); maxY = Mathf.Max(maxY, c.y / c.w);
            }
            float w = (Mathf.Clamp(maxX, -1f, 1f) - Mathf.Clamp(minX, -1f, 1f)) * 0.5f * width;
            float h = (Mathf.Clamp(maxY, -1f, 1f) - Mathf.Clamp(minY, -1f, 1f)) * 0.5f * height;
            return Mathf.Max(0f, Mathf.Max(w, h));
        }

        /// <summary>A ForwardAdd draw's shadow of its light: the light's shadow map and variables,
        /// and the keywords that sample it.</summary>
        static void AddPassShadow(Light light, bool receives, Dictionary<int, object> properties, out string keyword, out string softKeyword)
        {
            keyword = softKeyword = null;
            if (!receives || !s_LightShadows.TryGetValue(light, out var shadow)) return;
            properties[s_ShadowMapTextureId] = shadow.Map;
            properties[s_LightShadowDataId] = shadow.Data;
            if (shadow.Screen)
            {
                keyword = "SHADOWS_SCREEN";
                return;
            }
            if (shadow.Cube)
            {
                properties[s_LightPositionRangeId] = shadow.PositionRange;
                properties[s_LightProjectionParamsId] = shadow.ProjectionParams;
                keyword = "SHADOWS_CUBE";
                if (shadow.Soft) softKeyword = "SHADOWS_SOFT";
                return;
            }
            properties[s_WorldToShadowId] = shadow.WorldToShadow;
            keyword = "SHADOWS_DEPTH";
            if (!shadow.Soft) return;
            float h = 0.5f / shadow.Map.width;
            properties[s_ShadowOffsetsId] = new[] { new Vector4(-h, -h, 0f, 0f), new Vector4(h, -h, 0f, 0f), new Vector4(-h, h, 0f, 0f), new Vector4(h, h, 0f, 0f) };
            softKeyword = "SHADOWS_SOFT";
        }

        static readonly List<CommandBuffer> s_LightBuffers = new List<CommandBuffer>();

        /// <summary>Runs a light's command buffers for an event (and shadow map pass), with
        /// <paramref name="active"/> the current target (BuiltinRenderTextureType.CurrentActive).</summary>
        static void RunLightEvent(Camera camera, Light light, LightEvent evt, RenderTexture active, ShadowMapPass pass = ShadowMapPass.All)
        {
            if (light.commandBufferCount == 0) return;
            light.CommandBuffersAt(evt, pass, s_LightBuffers);
            if (s_LightBuffers.Count == 0) return;
            var saved = GfxStream.Current;
            var previous = RenderTexture.active;
            RenderTexture.SetActiveQuietly(active);
            foreach (var buffer in s_LightBuffers.ToArray())
            {
                var context = new CommandBuffer.Context { Camera = camera, CameraTarget = camera.targetTexture };
                try { buffer.ExecuteIn(context); }
                catch (Exception e) { Debug.LogException(e); }
            }
            GfxStream.Restore(saved);
            RenderTexture.SetActiveQuietly(previous);
        }

        static RenderTexture Temporary(RenderTextureDescriptor descriptor)
        {
            var rt = RenderTexture.GetTemporary(descriptor);
            s_ShadowTemporaries.Add(rt);
            return rt;
        }

        static void ReleaseShadowTemporaries()
        {
            if (s_CameraDepth != null || s_DepthNormalsDrawn) UnbindCameraTextures();
            s_DepthNormalsDrawn = false;
            foreach (var rt in s_ShadowTemporaries) RenderTexture.ReleaseTemporary(rt);
            s_ShadowTemporaries.Clear();
            s_LightShadows.Clear();
            s_CameraDepth = null;
            s_ScreenShadows = false;
        }
    }
}
