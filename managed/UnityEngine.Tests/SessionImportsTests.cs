using System.Collections.Generic;
using UnityEditor;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>How a session's writes are reported to OnPostprocessAllAssets
    /// (tests/projects/scenesave session-imports): one call per write, nothing for a write that
    /// changed nothing, one call for what asset editing held back, a copied folder before what it
    /// holds. (Edit mode: Play mode's writes are not measured.)</summary>
    [Collection("EditorState")]
    public class SessionImportsTests
    {
        static List<string> Calls(out System.IDisposable restore)
        {
            var calls = new List<string>();
            restore = SessionImports.ReportsForTests((imported, deleted, moved, from) =>
                calls.Add($"{string.Join(",", imported)} | {string.Join(",", deleted)} | {string.Join(",", moved)} <- {string.Join(",", from)}"));
            return calls;
        }

        [Fact]
        public void AWriteIsOneCallAndOutsideOneEachIsItsOwn()
        {
            var calls = Calls(out var restore);
            using (restore)
            Unbound.Runtime.EditorSession.AsEditMode(() =>
            {
                using (SessionImports.Write())
                {
                    SessionImports.Imported("Assets/A.asset");
                    SessionImports.Imported("Assets/B.asset");
                    SessionImports.Imported("Assets/A.asset");
                    Assert.Empty(calls);
                }
                Assert.Equal(new[] { "Assets/A.asset,Assets/B.asset |  |  <- " }, calls);
                calls.Clear();
                SessionImports.Imported("Assets/C.asset");
                SessionImports.Deleted("Assets/D.asset");
                Assert.Equal(new[] { "Assets/C.asset |  |  <- ", " | Assets/D.asset |  <- " }, calls);
                calls.Clear();
                // A write that changed nothing (a clean SaveAssets) is no call.
                using (SessionImports.Write()) { }
                Assert.Empty(calls);
            });
        }

        [Fact]
        public void AssetEditingHoldsTheWritesBackToOneCallAtItsEnd()
        {
            var calls = Calls(out var restore);
            using (restore)
            Unbound.Runtime.EditorSession.AsEditMode(() =>
            {
                AssetDatabase.StartAssetEditing();
                AssetDatabase.StartAssetEditing();
                using (SessionImports.Write()) SessionImports.Imported("Assets/First.asset");
                SessionImports.Imported("Assets/Second.asset");
                AssetDatabase.StopAssetEditing();
                Assert.Empty(calls);
                AssetDatabase.StopAssetEditing();
                Assert.Equal(new[] { "Assets/First.asset,Assets/Second.asset |  |  <- " }, calls);
            });
        }

        [Fact]
        public void ACopiedFolderIsReportedBeforeWhatItHoldsAndAMoveReportsItself()
        {
            var calls = Calls(out var restore);
            using (restore)
            Unbound.Runtime.EditorSession.AsEditMode(() =>
            {
                using (SessionImports.Write())
                {
                    SessionImports.Imported("Assets/Copy/Held.asset");
                    SessionImports.Imported("Assets/Copy");
                    SessionImports.ReportFirst("Assets/Copy");
                }
                Assert.Equal(new[] { "Assets/Copy |  |  <- ", "Assets/Copy/Held.asset |  |  <- " }, calls);
                calls.Clear();
                // Not a folder and its contents: one call.
                using (SessionImports.Write())
                {
                    SessionImports.Imported("Assets/Other.asset");
                    SessionImports.Imported("Assets/Copy2");
                    SessionImports.ReportFirst("Assets/Copy2");
                }
                Assert.Single(calls);
                calls.Clear();
                using (SessionImports.Write())
                {
                    using (SessionImports.Muted()) SessionImports.Imported("Assets/Moved/Held.asset");
                    SessionImports.Imported("Assets/Moved");
                    SessionImports.Moved("Assets/Folder", "Assets/Moved");
                    SessionImports.Moved("Assets/Folder/Held.asset", "Assets/Moved/Held.asset");
                }
                Assert.Equal(new[] { "Assets/Moved |  | Assets/Moved,Assets/Moved/Held.asset <- Assets/Folder,Assets/Folder/Held.asset" }, calls);
            });
        }
    }
}
