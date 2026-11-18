using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Completing a queued scene operation in a test.</summary>
    static class SceneOperations
    {
        /// <summary>Runs the scene operation queue until <paramref name="operation"/> is done.
        /// One pass integrates only what the frame's time limit lets it
        /// (Application.backgroundLoadingPriority), and operations queued before (another test's
        /// unloads) come first, so a single pass may not reach it.</summary>
        internal static void Complete(AsyncOperation operation)
        {
            Assert.NotNull(operation);
            for (int pass = 0; pass < 1000 && !operation.isDone; pass++) SceneManagerInternal.ProcessPending();
            Assert.True(operation.isDone, "the scene operation completes");
        }

        /// <summary>Unloads <paramref name="scene"/> and waits for the unload.</summary>
        internal static void Unload(Scene scene) => Complete(SceneManager.UnloadSceneAsync(scene));
    }
}
