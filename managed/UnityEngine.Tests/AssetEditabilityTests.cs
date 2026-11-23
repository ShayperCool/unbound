using System;
using System.Collections.Generic;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// MakeEditable, CanOpenForEdit and the AssetModificationProcessor editability callbacks
    /// without version control, as the Unity 6000.0.71f1 Editor answers them
    /// (tests/projects/openforedit, openforedithooks, openforeditorder).
    /// </summary>
    public sealed class AssetEditabilityTests : IDisposable
    {
        static readonly List<string> s_Calls = new List<string>();

        public AssetEditabilityTests() => s_Calls.Clear();

        public void Dispose()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = null;
            s_Calls.Clear();
        }

        static bool Refuse(string name, string marker, string[] paths, List<string> list)
        {
            s_Calls.Add(name + "[" + string.Join(",", paths) + "]");
            bool all = true;
            foreach (var path in paths)
            {
                if (path == null || !path.Contains(marker)) continue;
                all = false;
                list?.Add(path);
            }
            return all;
        }

        /// <summary>The single-path IsOpenForEdit (refuses "Held", and "Mute" without a message),
        /// CanOpenForEdit (refuses "Barred") and MakeEditable (refuses "NoMake").</summary>
        public sealed class PathProcessor : AssetModificationProcessor
        {
            static bool IsOpenForEdit(string assetPath, out string message)
            {
                s_Calls.Add("path.IsOpenForEdit(" + assetPath + ")");
                if (assetPath.Contains("Mute"))
                {
                    message = null;
                    return false;
                }
                message = assetPath.Contains("Held") ? "held" : "";
                return message.Length == 0;
            }

            static bool CanOpenForEdit(string assetPath, out string message)
            {
                s_Calls.Add("path.CanOpenForEdit(" + assetPath + ")");
                message = assetPath.Contains("Barred") ? "barred" : "";
                return message.Length == 0;
            }

            static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths)
            {
                s_Calls.Add("prompt=" + (prompt ?? "<null>"));
                return Refuse("path.MakeEditable", "NoMake", paths, outNotEditablePaths);
            }
        }

        /// <summary>The list IsOpenForEdit (refuses "Locked") and CanOpenForEdit (refuses "NoCan").</summary>
        public sealed class ListProcessor : AssetModificationProcessor
        {
            static bool IsOpenForEdit(string[] paths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions) =>
                Refuse("list.IsOpenForEdit", "Locked", paths, outNotEditablePaths);

            static bool CanOpenForEdit(string[] paths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions) =>
                Refuse("list.CanOpenForEdit", "NoCan", paths, outNotEditablePaths);
        }

        /// <summary>A second processor with the same callbacks as others, refusing "Second".</summary>
        public sealed class SecondProcessor : AssetModificationProcessor
        {
            static bool IsOpenForEdit(string[] paths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions) =>
                Refuse("second.IsOpenForEdit", "Second", paths, outNotEditablePaths);

            static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths) =>
                Refuse("second.MakeEditable", "Second", paths, outNotEditablePaths);
        }

        /// <summary>A callback of other parameters is not one of these callbacks.</summary>
        public sealed class OtherShapeProcessor : AssetModificationProcessor
        {
            static bool MakeEditable(string[] paths) => throw new InvalidOperationException("not a callback");
            static bool IsOpenForEdit(string assetPath) => throw new InvalidOperationException("not a callback");
        }

        [Fact]
        public void WithoutCallbacksEveryPathIsEditableAndTheListIsCleared()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(OtherShapeProcessor) };
            Assert.True(AssetDatabase.MakeEditable("Assets/Probe.txt"));
            Assert.True(AssetDatabase.MakeEditable(""));
            var list = new List<string> { "stale" };
            Assert.True(AssetDatabase.MakeEditable(new[] { "Assets/Probe.txt", null, "Assets/Missing.asset" }, "prompt", list));
            Assert.Empty(list);
            Assert.True(AssetDatabase.MakeEditable(new string[0]));
            Assert.Equal("path", Assert.Throws<ArgumentNullException>(() => AssetDatabase.MakeEditable((string)null)).ParamName);
            Assert.Equal("paths", Assert.Throws<ArgumentNullException>(() => AssetDatabase.MakeEditable((string[])null)).ParamName);
            Assert.True(AssetDatabase.CanOpenForEdit("Assets/Probe.txt", out var message));
            Assert.Equal("", message);
            Assert.True(AssetDatabase.CanOpenForEdit((Object)null));
            Assert.True(AssetDatabase.IsMetaFileOpenForEdit(null, out message));
            Assert.Equal("", message);
            Assert.Equal("assetOrMetaFilePaths", Assert.Throws<ArgumentNullException>(() => AssetDatabase.CanOpenForEdit(null, list)).ParamName);
            Assert.Equal("outNotEditablePaths", Assert.Throws<ArgumentNullException>(() => AssetDatabase.CanOpenForEdit(new string[0], null)).ParamName);
        }

        [Fact]
        public void ASinglePathAsksTheListCallbacksThenTheSinglePathOnesWhoseMessageItReturns()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(PathProcessor), typeof(ListProcessor) };
            Assert.True(AssetDatabase.IsOpenForEdit("Assets/A.txt", out var message));
            Assert.Equal(new[] { "list.IsOpenForEdit[Assets/A.txt]", "path.IsOpenForEdit(Assets/A.txt)" }, s_Calls);

            s_Calls.Clear();
            Assert.False(AssetDatabase.IsOpenForEdit("Assets/Locked.txt", out message));
            Assert.Equal("", message);
            Assert.Equal(new[] { "list.IsOpenForEdit[Assets/Locked.txt]" }, s_Calls);

            s_Calls.Clear();
            Assert.False(AssetDatabase.IsOpenForEdit("Assets/Held.txt", out message));
            Assert.Equal("held", message);

            // Empty paths reach no callback.
            s_Calls.Clear();
            Assert.True(AssetDatabase.IsOpenForEdit("", out _));
            Assert.Empty(s_Calls);
        }

        [Fact]
        public void CanOpenForEditAsksItsCallbacksOnlyForPathsNotOpen()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(PathProcessor), typeof(ListProcessor) };
            Assert.True(AssetDatabase.CanOpenForEdit("Assets/NoCan.txt", out var message));
            Assert.DoesNotContain(s_Calls, c => c.StartsWith("list.CanOpenForEdit"));

            Assert.True(AssetDatabase.CanOpenForEdit("Assets/Held.txt", out message));
            Assert.Equal("", message);
            Assert.False(AssetDatabase.CanOpenForEdit("Assets/HeldNoCan.txt", out message));
            Assert.Equal("", message);
            Assert.False(AssetDatabase.CanOpenForEdit("Assets/LockedNoCan.txt"));

            // A list: the paths not open (by the list callbacks alone) go to CanOpenForEdit.
            s_Calls.Clear();
            var list = new List<string> { "stale" };
            AssetDatabase.CanOpenForEdit(new[] { "Assets/A.txt", "Assets/Held.txt", "Assets/LockedNoCan.txt", "", "Assets/Locked.txt" }, list);
            Assert.Equal(new[] { "Assets/LockedNoCan.txt" }, list);
            Assert.Equal(new[]
            {
                "list.IsOpenForEdit[Assets/A.txt,Assets/Held.txt,Assets/LockedNoCan.txt,Assets/Locked.txt]",
                "list.CanOpenForEdit[Assets/LockedNoCan.txt,Assets/Locked.txt]",
            }, s_Calls);
        }

        [Fact]
        public void WhenTheListCallbacksAcceptTheSinglePathOnesSeeEveryPathAndTheirMessageIsKept()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(PathProcessor), typeof(ListProcessor) };
            var list = new List<string> { "stale" };
            AssetDatabase.IsOpenForEdit(new[] { "Assets/A.txt", "Assets/Held.txt", "Assets/Mute.txt" }, list);
            Assert.Equal(new[] { "Assets/Held.txt", "Assets/Mute.txt" }, list);
            Assert.Equal(new[]
            {
                "list.IsOpenForEdit[Assets/A.txt,Assets/Held.txt,Assets/Mute.txt]",
                "path.IsOpenForEdit(Assets/A.txt)", "path.IsOpenForEdit(Assets/Held.txt)", "path.IsOpenForEdit(Assets/Mute.txt)",
            }, s_Calls);

            Assert.False(AssetDatabase.IsOpenForEdit("Assets/Mute.txt", out var message));
            Assert.Null(message);
            // Not open, the CanOpenForEdit callbacks answer, with the single-path one's message.
            Assert.False(AssetDatabase.CanOpenForEdit("Assets/HeldBarred.txt", out message));
            Assert.Equal("barred", message);
            list = new List<string>();
            AssetDatabase.CanOpenForEdit(new[] { "Assets/Held.txt", "Assets/HeldBarred.txt", "Assets/A.txt" }, list);
            Assert.Equal(new[] { "Assets/HeldBarred.txt" }, list);
        }

        [Fact]
        public void PathsOutsideTheVersionedFoldersReachNoQueryCallbackButMakeEditableHasThemAll()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(PathProcessor), typeof(ListProcessor) };
            foreach (var path in new[] { "Library/Held.txt", "UserSettings/Held.asset", "/tmp/Held.txt", "Assets\\Held.txt", "AssetsHeld.txt" })
            {
                Assert.True(AssetDatabase.IsOpenForEdit(path, out _));
                Assert.True(AssetDatabase.CanOpenForEdit(path));
            }
            Assert.Empty(s_Calls);
            Assert.False(AssetDatabase.IsOpenForEdit("ProjectSettings/Held.asset"));
            Assert.True(AssetDatabase.IsOpenForEdit("Packages"));
            Assert.Contains("path.IsOpenForEdit(Packages)", s_Calls);
            var list = new List<string> { "stale" };
            AssetDatabase.IsOpenForEdit(new[] { "Library/Held.txt", "Assets/Held.txt" }, list);
            Assert.Equal(new[] { "Assets/Held.txt" }, list);
            Assert.False(AssetDatabase.MakeEditable("Library/NoMake.txt"));
        }

        [Fact]
        public void MakeEditableHandsThePathsPromptAndListToTheCallbacksUntilOneRefuses()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(SecondProcessor), typeof(PathProcessor) };
            var list = new List<string> { "stale" };
            Assert.False(AssetDatabase.MakeEditable(new[] { "Assets/Second.txt", "Assets/NoMake.txt" }, null, list));
            Assert.Equal(new[] { "stale", "Assets/Second.txt" }, list);
            Assert.Equal(new[] { "second.MakeEditable[Assets/Second.txt,Assets/NoMake.txt]" }, s_Calls);

            s_Calls.Clear();
            list = new List<string> { "stale" };
            Assert.False(AssetDatabase.MakeEditable(new[] { "Assets/NoMake.txt" }, null, list));
            Assert.Equal(new[] { "stale", "Assets/NoMake.txt" }, list);
            Assert.Equal(new[] { "second.MakeEditable[Assets/NoMake.txt]", "prompt=", "path.MakeEditable[Assets/NoMake.txt]" }, s_Calls);

            // All accept: the list is cleared. One empty path is passed as none.
            s_Calls.Clear();
            list = new List<string> { "stale" };
            Assert.True(AssetDatabase.MakeEditable(new[] { "Assets/A.txt" }, "asked", list));
            Assert.Empty(list);
            Assert.Contains("prompt=asked", s_Calls);
            s_Calls.Clear();
            Assert.True(AssetDatabase.MakeEditable(""));
            Assert.Equal(new[] { "second.MakeEditable[]", "prompt=", "path.MakeEditable[]" }, s_Calls);
        }

        [Fact]
        public void TheListQueryStopsAtTheFirstProcessorThatRefuses()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(SecondProcessor), typeof(ListProcessor) };
            var list = new List<string> { "stale" };
            AssetDatabase.IsOpenForEdit(new[] { "Assets/Locked.txt", "Assets/Second.txt" }, list);
            Assert.Equal(new[] { "Assets/Second.txt" }, list);
            Assert.Equal(new[] { "second.IsOpenForEdit[Assets/Locked.txt,Assets/Second.txt]" }, s_Calls);
        }
    }
}
