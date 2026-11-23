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
                Debug.LogWarning("after");
                using (Log.EarlierImport(false)) Debug.LogWarning("in session");
                Assert.Equal(new[] { "after", "in session" }, heard);
            }
            finally
            {
                Application.logMessageReceived -= hear;
            }
        }
    }
}
