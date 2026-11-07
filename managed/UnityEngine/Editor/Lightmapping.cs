using System;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEditor
{
    /// <summary>
    /// The scenes' Lighting Settings (Scripting API "Lightmapping.lightingSettings", 6000.0: "Will
    /// throw an exception if it is null"). Matches the Unity 6000.0.71f1 Editor in Play mode
    /// (tests/projects/lightingassets): the active scene's settings, the ones its LightmapSettings
    /// reference when it loaded; reading them when the scene has none throws
    /// <see cref="Exception"/>, TryGetLightingSettings and GetLightingSettingsForScene answer
    /// false/null; assigning null takes them away without an exception. bakedGI and realtimeGI read
    /// the scene's settings or else the defaults, and setting one gives a scene without settings a
    /// new LightingSettings first. lightingSettingsDefaults is one shared object. Changing settings
    /// changes nothing drawn until the scene is baked again (<see cref="LightingSettings"/>).
    /// </summary>
    public static partial class Lightmapping
    {
        const string k_NoSettings = "Lightmapping.lightingSettings is null. Please assign it to an existing asset or a new instance. ";

        static LightingSettings s_Defaults;

        public static LightingSettings lightingSettingsDefaults => s_Defaults ??= new LightingSettings();

        public static LightingSettings lightingSettings
        {
            get => Of(SceneRegistry.Active) ?? throw new Exception(k_NoSettings);
            set
            {
                if (SceneRegistry.Active != null) SceneRegistry.Active.LightingSettings = value;
            }
        }

        public static bool TryGetLightingSettings(out LightingSettings settings)
        {
            settings = Of(SceneRegistry.Active);
            return settings != null;
        }

        public static LightingSettings GetLightingSettingsForScene(Scene scene) => Of(scene.Data);

        public static void SetLightingSettingsForScene(Scene scene, LightingSettings lightingSettings)
        {
            if (scene.Data != null) scene.Data.LightingSettings = lightingSettings;
        }

        public static void SetLightingSettingsForScenes(Scene[] scenes, LightingSettings lightingSettings)
        {
            if (scenes == null) throw new ArgumentNullException(nameof(scenes));
            foreach (var scene in scenes) SetLightingSettingsForScene(scene, lightingSettings);
        }

        public static bool bakedGI
        {
            get => GetLightingSettingsOrDefaultsFallback().bakedGI;
            set => GetOrCreateLightingsSettings().bakedGI = value;
        }

        public static bool realtimeGI
        {
            get => GetLightingSettingsOrDefaultsFallback().realtimeGI;
            set => GetOrCreateLightingsSettings().realtimeGI = value;
        }

        [Obsolete("Lightmapping.indirectOutputScale is obsolete, use LightingSettings.indirectScale instead. ")]
        public static float indirectOutputScale
        {
            get => GetLightingSettingsOrDefaultsFallback().indirectScale;
            set => GetOrCreateLightingsSettings().indirectScale = value;
        }

        [Obsolete("Lightmapping.bounceBoost is obsolete, use LightingSettings.albedoBoost instead. ")]
        public static float bounceBoost
        {
            get => GetLightingSettingsOrDefaultsFallback().albedoBoost;
            set => GetOrCreateLightingsSettings().albedoBoost = value;
        }

        internal static LightingSettings GetLightingSettingsOrDefaultsFallback() => Of(SceneRegistry.Active) ?? lightingSettingsDefaults;

        /// <summary>The active scene's settings; a scene without any gets a new one.</summary>
        internal static LightingSettings GetOrCreateLightingsSettings()
        {
            var scene = SceneRegistry.Active;
            var settings = Of(scene);
            if (settings == null)
            {
                settings = new LightingSettings();
                if (scene != null) scene.LightingSettings = settings;
            }
            return settings;
        }

        /// <summary>A scene's settings; null when it has none (or they were destroyed).</summary>
        static LightingSettings Of(SceneData scene) => scene?.LightingSettings is LightingSettings s && s != null ? s : null;
    }
}
