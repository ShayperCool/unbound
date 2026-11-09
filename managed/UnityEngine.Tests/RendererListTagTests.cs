using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
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

        /// <summary>The draw's tag value from a real material: its SubShader's tag (an override
        /// tag first), or with a pass tag the drawn pass's.</summary>
        [Fact]
        public void DrawsMatchTheirMaterialsSubShaderOrPassTag()
        {
            var shader = ShaderUtil.CreateShaderAsset(
                "Shader \"Tests/RendererListTags\" { SubShader { Tags { \"Kind\" = \"B\" }"
                + " Pass { Tags { \"Flavor\" = \"Sweet\" } } Pass { Tags { \"Flavor\" = \"Sour\" } } } }");
            // As the renderer describes its drawn SubShader (seeded below).
            var material = new Material(shader);
            // The renderer's description of the drawn SubShader (no native call in tests).
            var info = new ShaderPassInfo { Found = true, DrawnShader = shader.name };
            info.Tags["Kind"] = "B";
            info.Passes.Add(("P0", new Dictionary<string, string> { ["Flavor"] = "Sweet" }));
            info.Passes.Add(("P1", new Dictionary<string, string> { ["Flavor"] = "Sour" }));
            var cache = (IDictionary)typeof(ShaderPassInfo).GetField("s_Cache", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var key = (shader.DrawnName, Shader.globalRenderPipeline);
            cache[key] = info;
            try
            {
                var blocks = new[] { new RenderStateBlock(RenderStateMask.Depth), new RenderStateBlock(RenderStateMask.Blend) };
                var bySubShader = new RendererLists.TaggedStates(new ShaderTagId("Kind"), false,
                    new[] { new ShaderTagId("A"), new ShaderTagId("B") }, blocks);
                Assert.Equal(1, bySubShader.IndexFor(material, -1));
                material.SetOverrideTag("Kind", "A");
                Assert.Equal(0, bySubShader.IndexFor(material, -1));
                var byPass = new RendererLists.TaggedStates(new ShaderTagId("Flavor"), true,
                    new[] { new ShaderTagId("Sour"), new ShaderTagId("Sweet") }, blocks);
                Assert.Equal(1, byPass.IndexFor(material, 0));
                Assert.Equal(0, byPass.IndexFor(material, 1));
                // An unknown pass has no pass tag: no override.
                Assert.Equal(-1, byPass.IndexFor(material, -1));
            }
            finally
            {
                cache.Remove(key);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void ADrawNoValueMatchesHasNoOverride()
        {
            Assert.Equal(-1, RendererLists.TaggedStates.Match("Unlit", new[] { "Lit", "SimpleLit" }));
            Assert.Equal(-1, RendererLists.TaggedStates.Match("", new[] { "Lit" }));
        }
    }
}
