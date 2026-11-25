using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A material's disabled passes as the Unity 6000.0.71f1 Editor keeps and writes them
    /// (tests/projects/urpwrites exit.Assets/Resources/M/*.mat).</summary>
    public class DisabledPassListTests
    {
        static string[] Names(DisabledPassList list)
        {
            var names = new string[list.Count];
            for (int i = 0; i < names.Length; i++) names[i] = list[i];
            return names;
        }

        /// <summary>PassNamesEdited: sixteen passes disabled in order, Universal2D enabled under
        /// another spelling, then DepthOnly and ShadowCaster enabled by the material's validation.</summary>
        [Fact]
        public void PassesAreAddedAtTheEndAndAnEnabledOneLeavesItsPlaceToTheLast()
        {
            var list = new DisabledPassList();
            foreach (var name in new[]
            {
                "UniversalForward", "Meta", "ShadowCaster", "Always", "DepthOnly", "ForwardBase", "Universal2D", "ForwardAdd",
                "Deferred", "Vertex", "MotionVectors", "SRPDefaultUnlit", "DepthNormals", "GrabPass", "lowercase", "MiXeD",
            })
                Assert.True(list.Add(name));
            Assert.True(list.Remove("universal2d"));
            Assert.True(list.Remove("DepthOnly"));
            Assert.True(list.Remove("ShadowCaster"));
            Assert.Equal(new[]
            {
                "UniversalForward", "META", "GRABPASS", "ALWAYS", "lowercase", "FORWARDBASE", "MiXeD", "FORWARDADD", "DEFERRED",
                "Vertex", "MOTIONVECTORS", "SRPDEFAULTUNLIT", "DepthNormals",
            }, Names(list));
        }

        /// <summary>LitStaleEdited: saved entries keep their order and spelling, and a pass
        /// disabled again under another spelling changes nothing; LitPassesAddedEdited: a
        /// transparent Lit material's validation from none.</summary>
        [Fact]
        public void SavedEntriesStayAsReadAndMatchInAnyCase()
        {
            var list = new DisabledPassList();
            foreach (var name in new[] { "SHADOWCASTER", "DepthOnly", "MOTIONVECTORS" }) list.AddAsRead(name);
            Assert.False(list.Add("ShadowCaster"));
            Assert.False(list.Add("DEPTHONLY"));
            Assert.True(list.Contains("motionvectors"));
            Assert.Equal(new[] { "SHADOWCASTER", "DepthOnly", "MOTIONVECTORS" }, Names(list));
            Assert.False(list.Remove("Universal2D"));

            // The engine's own names read in upper case (toolapis matx.partial: a saved ShadowCaster).
            var saved = new DisabledPassList();
            saved.AddAsRead("ShadowCaster");
            saved.AddAsRead("DepthOnly");
            Assert.Equal(new[] { "SHADOWCASTER", "DepthOnly" }, Names(saved));

            var added = new DisabledPassList();
            foreach (var name in new[] { "DepthOnly", "ShadowCaster", "MotionVectors" }) added.Add(name);
            Assert.Equal(new[] { "DepthOnly", "SHADOWCASTER", "MOTIONVECTORS" }, Names(added));
        }
    }
}
