using UnityEngine;

namespace UnityEditor.SceneManagement
{
    /// <summary>Stages: Play mode has only the main stage, which every camera renders.</summary>
    public static partial class StageUtility
    {
        public static bool IsGameObjectRenderedByCamera(GameObject gameObject, Camera camera) => gameObject != null;

        public static bool IsGameObjectRenderedByCameraAndPartOfEditableScene(GameObject gameObject, Camera camera) => gameObject != null;

        internal static bool IsGizmoCulledBySceneCullingMasksOrFocusedScene(GameObject gameObject, Camera camera) => false;
    }
}
