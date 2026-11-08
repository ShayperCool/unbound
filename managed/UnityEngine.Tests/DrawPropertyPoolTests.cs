using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The dictionaries draws merge their properties into: copies in the same order,
    /// kept until the outermost scope ends (a nested draw does not end a list's), reused after;
    /// outside any scope each copy is a new dictionary. Boxed vectors are given again only while
    /// the value is the same.</summary>
    public sealed class DrawPropertyPoolTests
    {
        [Fact]
        public void CopiesLiveUntilTheOutermostScopeEnds()
        {
            var source = new Dictionary<int, object> { [3] = 1f, [1] = 2f, [2] = Vector4.one };
            Dictionary<int, object> outer, inner;
            using (DrawPropertyPool.Begin())
            {
                outer = DrawPropertyPool.Copy(source);
                Assert.Equal(source.ToList(), outer.ToList());
                outer[4] = 5f;
                Assert.False(source.ContainsKey(4));
                using (DrawPropertyPool.Begin())
                {
                    inner = DrawPropertyPool.Copy(null);
                    Assert.Empty(inner);
                    Assert.NotSame(outer, inner);
                }
                // The nested scope's end leaves the outer copies alone.
                Assert.Equal(4, outer.Count);
                Assert.NotSame(outer, DrawPropertyPool.Copy(outer));
            }
            Assert.Empty(outer);
            using (DrawPropertyPool.Begin())
                Assert.Same(outer, DrawPropertyPool.Copy(null));
            var unscoped = DrawPropertyPool.Copy(source);
            Assert.NotSame(outer, unscoped);
            Assert.NotSame(unscoped, DrawPropertyPool.Copy(source));
        }

        [Fact]
        public void ABoxIsGivenAgainOnlyForTheSameValue()
        {
            var boxes = new BoxedVectors(2);
            var a = boxes.Box(0, new Vector4(1f, 2f, 3f, 4f));
            Assert.Same(a, boxes.Box(0, new Vector4(1f, 2f, 3f, 4f)));
            Assert.NotSame(a, boxes.Box(1, new Vector4(1f, 2f, 3f, 4f)));
            var b = boxes.Box(0, new Vector4(1f, 2f, 3f, 5f));
            Assert.NotSame(a, b);
            Assert.Equal(new Vector4(1f, 2f, 3f, 4f), (Vector4)a);
            Assert.Equal(new Vector4(1f, 2f, 3f, 5f), (Vector4)b);
            // -0 and 0 differ in their bits: a box of each.
            Assert.NotSame(boxes.Box(1, new Vector4(0f, 0f, 0f, 0f)), boxes.Box(1, new Vector4(-0f, 0f, 0f, 0f)));
        }
    }
}
