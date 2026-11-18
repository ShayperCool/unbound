using System;
using System.Reflection;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>BuildPlayerWindow's build handlers as the Unity 6000.0.71f1 Editor keeps them
    /// (tests/projects/buildplayerhandlers): a handler replacing another warns, null resets silently.</summary>
    public sealed class BuildPlayerHandlersTests
    {
        static object Field(string name) => typeof(BuildPlayerWindow).GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        [Fact]
        public void AReplacedHandlerWarnsAndNullResetsSilently()
        {
            using var recorded = new RecordedLogs();
            var logs = recorded.Logged;
            Action<BuildPlayerOptions> first = o => { }, second = o => { };
            Func<BuildPlayerOptions, BuildPlayerOptions> options = o => o, other = o => o;
            try
            {
                BuildPlayerWindow.RegisterBuildPlayerHandler(null);
                BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(null);
                BuildPlayerWindow.RegisterBuildPlayerHandler(first);
                Assert.Empty(logs);
                Assert.Same(first, Field("buildPlayerHandler"));
                BuildPlayerWindow.RegisterBuildPlayerHandler(second);
                Assert.Equal(new[] { "Warning: The build player handler in BuildPlayerWindow is being reassigned!" }, logs);
                Assert.Same(second, Field("buildPlayerHandler"));
                logs.Clear();
                BuildPlayerWindow.RegisterBuildPlayerHandler(null);
                BuildPlayerWindow.RegisterBuildPlayerHandler(first);
                Assert.Empty(logs);
                BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(options);
                BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(other);
                Assert.Equal(new[] { "Warning: The get build player options handler in BuildPlayerWindow is being reassigned!" }, logs);
                Assert.Same(other, Field("getBuildPlayerOptionsHandler"));
                // Control: the two handlers are separate.
                Assert.Same(first, Field("buildPlayerHandler"));
            }
            finally
            {
                BuildPlayerWindow.RegisterBuildPlayerHandler(null);
                BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(null);
            }
        }
    }
}
