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
    /// <item>A flare's elements are quads in view space at the flare's depth, on the line from
    /// the flare's point to the view axis at <c>position</c> along it (0 the flare, 1 the axis),
    /// half <c>size * 0.01</c> of that depth tall (so constant on screen with a perspective
    /// camera), rotated to point away from the axis (corners at 1.4 half sides) and scaled by
    /// the brightness and visibility when asked; a directional flare sits at twice the near
    /// plane's distance (see <see cref="BuildFlare"/>).</item>
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
        /// <summary>An element's half side in view units, per unit of its size and of the
        /// flare's depth.</summary>
        const float ElementScale = 0.01f;
        /// <summary>A rotated element's corner distance in half sides.</summary>
        const float RotatedCorner = 1.4f;
        /// <summary>The distance from the view axis under which a flare has no direction of its
        /// own: its rotated elements then point along +x (measured on the axis itself,
        /// tests/projects/flares `f.Rot20.3`; the threshold is Unbound's choice).</summary>
        const float AxisEpsilon = 1e-5f;

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
        /// <summary>The vertices and uvs the last build left (four per element).</summary>
        internal static IReadOnlyList<Vector3> BuiltVertices => s_Vertices;
        internal static IReadOnlyList<Vector2> BuiltUvs => s_Uvs;
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
        internal struct Source
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
            var cameraPosition = view.inverse.MultiplyPoint3x4(Vector3.zero);
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
                if (!BuildFlare(source, fade.Visibility, FlarePoint(view, source, camera.nearClipPlane))) continue;
                CheckLayer(camera, (Component)source.Key, "lens flare");
                // Each camera fades its flares on its own; a flare drawn by a second camera in a
                // frame was not measured.
                if (!s_DrawnBy.TryGetValue(source.Key, out var first)) s_DrawnBy[source.Key] = camera;
                else if (first != camera) Unmeasured("lens flare drawn by several cameras in a frame", "each camera fades it on its own");
                drawn = true;
                if (!matrices)
                {
                    // The quads are in view space: the camera's projection alone places them.
                    GfxStream.Camera(Matrix4x4.identity, projection, saved.Flags);
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

        /// <summary>
        /// The flare's place in view space: a positioned flare's point through the view matrix,
        /// a directional one's direction at twice the near plane's distance (rotated only). The
        /// order of the sums is part of the result: it decides the last bit of an element's
        /// edge, and with it which pixels an edge on pixel centres covers. This order
        /// reproduces every flare image of the Unity 6000.0.71f1 Editor (tests/projects/flares).
        /// </summary>
        internal static Vector3 FlarePoint(in Matrix4x4 view, in Source source, float near)
        {
            var q = source.Point;
            if (!source.Directional)
                return new Vector3(
                    q.z * view.m02 + q.y * view.m01 + view.m00 * q.x + view.m03,
                    q.z * view.m12 + q.y * view.m11 + view.m10 * q.x + view.m13,
                    q.z * view.m22 + q.y * view.m21 + q.x * view.m20 + view.m23);
            // Twice the near plane's distance: within 60 degrees of the view axis it is in view.
            float far = near + near;
            float x = q.x * far, y = q.y * far, z = far * q.z;
            return new Vector3(
                z * view.m02 + y * view.m01 + view.m00 * x,
                z * view.m12 + y * view.m11 + view.m10 * x,
                view.m20 * x + y * view.m21 + z * view.m22);
        }

        /// <summary>
        /// The flare's element quads, in view space at the flare's depth (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/flares): an element sits on the line from
        /// the flare's point to the view axis at its position; its half side is its size times
        /// the depth times <see cref="ElementScale"/> (a zooming element's also times the
        /// visibility), so a perspective camera shows it at one size whatever the depth and an
        /// orthographic one scales it by the depth; a rotating element is a diamond
        /// <see cref="RotatedCorner"/> half sides wide whose first corner points away from the
        /// axis. Being at the flare's depth, the quads are clipped by the camera's planes as
        /// the Editor's are: a flare beyond the far plane or nearer than the near plane is not
        /// drawn, nor is a directional one more than 60 degrees off the view axis (`fl.*`). The order of the products and sums matters to the last bit, as in
        /// <see cref="FlarePoint"/>.
        /// </summary>
        internal static bool BuildFlare(in Source source, float visibility, Vector3 point)
        {
            s_Vertices.Clear(); s_Uvs.Clear(); s_Colors.Clear(); s_Indices.Clear();
            var flare = source.Asset;
            var texture = flare.m_FlareTexture;
            if (texture == null) return false;
            float distance = Mathf.Sqrt(point.y * point.y + point.x * point.x);
            float awayX = 1f, awayY = 0f;
            if (distance > AxisEpsilon)
            {
                awayX = point.x / distance;
                awayY = point.y / distance;
            }
            float halfTexelU = (1f / Math.Max(1, texture.width)) * 0.5f, halfTexelV = (1f / Math.Max(1, texture.height)) * 0.5f;
            foreach (var element in flare.m_Elements)
            {
                var color = ElementColor(element, source, visibility);
                if (color.r == 0 && color.g == 0 && color.b == 0) continue;
                float toAxis = element.Position, toPoint = 1f - toAxis;
                float depth = toPoint * point.z + point.z * toAxis;
                float x = point.x * toPoint + toAxis * 0f, y = point.y * toPoint + toAxis * 0f;
                float half = element.Size * point.z * (element.Zoom ? ElementScale * (source.Brightness * visibility) : ElementScale);
                // In front of the camera a sized element's half side is negative. Anything else
                // (no size, a negative one, a point behind an orthographic camera) is not
                // measured and not drawn.
                if (!(half < 0f)) continue;
                float halfX = half, halfY = half;
                if (element.Rotate)
                {
                    float corner = half * RotatedCorner;
                    halfX = corner * awayX;
                    halfY = corner * awayY;
                }
                ImageUv(flare.m_TextureLayout, element.ImageIndex, halfTexelU, halfTexelV, out float uMin, out float uMax, out float vMin, out float vMax);
                int start = s_Vertices.Count;
                // In front of the camera the depth, and with it the half side, is negative: the
                // first corner is then the one away from the axis (the top right of an upright
                // element) and takes the image's top right.
                s_Vertices.Add(new Vector3(x - halfX, y - halfY, depth));
                s_Vertices.Add(new Vector3(x + halfY, y - halfX, depth));
                s_Vertices.Add(new Vector3(x + halfX, y + halfY, depth));
                s_Vertices.Add(new Vector3(x - halfY, y + halfX, depth));
                s_Uvs.Add(new Vector2(uMax, vMax));
                s_Uvs.Add(new Vector2(uMin, vMax));
                s_Uvs.Add(new Vector2(uMin, vMin));
                s_Uvs.Add(new Vector2(uMax, vMin));
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

        /// <summary>
        /// The part of the flare texture an image of a layout covers, in uv (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/flares `f.L*`): the image's cell
        /// with half a texel taken off each side, measured from the top. Layouts: 0 one large
        /// image over four small ones, 1 one large, two medium and eight small, 2 the whole
        /// texture, 3 to 5 grids of 2, 3 and 4 images a row. The large and medium images of
        /// layout 1 keep their whole left and top sides. Images past a layout's count continue
        /// its last pattern.
        /// </summary>
        internal static void ImageUv(int layout, int image, float halfTexelU, float halfTexelV, out float uMin, out float uMax, out float vMin, out float vMax)
        {
            // The top and the bottom of the image, counted down from the top of the texture.
            float top, bottom;
            if (layout == 1)
            {
                if (image <= 2)
                {
                    uMin = 0f;
                    uMax = (image == 0 ? 1f : 0.5f) - halfTexelU;
                    top = image == 0 ? 0f : image == 1 ? 0.5f : 0.75f;
                    bottom = (image == 0 ? 0.5f : image == 1 ? 0.75f : 1f) - halfTexelV;
                }
                else
                {
                    int column = (image - 3) & 1, row = (image - 3) >> 1;
                    uMin = column * 0.25f + 0.5f + halfTexelU;
                    uMax = (column + 1) * 0.25f + 0.5f - halfTexelU;
                    top = row * 0.25f * 0.5f + 0.5f + halfTexelV;
                    bottom = (row + 1) * 0.25f * 0.5f + 0.5f - halfTexelV;
                }
            }
            else if (layout == 0)
            {
                if (image == 0)
                {
                    uMin = halfTexelU + 0f;
                    uMax = 1f - halfTexelU;
                    top = halfTexelV + 0f;
                    bottom = 0.5f - halfTexelV;
                }
                else
                {
                    int column = (image - 1) & 1, row = (image - 1) >> 1;
                    uMin = column * 0.5f + halfTexelU;
                    uMax = (column + 1) * 0.5f - halfTexelU;
                    top = row * 0.5f * 0.5f + 0.5f + halfTexelV;
                    bottom = (row + 1) * 0.5f * 0.5f + 0.5f - halfTexelV;
                }
            }
            else
            {
                // Layouts 3 to 5 are grids; 2, and any value outside the six, is the whole texture.
                int perRow = layout >= 3 && layout <= 5 ? layout - 1 : 1;
                float cell = 1f / perRow;
                uMin = (image % perRow) * cell + halfTexelU;
                uMax = (image % perRow + 1) * cell - halfTexelU;
                top = (image / perRow) * cell + halfTexelV;
                bottom = (image / perRow + 1) * cell - halfTexelV;
            }
            vMax = 1f - top;
            vMin = 1f - bottom;
        }
    }
}
