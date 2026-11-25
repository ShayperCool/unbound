using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>What a session's start imports (tests/projects/firstimport): every asset without a
    /// record, then what changed since; OnPostprocessAllAssets dispatched and ordered as Unity's own
    /// C# does.</summary>
    [Collection("EditorState")]
    public class ProjectImportsTests : IDisposable
    {
        readonly string m_Root = Path.Combine(Path.GetTempPath(), "unbound-imports-" + Guid.NewGuid().ToString("N"));
        string Record => Path.Combine(m_Root, "LibraryUnbound", "ImportRecord.json");

        public ProjectImportsTests()
        {
            Directory.CreateDirectory(Path.Combine(m_Root, "Assets", "M"));
            Write("Assets/M.meta", "guid: f0\n");
            Write("Assets/M/A.mat", "a");
            Write("Assets/M/A.mat.meta", "guid: a1\n");
            Write("Assets/M/B.mat", "b");
            Write("Assets/M/B.mat.meta", "guid: b1\n");
            Write("Assets/M/C.mat", "c");
            Write("Assets/M/C.mat.meta", "guid: c1\n");
        }

        public void Dispose()
        {
            try { Directory.Delete(m_Root, true); } catch (IOException) { }
        }

        void Write(string path, string text) => File.WriteAllText(Path.Combine(m_Root, path), text);

        List<ProjectImports.Asset> Assets(params (string Path, string Guid)[] assets) =>
            assets.Select(a => new ProjectImports.Asset(a.Path, a.Guid, Path.Combine(m_Root, a.Path))).ToList();

        List<ProjectImports.Asset> Initial() => Assets(("Assets", ""), ("Assets/M", "f0"), ("Assets/M/A.mat", "a1"), ("Assets/M/B.mat", "b1"), ("Assets/M/C.mat", "c1"));

        ProjectImports.Changes Scan(List<ProjectImports.Asset> assets)
        {
            var changes = ProjectImports.Scan(assets, Record, out var now);
            Directory.CreateDirectory(Path.GetDirectoryName(Record));
            File.WriteAllText(Record, System.Text.Json.JsonSerializer.Serialize(new { Format = ProjectImports.FormatVersion, Assets = now }));
            return changes;
        }

        [Fact]
        public void WithoutARecordEveryAssetIsImportedThenNothingWhenNothingChanged()
        {
            var first = Scan(Initial());
            Assert.True(first.First);
            Assert.Equal(new[] { "Assets", "Assets/M", "Assets/M/A.mat", "Assets/M/B.mat", "Assets/M/C.mat" }, first.Imported);
            var second = Scan(Initial());
            Assert.False(second.First);
            Assert.Empty(second.Imported);
            Assert.Empty(second.Deleted);
            Assert.Empty(second.Moved);
        }

        [Fact]
        public void ChangedContentOrMetaNewDeletedAndMovedAssetsAreListedAndATouchIsNot()
        {
            Scan(Initial());
            Write("Assets/M/A.mat", "a2");                                   // content
            Write("Assets/M/B.mat.meta", "guid: b1\nlabels:\n- X\n");       // .meta only
            var touched = Path.Combine(m_Root, "Assets/M/C.mat");          // time only
            File.SetLastWriteTimeUtc(touched, File.GetLastWriteTimeUtc(touched).AddSeconds(10));
            Write("Assets/M/D.mat", "d");                                   // new
            Directory.CreateDirectory(Path.Combine(m_Root, "Assets/N"));     // new folder, B moved into it
            File.Move(Path.Combine(m_Root, "Assets/M/B.mat"), Path.Combine(m_Root, "Assets/N/B.mat"));
            File.Move(Path.Combine(m_Root, "Assets/M/B.mat.meta"), Path.Combine(m_Root, "Assets/N/B.mat.meta"));
            File.Delete(Path.Combine(m_Root, "Assets/M/A.mat"));             // A deleted after all
            Write("Assets/M/E.mat", "e");
            var changes = Scan(Assets(("Assets", ""), ("Assets/M", "f0"), ("Assets/M/C.mat", "c1"), ("Assets/M/D.mat", "d1"), ("Assets/M/E.mat", "e1"),
                ("Assets/N", "n1"), ("Assets/N/B.mat", "b1")));
            Assert.False(changes.First);
            // The moved asset's .meta changed too: moved, and imported.
            Assert.Equal(new[] { "Assets/M/D.mat", "Assets/M/E.mat", "Assets/N", "Assets/N/B.mat" }, changes.Imported);
            Assert.Equal(new[] { "Assets/M/A.mat" }, changes.Deleted);
            Assert.Equal(new[] { "Assets/N/B.mat" }, changes.Moved);
            Assert.Equal(new[] { "Assets/M/B.mat" }, changes.MovedFrom);
        }

        [Fact]
        public void AMovedAssetUnchangedIsMovedNotImported()
        {
            Scan(Initial());
            File.Move(Path.Combine(m_Root, "Assets/M/C.mat"), Path.Combine(m_Root, "Assets/C.mat"));
            File.Move(Path.Combine(m_Root, "Assets/M/C.mat.meta"), Path.Combine(m_Root, "Assets/C.mat.meta"));
            var changes = Scan(Assets(("Assets", ""), ("Assets/M", "f0"), ("Assets/M/A.mat", "a1"), ("Assets/M/B.mat", "b1"), ("Assets/C.mat", "c1")));
            Assert.Empty(changes.Imported);
            Assert.Empty(changes.Deleted);
            Assert.Equal(new[] { "Assets/C.mat" }, changes.Moved);
            Assert.Equal(new[] { "Assets/M/C.mat" }, changes.MovedFrom);
        }

        [Fact]
        public void TheSessionsEndRecordsOnlyWhatTheSessionWroteSoOutsideChangesAreImportedNext()
        {
            ProjectImports.Scan(Initial(), Record, out var start);
            ProjectImports.ResetForTests();
            ProjectImports.BaseForTests(start);
            try
            {
                // Unbound writes A and deletes C; outside the session B changes and D appears.
                Write("Assets/M/A.mat", "a written");
                ProjectImports.Wrote("Assets/M/A.mat");
                File.Delete(Path.Combine(m_Root, "Assets/M/C.mat"));
                File.Delete(Path.Combine(m_Root, "Assets/M/C.mat.meta"));
                ProjectImports.Wrote("Assets/M/C.mat.meta");
                Write("Assets/M/B.mat", "b changed outside");
                Write("Assets/M/D.mat", "d");
                var now = Assets(("Assets", ""), ("Assets/M", "f0"), ("Assets/M/A.mat", "a1"), ("Assets/M/B.mat", "b1"), ("Assets/M/D.mat", "d1"));
                ProjectImports.Settle(now, Record);
                var next = ProjectImports.Scan(now, Record, out _);
                Assert.Equal(new[] { "Assets/M/B.mat", "Assets/M/D.mat" }, next.Imported);
                Assert.Empty(next.Deleted);
            }
            finally { ProjectImports.ResetForTests(); }
        }

        [Fact]
        public void ARecordOfAnotherFormatIsAFirstOpen()
        {
            Scan(Initial());
            File.WriteAllText(Record, "{\"Format\":0,\"Assets\":[]}");
            var changes = Scan(Initial());
            Assert.True(changes.First);
            Assert.Equal(5, changes.Imported.Length);
            File.WriteAllText(Record, "not json");
            Assert.True(Scan(Initial()).First);
        }

        [Fact]
        public void ASettingsFileWithoutMetaIsListedWhenItChanges()
        {
            Directory.CreateDirectory(Path.Combine(m_Root, "ProjectSettings"));
            Write("ProjectSettings/TagManager.asset", "tags: []\n");
            var assets = Initial();
            assets.AddRange(Assets(("ProjectSettings/TagManager.asset", "")));
            Assert.Contains("ProjectSettings/TagManager.asset", Scan(assets).Imported);
            Assert.Empty(Scan(assets).Imported);
            Write("ProjectSettings/TagManager.asset", "tags:\n- ProbeTag\n");
            Assert.Equal(new[] { "ProjectSettings/TagManager.asset" }, Scan(assets).Imported);
        }

        // ---------------------------------------------------------------- dispatch and order

        static readonly List<string> s_Calls = new List<string>();

        sealed class Five : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload) =>
                s_Calls.Add($"Five {string.Join(",", imported)} {string.Join(",", deleted)} {string.Join(",", moved)}<-{string.Join(",", movedFrom)} {didDomainReload}");
        }

        sealed class Four : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) =>
                s_Calls.Add($"Four {string.Join(",", imported)} {string.Join(",", deleted)}");
        }

        // Both forms: Unity's C# takes the four-argument one.
        sealed class Both : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) => s_Calls.Add("Both four");
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload) => s_Calls.Add("Both five");
        }

        static List<System.Reflection.MethodInfo> Methods(params Type[] types) =>
            types.Select(t => t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Where(m => m.Name == "OnPostprocessAllAssets").OrderBy(m => m.GetParameters().Length).First()).ToList();

        static IEnumerable<string> Ours() => s_Calls.Where(c => c.StartsWith("Five") || c.StartsWith("Four") || c.StartsWith("Both"));

        [Fact]
        public void TheFourArgumentFormIsCalledOnlyWhenAListHoldsAnAsset()
        {
            var callbacks = Methods(typeof(Five), typeof(Four));
            s_Calls.Clear();
            AssetPostprocessing.Dispatch(callbacks, new ProjectImports.Changes(), null);
            Assert.Equal(new[] { "Five   <- True" }, Ours());
            s_Calls.Clear();
            AssetPostprocessing.Dispatch(callbacks, new ProjectImports.Changes { Deleted = new[] { "Assets/Gone.mat" } }, null);
            Assert.Equal(new[] { "Five  Assets/Gone.mat <- True", "Four  Assets/Gone.mat" }, Ours());
            s_Calls.Clear();
            // The start's lists and the imports asked for while the domain loaded, once each.
            AssetPostprocessing.Dispatch(callbacks, new ProjectImports.Changes { Imported = new[] { "Assets/A.mat" }, Moved = new[] { "Assets/N/B.mat" }, MovedFrom = new[] { "Assets/B.mat" } },
                new[] { "Assets/A.mat", "Assets/Sprite.png" });
            Assert.Equal(new[] { "Five Assets/A.mat,Assets/Sprite.png  Assets/N/B.mat<-Assets/B.mat True", "Four Assets/A.mat,Assets/Sprite.png " }, Ours());
        }

        [Fact]
        public void AClassWithBothFormsIsGivenTheFourArgumentOne()
        {
            s_Calls.Clear();
            AssetPostprocessing.ImportedAssets(typeof(ProjectImportsTests).Assembly, new[] { "Assets/X.mat" });
            Assert.Contains("Both four", s_Calls);
            Assert.DoesNotContain("Both five", s_Calls);
        }

        static class Root1 { static void OnPostprocessAllAssets(string[] i, string[] d, string[] m, string[] f) => s_Calls.Add("Root1"); }
        static class Root2 { static void OnPostprocessAllAssets(string[] i, string[] d, string[] m, string[] f) => s_Calls.Add("Root2"); }
        static class Root3 { static void OnPostprocessAllAssets(string[] i, string[] d, string[] m, string[] f) => s_Calls.Add("Root3"); }

        // Ordering only reads the methods: these are no postprocessors, so other tests' dispatches
        // over this assembly do not call them. Follows the first root: Kahn's algorithm runs it
        // after every root.
        static class After1
        {
            [UnityEditor.Callbacks.RunAfterClass(typeof(Root1))]
            static void OnPostprocessAllAssets(string[] i, string[] d, string[] m, string[] f) => s_Calls.Add("After1");
        }

        static class lowercase { static void OnPostprocessAllAssets(string[] i, string[] d, string[] m, string[] f) => s_Calls.Add("lowercase"); }

        [Fact]
        public void CallbacksRunBreadthFirstAfterTheRootsInACulturesNameOrder()
        {
            s_Calls.Clear();
            var ordered = AssetPostprocessing.Ordered(Methods(typeof(After1), typeof(Root3), typeof(lowercase), typeof(Root1), typeof(Root2)));
            Assert.Equal(new[] { "lowercase", "Root1", "Root2", "Root3", "After1" }, ordered.Select(m => m.DeclaringType.Name));
        }

        static class CycleA
        {
            [UnityEditor.Callbacks.RunAfterClass(typeof(CycleB))]
            static void OnPostprocessAllAssets(string[] i, string[] d, string[] m, string[] f) => s_Calls.Add("CycleA");
        }

        static class CycleB
        {
            [UnityEditor.Callbacks.RunAfterClass(typeof(CycleA))]
            static void OnPostprocessAllAssets(string[] i, string[] d, string[] m, string[] f) => s_Calls.Add("CycleB");
        }

        [Fact]
        public void CallbacksInACycleAreNotCalledWithUnitysError()
        {
            var logs = new LogCapture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            try
            {
                var ordered = AssetPostprocessing.Ordered(Methods(typeof(CycleA), typeof(Root1), typeof(CycleB)));
                Assert.Equal(new[] { "Root1" }, ordered.Select(m => m.DeclaringType.Name));
                // The same callbacks again: ordered once, reported once.
                Assert.Equal(new[] { "Root1" }, AssetPostprocessing.Ordered(Methods(typeof(CycleA), typeof(Root1), typeof(CycleB))).Select(m => m.DeclaringType.Name));
            }
            finally { Debug.unityLogger.logHandler = previous; }
            var prefix = typeof(ProjectImportsTests).FullName + "+";
            Assert.Equal(new[] { "Error: Found cycles in callback dependency graph for OnPostprocessAllAssets.\nThe following nodes could not be added:\n"
                + prefix + "CycleA\n-" + prefix + "CycleB\n--<color=red>" + prefix + "CycleA</color>\n" }, logs.Lines.Select(l => l.Replace("\r\n", "\n")));
        }

        [Fact]
        public void TheStartRunsInEditModeWhilePlayModeIsBeingEntered()
        {
            bool playing = true, changing = true;
            EditorSession.BeginEnteringPlayMode();
            try
            {
                Assert.True(EditorApplication.isPlayingOrWillChangePlaymode);
                EditorSession.AsEditMode(() =>
                {
                    playing = EditorApplication.isPlaying;
                    changing = EditorApplication.isPlayingOrWillChangePlaymode;
                    // A request to enter Play mode now is the entry under way.
                    EditorApplication.isPlaying = true;
                });
                Assert.False(playing);
                Assert.False(changing);
                Assert.True(EditorApplication.isPlayingOrWillChangePlaymode);
            }
            finally { EditorSession.EndEnteringPlayMode(); }
            Assert.True(EditorApplication.isPlaying);
            Assert.False(EditorSession.WillChange);
        }

        [Fact]
        public void AFoldersMainTypeIsAnsweredWithoutLoadingIt()
        {
            AssetDatabase.List("[{\"guid\":\"f1a5c0de0000000000000000000000f1\",\"path\":\"Assets/Folder\",\"kind\":\"folder\"}]");
            try
            {
                Assert.Equal(typeof(DefaultAsset), AssetDatabase.GetMainAssetTypeAtPath("Assets/Folder"));
                Assert.False(AssetDatabase.IsMainAssetAtPathLoaded("Assets/Folder"));
                Assert.Null(AssetDatabase.GetMainAssetTypeAtPath("Assets/Missing"));
            }
            finally { AssetDatabase.Invalidate(); }
        }

        sealed class LogCapture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add("Exception: " + exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(logType + ": " + string.Format(format, args));
        }
    }
}
