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

        [Obsolete("onSceneGUIDelegate has been deprecated. Use duringSceneGui instead.")]
        public static OnSceneFunc onSceneGUIDelegate;

        public static ArrayList sceneViews => s_SceneViews;

        public static SceneView lastActiveSceneView => null;

        public static SceneView currentDrawingSceneView => null;

        public static Camera[] GetAllSceneCameras() => Array.Empty<Camera>();

        public static void RepaintAll() { }

        public static bool FrameLastActiveSceneView() => false;

        public static bool FrameLastActiveSceneViewWithLock() => false;

#pragma warning disable CS0067 // No scene view draws.
        public static event Action<SceneView> beforeSceneGui;
        public static event Action<SceneView> duringSceneGui;
#pragma warning restore CS0067

        public delegate void OnSceneFunc(SceneView sceneView);
    }

    public sealed partial class Handles
    {
        /// <summary>Whether the view being drawn shows gizmos: the game view, Unbound's only view,
        /// has its Gizmos toggle off (its default).</summary>
        [Unbound.EditorFaithful("measured: tests/projects/editor sceneView.gizmos")]
        public static bool ShouldRenderGizmos() => false;
    }
}
