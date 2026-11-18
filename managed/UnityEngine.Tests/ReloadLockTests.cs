using System;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>EditorApplication.LockReloadAssemblies: a reload asked for while locked (a requested
    /// compilation, new scripting defines, an edit that cannot be patched) waits for the last unlock;
    /// without one asked for, unlocking reloads nothing.</summary>
    [Collection("EditorState")]
    public sealed class ReloadLockTests
    {
        [Fact]
        public void ADeferredReloadWaitsForTheLastUnlockAndHappensOnce()
        {
            var saved = EditorApplication.s_DeferredReload;
            int reloads = 0;
            EditorApplication.s_DeferredReload = () => reloads++;
            try
            {
                Assert.False(EditorApplication.ReloadLocked);
                EditorApplication.LockReloadAssemblies();
                EditorApplication.LockReloadAssemblies();
                Assert.True(EditorApplication.ReloadLocked);
                EditorApplication.DeferReload();
                EditorApplication.UnlockReloadAssemblies();
                Assert.True(EditorApplication.ReloadLocked);
                Assert.Equal(0, reloads);
                EditorApplication.UnlockReloadAssemblies();
                Assert.False(EditorApplication.ReloadLocked);
                Assert.Equal(1, reloads);
                // An extra unlock, and a lock released without a reload asked for, reload nothing.
                EditorApplication.UnlockReloadAssemblies();
                EditorApplication.LockReloadAssemblies();
                EditorApplication.UnlockReloadAssemblies();
                Assert.Equal(1, reloads);
                Assert.False(EditorApplication.ReloadLocked);
            }
            finally
            {
                EditorApplication.s_DeferredReload = saved;
            }
        }
    }
}
