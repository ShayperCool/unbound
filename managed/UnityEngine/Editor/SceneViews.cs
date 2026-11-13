using System;
using System.Collections;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>A window with a search field (Scene, Hierarchy, Project).</summary>
    public partial class SearchableEditorWindow : EditorWindow
    {
    }

    /// <summary>
    /// Scene views. Unbound's Editor layout has the game view only: no Scene view is open, so the
    /// lists are empty, nothing is the last active or drawing view, and the scene GUI events never
    /// fire (packages that draw handles or scene-view overlays find nothing to draw into).
    /// </summary>
    [Unbound.EditorFaithful("no Scene view is open, as in the batch-mode Editor (measured: tests/projects/editorui SceneView, tests/projects/editor sceneView.count)")]
    public partial class SceneView : SearchableEditorWindow
    {
        static readonly ArrayList s_SceneViews = new ArrayList();

        public static Action<SceneView, SceneView> lastActiveSceneViewChanged;

        [global::System.ComponentModel.@EditorBrowsableAttribute((global::System.ComponentModel.EditorBrowsableState)1)] [global::System.@ObsoleteAttribute("onSceneGUIDelegate has been deprecated. Use duringSceneGui instead.")]
        public static OnSceneFunc onSceneGUIDelegate;

        public static ArrayList sceneViews => s_SceneViews;

        public static SceneView lastActiveSceneView { get; private set; }

        public static SceneView currentDrawingSceneView => null;

        public static Camera[] GetAllSceneCameras() => Array.Empty<Camera>();

        public static void RepaintAll() { }

        public static bool FrameLastActiveSceneView() => false;

        public static bool FrameLastActiveSceneViewWithLock() => false;

#pragma warning disable CS0067 // No scene view draws.
        public static event Action<SceneView> beforeSceneGui;
        public static event Action<SceneView> duringSceneGui;
#pragma warning restore CS0067

        [global::System.@ObsoleteAttribute("OnSceneFunc() has been deprecated. Use System.Action instead.")] [global::System.ComponentModel.@EditorBrowsableAttribute((global::System.ComponentModel.EditorBrowsableState)1)] public delegate void OnSceneFunc(SceneView sceneView);
    }

    public sealed partial class Handles
    {
        /// <summary>Whether the Game view has its Gizmos toggle enabled, initially false.</summary>
        [Unbound.EditorFaithful("measured: tests/projects/editor sceneView.gizmos")]
        public static bool ShouldRenderGizmos() => Gizmos.GameViewEnabled;
    }
}

namespace UnityEditor
{
    [Serializable]
    internal abstract partial class PlayModeView : EditorWindow
    {
        protected bool showGizmos { get => UnityEngine.Gizmos.GameViewEnabled; set { UnityEngine.Gizmos.GameViewEnabled = value; } }
    }

    internal partial class GameView : PlayModeView
    {
        public GameView() { }
        internal bool m_Gizmos;
        internal bool drawGizmos { get => m_Gizmos; set { m_Gizmos = value; UnityEngine.Gizmos.GameViewEnabled = value; } }
    }
}
