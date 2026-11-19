using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEditor
{
    /// <summary>The Scene view's hidden objects, kept by GameObject. Unbound shows no Scene view:
    /// hiding changes what the Scene view's renderer filters answer
    /// (Rendering.EditorCameraUtils.GetRenderersHiddenResultBits: tests/projects/graphicsgaps
    /// cameraUtils.hidden*), not what cameras draw.</summary>
    public partial class SceneVisibilityManager : ScriptableSingleton<SceneVisibilityManager>
    {
        static readonly HashSet<int> s_Hidden = new HashSet<int>();

        internal static bool IsHiddenObject(GameObject gameObject) => gameObject != null && s_Hidden.Contains(gameObject.GetInstanceID());

        static void Visit(GameObject gameObject, bool includeDescendants, bool hide)
        {
            if (gameObject == null) return;
            if (hide) s_Hidden.Add(gameObject.GetInstanceID());
            else s_Hidden.Remove(gameObject.GetInstanceID());
            if (!includeDescendants) return;
            foreach (Transform child in gameObject.transform) Visit(child.gameObject, true, hide);
        }

        public void Hide(GameObject gameObject, bool includeDescendants) => Visit(gameObject, includeDescendants, true);

        public void Show(GameObject gameObject, bool includeDescendants) => Visit(gameObject, includeDescendants, false);

        public void Hide(GameObject[] gameObjects, bool includeDescendants)
        {
            if (gameObjects == null) return;
            foreach (var gameObject in gameObjects) Visit(gameObject, includeDescendants, true);
        }

        public void Show(GameObject[] gameObjects, bool includeDescendants)
        {
            if (gameObjects == null) return;
            foreach (var gameObject in gameObjects) Visit(gameObject, includeDescendants, false);
        }

        public bool IsHidden(GameObject gameObject, bool includeDescendants = false)
        {
            if (!IsHiddenObject(gameObject)) return false;
            if (!includeDescendants) return true;
            foreach (Transform child in gameObject.transform)
                if (!IsHidden(child.gameObject, true)) return false;
            return true;
        }

        /// <summary>Whether an object of the scene is hidden.</summary>
        public bool AreAnyDescendantsHidden(Scene scene)
        {
            if (!scene.IsValid() || s_Hidden.Count == 0) return false;
            foreach (var root in scene.GetRootGameObjects())
                if (AnyHidden(root)) return true;
            return false;
        }

        static bool AnyHidden(GameObject gameObject)
        {
            if (IsHiddenObject(gameObject)) return true;
            foreach (Transform child in gameObject.transform)
                if (AnyHidden(child.gameObject)) return true;
            return false;
        }
    }
}
