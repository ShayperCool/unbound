using System;
using UnityEditorInternal;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class ReorderableListTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void InvalidHeightCallbackCanBeReplaced(bool recursive)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var list = new ReorderableList(new[] { 1 }, typeof(int));
            int height = 0;
            list.elementHeightCallback = _ => recursive ? list.GetHeight() : ++height;

            Assert.Throws<InvalidOperationException>(() => list.GetHeight());

            list.elementHeightCallback = _ => 11f;
            Assert.Equal(61f, list.GetHeight());
        }
    }
}
