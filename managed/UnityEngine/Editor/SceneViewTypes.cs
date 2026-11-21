using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Serialization;

namespace UnityEditor
{
    /// <summary>The Scene view's draw modes (declared as the release's UnityEditor has them).</summary>
    public enum DrawCameraMode
    {
        UserDefined = -2147483648,
        Normal = -1,
        Textured = 0,
        Wireframe = 1,
        TexturedWire = 2,
        ShadowCascades = 3,
        RenderPaths = 4,
        AlphaChannel = 5,
        Overdraw = 6,
        Mipmaps = 7,
        DeferredDiffuse = 8,
        DeferredSpecular = 9,
        DeferredSmoothness = 10,
        DeferredNormal = 11,
        [global::System.Obsolete("Renamed to better distinguish this mode from new Progressive baked modes. Please use RealtimeCharting instead. (UnityUpgradable) -> RealtimeCharting")] Charting = -12,
        RealtimeCharting = 12,
        Systems = 13,
        [global::System.Obsolete("Renamed to better distinguish this mode from new Progressive baked modes. Please use RealtimeAlbedo instead. (UnityUpgradable) -> RealtimeAlbedo")] Albedo = -14,
        RealtimeAlbedo = 14,
        [global::System.Obsolete("Renamed to better distinguish this mode from new Progressive baked modes. Please use RealtimeEmissive instead. (UnityUpgradable) -> RealtimeEmissive")] Emissive = -15,
        RealtimeEmissive = 15,
        [global::System.Obsolete("Renamed to better distinguish this mode from new Progressive baked modes. Please use RealtimeIndirect instead. (UnityUpgradable) -> RealtimeIndirect")] Irradiance = -16,
        RealtimeIndirect = 16,
        [global::System.Obsolete("Renamed to better distinguish this mode from new Progressive baked modes. Please use RealtimeDirectionality instead. (UnityUpgradable) -> RealtimeDirectionality")] Directionality = -17,
        RealtimeDirectionality = 17,
        [global::System.Obsolete("Renamed to better distinguish this mode from new Progressive baked modes. Please use BakedLightmap instead. (UnityUpgradable) -> BakedLightmap")] Baked = -18,
        BakedLightmap = 18,
        Clustering = 19,
        LitClustering = 20,
        ValidateAlbedo = 21,
        ValidateMetalSpecular = 22,
        ShadowMasks = 23,
        LightOverlap = 24,
        BakedAlbedo = 25,
        BakedEmissive = 26,
        BakedDirectionality = 27,
        BakedTexelValidity = 28,
        BakedIndices = 29,
        BakedCharting = 30,
        SpriteMask = 31,
        BakedUVOverlap = 32,
        TextureStreaming = 33,
        BakedLightmapCulling = 34,
        GIContributorsReceivers = 35,
    }

    public partial class SceneView
    {
        /// <summary>
        /// A built-in Scene view mode: its draw mode, name and toolbar section. Two modes are equal
        /// when those three are (whether the mode is shown does not count); its text is the three
        /// joined by "||", and its hash that text's (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/sceneviewtypes mode.*).
        /// </summary>
        [Serializable]
        public partial struct CameraMode
        {
            public DrawCameraMode drawMode;
            public string name;
            public string section;
            internal bool show;

            internal CameraMode(DrawCameraMode drawMode, string name, string section, bool show = true)
            {
                this.drawMode = drawMode;
                this.name = name;
                this.section = section;
                this.show = show;
            }

            public static bool operator ==(CameraMode a, CameraMode z) =>
                a.drawMode == z.drawMode && a.name == z.name && a.section == z.section;

            public static bool operator !=(CameraMode a, CameraMode z) => !(a == z);

            public override bool Equals(object otherObject) => otherObject is CameraMode other && this == other;

            public override int GetHashCode() => ToString().GetHashCode();

            public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0}||{1}||{2}", drawMode, name, section);
        }

        /// <summary>
        /// The Scene view's graphics switches. Every switch starts on but "always refresh"; each
        /// "…Enabled" answer is its switch with the effects switch also on; allEnabled asks every
        /// switch but the effects one (the visual effect graphs only while visual effects are
        /// active). The clouds switch is not serialized (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/sceneviewtypes state.*).
        /// </summary>
        [Serializable]
        public partial class SceneViewState
        {
            [FormerlySerializedAs("showMaterialUpdate")] [SerializeField] bool m_AlwaysRefresh;
            public bool showFog = true;
            public bool showSkybox = true;
            public bool showFlares = true;
            public bool showImageEffects = true;
            public bool showParticleSystems = true;
            public bool showVisualEffectGraphs = true;
            bool m_ShowClouds = true;
            [SerializeField] bool m_FxEnabled = true;

            public SceneViewState() { }

            public SceneViewState(SceneViewState other)
            {
                fxEnabled = other.fxEnabled;
                showFog = other.showFog;
                alwaysRefresh = other.alwaysRefresh;
                showSkybox = other.showSkybox;
                showClouds = other.showClouds;
                showFlares = other.showFlares;
                showImageEffects = other.showImageEffects;
                showParticleSystems = other.showParticleSystems;
                showVisualEffectGraphs = other.showVisualEffectGraphs;
            }

            [Obsolete("Obsolete msg (UnityUpgradable) -> alwaysRefresh")]
            public bool showMaterialUpdate { get => m_AlwaysRefresh; set => m_AlwaysRefresh = value; }

            public bool alwaysRefresh { get => m_AlwaysRefresh; set => m_AlwaysRefresh = value; }
            public bool showClouds { get => m_ShowClouds; set => m_ShowClouds = value; }

            public bool fogEnabled => fxEnabled && showFog;

            [Obsolete("Obsolete msg (UnityUpgradable) -> alwaysRefreshEnabled")]
            public bool materialUpdateEnabled => alwaysRefreshEnabled;

            public bool alwaysRefreshEnabled => fxEnabled && alwaysRefresh;
            public bool skyboxEnabled => fxEnabled && showSkybox;
            public bool cloudsEnabled => fxEnabled && showClouds;
            public bool flaresEnabled => fxEnabled && showFlares;
            public bool imageEffectsEnabled => fxEnabled && showImageEffects;
            public bool particleSystemsEnabled => fxEnabled && showParticleSystems;
            public bool visualEffectGraphsEnabled => fxEnabled && showVisualEffectGraphs;

            public bool allEnabled =>
                showFog && alwaysRefresh && showSkybox && showClouds && showFlares && showImageEffects && showParticleSystems
                && (!UnityEngine.VFX.VFXManager.activateVFX || showVisualEffectGraphs);

            /// <summary>The effects switch; turning it raises fxEnableChanged.</summary>
            public bool fxEnabled
            {
                get => m_FxEnabled;
                set
                {
                    if (m_FxEnabled == value) return;
                    m_FxEnabled = value;
                    fxEnableChanged?.Invoke(value);
                }
            }

            internal event Action<bool> fxEnableChanged;

            [Obsolete("IsAllOn() has been deprecated. Use allEnabled instead (UnityUpgradable) -> allEnabled")]
            public bool IsAllOn() => allEnabled;

            [Obsolete("Toggle() has been deprecated. Use SetAllEnabled() instead (UnityUpgradable) -> SetAllEnabled(*)")]
            public void Toggle(bool value) => SetAllEnabled(value);

            /// <summary>Sets every switch but the effects one.</summary>
            public void SetAllEnabled(bool value)
            {
                showFog = value;
                alwaysRefresh = value;
                showSkybox = value;
                showClouds = value;
                showFlares = value;
                showImageEffects = value;
                showParticleSystems = value;
                showVisualEffectGraphs = value;
            }
        }
    }
}
