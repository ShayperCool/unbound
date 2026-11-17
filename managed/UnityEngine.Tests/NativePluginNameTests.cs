using Xunit;

namespace Unbound.Tests
{
    /// <summary>The name a native plugin answers [DllImport] by (tests/projects/pluginbundle).</summary>
    public class NativePluginNameTests
    {
        [Theory]
        // A macOS bundle by its own name, whatever its executable is called.
        [InlineData("/p/Assets/Plugins/Renamed.bundle/Contents/MacOS/RenamedLib", "Renamed.bundle")]
        [InlineData("/p/Assets/Plugins/Same.bundle/Contents/MacOS/Same", "Same.bundle")]
        // Plain libraries by their file; a MacOS folder outside a bundle is no bundle.
        [InlineData("/p/Assets/Plugins/libfoo.so", "libfoo.so")]
        [InlineData("/p/Assets/Plugins/foo.dylib", "foo.dylib")]
        [InlineData("/p/Assets/Plugins/Tools/Contents/MacOS/bar", "bar")]
        [InlineData("/p/Assets/Plugins/Plain.bundle", "Plain.bundle")]
        public void BundleExecutablesAnswerByTheBundle(string path, string name) =>
            Assert.Equal(name, Unbound.Runtime.GameDomain.NativePluginName(path));
    }
}
