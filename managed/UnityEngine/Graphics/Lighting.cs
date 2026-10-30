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
        LightmapBakeType m_Lightmapping = LightmapBakeType.Realtime;
        LightShadowCasterMode m_LightShadowCasterMode;
        Vector2 m_AreaSize = Vector2.one;
        Vector4 m_BoundingSphereOverride;
        bool m_UseBoundingSphereOverride;
        bool m_UseViewFrustumForShadowCasterCull = true;
        bool m_ForceVisible;
        float m_ShadowRadius;
        float m_ShadowAngle;
        LightShadowResolution m_ShadowResolution = LightShadowResolution.FromQualitySettings;
        int m_ShadowCustomResolution = -1;
        Matrix4x4 m_ShadowMatrixOverride = Matrix4x4.identity;
        bool m_UseShadowMatrixOverride;
        Flare m_Flare;
        // What the Editor reports for a light no lighting data bakes (measured: CapsProbe).
        LightBakingOutput m_BakingOutput = new LightBakingOutput
        {
            probeOcclusionLightIndex = -1,
            occlusionMaskChannel = -1,
            lightmapBakeType = LightmapBakeType.Realtime,
            mixedLightingMode = MixedLightingMode.Shadowmask,
            isBaked = false,
        };

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
        public LightmapBakeType lightmapBakeType { get => m_Lightmapping; set => m_Lightmapping = value; }
        public LightShadowCasterMode lightShadowCasterMode { get => m_LightShadowCasterMode; set => m_LightShadowCasterMode = value; }
        public Vector2 areaSize { get => m_AreaSize; set => m_AreaSize = value; }
        public Vector4 boundingSphereOverride { get => m_BoundingSphereOverride; set => m_BoundingSphereOverride = value; }
        public bool useBoundingSphereOverride { get => m_UseBoundingSphereOverride; set => m_UseBoundingSphereOverride = value; }
        public bool useViewFrustumForShadowCasterCull { get => m_UseViewFrustumForShadowCasterCull; set => m_UseViewFrustumForShadowCasterCull = value; }
        public bool forceVisible { get => m_ForceVisible; set => m_ForceVisible = value; }
        public float shadowRadius { get => m_ShadowRadius; set => m_ShadowRadius = value; }
        public float shadowAngle { get => m_ShadowAngle; set => m_ShadowAngle = value; }
        public LightShadowResolution shadowResolution { get => m_ShadowResolution; set => m_ShadowResolution = value; }
        public int shadowCustomResolution { get => m_ShadowCustomResolution; set => m_ShadowCustomResolution = value; }
        public Matrix4x4 shadowMatrixOverride { get => m_ShadowMatrixOverride; set => m_ShadowMatrixOverride = value; }
        public bool useShadowMatrixOverride { get => m_UseShadowMatrixOverride; set => m_UseShadowMatrixOverride = value; }
        public Flare flare { get => m_Flare; set => m_Flare = value; }
        public LightBakingOutput bakingOutput { get => m_BakingOutput; set => m_BakingOutput = value; }

        /// <summary>Baked into lightmaps and probes alone: such a light does not light at run time.</summary>
        internal bool IsFullyBaked => m_BakingOutput.isBaked && m_BakingOutput.lightmapBakeType == LightmapBakeType.Baked;

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
            m_Lightmapping = o.m_Lightmapping;
            m_LightShadowCasterMode = o.m_LightShadowCasterMode;
            m_AreaSize = o.m_AreaSize;
            m_BoundingSphereOverride = o.m_BoundingSphereOverride;
            m_UseBoundingSphereOverride = o.m_UseBoundingSphereOverride;
            m_UseViewFrustumForShadowCasterCull = o.m_UseViewFrustumForShadowCasterCull;
            m_ForceVisible = o.m_ForceVisible;
            m_ShadowRadius = o.m_ShadowRadius;
            m_ShadowAngle = o.m_ShadowAngle;
            m_ShadowResolution = o.m_ShadowResolution;
            m_ShadowCustomResolution = o.m_ShadowCustomResolution;
            m_ShadowMatrixOverride = o.m_ShadowMatrixOverride;
            m_UseShadowMatrixOverride = o.m_UseShadowMatrixOverride;
            m_Flare = remap(o.m_Flare) as Flare;
            m_BakingOutput = o.m_BakingOutput;
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
                m_ShadowResolution = (LightShadowResolution)shadows.Int("m_Resolution", -1);
                m_ShadowCustomResolution = shadows.Int("m_CustomResolution", -1);
                m_UseShadowMatrixOverride = shadows.Bool("m_UseCullingMatrixOverride");
                var m = shadows.Get("m_CullingMatrixOverride");
                if (m != null)
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 4; c++)
                            m_ShadowMatrixOverride[r, c] = m.Float($"e{r}{c}", r == c ? 1f : 0f);
            }
            m_Lightmapping = (LightmapBakeType)body.Int("m_Lightmapping", (int)LightmapBakeType.Realtime);
            m_LightShadowCasterMode = (LightShadowCasterMode)body.Int("m_LightShadowCasterMode");
            if (body.Get("m_AreaSize") != null) m_AreaSize = body.Vector2("m_AreaSize");
            m_BoundingSphereOverride = body.Vector4("m_BoundingSphereOverride");
            m_UseBoundingSphereOverride = body.Bool("m_UseBoundingSphereOverride");
            m_UseViewFrustumForShadowCasterCull = body.Bool("m_UseViewFrustumForShadowCasterCull", true);
            m_ForceVisible = body.Bool("m_ForceVisible");
            m_ShadowRadius = body.Float("m_ShadowRadius");
            m_ShadowAngle = body.Float("m_ShadowAngle");
            var flare = body.Ref("m_Flare");
            m_Flare = flare.IsNull ? null : resolver.Resolve(flare, typeof(Flare)) as Flare;
            m_RenderMode = (LightRenderMode)body.Int("m_RenderMode");
            m_ColorTemperature = body.Float("m_ColorTemperature", 6570f);
            m_UseColorTemperature = body.Bool("m_UseColorTemperature");
            var cookie = body.Ref("m_Cookie");
            m_Cookie = cookie.IsNull ? null : resolver.Resolve(cookie, typeof(Texture)) as Texture;
            m_CookieSize = body.Float("m_CookieSize", 10f);
        }

        /// <summary>The light's color times its intensity (and color temperature), in the project's
        /// color space.</summary>
        /// <summary>A scriptable pipeline's light color: color times intensity, in linear space
        /// with GraphicsSettings.lightsUseLinearIntensity, else in gamma space (converted to
        /// linear in linear projects).</summary>
        internal Color SrpFinalColor(bool linear)
        {
            var c = m_Color;
            if (m_UseColorTemperature && UnityEngine.Rendering.GraphicsSettings.lightsUseColorTemperature)
            {
                var t = Mathf.CorrelatedColorTemperatureToRGB(m_ColorTemperature);
                c *= t.gamma;
            }
            Color result;
            if (UnityEngine.Rendering.GraphicsSettings.lightsUseLinearIntensity)
            {
                var l = c.linear;
                result = new Color(l.r * m_Intensity, l.g * m_Intensity, l.b * m_Intensity, 1f);
                if (!linear) result = result.gamma;
            }
            else
            {
                result = new Color(c.r * m_Intensity, c.g * m_Intensity, c.b * m_Intensity, 1f);
                if (linear) result = result.linear;
            }
            result.a = 1f;
            return result;
        }

        /// <summary>The color a light gives shaders (in the project's color space): its intensity
        /// multiplies the linear color when GraphicsSettings.lightsUseLinearIntensity is set (URP
        /// sets it), else the gamma color, converted after (measured in the built-in pipeline: an
        /// intensity of 1.5 lights as 1.5^2.2 in a linear project).</summary>
        internal Color FinalColor(bool linear) => SrpFinalColor(linear);
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

        /// <summary>A scene's RenderSettings: its document, and the values in effect while
        /// another scene was active (scripts may have changed them).</summary>
        sealed class SceneSettings
        {
            internal YNode Body;
            internal IReferenceResolver Resolver;
            internal object[] Saved;
        }

        static readonly System.Collections.Generic.Dictionary<SceneData, SceneSettings> s_SceneSettings = new System.Collections.Generic.Dictionary<SceneData, SceneSettings>();
        static SceneData s_SettingsScene;

        /// <summary>What a scene's RenderSettings object holds (everything Read sets).</summary>
        static readonly System.Reflection.FieldInfo[] s_StateFields = Array.ConvertAll(new[]
        {
            nameof(s_Fog), nameof(s_FogColor), nameof(s_FogMode), nameof(s_FogDensity), nameof(s_FogStart), nameof(s_FogEnd),
            nameof(s_AmbientSky), nameof(s_AmbientEquator), nameof(s_AmbientGround), nameof(s_AmbientIntensity), nameof(s_AmbientMode),
            nameof(s_CustomProbe), nameof(s_SubtractiveShadowColor), nameof(s_IndirectSpecular), nameof(s_ReflectionIntensity),
            nameof(s_ReflectionBounces), nameof(s_DefaultReflectionMode), nameof(s_DefaultReflectionResolution), nameof(s_CustomReflection),
            nameof(s_SerializedProbe), nameof(s_SerializedReflection), nameof(s_HaloStrength), nameof(s_FlareStrength),
            nameof(s_FlareFadeSpeed), nameof(s_Skybox), nameof(s_Sun), nameof(s_PendingSun),
        }, n => typeof(RenderSettings).GetField(n, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));

        /// <summary>A scene's RenderSettings document was loaded. Each scene keeps its own; the
        /// active scene's are the ones in effect (an additive scene's do not replace them).</summary>
        internal static void SceneLoaded(SceneData scene, YNode body, IReferenceResolver resolver)
        {
            if (scene == null)
            {
                Read(body, resolver);
                return;
            }
            s_SceneSettings[scene] = new SceneSettings { Body = body, Resolver = resolver };
            if (scene == SceneRegistry.Active || s_SettingsScene == scene) Use(scene);
        }

        /// <summary>The active scene changed: its RenderSettings take effect, those in effect
        /// are kept for the scene they belong to.</summary>
        internal static void ActiveSceneChanged(SceneData active)
        {
            if (active == null || active == s_SettingsScene || !s_SceneSettings.ContainsKey(active)) return;
            Use(active);
        }

        static void Use(SceneData scene)
        {
            if (s_SettingsScene != null && s_SettingsScene != scene && s_SceneSettings.TryGetValue(s_SettingsScene, out var current))
            {
                if (s_SettingsScene.IsLoaded)
                    current.Saved = Array.ConvertAll(s_StateFields, f => f.GetValue(null));
                else
                    s_SceneSettings.Remove(s_SettingsScene);
            }
            s_SettingsScene = scene;
            var next = s_SceneSettings[scene];
            if (next.Saved != null)
            {
                for (int i = 0; i < s_StateFields.Length; i++) s_StateFields[i].SetValue(null, next.Saved[i]);
                next.Saved = null;
            }
            else
            {
                Read(next.Body, next.Resolver);
            }
            foreach (var stale in new System.Collections.Generic.List<SceneData>(s_SceneSettings.Keys))
                if (stale != scene && !stale.IsLoaded && stale.LoadingState != UnityEngine.SceneManagement.Scene.LoadingState.Loading)
                    s_SceneSettings.Remove(stale);
        }

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
            s_CustomProbe = null;
            s_SubtractiveShadowColor = b.Color("m_SubtractiveShadowColor", s_SubtractiveShadowColor);
            s_IndirectSpecular = b.Color("m_IndirectSpecularColor", default);
            s_ReflectionIntensity = b.Float("m_ReflectionIntensity", 1f);
            s_ReflectionBounces = b.Int("m_ReflectionBounces", 1);
            s_DefaultReflectionMode = (DefaultReflectionMode)b.Int("m_DefaultReflectionMode");
            s_DefaultReflectionResolution = b.Int("m_DefaultReflectionResolution", 128);
            var custom = b.Ref("m_CustomReflection");
            s_CustomReflection = custom.IsNull ? null : resolver.Resolve(custom, typeof(Texture)) as Texture;
            // A built scene's ambient probe and skybox reflection (player data: no lighting data asset).
            s_SerializedProbe = null;
            var probe = b.Get("m_AmbientProbe");
            if (probe != null && probe.Get("sh[ 0]") != null)
            {
                var sh = new SphericalHarmonicsL2();
                for (int k = 0; k < 27; k++) sh[k / 9, k % 9] = probe.Float($"sh[{k,2}]");
                s_SerializedProbe = sh;
            }
            var generated = b.Ref("m_GeneratedSkyboxReflection");
            s_SerializedReflection = generated.IsNull ? null : resolver.Resolve(generated, typeof(Cubemap)) as Cubemap;
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
        public static AmbientMode ambientMode { get => s_AmbientMode; set { s_AmbientMode = value; s_CustomProbe = null; } }
        public static Color ambientSkyColor { get => s_AmbientSky; set { s_AmbientSky = value; s_CustomProbe = null; } }
        public static Color ambientEquatorColor { get => s_AmbientEquator; set { s_AmbientEquator = value; s_CustomProbe = null; } }
        public static Color ambientGroundColor { get => s_AmbientGround; set { s_AmbientGround = value; s_CustomProbe = null; } }
        public static float ambientIntensity { get => s_AmbientIntensity; set { s_AmbientIntensity = value; s_CustomProbe = null; } }
        public static Color ambientLight { get => s_AmbientSky; set { s_AmbientSky = value; s_CustomProbe = null; } }

        static SphericalHarmonicsL2? s_CustomProbe;
        static Cubemap s_BakedReflection;
        static readonly System.Collections.Generic.Dictionary<(string, long), Cubemap> s_Cubemaps = new System.Collections.Generic.Dictionary<(string, long), Cubemap>();
        static DefaultReflectionMode s_DefaultReflectionMode;
        static Texture s_CustomReflection;
        static int s_DefaultReflectionResolution = 128;

        public static DefaultReflectionMode defaultReflectionMode { get => s_DefaultReflectionMode; set => s_DefaultReflectionMode = value; }
        public static int defaultReflectionResolution { get => s_DefaultReflectionResolution; set => s_DefaultReflectionResolution = value; }
        public static Texture customReflectionTexture { get => s_CustomReflection; set => s_CustomReflection = value; }

        /// <summary>The skybox's reflection (from the scene's lighting data).</summary>
        internal static Cubemap defaultReflection => s_BakedReflection;

        /// <summary>The reflection objects without a probe reflect: the custom cubemap, else the
        /// skybox's.</summary>
        internal static Texture DefaultReflectionTexture =>
            s_DefaultReflectionMode == DefaultReflectionMode.Custom && s_CustomReflection != null ? s_CustomReflection : s_BakedReflection ?? s_SerializedReflection;
        /// <summary>The scene's lighting data's ambient probe (baked, or the built-in data's for
        /// the default skybox).</summary>
        internal static SphericalHarmonicsL2? s_BakedProbe;
        /// <summary>The ambient probe DynamicGI.UpdateEnvironment derived from the skybox (until
        /// the next scene's lighting data).</summary>
        internal static SphericalHarmonicsL2? s_EnvironmentProbe;
        /// <summary>A built scene's own ambient probe and skybox reflection (an AssetBundle's scene).</summary>
        static SphericalHarmonicsL2? s_SerializedProbe;
        static Cubemap s_SerializedReflection;

        /// <summary>Reads a scene's lighting data (LightmapSettings' m_LightingDataAsset): the
        /// ambient probe, the skybox's reflection and the baked lighting
        /// (<see cref="BakedLighting"/>) of the objects of <paramref name="file"/>.</summary>
        internal static void ReadLightingData(ObjectRef reference, LoadedFile file = null, bool firstScene = true)
        {
            s_BakedProbe = null;
            s_BakedReflection = null;
            s_EnvironmentProbe = null;
            if (firstScene) LightmapSettings.Reset();
            if (reference.IsNull || reference.Guid == null || !Native.IsReady) return;
            try
            {
                var json = Native.Call("lighting-data", System.Text.Json.JsonSerializer.Serialize(new { guid = reference.Guid, fileId = reference.FileId }));
                var probe = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("ambientProbe");
                var sh = new SphericalHarmonicsL2();
                int k = 0;
                foreach (var v in probe.EnumerateArray())
                {
                    if (k >= 27) break;
                    sh[k / 9, k % 9] = v.GetSingle();
                    k++;
                }
                s_BakedProbe = sh;
                var root = System.Text.Json.JsonDocument.Parse(json).RootElement;
                BakedLighting.Read(root, file);
                if (root.TryGetProperty("defaultReflection", out var reflection) && reflection.ValueKind == System.Text.Json.JsonValueKind.Object)
                    s_BakedReflection = BakedReflection(reflection.GetProperty("guid").GetString(), reflection.GetProperty("fileId").GetInt64());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: the scene's lighting data could not be read: {e.Message}");
            }
        }
        static bool s_WarnedSkyboxProbe;

        /// <summary>A baked reflection a lighting data names (the skybox's, a probe's): a cubemap
        /// of the built-in resources, or a project's baked cubemap asset.</summary>
        internal static Cubemap BakedReflection(string guid, long fileId)
        {
            var key = (guid, fileId);
            if (s_Cubemaps.TryGetValue(key, out var cube) && cube) return cube;
            try
            {
                cube = guid.StartsWith("0000000000000000", StringComparison.Ordinal)
                    ? Cubemap.FromSerialized(guid, fileId)
                    : AssetDatabaseRuntime.Load(guid, fileId, typeof(Cubemap)) as Cubemap;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: the scene's baked reflection could not be read: {e.Message}");
                cube = null;
            }
            if (cube != null) s_Cubemaps[key] = cube;
            return cube;
        }

        /// <summary>
        /// The ambient lighting as spherical harmonics (linear color): a flat color in its first
        /// coefficient; a gradient's sky, equator and ground projected with the weights the Editor
        /// gives them (measured); a skybox's is baked by the Editor, which Unbound does not do yet: the
        /// sky color stands in. A probe set here stands until the ambient settings change.
        /// </summary>
        public static SphericalHarmonicsL2 ambientProbe
        {
            get
            {
                if (s_CustomProbe.HasValue) return s_CustomProbe.Value;
                var sh = new SphericalHarmonicsL2();
                var sky = s_AmbientSky.linear;
                switch (s_AmbientMode)
                {
                    case AmbientMode.Trilight:
                    {
                        var equator = s_AmbientEquator.linear;
                        var ground = s_AmbientGround.linear;
                        var c0 = (sky + 4f * equator + ground) / 6f;
                        var c1 = (sky - ground) * Mathf.Sqrt(2f / 27f);
                        var curve = equator * 2f - sky - ground;
                        for (int c = 0; c < 3; c++)
                        {
                            sh[c, 0] = c0[c];
                            sh[c, 1] = c1[c];
                            sh[c, 6] = curve[c] * (5f / 192f);
                            sh[c, 8] = curve[c] * (5f / 64f);
                        }
                        break;
                    }
                    case AmbientMode.Skybox when s_EnvironmentProbe.HasValue:
                        return s_EnvironmentProbe.Value;
                    case AmbientMode.Skybox when s_BakedProbe.HasValue:
                        // The lighting data's probe is at intensity 1; the Editor scales it by the
                        // intensity taken as a gamma value (measured: 0.18, 0.36 and 2).
                        return s_BakedProbe.Value * Mathf.GammaToLinearSpace(s_AmbientIntensity);
                    case AmbientMode.Skybox when s_SerializedProbe.HasValue:
                        return s_SerializedProbe.Value;
                    case AmbientMode.Skybox:
                        if (!s_WarnedSkyboxProbe && s_Skybox != null)
                        {
                            s_WarnedSkyboxProbe = true;
                            Unbound.Runtime.Native.Log(2, "Unbound: skybox ambient lighting is approximated by the ambient sky color (no baked ambient probe)");
                        }
                        sh.AddAmbientLight(sky * s_AmbientIntensity);
                        break;
                    default:
                        sh.AddAmbientLight(sky);
                        break;
                }
                return sh;
            }
            set => s_CustomProbe = value;
        }
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

    /// <summary>What the lighting data says a light was baked into.</summary>
    public partial struct LightBakingOutput
    {
        public int probeOcclusionLightIndex;
        public int occlusionMaskChannel;
        public LightmapBakeType lightmapBakeType;
        public MixedLightingMode mixedLightingMode;
        public bool isBaked;
    }

    /// <summary>A lens flare asset (Unbound draws no flares).</summary>
    public sealed partial class Flare : Object
    {
        public Flare() { }
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
                // No light: zero (measured: tests/projects/video; the sky sees another value).
                into.Add(("_WorldSpaceLightPos0", new Vector4(0f, 0f, 0f, 0f)));
                into.Add(("_LightColor0", Vector4.zero));
            }

            // Ambient: ShadeSH9's constants from the ambient probe (linear; gamma projects convert
            // the result back).
            float intensity = UnityEngine.RenderSettings.ambientIntensity;
            var sky = Space(UnityEngine.RenderSettings.ambientSkyColor) * intensity;
            var equator = Space(UnityEngine.RenderSettings.ambientEquatorColor) * intensity;
            var ground = Space(UnityEngine.RenderSettings.ambientGroundColor) * intensity;
            UnityEngine.RenderSettings.ambientProbe.ShaderConstants(out var shAr, out var shAg, out var shAb, out var shBr, out var shBg, out var shBb, out var shC);
            into.Add(("unity_SHAr", shAr));
            into.Add(("unity_SHAg", shAg));
            into.Add(("unity_SHAb", shAb));
            into.Add(("unity_SHBr", shBr));
            into.Add(("unity_SHBg", shBg));
            into.Add(("unity_SHBb", shBb));
            into.Add(("unity_SHC", shC));
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

        internal static Light MainLightOf() => MainLight();

        /// <summary>The scene's sun when it is an enabled directional light, else the brightest
        /// enabled directional light. A light with a cookie is never it: the base pass has no
        /// cookie variant, such a light draws in the ForwardAdd pass (measured:
        /// tests/projects/builtinrp main_light_cookie).</summary>
        static Light MainLight()
        {
            bool builtin = GraphicsSettings.currentRenderPipeline == null;
            var sun = UnityEngine.RenderSettings.sun;
            if (sun != null && sun.isActiveAndEnabled && sun.type == LightType.Directional && !sun.IsFullyBaked && !(builtin && sun.cookie != null)) return sun;
            Light best = null;
            float bestValue = -1f;
            foreach (var l in Light.s_Enabled)
            {
                if (l == null || !l.isActiveAndEnabled || l.type != LightType.Directional || l.IsFullyBaked) continue;
                if (builtin && l.cookie != null) continue;
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
