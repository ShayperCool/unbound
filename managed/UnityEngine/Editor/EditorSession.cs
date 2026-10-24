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

        internal static bool WillChange => s_Request != 0;

        internal static void Request(bool play)
        {
            if (play == IsPlaying)
            {
                s_Request = 0;
                return;
            }
            if (!play && !StartedInEditMode)
            {
                // Play mode is the whole session: leaving it ends the session.
                Application.Quit();
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

        static void EnterPlayMode()
        {
            EditorApplication.Internal_PlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
            IsPlaying = true;
            Time.ResetForPlayMode();
            EditorInitialization.EnterPlayModeMethods();
            RuntimeInitializers.Run(RuntimeInitializeLoadType.SubsystemRegistration);
            RuntimeInitializers.Run(RuntimeInitializeLoadType.AfterAssembliesLoaded);
            RuntimeInitializers.Run(RuntimeInitializeLoadType.BeforeSplashScreen);
            RuntimeInitializers.Run(RuntimeInitializeLoadType.BeforeSceneLoad);
            // The open scenes play: their objects wake up as a loaded scene's do.
            var components = new List<Component>();
            foreach (var scene in SceneRegistry.Loaded.ToArray())
                foreach (var root in scene.Roots.ToArray())
                    if (root != null) Collect(root.m_Transform, components);
            Lifecycle.Activate(components);
            foreach (var scene in SceneRegistry.Loaded.ToArray())
                SceneManager.Internal_SceneLoaded(new Scene(scene.Handle), LoadSceneMode.Single);
            RuntimeInitializers.Run(RuntimeInitializeLoadType.AfterSceneLoad);
            PlayerLoopDriver.RestartPlayMode();
        }

        static void Collect(Transform t, List<Component> into)
        {
            var go = t.m_GameObject;
            if (go == null || go.m_Destroyed || !go.m_IsActive) return;
            foreach (var c in go.m_Components)
                if (!c.m_Destroyed) into.Add(c);
            foreach (var child in t.m_Children) Collect(child, into);
        }

        static void ExitPlayMode()
        {
            EditorApplication.Internal_PlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
            // Play mode's objects go (OnDisable, OnDestroy), the scenes with them.
            var scenes = new List<SceneData>(SceneRegistry.Loaded);
            scenes.Add(SceneRegistry.DontDestroyOnLoad);
            foreach (var scene in scenes)
                foreach (var root in scene.Roots.ToArray())
                    if (root != null) Object.DestroyImmediate(root);
            IsPlaying = false;
            SceneRegistry.ResetToUntitled();
            EditorApplication.Internal_PlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
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
                    runs = type.IsDefined(typeof(ExecuteAlways), false) || type.IsDefined(typeof(ExecuteInEditMode), false);
                    s_RunsInEditMode[type] = runs;
                }
                return runs;
            }
        }
    }
}
