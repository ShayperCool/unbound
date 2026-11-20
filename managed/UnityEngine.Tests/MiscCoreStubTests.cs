using System.Collections.Generic;
using UnityEngine.AI;
using UnityEngine.Assertions.Comparers;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Small core and Editor members as Unity 6000.0.71f1 declares them: ColorUsageAttribute's
    /// obsolete HDR picker limits, the assertion float comparer, the internal generic stack, and the
    /// obsolete Editor NavMeshBuilder stage collection that forwards to NavMeshEditorHelpers.
    /// </summary>
    public sealed class MiscCoreStubTests
    {
#pragma warning disable CS0618 // The obsolete members are what is tested.
        [Fact]
        public void ColorUsageKeepsItsObsoletePickerLimits()
        {
            var plain = new ColorUsageAttribute(false, true);
            Assert.False(plain.showAlpha);
            Assert.True(plain.hdr);
            Assert.Equal(new[] { 0f, 8f, 0.125f, 3f },
                new[] { plain.minBrightness, plain.maxBrightness, plain.minExposureValue, plain.maxExposureValue });
            var limited = new ColorUsageAttribute(true, true, 1f, 2f, 0.5f, 4f);
            Assert.True(limited.showAlpha);
            Assert.Equal(new[] { 1f, 2f, 0.5f, 4f },
                new[] { limited.minBrightness, limited.maxBrightness, limited.minExposureValue, limited.maxExposureValue });
        }

        [Fact]
        public void FloatComparerComparesWithinItsError()
        {
            Assert.Equal(1E-05f, FloatComparer.kEpsilon);
            Assert.True(FloatComparer.s_ComparerWithDefaultTolerance.Equals(1f, 1.000005f));
            Assert.False(FloatComparer.s_ComparerWithDefaultTolerance.Equals(1f, 1.0001f));
            // Absolute: the difference; relative: the difference over the larger magnitude.
            Assert.True(FloatComparer.AreEqual(100f, 100.5f, 0.5f));
            Assert.False(FloatComparer.AreEqual(100f, 100.6f, 0.5f));
            Assert.True(FloatComparer.AreEqualRelative(100f, 101f, 0.01f));
            Assert.False(FloatComparer.AreEqualRelative(100f, 102f, 0.01f));
            Assert.True(new FloatComparer(0.01f, true).Equals(1000f, 1005f));
            Assert.False(new FloatComparer(0.01f).Equals(1000f, 1005f));
            // Control: equal values compare equal even where the relative difference is undefined.
            Assert.True(FloatComparer.AreEqualRelative(0f, 0f, 0f));
            Assert.True(FloatComparer.AreEqualRelative(float.PositiveInfinity, float.PositiveInfinity, 0f));
            Assert.False(FloatComparer.AreEqualRelative(float.NaN, float.NaN, 1f));
            // As an equality comparer: the hash is the comparer's, so equal floats share a bucket.
            var set = new HashSet<float>(new FloatComparer(0.1f)) { 1f };
            Assert.Contains(1.05f, set);
            Assert.Equal(set.Comparer.GetHashCode(1f), set.Comparer.GetHashCode(5f));
        }

        [Fact]
        public void GenericStackIsAStack()
        {
            var stack = new UnityEngineInternal.GenericStack();
            stack.Push(1);
            stack.Push("two");
            Assert.Equal("two", stack.Pop());
            Assert.Equal(1, stack.Peek());
            Assert.IsAssignableFrom<System.Collections.Stack>(stack);
        }

        [Fact]
        public void TheObsoleteStageCollectionCollectsWhatTheHelpersCollect()
        {
            var go = new GameObject("NavSource");
            try
            {
                go.AddComponent<BoxCollider>();
                var markups = new List<NavMeshBuildMarkup>();
                var expected = new List<NavMeshBuildSource>();
                var actual = new List<NavMeshBuildSource>();
                UnityEditor.AI.NavMeshEditorHelpers.CollectSourcesInStage(go.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, go.scene, expected);
                UnityEditor.AI.NavMeshBuilder.CollectSourcesInStage(go.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, go.scene, actual);
                Assert.NotEmpty(expected);
                Assert.Equal(expected.Count, actual.Count);
                Assert.Equal(expected[0].shape, actual[0].shape);
                Assert.Same(expected[0].component, actual[0].component);
                var bounds = new Bounds(Vector3.zero, Vector3.one * 10f);
                var inBounds = new List<NavMeshBuildSource>();
                UnityEditor.AI.NavMeshBuilder.CollectSourcesInStage(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, true, markups, false, go.scene, inBounds);
                Assert.Equal(expected.Count, inBounds.Count);
                // Control: a layer mask without the object's layer collects nothing.
                var none = new List<NavMeshBuildSource>();
                UnityEditor.AI.NavMeshBuilder.CollectSourcesInStage(go.transform, 0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, go.scene, none);
                Assert.Empty(none);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
#pragma warning restore CS0618
    }
}
