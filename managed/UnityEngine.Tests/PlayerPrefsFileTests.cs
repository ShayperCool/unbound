using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The PlayerPrefs file a run is given (`unbound run --player-prefs`).</summary>
    public class PlayerPrefsFileTests
    {
        /// <summary>A bare file name is a file of the current directory: it is written, not refused
        /// for its empty directory name.</summary>
        [Fact]
        public void ABareFileNameIsWrittenInTheCurrentDirectory()
        {
            var name = "unbound-prefs-test-" + System.Guid.NewGuid().ToString("N") + ".xml";
            try
            {
                UnityPrefsXml.Write(name, new Dictionary<string, object> { ["k"] = 3 });
                Assert.True(File.Exists(name));
                Assert.Equal(3, UnityPrefsXml.Read(name)["k"]);
            }
            finally { File.Delete(name); }
        }
    }
}
