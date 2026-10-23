using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// A light. Forward rendering lights each object with the main directional light (the scene's
    /// sun, else the brightest enabled directional light) and the ambient of
    /// <see cref="RenderSettings"/>; see <see cref="LightingGlobals"/>.
    /// </summary>
    [ClassId(108)]
    public sealed partial class Light : Behaviour
    {
        internal static readonly List<Light> s_Enabled = new List<Light>();

        LightType m_Type = LightType.Point;
        Color m_Color = new Color(1f, 0.95686275f, 0.8392157f, 1f);
        float m_Intensity = 1f;
        float m_BounceIntensity = 1f;
        float m_Range = 10f;
        float m_SpotAngle = 30f;
        float m_InnerSpotAngle = 21.80208f;
        int m_CullingMask = -1;
        int m_RenderingLayerMask = 1;
        LightShadows m_Shadows = LightShadows.None;
        float m_ShadowStrength = 1f;
        float m_ShadowBias = 0.05f;
        float m_ShadowNormalBias = 0.4f;
        float m_ShadowNearPlane = 0.2f;
        LightRenderMode m_RenderMode = LightRenderMode.Auto;
        float m_ColorTemperature = 6570f;
        bool m_UseColorTemperature;
        Texture m_Cookie;
        float m_CookieSize = 10f;

        public Light() { }

        internal override void OnEngineEnable()
        {
            if (!s_Enabled.Contains(this)) s_Enabled.Add(this);
        }

        internal override void OnEngineDisable() => s_Enabled.Remove(this);

        internal override void OnEngineDestroy() => s_Enabled.Remove(this);

        public LightType type { get => m_Type; set => m_Type = value; }
        public Color color { get => m_Color; set => m_Color = value; }
        public float intensity { get => m_Intensity; set => m_Intensity = Mathf.Max(0f, value); }
        public float bounceIntensity { get => m_BounceIntensity; set => m_BounceIntensity = value; }
        public float range { get => m_Range; set => m_Range = value; }
        public float spotAngle { get => m_SpotAngle; set => m_SpotAngle = value; }
        public float innerSpotAngle { get => m_InnerSpotAngle; set => m_InnerSpotAngle = value; }
        public int cullingMask { get => m_CullingMask; set => m_CullingMask = value; }
        public int renderingLayerMask { get => m_RenderingLayerMask; set => m_RenderingLayerMask = value; }
        public LightShadows shadows { get => m_Shadows; set => m_Shadows = value; }
        public float shadowStrength { get => m_ShadowStrength; set => m_ShadowStrength = value; }
        public float shadowBias { get => m_ShadowBias; set => m_ShadowBias = value; }
        public float shadowNormalBias { get => m_ShadowNormalBias; set => m_ShadowNormalBias = value; }
        public float shadowNearPlane { get => m_ShadowNearPlane; set => m_ShadowNearPlane = value; }
        public LightRenderMode renderMode { get => m_RenderMode; set => m_RenderMode = value; }
        public float colorTemperature { get => m_ColorTemperature; set => m_ColorTemperature = value; }
        public bool useColorTemperature { get => m_UseColorTemperature; set => m_UseColorTemperature = value; }
        public Texture cookie { get => m_Cookie; set => m_Cookie = value; }
        public float cookieSize { get => m_CookieSize; set => m_CookieSize = value; }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var o = (Light)source;
            m_Type = o.m_Type;
            m_Color = o.m_Color;
            m_Intensity = o.m_Intensity;
            m_BounceIntensity = o.m_BounceIntensity;
            m_Range = o.m_Range;
            m_SpotAngle = o.m_SpotAngle;
            m_InnerSpotAngle = o.m_InnerSpotAngle;
            m_CullingMask = o.m_CullingMask;
            m_RenderingLayerMask = o.m_RenderingLayerMask;
            m_Shadows = o.m_Shadows;
            m_ShadowStrength = o.m_ShadowStrength;
            m_ShadowBias = o.m_ShadowBias;
            m_ShadowNormalBias = o.m_ShadowNormalBias;
            m_ShadowNearPlane = o.m_ShadowNearPlane;
            m_RenderMode = o.m_RenderMode;
            m_ColorTemperature = o.m_ColorTemperature;
            m_UseColorTemperature = o.m_UseColorTemperature;
            m_Cookie = remap(o.m_Cookie) as Texture;
            m_CookieSize = o.m_CookieSize;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Type = (LightType)body.Int("m_Type", (int)LightType.Point);
            m_Color = body.Color("m_Color", m_Color);
            m_Intensity = body.Float("m_Intensity", 1f);
            m_BounceIntensity = body.Float("m_BounceIntensity", 1f);
            m_Range = body.Float("m_Range", 10f);
            m_SpotAngle = body.Float("m_SpotAngle", 30f);
            m_InnerSpotAngle = body.Float("m_InnerSpotAngle", m_InnerSpotAngle);
            m_CullingMask = (int)(body.Get("m_CullingMask")?.Long("m_Bits", -1) ?? -1);
            m_RenderingLayerMask = body.Int("m_RenderingLayerMask", 1);
            var shadows = body.Get("m_Shadows");
            if (shadows != null)
            {
                m_Shadows = (LightShadows)shadows.Int("m_Type");
                m_ShadowStrength = shadows.Float("m_Strength", 1f);
                m_ShadowBias = shadows.Float("m_Bias", 0.05f);
                m_ShadowNormalBias = shadows.Float("m_NormalBias", 0.4f);
                m_ShadowNearPlane = shadows.Float("m_NearPlane", 0.2f);
            }
            m_RenderMode = (LightRenderMode)body.Int("m_RenderMode");
            m_ColorTemperature = body.Float("m_ColorTemperature", 6570f);
            m_UseColorTemperature = body.Bool("m_UseColorTemperature");
            var cookie = body.Ref("m_Cookie");
            m_Cookie = cookie.IsNull ? null : resolver.Resolve(cookie, typeof(Texture)) as Texture;
            m_CookieSize = body.Float("m_CookieSize", 10f);
        }

        /// <summary>The light's color times its intensity (and color temperature), in the project's
        /// color space.</summary>
        internal Color FinalColor(bool linear)
        {
            var c = linear ? m_Color.linear : m_Color;
            if (m_UseColorTemperature)
            {
                var t = Mathf.CorrelatedColorTemperatureToRGB(m_ColorTemperature);
                c *= linear ? t : t.gamma;
            }
            // Unity scales light intensity in linear space: gamma projects convert.
            float intensity = linear ? m_Intensity : Mathf.LinearToGammaSpace(m_Intensity);
            return new Color(c.r * intensity, c.g * intensity, c.b * intensity, 1f);
        }
    }

    /// <summary>The scene's lighting environment (its RenderSettings document): ambient, fog,
    /// skybox and sun.</summary>
    public sealed partial class RenderSettings : Object
    {
        static bool s_Fog;
        static FogMode s_FogMode = FogMode.ExponentialSquared;
        static Color s_FogColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        static float s_FogDensity = 0.01f;
        static float s_FogStart;
        static float s_FogEnd = 300f;
        static AmbientMode s_AmbientMode = AmbientMode.Skybox;
        static Color s_AmbientSky = new Color(0.212f, 0.227f, 0.259f, 1f);
        static Color s_AmbientEquator = new Color(0.114f, 0.125f, 0.133f, 1f);
        static Color s_AmbientGround = new Color(0.047f, 0.043f, 0.035f, 1f);
        static float s_AmbientIntensity = 1f;
        static Color s_SubtractiveShadowColor = new Color(0.42f, 0.478f, 0.627f, 1f);
        static Color s_IndirectSpecular;
        static float s_ReflectionIntensity = 1f;
        static int s_ReflectionBounces = 1;
        static float s_HaloStrength = 0.5f;
        static float s_FlareStrength = 1f;
        static float s_FlareFadeSpeed = 3f;
        static Material s_Skybox;
        static Light s_Sun;
        // The sun is resolved on first use: the scene's objects are not all built when the settings
        // are read.
        static (ObjectRef Ref, IReferenceResolver Resolver)? s_PendingSun;

        RenderSettings() { }

        /// <summary>Reads a scene's RenderSettings document.</summary>
        internal static void Read(YNode b, IReferenceResolver resolver)
        {
            s_Fog = b.Bool("m_Fog");
            s_FogColor = b.Color("m_FogColor", s_FogColor);
            s_FogMode = (FogMode)b.Int("m_FogMode", 3);
            s_FogDensity = b.Float("m_FogDensity", 0.01f);
            s_FogStart = b.Float("m_LinearFogStart");
            s_FogEnd = b.Float("m_LinearFogEnd", 300f);
            s_AmbientSky = b.Color("m_AmbientSkyColor", s_AmbientSky);
            s_AmbientEquator = b.Color("m_AmbientEquatorColor", s_AmbientEquator);
            s_AmbientGround = b.Color("m_AmbientGroundColor", s_AmbientGround);
            s_AmbientIntensity = b.Float("m_AmbientIntensity", 1f);
            s_AmbientMode = (AmbientMode)b.Int("m_AmbientMode");
            s_SubtractiveShadowColor = b.Color("m_SubtractiveShadowColor", s_SubtractiveShadowColor);
            s_IndirectSpecular = b.Color("m_IndirectSpecularColor", default);
            s_ReflectionIntensity = b.Float("m_ReflectionIntensity", 1f);
            s_ReflectionBounces = b.Int("m_ReflectionBounces", 1);
            s_HaloStrength = b.Float("m_HaloStrength", 0.5f);
            s_FlareStrength = b.Float("m_FlareStrength", 1f);
            s_FlareFadeSpeed = b.Float("m_FlareFadeSpeed", 3f);
            var skybox = b.Ref("m_SkyboxMaterial");
            s_Skybox = skybox.IsNull ? null : resolver.Resolve(skybox, typeof(Material)) as Material;
            s_Sun = null;
            var sun = b.Ref("m_Sun");
            s_PendingSun = sun.IsNull ? null : (sun, resolver);
        }

        public static bool fog { get => s_Fog; set => s_Fog = value; }
        public static float fogStartDistance { get => s_FogStart; set => s_FogStart = value; }
        public static float fogEndDistance { get => s_FogEnd; set => s_FogEnd = value; }
        public static FogMode fogMode { get => s_FogMode; set => s_FogMode = value; }
        public static Color fogColor { get => s_FogColor; set => s_FogColor = value; }
        public static float fogDensity { get => s_FogDensity; set => s_FogDensity = value; }
        public static AmbientMode ambientMode { get => s_AmbientMode; set => s_AmbientMode = value; }
        public static Color ambientSkyColor { get => s_AmbientSky; set => s_AmbientSky = value; }
        public static Color ambientEquatorColor { get => s_AmbientEquator; set => s_AmbientEquator = value; }
        public static Color ambientGroundColor { get => s_AmbientGround; set => s_AmbientGround = value; }
        public static float ambientIntensity { get => s_AmbientIntensity; set => s_AmbientIntensity = value; }
        public static Color ambientLight { get => s_AmbientSky; set => s_AmbientSky = value; }
        public static Color subtractiveShadowColor { get => s_SubtractiveShadowColor; set => s_SubtractiveShadowColor = value; }
        public static Material skybox { get => s_Skybox; set => s_Skybox = value; }
        public static Light sun
        {
            get
            {
                if (s_PendingSun is { } pending)
                {
                    s_PendingSun = null;
                    s_Sun = pending.Resolver.Resolve(pending.Ref, typeof(Light)) as Light;
                }
                return s_Sun;
            }
            set
            {
                s_PendingSun = null;
                s_Sun = value;
            }
        }
        public static float reflectionIntensity { get => s_ReflectionIntensity; set => s_ReflectionIntensity = value; }
        public static int reflectionBounces { get => s_ReflectionBounces; set => s_ReflectionBounces = value; }
        public static float haloStrength { get => s_HaloStrength; set => s_HaloStrength = value; }
        public static float flareStrength { get => s_FlareStrength; set => s_FlareStrength = value; }
        public static float flareFadeSpeed { get => s_FlareFadeSpeed; set => s_FlareFadeSpeed = value; }

        internal static Color IndirectSpecularColor => s_IndirectSpecular;
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>
    /// The built-in shader variables of forward lighting, sent with each frame as global shader
    /// properties: the main directional light (`_WorldSpaceLightPos0`, `_LightColor0`), the ambient
    /// as spherical harmonics (`unity_SH*`) and colors (`unity_Ambient*`,
    /// `glstate_lightmodel_ambient`), fog (`unity_FogColor`, `unity_FogParams` and the `FOG_*`
    /// keyword), `unity_IndirectSpecColor`. Values are in the project's color space.
    /// </summary>
    internal static class LightingGlobals
    {
        static bool s_WarnedSkybox;

        internal static void Collect(List<(string Name, Vector4 Value)> into, List<string> keywords)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Color Space(Color c) => linear ? c.linear : c;

            // The main light.
            var main = MainLight();
            if (main != null)
            {
                var forward = main.transform.forward;
                into.Add(("_WorldSpaceLightPos0", new Vector4(-forward.x, -forward.y, -forward.z, 0f)));
                into.Add(("_LightColor0", (Vector4)main.FinalColor(linear)));
            }
            else
            {
                into.Add(("_WorldSpaceLightPos0", new Vector4(0f, 0f, 1f, 0f)));
                into.Add(("_LightColor0", Vector4.zero));
            }

            // Ambient.
            float intensity = UnityEngine.RenderSettings.ambientIntensity;
            var sky = Space(UnityEngine.RenderSettings.ambientSkyColor) * intensity;
            var equator = Space(UnityEngine.RenderSettings.ambientEquatorColor) * intensity;
            var ground = Space(UnityEngine.RenderSettings.ambientGroundColor) * intensity;
            Color l0, l1y;
            switch (UnityEngine.RenderSettings.ambientMode)
            {
                case AmbientMode.Trilight:
                    // Linear in the normal's y: sky above, equator at the horizon, ground below.
                    l0 = equator * 0.5f + (sky + ground) * 0.25f;
                    l1y = (sky - ground) * 0.5f;
                    break;
                case AmbientMode.Skybox:
                    // Unity bakes the skybox's ambient probe; without it, the sky color stands in.
                    if (!s_WarnedSkybox && UnityEngine.RenderSettings.skybox != null)
                    {
                        s_WarnedSkybox = true;
                        Native.Log(2, "Unbound: skybox ambient lighting is approximated by the ambient sky color (no baked ambient probe)");
                    }
                    l0 = sky;
                    l1y = default;
                    break;
                default:
                    l0 = sky;
                    l1y = default;
                    break;
            }
            // ShadeSH9 computes dot(SHA, (n, 1)) per channel (linear values; gamma projects convert
            // the result back).
            Color shLinear(Color c) => linear ? c : c.linear;
            var a0 = shLinear(l0);
            var a1 = shLinear(l0 + l1y) - a0;
            into.Add(("unity_SHAr", new Vector4(0f, a1.r, 0f, a0.r)));
            into.Add(("unity_SHAg", new Vector4(0f, a1.g, 0f, a0.g)));
            into.Add(("unity_SHAb", new Vector4(0f, a1.b, 0f, a0.b)));
            into.Add(("unity_SHBr", Vector4.zero));
            into.Add(("unity_SHBg", Vector4.zero));
            into.Add(("unity_SHBb", Vector4.zero));
            into.Add(("unity_SHC", Vector4.zero));
            into.Add(("unity_AmbientSky", (Vector4)sky));
            into.Add(("unity_AmbientEquator", (Vector4)equator));
            into.Add(("unity_AmbientGround", (Vector4)ground));
            // UNITY_LIGHTMODEL_AMBIENT is twice this.
            into.Add(("glstate_lightmodel_ambient", (Vector4)(sky * 0.5f)));
            into.Add(("unity_IndirectSpecColor", (Vector4)Space(UnityEngine.RenderSettings.IndirectSpecularColor)));

            // Fog.
            into.Add(("unity_FogColor", (Vector4)Space(UnityEngine.RenderSettings.fogColor)));
            float density = UnityEngine.RenderSettings.fogDensity;
            float start = UnityEngine.RenderSettings.fogStartDistance, end = UnityEngine.RenderSettings.fogEndDistance;
            float range = Mathf.Max(end - start, 1e-4f);
            into.Add(("unity_FogParams", new Vector4(density / Mathf.Sqrt(Mathf.Log(2f)), density / Mathf.Log(2f), -1f / range, end / range)));
            if (UnityEngine.RenderSettings.fog)
            {
                keywords.Add(UnityEngine.RenderSettings.fogMode switch
                {
                    FogMode.Linear => "FOG_LINEAR",
                    FogMode.Exponential => "FOG_EXP",
                    _ => "FOG_EXP2",
                });
            }
        }

        /// <summary>The scene's sun when it is an enabled directional light, else the brightest
        /// enabled directional light.</summary>
        static Light MainLight()
        {
            var sun = UnityEngine.RenderSettings.sun;
            if (sun != null && sun.isActiveAndEnabled && sun.type == LightType.Directional) return sun;
            Light best = null;
            float bestValue = -1f;
            foreach (var l in Light.s_Enabled)
            {
                if (l == null || !l.isActiveAndEnabled || l.type != LightType.Directional) continue;
                float value = l.intensity * l.color.grayscale;
                if (value > bestValue)
                {
                    best = l;
                    bestValue = value;
                }
            }
            return best;
        }
    }
}
