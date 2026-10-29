using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.SceneManagement;

namespace UnityEditor.SceneManagement
{
    /// <summary>
    /// The Editor's scene management. Scenes open from their files and new untitled scenes are
    /// created as in the Editor; saving keeps a scene's new path and clears its dirty flag but
    /// writes nothing (Unbound never writes a project's scenes), so a scene "saved" to a new path
    /// lives in memory only. Dialogs asking to save modified scenes answer yes without saving.
    /// </summary>
    public sealed partial class EditorSceneManager : SceneManager
    {
        public delegate void SceneManagerSetupRestoredCallback(Scene[] scenes);
        public delegate void NewSceneCreatedCallback(Scene scene, NewSceneSetup setup, NewSceneMode mode);
        public delegate void SceneOpeningCallback(string path, OpenSceneMode mode);
        public delegate void SceneOpenedCallback(Scene scene, OpenSceneMode mode);
        public delegate void SceneClosingCallback(Scene scene, bool removingScene);
        public delegate void SceneClosedCallback(Scene scene);
        public delegate void SceneSavingCallback(Scene scene, string path);
        public delegate void SceneSavedCallback(Scene scene);
        public delegate void SceneDirtiedCallback(Scene scene);

        public static event SceneManagerSetupRestoredCallback sceneManagerSetupRestored;
        public static event NewSceneCreatedCallback newSceneCreated;
        public static event SceneOpeningCallback sceneOpening;
        public static event SceneOpenedCallback sceneOpened;
        public static event SceneClosingCallback sceneClosing;
        public static event SceneClosedCallback sceneClosed;
        public static event SceneSavingCallback sceneSaving;
        public static event SceneSavedCallback sceneSaved;
        public static event SceneDirtiedCallback sceneDirtied;
#pragma warning disable CS0067 // Edit mode scene changes are not tracked.
        public static event UnityEngine.Events.UnityAction<Scene, Scene> activeSceneChangedInEditMode;
#pragma warning restore CS0067

        public static bool preventCrossSceneReferences { get; set; } = true;

        public static SceneAsset playModeStartScene { get; set; }

        public static int loadedRootSceneCount => SceneRegistry.Loaded.Count(s => s.IsLoaded);

        [Unbound.EditorFaithful("no preview scene is open: only editor windows (inspector previews, Prefab mode) make them, as in the batch-mode Editor")]
        public static int previewSceneCount => 0;

        [Unbound.EditorFaithful("scenes only reload from Edit mode (after an asset change), never while playing")]
        public static bool IsReloading(Scene scene) => false;

        public static Scene NewScene(NewSceneSetup setup) => NewScene(setup, NewSceneMode.Single);

        public static Scene NewScene(NewSceneSetup setup, [DefaultValue("NewSceneMode.Single")] NewSceneMode mode)
        {
            if (mode == NewSceneMode.Single)
                foreach (var data in SceneRegistry.Loaded.ToArray())
                    sceneClosing?.Invoke(new Scene(data.Handle), true);
            var scene = SceneManagerInternal.NewEmpty(mode == NewSceneMode.Additive);
            if (setup == NewSceneSetup.DefaultGameObjects)
            {
                var previous = SceneRegistry.Active;
                SceneRegistry.Active = scene.Data;
                var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
                camera.transform.position = new Vector3(0f, 1f, -10f);
                var light = new GameObject("Directional Light", typeof(Light));
                light.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(50f, -30f, 0f));
                var l = light.GetComponent<Light>();
                l.type = LightType.Directional;
                l.shadows = LightShadows.Soft;
                if (mode == NewSceneMode.Additive && previous != null) SceneRegistry.Active = previous;
            }
            newSceneCreated?.Invoke(scene, setup, mode);
            return scene;
        }

        public static Scene OpenScene(string scenePath) => OpenScene(scenePath, OpenSceneMode.Single);

        public static Scene OpenScene(string scenePath, [DefaultValue("OpenSceneMode.Single")] OpenSceneMode mode)
        {
            if (string.IsNullOrEmpty(scenePath)) throw new ArgumentException("Scene path cannot be null or empty.", nameof(scenePath));
            if (UnityEngine.Application.isPlaying)
                throw new InvalidOperationException("This cannot be used during play mode, please use SceneManager.LoadScene()/SceneManager.LoadSceneAsync() instead.");
            sceneOpening?.Invoke(scenePath, mode);
            var scene = SceneManagerInternal.OpenPath(scenePath, mode != OpenSceneMode.Single);
            sceneOpened?.Invoke(scene, mode);
            return scene;
        }

        public static bool CloseScene(Scene scene, bool removeScene)
        {
            var data = scene.Data;
            if (data == null) return false;
            sceneClosing?.Invoke(scene, removeScene);
            var closed = SceneManagerInternal.Close(data, removeScene);
            if (closed) sceneClosed?.Invoke(scene);
            return closed;
        }

        public static bool SaveScene(Scene scene) => SaveScene(scene, "", false);

        public static bool SaveScene(Scene scene, string dstScenePath) => SaveScene(scene, dstScenePath, false);

        public static bool SaveScene(Scene scene, [DefaultValue("\"\"")] string dstScenePath, [DefaultValue("false")] bool saveAsCopy)
        {
            var data = scene.Data;
            if (data == null) return false;
            var path = string.IsNullOrEmpty(dstScenePath) ? data.Path : dstScenePath;
            if (string.IsNullOrEmpty(path)) return false;
            sceneSaving?.Invoke(scene, path);
            if (!saveAsCopy)
            {
                data.Path = path;
                data.Name = System.IO.Path.GetFileNameWithoutExtension(path);
                data.IsDirty = false;
            }
            sceneSaved?.Invoke(scene);
            return true;
        }

        public static bool SaveScenes(Scene[] scenes)
        {
            if (scenes == null) throw new ArgumentNullException(nameof(scenes));
            bool all = true;
            foreach (var s in scenes) all &= SaveScene(s);
            return all;
        }

        public static bool SaveOpenScenes() => SaveScenes(SceneRegistry.Loaded.Where(s => s.Path.Length > 0).Select(s => new Scene(s.Handle)).ToArray());

        public static bool SaveCurrentModifiedScenesIfUserWantsTo()
        {
            Stubs.Ignored("EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo", "asks nothing and saves nothing (answered as if nothing needed saving)");
            return true;
        }

        public static bool SaveModifiedScenesIfUserWantsTo(Scene[] scenes)
        {
            if (scenes == null) throw new ArgumentNullException(nameof(scenes));
            return true;
        }

        public static bool MarkSceneDirty(Scene scene)
        {
            var data = scene.Data;
            if (data == null || UnityEngine.Application.isPlaying) return false;
            data.IsDirty = true;
            sceneDirtied?.Invoke(scene);
            return true;
        }

        public static void MarkAllScenesDirty()
        {
            foreach (var data in SceneRegistry.Loaded) MarkSceneDirty(new Scene(data.Handle));
        }

        public static SceneSetup[] GetSceneManagerSetup() =>
            SceneRegistry.Loaded.Select(s => new SceneSetup
            {
                path = s.Path,
                isLoaded = s.IsLoaded,
                isActive = s == SceneRegistry.Active,
            }).ToArray();

        /// <summary>Opens the scenes a setup lists (untitled ones come back empty).</summary>
        public static void RestoreSceneManagerSetup(SceneSetup[] value)
        {
            if (value == null || value.Length == 0) throw new ArgumentException("Scene Manager Setup cannot be empty.", nameof(value));
            Scene active = default;
            bool first = true;
            foreach (var setup in value)
            {
                Scene scene;
                if (!string.IsNullOrEmpty(setup.path) && System.IO.File.Exists(System.IO.Path.Combine(UnityEngine.ProjectInfo.ProjectPath, setup.path)))
                    scene = SceneManagerInternal.OpenPath(setup.path, !first);
                else
                    scene = SceneManagerInternal.NewEmpty(!first);
                first = false;
                if (setup.isActive) active = scene;
            }
            if (active.IsValid()) SetActiveScene(active);
            sceneManagerSetupRestored?.Invoke(SceneRegistry.Loaded.Select(s => new Scene(s.Handle)).ToArray());
        }

        public static bool DetectCrossSceneReferences(Scene scene)
        {
            Stubs.Ignored("EditorSceneManager.DetectCrossSceneReferences", "does not look for cross-scene references");
            return false;
        }

        public static Scene LoadSceneInPlayMode(string path, LoadSceneParameters parameters)
        {
            if (!UnityEngine.Application.isPlaying) throw new InvalidOperationException("This can only be used during play mode, please use EditorSceneManager.OpenScene() instead.");
            return SceneManagerInternal.OpenPath(path, parameters.loadSceneMode == LoadSceneMode.Additive);
        }

        public static AsyncOperation LoadSceneAsyncInPlayMode(string path, LoadSceneParameters parameters)
        {
            LoadSceneInPlayMode(path, parameters);
            return AsyncOperation.Completed();
        }
    }

    [Serializable]
    public partial class SceneSetup
    {
        [SerializeField] string m_Path;
        [SerializeField] bool m_IsLoaded;
        [SerializeField] bool m_IsActive;
        [SerializeField] bool m_IsSubScene;

        public string path { get => m_Path; set => m_Path = value; }
        public bool isLoaded { get => m_IsLoaded; set => m_IsLoaded = value; }
        public bool isActive { get => m_IsActive; set => m_IsActive = value; }
        public bool isSubScene { get => m_IsSubScene; set => m_IsSubScene = value; }
    }

    public enum NewSceneSetup
    {
        EmptyScene,
        DefaultGameObjects,
    }

    public enum NewSceneMode
    {
        Single,
        Additive,
    }

    public enum OpenSceneMode
    {
        Single,
        Additive,
        AdditiveWithoutLoading,
    }
}

namespace UnityEditor
{
    /// <summary>A scene file as an asset.</summary>
    public partial class SceneAsset : UnityEngine.Object
    {
        internal SceneAsset() { }
    }
}
