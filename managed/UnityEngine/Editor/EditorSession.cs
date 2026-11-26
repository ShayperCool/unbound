using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// Edit mode and Play mode of Unbound's Editor. `unbound run` starts in Play mode and leaving it
    /// ends the session; `unbound test` starts in Edit mode, where only the Editor's own updates run
    /// (no scene plays, scripts without [ExecuteAlways]/[ExecuteInEditMode] are not awakened), and
    /// editor code enters and leaves Play mode (<c>EditorApplication.isPlaying</c>) as in the
    /// Editor with "Enter Play Mode Options" skipping the domain reload: the objects of the open
    /// scenes wake up; on leaving, everything of Play mode is destroyed and an empty scene opens.
    /// </summary>
    internal static class EditorSession
    {
        /// <summary>The session started in Edit mode (`unbound test`).</summary>
        internal static bool StartedInEditMode;

        internal static bool IsPlaying = true;

        /// <summary>1: enter Play mode on the next update; -1: leave it.</summary>
        static int s_Request;
        static bool s_SingleSceneOpenedThisEditorUpdate;

        internal static void NoteSingleSceneOpened() => s_SingleSceneOpenedThisEditorUpdate = true;

        // Requests from an update are applied before the next Edit mode update starts.
        internal static void BeginEditModeFrame() => s_SingleSceneOpenedThisEditorUpdate = false;

        internal static bool WillChange => s_Request != 0 || s_EnteringPlayMode || s_LeavingPlayMode;

        /// <summary>Play mode is being left: `isPlaying` is still true and
        /// `isPlayingOrWillChangePlaymode` already false, from ExitingPlayMode until Edit mode
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/unchangedsaves open4.leaving:
        /// Application.quitting sees it false).</summary>
        static bool s_LeavingPlayMode;

        internal static void BeginLeavingPlayMode() => s_LeavingPlayMode = IsPlaying;

        internal static void EndLeavingPlayMode()
        {
            IsPlaying = false;
            s_LeavingPlayMode = false;
        }

        /// <summary>The editor domain is loading for Play mode: `isPlaying` is still false and
        /// `isPlayingOrWillChangePlaymode` already true, until the runtime initialization starts
        /// (measured: tests/projects/editorcallbacks).</summary>
        static bool s_EnteringPlayMode;

        internal static void BeginEnteringPlayMode()
        {
            IsPlaying = false;
            s_EnteringPlayMode = true;
            // Entering Play mode answers a request to enter it (one an Edit-mode load made).
            if (s_Request > 0) s_Request = 0;
        }

        /// <summary>Runs what the Editor does in Edit mode before entering Play mode (the start's
        /// import, which Unbound's single editor domain does as Play mode is being entered):
        /// <c>isPlaying</c> and <c>isPlayingOrWillChangePlaymode</c> answer false meanwhile, and a
        /// request to enter Play mode made then is the entry already under way.</summary>
        internal static void AsEditMode(Action action)
        {
            bool playing = IsPlaying, entering = s_EnteringPlayMode;
            IsPlaying = false;
            s_EnteringPlayMode = false;
            try { action(); }
            finally
            {
                IsPlaying = playing;
                s_EnteringPlayMode = entering;
                if (entering && s_Request > 0) s_Request = 0;
            }
        }

        /// <summary>Play mode's build list, fixed as Play mode starts after Edit mode's editor code
        /// has run (a list it set counts): the build settings' scenes and the open ones, which take
        /// their indices in it (tests/projects/scenesave build-index-play start.*).</summary>
        internal static void FixPlayModeBuildList()
        {
            var open = new List<string>();
            foreach (var scene in SceneManagerInternal.Listed())
                if (scene.IsLoaded && scene.LoadKey == null) open.Add(scene.Path);
            EditorBuildSettings.BeginPlayModeList(open);
            EditorBuildSettings.RefreshOpenSceneIndices();
        }

        internal static void EndEnteringPlayMode()
        {
            s_EnteringPlayMode = false;
            IsPlaying = true;
        }

        internal static void Request(bool play)
        {
            // A request to enter Play mode while entering it is ignored: nothing stays pending
            // (matches the Unity 6000.0.71f1 Editor, tests/projects/toolapis playRequest.seen).
            if (play && s_EnteringPlayMode) return;
            if (play == IsPlaying)
            {
                s_Request = 0;
                return;
            }
            if (!play && !StartedInEditMode)
            {
                // Play mode is the whole session: leaving it ends the session.
                PlayerLoopDriver.RequestQuit(0);
                return;
            }
            s_Request = play ? 1 : -1;
        }

        /// <summary>Applies a requested change, at the start of a frame (the Editor applies it
        /// after the update that asked).</summary>
        internal static void ApplyRequest()
        {
            int request = s_Request;
            s_Request = 0;
            if (request > 0 && !IsPlaying) EnterPlayMode();
            else if (request < 0 && IsPlaying) ExitPlayMode();
        }

        /// <summary>Edit mode's end as Play mode is entered: the open scenes as the Editor's views
        /// drew them (their drawn materials validated, MaterialValidation.ValidateDrawnBeforePlay),
        /// then ExitingEditMode.</summary>
        internal static void LeaveEditModeForPlay()
        {
            foreach (var scene in SceneRegistry.Loaded.ToArray()) MaterialValidation.ValidateDrawnBeforePlay(scene);
            EditorApplication.Internal_PlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
        }

        static void EnterPlayMode()
        {
            Application.Internal_PlayModeStarting();
            // As Direct Play enters it with the domain kept (EditorInitialization.LoadForPlayMode):
            // isPlaying false and isPlayingOrWillChangePlaymode true from here, Play mode's atlases
            // packed, ExitingEditMode, [InitializeOnEnterPlayMode] in Edit mode (matches the Unity
            // 6000.0.71f1 Editor, tests/projects/editorcallbacks: willPlay true, playing false), then,
            // with the options keeping the scenes, their objects stop in Edit mode
            // (tests/projects/scenereloadoff both.events: the probe enters Play mode from Edit mode).
            BeginEnteringPlayMode();
            bool keep;
            try
            {
                UnityEngine.U2D.SpriteAtlasPacking.PackV1ForPlayMode();
                LeaveEditModeForPlay();
                EditorInitialization.EnterPlayModeMethodsKeepingTheDomain();
                keep = KeepsScenesForPlay;
                if (keep) StopOpenScenesForPlay();
            }
            finally { EndEnteringPlayMode(); }
            FixPlayModeBuildList();
            Time.ResetForPlayMode();
            RuntimeInitializers.Run(RuntimeInitializeLoadType.SubsystemRegistration);
            RuntimeInitializers.Run(RuntimeInitializeLoadType.AfterAssembliesLoaded);
            RuntimeInitializers.Run(RuntimeInitializeLoadType.BeforeSplashScreen);
            RuntimeInitializers.Run(RuntimeInitializeLoadType.BeforeSceneLoad);
            PlayOpenScenes(reloaded: !keep);
            PlayerLoopDriver.SendPlayModeStart();
            RuntimeInitializers.Run(RuntimeInitializeLoadType.AfterSceneLoad);
            PlayerLoopDriver.RestartPlayMode(s_SingleSceneOpenedThisEditorUpdate);
        }

        /// <summary>Enter Play Mode Options keep the open scenes for Play mode (Reload Scene
        /// disabled).</summary>
        internal static bool KeepsScenesForPlay => Compat.EnterPlayModeEditStartMeasured
            && (EditorSettings.ActiveEnterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0;

        /// <summary>Direct Play opens its start scene in Edit mode first, as the Editor has it open
        /// before entering Play mode (its [ExecuteAlways] and [ExecuteInEditMode] scripts awake
        /// there), when the options keep for Play mode what that Edit mode did: the domain (Reload
        /// Domain disabled) or the scene's objects (Reload Scene disabled). With both reloads on the
        /// Editor discards that domain and those objects before Play mode starts, and the start
        /// scene loads in Play mode only (a recorded residual of tests/projects/scenereloadoff).</summary>
        internal static bool OpensStartSceneInEditMode => Compat.EnterPlayModeEditStartMeasured
            && (EditorSettings.ActiveEnterPlayModeOptions & (EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload)) != 0;

        /// <summary>The roots the Edit-mode open of the start scene added, until Play mode takes
        /// them over (scene kept) or destroys them (scene reloaded).</summary>
        static List<GameObject> s_EditModeStartRoots;

        /// <summary>Opens Direct Play's start scene in Edit mode (<see cref="OpensStartSceneInEditMode"/>),
        /// once the editor domain has loaded and before ExitingEditMode.</summary>
        internal static void OpenStartSceneInEditMode(SceneData scene)
        {
            if (scene == null || !OpensStartSceneInEditMode) return;
            s_EditModeStartRoots = SceneManagerInternal.OpenStartSceneInEditMode(scene);
        }

        /// <summary>Whether Direct Play's start scene plays as Edit mode left it: opened in Edit
        /// mode and kept (<see cref="KeepsScenesForPlay"/>); then it is not loaded again.</summary>
        internal static bool PlaysStartSceneAsOpened => s_EditModeStartRoots != null && KeepsScenesForPlay;

        /// <summary>The Edit-mode start's end, after ExitingEditMode and the domain's
        /// [InitializeOnEnterPlayMode] methods (when the domain is kept; matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/scenereloadoff), still in Edit mode: with the scene
        /// kept its objects stop (<see cref="StopOpenScenesForPlay"/>); with the scene reloaded the
        /// objects the open made are destroyed, before the reload loads the file in Play mode.</summary>
        internal static void EndEditModeStart()
        {
            var roots = s_EditModeStartRoots;
            if (roots == null) return;
            if (KeepsScenesForPlay)
            {
                StopOpenScenesForPlay();
                return;
            }
            s_EditModeStartRoots = null;
            // Roots a scene load keeps (DontSaveInEditor, as an [ExecuteAlways] script's helper
            // object) stay: the reload moves them out of the scene, as for any Single load.
            foreach (var root in roots)
                if (root != null && !SceneManagerInternal.KeptBySingleLoad(root)) Object.DestroyImmediate(root);
        }

        /// <summary>The components of the open scenes' active objects, in hierarchy order.</summary>
        static List<Component> OpenSceneComponents()
        {
            var components = new List<Component>();
            foreach (var scene in SceneRegistry.Loaded.ToArray())
                foreach (var root in scene.Roots.ToArray())
                    if (root != null) Collect(root.m_Transform, components);
            return components;
        }

        /// <summary>Entering Play mode with the open scenes kept: in Edit mode their enabled scripts
        /// get OnDisable (matches the Unity 6000.0.71f1 Editor, tests/projects/scenereloadoff: after
        /// ExitingEditMode, then OnEnable on the same instances in Play mode and no second Awake).
        /// Their engine components stop too, so that Play mode starts them as a loaded scene's
        /// (Play On Awake, for one); that part is Unbound's mechanism, not measured.</summary>
        internal static void StopOpenScenesForPlay() => Lifecycle.Deactivate(OpenSceneComponents());

        /// <summary>The open scenes play, after the BeforeSceneLoad initializers: their objects wake
        /// up as a loaded scene's do (the scripts awake in Edit mode get only OnEnable), are
        /// processed (IProcessSceneWithReport, [PostProcessScene]). <paramref name="reloaded"/>: as
        /// the scenes the Editor reloads for Play mode, their missing prefabs are reported again and
        /// sceneLoaded follows; kept scenes raise no sceneLoaded and no activeSceneChanged (matches
        /// the Unity 6000.0.71f1 Editor, tests/projects/scenereloadoff; whether a kept scene reports
        /// its missing prefabs again is not measured: it does not). Their objects are no prefab
        /// instances in Play mode (measured for reloaded scenes only).</summary>
        internal static void PlayOpenScenes(bool reloaded)
        {
            s_EditModeStartRoots = null;
            DisabledModules.PruneOpenScenes();
            var components = OpenSceneComponents();
            foreach (var scene in SceneRegistry.Loaded.ToArray())
                foreach (var root in scene.Roots.ToArray())
                    if (root != null) Unlink(root.m_Transform);
            Lifecycle.Activate(components);
            if (reloaded)
                foreach (var scene in SceneRegistry.Loaded.ToArray()) SceneLoader.LogMissingPrefabs(scene);
            foreach (var scene in SceneRegistry.Loaded.ToArray())
                EditorInitialization.ProcessScene(new Scene(scene.Handle));
            if (!reloaded) return;
            foreach (var scene in SceneRegistry.Loaded.ToArray())
                SceneManager.Internal_SceneLoaded(new Scene(scene.Handle), SceneManagerInternal.PlayModeEntry);
        }

        static void Unlink(Transform t)
        {
            PrefabLinks.Unlink(t.m_GameObject);
            foreach (var c in t.m_GameObject.m_Components) PrefabLinks.Unlink(c);
            foreach (var child in t.m_Children) Unlink(child);
        }

        static void Collect(Transform t, List<Component> into)
        {
            var go = t.m_GameObject;
            if (go == null || go.m_Destroyed || !go.m_IsActive) return;
            foreach (var c in go.m_Components)
                if (!c.m_Destroyed) into.Add(c);
            foreach (var child in t.m_Children) Collect(child, into);
        }

        /// <summary>Destroys playing scene roots. Internal detached holders remain available in
        /// Edit mode and are destroyed only when the process shuts down.</summary>
        internal static void DestroyPlayModeObjects(bool destroyDetached = false)
        {
            var scenes = new List<SceneData>(SceneRegistry.Loaded);
            scenes.Add(SceneRegistry.DontDestroyOnLoad);
            if (destroyDetached) scenes.Add(SceneRegistry.Detached);
            foreach (var scene in scenes)
                foreach (var root in scene.Roots.ToArray())
                    if (root != null) Object.DestroyImmediate(root);
        }

        static void ExitPlayMode()
        {
            BeginLeavingPlayMode();
            EditorApplication.Internal_PlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
            Lifecycle.Broadcast(m => m.OnApplicationQuit, null);
            if (Compat.QuitCallbacksMeasured) Application.Internal_PlayModeQuitting();
            DestroyPlayModeObjects();
            EndLeavingPlayMode();
            SceneRegistry.ResetToUntitled();
            EditorApplication.Internal_PlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
        }

        static bool s_EditModeUpdateRequested;

        internal static void QueuePlayerLoopUpdate() => s_EditModeUpdateRequested = true;

        internal static void RunQueuedEditModeUpdate()
        {
            if (!s_EditModeUpdateRequested) return;
            s_EditModeUpdateRequested = false;
            Lifecycle.RunPendingStarts();
            Lifecycle.RunUpdate();
            Lifecycle.RunLateUpdate();
        }

        static readonly Dictionary<Type, bool> s_RunsInEditMode = new Dictionary<Type, bool>();

        /// <summary>Whether a script's messages run now: always in Play mode; in Edit mode only
        /// for [ExecuteAlways] and [ExecuteInEditMode] scripts.</summary>
        internal static bool Runs(MonoBehaviour behaviour)
        {
            if (IsPlaying) return true;
            var type = behaviour.GetType();
            lock (s_RunsInEditMode)
            {
                if (!s_RunsInEditMode.TryGetValue(type, out var runs))
                {
                    runs = type.IsDefined(typeof(ExecuteAlways), true) || type.IsDefined(typeof(ExecuteInEditMode), true);
                    s_RunsInEditMode[type] = runs;
                }
                return runs;
            }
        }
    }
}
