using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Plugin copies that declare one assembly name load as one, the highest version,
    /// whichever copy an assembly compiled against; every copy stays listed, so scripts named
    /// through any copy's GUID resolve (Unity 6000.0.71f1, tests/projects/compilesetup
    /// <c>plugins.alias.*</c>).</summary>
    public class PluginBindingTests
    {
        static string Probe(string path, [CallerFilePath] string source = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "tests", "projects", "compilesetup", path));

        [Fact]
        public void EachAssemblyNameBindsItsHighestVersion()
        {
            var v1 = new GameAssembly { Name = "ProbeAlias", Dll = Probe("Assets/Plugins/Alias/ProbeAlias.dll"), PluginGuid = "a" };
            var other = new GameAssembly { Name = "ProbeSame", Dll = Probe("Assets/Plugins/Same/ProbeSame.dll"), PluginGuid = "b" };
            var v2 = new GameAssembly { Name = "ProbeAliasNew", Dll = Probe("Packages/com.probe.setup/Plugins/ProbeAliasNew.dll"), PluginGuid = "c" };
            foreach (var order in new[] { new List<GameAssembly> { v1, other, v2 }, new List<GameAssembly> { v2, other, v1 } })
            {
                var bound = GameDomain.GameLoadContext.BoundByName(order);
                Assert.Equal(2, bound.Count);
                Assert.Same(v2, bound["ProbeAlias"]);
                Assert.Same(other, bound["ProbeSame"]);
            }
            // The same version twice: the first listed.
            var copy = new GameAssembly { Name = "ProbeSame", Dll = Probe("Packages/com.probe.setup/Plugins/ProbeSame.dll"), PluginGuid = "d" };
            Assert.Same(other, GameDomain.GameLoadContext.BoundByName(new List<GameAssembly> { other, copy })["ProbeSame"]);
        }
    }
}
