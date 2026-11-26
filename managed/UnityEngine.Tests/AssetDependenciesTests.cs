using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// AssetDatabase.GetDependencies and GetAssetBundleDependencies over tests/projects/assetdependencies'
    /// files, compared with the Unity 6000.0.71f1 Editor's answers in its reference (as sets: the
    /// Editor's order is not documented).
    /// </summary>
    [Collection("EditorState")]
    public sealed class AssetDependenciesTests : IDisposable
    {
        readonly string m_Root = Path.Combine(Path.GetTempPath(), "unbound-assetdependencies-" + System.Guid.NewGuid().ToString("N"));
        readonly string m_ProjectPath = ProjectInfo.ProjectPath;
        readonly ILogHandler m_Handler = Debug.unityLogger.logHandler;
        readonly Capture m_Logs = new Capture();
        readonly IDisposable m_Listing;
        readonly Dictionary<string, string> m_Reference = new Dictionary<string, string>(StringComparer.Ordinal);

        static string Probe([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "tests", "projects", "assetdependencies"));

        public AssetDependenciesTests()
        {
            Debug.unityLogger.logHandler = m_Logs;
            var probe = Probe();
            var entries = new List<string> { Entry("00000000000000001000000000000000", "Assets", "folder", "DefaultImporter", "", "") };
            foreach (var folder in new[] { "Assets/Deps", "Assets/Scripts", "Assets/Scenes" })
            {
                Directory.CreateDirectory(Path.Combine(m_Root, folder));
                entries.Add(Entry(GuidOf(File.ReadAllText(Path.Combine(probe, folder + ".meta"))), folder, "folder", "DefaultImporter", "", ""));
                foreach (var file in Directory.GetFiles(Path.Combine(probe, folder)))
                {
                    var name = folder + "/" + Path.GetFileName(file);
                    File.Copy(file, Path.Combine(m_Root, name));
                    if (name.EndsWith(".meta", StringComparison.Ordinal)) continue;
                    var meta = File.ReadAllText(file + ".meta");
                    var importer = Regex.Match(meta, @"(?m)^(\w+Importer):").Groups[1].Value;
                    entries.Add(Entry(GuidOf(meta), name, name.EndsWith(".unity", StringComparison.Ordinal) ? "scene" : "asset", importer, Scalar(meta, "assetBundleName"), Scalar(meta, "assetBundleVariant")));
                }
            }
            ProjectInfo.ProjectPath = m_Root;
            m_Listing = AssetDatabase.ListForTests("[" + string.Join(",", entries) + "]");
            foreach (var line in File.ReadLines(Path.Combine(probe, "Reference", "unity-6000.0.71f1", "values.jsonl")))
            {
                var row = JsonDocument.Parse(line).RootElement;
                if (row.GetProperty("v").ValueKind == JsonValueKind.String) m_Reference[row.GetProperty("k").GetString()!] = row.GetProperty("v").GetString()!;
            }
        }

        public void Dispose()
        {
            Debug.unityLogger.logHandler = m_Handler;
            m_Listing.Dispose();
            ProjectInfo.ProjectPath = m_ProjectPath;
            try { Directory.Delete(m_Root, true); } catch (IOException) { }
        }

        static string GuidOf(string meta) => Regex.Match(meta, @"(?m)^guid: ([0-9a-f]{32})").Groups[1].Value;

        static string Scalar(string meta, string key) => Regex.Match(meta, "(?m)^  " + key + ": ?(.*)$").Groups[1].Value.Trim();

        static string Entry(string guid, string path, string kind, string importer, string bundle, string variant) =>
            JsonSerializer.Serialize(new { guid, path, kind, importer, labels = Array.Empty<string>(), bundle, variant });

        static string L(IEnumerable<string> values) => "[" + string.Join(", ", values.Select(v => "\"" + v + "\"")) + "]";

        static string Set(IEnumerable<string> values) => L(values.OrderBy(v => v, StringComparer.Ordinal));

        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add("Exception: " + exception.Message);
            public void LogFormat(LogType type, Object context, string format, params object[] args) =>
                Lines.Add(type + ": " + string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
        }

        public static IEnumerable<object[]> Assets() =>
            new[]
            {
                "Assets/Deps/Tex.png", "Assets/Deps/Tex2.png", "Assets/Deps/Sec.png", "Assets/Deps/Custom.shader", "Assets/Deps/Fall.shader",
                "Assets/Deps/UsePass.shader", "Assets/Deps/Dependency.shader", "Assets/Deps/DepInclude.cginc", "Assets/Deps/Mat.mat",
                "Assets/Deps/BuiltinMat.mat", "Assets/Deps/Variant.mat", "Assets/Scripts/DepData.cs", "Assets/Scripts/DepBehaviour.cs",
                "Assets/Deps/Data.asset", "Assets/Deps/Other.asset", "Assets/Deps/Text.txt", "Assets/Deps/Prefab.prefab",
                "Assets/Deps/Deps.unity", "Assets/Scenes/Probe.unity", "Assets/Scripts/AssetDependenciesProbe.cs",
            }.Select(p => new object[] { p });

        [Theory]
        [MemberData(nameof(Assets))]
        public void AnAssetsDependenciesAreTheEditors(string path)
        {
            Assert.Equal(m_Reference["deps/" + path + "/default.set"], Set(AssetDatabase.GetDependencies(path)));
            Assert.Equal(m_Reference["deps/" + path + "/recursive.set"], Set(AssetDatabase.GetDependencies(path, true)));
            Assert.Equal(m_Reference["deps/" + path + "/direct.set"], Set(AssetDatabase.GetDependencies(path, false)));
            Assert.Empty(m_Logs.Lines);
        }

        [Fact]
        public void SeveralPathsAnswerTheUnionOfTheirDependencies()
        {
            var many = new[] { "Assets/Deps/Prefab.prefab", "Assets/Deps/Mat.mat", "Assets/Deps/Prefab.prefab", "Assets/Deps/Text.txt" };
            Assert.Equal(m_Reference["many/default.set"], Set(AssetDatabase.GetDependencies(many)));
            Assert.Equal(m_Reference["many/direct.set"], Set(AssetDatabase.GetDependencies(many, false)));
            var cycle = new[] { "Assets/Deps/Other.asset", "Assets/Deps/Data.asset" };
            Assert.Equal(m_Reference["cycle/recursive.set"], Set(AssetDatabase.GetDependencies(cycle, true)));
            Assert.Equal(m_Reference["cycle/direct.set"], Set(AssetDatabase.GetDependencies(cycle, false)));
            Assert.Equal(m_Reference["many/empty.set"], Set(AssetDatabase.GetDependencies(new string[0], true)));
            Assert.Equal(m_Reference["many/null.set"], Set(AssetDatabase.GetDependencies((string[])null, true)));
            Assert.Equal(m_Reference["many/withMissing.set"], Set(AssetDatabase.GetDependencies(new[] { "Assets/Nope.mat", "Assets/Deps/Mat.mat" }, false)));
        }

        [Theory]
        [InlineData("null", null)]
        [InlineData("empty", "")]
        [InlineData("folder", "Assets/Deps")]
        [InlineData("assets", "Assets")]
        [InlineData("missing", "Assets/Deps/Nope.mat")]
        [InlineData("case", "Assets/Deps/MAT.mat")]
        [InlineData("backslash", "Assets\\Deps\\Mat.mat")]
        [InlineData("slash", "Assets/Deps/Mat.mat/")]
        public void PathsAreReadAsTheEditorReadsThem(string name, string path)
        {
            Assert.Equal(m_Reference["odd/" + name + "/recursive.set"], Set(AssetDatabase.GetDependencies(path, true)));
            Assert.Equal(m_Reference["odd/" + name + "/direct.set"], Set(AssetDatabase.GetDependencies(path, false)));
        }

        [Theory]
        [InlineData("prefab")]
        [InlineData("mat")]
        [InlineData("data")]
        [InlineData("tex")]
        [InlineData("tex.hd")]
        [InlineData("missing")]
        public void ABundlesDependenciesAreTheBundlesOfItsAssetsDependencies(string bundle)
        {
            foreach (var recursive in new[] { false, true })
                Assert.Equal(m_Reference["bundles/" + bundle + "/" + recursive], L(AssetDatabase.GetAssetBundleDependencies(bundle, recursive)));
        }

        /// <summary>A saved file's change is read again (the answer follows the file, not a stale
        /// reading); an unsaved one is not seen (spritemeta change.dirty: it reads the files).</summary>
        [Fact]
        public void AChangedFileIsReadAgain()
        {
            Assert.Contains("Assets/Deps/Tex.png", AssetDatabase.GetDependencies("Assets/Deps/Mat.mat", false));
            var file = Path.Combine(m_Root, "Assets", "Deps", "Mat.mat");
            File.WriteAllText(file, File.ReadAllText(file).Replace("guid: d1e00000000000000000000000000001", "guid: d1e000000000000000000000000000fe"));
            File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(5));
            Assert.Equal(new[] { "Assets/Deps/Custom.shader" }, AssetDatabase.GetDependencies("Assets/Deps/Mat.mat", false));
        }

        /// <summary>A text that reads like Unity's serialization is a text; an unknown file holds no
        /// references (neither says anything).</summary>
        [Fact]
        public void ATextAndAnUnknownFileReferenceNothing()
        {
            using var listing = AssetDatabase.ListForTests("[" + Entry("d1e000000000000000000000000000ab", "Assets/Deps/Yaml.txt", "text", "TextScriptImporter", "", "") + ","
                + Entry("d1e000000000000000000000000000ac", "Assets/Deps/Notes.md", "unknown", "DefaultImporter", "", "") + ","
                + Entry("d1e00000000000000000000000000004", "Assets/Deps/Mat.mat", "asset", "NativeFormatImporter", "", "") + "]");
            var yaml = "%YAML 1.1\nrefs: {fileID: 2100000, guid: d1e00000000000000000000000000004, type: 2}\n";
            File.WriteAllText(Path.Combine(m_Root, "Assets", "Deps", "Yaml.txt"), yaml);
            File.WriteAllText(Path.Combine(m_Root, "Assets", "Deps", "Notes.md"), "notes {fileID: 2100000, guid: d1e00000000000000000000000000004, type: 2}\n");
            Assert.Empty(AssetDatabase.GetDependencies("Assets/Deps/Yaml.txt", false));
            Assert.Empty(AssetDatabase.GetDependencies("Assets/Deps/Notes.md", false));
            Assert.Empty(m_Logs.Lines);
        }

        /// <summary>A binary-serialized scene's references are not read: said.</summary>
        [Fact]
        public void ABinarySceneIsSaid()
        {
            using var listing = AssetDatabase.ListForTests("[" + Entry("d1e000000000000000000000000000ad", "Assets/Deps/Binary.unity", "scene", "DefaultImporter", "", "") + "]");
            File.WriteAllBytes(Path.Combine(m_Root, "Assets", "Deps", "Binary.unity"), new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 });
            Stubs.Forget("AssetDatabase.GetDependencies(binary)");
            Assert.Empty(AssetDatabase.GetDependencies("Assets/Deps/Binary.unity", false));
            Assert.Single(m_Logs.Lines, l => l.Contains("does not read the references of binary-serialized assets yet"));
        }

        /// <summary>The assets of an importer whose references are not read say so, once.</summary>
        [Fact]
        public void AnAssetOfAnImporterNotReadIsSaid()
        {
            using var listing = AssetDatabase.ListForTests("[" + Entry("d1e000000000000000000000000000aa", "Assets/Deps/Model.fbx", "model", "ModelImporter", "", "") + "]");
            File.WriteAllBytes(Path.Combine(m_Root, "Assets", "Deps", "Model.fbx"), new byte[] { 1, 2, 3, 4, 5, 6 });
            Stubs.Forget("AssetDatabase.GetDependencies(ModelImporter)");
            Assert.Equal(new[] { "Assets/Deps/Model.fbx" }, AssetDatabase.GetDependencies("Assets/Deps/Model.fbx"));
            AssetDatabase.GetDependencies("Assets/Deps/Model.fbx");
            Assert.Single(m_Logs.Lines, l => l.Contains("does not read the references of ModelImporter assets yet"));
        }
    }
}
