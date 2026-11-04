using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Material.GetTag and Material.FindPass from the declarations, with a stand-in host (canonical
    /// tests/projects/shaderpasscount/Reference/unity-6000.0.71f1.jsonl case.*.mat.subTag,
    /// mat.subTagFallbacks, mat.findPass). Without fallbacks GetTag and FindPass answer from the
    /// SubShader Shader.passCount counts (a Fallback's when picked, the first when none is in
    /// reach); GetTag with fallbacks returns the first SubShader's tag, in declaration order,
    /// whatever the limits or a RenderPipeline tag. Every unknown condition keeps the renderer's
    /// answer (seeded here), as before.
    /// </summary>
    public class ShaderQueryTests : IDisposable
    {
        readonly Dictionary<string, string> m_Shaders = new Dictionary<string, string>();
        readonly List<(string, string)> m_Seeded = new List<(string, string)>();
        readonly int m_GlobalLod = Shader.globalMaximumLOD;
        readonly string m_Pipeline = Shader.globalRenderPipeline;
        static int s_Made;

        public ShaderQueryTests()
        {
            ShaderDeclarations.ResetForTests();
            ShaderDeclarations.s_FrameForTests = 1;
            ShaderDeclarations.s_CallForTests = Call;
        }

        public void Dispose()
        {
            ShaderDeclarations.s_CallForTests = null;
            ShaderDeclarations.s_FrameForTests = null;
            ShaderDeclarations.ResetForTests();
            Shader.globalMaximumLOD = m_GlobalLod;
            var cache = PassInfoCache();
            foreach (var key in m_Seeded) cache.Remove(key);
        }

        string Call(string method, string request)
        {
            using var document = JsonDocument.Parse(request);
            return method == "shader-declarations" && m_Shaders.TryGetValue(document.RootElement.GetProperty("name").GetString(), out var s)
                ? s
                : "{\"found\":false,\"subshaders_known\":false,\"keywords_known\":false,\"transient\":false,\"reason\":\"\",\"subshaders\":[],\"keywords\":[]}";
        }

        /// <summary>A SubShader tagged <c>SubId</c>=<paramref name="id"/> whose passes are
        /// named <paramref name="passes"/> (null: the pass sends no name, as an older host).</summary>
        static string Sub(string id, int lod, string[] passes, string pipeline = null)
        {
            var tags = new List<string> { "[\"SubId\",\"" + id + "\"]" };
            if (pipeline != null) tags.Add("[\"RenderPipeline\",\"" + pipeline + "\"]");
            var list = string.Join(",", passes.Select(n => n == null ? "{\"tags\":[]}" : "{\"name\":\"" + n + "\",\"tags\":[]}"));
            return "{\"tags\":[" + string.Join(",", tags) + "],\"passes\":[" + list + "],\"lod\":" + lod + ",\"restricted\":false}";
        }

        static string[] Named(string id, int count) => Enumerable.Range(0, count).Select(i => id + "P" + i).ToArray();

        static string Answer(params string[] subshaders) =>
            "{\"found\":true,\"builtin\":false,\"subshaders_known\":true,\"keywords_known\":true,\"transient\":false,\"reason\":\"\",\"subshaders\":["
            + string.Join(",", subshaders) + "],\"keywords\":[]}";

        Shader Make(params string[] subshaders)
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/Query/S" + (++s_Made) + "\" { SubShader { Pass { } } }");
            m_Shaders[shader.name] = Answer(subshaders);
            return shader;
        }

        static IDictionary PassInfoCache() =>
            (IDictionary)typeof(ShaderPassInfo).GetField("s_Cache", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        /// <summary>The renderer's drawn SubShader for the shader as drawn now: its SubId tag and
        /// pass names, so no native call is made.</summary>
        void SeedRenderer(Shader shader, string subId, params string[] passes)
        {
            var info = new ShaderPassInfo { Found = true, DrawnShader = shader.name };
            info.Tags["SubId"] = subId;
            foreach (var p in passes) info.Passes.Add((p, new Dictionary<string, string>()));
            var key = (shader.DrawnName, Shader.globalRenderPipeline);
            PassInfoCache()[key] = info;
            m_Seeded.Add(key);
        }

        static int Hits(string member) =>
            Stubs.HitReport().Where(h => h.Key == member).Select(h => h.Value).FirstOrDefault();

        static readonly string[] s_LodNames = Named("L0", 3).Concat(Named("L1", 1)).Concat(Named("L2", 2)).Concat(Named("F0", 4)).Concat(Named("F1", 5)).ToArray();

        /// <summary>Own SubShaders LOD 400/300/200, then the Fallback's 150/50.</summary>
        Shader LodChain() => Make(Sub("L0", 400, Named("L0", 3)), Sub("L1", 300, Named("L1", 1)), Sub("L2", 200, Named("L2", 2)),
            Sub("F0", 150, Named("F0", 4)), Sub("F1", 50, Named("F1", 5)));

        /// <summary>FindPass for every name of the chain: the picked SubShader's own indices.</summary>
        static int[] Find(Material material, string[] names) => names.Select(material.FindPass).ToArray();

        static int[] Expect(string[] names, string[] picked) => names.Select(n => Array.IndexOf(picked, n)).ToArray();

        /// <summary>Without fallbacks the tag and pass indices are the picked SubShader's, its
        /// own before its Fallback's; with them always the first SubShader's tag. None in reach:
        /// the first SubShader.</summary>
        [Fact]
        public void TheLimitPicksTheSubShaderTagAndPassesAnswerFrom()
        {
            var shader = LodChain();
            var material = new Material(shader);
            foreach (var (limit, id, count) in new[] { (-1, "L0", 3), (350, "L1", 1), (250, "L2", 2), (175, "F0", 4), (100, "F1", 5), (25, "L0", 3) })
            {
                shader.maximumLOD = limit;
                Assert.Equal(id, material.GetTag("SubId", false, "none"));
                Assert.Equal("L0", material.GetTag("SubId", true, "none"));
                Assert.Equal(Expect(s_LodNames, Named(id, count)), Find(material, s_LodNames));
            }
            shader.maximumLOD = -1;
        }

        /// <summary>A per-shader limit replaces the global one, also under a global limit that
        /// leaves nothing in reach; a shader without its own limit then answers from its first
        /// SubShader.</summary>
        [Fact]
        public void APerShaderLimitOverridesTheGlobalOneForTagsAndPasses()
        {
            var shader = LodChain();
            var fallback = Make(Sub("F0", 150, Named("F0", 4)), Sub("F1", 50, Named("F1", 5)));
            var material = new Material(shader);
            var other = new Material(fallback);
            var fallbackNames = Named("F0", 4).Concat(Named("F1", 5)).ToArray();
            Shader.globalMaximumLOD = 100;
            Assert.Equal("F1", material.GetTag("SubId", false, "none"));
            Assert.Equal("F1", other.GetTag("SubId", false, "none"));
            Assert.Equal("F0", other.GetTag("SubId", true, "none"));
            Shader.globalMaximumLOD = 25;
            Assert.Equal("L0", material.GetTag("SubId", false, "none"));
            Assert.Equal("F0", other.GetTag("SubId", false, "none"));
            Assert.Equal(Expect(fallbackNames, Named("F0", 4)), Find(other, fallbackNames));
            shader.maximumLOD = 350;
            Assert.Equal("L1", material.GetTag("SubId", false, "none"));
            Assert.Equal("L0", material.GetTag("SubId", true, "none"));
            shader.maximumLOD = 100;
            Assert.Equal("F1", material.GetTag("SubId", false, "none"));
            Assert.Equal(Expect(s_LodNames, Named("F1", 5)), Find(material, s_LodNames));
            shader.maximumLOD = -1;
        }

        /// <summary>A Fallback shader's own limit does not change what its referring shader
        /// answers; its own material answers from its own pick.</summary>
        [Fact]
        public void TheFallbacksOwnLimitDoesNotApplyToTheReferringShaderTags()
        {
            var shader = LodChain();
            var fallback = Make(Sub("F0", 150, Named("F0", 4)), Sub("F1", 50, Named("F1", 5)));
            shader.maximumLOD = 175;
            fallback.maximumLOD = 100;
            Assert.Equal("F0", new Material(shader).GetTag("SubId", false, "none"));
            Assert.Equal("F1", new Material(fallback).GetTag("SubId", false, "none"));
            Assert.Equal("F0", new Material(fallback).GetTag("SubId", true, "none"));
            shader.maximumLOD = -1;
            fallback.maximumLOD = -1;
        }

        /// <summary>A pipeline-tagged first SubShader is skipped for the picked SubShader's tag
        /// and passes, but its tag is the one found with fallbacks.</summary>
        [Fact]
        public void APipelineTaggedFirstSubShaderIsSearchedWithFallbacksOnly()
        {
            var shader = Make(Sub("T0", 0, Named("T0", 3), "UniversalPipeline"), Sub("T1", 0, Named("T1", 2)));
            var material = new Material(shader);
            var names = Named("T0", 3).Concat(Named("T1", 2)).ToArray();
            foreach (var global in new[] { m_GlobalLod, 25 })
            {
                Shader.globalMaximumLOD = global;
                Assert.Equal("T1", material.GetTag("SubId", false, "none"));
                Assert.Equal("T0", material.GetTag("SubId", true, "none"));
                Assert.Equal(new[] { -1, -1, -1, 0, 1 }, Find(material, names));
            }
        }

        /// <summary>A surface SubShader's passes are its declared generated ones (forward base
        /// and add both named FORWARD, then DEFERRED and META), not the renderer's two forward
        /// passes; the first of equal names is found.</summary>
        [Fact]
        public void FindPassIndexesTheDeclaredSurfacePassesNotTheRenderersList()
        {
            var surface = Make(Sub("S0", 300, new[] { "FORWARD", "FORWARD", "DEFERRED", "META" }), Sub("S1", 200, Named("S1", 3)));
            SeedRenderer(surface, "S0", "FORWARD", "FORWARD");
            var material = new Material(surface);
            var names = new[] { "FORWARD", "FORWARD_DELTA", "DEFERRED", "META", "S1P0", "S1P1", "S1P2" };
            Assert.Equal(new[] { 0, -1, 2, 3, -1, -1, -1 }, Find(material, names));
            Assert.Equal(2, material.FindPass("deferred"));
            Shader.globalMaximumLOD = 250;
            Assert.Equal(new[] { -1, -1, -1, -1, 0, 1, 2 }, Find(material, names));
            Assert.Equal("S1", material.GetTag("SubId", false, "none"));
            Assert.Equal("S0", material.GetTag("SubId", true, "none"));
            Shader.globalMaximumLOD = 100;
            Assert.Equal(new[] { 0, -1, 2, 3, -1, -1, -1 }, Find(material, names));
            Assert.Equal("S0", material.GetTag("SubId", false, "none"));
        }

        /// <summary>The material's override tag still comes first; a tag no SubShader has gives
        /// the default value.</summary>
        [Fact]
        public void AnOverrideTagWinsAndAMissingTagGivesTheDefault()
        {
            var material = new Material(LodChain());
            Assert.Equal("none", material.GetTag("Missing", false, "none"));
            Assert.Equal("none", material.GetTag("Missing", true, "none"));
            material.SetOverrideTag("SubId", "Mine");
            Assert.Equal("Mine", material.GetTag("SubId", false, "none"));
            Assert.Equal("Mine", material.GetTag("SubId", true, "none"));
        }

        /// <summary>Unknown conditions keep the renderer's answers, with a warning: missing pass
        /// names (an older host) for FindPass; a render pipeline for both, GetTag with fallbacks
        /// included. The pipeline's command is discarded afterwards (no engine).</summary>
        [Fact]
        public void UnknownConditionsKeepTheRenderersAnswers()
        {
            var unnamed = Make(Sub("U0", 0, new string[] { null, null }));
            SeedRenderer(unnamed, "R", "R0", "R1");
            Assert.Equal(1, new Material(unnamed).FindPass("R1"));

            var shader = Make(Sub("P0", 0, Named("P0", 3), "UniversalPipeline"), Sub("P1", 0, Named("P1", 2)));
            var length = typeof(GfxStream).GetField("s_Length", BindingFlags.NonPublic | BindingFlags.Static);
            var before = length.GetValue(null);
            try
            {
                Shader.globalRenderPipeline = "UniversalPipeline";
                SeedRenderer(shader, "R", "R0");
                var material = new Material(shader);
                int warned = Hits("Material.GetTag with fallbacks under a render pipeline");
                Assert.Equal("R", material.GetTag("SubId", false, "none"));
                Assert.Equal("R", material.GetTag("SubId", true, "none"));
                Assert.Equal(0, material.FindPass("R0"));
                Assert.Equal(-1, material.FindPass("P0P0"));
                Assert.True(Hits("Material.GetTag with fallbacks under a render pipeline") > warned);
                Assert.True(Hits("Material.GetTag under a render pipeline") > 0);
                Assert.True(Hits("Material.FindPass under a render pipeline") > 0);
            }
            finally
            {
                Shader.globalRenderPipeline = m_Pipeline;
                length.SetValue(null, before);
            }
            Assert.Equal("P1", new Material(shader).GetTag("SubId", false, "none"));
        }
    }
}
