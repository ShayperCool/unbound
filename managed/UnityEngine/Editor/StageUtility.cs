using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEditor.SceneManagement
{
    /// <summary>Stages: Play mode has only the main stage, which every camera renders.</summary>
    public static partial class StageUtility
    {
        public static bool IsGameObjectRenderedByCamera(GameObject gameObject, Camera camera) => gameObject != null;

        public static bool IsGameObjectRenderedByCameraAndPartOfEditableScene(GameObject gameObject, Camera camera) => gameObject != null;

        internal static bool IsGizmoCulledBySceneCullingMasksOrFocusedScene(GameObject gameObject, Camera camera) => false;

        /// <summary>The main stage: the current one, and every scene object's (measured:
        /// tests/projects/scenesave stage.*); an object of no scene (an asset) has none.</summary>
        public static Stage GetCurrentStage() => MainStage.Instance;

        public static MainStage GetMainStage() => MainStage.Instance;

        public static Stage GetStage(GameObject gameObject) => gameObject != null && gameObject.scene.IsValid() ? MainStage.Instance : null;

        public static Stage GetStage(Scene scene) => scene.IsValid() ? MainStage.Instance : null;

        [Unbound.EditorFaithful("the main stage is always the current one")]
        public static void GoToMainStage() { }

        [Unbound.EditorFaithful("the main stage is always the current one: there is no previous stage")]
        public static void GoBackToPreviousStage() { }

        public static void PlaceGameObjectInCurrentStage(GameObject gameObject) => MainStage.Instance.PlaceGameObjectInStage(gameObject);
    }
}
