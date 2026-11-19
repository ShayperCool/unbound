using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEditor.SceneManagement
{
    /// <summary>
    /// The Editor's scene management. Scenes open from their files and new untitled scenes are
    /// created as in the Editor; saving writes the scene's file as the Editor does
    /// (<see cref="SceneFiles"/>). Measured in the Unity 6000.0.71f1 Editor
    /// (tests/projects/scenesave): SaveScene writes the scene whether or not it is dirty, a copy
    /// keeps the scene's path, a new path becomes the scene's; SaveScenes and SaveOpenScenes write
    /// only the dirty ones; scripts changing objects do not dirty a scene; in Play mode saving and
    /// MarkSceneDirty throw. The dialog asking to save modified scenes is cancelled with the
    /// batch-mode Editor's assertion, so nothing is saved and the caller is told to stop.
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

        /// <summary>The preview scenes open: those of prefab contents loaded by script (no editor
        /// window, inspector preview or Prefab mode makes one, as in the batch-mode Editor).</summary>
        public static int previewSceneCount => PrefabContents.LoadedCount;

        [Unbound.EditorFaithful("scenes only reload from Edit mode (after an asset change), never while playing")]
        public static bool IsReloading(Scene scene) => false;

        /// <summary>The scene culling mask of a scene of the main stage, the default one (measured:
        /// tests/projects/scenesave cull.mask); none for an invalid scene (unmeasured).</summary>
        public static ulong GetSceneCullingMask(Scene scene) => scene.IsValid() ? SceneCullingMasks.DefaultSceneCullingMask : 0;

        /// <summary>Unbound's Editor opens preview scenes only for prefab contents loaded by script
        /// (PrefabUtility.LoadPrefabContents, measured: tests/projects/scenesave prefab-writes
        /// contentsCopy.preview); no scene opened as a document is one, and a prefab asset's object
        /// is in none (measured: tests/projects/scenesave preview.*).</summary>
        public static bool IsPreviewScene(Scene scene) => scene.Data?.IsPreview == true;

        public static bool IsPreviewSceneObject(Object obj) =>
            (obj as GameObject ?? (obj as Component)?.gameObject) is GameObject go && go.scene.Data?.IsPreview == true;

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
                // As the Editor's new scene (tests/projects/scenesave Fresh.unity): a camera drawn
                // first and a warm directional light with soft shadows.
                var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
                camera.transform.position = new Vector3(0f, 1f, -10f);
                camera.GetComponent<Camera>().depth = -1f;
                var light = new GameObject("Directional Light", typeof(Light));
                var angles = new Vector3(50f, -30f, 0f);
                light.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(angles));
                light.transform.SetLocalEulerHint(angles);
                var l = light.GetComponent<Light>();
                l.type = LightType.Directional;
                l.color = new Color(1f, 0.95686275f, 0.8392157f, 1f);
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
            // The scenes a Single open replaces are closing, then closed as they go.
            if (mode == OpenSceneMode.Single)
                foreach (var data in SceneRegistry.Loaded.ToArray())
                    sceneClosing?.Invoke(new Scene(data.Handle), true);
            var scene = SceneManagerInternal.OpenPath(scenePath, mode != OpenSceneMode.Single);
            sceneOpened?.Invoke(scene, mode);
            return scene;
        }

        /// <summary>
        /// Reloads a saved scene from its file, its changes discarded (the Test Framework reloads
        /// the saved scenes a test left dirty). Measured for the only loaded scene
        /// (tests/projects/scenesave prefab-writes reload*): it opens again as OpenScene opens a
        /// scene alone (sceneOpening, sceneClosing, sceneClosed, sceneOpened), dirty or not; the
        /// Scene passed in is no longer valid; one ChangeScene change of the new scene is published;
        /// the undo history is gone; an untitled scene throws; an invalid one answers false. Another
        /// scene among several reloads in its place, the active one staying active (unmeasured).
        /// </summary>
        internal static bool ReloadScene(Scene scene)
        {
            var data = scene.Data;
            if (data == null) return false;
            if (string.IsNullOrEmpty(data.Path))
                throw new ArgumentException("ReloadScene cannot be used with a scene that haven't been saved yet: " + data.Name);
            if (UnityEngine.Application.isPlaying) throw new InvalidOperationException(PlayModeRefusal);
            var loaded = SceneRegistry.Loaded;
            int index = loaded.IndexOf(data);
            bool alone = loaded.Count(s => s.IsLoaded) <= 1;
            bool active = SceneRegistry.Active == data;
            Undo.ClearAll();
            Scene reloaded;
            if (alone) reloaded = OpenScene(data.Path, OpenSceneMode.Single);
            else
            {
                CloseScene(scene, true);
                reloaded = OpenScene(data.Path, OpenSceneMode.Additive);
                var opened = reloaded.Data;
                if (opened != null && index >= 0 && loaded.Remove(opened)) loaded.Insert(Math.Min(index, loaded.Count), opened);
                if (active && opened != null) SceneRegistry.Active = opened;
            }
            ObjectChangeEvents.Note(ObjectChangeKind.ChangeScene, new ChangeSceneEventArgs(reloaded));
            return true;
        }

        internal static void Internal_SceneClosed(Scene scene) => sceneClosed?.Invoke(scene);

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
            if (UnityEngine.Application.isPlaying) throw new InvalidOperationException(PlayModeRefusal);
            Undo.FlushDirtiness();
            var data = scene.Data;
            if (data == null) return false;
            var path = string.IsNullOrEmpty(dstScenePath) ? data.Path : dstScenePath;
            if (string.IsNullOrEmpty(path) || !SceneFiles.CanSave(data, path)) return false;
            path = SceneFiles.ProjectRelative(path);
            sceneSaving?.Invoke(scene, path);
            if (!SceneFiles.Save(data, path)) return false;
            if (!saveAsCopy)
            {
                data.Path = path;
                data.Name = System.IO.Path.GetFileNameWithoutExtension(path);
                data.IsDirty = false;
            }
            sceneSaved?.Invoke(scene);
            return true;
        }

        const string PlayModeRefusal = "This cannot be used during play mode.";

        /// <summary>Saves the dirty ones of the scenes.</summary>
        public static bool SaveScenes(Scene[] scenes)
        {
            if (scenes == null) throw new ArgumentNullException(nameof(scenes));
            if (UnityEngine.Application.isPlaying) throw new InvalidOperationException(PlayModeRefusal);
            Undo.FlushDirtiness();
            bool all = true;
            foreach (var s in scenes)
                if (s.Data is SceneData data && data.IsDirty) all &= SaveScene(s);
            return all;
        }

        public static bool SaveOpenScenes() => SaveScenes(SceneRegistry.Loaded.Where(s => s.IsLoaded).Select(s => new Scene(s.Handle)).ToArray());

        public static bool SaveCurrentModifiedScenesIfUserWantsTo() =>
            SaveModifiedScenesIfUserWantsTo(SceneRegistry.Loaded.Select(s => new Scene(s.Handle)).ToArray());

        /// <summary>With modified scenes, the dialog asking to save them is cancelled as the
        /// batch-mode Editor cancels it (its assertion measured in tests/projects/scenesave for one
        /// scene; each scene's name is one line of the message): nothing is saved and the answer is
        /// false, to stop what asked.</summary>
        public static bool SaveModifiedScenesIfUserWantsTo(Scene[] scenes)
        {
            if (scenes == null) throw new ArgumentNullException(nameof(scenes));
            Undo.FlushDirtiness();
            var modified = new List<string>();
            foreach (var s in scenes)
                if (s.Data is SceneData data && data.IsDirty) modified.Add(data.Name.Length > 0 ? data.Name : "Untitled");
            if (modified.Count == 0) return true;
            var names = string.Concat(modified.Select(n => "\n " + n));
            // The scene dialog's own text, with the batch-mode Editor's suffix (EditorUtility.DisplayDialog's
            // measured assertion has none).
            Debug.LogAssertion($"Cancelling DisplayDialog: Scene(s) Have Been Modified Do you want to save the changes you made in the scenes:{names}\n\nYour changes will be lost if you don't save them.\nThis should not be called in batch mode.");
            return false;
        }

        /// <summary>A change to one of the scene's objects (EditorUtility.SetDirty): the scene is
        /// dirty, sceneDirtied raised when it was not.</summary>
        internal static void Dirty(SceneData data)
        {
            if (data.IsDirty || !data.IsLoaded) return;
            data.IsDirty = true;
            sceneDirtied?.Invoke(new Scene(data.Handle));
        }

        public static bool MarkSceneDirty(Scene scene)
        {
            if (UnityEngine.Application.isPlaying) throw new InvalidOperationException(PlayModeRefusal);
            var data = scene.Data;
            if (data == null) return false;
            data.IsDirty = true;
            sceneDirtied?.Invoke(scene);
            // A change of the scene (measured: tests/projects/scenesave prefab-writes reload.event,
            // published after the scene was reloaded, so for a scene no longer valid).
            ObjectChangeEvents.Note(ObjectChangeKind.ChangeScene, new ChangeSceneEventArgs(scene));
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
            return SceneManagerInternal.OpenPath(path, parameters.loadSceneMode == LoadSceneMode.Additive, opened: false);
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
