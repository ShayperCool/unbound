using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// Halos and lens flares of the built-in pipeline, drawn with Unity's Hidden/Internal-Halo and
    /// Hidden/Internal-Flare shaders after OnPostRender, between the camera's
    /// BeforeHaloAndLensFlares and AfterHaloAndLensFlares events. Everything here is measured in the
    /// Editor (tests/projects/flares):
    /// <list type="bullet">
    /// <item>Halos draw with or without a FlareLayer, flares only on cameras with an enabled
    /// FlareLayer. A camera command buffer that clears the target's color at AfterForwardAlpha or
    /// BeforeHaloAndLensFlares hides the halos (not the flares); empty buffers and buffers setting a
    /// global float do not (see <see cref="HalosHidden"/>).</item>
    /// <item>A halo is a square fan of four triangles facing the camera (its right and up axes),
    /// half as wide as the halo's size (a Halo component's size, a light's range times
    /// <c>RenderSettings.haloStrength</c>), its center vertex a third of that size nearer the
    /// camera, sampling <c>_HaloFalloff</c>: the scene's halo texture, else a 64x64 alpha texture
    /// of <c>1 - r²</c>.</item>
    /// <item>A flare's elements are quads on the line from the flare's point on screen through the
    /// screen's center, at <c>position</c> along it (0 the flare, 1 the center), half
    /// <c>size * 0.0100354</c> of the projection's vertical scale tall (constant on screen with a
    /// perspective camera), rotated to face the center (corners at 1.4 half sizes) and scaled by
    /// the visibility when asked.</item>
    /// <item>A flare fades in at its fade speed per second (a LensFlare's own, a light's
    /// <c>RenderSettings.flareFadeSpeed</c>) while its point is on screen and no collider is
    /// between it and the camera (triggers too; layers the LensFlare ignores do not count), and
    /// out at half that speed; disabling it starts it over.</item>
    /// <item>Colors become 8-bit sRGB vertex colors that a linear project decodes again.</item>
    /// </list>
    /// What was not measured is drawn as above with a warning, once per condition (see
    /// <see cref="Unmeasured"/>): the Gamma color space, a halo or flare on a layer the camera's
    /// cullingMask excludes, a camera with a replacement shader, a cube map face render, and one
    /// flare drawn by several cameras in a frame.
    /// </summary>
    internal static class Halos
    {
        /// <summary>The element size scale (half height in projection units per size unit).</summary>
        const float ElementScale = 0.0100354f;
        /// <summary>A rotated element's corner distance in half sizes.</summary>
        const float RotatedCorner = 1.4f;

        static readonly List<Halo> s_Halos = new List<Halo>();
        static readonly List<LensFlare> s_LensFlares = new List<LensFlare>();
        static readonly Dictionary<(UnityEngine.Object, Camera), Fade> s_Fades = new Dictionary<(UnityEngine.Object, Camera), Fade>();
        static readonly List<(UnityEngine.Object, Camera)> s_Stale = new List<(UnityEngine.Object, Camera)>();
        static readonly int s_FalloffId = Shader.PropertyToID("_HaloFalloff");
        static readonly int s_FalloffStId = Shader.PropertyToID("_HaloFalloff_ST");
        static readonly int s_FlareTextureId = Shader.PropertyToID("_FlareTexture");
        static readonly int s_FlareTextureStId = Shader.PropertyToID("_FlareTexture_ST");
        static readonly Dictionary<int, object> s_FlareProperties = new Dictionary<int, object>();
        static readonly Dictionary<int, object> s_HaloProperties = new Dictionary<int, object>();
        static Texture2D s_Falloff;
        static Material s_HaloMaterial, s_FlareMaterial;
        static Mesh s_HaloMesh, s_FlareMesh;
        static readonly List<Vector3> s_Vertices = new List<Vector3>();
        static readonly List<Vector2> s_Uvs = new List<Vector2>();
        static readonly List<Color32> s_Colors = new List<Color32>();
        static readonly List<int> s_Indices = new List<int>();

        /// <summary>The flares drawn in frame <see cref="s_DrawnFrame"/> and the camera that drew
        /// each first.</summary>
        static readonly Dictionary<UnityEngine.Object, Camera> s_DrawnBy = new Dictionary<UnityEngine.Object, Camera>();
        static int s_DrawnFrame = -1;
        static bool s_WarnedGamma;

        /// <summary>A cube map face render is in progress (RenderToCubemap, a realtime reflection
        /// probe).</summary>
        internal static int CubeFaces;

        /// <summary>Warns once per condition that halos or flares are drawn where the Editor was not
        /// measured, naming what Unbound does (tests/projects/flares; docs/COMPATIBILITY_GAPS.md).</summary>
        static void Unmeasured(string condition, string applied) =>
            Stubs.Ignored("Halo/LensFlare (" + condition + ")", "is not measured against the Editor: " + applied);

        sealed class Fade
        {
            internal float Visibility;
            internal int Frame = -1;
            internal bool Seen;
        }

        internal static void Add(Halo halo) { if (!s_Halos.Contains(halo)) s_Halos.Add(halo); }
        internal static void Remove(Halo halo) => s_Halos.Remove(halo);
        internal static void Add(LensFlare flare) { if (!s_LensFlares.Contains(flare)) s_LensFlares.Add(flare); }

        internal static void Remove(LensFlare flare)
        {
            s_LensFlares.Remove(flare);
            Forget(flare);
        }

        static void Forget(UnityEngine.Object source)
        {
            s_Stale.Clear();
            foreach (var key in s_Fades.Keys)
                if (ReferenceEquals(key.Item1, source)) s_Stale.Add(key);
            foreach (var key in s_Stale) s_Fades.Remove(key);
        }

        /// <summary>The built-in halo falloff: 64x64 alpha, <c>round(255 * max(0, 1 - r²))</c> with r the
        /// distance of the texel's center from the middle over 32, filtered bilinearly, clamped.</summary>
        internal static Texture2D Falloff
        {
            get
            {
                if (s_Falloff != null) return s_Falloff;
                const int size = 64;
                var alpha = new byte[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f - 32f) / 32f, dy = (y + 0.5f - 32f) / 32f;
                        float v = Math.Max(0f, 1f - (dx * dx + dy * dy));
                        alpha[y * size + x] = (byte)(int)(v * 255f + 0.5f);
                    }
                using (ObjectRegistry.AsAssets())
                {
                    s_Falloff = new Texture2D(size, size, TextureFormat.Alpha8, false, true)
                    {
                        name = "UnityHalo",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    s_Falloff.SetPixelData(alpha, 0);
                    s_Falloff.Apply(false, false);
                }
                return s_Falloff;
            }
        }

        /// <summary>The global <c>_HaloFalloff</c>: the scene's halo texture, else the built-in falloff
        /// (set when the scene's RenderSettings take effect).</summary>
        internal static void UseHaloTexture(Texture texture) => Shader.SetGlobalTexture(s_FalloffId, texture != null ? texture : Falloff);

        /// <summary>A gamma (sRGB) value as the 8-bit vertex color the shaders receive: a linear
        /// project decodes the byte to linear and stores that again in 8 bits.</summary>
        static byte VertexByte(float gamma)
        {
            int g = (int)(Mathf.Clamp01(gamma) * 255f + 0.5f);
            if (QualitySettings.activeColorSpace != ColorSpace.Linear)
            {
                if (!s_WarnedGamma)
                {
                    s_WarnedGamma = true;
                    Unmeasured("Gamma color space", "the 8-bit sRGB colors are drawn as they are");
                }
                return (byte)g;
            }
            return (byte)(int)(Mathf.GammaToLinearSpace(g / 255f) * 255f + 0.5f);
        }

        static Color32 VertexColor(float r, float g, float b) => new Color32(VertexByte(r), VertexByte(g), VertexByte(b), 255);

        static Mesh NewMesh(string name)
        {
            using (ObjectRegistry.AsAssets())
                return new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
        }

        /// <summary>The halos and, with a FlareLayer, the lens flares of a camera, into its current
        /// target with its matrices.</summary>
        internal static void Render(Camera camera, in Matrix4x4 view, in Matrix4x4 projection, Rect viewport)
        {
            bool drawn = DrawHalos(camera, view);
            var layer = camera.GetComponent<FlareLayer>();
            if (layer != null && layer.enabled) drawn |= DrawFlares(camera, view, projection, viewport);
            else ForgetCamera(camera);
            if (!drawn) return;
            if (camera.m_ReplacementShader != null)
                Unmeasured("camera with a replacement shader", "they are drawn with their own shaders");
            if (CubeFaces > 0)
                Unmeasured("cube map face render", "they are drawn into every face as into a camera's target");
        }

        /// <summary>Warns when the camera's cullingMask excludes the layer of a halo or flare it
        /// draws.</summary>
        static void CheckLayer(Camera camera, Component source, string what)
        {
            if ((camera.cullingMask & (1 << source.gameObject.layer)) == 0)
                Unmeasured(what + " on a layer the camera's cullingMask excludes", "it is drawn anyway");
        }

        static bool DrawHalos(Camera camera, in Matrix4x4 view)
        {
            s_Vertices.Clear(); s_Uvs.Clear(); s_Colors.Clear(); s_Indices.Clear();
            var cameraToWorld = view.inverse;
            Vector3 right = cameraToWorld.GetColumn(0), up = cameraToWorld.GetColumn(1), forward = -(Vector3)cameraToWorld.GetColumn(2);
            right.Normalize(); up.Normalize(); forward.Normalize();
            foreach (var halo in s_Halos)
            {
                if (halo == null || !halo.isActiveAndEnabled || halo.m_Size <= 0f) continue;
                // A Halo component's color is its own (its transform's scale does not matter).
                var c = halo.m_Color;
                CheckLayer(camera, halo, "halo");
                AddHalo(halo.transform.position, halo.m_Size, VertexColor(c.r, c.g, c.b), right, up, forward);
            }
            float strength = RenderSettings.haloStrength;
            float scale = Mathf.LinearToGammaSpace(Mathf.Max(0f, strength));
            foreach (var light in Light.s_Enabled)
            {
                if (light == null || !light.m_DrawHalo || !light.isActiveAndEnabled) continue;
                float size = light.range * strength;
                if (size <= 0f) continue;
                // The light's sRGB color times its intensity and the strength's sRGB encoding.
                var c = light.color;
                CheckLayer(camera, light, "halo");
                float k = light.intensity * scale;
                AddHalo(light.transform.position, size, VertexColor(c.r * k, c.g * k, c.b * k), right, up, forward);
            }
            if (s_Vertices.Count == 0 || HalosHidden(camera)) return false;
            s_HaloMesh ??= NewMesh("Unbound Halos");
            s_HaloMesh.Clear();
            s_HaloMesh.SetVertices(s_Vertices);
            s_HaloMesh.SetUVs(0, s_Uvs);
            s_HaloMesh.SetColors(s_Colors);
            s_HaloMesh.SetTriangles(s_Indices, 0);
            s_HaloMaterial ??= new Material(Shader.Find("Hidden/Internal-Halo")) { hideFlags = HideFlags.HideAndDontSave };
            // The global _HaloFalloff (the scene's halo texture, the built-in falloff, or what a
            // script set), bound with the draw.
            s_HaloProperties[s_FalloffId] = Shader.GetGlobalTexture(s_FalloffId) ?? Falloff;
            // The shader declares no properties: its scale and offset come with the draw.
            s_HaloProperties[s_FalloffStId] = new Vector4(1f, 1f, 0f, 0f);
            GfxStream.Draw(s_HaloMesh.Sync(), 0, s_HaloMaterial.Sync(), 0, Matrix4x4.identity, Color.white, properties: s_HaloProperties);
            return true;
        }

        /// <summary>The camera events whose command buffers run after the halos.</summary>
        static bool AfterHalos(CameraEvent e) => e == CameraEvent.AfterHaloAndLensFlares || e == CameraEvent.BeforeImageEffects
            || e == CameraEvent.AfterImageEffects || e == CameraEvent.AfterEverything;

        static readonly CameraEvent[] s_Events = (CameraEvent[])Enum.GetValues(typeof(CameraEvent));

        /// <summary>Whether the camera's command buffers hide its halos. Measured
        /// (tests/projects/flares ho.*, hb.*): a buffer clearing only the color at AfterForwardAlpha or
        /// BeforeHaloAndLensFlares hides them; an empty buffer (at BeforeForwardOpaque,
        /// AfterForwardAlpha) or one setting a global float (at BeforeImageEffects) does not. A
        /// buffer running after the halos cannot change them. Any other buffer before the halos is
        /// not measured: it warns once per camera, buffer and event, and hides them if it clears,
        /// else not.</summary>
        internal static bool HalosHidden(Camera camera)
        {
            if (camera.commandBufferCount == 0) return false;
            bool hidden = false;
            foreach (var e in s_Events)
            {
                if (AfterHalos(e)) continue;
                var buffers = camera.CommandBuffersAt(e);
                if (buffers == null) continue;
                foreach (var buffer in buffers)
                {
                    bool clears = buffer.ColorClears + buffer.OtherClears > 0;
                    bool measured = clears
                        ? buffer.OtherClears == 0 && buffer.CommandCount == buffer.ColorClears
                            && (e == CameraEvent.AfterForwardAlpha || e == CameraEvent.BeforeHaloAndLensFlares)
                        : buffer.CommandCount == buffer.GlobalFloats;
                    if (!measured)
                        Stubs.Ignored("Halo (command buffer '" + buffer.name + "' at " + e + " of camera '" + camera.name + "' #" + camera.GetInstanceID() + ")",
                            "is not measured against the Editor: halos are " + (clears ? "hidden, as by a buffer clearing the color before them" : "drawn, as with a buffer that does not clear"));
                    hidden |= clears;
                }
            }
            return hidden;
        }

        static void AddHalo(Vector3 center, float size, Color32 color, Vector3 right, Vector3 up, Vector3 forward)
        {
            int start = s_Vertices.Count;
            s_Vertices.Add(center - forward * (size / 3f));
            s_Uvs.Add(new Vector2(0.5f, 0.5f));
            s_Vertices.Add(center - right * size - up * size); s_Uvs.Add(new Vector2(0f, 0f));
            s_Vertices.Add(center + right * size - up * size); s_Uvs.Add(new Vector2(1f, 0f));
            s_Vertices.Add(center + right * size + up * size); s_Uvs.Add(new Vector2(1f, 1f));
            s_Vertices.Add(center - right * size + up * size); s_Uvs.Add(new Vector2(0f, 1f));
            for (int i = 0; i < 5; i++) s_Colors.Add(color);
            for (int i = 0; i < 4; i++)
            {
                s_Indices.Add(start);
                s_Indices.Add(start + 1 + i);
                s_Indices.Add(start + 1 + (i + 1) % 4);
            }
        }

        /// <summary>One flare to draw: where it is, its asset, the sRGB color its light-colored
        /// elements take, its brightness and fade speeds, and the layers its occlusion test
        /// ignores.</summary>
        struct Source
        {
            internal UnityEngine.Object Key;
            internal Flare Asset;
            internal Vector3 Point;
            internal bool Directional;
            internal Vector3 LightColor;
            internal float Brightness, FadeIn, FadeOut;
            internal int IgnoreLayers;
        }

        static readonly List<Source> s_Sources = new List<Source>();

        static void CollectSources()
        {
            s_Sources.Clear();
            foreach (var lens in s_LensFlares)
            {
                if (lens == null || !lens.isActiveAndEnabled || lens.Asset == null) continue;
                var t = lens.transform;
                var tint = lens.Tint;
                s_Sources.Add(new Source
                {
                    Key = lens, Asset = lens.Asset,
                    // A directional flare is infinitely far, opposite its forward axis.
                    Point = lens.Directional ? -t.forward : t.position, Directional = lens.Directional,
                    LightColor = new Vector3(tint.r, tint.g, tint.b),
                    Brightness = lens.Brightness, FadeIn = lens.FadeSpeed, FadeOut = lens.FadeSpeed * 0.5f,
                    IgnoreLayers = lens.IgnoreLayers,
                });
            }
            foreach (var light in Light.s_Enabled)
            {
                if (light == null || !light.isActiveAndEnabled || light.flare == null) continue;
                var t = light.transform;
                bool directional = light.type == LightType.Directional;
                var c = light.color * light.intensity;
                s_Sources.Add(new Source
                {
                    Key = light, Asset = light.flare,
                    Point = directional ? -t.forward : t.position, Directional = directional,
                    LightColor = new Vector3(c.r, c.g, c.b),
                    // A light's flares take the scene's flare strength as their brightness.
                    Brightness = RenderSettings.flareStrength, FadeIn = RenderSettings.flareFadeSpeed, FadeOut = RenderSettings.flareFadeSpeed * 0.5f,
                });
            }
        }

        static void ForgetCamera(Camera camera)
        {
            s_Stale.Clear();
            foreach (var key in s_Fades.Keys)
                if (key.Item2 == camera) s_Stale.Add(key);
            foreach (var key in s_Stale) s_Fades.Remove(key);
        }

        static bool DrawFlares(Camera camera, in Matrix4x4 view, in Matrix4x4 projection, Rect viewport)
        {
            CollectSources();
            foreach (var fade in s_Fades) if (fade.Key.Item2 == camera) fade.Value.Seen = false;
            float width = viewport.width, height = viewport.height;
            if (width <= 0f || height <= 0f) return false;
            if (s_DrawnFrame != Time.frameCount)
            {
                s_DrawnFrame = Time.frameCount;
                s_DrawnBy.Clear();
            }
            bool drawn = false;
            var center = new Vector2(width * 0.5f, height * 0.5f);
            var cameraPosition = view.inverse.MultiplyPoint3x4(Vector3.zero);
            bool orthographic = projection.m33 == 1f;
            var saved = GfxStream.Current;
            bool matrices = false;
            foreach (var source in s_Sources)
            {
                var viewPoint = source.Directional ? view.MultiplyVector(source.Point) : view.MultiplyPoint3x4(source.Point);
                var clip = projection * new Vector4(viewPoint.x, viewPoint.y, viewPoint.z, source.Directional ? 0f : 1f);
                bool front = clip.w > 0f;
                var ndc = front ? new Vector2(clip.x / clip.w, clip.y / clip.w) : Vector2.zero;
                bool visible = front && Mathf.Abs(ndc.x) <= 1f && Mathf.Abs(ndc.y) <= 1f && !Occluded(cameraPosition, source);
                if (!s_Fades.TryGetValue((source.Key, camera), out var fade))
                    s_Fades[(source.Key, camera)] = fade = new Fade();
                fade.Seen = true;
                if (fade.Frame != Time.frameCount)
                {
                    fade.Frame = Time.frameCount;
                    fade.Visibility = visible
                        ? Mathf.Min(1f, fade.Visibility + Time.deltaTime * source.FadeIn)
                        : Mathf.Max(0f, fade.Visibility - Time.deltaTime * source.FadeOut);
                }
                if (fade.Visibility <= 0f || !front) continue;
                var screen = new Vector2((ndc.x * 0.5f + 0.5f) * width, (ndc.y * 0.5f + 0.5f) * height);
                // On screen the elements keep their size with a perspective camera; an orthographic
                // camera scales them by the flare's depth.
                float unit = ElementScale * projection.m11 * height * 0.5f * (orthographic ? (source.Directional ? camera.farClipPlane : -viewPoint.z) : 1f);
                if (!BuildFlare(source, fade.Visibility, screen, center, unit)) continue;
                CheckLayer(camera, (Component)source.Key, "lens flare");
                // Each camera fades its flares on its own; a flare drawn by a second camera in a
                // frame was not measured.
                if (!s_DrawnBy.TryGetValue(source.Key, out var first)) s_DrawnBy[source.Key] = camera;
                else if (first != camera) Unmeasured("lens flare drawn by several cameras in a frame", "each camera fades it on its own");
                drawn = true;
                if (!matrices)
                {
                    GfxStream.Camera(Matrix4x4.identity, Matrix4x4.Ortho(0f, width, 0f, height, -1f, 1f), saved.Flags);
                    matrices = true;
                }
                s_FlareMesh ??= NewMesh("Unbound Lens Flare");
                s_FlareMesh.Clear();
                s_FlareMesh.SetVertices(s_Vertices);
                s_FlareMesh.SetUVs(0, s_Uvs);
                s_FlareMesh.SetColors(s_Colors);
                s_FlareMesh.SetTriangles(s_Indices, 0);
                s_FlareMaterial ??= new Material(Shader.Find("Hidden/Internal-Flare")) { hideFlags = HideFlags.HideAndDontSave };
                s_FlareProperties.Clear();
                s_FlareProperties[s_FlareTextureId] = source.Asset.m_FlareTexture;
                s_FlareProperties[s_FlareTextureStId] = new Vector4(1f, 1f, 0f, 0f);
                GfxStream.Draw(s_FlareMesh.Sync(), 0, s_FlareMaterial.Sync(), 0, Matrix4x4.identity, Color.white, properties: s_FlareProperties);
            }
            if (matrices) GfxStream.Camera(saved.View, saved.Projection, saved.Flags);
            // A flare that is no longer drawn (disabled, without an asset) starts over.
            s_Stale.Clear();
            foreach (var fade in s_Fades)
                if (fade.Key.Item2 == camera && !fade.Value.Seen) s_Stale.Add(fade.Key);
            foreach (var key in s_Stale) s_Fades.Remove(key);
            return drawn;
        }

        /// <summary>A collider between the camera and the flare (triggers too; for a directional
        /// flare, anywhere along its direction) hides it.</summary>
        static bool Occluded(Vector3 cameraPosition, in Source source)
        {
            Vector3 direction;
            float distance;
            if (source.Directional)
            {
                direction = source.Point.normalized;
                distance = Mathf.Infinity;
            }
            else
            {
                direction = source.Point - cameraPosition;
                distance = direction.magnitude;
                if (distance <= 0f) return false;
                direction /= distance;
            }
            return Physics.Raycast(cameraPosition, direction, out _, distance, ~source.IgnoreLayers, QueryTriggerInteraction.Collide);
        }

        /// <summary>The flare's element quads, in viewport pixels.</summary>
        static bool BuildFlare(in Source source, float visibility, Vector2 screen, Vector2 center, float unit)
        {
            s_Vertices.Clear(); s_Uvs.Clear(); s_Colors.Clear(); s_Indices.Clear();
            var flare = source.Asset;
            var texture = flare.m_FlareTexture;
            if (texture == null) return false;
            var offset = screen - center;
            float angle = offset.sqrMagnitude > 0f ? Mathf.Atan2(offset.y, offset.x) : 0f;
            foreach (var element in flare.m_Elements)
            {
                var color = ElementColor(element, source, visibility);
                if (color.r == 0 && color.g == 0 && color.b == 0) continue;
                var position = screen + (center - screen) * element.Position;
                float half = element.Size * unit * (element.Zoom ? visibility : 1f);
                if (half <= 0f) continue;
                var rect = ImageRect(flare.m_TextureLayout, element.ImageIndex, texture.width, texture.height);
                int start = s_Vertices.Count;
                if (element.Rotate)
                {
                    // A diamond whose top-right corner points away from the screen's center.
                    float r = half * RotatedCorner;
                    for (int k = 0; k < 4; k++)
                    {
                        // Corners in uv order (0,0), (1,0), (1,1), (0,1): at angle + 180, 270, 0 and 90 degrees.
                        float a = angle + Mathf.PI * (k == 0 ? 1f : k == 1 ? 1.5f : k == 2 ? 0f : 0.5f);
                        s_Vertices.Add(new Vector3(position.x + r * Mathf.Cos(a), position.y + r * Mathf.Sin(a), 0f));
                    }
                }
                else
                {
                    s_Vertices.Add(new Vector3(position.x - half, position.y - half, 0f));
                    s_Vertices.Add(new Vector3(position.x + half, position.y - half, 0f));
                    s_Vertices.Add(new Vector3(position.x + half, position.y + half, 0f));
                    s_Vertices.Add(new Vector3(position.x - half, position.y + half, 0f));
                }
                s_Uvs.Add(new Vector2(rect.xMin, rect.yMin));
                s_Uvs.Add(new Vector2(rect.xMax, rect.yMin));
                s_Uvs.Add(new Vector2(rect.xMax, rect.yMax));
                s_Uvs.Add(new Vector2(rect.xMin, rect.yMax));
                for (int k = 0; k < 4; k++) s_Colors.Add(color);
                s_Indices.Add(start); s_Indices.Add(start + 1); s_Indices.Add(start + 2);
                s_Indices.Add(start); s_Indices.Add(start + 2); s_Indices.Add(start + 3);
            }
            return s_Vertices.Count > 0;
        }

        /// <summary>An element's color, in sRGB: its own, times the flare's light color (a
        /// LensFlare's color, a light's color times its intensity) when it uses it, times the sRGB
        /// encoding of the brightness and visibility, which a light-colored element that does not
        /// fade ignores.</summary>
        static Color32 ElementColor(in Flare.Element element, in Source source, float visibility)
        {
            var c = element.Color;
            var gamma = new Vector3(c.r, c.g, c.b);
            if (element.UseLightColor) gamma = Vector3.Scale(gamma, source.LightColor);
            if (element.Fade || !element.UseLightColor) gamma *= Mathf.LinearToGammaSpace(Mathf.Max(0f, source.Brightness * visibility));
            return VertexColor(gamma.x, gamma.y, gamma.z);
        }

        /// <summary>The part of the flare texture an image of a layout covers, in uv, inset by half a
        /// texel times the part of the texture outside it (layouts: 0 one large and four small, 1 one
        /// large, two medium and eight small, 2 the whole texture, 3-5 grids of 2, 3 and 4 images a
        /// row, from the top left). Images past a layout's count continue its last pattern.</summary>
        internal static Rect ImageRect(int layout, int image, int textureWidth, int textureHeight)
        {
            float x, y, w, h;
            switch (layout)
            {
                case 0:
                    if (image == 0) { x = 0f; y = 0.5f; w = 1f; h = 0.5f; }
                    else { int i = image - 1; x = (i % 2) * 0.5f; y = 0.5f - (i / 2 + 1) * 0.25f; w = 0.5f; h = 0.25f; }
                    break;
                case 1:
                    if (image == 0) { x = 0f; y = 0.5f; w = 1f; h = 0.5f; }
                    else if (image <= 2) { x = 0f; y = image == 1 ? 0.25f : 0f; w = 0.5f; h = 0.25f; }
                    else { int i = image - 3; x = 0.5f + (i % 2) * 0.25f; y = 0.5f - (i / 2 + 1) * 0.125f; w = 0.25f; h = 0.125f; }
                    break;
                case 3:
                case 4:
                case 5:
                {
                    int n = layout - 1;
                    x = (float)(image % n) / n; y = 1f - (float)(image / n + 1) / n; w = 1f / n; h = 1f / n;
                    break;
                }
                default:
                    x = 0f; y = 0f; w = 1f; h = 1f;
                    break;
            }
            float tw = Math.Max(1, textureWidth), th = Math.Max(1, textureHeight);
            float insetU = 0.5f * (1f - w) / tw, insetV = 0.5f * (1f - h) / th;
            float xMin = x + insetU, xMax = x + w - insetU, yMin = y + insetV, yMax = y + h - insetV;
            if (layout == 1 && image <= 2)
            {
                // The large and medium images reach 0.47 texel further left and 0.46 texel further up.
                xMin -= 0.47f / tw;
                yMax += 0.46f / th;
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
