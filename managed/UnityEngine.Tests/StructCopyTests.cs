using System;
using System.Linq;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    public class StructCopyHolder : MonoBehaviour
    {
        [Serializable]
        public struct Pair
        {
            public int kept;
            [NonSerialized] public int transient;
            public Inner inner;
        }

        [Serializable]
        public struct Inner
        {
            public float x;
            [NonSerialized] public float hidden;
        }

        public Pair pair = new Pair { kept = 1, transient = 9, inner = new Inner { x = 1, hidden = 4 } };
        public Pair[] pairs = { new Pair { kept = 2, transient = 8 } };
        public Rigidbody2D.SlideResults results;
    }

    /// <summary>Instantiate copies a struct's serialized members over the copy's own value
    /// (Unity 6000.0.71f1, tests/projects/declarationextras clone.pair and clone).</summary>
    public class StructCopyTests
    {
        [Fact]
        public void ACopiedStructKeepsTheCopysNonSerializedMembers()
        {
            var go = new GameObject("structs");
            GameObject copy = null;
            try
            {
                var holder = go.AddComponent<StructCopyHolder>();
                holder.pair = new StructCopyHolder.Pair { kept = 5, transient = 42, inner = new StructCopyHolder.Inner { x = 6, hidden = 43 } };
                holder.pairs = new[] { new StructCopyHolder.Pair { kept = 7, transient = 44 } };
                holder.results = new Rigidbody2D.SlideResults
                {
                    position = new Vector2(3, 4), iterationsUsed = 2,
                    slideHit = new RaycastHit2D { point = new Vector2(1, 2), distance = 5 },
                };
                copy = Object.Instantiate(go);
                var cloned = copy.GetComponent<StructCopyHolder>();
                // The copy's field initializer gave the non-serialized members 9 and 4.
                Assert.Equal(5, cloned.pair.kept);
                Assert.Equal(9, cloned.pair.transient);
                Assert.Equal(6f, cloned.pair.inner.x);
                Assert.Equal(4f, cloned.pair.inner.hidden);
                // A new array element starts from the default.
                Assert.Equal(7, cloned.pairs.Single().kept);
                Assert.Equal(0, cloned.pairs.Single().transient);
                // The hits are serialized fields of a type that is not serializable: not copied.
                Assert.Equal(new Vector2(3, 4), cloned.results.position);
                Assert.Equal(2, cloned.results.iterationsUsed);
                Assert.Equal(Vector2.zero, cloned.results.slideHit.point);
                Assert.Equal(0f, cloned.results.slideHit.distance);
                Assert.Equal(42, holder.pair.transient);
                Assert.Equal(new Vector2(1, 2), holder.results.slideHit.point);
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (copy != null) Object.DestroyImmediate(copy);
            }
        }

        [Fact]
        public void SlideResultsJsonLeavesTheHitsOut()
        {
            var results = new Rigidbody2D.SlideResults { remainingVelocity = new Vector2(5, -5), position = new Vector2(10, 1), iterationsUsed = 5, slideHit = new RaycastHit2D { distance = 3 } };
            Assert.Equal("{\"<remainingVelocity>k__BackingField\":{\"x\":5.0,\"y\":-5.0},\"<position>k__BackingField\":{\"x\":10.0,\"y\":1.0},\"<iterationsUsed>k__BackingField\":5}",
                JsonUtility.ToJson(results));
        }

        [Fact]
        public void HierarchyIsNotEquatableButComparesByOwner()
        {
            Assert.Empty(typeof(VisualElement.Hierarchy).GetInterfaces());
            var a = new VisualElement();
            var b = new VisualElement();
            var comparer = System.Collections.Generic.EqualityComparer<VisualElement.Hierarchy>.Default;
            Assert.True(comparer.Equals(a.hierarchy, a.hierarchy));
            Assert.False(comparer.Equals(a.hierarchy, b.hierarchy));
            Assert.Equal(a.GetHashCode(), comparer.GetHashCode(a.hierarchy));
            Assert.True(comparer.Equals(default, default));
        }
    }
}
