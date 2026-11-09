using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A renderer list sorts the places of its draws, not the draws: the order is the
    /// one sorting the draws with the same comparison gives, ties (equal keys, equal sequence
    /// numbers) and sorting groups included, for every kind of criteria.</summary>
    public sealed class RendererListOrderTests
    {
        static List<FrameBuilder.DrawItem> Draws(int count, int seed)
        {
            var random = new System.Random(seed);
            var draws = new List<FrameBuilder.DrawItem>();
            for (int i = 0; i < count; i++)
            {
                FrameBuilder.SortLevel[] groups = null;
                if (random.Next(4) == 0)
                {
                    groups = new FrameBuilder.SortLevel[random.Next(1, 3)];
                    for (int g = 0; g < groups.Length; g++)
                        groups[g] = new FrameBuilder.SortLevel { Id = random.Next(3), SortingLayer = random.Next(2), SortingOrder = random.Next(2), Distance = random.Next(3) };
                }
                draws.Add(new FrameBuilder.DrawItem
                {
                    Mesh = (ulong)i,
                    SortingLayer = random.Next(2),
                    SortingOrder = random.Next(3),
                    Queue = 2000 + 500 * random.Next(3),
                    Distance = random.Next(4),
                    // Few distinct sequence numbers: many draws tie on every key.
                    Sequence = random.Next(5),
                    Groups = groups,
                });
            }
            return draws;
        }

        [Theory]
        [InlineData(SortingCriteria.CommonTransparent)]
        [InlineData(SortingCriteria.CommonOpaque)]
        [InlineData(SortingCriteria.SortingLayer | SortingCriteria.RenderQueue)]
        [InlineData(SortingCriteria.QuantizedFrontToBack)]
        [InlineData(SortingCriteria.BackToFront | SortingCriteria.RenderQueue)]
        [InlineData(SortingCriteria.None)]
        public void PlacesSortAsTheDrawsWould(SortingCriteria criteria)
        {
            foreach (int count in new[] { 3, 16, 17, 40, 300 })
            {
                var draws = Draws(count, count * 31 + (int)criteria);
                // A list keeps some of the collected draws: every third is left out.
                var kept = Enumerable.Range(0, count).Where(i => i % 3 != 1).ToArray();
                var expected = kept.Select(i => draws[i]).ToList();
                if (criteria != SortingCriteria.None)
                    expected.Sort((a, b) => FrameBuilder.CompareForList(a, b, criteria));
                var order = (int[])kept.Clone();
                FrameBuilder.SortRendererList(draws, order, criteria);
                Assert.Equal(expected.Select(d => d.Mesh), order.Select(i => draws[i].Mesh));
            }
        }
    }
}
