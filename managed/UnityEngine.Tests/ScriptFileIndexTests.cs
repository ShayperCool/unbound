using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    // Which script file a class's own script is when several files stand for it: a partial class whose
    // other files declare it before any other type (each such file stands for it, as for a file without
    // a class named like it). The importer's settings in a .meta name the file named like the class
    // (tests/projects/importerpick Own.part, Unity 6000.0.71f1).
    public class ScriptFileIndexTests
    {
        sealed class Split : ScriptableObject { }
        sealed class Lone : ScriptableObject { }

        static Dictionary<string, List<Type>> ByName(params Type[] types)
        {
            var byName = new Dictionary<string, List<Type>>();
            foreach (var type in types)
            {
                if (!byName.TryGetValue(type.Name, out var list)) byName[type.Name] = list = new List<Type>();
                list.Add(type);
            }
            return byName;
        }

        [Theory]
        // The own file first, between or last among the files that also stand for the class.
        [InlineData(new object[] { new[] { "Split", "SplitApi", "SplitData" } })]
        [InlineData(new object[] { new[] { "ASplitHelpers", "Split", "SplitData" } })]
        [InlineData(new object[] { new[] { "ASplitHelpers", "SplitData", "Split" } })]
        public void AClassOwnScriptIsTheFileNamedLikeIt(string[] files)
        {
            var scripts = new List<(string, string, string, string)>();
            foreach (var file in files) scripts.Add(("Split", file == "Split" ? "own" : file.ToLowerInvariant(), null, file));
            var byGuid = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            var guidByType = new Dictionary<Type, string>();
            GameDomain.IndexSourceScripts(ByName(typeof(Split)), scripts, byGuid, guidByType);
            Assert.Equal("own", guidByType[typeof(Split)]);
            // Every file still resolves to the class.
            foreach (var (_, guid, _, _) in scripts) Assert.Same(typeof(Split), byGuid[guid]);
        }

        [Fact]
        public void AClassNoFileIsNamedLikeKeepsTheLastFileStandingForIt()
        {
            var scripts = new List<(string, string, string, string)>
            {
                ("Lone", "first", null, "LoneA"),
                ("Lone", "second", null, "LoneB"),
            };
            var byGuid = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            var guidByType = new Dictionary<Type, string>();
            GameDomain.IndexSourceScripts(ByName(typeof(Lone)), scripts, byGuid, guidByType);
            Assert.Equal("second", guidByType[typeof(Lone)]);
            Assert.Same(typeof(Lone), byGuid["first"]);
        }
    }
}
