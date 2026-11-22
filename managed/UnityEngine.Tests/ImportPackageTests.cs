using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// AssetDatabase.ImportPackage as the Unity 6000.0.71f1 Editor does it without its window
    /// (tests/projects/projectwrites 14.package): nothing in Play mode, a failure at once for a
    /// file that is no package, otherwise importPackageStarted in the call and the files, the
    /// import and the completion events with the next editor update.
    /// </summary>
    [Collection("EditorState")]
    public sealed class ImportPackageTests : IDisposable
    {
        const string OwnerGuid = "0c000000000000000000000000000001";
        const string OtherGuid = "0c000000000000000000000000000002";
        const string Table = """
            [
              {"guid":"00000000000000001000000000000000","path":"Assets","kind":"folder","importer":"DefaultImporter","labels":[]},
              {"guid":"0c000000000000000000000000000001","path":"Assets/Target/SameGuid.txt","kind":"text","importer":"TextScriptImporter","labels":[]},
              {"guid":"0c000000000000000000000000000002","path":"Assets/Target/SamePath.txt","kind":"text","importer":"TextScriptImporter","labels":[]}
            ]
            """;

        readonly string m_Project, m_PreviousProject;
        readonly bool m_PreviousPlaying;
        readonly List<string> m_Events = new List<string>();
        readonly List<string> m_Logs = new List<string>();
        readonly Application.LogCallback m_OnLog;
        readonly AssetDatabase.ImportPackageCallback m_Started, m_Completed;
        readonly AssetDatabase.ImportPackageFailedCallback m_Failed;
        readonly Action<string[]> m_Items;
        int m_Refreshes;

        public ImportPackageTests()
        {
            m_Project = Path.Combine(Path.GetTempPath(), "unbound-importpackage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(m_Project, "Assets", "Target"));
            File.WriteAllText(Path.Combine(m_Project, "Assets/Target/SameGuid.txt"), "project");
            File.WriteAllText(Path.Combine(m_Project, "Assets/Target/SamePath.txt"), "project");
            m_PreviousProject = ProjectInfo.ProjectPath;
            ProjectInfo.ProjectPath = m_Project;
            m_PreviousPlaying = EditorSession.IsPlaying;
            EditorSession.IsPlaying = false;
            AssetDatabase.List(Table);
            AssetDatabase.SetRootsForTests(("Assets", true), ("Packages/com.example.registry", false));
            PackageImports.RefreshForTests = () => m_Refreshes++;
            m_Started = name => m_Events.Add("started:" + name + ":" + File.Exists(Asset("Assets/Pack/Note.txt")));
            m_Completed = name => m_Events.Add("completed:" + name + ":" + File.Exists(Asset("Assets/Pack/Note.txt")));
            m_Failed = (name, error) => m_Events.Add("failed:" + name + ":" + error);
            m_Items = items => m_Events.Add("items:" + string.Join(",", items));
            AssetDatabase.importPackageStarted += m_Started;
            AssetDatabase.importPackageCompleted += m_Completed;
            AssetDatabase.importPackageFailed += m_Failed;
            AssetDatabase.onImportPackageItemsCompleted += m_Items;
            m_OnLog = (condition, _, type) => m_Logs.Add(type + ":" + condition);
            Application.logMessageReceived += m_OnLog;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= m_OnLog;
            AssetDatabase.importPackageStarted -= m_Started;
            AssetDatabase.importPackageCompleted -= m_Completed;
            AssetDatabase.importPackageFailed -= m_Failed;
            AssetDatabase.onImportPackageItemsCompleted -= m_Items;
            // Nothing asked for here is left for a later test's editor update.
            EditorApplication.RunEditorCallbacksNow();
            PackageImports.RefreshForTests = null;
            AssetDatabase.SetRootsForTests(null);
            EditorSession.IsPlaying = m_PreviousPlaying;
            AssetDatabase.Invalidate();
            ProjectInfo.ProjectPath = m_PreviousProject;
            Directory.Delete(m_Project, true);
        }

        string Asset(string path) => Path.Combine(m_Project, path);

        static string Meta(string guid, string userData = "") =>
            "fileFormatVersion: 2\nguid: " + guid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData: " + userData + "\n";

        /// <summary>A package file of (GUID, path, content or null for a folder, .meta or null).</summary>
        string Package(string name, params (string Guid, string Path, string Asset, string Meta)[] items)
        {
            var file = Path.Combine(m_Project, name);
            using var stream = File.Create(file);
            using var zipped = new GZipStream(stream, System.IO.Compression.CompressionLevel.Fastest);
            using var tar = new TarWriter(zipped, TarEntryFormat.Ustar);
            void Add(string entry, string text)
            {
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, entry) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) });
            }
            foreach (var item in items)
            {
                tar.WriteEntry(new UstarTarEntry(TarEntryType.Directory, item.Guid + "/"));
                if (item.Asset != null) Add(item.Guid + "/asset", item.Asset);
                if (item.Meta != null) Add(item.Guid + "/asset.meta", item.Meta);
                // As the Editor writes it: the path, then a line of its own.
                Add(item.Guid + "/pathname", item.Path + "\n00");
            }
            return name;
        }

        string Pack() => Package("Pack.unitypackage",
            ("a1000000000000000000000000000001", "Assets/Pack", null, "fileFormatVersion: 2\nguid: a1000000000000000000000000000001\nfolderAsset: yes\n"),
            ("a1000000000000000000000000000002", "Assets/Pack/Note.txt", "packed", Meta("a1000000000000000000000000000002", "packed")),
            ("a1000000000000000000000000000003", "Assets/Pack/Deep/Bare.txt", "bare", null),
            (OwnerGuid, "Assets/Pack/SameGuid.txt", "same guid", Meta(OwnerGuid, "same")),
            ("a1000000000000000000000000000005", "Assets/Target/SamePath.txt", "same path", Meta("a1000000000000000000000000000005", "path")));

        [Fact]
        public void APackageStartsInTheCallAndIsWrittenAndCompletedWithTheNextEditorUpdate()
        {
            AssetDatabase.ImportPackage(Pack(), false);

            Assert.Equal(new[] { "started:Pack:False" }, m_Events);
            Assert.False(Directory.Exists(Asset("Assets/Pack")));
            Assert.Equal(0, m_Refreshes);
            m_Events.Clear();

            EditorApplication.RunEditorCallbacksNow();

            Assert.Equal(new[]
            {
                "completed:Pack:True",
                "items:Assets/Pack,Assets/Pack/Note.txt,Assets/Pack/Deep/Bare.txt,Assets/Target/SameGuid.txt,Assets/Target/SamePath.txt",
            }, m_Events);
            Assert.Equal(1, m_Refreshes);
            Assert.Equal("packed", File.ReadAllText(Asset("Assets/Pack/Note.txt")));
            Assert.Equal(Meta("a1000000000000000000000000000002", "packed"), File.ReadAllText(Asset("Assets/Pack/Note.txt.meta")));
            Assert.Contains("folderAsset: yes", File.ReadAllText(Asset("Assets/Pack.meta")));
            // An item without a .meta gets one of its GUID alone; its folder is made.
            Assert.Equal("bare", File.ReadAllText(Asset("Assets/Pack/Deep/Bare.txt")));
            Assert.Equal("fileFormatVersion: 2\nguid: a1000000000000000000000000000003\n", File.ReadAllText(Asset("Assets/Pack/Deep/Bare.txt.meta")));
            // The item of a GUID the project has goes to that asset's path, not the package's.
            Assert.False(File.Exists(Asset("Assets/Pack/SameGuid.txt")));
            Assert.Equal("same guid", File.ReadAllText(Asset("Assets/Target/SameGuid.txt")));
            Assert.Equal(Meta(OwnerGuid, "same"), File.ReadAllText(Asset("Assets/Target/SameGuid.txt.meta")));
            // The item at the path of an asset with another GUID replaces it, GUID too, and says so.
            Assert.Equal("same path", File.ReadAllText(Asset("Assets/Target/SamePath.txt")));
            Assert.Equal(Meta("a1000000000000000000000000000005", "path"), File.ReadAllText(Asset("Assets/Target/SamePath.txt.meta")));
            Assert.Equal(new[] { "Warning:Overriding existing asset with new GUID from package, this may break links within your project to package assets (Assets/Target/SamePath.txt)" }, m_Logs);

            // Nothing more with later updates.
            m_Events.Clear();
            EditorApplication.RunEditorCallbacksNow();
            Assert.Empty(m_Events);
            Assert.Equal(1, m_Refreshes);
        }

        [Fact]
        public void ImportsAskedForBeforeAnUpdateEndInTheirOrderAndAnAbsolutePathNamesThePackageToo()
        {
            var first = Package("First.unitypackage", ("b1000000000000000000000000000001", "Assets/One.txt", "1", Meta("b1000000000000000000000000000001")));
            var second = Package("Second.unitypackage", ("b1000000000000000000000000000002", "Assets/One.txt", "2", Meta("b1000000000000000000000000000002")));
            AssetDatabase.ImportPackage(first, false);
            AssetDatabase.ImportPackage(Path.Combine(m_Project, second), true);
            EditorApplication.RunEditorCallbacksNow();

            Assert.Equal(new[] { "started:First:False", "started:Second:False", "completed:First:False", "items:Assets/One.txt", "completed:Second:False", "items:Assets/One.txt" }, m_Events);
            Assert.Equal("2", File.ReadAllText(Asset("Assets/One.txt")));
        }

        [Theory]
        [InlineData("Missing.unitypackage", "Missing")]
        [InlineData("Broken.unitypackage", "Broken")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void AFileThatIsNoPackageFailsAtOnceWithTwoErrors(string path, string name)
        {
            File.WriteAllText(Asset("Broken.unitypackage"), "not an archive");
            AssetDatabase.ImportPackage(path, false);
            EditorApplication.RunEditorCallbacksNow();

            Assert.Equal(new[] { "failed:" + name + ":Failed to unpack package contents" }, m_Events);
            Assert.Equal(Enumerable.Repeat("Error:Failed to import package with error: Couldn't decompress package", 2), m_Logs);
            Assert.Equal(0, m_Refreshes);
        }

        [Fact]
        public void InPlayModeNothingIsImported()
        {
            var package = Pack();
            EditorSession.IsPlaying = true;
            AssetDatabase.ImportPackage(package, false);
            AssetDatabase.ImportPackage("Missing.unitypackage", false);
            EditorSession.IsPlaying = false;
            EditorApplication.RunEditorCallbacksNow();

            Assert.Equal(new[] { "failed:Pack:Failed to unpack package contents", "failed:Missing:Failed to unpack package contents" }, m_Events);
            Assert.Equal(Enumerable.Repeat("Error:Failed to import package with error: Cannot import package in play mode.", 4), m_Logs);
            Assert.False(Directory.Exists(Asset("Assets/Pack")));
            Assert.Equal(0, m_Refreshes);
        }

        [Fact]
        public void AnItemOutsideTheProjectsAssetsIsNotWritten()
        {
            var package = Package("Out.unitypackage",
                ("c1000000000000000000000000000001", "Assets/../../Escaped.txt", "x", null),
                ("c1000000000000000000000000000002", "ProjectSettings/Mine.asset", "x", null),
                ("c1000000000000000000000000000004", "Packages/com.example.registry/In.txt", "x", null),
                ("c1000000000000000000000000000003", "Assets/Kept.txt", "kept", null));
            AssetDatabase.ImportPackage(package, false);
            EditorApplication.RunEditorCallbacksNow();

            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(m_Project), "Escaped.txt")));
            Assert.False(File.Exists(Asset("ProjectSettings/Mine.asset")));
            Assert.Equal("kept", File.ReadAllText(Asset("Assets/Kept.txt")));
            Assert.Contains("items:Assets/Kept.txt", m_Events);
            Assert.Equal(3, m_Logs.Count(log => log.StartsWith("Error:AssetDatabase.ImportPackage:", StringComparison.Ordinal)));
        }

        [Fact]
        public void AFileThatCannotBeWrittenEndsTheImportAsFailed()
        {
            // A file where the item's folder should be.
            File.WriteAllText(Asset("Assets/Blocked"), "a file");
            var package = Package("Blocked.unitypackage",
                ("e1000000000000000000000000000002", "Assets/Before.txt", "before", null),
                ("e1000000000000000000000000000001", "Assets/Blocked/In.txt", "x", null));
            AssetDatabase.ImportPackage(package, false);
            EditorApplication.RunEditorCallbacksNow();

            Assert.Equal(new[] { "started:Blocked:False", "failed:Blocked:Failed to unpack package contents" }, m_Events);
            Assert.Equal(2, m_Logs.Count(log => log.StartsWith("Error:Failed to import package with error: ", StringComparison.Ordinal)));
            // What was written before is imported all the same.
            Assert.Equal("before", File.ReadAllText(Asset("Assets/Before.txt")));
            Assert.Equal(1, m_Refreshes);
        }

        [Fact]
        public void AnImportAskedForByAnImportsEventEndsWithALaterUpdate()
        {
            var second = Package("Second.unitypackage", ("f1000000000000000000000000000002", "Assets/Two.txt", "2", null));
            var first = Package("First.unitypackage", ("f1000000000000000000000000000001", "Assets/One.txt", "1", null));
            AssetDatabase.ImportPackageCallback chain = name => { if (name == "First") AssetDatabase.ImportPackage(second, false); };
            AssetDatabase.importPackageCompleted += chain;
            try
            {
                AssetDatabase.ImportPackage(first, false);
                EditorApplication.RunEditorCallbacksNow();
                Assert.Equal(new[] { "started:First:False", "completed:First:False", "started:Second:False", "items:Assets/One.txt" }, m_Events);
                Assert.False(File.Exists(Asset("Assets/Two.txt")));
                m_Events.Clear();

                EditorApplication.RunEditorCallbacksNow();
                Assert.Equal(new[] { "completed:Second:False", "items:Assets/Two.txt" }, m_Events);
                Assert.Equal("2", File.ReadAllText(Asset("Assets/Two.txt")));
            }
            finally { AssetDatabase.importPackageCompleted -= chain; }
        }

        [Fact]
        public void EntriesThatAreNoItemsArePassedOver()
        {
            var file = Path.Combine(m_Project, "Odd.unitypackage");
            using (var stream = File.Create(file))
            using (var zipped = new GZipStream(stream, System.IO.Compression.CompressionLevel.Fastest))
            using (var tar = new TarWriter(zipped, TarEntryFormat.Ustar))
            {
                void Add(string entry, string text) =>
                    tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, entry) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) });
                Add("./d1000000000000000000000000000001/asset", "dotted");
                Add("./d1000000000000000000000000000001/pathname", "Assets/Dotted.txt");
                Add("d1000000000000000000000000000002/asset", "no path");
                Add("readme.txt", "loose");
                Add("d1000000000000000000000000000003/preview.png", "p");
            }
            var items = PackageImports.Unpack(file);

            Assert.Equal(new[] { "d1000000000000000000000000000001:Assets/Dotted.txt:dotted" },
                items.Select(i => i.Guid + ":" + i.Path + ":" + Encoding.UTF8.GetString(i.Asset)));
            Assert.Null(PackageImports.Unpack(Path.Combine(m_Project, "None.unitypackage")));
        }
    }
}
