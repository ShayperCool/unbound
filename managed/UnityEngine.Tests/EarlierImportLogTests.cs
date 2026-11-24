using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Messages of an import the Editor makes before Play mode reach no Play mode log
    /// handler (tests/projects/importlogs load.*); others do.</summary>
    public sealed class EarlierImportLogTests
    {
        [Fact]
        public void AnEarlierImportsMessagesReachNoHandler()
        {
            var heard = new List<string>();
            Application.LogCallback hear = (message, stack, type) => heard.Add(message);
            Application.logMessageReceived += hear;
            try
            {
                using (Log.EarlierImport(true))
                {
                    Debug.LogWarning("earlier import");
                    using (Log.EarlierImport(true)) Debug.LogError("nested");
                    Debug.Log("still earlier");
                }
                // A scope left by an exception ends too.
                try
                {
                    using (Log.EarlierImport(true)) throw new System.InvalidOperationException("import failed");
                }
                catch (System.InvalidOperationException) { }
                Debug.LogWarning("after");
                using (Log.EarlierImport(false)) Debug.LogWarning("in session");
                // A Play mode step inside an earlier import (a sprite's atlas binding) is heard,
                // and the import's own messages after it are not.
                using (Log.EarlierImport(true))
                {
                    using (Log.EarlierImport(true))
                    {
                        using (Log.PlayModeStep()) Debug.LogWarning("binding");
                        Debug.LogWarning("import again");
                    }
                    Debug.LogWarning("import");
                }
                Assert.Null(Log.PlayModeStep());
                Debug.LogWarning("last");
                Assert.Equal(new[] { "after", "in session", "binding", "last" }, heard);
            }
            finally
            {
                Application.logMessageReceived -= hear;
            }
        }
    }
}
