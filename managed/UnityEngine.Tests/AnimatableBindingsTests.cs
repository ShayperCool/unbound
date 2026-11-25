using System.Linq;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>GetAnimatableBindings of engine components from the measured table
    /// (tests/projects/animbindings), and other families' earlier listing.</summary>
    public class AnimatableBindingsTests
    {
        static string[] Names(GameObject go, System.Type type) =>
            AnimationUtility.GetAnimatableBindings(go, go).Where(b => b.type == type).Select(b => b.propertyName).ToArray();

        [Fact]
        public void ALightListsItsMeasuredBindingsAndNotItsType()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var go = new GameObject("light");
            try
            {
                go.AddComponent<Light>();
                var names = Names(go, typeof(Light));
                Assert.Equal(AnimationUtility.s_EngineBindings["UnityEngine.Light"].Floats, names);
                Assert.DoesNotContain("m_Type", names);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void MaterialSlotsFollowTheRendererAndAreObjectReferences()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var go = new GameObject("renderer");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new Material[3];
                var slots = AnimationUtility.GetAnimatableBindings(go, go)
                    .Where(b => b.type == typeof(MeshRenderer) && b.isPPtrCurve).Select(b => b.propertyName).ToArray();
                Assert.Equal(new[] { "m_Materials.Array.data[0]", "m_Materials.Array.data[1]", "m_Materials.Array.data[2]" }, slots);
                Assert.Contains("m_SortingOrder", Names(go, typeof(MeshRenderer)));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void OtherFamiliesKeepTheSerializedListing()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var go = new GameObject("light");
            try
            {
                go.AddComponent<Light>();
                Assert.NotEqual(AnimationUtility.s_EngineBindings["UnityEngine.Light"].Floats, Names(go, typeof(Light)));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
