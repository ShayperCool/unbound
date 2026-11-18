using System;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>The options of a player build, as a script fills them for a build handler.</summary>
    public partial struct BuildPlayerOptions
    {
        public string[] scenes { get; set; }
        public string locationPathName { get; set; }
        public string assetBundleManifestPath { get; set; }
        public BuildTargetGroup targetGroup { get; set; }
        public BuildTarget target { get; set; }
        public int subtarget { get; set; }
        public BuildOptions options { get; set; }
        public string[] extraScriptingDefines { get; set; }
    }

    /// <summary>The Build Settings window's handlers: kept as the Editor keeps them. Unbound builds no
    /// player and has no Build Settings window, so neither handler is called.</summary>
    public partial class BuildPlayerWindow : EditorWindow
    {
        /// <summary>The window's initial size, place and title, as the release's constructor sets them.</summary>
        public BuildPlayerWindow()
        {
            minSize = new Vector2(640f, 580f);
            position = new Rect(50f, 50f, minSize.x, minSize.y);
            titleContent = EditorGUIUtility.TrTextContent("Build Settings", null, (Texture)null);
        }

        // The release's names: editor tooling reads them by reflection.
        static Func<BuildPlayerOptions, BuildPlayerOptions> getBuildPlayerOptionsHandler;
        static Action<BuildPlayerOptions> buildPlayerHandler;

        /// <summary>Replaces the handler; one replacing another logs a warning, null resets it
        /// without one (matches the Unity 6000.0.71f1 Editor, tests/projects/buildplayerhandlers).</summary>
        public static void RegisterGetBuildPlayerOptionsHandler(Func<BuildPlayerOptions, BuildPlayerOptions> func)
        {
            if (func != null && getBuildPlayerOptionsHandler != null)
                Debug.LogWarning("The get build player options handler in BuildPlayerWindow is being reassigned!");
            getBuildPlayerOptionsHandler = func;
        }

        public static void RegisterBuildPlayerHandler(Action<BuildPlayerOptions> func)
        {
            if (func != null && buildPlayerHandler != null)
                Debug.LogWarning("The build player handler in BuildPlayerWindow is being reassigned!");
            buildPlayerHandler = func;
        }
    }
}
