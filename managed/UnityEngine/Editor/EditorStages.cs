using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEditor.SceneManagement
{
    /// <summary>An editing stage (the main scenes, prefab mode).</summary>
    public abstract partial class Stage : ScriptableObject
    {
        internal abstract int sceneCount { get; }

        internal abstract Scene GetSceneAt(int index);

        [Unbound.EditorFaithful("a stage of no asset (the main stage) has no asset path; Prefab mode's has its prefab's")]
        public virtual string assetPath => "";

        public virtual StageHandle stageHandle => StageHandle.Main;

        /// <summary>A stage is the Editor's own object: hidden and never saved (a new MainStage reads
        /// hideFlags HideAndDontSave in the 6000.0.71f1 Editor, tests/projects/defaultstate; which
        /// callback sets it is not measured).</summary>
        protected virtual void OnEnable()
        {
            if (Unbound.Runtime.Compat.DefaultConstructorStateMeasured) hideFlags = HideFlags.HideAndDontSave;
        }

        /// <summary>Puts a root GameObject in one of the stage's scenes.</summary>
        internal abstract void PlaceGameObjectInStage(GameObject rootGameObject);

        internal virtual ulong GetSceneCullingMask() => 0;

        public virtual ulong GetCombinedSceneCullingMaskForCamera() => GetSceneCullingMask();

        /// <summary>The first component of the type in the stage's scenes (inactive objects included).</summary>
        public T FindComponentOfType<T>() where T : Component
        {
            var all = FindComponentsOfType<T>();
            return all.Length > 0 ? all[0] : null;
        }

        /// <summary>The components of the type in the stage's scenes, inactive objects included, in
        /// hierarchy order.</summary>
        public T[] FindComponentsOfType<T>() where T : Component
        {
            var found = new List<T>();
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            }
            return found.ToArray();
        }
    }

    /// <summary>The scene culling mask bits of the release (its declarations).</summary>
    public static partial class SceneCullingMasks
    {
        public const ulong DefaultSceneCullingMask = 16140901064495857664UL;
        public const ulong GameViewObjects = 9223372036854775808UL;
        public const ulong MainStageSceneViewObjects = 6917529027641081856UL;
        internal const ulong MainStageExcludingPrefabInstanceObjectsOpenInPrefabMode = 4611686018427387904UL;
        internal const ulong MainStagePrefabInstanceObjectsOpenInPrefabMode = 2305843009213693952UL;
        internal const ulong PrefabStagePrefabInstanceObjectsOpenInPrefabMode = 1152921504606846976UL;
    }

    /// <summary>The main stage: the scenes open in the Editor (the only stage Unbound's Editor has),
    /// whose cameras cull with the main stage's Scene view mask (measured: tests/projects/scenesave
    /// cull.combined).</summary>
    public sealed partial class MainStage : Stage
    {
        static MainStage s_Instance;

        internal static MainStage CreateMainStage()
        {
            var stage = CreateInstance<MainStage>();
            stage.hideFlags = HideFlags.HideAndDontSave;
            stage.name = "MainStage";
            return stage;
        }

        internal static MainStage Instance => s_Instance != null ? s_Instance : s_Instance = CreateMainStage();

        internal override int sceneCount => SceneManager.sceneCount;

        internal override Scene GetSceneAt(int index) => SceneManager.GetSceneAt(index);

        internal override ulong GetSceneCullingMask() => SceneCullingMasks.MainStageSceneViewObjects;

        /// <summary>A root not in one of the main stage's scenes goes to the active scene; one that
        /// is stays, and so does a GameObject with a parent or none (measured: tests/projects/scenesave
        /// stage.place*, no exception).</summary>
        internal override void PlaceGameObjectInStage(GameObject rootGameObject)
        {
            if (rootGameObject == null || rootGameObject.transform.parent != null) return;
            var scene = rootGameObject.scene;
            if (scene.IsValid() && scene.isLoaded) return;
            SceneManager.MoveGameObjectToScene(rootGameObject, SceneManager.GetActiveScene());
        }
    }

    /// <summary>A stage of one preview scene (Prefab mode's); Unbound's Editor opens none.</summary>
    public abstract partial class PreviewSceneStage : Stage
    {
        public Scene scene { get; protected set; }

        internal override int sceneCount => scene.IsValid() ? 1 : 0;

        internal override Scene GetSceneAt(int index) => index == 0 ? scene : default;

        internal override void PlaceGameObjectInStage(GameObject rootGameObject)
        {
            if (rootGameObject != null && rootGameObject.transform.parent == null && scene.IsValid() && rootGameObject.scene != scene)
                SceneManager.MoveGameObjectToScene(rootGameObject, scene);
        }
    }

    /// <summary>Prefab mode. Unbound's Editor never opens it (no stage of it exists); its
    /// members answer for the stage they describe.</summary>
    [global::System.Serializable]
    public sealed partial class PrefabStage : PreviewSceneStage
    {
        GameObject m_ContentsRoot;
        GameObject m_OpenedFromInstanceObject;
        Mode m_Mode;
        // A PrefabStage made with CreateInstance has no asset path: null (6000.0.71f1 Editor,
        // tests/projects/defaultstate); unmeasured releases keep "".
        string m_AssetPath = Unbound.Runtime.Compat.DefaultConstructorStateMeasured ? null : "";

        public enum Mode
        {
            InIsolation = 0,
            InContext = 1,
        }

        public GameObject prefabContentsRoot => m_ContentsRoot;

        public GameObject openedFromInstanceObject => m_OpenedFromInstanceObject;

        public GameObject openedFromInstanceRoot =>
            m_OpenedFromInstanceObject == null ? null : PrefabUtility.GetOutermostPrefabInstanceRoot(m_OpenedFromInstanceObject);

        public Mode mode => m_Mode;

        public override string assetPath => m_AssetPath;

        [Obsolete("prefabAssetPath has been deprecated. Use assetPath instead.")]
        public string prefabAssetPath => m_AssetPath;

        /// <summary>Whether the GameObject is the prefab's contents root or below it.</summary>
        public bool IsPartOfPrefabContents(GameObject gameObject) =>
            gameObject != null && m_ContentsRoot != null && gameObject.transform.IsChildOf(m_ContentsRoot.transform);
    }

    [Unbound.EditorFaithful("Prefab mode is not open while playing (as the batch-mode Editor)")]
    public static partial class PrefabStageUtility
    {
        public static PrefabStage GetCurrentPrefabStage() => null;

        public static PrefabStage GetPrefabStage(GameObject gameObject) => null;
    }
}
