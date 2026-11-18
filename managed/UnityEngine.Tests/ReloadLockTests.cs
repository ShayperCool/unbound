using System;
using System.Collections.Generic;
using UnityEditor;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>EditorApplication.LockReloadAssemblies: a reload asked for while locked (a requested
    /// compilation, new scripting defines, an edit that cannot be patched) waits for the last unlock;
    /// without one asked for, unlocking reloads nothing; an Edit-mode session (unbound test) does
    /// not reload at all.</summary>
    [Collection("EditorState")]
    public sealed class ReloadLockTests
    {
        sealed class Logs : ILogHandler
        {
            internal readonly List<(LogType, string)> Entries = new List<(LogType, string)>();
            public void LogFormat(LogType type, Object context, string format, params object[] args) =>
                Entries.Add((type, string.Format(format, args)));
            public void LogException(Exception exception, Object context) => Entries.Add((LogType.Exception, exception.Message));
        }

        [Fact]
        public void ADeferredReloadWaitsForTheLastUnlockAndHappensOnce()
        {
            var saved = EditorApplication.s_DeferredReload;
            var reloads = new List<bool>();
            EditorApplication.s_DeferredReload = optimized => reloads.Add(optimized);
            int before = 0;
            AssemblyReloadEvents.AssemblyReloadCallback raised = () => before++;
            AssemblyReloadEvents.beforeAssemblyReload += raised;
            try
            {
                // Unlocked, the caller reloads at once.
                Assert.False(EditorApplication.TryDeferReload(false));
                EditorApplication.LockReloadAssemblies();
                EditorApplication.LockReloadAssemblies();
                Assert.True(EditorApplication.TryDeferReload(false));
                Assert.True(EditorApplication.TryDeferReload(true));
                // Deferring raises no beforeAssemblyReload: the reload has not happened yet.
                Assert.Equal(0, before);
                EditorApplication.UnlockReloadAssemblies();
                Assert.Empty(reloads);
                EditorApplication.UnlockReloadAssemblies();
                // One reload, keeping that one of the deferred edits needed Debug code.
                Assert.Equal(new[] { true }, reloads);
                // An extra unlock, and a lock released without a reload asked for, reload nothing.
                EditorApplication.UnlockReloadAssemblies();
                EditorApplication.LockReloadAssemblies();
                EditorApplication.UnlockReloadAssemblies();
                Assert.Single(reloads);
                Assert.False(EditorApplication.TryDeferReload(false));
            }
            finally
            {
                AssemblyReloadEvents.beforeAssemblyReload -= raised;
                EditorApplication.s_DeferredReload = saved;
            }
        }

        [Fact]
        public void AnEditModeSessionDoesNotReloadWhenTheLockIsReleased()
        {
            var handler = Debug.unityLogger.logHandler;
            var logs = new Logs();
            bool session = EditorApplication.s_EditModeSession;
            int before = 0;
            AssemblyReloadEvents.AssemblyReloadCallback raised = () => before++;
            AssemblyReloadEvents.beforeAssemblyReload += raised;
            Debug.unityLogger.logHandler = logs;
            EditorApplication.s_EditModeSession = true;
            try
            {
                EditorApplication.LockReloadAssemblies();
                Assert.True(EditorApplication.TryDeferReload(false));
                // The real deferred reload: in an Edit-mode session a warning, no reload (no
                // beforeAssemblyReload and no native restart request, which a test has no host for).
                EditorApplication.UnlockReloadAssemblies();
                Assert.Equal(0, before);
                var entry = Assert.Single(logs.Entries);
                Assert.Equal(LogType.Warning, entry.Item1);
                Assert.Contains("Edit-mode session", entry.Item2);
            }
            finally
            {
                EditorApplication.s_EditModeSession = session;
                Debug.unityLogger.logHandler = handler;
                AssemblyReloadEvents.beforeAssemblyReload -= raised;
            }
        }
    }
}
