using System.Collections.Generic;
using UnityEditor;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>How a session's writes are reported to OnPostprocessAllAssets
    /// (tests/projects/scenesave session-imports): one call per write, nothing for a write that
    /// changed nothing, one call for what asset editing held back, a copied folder before what it
    /// holds.</summary>
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

        [Fact]
        public void WhatACallbackWritesIsItsOwnCallAfterTheRoundAndScriptsAreNotReported()
        {
            var calls = new List<string>();
            bool wrote = false;
            using (SessionImports.ReportsForTests((imported, deleted, moved, from) =>
            {
                calls.Add("start " + string.Join(",", imported));
                // A callback's own write: reported after this round, not inside it.
                if (!wrote)
                {
                    wrote = true;
                    SessionImports.Imported("Assets/Written.asset");
                }
                calls.Add("end " + string.Join(",", imported));
            }))
            Unbound.Runtime.EditorSession.AsEditMode(() =>
            {
                SessionImports.Imported("Assets/First.asset");
                Assert.Equal(new[] { "start Assets/First.asset", "end Assets/First.asset", "start Assets/Written.asset", "end Assets/Written.asset" }, calls);
                calls.Clear();
                SessionImports.Imported("Assets/Code.cs");
                SessionImports.Imported("Assets/Code.asmdef");
                Assert.Empty(calls);
            });
        }

        static readonly List<string> s_Round = new List<string>();

        sealed class Writing : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
            {
                s_Round.Add("callback " + didDomainReload);
                // A callback's write in a domain load's round: reported once the round has ended.
                if (didDomainReload) SessionImports.Imported("Assets/FromLoad.asset");
                s_Round.Add("callback end");
            }
        }

        [Fact]
        public void WhatADomainLoadsCallbacksWriteIsReportedAfterItsRoundAndScriptMovesAreNot()
        {
            s_Round.Clear();
            using (SessionImports.ReportsForTests((imported, deleted, moved, from) =>
                s_Round.Add("reported " + string.Join(",", imported) + string.Join(",", moved))))
            Unbound.Runtime.EditorSession.AsEditMode(() =>
            {
                var callbacks = new List<System.Reflection.MethodInfo>
                {
                    typeof(Writing).GetMethod("OnPostprocessAllAssets", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic),
                };
                AssetPostprocessing.Dispatch(callbacks, new ProjectImports.Changes(), null);
                Assert.Equal(new[] { "callback True", "callback end", "reported Assets/FromLoad.asset" }, s_Round);
                s_Round.Clear();
                SessionImports.Moved("Assets/A.cs", "Assets/B/A.cs");
                SessionImports.Deleted("Assets/C.asmdef");
                Assert.Empty(s_Round);
            });
        }
    }
}
