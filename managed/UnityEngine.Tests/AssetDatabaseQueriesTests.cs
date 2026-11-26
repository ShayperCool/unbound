using System;
using System.Collections.Generic;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// AssetDatabase's path, folder, AssetBundle name, worker and auto-refresh answers over a listing
    /// (matches the Unity 6000.0.71f1 Editor, tests/projects/assetqueries).
    /// </summary>
    // The listing is global: with the other tests that replace it (AssetDependenciesTests).
    [Collection("EditorState")]
    public sealed class AssetDatabaseQueriesTests : IDisposable
    {
        readonly ILogHandler m_Handler = Debug.unityLogger.logHandler;
        readonly Capture m_Logs = new Capture();
        readonly IDisposable m_Listing;

        public AssetDatabaseQueriesTests()
        {
            Debug.unityLogger.logHandler = m_Logs;
            AssetDatabase.ForgetBundleNames();
            m_Listing = AssetDatabase.ListForTests("""
                [
                  {"guid":"00000000000000001000000000000000","path":"Assets","kind":"folder","importer":"DefaultImporter","labels":[]},
                  {"guid":"10000000000000000000000000000001","path":"Assets/Data","kind":"folder","importer":"DefaultImporter","labels":[]},
                  {"guid":"10000000000000000000000000000002","path":"Assets/Data/A.txt","kind":"text","importer":"TextScriptImporter","labels":[],"bundle":"bundle-a","variant":""},
                  {"guid":"10000000000000000000000000000003","path":"Assets/Data/B.txt","kind":"text","importer":"TextScriptImporter","labels":[],"bundle":"bundle-b","variant":"hd"},
                  {"guid":"10000000000000000000000000000004","path":"Assets/Data/Plain.txt","kind":"text","importer":"TextScriptImporter","labels":[],"bundle":"","variant":""},
                  {"guid":"10000000000000000000000000000005","path":"Assets/Bundled","kind":"folder","importer":"DefaultImporter","labels":[],"bundle":"folder-bundle","variant":"sd"},
                  {"guid":"10000000000000000000000000000006","path":"Assets/Bundled/Sub","kind":"folder","importer":"DefaultImporter","labels":[],"bundle":"","variant":""},
                  {"guid":"10000000000000000000000000000007","path":"Assets/Bundled/Sub/D.txt","kind":"text","importer":"TextScriptImporter","labels":[],"bundle":"","variant":""},
                  {"guid":"10000000000000000000000000000008","path":"Assets/Bundled/C.txt","kind":"text","importer":"TextScriptImporter","labels":[],"bundle":"","variant":""},
                  {"guid":"10000000000000000000000000000009","path":"Assets/Data/Other/A.txt","kind":"text","importer":"TextScriptImporter","labels":[],"bundle":"bundle-a","variant":""}
                ]
                """);
        }

        public void Dispose()
        {
            Debug.unityLogger.logHandler = m_Handler;
            AssetDatabase.ForgetBundleNames();
            m_Listing.Dispose();
        }

        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add("Exception: " + exception.Message);
            public void LogFormat(LogType type, Object context, string format, params object[] args) =>
                Lines.Add(type + ": " + string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
        }

        [Fact]
        public void AMetaPathIsThePathWithMetaAddedATrailingSlashLeftOut()
        {
            Assert.Equal("Assets/Data.meta", AssetDatabase.GetTextMetaFilePathFromAssetPath("Assets/Data/"));
            Assert.Equal("Assets\\Data\\A.txt.meta", AssetDatabase.GetTextMetaFilePathFromAssetPath("Assets\\Data\\A.txt"));
            Assert.Equal("Assets/Nothing.meta", AssetDatabase.GetTextMetaFilePathFromAssetPath("Assets/Nothing"));
            Assert.Equal(".meta", AssetDatabase.GetTextMetaFilePathFromAssetPath(null));
            Assert.Equal(".meta", AssetDatabase.GetTextMetaFilePathFromAssetPath(""));
        }

        [Fact]
        public void AnAssetPathExistsAsTheDatabaseNamesIt()
        {
            Assert.True(AssetDatabase.AssetPathExists("Assets"));
            Assert.True(AssetDatabase.AssetPathExists("Assets/Data/A.txt"));
            Assert.True(AssetDatabase.AssetPathExists("assets/data/a.txt"));
            Assert.True(AssetDatabase.AssetPathExists("Assets\\Data\\A.txt"));
            Assert.False(AssetDatabase.AssetPathExists("Assets/Data/"));
            Assert.False(AssetDatabase.AssetPathExists("Assets/Data/A.txt/"));
            Assert.False(AssetDatabase.AssetPathExists("Assets/./Data/A.txt"));
            Assert.False(AssetDatabase.AssetPathExists(" Assets/Data/A.txt"));
            Assert.False(AssetDatabase.AssetPathExists("Assets/Data/Missing.txt"));
            Assert.False(AssetDatabase.AssetPathExists(null));
            Assert.Empty(m_Logs.Lines);
            var absolute = System.IO.Path.GetFullPath("Assets/Data/A.txt");
            Assert.False(AssetDatabase.AssetPathExists(absolute));
            Assert.Equal(new[] { "Warning: Invalid AssetDatabase path: " + absolute + ". Use path relative to the project folder." }, m_Logs.Lines);
        }

        [Fact]
        public void AFolderInfoIsOfThePathsRootNotOfTheAsset()
        {
            Assert.True(AssetDatabase.TryGetAssetFolderInfo("Assets", out bool root, out bool immutable));
            Assert.True(root);
            Assert.False(immutable);
            // A missing file under Assets, in other spellings too.
            foreach (var path in new[] { "Assets/Data/Missing.txt", "assets/data/a.txt", "Assets/Data/", "Assets\\Data\\A.txt", "Assets/./Data/A.txt" })
            {
                Assert.True(AssetDatabase.TryGetAssetFolderInfo(path, out root, out immutable));
                Assert.False(root);
            }
            foreach (var path in new[] { " Assets/Data/A.txt", "Packages", "Library", "", null, "Other/A.txt", "AssetsMore/A.txt" })
                Assert.False(AssetDatabase.TryGetAssetFolderInfo(path, out root, out immutable));
        }

        [Fact]
        public void AssetBundleNamesListWithTheirVariantsAndFoldersNameTheirAssets()
        {
            Assert.Equal(new[] { "bundle-a", "bundle-b.hd", "folder-bundle.sd" }, AssetDatabase.GetAllAssetBundleNames());
            Assert.Empty(AssetDatabase.GetUnusedAssetBundleNames());
            Assert.Equal(new[] { "Assets/Data/A.txt", "Assets/Data/Other/A.txt" }, AssetDatabase.GetAssetPathsFromAssetBundle("bundle-a"));
            Assert.Empty(AssetDatabase.GetAssetPathsFromAssetBundle("BUNDLE-A"));
            Assert.Empty(AssetDatabase.GetAssetPathsFromAssetBundle("bundle-b"));
            Assert.Equal(new[] { "Assets/Data/B.txt" }, AssetDatabase.GetAssetPathsFromAssetBundle("bundle-b.hd"));
            // A folder's name is its assets', its subfolders' too; the folders are not listed.
            Assert.Equal(new[] { "Assets/Bundled/C.txt", "Assets/Bundled/Sub/D.txt" }, AssetDatabase.GetAssetPathsFromAssetBundle("folder-bundle.sd"));
            Assert.Equal(new[] { "Assets/Data/A.txt", "Assets/Data/Other/A.txt" }, AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName("bundle-a", "a"));
            Assert.Empty(AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName("bundle-a", "A.txt"));
            Assert.Equal("folder-bundle", AssetDatabase.GetImplicitAssetBundleName("Assets/Bundled/Sub/D.txt"));
            Assert.Equal("sd", AssetDatabase.GetImplicitAssetBundleVariantName("Assets/Bundled/Sub"));
            Assert.Equal("", AssetDatabase.GetImplicitAssetBundleName("Assets/Data/Plain.txt"));
            var missing = Assert.Throws<ArgumentException>(() => AssetDatabase.GetImplicitAssetBundleName("Assets/Bundled/Missing.txt"));
            Assert.Equal("The specified asset Assets/Bundled/Missing.txt cannot be found.", missing.Message);
        }

        [Fact]
        public void ANameStaysInTheDatabaseUntilRemovedAndRemovingLeavesTheAssets()
        {
            // A name given to an asset, then taken away: unused until removed.
            AssetDatabase.BundleNameSet("Assets/Data/Plain.txt", "temp-bundle", "", written: false);
            Assert.Equal(new[] { "Assets/Data/Plain.txt" }, AssetDatabase.GetAssetPathsFromAssetBundle("temp-bundle"));
            AssetDatabase.BundleNameSet("Assets/Data/Plain.txt", "", "", written: false);
            Assert.Contains("temp-bundle", AssetDatabase.GetAllAssetBundleNames());
            Assert.Equal(new[] { "temp-bundle" }, AssetDatabase.GetUnusedAssetBundleNames());
            AssetDatabase.RemoveUnusedAssetBundleNames();
            Assert.DoesNotContain("temp-bundle", AssetDatabase.GetAllAssetBundleNames());

            // One in use stays unless forced, said with the first asset that has it.
            Assert.False(AssetDatabase.RemoveAssetBundleName("bundle-a", false));
            Assert.Equal(new[] { "Warning: AssetBundle name \"bundle-a\" is still in use by \"Assets/Data/A.txt\"." }, m_Logs.Lines);
            Assert.True(AssetDatabase.RemoveAssetBundleName("bundle-a", true));
            Assert.Equal(new[] { "bundle-b.hd", "folder-bundle.sd" }, AssetDatabase.GetAllAssetBundleNames());
            // The assets keep it.
            Assert.Equal("bundle-a", AssetDatabase.GetImplicitAssetBundleName("Assets/Data/A.txt"));
            m_Logs.Lines.Clear();
            Assert.True(AssetDatabase.RemoveAssetBundleName("missing", true));
            Assert.Equal(new[] { "Warning: AssetBundle name \"missing\" is not in the asset database." }, m_Logs.Lines);
            Assert.True(AssetDatabase.RemoveAssetBundleName("bundle-b.hd", true));
            Assert.Equal(new[] { "folder-bundle.sd" }, AssetDatabase.GetAllAssetBundleNames());
        }

        /// <summary>A name an asset brings when the listing is taken again (an import of a .meta that
        /// names one), not a name removed whose asset did not change.</summary>
        [Fact]
        public void ANameAListedAssetBringsJoinsTheDatabase()
        {
            Assert.True(AssetDatabase.RemoveAssetBundleName("bundle-b.hd", true));
            AssetDatabase.List("""
                [
                  {"guid":"10000000000000000000000000000003","path":"Assets/Data/B.txt","kind":"text","importer":"TextScriptImporter","labels":[],"bundle":"bundle-b","variant":"hd"},
                  {"guid":"10000000000000000000000000000004","path":"Assets/Data/Plain.txt","kind":"text","importer":"TextScriptImporter","labels":[],"bundle":"new-bundle","variant":""}
                ]
                """);
            Assert.Equal(new[] { "bundle-a", "folder-bundle.sd", "new-bundle" }, AssetDatabase.GetAllAssetBundleNames());
        }

        [Fact]
        public void TheDesiredWorkerCountIsTheSessionsOwnFromTheUserSetting()
        {
            // A quarter of 12 processors is 3 (the Editor's on the measured machine); the user
            // settings file's values come first.
            Assert.Equal(3, EditorUserSettings.DefaultDesiredImportWorkerCount(12, 0.25f));
            // The preference's share (rounding down, at least one: not measured beyond 12 at 25%).
            Assert.Equal(6, EditorUserSettings.DefaultDesiredImportWorkerCount(12, 0.5f));
            Assert.Equal(1, EditorUserSettings.DefaultDesiredImportWorkerCount(2, 0.25f));
            const string file = "%YAML 1.1\n--- !u!162 &1\nEditorUserSettings:\n  m_ObjectHideFlags: 0\n  m_DesiredImportWorkerCount: 8\n  m_StandbyImportWorkerCount: 2\n";
            Assert.Equal(8, EditorUserSettings.WorkerSetting(file, "m_DesiredImportWorkerCount"));
            Assert.Null(EditorUserSettings.WorkerSetting(file, "m_IdleImportWorkerShutdownDelay"));
            Assert.Null(EditorUserSettings.WorkerSetting(null, "m_DesiredImportWorkerCount"));
            int user = EditorUserSettings.desiredImportWorkerCount;
            Assert.Equal(2, EditorUserSettings.standbyImportWorkerCount);
            try
            {
                AssetDatabase.DesiredWorkerCount = 3;
                EditorUserSettings.desiredImportWorkerCount = 2;
                Assert.Equal(3, AssetDatabase.DesiredWorkerCount);
                AssetDatabase.DesiredWorkerCount = -1;
                Assert.Equal(-1, AssetDatabase.DesiredWorkerCount);
                Assert.Equal(2, EditorUserSettings.desiredImportWorkerCount);
            }
            finally
            {
                EditorUserSettings.desiredImportWorkerCount = user;
                AssetDatabase.DesiredWorkerCount = user;
            }
        }

        [Fact]
        public void AnAllowWithoutItsDisallowIsAssertedWhileTheCountIsBelowZero()
        {
            AssetDatabase.DisallowAutoRefresh();
            Assert.True(AssetDatabase.AutoRefreshDisallowed);
            AssetDatabase.AllowAutoRefresh();
            Assert.Empty(m_Logs.Lines);
            AssetDatabase.AllowAutoRefresh();
            AssetDatabase.DisallowAutoRefresh();
            AssetDatabase.AllowAutoRefresh();
            Assert.Equal(new[] { "Assert: Assertion failed on expression: 'm_DisallowAutoRefresh >= 0'", "Assert: Assertion failed on expression: 'm_DisallowAutoRefresh >= 0'" }, m_Logs.Lines);
            // Balanced again for the tests after.
            AssetDatabase.DisallowAutoRefresh();
            Assert.False(AssetDatabase.AutoRefreshDisallowed);
        }
    }
}
