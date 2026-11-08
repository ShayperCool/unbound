using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// Unity's main-thread check of its engine API. Measured in the Unity 6000.0.71f1 Editor
    /// (tests/projects/mainthread): a member that is not thread-safe throws
    /// <c>UnityException: &lt;binding&gt; can only be called from the main thread.</c> (plus two lines
    /// about loading-thread constructors) on any other thread, a Task.Run or job worker included,
    /// and also inside a job the main thread runs (IJob.Run): Unity counts executing a job as off
    /// the main thread. The name is the native binding's (get_time, GetName, Internal_CloneSingle,
    /// ...), so each member passes its own. Thread-safe members (math, Debug.Log, JsonUtility, ...)
    /// have no check.
    /// </summary>
    internal static class MainThreadCheck
    {
        [ThreadStatic] static int t_EngineDepth;

        /// <summary>Managed audio callbacks can run on the main thread during offline rendering.</summary>
        [ThreadStatic] internal static bool t_InAudioCallback;

        /// <summary>The caller may use the main-thread API: the main thread outside a job, or engine
        /// code inside an <see cref="Engine"/> scope.</summary>
        internal static bool Allowed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (PlayerLoopDriver.IsMainThread && !Jobs.JobSystem.IsExecutingJob) || t_EngineDepth > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Check(string binding)
        {
            if (!Allowed) Throw(binding);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Throw(string binding) =>
            throw new UnityException(binding + " can only be called from the main thread.\nConstructors and field initializers will be executed from the loading thread when loading a scene.\nDon't use this function in the constructor or field initializers, instead move initialization code to the Awake or Start function.");

        /// <summary>Object.GetInstanceID's own check (and so a destroyed object's comparison with
        /// null): only the thread counts, so it passes inside IJob.Run (measured).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void EnsureRunningOnMainThread()
        {
            if (!PlayerLoopDriver.IsMainThread && t_EngineDepth == 0) ThrowNotMainThread();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowNotMainThread() =>
            throw new InvalidOperationException("EnsureRunningOnMainThread can only be called from the main thread");

        /// <summary>Engine code that does on another thread, or in a job, what Unity's native side
        /// does there (TransformAccess reading transforms for a transform job):
        /// <c>using (MainThreadCheck.Engine()) ...</c>.</summary>
        internal static EngineScope Engine()
        {
            t_EngineDepth++;
            return default;
        }

        internal readonly ref struct EngineScope
        {
            public void Dispose() => t_EngineDepth--;
        }
    }
}
