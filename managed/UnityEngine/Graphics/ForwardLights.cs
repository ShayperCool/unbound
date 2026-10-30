using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// The built-in forward renderer's lights beyond the main directional one, per object: the
    /// brightest ones (Important always, Auto ones up to the quality level's pixel light count)
    /// draw the shader's ForwardAdd pass once each; up to four other point and spot lights light
    /// the base pass per vertex (VERTEXLIGHT_ON); the rest add to the object's spherical harmonics.
    /// The light textures are Unity's (measured, tests/projects/lighting): the attenuation
    /// 1 / (1 + 25 d²) fading linearly to 0 from d² = 0.64 to 1 (d is the distance over the range),
    /// and the default spot cookie 1 − smoothstep(0.66, 0.99, r) of the normalized radius.
    /// </summary>
    internal static class ForwardLights
    {
        internal sealed class ObjectLights
        {
            internal readonly List<Light> Pixel = new List<Light>();
            internal readonly List<Light> Vertex = new List<Light>();
            internal readonly List<Light> Spherical = new List<Light>();

            internal void Clear()
            {
                Pixel.Clear();
                Vertex.Clear();
                Spherical.Clear();
            }
        }

        static readonly List<Light> s_Lights = new List<Light>();
        static Light s_Main;
        static Texture2D s_Attenuation, s_SpotCookie;
        static readonly List<(Light Light, float Importance)> s_Candidates = new List<(Light, float)>();

        /// <summary>The attenuation lookup, indexed by the squared distance over the range.</summary>
        internal static Texture2D Attenuation
        {
            get
            {
                if (s_Attenuation != null) return s_Attenuation;
                const int size = 1024;
                s_Attenuation = new Texture2D(size, 1, TextureFormat.RGBA32, false, true) { name = "Attenuation", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color32[size];
                for (int i = 0; i < size; i++)
                {
                    float d2 = (i + 0.5f) / size;
                    float a = 1f / (1f + 25f * d2);
                    if (d2 > 0.64f) a *= Mathf.Clamp01((1f - d2) / 0.36f);
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    pixels[i] = new Color32(b, b, b, b);
                }
                s_Attenuation.SetPixels32(pixels);
                s_Attenuation.Apply();
                return s_Attenuation;
            }
        }

        /// <summary>The spot lights' default cookie (its alpha).</summary>
        internal static Texture2D SpotCookie
        {
            get
            {
                if (s_SpotCookie != null) return s_SpotCookie;
                const int size = 64;
                s_SpotCookie = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { name = "Soft", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size - 0.5f, v = (y + 0.5f) / size - 0.5f;
                        float r = Mathf.Sqrt(u * u + v * v) * 2f;
                        float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.66f, 0.99f, r));
                        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                        pixels[y * size + x] = new Color32(b, b, b, b);
                    }
                s_SpotCookie.SetPixels32(pixels);
                s_SpotCookie.Apply();
                return s_SpotCookie;
            }
        }

        /// <summary>The lights a camera's objects can get.</summary>
        internal static void BeginCamera(Camera camera)
        {
            s_Lights.Clear();
            s_Main = LightingGlobals.MainLightOf();
            int mask = camera != null ? camera.cullingMask : -1;
            foreach (var l in Light.s_Enabled)
            {
                if (l == null || !l.isActiveAndEnabled || l == s_Main || l.IsFullyBaked) continue;
                if (l.type != LightType.Point && l.type != LightType.Spot && l.type != LightType.Directional) continue;
                if (l.intensity <= 0f) continue;
                s_Lights.Add(l);
            }
        }

        internal static bool Any => s_Lights.Count > 0;

        /// <summary>The camera's main directional light (null: none).</summary>
        internal static Light Main => s_Main;

        /// <summary>The directional light an object's base pass draws: the main one when its
        /// culling mask has the object's layer, else the brightest other directional light
        /// without a cookie that has it (measured: tests/projects/builtinrp main_light_mask*),
        /// else none.</summary>
        internal static Light BaseLightFor(int layer)
        {
            if (s_Main != null && (s_Main.cullingMask & (1 << layer)) != 0) return s_Main;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Light best = null;
            float bestValue = -1f;
            foreach (var l in s_Lights)
            {
                if (l.type != LightType.Directional || l.cookie != null || l.renderMode == LightRenderMode.ForceVertex) continue;
                if ((l.cullingMask & (1 << layer)) == 0) continue;
                var c = l.FinalColor(linear);
                float value = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                if (value > bestValue)
                {
                    best = l;
                    bestValue = value;
                }
            }
            return best;
        }

        /// <summary>The camera's lights other than the main one.</summary>
        internal static List<Light> Lights => s_Lights;

        /// <summary>Sorts an object's lights into pixel, vertex and SH ones.</summary>
        internal static void Classify(Bounds bounds, int layer, ObjectLights into, Light baseLight = null)
        {
            into.Clear();
            if (s_Lights.Count == 0) return;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            s_Candidates.Clear();
            foreach (var l in s_Lights)
            {
                if (l == baseLight || (l.cullingMask & (1 << layer)) == 0) continue;
                float importance;
                var c = l.FinalColor(linear);
                float brightness = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                if (l.type == LightType.Directional) importance = brightness * 1e6f;
                else
                {
                    var position = l.transform.position;
                    if (bounds.SqrDistance(position) > l.range * l.range) continue;
                    float d = (bounds.center - position).magnitude / Mathf.Max(l.range, 1e-4f);
                    importance = brightness / (1f + 25f * d * d);
                }
                s_Candidates.Add((l, importance));
            }
            s_Candidates.Sort((a, b) => b.Importance.CompareTo(a.Importance));
            int pixelBudget = Math.Max(0, QualitySettings.pixelLightCount);
            foreach (var (l, _) in s_Candidates)
            {
                if (l.renderMode == LightRenderMode.ForcePixel) into.Pixel.Add(l);
            }
            pixelBudget -= into.Pixel.Count;
            foreach (var (l, _) in s_Candidates)
            {
                if (l.renderMode == LightRenderMode.ForcePixel) continue;
                if (l.renderMode == LightRenderMode.Auto && pixelBudget > 0)
                {
                    into.Pixel.Add(l);
                    pixelBudget--;
                }
                else if (l.type != LightType.Directional && into.Vertex.Count < 4) into.Vertex.Add(l);
                else into.Spherical.Add(l);
            }
        }

        static readonly int s_4LightPosX0 = Shader.PropertyToID("unity_4LightPosX0");
        static readonly int s_4LightPosY0 = Shader.PropertyToID("unity_4LightPosY0");
        static readonly int s_4LightPosZ0 = Shader.PropertyToID("unity_4LightPosZ0");
        static readonly int s_4LightAtten0 = Shader.PropertyToID("unity_4LightAtten0");
        static readonly int s_LightColor = Shader.PropertyToID("unity_LightColor");

        /// <summary>The base pass's vertex lights (unity_4Light*, unity_LightColor).</summary>
        internal static void VertexLightProperties(ObjectLights lights, Dictionary<int, object> into)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Vector4 x = default, y = default, z = default, atten = default;
            var colors = new Vector4[8];
            for (int i = 0; i < lights.Vertex.Count && i < 4; i++)
            {
                var l = lights.Vertex[i];
                var p = l.transform.position;
                x[i] = p.x;
                y[i] = p.y;
                z[i] = p.z;
                atten[i] = 25f / Mathf.Max(l.range * l.range, 1e-8f);
                colors[i] = (Vector4)l.FinalColor(linear);
            }
            into[s_4LightPosX0] = x;
            into[s_4LightPosY0] = y;
            into[s_4LightPosZ0] = z;
            into[s_4LightAtten0] = atten;
            into[s_LightColor] = colors;
        }

        static readonly int s_WorldSpaceLightPos0 = Shader.PropertyToID("_WorldSpaceLightPos0");
        static readonly int s_LightColor0 = Shader.PropertyToID("_LightColor0");
        static readonly int s_WorldToLight = Shader.PropertyToID("unity_WorldToLight");
        static readonly int s_LightTexture0 = Shader.PropertyToID("_LightTexture0");
        static readonly int s_LightTextureB0 = Shader.PropertyToID("_LightTextureB0");

        /// <summary>A pixel light's ForwardAdd variables and its keyword.</summary>
        internal static string AddPassProperties(Light l, Dictionary<int, object> into)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var t = l.transform;
            into[s_LightColor0] = (Vector4)l.FinalColor(linear);
            var cookie = l.cookie;
            switch (l.type)
            {
                case LightType.Directional:
                {
                    var forward = t.forward;
                    into[s_WorldSpaceLightPos0] = new Vector4(-forward.x, -forward.y, -forward.z, 0f);
                    if (cookie != null)
                    {
                        float size = Mathf.Max(l.cookieSize, 1e-4f);
                        into[s_WorldToLight] = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0f), Quaternion.identity, new Vector3(1f / size, 1f / size, 1f)) * t.worldToLocalMatrix;
                        into[s_LightTexture0] = cookie;
                        return "DIRECTIONAL_COOKIE";
                    }
                    into[s_WorldToLight] = t.worldToLocalMatrix;
                    return "DIRECTIONAL";
                }
                case LightType.Spot:
                {
                    var p = t.position;
                    into[s_WorldSpaceLightPos0] = new Vector4(p.x, p.y, p.z, 1f);
                    float range = Mathf.Max(l.range, 1e-4f);
                    float spread = 2f * Mathf.Tan(l.spotAngle * 0.5f * Mathf.Deg2Rad) / range;
                    var projection = new Matrix4x4(
                        new Vector4(1f / range, 0f, 0f, 0f),
                        new Vector4(0f, 1f / range, 0f, 0f),
                        new Vector4(0f, 0f, 1f / range, spread),
                        new Vector4(0f, 0f, 0f, 0f));
                    into[s_WorldToLight] = projection * Matrix4x4.TRS(p, t.rotation, Vector3.one).inverse;
                    into[s_LightTexture0] = cookie != null ? cookie : SpotCookie;
                    into[s_LightTextureB0] = Attenuation;
                    return "SPOT";
                }
                default:
                {
                    var p = t.position;
                    into[s_WorldSpaceLightPos0] = new Vector4(p.x, p.y, p.z, 1f);
                    float range = Mathf.Max(l.range, 1e-4f);
                    into[s_WorldToLight] = Matrix4x4.TRS(p, t.rotation, new Vector3(range, range, range)).inverse;
                    if (cookie != null)
                    {
                        into[s_LightTexture0] = cookie;
                        into[s_LightTextureB0] = Attenuation;
                        return "POINT_COOKIE";
                    }
                    into[s_LightTexture0] = Attenuation;
                    return "POINT";
                }
            }
        }

        internal static readonly string[] AddKeywords = { "POINT", "SPOT", "DIRECTIONAL", "POINT_COOKIE", "DIRECTIONAL_COOKIE" };

        static readonly int s_LightPosition = Shader.PropertyToID("unity_LightPosition");
        static readonly int s_LightAtten = Shader.PropertyToID("unity_LightAtten");
        static readonly int s_SpotDirection = Shader.PropertyToID("unity_SpotDirection");

        /// <summary>A vertex-lit (LightMode Vertex) pass's lights: the eight brightest, the main
        /// light first, in view space (unity_LightPosition, unity_LightColor, unity_LightAtten:
        /// spot cone cos and its inverse range, 25 / range², range²; unity_SpotDirection).</summary>
        internal static void VertexLitProperties(Bounds bounds, int layer, Matrix4x4 view, Dictionary<int, object> into)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            s_Candidates.Clear();
            if (s_Main != null && (s_Main.cullingMask & (1 << layer)) != 0) s_Candidates.Add((s_Main, float.MaxValue));
            foreach (var l in s_Lights)
            {
                if ((l.cullingMask & (1 << layer)) == 0) continue;
                var c = l.FinalColor(linear);
                float brightness = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                float importance;
                if (l.type == LightType.Directional) importance = brightness * 1e6f;
                else
                {
                    var position = l.transform.position;
                    if (bounds.SqrDistance(position) > l.range * l.range) continue;
                    float d = (bounds.center - position).magnitude / Mathf.Max(l.range, 1e-4f);
                    importance = brightness / (1f + 25f * d * d);
                }
                s_Candidates.Add((l, importance));
            }
            s_Candidates.Sort((a, b) => b.Importance.CompareTo(a.Importance));
            var positions = new Vector4[8];
            var colors = new Vector4[8];
            var atten = new Vector4[8];
            var directions = new Vector4[8];
            for (int i = 0; i < 8; i++)
            {
                atten[i] = new Vector4(-1f, 1f, 0f, 1f);
                directions[i] = new Vector4(0f, 0f, 1f, 0f);
            }
            for (int i = 0; i < s_Candidates.Count && i < 8; i++)
            {
                var l = s_Candidates[i].Light;
                var t = l.transform;
                colors[i] = (Vector4)l.FinalColor(linear);
                if (l.type == LightType.Directional)
                {
                    var d = view.MultiplyVector(-t.forward).normalized;
                    positions[i] = new Vector4(d.x, d.y, d.z, 0f);
                    continue;
                }
                var p = view.MultiplyPoint3x4(t.position);
                positions[i] = new Vector4(p.x, p.y, p.z, 1f);
                float range = Mathf.Max(l.range, 1e-4f);
                if (l.type == LightType.Spot)
                {
                    float outer = Mathf.Cos(l.spotAngle * 0.25f * Mathf.Deg2Rad * 2f);
                    float inner = Mathf.Cos(l.spotAngle * 0.25f * Mathf.Deg2Rad);
                    atten[i] = new Vector4(outer, 1f / Mathf.Max(inner - outer, 1e-4f), 25f / (range * range), range * range);
                    var s = view.MultiplyVector(-t.forward).normalized;
                    directions[i] = new Vector4(s.x, s.y, s.z, 0f);
                }
                else atten[i] = new Vector4(-1f, 1f, 25f / (range * range), range * range);
            }
            into[s_LightPosition] = positions;
            into[s_LightColor] = colors;
            into[s_LightAtten] = atten;
            into[s_SpotDirection] = directions;
        }

        /// <summary>The ambient probe plus the SH lights, seen from the object's center.</summary>
        internal static SphericalHarmonicsL2 WithSphericalLights(SphericalHarmonicsL2 sh, ObjectLights lights, Vector3 center)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            foreach (var l in lights.Spherical)
            {
                var c = l.FinalColor(linear);
                if (l.type == LightType.Directional)
                {
                    sh.AddDirectionalLight(-l.transform.forward, c, 0.5f);
                    continue;
                }
                var toLight = l.transform.position - center;
                float d = toLight.magnitude / Mathf.Max(l.range, 1e-4f);
                float atten = 1f / (1f + 25f * d * d);
                sh.AddDirectionalLight(toLight.sqrMagnitude > 1e-12f ? toLight.normalized : Vector3.up, c * atten, 0.5f);
            }
            return sh;
        }
    }
}
