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

        /// <summary>Obsolete: OnDemand whatever is set, as Unity's own C# answers (the Unity
        /// 6000.0.71f1 Editor, tests/projects/graphicsgaps lightmapping.*).</summary>
        [Obsolete("Lightmapping.giWorkflowMode is obsolete.", false)]
        [Unbound.EditorFaithful("Unity's obsolete property answers OnDemand and ignores a set (tests/projects/graphicsgaps)")]
        public static GIWorkflowMode giWorkflowMode { get => GIWorkflowMode.OnDemand; set { } }

        public enum GIWorkflowMode
        {
            Iterative = 0,
            OnDemand = 1,
            Legacy = 2,
        }

        /// <summary>The Lighting Data asset the active scene's lighting settings name, one object
        /// per asset (named as its file; the built-in one, which scenes made in Unity 6 name,
        /// "LightingData"), or null when they name none. Matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/tiersettings and indirectspec. Assigning one (which in the Editor
        /// replaces the scene's baked lighting) is not supported.</summary>
        public static LightingDataAsset lightingDataAsset
        {
            get
            {
                var scene = SceneRegistry.Active;
                if (scene != null && scene.BakedOnOpen)
                    Stubs.Ignored("Lightmapping.lightingDataAsset", "of a scene the Editor baked (when it opened it, or by Lightmapping.Bake) is the Editor's new asset; Unbound answers null");
                return scene == null || scene.BakedOnOpen ? null : LightingDataAsset.Of(scene.LightingDataRef);
            }
            set => throw new NotSupportedException("Unbound: assigning Lightmapping.lightingDataAsset (replacing a scene's baked lighting) is not supported.");
        }

        const string k_PlayMode = "Computing lightmaps is not available in play mode.";

        /// <summary>
        /// "Starts a synchronous bake job ... Returns true if the bake ran successfully. Will
        /// return false and print a warning message if it's not possible to start the bake"
        /// (Scripting API 6000.0). Measured (the Unity 6000.0.71f1 Editor, batch mode,
        /// tests/projects/realtimegi `script_bake.py`): in Play mode it bakes nothing, warns and
        /// answers false; called by editor code that loads with the domain for Play mode
        /// ([InitializeOnLoad] and the other domain load callbacks) it bakes the open
        /// scenes, sending <see cref="bakeStarted"/> and <see cref="bakeCompleted"/> before it answers
        /// true, and writes their lighting data, but Play mode starts with the scenes as they were
        /// (no realtime GI from that bake). Unbound answers and sends the same then and writes no
        /// lighting data (which later sessions would use: said once). In Edit mode (`unbound
        /// test`, editor scripts) it bakes the open scenes' realtime GI with Unbound's own
        /// precompute (`script_bake.py --edit`; RealtimeGI.BakeInEditMode). Entering Play mode
        /// from Edit mode without a domain reload (`unbound test`), [InitializeOnEnterPlayMode]
        /// code runs with isPlaying already true and gets the Play mode answer (the Editor's is
        /// not measured there).
        /// </summary>
        public static bool Bake()
        {
            if (EditorSession.IsPlaying)
            {
                Debug.LogWarning(k_PlayMode);
                return false;
            }
            if (!EditorInitialization.PlayModeDomainLoading) return RealtimeGI.BakeInEditMode();
            Stubs.Ignored("Lightmapping.Bake", "as Play mode is entered writes the open scenes' lighting data in the Editor, which later sessions use; Unbound writes none");
            RaiseBakeStarted();
            RaiseBakeCompleted();
            return true;
        }

        /// <summary>Whether a bake is running: from just after bakeStarted to just before
        /// bakeCompleted (measured: tests/projects/realtimegi `script_bake.py --edit`).</summary>
        public static bool isRunning => RealtimeGI.Baking;

        internal static void RaiseBakeStarted() => bakeStarted?.Invoke();
        internal static void RaiseBakeCompleted() => bakeCompleted?.Invoke();
        internal static void RaiseLightingDataUpdated() => lightingDataUpdated?.Invoke();

        /// <summary>In Play mode as <see cref="Bake"/> (measured: the same warning, false); an
        /// asynchronous bake outside Play mode is not implemented.</summary>
        public static bool BakeAsync()
        {
            if (EditorSession.IsPlaying)
            {
                Debug.LogWarning(k_PlayMode);
                return false;
            }
            Stubs.NotImplemented("UnityEditor.Lightmapping.BakeAsync outside Play mode");
            return false;
        }

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

        [global::System.@ObsoleteAttribute("Lightmapping.indirectOutputScale is obsolete, use LightingSettings.indirectScale instead. ", false)]
        public static float indirectOutputScale
        {
            get => GetLightingSettingsOrDefaultsFallback().indirectScale;
            set => GetOrCreateLightingsSettings().indirectScale = value;
        }

        [global::System.@ObsoleteAttribute("Lightmapping.bounceBoost is obsolete, use LightingSettings.albedoBoost instead. ", false)]
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
