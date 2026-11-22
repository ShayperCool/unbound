using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEditor
{
    /// <summary>
    /// The Scene view's hidden and unpickable objects, kept by GameObject (matches the Unity
    /// 6000.0.71f1 Editor in Play mode, tests/projects/scenevisibility). Unbound shows no Scene
    /// view: hiding changes what the Scene view's renderer filters answer
    /// (Rendering.EditorCameraUtils.GetRenderersHiddenResultBits: tests/projects/graphicsgaps
    /// cameraUtils.hidden*), not what cameras draw.
    /// <para>An object's own state and its descendants' are separate: "with descendants" answers
    /// need the object and every descendant; "all descendants" answers leave the object out (an
    /// object without children has no hidden descendants and only visible ones). Isolation keeps
    /// a hidden set of its own, which starts with everything hidden but the isolated objects and
    /// is dropped on exit, so the earlier visibility comes back. Of the events only showing a scene
    /// raises visibilityChanged (at once); nothing here raises pickingChanged. Changes are not
    /// recorded for undo (unmeasured).</para>
    /// </summary>
    public partial class SceneVisibilityManager : ScriptableSingleton<SceneVisibilityManager>
    {
        internal enum SceneVisState
        {
            AllHidden,
            AllVisible,
            Mixed,
        }

        internal enum ScenePickingState
        {
            PickingDisabledAll,
            PickingEnabledAll,
            Mixed,
        }

        public static event Action visibilityChanged;
        public static event Action pickingChanged;
        internal static event Action<bool> currentStageIsIsolated;

        /// <summary>Hidden objects outside isolation, and inside it.</summary>
        static readonly HashSet<int> s_Hidden = new HashSet<int>(), s_IsolationHidden = new HashSet<int>();
        static readonly HashSet<int> s_PickingDisabled = new HashSet<int>();
        static bool s_Isolated, s_VisibilityActive = true, s_PickingActive = true;

        static HashSet<int> Hidden => s_Isolated ? s_IsolationHidden : s_Hidden;

        internal bool enableSceneVisibility
        {
            get => s_VisibilityActive;
            set => s_VisibilityActive = value;
        }

        internal bool enableScenePicking
        {
            get => s_PickingActive;
            set => s_PickingActive = value;
        }

        internal static bool IsHiddenObject(GameObject gameObject) => gameObject != null && Hidden.Contains(gameObject.GetInstanceID());

        static bool IsPickingDisabledObject(GameObject gameObject) => gameObject != null && s_PickingDisabled.Contains(gameObject.GetInstanceID());

        /// <summary>Adds the object (and, with descendants, its hierarchy) to a set or takes it out.</summary>
        static void Set(HashSet<int> set, GameObject gameObject, bool on, bool includeDescendants)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            Mark(set, gameObject, on, includeDescendants);
        }

        static void Mark(HashSet<int> set, GameObject gameObject, bool on, bool includeDescendants)
        {
            if (on) set.Add(gameObject.GetInstanceID());
            else set.Remove(gameObject.GetInstanceID());
            if (!includeDescendants) return;
            foreach (Transform child in gameObject.transform) Mark(set, child.gameObject, on, true);
        }

        static void Set(HashSet<int> set, GameObject[] gameObjects, bool on, bool includeDescendants)
        {
            if (gameObjects == null) throw new ArgumentNullException(nameof(gameObjects));
            foreach (var gameObject in gameObjects)
                if (gameObject != null) Mark(set, gameObject, on, includeDescendants);
        }

        /// <summary>Whether the object and, with descendants, every descendant is in the set.</summary>
        static bool Holds(HashSet<int> set, GameObject gameObject, bool includeDescendants)
        {
            if (gameObject == null || !set.Contains(gameObject.GetInstanceID())) return false;
            return !includeDescendants || AllDescendants(set, gameObject, true);
        }

        /// <summary>Whether every descendant of the object (not the object) is in the set, or, with
        /// <paramref name="inSet"/> false, outside it.</summary>
        static bool AllDescendants(HashSet<int> set, GameObject gameObject, bool inSet)
        {
            foreach (Transform child in gameObject.transform)
            {
                if (set.Contains(child.gameObject.GetInstanceID()) != inSet) return false;
                if (!AllDescendants(set, child.gameObject, inSet)) return false;
            }
            return true;
        }

        static bool AnyInSet(HashSet<int> set, GameObject gameObject)
        {
            if (set.Contains(gameObject.GetInstanceID())) return true;
            foreach (Transform child in gameObject.transform)
                if (AnyInSet(set, child.gameObject)) return true;
            return false;
        }

        static bool Loaded(Scene scene) => scene.IsValid() && scene.isLoaded;

        static void ForScene(HashSet<int> set, Scene scene, bool on)
        {
            foreach (var root in scene.GetRootGameObjects()) Mark(set, root, on, true);
        }

        /// <summary>Whether every root of the scene and all its descendants are in the set (true for
        /// a scene without roots).</summary>
        static bool AllRoots(HashSet<int> set, Scene scene)
        {
            if (!Loaded(scene)) return false;
            foreach (var root in scene.GetRootGameObjects())
                if (!Holds(set, root, true)) return false;
            return true;
        }

        static bool AnyInScene(HashSet<int> set, Scene scene)
        {
            if (!Loaded(scene) || set.Count == 0) return false;
            foreach (var root in scene.GetRootGameObjects())
                if (AnyInSet(set, root)) return true;
            return false;
        }

        public void Hide(GameObject gameObject, bool includeDescendants) => Set(Hidden, gameObject, true, includeDescendants);

        public void Show(GameObject gameObject, bool includeDescendants) => Set(Hidden, gameObject, false, includeDescendants);

        public void Hide(GameObject[] gameObjects, bool includeDescendants) => Set(Hidden, gameObjects, true, includeDescendants);

        public void Show(GameObject[] gameObjects, bool includeDescendants) => Set(Hidden, gameObjects, false, includeDescendants);

        public void ToggleVisibility(GameObject gameObject, bool includeDescendants) =>
            Set(Hidden, gameObject, !IsHiddenObject(gameObject), includeDescendants);

        public void DisablePicking(GameObject gameObject, bool includeDescendants) => Set(s_PickingDisabled, gameObject, true, includeDescendants);

        public void EnablePicking(GameObject gameObject, bool includeDescendants) => Set(s_PickingDisabled, gameObject, false, includeDescendants);

        public void DisablePicking(GameObject[] gameObjects, bool includeDescendants) => Set(s_PickingDisabled, gameObjects, true, includeDescendants);

        public void EnablePicking(GameObject[] gameObjects, bool includeDescendants) => Set(s_PickingDisabled, gameObjects, false, includeDescendants);

        public void TogglePicking(GameObject gameObject, bool includeDescendants) =>
            Set(s_PickingDisabled, gameObject, !IsPickingDisabledObject(gameObject), includeDescendants);

        /// <summary>Shows the scene's objects and raises visibilityChanged.</summary>
        public void Show(Scene scene)
        {
            if (!Loaded(scene)) return;
            ForScene(Hidden, scene, false);
            visibilityChanged?.Invoke();
        }

        public void Hide(Scene scene)
        {
            if (Loaded(scene)) ForScene(Hidden, scene, true);
        }

        public void EnablePicking(Scene scene)
        {
            if (Loaded(scene)) ForScene(s_PickingDisabled, scene, false);
        }

        public void DisablePicking(Scene scene)
        {
            if (Loaded(scene)) ForScene(s_PickingDisabled, scene, true);
        }

        static void ForLoadedScenes(Action<Scene> action)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (Loaded(scene)) action(scene);
            }
        }

        public void HideAll() => ForLoadedScenes(scene => ForScene(Hidden, scene, true));

        /// <summary>Shows every scene's objects, without visibilityChanged.</summary>
        public void ShowAll() => ForLoadedScenes(scene => ForScene(Hidden, scene, false));

        public void DisableAllPicking() => ForLoadedScenes(scene => ForScene(s_PickingDisabled, scene, true));

        public void EnableAllPicking() => ForLoadedScenes(scene => ForScene(s_PickingDisabled, scene, false));

        public bool IsHidden(GameObject gameObject, bool includeDescendants = false) => Holds(Hidden, gameObject, includeDescendants);

        public bool IsHidden(Scene scene) => AllRoots(Hidden, scene);

        public bool IsPickingDisabled(GameObject gameObject, bool includeDescendants = false) => Holds(s_PickingDisabled, gameObject, includeDescendants);

        public bool IsPickingDisabled(Scene scene) => AllRoots(s_PickingDisabled, scene);

        internal bool IsSelectable(GameObject go) => IsSelectable(go, false);

        internal bool IsSelectable(GameObject go, bool includeDescendants) => !IsPickingDisabled(go, includeDescendants) && !IsHidden(go, includeDescendants);

        public bool AreAllDescendantsHidden(Scene scene) => AllRoots(Hidden, scene);

        public bool AreAnyDescendantsHidden(Scene scene) => AnyInScene(Hidden, scene);

        public bool IsPickingDisabledOnAllDescendants(Scene scene) => AllRoots(s_PickingDisabled, scene);

        public bool IsPickingDisabledOnAnyDescendant(Scene scene) => AnyInScene(s_PickingDisabled, scene);

        /// <summary>Whether the object has descendants and all are hidden.</summary>
        public bool AreAllDescendantsHidden(GameObject gameObject) =>
            gameObject != null && gameObject.transform.childCount > 0 && AllDescendants(Hidden, gameObject, true);

        public bool AreAllDescendantsVisible(GameObject gameObject) => gameObject != null && AllDescendants(Hidden, gameObject, false);

        public bool IsPickingDisabledOnAllDescendants(GameObject gameObject) =>
            gameObject != null && gameObject.transform.childCount > 0 && AllDescendants(s_PickingDisabled, gameObject, true);

        public bool IsPickingEnabledOnAllDescendants(GameObject gameObject) => gameObject != null && AllDescendants(s_PickingDisabled, gameObject, false);

        internal SceneVisState GetSceneVisibilityState(Scene scene) =>
            AreAllDescendantsHidden(scene) ? SceneVisState.AllHidden : AreAnyDescendantsHidden(scene) ? SceneVisState.Mixed : SceneVisState.AllVisible;

        internal ScenePickingState GetScenePickingState(Scene scene) =>
            IsPickingDisabledOnAllDescendants(scene) ? ScenePickingState.PickingDisabledAll
            : IsPickingDisabledOnAnyDescendant(scene) ? ScenePickingState.Mixed : ScenePickingState.PickingEnabledAll;

        public bool IsCurrentStageIsolated() => s_Isolated;

        /// <summary>Isolates the objects: everything else is hidden until isolation ends.</summary>
        public void Isolate(GameObject gameObject, bool includeDescendants)
        {
            BeginIsolation();
            Set(Hidden, gameObject, false, includeDescendants);
        }

        public void Isolate(GameObject[] gameObjects, bool includeDescendants)
        {
            BeginIsolation();
            Set(Hidden, gameObjects, false, includeDescendants);
        }

        void BeginIsolation()
        {
            s_Isolated = true;
            currentStageIsIsolated?.Invoke(true);
            s_IsolationHidden.Clear();
            HideAll();
        }

        /// <summary>Ends isolation: the visibility from before it is back.</summary>
        public void ExitIsolation()
        {
            s_Isolated = false;
            s_IsolationHidden.Clear();
            currentStageIsIsolated?.Invoke(false);
        }
    }
}
