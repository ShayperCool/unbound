using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>ProjectWindowUtil without a Project window, as the Unity 6000.0.71f1 Editor runs it
    /// in Play mode (tests/projects/projectwindowutil).</summary>
    public sealed class ProjectWindowUtilTests
    {
        sealed class Recording : EndNameEditAction
        {
            internal readonly List<string> Calls = new List<string>();
            public override void Action(int instanceId, string pathName, string resourceFile) => Calls.Add(instanceId + ":" + pathName + ":" + resourceFile);
        }

        [Fact]
        public void AnEndActionIsHiddenAndItsCleanUpDestroysIt()
        {
            // The path a name edit ends on comes from the project's asset database (the probe
            // tests/projects/projectwindowutil checks it); here the action's own part.
            var action = ScriptableObject.CreateInstance<Recording>();
            Assert.Equal(HideFlags.HideAndDontSave, action.hideFlags);
            action.Cancelled(1, "Assets/X.asset", null);
            Assert.Empty(action.Calls);
            action.CleanUp();
            Assert.True(action == null);
            Assert.Equal("Assets", ProjectWindowUtilActiveFolder());
        }

        static string ProjectWindowUtilActiveFolder()
        {
            Assert.False(ProjectWindowUtil.TryGetActiveFolderPath(out var path));
            Assert.Equal("", path);
            return ProjectWindowUtil.GetActiveFolderPath();
        }

        [Fact]
        public void PathHelpersTrimAndKeepTheOutermostFolders()
        {
            Assert.Equal("Assets/A", ProjectWindowUtil.GetContainingFolder("/Assets/A/b/"));
            Assert.Null(ProjectWindowUtil.GetContainingFolder("Assets"));
            Assert.Null(ProjectWindowUtil.GetContainingFolder(null));
            Assert.Equal("a/", ProjectWindowUtil.GetContainingFolder("a//b"));
            Assert.Equal(new[] { "Assets/A", "Assets/AB", "Assets/B" }, ProjectWindowUtil.GetBaseFolders(new[] { "Assets/B", "Assets/A", "Assets/A/Sub", "Assets/AB" }));
            Assert.Equal(new[] { "Only/" }, ProjectWindowUtil.GetBaseFolders(new[] { "Only/" }));
        }
    }
}
