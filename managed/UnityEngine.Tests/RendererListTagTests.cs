using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A renderer list's per-tag state blocks (RendererListParams.tagValues), as the
    /// DrawRenderers documentation gives them: the first value that is the draw's tag value wins,
    /// the default (empty) value matching any draw; a draw no value matches has no override.</summary>
    public class RendererListTagTests
    {
        [Fact]
        public void TheFirstMatchingValueWins()
        {
            var values = new[] { "Lit", "SimpleLit", "Lit" };
            Assert.Equal(0, RendererLists.TaggedStates.Match("Lit", values));
            Assert.Equal(1, RendererLists.TaggedStates.Match("SimpleLit", values));
        }

        [Fact]
        public void TheDefaultValueMatchesEveryDrawWhereItStands()
        {
            // Last (URP's catch-all): only draws no earlier value matches.
            var last = new[] { "Lit", "SimpleLit", "" };
            Assert.Equal(0, RendererLists.TaggedStates.Match("Lit", last));
            Assert.Equal(2, RendererLists.TaggedStates.Match("Unlit", last));
            Assert.Equal(2, RendererLists.TaggedStates.Match("", last));
            // First: every draw, its own value's block included.
            var first = new[] { "", "Lit" };
            Assert.Equal(0, RendererLists.TaggedStates.Match("Lit", first));
        }

        [Fact]
        public void ADrawNoValueMatchesHasNoOverride()
        {
            Assert.Equal(-1, RendererLists.TaggedStates.Match("Unlit", new[] { "Lit", "SimpleLit" }));
            Assert.Equal(-1, RendererLists.TaggedStates.Match("", new[] { "Lit" }));
        }
    }
}
