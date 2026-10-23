using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class SortingGroupTests
    {
        static FrameBuilder.DrawItem Draw(string name, int order, float distance, params FrameBuilder.SortLevel[] groups) => new FrameBuilder.DrawItem
        {
            Queue = 3000,
            SortingOrder = order,
            Distance = distance,
            Sequence = name[0],
            Groups = groups.Length == 0 ? null : groups,
            Color = new Color(name[0], 0, 0, 0),
        };

        static FrameBuilder.SortLevel Group(int id, int order, float distance) =>
            new FrameBuilder.SortLevel { Id = id, SortingOrder = order, Distance = distance };

        static string Order(List<FrameBuilder.DrawItem> items)
        {
            FrameBuilder.SortForCamera(items);
            var s = "";
            foreach (var item in items) s += (char)item.Color.r;
            return s;
        }

        [Fact]
        public void AGroupSortsAsOneUnitByItsOwnOrder()
        {
            // Group 1 (order 5) holds a renderer with order -100: it still draws after the free
            // renderer with order 0, because the group's order is what counts outside it.
            var items = new List<FrameBuilder.DrawItem>
            {
                Draw("g", -100, 1f, Group(1, 5, 1f)),
                Draw("f", 0, 1f),
            };
            Assert.Equal("fg", Order(items));
        }

        [Fact]
        public void InsideAGroupRenderersSortAmongThemselves()
        {
            var items = new List<FrameBuilder.DrawItem>
            {
                Draw("b", 2, 1f, Group(1, 0, 5f)),
                Draw("a", 1, 1f, Group(1, 0, 5f)),
                Draw("x", 1, 1f, Group(2, 0, 9f)),
            };
            // Group 2 is farther: drawn first. Inside group 1, order 1 before order 2.
            Assert.Equal("xab", Order(items));
        }
    }
}
