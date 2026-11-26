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
                // A folder the session made does not cover a file added to it from outside.
                ProjectImports.Wrote("Assets/M");
                var now = Assets(("Assets", ""), ("Assets/M", "f0"), ("Assets/M/A.mat", "a1"), ("Assets/M/B.mat", "b1"), ("Assets/M/D.mat", "d1"));
                ProjectImports.Settle(now, Record);
                var next = ProjectImports.Scan(now, Record, out _);
                Assert.Equal(new[] { "Assets/M/B.mat", "Assets/M/D.mat" }, next.Imported);
                Assert.Empty(next.Deleted);
            }
            finally { ProjectImports.ResetForTests(); }
        }

        [Fact]
        public void AFolderTheSessionDeletedOrMovedCoversWhatItHeldAndARefreshsImportsAreTheSessions()
        {
            Directory.CreateDirectory(Path.Combine(m_Root, "Assets/N"));
            Write("Assets/N.meta", "guid: n1\n");
            Write("Assets/N/E.mat", "e");
            Write("Assets/N/E.mat.meta", "guid: e1\n");
            var withN = Initial();
            withN.AddRange(Assets(("Assets/N", "n1"), ("Assets/N/E.mat", "e1")));
            ProjectImports.Scan(withN, Record, out var start);
            ProjectImports.ResetForTests();
            ProjectImports.BaseForTests(start);
            try
            {
                // The session deletes folder N (and so E); a refresh finds B changed outside and imports it.
                Directory.Delete(Path.Combine(m_Root, "Assets/N"), true);
                File.Delete(Path.Combine(m_Root, "Assets/N.meta"));
                ProjectImports.Wrote("Assets/N", tree: true);
                Write("Assets/M/B.mat", "b changed outside");
                AssetDatabase.ApplyRefresh("{\"assets\":[{\"path\":\"Assets/M/B.mat\",\"guid\":\"b1\"}]}");
                ProjectImports.Settle(Initial(), Record);
                var next = ProjectImports.Scan(Initial(), Record, out _);
                Assert.Empty(next.Imported);
                Assert.Empty(next.Deleted);
            }
            finally
            {
                ProjectImports.ResetForTests();
                AssetDatabase.Invalidate();
            }
        }

        /// <summary>A Refresh imports again what changed on disk since the session saw it (its file
        /// or its .meta), not a file only touched, not what the session wrote itself, and each change
        /// once (tests/projects/scenesave refresh-changes touch, text, meta).</summary>
        [Fact]
        public void TheSessionViewFindsFilesChangedOnDiskOnce()
        {
            ProjectImports.Scan(Initial(), Record, out var start);
            var before = ProjectInfo.ProjectPath;
            ProjectImports.ResetForTests();
            ProjectImports.SessionForTests(start);
            ProjectInfo.ProjectPath = m_Root;
            AssetDatabase.List("[{\"guid\":\"a1\",\"path\":\"Assets/M/A.mat\",\"kind\":\"yaml\"},{\"guid\":\"b1\",\"path\":\"Assets/M/B.mat\",\"kind\":\"yaml\"},"
                + "{\"guid\":\"c1\",\"path\":\"Assets/M/C.mat\",\"kind\":\"yaml\"}]");
            try
            {
                Assert.Empty(ProjectImports.ChangedSinceSeen());
                var b = Path.Combine(m_Root, "Assets/M/B.mat");
                File.SetLastWriteTimeUtc(b, File.GetLastWriteTimeUtc(b).AddMinutes(5));
                Write("Assets/M/C.mat", "c changed outside");
                // The session's own write, as an AssetDatabase write ends.
                Write("Assets/M/A.mat", "a saved");
                ProjectImports.Wrote("Assets/M/A.mat");
                ProjectImports.SeeWrites();
                Assert.Equal(new[] { "Assets/M/C.mat" }, ProjectImports.ChangedSinceSeen());
                Assert.Empty(ProjectImports.ChangedSinceSeen());
                // A .meta edited outside, then one asset asked about alone (ImportAsset).
                Write("Assets/M/B.mat.meta", "guid: b1\nNativeFormatImporter:\n  mainObjectFileID: 0\n");
                Assert.Equal(new[] { "Assets/M/B.mat" }, ProjectImports.ChangedSinceSeen());
                Write("Assets/M/A.mat", "a changed outside");
                Assert.True(ProjectImports.ChangedSinceSeen("Assets/M/A.mat"));
                Assert.False(ProjectImports.ChangedSinceSeen("Assets/M/A.mat"));
                Assert.Empty(ProjectImports.ChangedSinceSeen());
            }
            finally
            {
                ProjectInfo.ProjectPath = before;
                ProjectImports.ResetForTests();
                AssetDatabase.Invalidate();
            }
        }

        /// <summary>Without a session view (no project) nothing is compared.</summary>
        [Fact]
        public void WithoutASessionViewNothingIsFound()
        {
            ProjectImports.ResetForTests();
            Assert.Empty(ProjectImports.ChangedSinceSeen());
            Assert.False(ProjectImports.ChangedSinceSeen("Assets/M/A.mat"));
        }

        sealed class GameData : ScriptableObject { }

        /// <summary>A loaded text asset takes a new import's content (the copy, destroyed next, the
        /// old one); what is kept in place: text assets, imported textures (not one a script made)
        /// materials, UI Toolkit documents and game scripts' objects, not the engine's other script
        /// objects (a style sheet) or meshes.</summary>
        [Fact]
        public void WhatTakesANewImportInPlace()
        {
            var kept = new TextAsset("notes");
            var copy = new TextAsset("changed notes");
            AssetDatabaseRuntime.SwapContent(copy, kept);
            Assert.Equal("changed notes", kept.text);
            Assert.Equal("notes", copy.text);
            Assert.True(AssetDatabaseRuntime.TakesContentInPlace(kept));
            var game = ScriptableObject.CreateInstance<GameData>();
            var engine = ScriptableObject.CreateInstance<UnityEngine.UIElements.StyleSheet>();
            var document = ScriptableObject.CreateInstance<UnityEngine.UIElements.VisualTreeAsset>();
            var texture = new Texture2D(4, 4);
            var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var mesh = new Mesh();
            try
            {
                Assert.True(AssetDatabaseRuntime.TakesContentInPlace(game));
                Assert.False(AssetDatabaseRuntime.TakesContentInPlace(engine));
                Assert.True(AssetDatabaseRuntime.TakesContentInPlace(document));
                Assert.False(AssetDatabaseRuntime.TakesContentInPlace(texture));
                Assert.True(AssetDatabaseRuntime.TakesContentInPlace(material));
                Assert.False(AssetDatabaseRuntime.TakesContentInPlace(mesh));
            }
            finally
            {
                foreach (var o in new Object[] { game, engine, document, texture, material, mesh }) Object.DestroyImmediate(o);
            }
        }

        /// <summary>The overrides a changed document's new tree holds name the loaded document, not
        /// the copy destroyed after it takes the tree.</summary>
        [Fact]
        public void ADocumentsOverridesNameTheLoadedDocument()
        {
            const string text = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Template name=\"Row\" src=\"Row.uxml\" />"
                + "<ui:Instance template=\"Row\"><ui:AttributeOverrides element-name=\"x\" text=\"y\" /></ui:Instance></ui:UXML>\n";
            var kept = ScriptableObject.CreateInstance<UnityEngine.UIElements.VisualTreeAsset>();
            var copy = ScriptableObject.CreateInstance<UnityEngine.UIElements.VisualTreeAsset>();
            try
            {
                Unbound.UIElements.UxmlImporter.Import("Assets/Main.uxml", text, kept);
                Unbound.UIElements.UxmlImporter.Import("Assets/Main.uxml", text, copy);
                Unbound.UIElements.UxmlImporter.Adopt(kept, copy, "Assets/Main.uxml");
                var sources = new System.Collections.Generic.List<UnityEngine.UIElements.VisualTreeAsset>();
                void Visit(Unbound.UIElements.UxmlNode node)
                {
                    foreach (var o in node.Overrides ?? new System.Collections.Generic.List<Unbound.UIElements.UxmlAttributeOverride>()) sources.Add(o.Source);
                    foreach (var c in node.Children) Visit(c);
                }
                Visit(kept.m_UaRoot);
                Assert.NotEmpty(sources);
                Assert.All(sources, source => Assert.Same(kept, source));
            }
            finally
            {
                Object.DestroyImmediate(kept);
                Object.DestroyImmediate(copy);
            }
        }

        sealed class WarningCapture : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly System.Collections.Generic.List<string> m_Warnings;

            internal WarningCapture(ILogHandler inner, System.Collections.Generic.List<string> warnings)
            {
                m_Inner = inner;
                m_Warnings = warnings;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning) m_Warnings.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(System.Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        /// <summary>A loaded UI Toolkit document takes its changed file's tree: a clone made after
        /// shows the new content, the copy the old (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/scenesave refresh-changes document.after).</summary>
        [Fact]
        public void ALoadedDocumentTakesItsNewTree()
        {
            const string path = "Assets/Doc.uxml";
            var kept = ScriptableObject.CreateInstance<UnityEngine.UIElements.VisualTreeAsset>();
            var copy = ScriptableObject.CreateInstance<UnityEngine.UIElements.VisualTreeAsset>();
            var shown = new GameObject("Shown").AddComponent<UnityEngine.UIElements.UIDocument>();
            var other = ScriptableObject.CreateInstance<UnityEngine.UIElements.VisualTreeAsset>();
            var cloned = new GameObject("Cloned").AddComponent<UnityEngine.UIElements.UIDocument>();
            // Unbound's diagnostics bypass Application.logMessageReceived: captured at the handler.
            var warnings = new System.Collections.Generic.List<string>();
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            logger.logHandler = new WarningCapture(previous, warnings);
            try
            {
                Unbound.UIElements.UxmlImporter.Import(path, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label text=\"first\" /></ui:UXML>\n", kept);
                Unbound.UIElements.UxmlImporter.Import(path, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label text=\"second\" /></ui:UXML>\n", copy);
                shown.visualTreeAsset = kept;
                // Another document's panel with a clone of the changed one a script added.
                Unbound.UIElements.UxmlImporter.Import("Assets/Other.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label text=\"other\" /></ui:UXML>\n", other);
                cloned.visualTreeAsset = other;
                Assert.NotNull(cloned.rootVisualElement);
                cloned.rootVisualElement.Add(kept.CloneTree());
                Assert.Null(AssetDatabaseRuntime.InPlaceRefusal(kept, copy));
                Unbound.UIElements.UxmlImporter.Adopt(kept, copy, path);
                Assert.Equal("second", UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(kept.CloneTree()).text);
                Assert.Equal("first", UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(copy.CloneTree()).text);
                // The document a UIDocument shows: its elements stay, with a warning.
                Assert.Contains(warnings, w => w.Contains(path + " changed on disk; the UIDocument of 'Shown' keeps the elements made from it"));
                Assert.Contains(warnings, w => w.Contains(path + " changed on disk; the UIDocument of 'Cloned' keeps the elements made from it"));
            }
            finally
            {
                logger.logHandler = previous;
                Object.DestroyImmediate(shown.gameObject);
                Object.DestroyImmediate(cloned.gameObject);
                Object.DestroyImmediate(other);
                Object.DestroyImmediate(kept);
                Object.DestroyImmediate(copy);
            }
        }

        /// <summary>A material variant, or one with locked properties, is not taken in place (the
        /// value copy would keep its old parent and overrides); a plain material is; an object the
        /// new import lacks or has as another type is not.</summary>
        [Fact]
        public void AMaterialVariantIsNotTakenInPlace()
        {
            var shader = Shader.Find("Hidden/InternalErrorShader");
            var plain = new Material(shader);
            var copy = new Material(shader);
            var parent = new Material(shader);
            var variant = new Material(shader) { parent = parent };
            var text = new TextAsset("notes");
            try
            {
                Assert.Null(AssetDatabaseRuntime.InPlaceRefusal(plain, copy));
                Assert.NotNull(AssetDatabaseRuntime.InPlaceRefusal(plain, variant));
                Assert.NotNull(AssetDatabaseRuntime.InPlaceRefusal(variant, copy));
                Assert.NotNull(AssetDatabaseRuntime.InPlaceRefusal(plain, null));
                Assert.NotNull(AssetDatabaseRuntime.InPlaceRefusal(plain, text));
                // A material that locks a property for its variants.
                parent.SetPropertyLock("_Color", true);
                Assert.NotNull(AssetDatabaseRuntime.InPlaceRefusal(parent, copy));
            }
            finally
            {
                foreach (var o in new Object[] { plain, copy, parent, variant, text }) Object.DestroyImmediate(o);
            }
        }

        /// <summary>ImportAsset of an asset changed on disk between StartAssetEditing and
        /// StopAssetEditing imports and reports it at the last StopAssetEditing, not before
        /// (Scripting API 6000.0, AssetDatabase.StartAssetEditing).</summary>
        [Fact]
        public void AChangedAssetImportedWhileEditingIsImportedAtTheLastStop()
        {
            ProjectImports.Scan(Initial(), Record, out var start);
            var before = ProjectInfo.ProjectPath;
            ProjectImports.ResetForTests();
            ProjectImports.SessionForTests(start);
            ProjectInfo.ProjectPath = m_Root;
            AssetDatabase.List("[{\"guid\":\"c1\",\"path\":\"Assets/M/C.mat\",\"kind\":\"yaml\"}]");
            var reports = new List<string>();
            using var capture = SessionImports.ReportsForTests((imported, deleted, moved, from) => reports.Add(string.Join(",", imported)));
            try
            {
                AssetDatabase.StartAssetEditing();
                AssetDatabase.StartAssetEditing();
                Write("Assets/M/C.mat", "c changed outside");
                AssetDatabase.ImportAsset("Assets/M/C.mat");
                AssetDatabase.StopAssetEditing();
                Assert.Empty(reports);
                Assert.Equal(1, AssetDatabase.PendingReimportCount);
                AssetDatabase.StopAssetEditing();
                Assert.Equal(0, AssetDatabase.PendingReimportCount);
                Assert.Equal(new[] { "Assets/M/C.mat" }, reports);
                // Seen now: a Refresh finds nothing more.
                Assert.Empty(ProjectImports.ChangedSinceSeen());
            }
            finally
            {
                while (AssetDatabase.Editing) AssetDatabase.StopAssetEditing();
                ProjectInfo.ProjectPath = before;
                ProjectImports.ResetForTests();
                AssetDatabase.Invalidate();
            }
        }

        [Fact]
        public void AForcedImportOfAnUnloadedAssetIsTheSessionsImport()
        {
            ProjectImports.Scan(Initial(), Record, out var start);
            ProjectImports.ResetForTests();
            ProjectImports.BaseForTests(start);
            var logs = new LogCapture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            try
            {
                AssetDatabase.List("[{\"guid\":\"c1\",\"path\":\"Assets/M/C.mat\",\"kind\":\"yaml\"}]");
                // Changed outside, then imported by a tool in a different spelling of its path.
                Write("Assets/M/C.mat", "c changed outside");
                AssetDatabase.ImportAsset("assets/m/c.mat", ImportAssetOptions.ForceUpdate);
                ProjectImports.Settle(Initial(), Record);
                Assert.Empty(ProjectImports.Scan(Initial(), Record, out _).Imported);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                ProjectImports.ResetForTests();
                AssetDatabase.Invalidate();
            }
        }

        [Fact]
        public void AForcedImportOfAV2SpriteAtlasIsTheSessionsImport()
        {
            Write("Assets/M/S.spriteatlasv2", "s");
            Write("Assets/M/S.spriteatlasv2.meta", "guid: s1\n");
            var assets = Initial();
            assets.AddRange(Assets(("Assets/M/S.spriteatlasv2", "f1a5c0de0000000000000000000000c7")));
            ProjectImports.Scan(assets, Record, out var start);
            ProjectImports.ResetForTests();
            ProjectImports.BaseForTests(start);
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new LogCapture();
            try
            {
                AssetDatabase.List("[{\"guid\":\"f1a5c0de0000000000000000000000c7\",\"path\":\"Assets/M/S.spriteatlasv2\",\"kind\":\"yaml\"}]");
                Write("Assets/M/S.spriteatlasv2", "s changed outside");
                AssetDatabase.ImportAsset("assets/m/s.spriteatlasv2", ImportAssetOptions.ForceUpdate);
                ProjectImports.Settle(assets, Record);
                Assert.Empty(ProjectImports.Scan(assets, Record, out _).Imported);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                ProjectImports.ResetForTests();
                AssetDatabase.Invalidate();
            }
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

        sealed class Data : ScriptableObject { }

        [Fact]
        public void TheFirstOpenWritesWhatTheDispatchDirtiedNotWhatWasDirtyBeforeOrByItsLoad()
        {
            var before = ScriptableObject.CreateInstance<Data>();
            before.name = "DirtyBefore";
            var kept = ScriptableObject.CreateInstance<Data>();
            kept.name = "Edited";
            EditorUtility.SetDirty(before);
            Data loaded = null, edited = null, added = null, upgraded = null;
            try
            {
                var files = EditorInitialization.ChangedDuring(() =>
                {
                    // A load that dirties its own object (OnEnable calling SetDirty), then read whole.
                    loaded = ScriptableObject.CreateInstance<Data>();
                    loaded.name = "DirtiedByItsLoad";
                    EditorUtility.SetDirty(loaded);
                    var file = new LoadedFile();
                    file.Objects[11400000] = loaded;
                    AssetDatabaseRuntime.FileRead?.Invoke(file);
                    // A loaded asset a postprocessor edits, and a new object it adds to an asset.
                    edited = kept;
                    EditorUtility.SetDirty(edited);
                    added = ScriptableObject.CreateInstance<Data>();
                    added.name = "Added";
                    EditorUtility.SetDirty(added);
                    // Read during the call, then edited (a pipeline's material upgrade): a change.
                    upgraded = ScriptableObject.CreateInstance<Data>();
                    upgraded.name = "Upgraded";
                    var read = new LoadedFile();
                    read.Objects[11400000] = upgraded;
                    AssetDatabaseRuntime.FileRead?.Invoke(read);
                    EditorUtility.SetDirty(upgraded);
                }, o => o is Data ? o.name : null);
                Assert.Equal(new[] { "Added", "Edited", "Upgraded" }, files.OrderBy(f => f, StringComparer.Ordinal));
            }
            finally
            {
                foreach (var o in new Object[] { before, kept, loaded, added, upgraded })
                    if (o != null) { EditorUtility.ClearDirty(o); Object.DestroyImmediate(o); }
            }
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

        static readonly ImportedTextureInfo Square4 = new ImportedTextureInfo
        {
            Width = 4, Height = 4, MipCount = 1, Srgb = 1, FilterMode = (int)FilterMode.Bilinear, AnisoLevel = 1, Readable = 1, Format = 0, Dimension = 2,
        };

        /// <summary>A generated texture imported again takes the new import's texels under its own
        /// handle and the copy (destroyed next) the old ones (importselfload kept.Thing.gen width:
        /// the old reference has the new size); a failed import changes neither.</summary>
        [Fact]
        public void AGeneratedTextureTakesTheNewImportUnderItsHandle()
        {
            var larger = Square4;
            larger.Width = larger.Height = 8;
            var calls = new List<(ulong, string)>();
            bool succeed = true;
            var previous = Texture2D.Reimport;
            Texture2D.Reimport = (handle, key) => { calls.Add((handle, key)); return (succeed, larger); };
            var old = Texture2D.FromImport(9101, "generated:00000000000000000000000000000a01", "Thing", Square4);
            var fresh = Texture2D.FromImport(9102, "generated:00000000000000000000000000000a02", "Thing", larger);
            try
            {
                Assert.Null(old.TakeImport(fresh));
                Assert.Equal(new[] { (9101UL, "generated:00000000000000000000000000000a02") }, calls);
                Assert.Equal(8, old.width);
                Assert.Equal(9101UL, old.m_NativeHandle);
                Assert.True(old.IsImported);
                // The copy releases the texels the loaded texture had.
                Assert.Equal("generated:00000000000000000000000000000a01", fresh.m_ImportedFrom);

                succeed = false;
                var other = Texture2D.FromImport(9103, "generated:00000000000000000000000000000a03", "Thing", Square4);
                Assert.NotNull(old.TakeImport(other));
                Assert.Equal("generated:00000000000000000000000000000a02", old.m_ImportedFrom);
                Assert.Equal("generated:00000000000000000000000000000a03", other.m_ImportedFrom);
                Assert.Equal(8, old.width);
                other.m_ImportedFrom = null;
                Object.DestroyImmediate(other, true);
            }
            finally
            {
                Texture2D.Reimport = previous;
                // Not generated texels of the native side: nothing to release.
                old.m_ImportedFrom = fresh.m_ImportedFrom = null;
                Object.DestroyImmediate(old, true);
                Object.DestroyImmediate(fresh, true);
            }
        }

        /// <summary>A texture imported again under its own renderer handle takes the new import's
        /// state whole, what its postprocessors changed included (OnPostprocessTexture edits the
        /// import, Scripting API 6000.0): size, sampler, texels not as imported; the copy gives the
        /// handle up, so destroying it keeps the texture drawn. Nothing is read from the file again.</summary>
        [Fact]
        public void ATextureImportedAgainUnderItsHandleTakesWhatItsPostprocessorsSet()
        {
            var larger = Square4;
            larger.Width = larger.Height = 8;
            var previous = Texture2D.Reimport;
            var reimports = new List<string>();
            Texture2D.Reimport = (handle, key) => { reimports.Add(key); return (true, Square4); };
            const string guid = "c0de00000000000000000000000000a5";
            var old = Texture2D.FromImport(9121, guid, "Plain", Square4);
            var fresh = Texture2D.FromImport(9121, guid, "Plain", larger);
            try
            {
                // As a postprocessor of the new import leaves it.
                fresh.filterMode = FilterMode.Point;
                fresh.IncrementUpdateCount();
                old.IncrementUpdateCount();
                uint counted = old.updateCount;
                Assert.Null(old.TakeImport(fresh));
                Assert.Empty(reimports);
                Assert.Equal(8, old.width);
                Assert.Equal(FilterMode.Point, old.filterMode);
                Assert.Equal(9121UL, old.m_NativeHandle);
                Assert.Equal(0UL, fresh.m_NativeHandle);
                Assert.Equal(counted + 1, old.updateCount);
                Assert.False(old.TexelsAsImported);
                Assert.True(old.IsImported);
            }
            finally
            {
                Texture2D.Reimport = previous;
                Object.DestroyImmediate(fresh, true);
                Object.DestroyImmediate(old, true);
            }
        }

        /// <summary>A loaded texture and its sprite take a new import in place: the texture its
        /// texels, the sprite the new rect and pivot, still drawing the loaded texture, not the new
        /// import's (importselfload kept.Sprite.png: both keep their instance ids).</summary>
        [Fact]
        public void ATextureAndItsSpriteTakeANewImportInPlace()
        {
            var previous = Texture2D.Reimport;
            var reimports = new List<string>();
            Texture2D.Reimport = (handle, key) => { reimports.Add(key); return (true, Square4); };
            const string guid = "c0de00000000000000000000000000b1";
            var texture = Texture2D.FromImport(9111, guid, "Sprite", Square4);
            // The new import is made under the loaded texture's handle.
            var copyTexture = Texture2D.FromImport(9111, guid, "Sprite", Square4);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            var copySprite = Sprite.Create(copyTexture, new Rect(0, 0, 2, 4), Vector2.zero, 50, 0, SpriteMeshType.FullRect);
            int spriteId = sprite.GetInstanceID();
            try
            {
                Assert.Null(AssetDatabaseRuntime.InPlaceRefusal(sprite, copySprite));
                AssetDatabaseRuntime.TakeInPlace(new Dictionary<Object, Object>(ReferenceEqualityComparer.Instance)
                {
                    [copyTexture] = texture,
                    [copySprite] = sprite,
                }, "Assets/Sprite.png");
                Assert.Empty(reimports);
                Assert.Equal(9111UL, texture.m_NativeHandle);
                Assert.Equal(new Rect(0, 0, 2, 4), sprite.rect);
                Assert.Equal(50f, sprite.pixelsPerUnit);
                Assert.Equal(Vector2.zero, sprite.pivot);
                Assert.Same(texture, sprite.texture);
                Assert.Equal(spriteId, sprite.GetInstanceID());
                // The copy, destroyed next, holds the old content.
                Assert.Equal(new Rect(0, 0, 4, 4), copySprite.rect);
            }
            finally
            {
                Texture2D.Reimport = previous;
                foreach (var o in new Object[] { copySprite, sprite, copyTexture, texture }) Object.DestroyImmediate(o, true);
            }
        }

        /// <summary>A sprite packed in an atlas is not taken in place (not measured), nor an asset
        /// whose loaded objects include a kind not taken so; one of text assets is.</summary>
        [Fact]
        public void LoadedObjectsThatCannotTakeANewImportAreSaidBeforeReading()
        {
            var texture = new Texture2D(4, 4);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero, 100, 0, SpriteMeshType.FullRect);
            var bound = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero, 100, 0, SpriteMeshType.FullRect);
            bound.m_AtlasBinding = ("c0de00000000000000000000000000b2", 21300000L);
            var mesh = new Mesh { IsPersistentAsset = true };
            var text = new TextAsset("notes") { IsPersistentAsset = true };
            const string meshGuid = "c0de00000000000000000000000000b3", textGuid = "c0de00000000000000000000000000b4";
            try
            {
                Assert.NotNull(AssetDatabaseRuntime.InPlaceRefusal(bound, sprite));
                Assert.NotNull(AssetDatabaseRuntime.InPlaceRefusal(sprite, bound));
                Assert.True(AssetDatabaseRuntime.TakesContentInPlace(sprite));
                AddLoaded(meshGuid, "Assets/Triangle.fbx", 4300000, mesh);
                AddLoaded(textGuid, "Assets/Notes.txt", AssetImporters.TextAssetFileId, text);
                Assert.Contains("Mesh", AssetDatabaseRuntime.LoadedRefusal(meshGuid));
                Assert.Null(AssetDatabaseRuntime.LoadedRefusal(textGuid));
                Assert.Null(AssetDatabaseRuntime.LoadedRefusal("c0de00000000000000000000000000b5"));
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(meshGuid);
                AssetDatabaseRuntime.Invalidate(textGuid);
                foreach (var o in new Object[] { bound, sprite, texture, mesh, text }) Object.DestroyImmediate(o, true);
            }
        }

        static void AddLoaded(string guid, string path, long fileId, Object asset)
        {
            var file = new LoadedFile { Guid = guid, Path = path, MainObject = asset };
            file.Objects[fileId] = asset;
            AssetDatabaseRuntime.AddLoaded(file);
        }

        /// <summary>ImportAsset of an unchanged asset an importer makes imports it again, with or
        /// without ForceUpdate (importselfload plain.*): a loaded one whose objects cannot take the
        /// new import says so once and keeps them; StartAssetEditing holds it back to the last
        /// StopAssetEditing. An unchanged native-format asset is only reported imported.</summary>
        [Fact]
        public void AnUnchangedImportedAssetIsImportedAgainWithOrWithoutForceUpdate()
        {
            Write("Assets/M/T.png", "t");
            Write("Assets/M/T.png.meta", "guid: t1\n");
            var assets = Initial();
            assets.AddRange(Assets(("Assets/M/T.png", "c0de00000000000000000000000000c1")));
            ProjectImports.Scan(assets, Record, out var start);
            var before = ProjectInfo.ProjectPath;
            ProjectImports.ResetForTests();
            ProjectImports.SessionForTests(start);
            ProjectInfo.ProjectPath = m_Root;
            AssetDatabase.List("[{\"guid\":\"c0de00000000000000000000000000c1\",\"path\":\"Assets/M/T.png\",\"kind\":\"texture\"},"
                + "{\"guid\":\"c1\",\"path\":\"Assets/M/C.mat\",\"kind\":\"yaml\"}]");
            var mesh = new Mesh { IsPersistentAsset = true };
            var material = new Material(Shader.Find("Hidden/InternalErrorShader")) { IsPersistentAsset = true };
            AddLoaded("c0de00000000000000000000000000c1", "Assets/M/T.png", 4300000, mesh);
            AddLoaded("c1", "Assets/M/C.mat", 2100000, material);
            const string member = "AssetDatabase.ImportAsset of texture asset";
            Stubs.Forget(member);
            var reports = new List<string>();
            var logs = new LogCapture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            using var capture = SessionImports.ReportsForTests((imported, deleted, moved, from) => reports.Add(string.Join(",", imported)));
            try
            {
                AssetDatabase.ImportAsset("Assets/M/T.png");
                Assert.Equal(1, Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value);
                Assert.Contains(logs.Lines, l => l.Contains("does not import a loaded asset again (Assets/M/T.png"));
                AssetDatabase.ImportAsset("Assets/M/T.png", ImportAssetOptions.ForceUpdate);
                Assert.Equal(2, Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value);
                Assert.Same(mesh, AssetDatabaseRuntime.Load("c0de00000000000000000000000000c1", 4300000, typeof(Mesh)));
                // The native-format asset: reported, nothing read again, nothing said.
                logs.Lines.Clear();
                AssetDatabase.ImportAsset("Assets/M/C.mat");
                Assert.Empty(logs.Lines);
                Assert.Same(material, AssetDatabaseRuntime.Load("c1", 2100000, typeof(Material)));
                AssetDatabase.StartAssetEditing();
                AssetDatabase.ImportAsset("Assets/M/T.png");
                Assert.Equal(1, AssetDatabase.PendingReimportCount);
                Assert.Equal(2, Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value);
                AssetDatabase.StopAssetEditing();
                Assert.Equal(0, AssetDatabase.PendingReimportCount);
                Assert.Equal(3, Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value);
                Assert.Equal(new[] { "Assets/M/T.png", "Assets/M/T.png", "Assets/M/C.mat", "Assets/M/T.png" }, reports);
            }
            finally
            {
                while (AssetDatabase.Editing) AssetDatabase.StopAssetEditing();
                Debug.unityLogger.logHandler = previous;
                ProjectInfo.ProjectPath = before;
                ProjectImports.ResetForTests();
                AssetDatabaseRuntime.Invalidate("c0de00000000000000000000000000c1");
                AssetDatabaseRuntime.Invalidate("c1");
                AssetDatabase.Invalidate();
                Object.DestroyImmediate(mesh, true);
                Object.DestroyImmediate(material, true);
            }
        }

        /// <summary>ImportAsset of an asset changed on disk and not loaded yet imports it during the
        /// call, as one unchanged (importselfload plain.*, toolapis imported.during): an asset an
        /// importer makes, with or without ForceUpdate; a native-format one only when forced (else
        /// its next load reads it).</summary>
        [Fact]
        public void AChangedAssetNotLoadedIsImportedDuringTheCall()
        {
            Write("Assets/M/U.png", "u");
            Write("Assets/M/U.png.meta", "guid: u1\n");
            var assets = Initial();
            assets.AddRange(Assets(("Assets/M/U.png", "c0de00000000000000000000000000d1")));
            ProjectImports.Scan(assets, Record, out var start);
            var before = ProjectInfo.ProjectPath;
            ProjectImports.ResetForTests();
            ProjectImports.SessionForTests(start);
            ProjectInfo.ProjectPath = m_Root;
            AssetDatabase.List("[{\"guid\":\"c0de00000000000000000000000000d1\",\"path\":\"Assets/M/U.png\",\"kind\":\"texture\"},"
                + "{\"guid\":\"c1\",\"path\":\"Assets/M/C.mat\",\"kind\":\"yaml\"}]");
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new LogCapture();
            try
            {
                // Its preprocessors ran for an earlier import (ImportAsset of the new file), which
                // loaded nothing: this import forgets that, so its read runs them again.
                AssetPostprocessing.EnsurePreprocessed("Assets/M/U.png", "texture");
                Assert.True(AssetPostprocessing.Preprocessed("Assets/M/U.png"));
                Write("Assets/M/U.png", "u changed outside");
                Write("Assets/M/C.mat", "c changed outside");
                AssetDatabase.ImportAsset("Assets/M/U.png");
                // (Without the native side the read fails before the preprocessors run again.)
                Assert.False(AssetPostprocessing.Preprocessed("Assets/M/U.png"));
                AssetDatabase.ImportAsset("Assets/M/C.mat");
                // Read during the call (without the native side the read fails, its entry stays).
                Assert.True(AssetDatabaseRuntime.IsLoaded("c0de00000000000000000000000000d1"));
                Assert.False(AssetDatabaseRuntime.IsLoaded("c1"));
                Write("Assets/M/C.mat", "c changed again");
                AssetDatabase.ImportAsset("Assets/M/C.mat", ImportAssetOptions.ForceUpdate);
                Assert.True(AssetDatabaseRuntime.IsLoaded("c1"));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                AssetPostprocessing.ForgetPreprocessed("Assets/M/U.png");
                ProjectInfo.ProjectPath = before;
                ProjectImports.ResetForTests();
                AssetDatabaseRuntime.Invalidate("c0de00000000000000000000000000d1");
                AssetDatabaseRuntime.Invalidate("c1");
                AssetDatabase.Invalidate();
            }
        }

        /// <summary>An import of an existing asset asked for while the editor domain loads is
        /// among those the domain load lists once it has ended, with or without ForceUpdate (it is
        /// imported again either way); a path that is no asset is not.</summary>
        [Fact]
        public void ADeferredImportOfAnExistingAssetIsListedWithOrWithoutForceUpdate()
        {
            var loading = typeof(EditorInitialization).GetProperty(nameof(EditorInitialization.DomainLoading),
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var run = typeof(EditorInitialization).GetMethod("RunDeferredImports", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            AssetDatabase.List("[{\"guid\":\"c1\",\"path\":\"Assets/M/C.mat\",\"kind\":\"yaml\"},{\"guid\":\"b1\",\"path\":\"Assets/M/B.mat\",\"kind\":\"yaml\"}]");
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new LogCapture();
            try
            {
                loading.SetValue(null, true);
                Assert.True(EditorInitialization.DeferImport("Assets/M/C.mat", ImportAssetOptions.Default));
                Assert.True(EditorInitialization.DeferImport("Assets/M/B.mat", ImportAssetOptions.ForceUpdate));
                Assert.True(EditorInitialization.DeferImport("Assets/M/None.mat", ImportAssetOptions.Default));
                loading.SetValue(null, false);
                Assert.Equal(new[] { "Assets/M/C.mat", "Assets/M/B.mat" }, (string[])run.Invoke(null, null));
            }
            finally
            {
                loading.SetValue(null, false);
                Debug.unityLogger.logHandler = previous;
                AssetDatabaseRuntime.Invalidate("b1");
                AssetDatabase.Invalidate();
            }
        }

        sealed class LogCapture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add("Exception: " + exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(logType + ": " + string.Format(format, args));
        }
    }
}
