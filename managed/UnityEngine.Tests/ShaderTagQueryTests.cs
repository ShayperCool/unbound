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
    /// <summary>Material tag queries use the declared active SubShader, or search all declared
    /// SubShaders and fallbacks, under the measured built-in pipeline. The host is a stand-in;
    /// the Native fixture is shaderpasscount. Pass indices and drawing are unchanged.</summary>
    public class ShaderTagQueryTests : IDisposable
    {
        readonly Dictionary<string, string> m_Shaders = new Dictionary<string, string>();
        readonly List<(string, string)> m_Seeded = new List<(string, string)>();
        readonly int m_GlobalLod = Shader.globalMaximumLOD;
        readonly string m_Pipeline = Shader.globalRenderPipeline;
        static int s_Made;

        public ShaderTagQueryTests()
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
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/TagQuery/S" + (++s_Made) + "\" { SubShader { Pass { } } }");
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

        /// <summary>Own SubShaders LOD 400/300/200, then the Fallback's 150/50.</summary>
        Shader LodChain() => Make(Sub("L0", 400, Named("L0", 3)), Sub("L1", 300, Named("L1", 1)), Sub("L2", 200, Named("L2", 2)),
            Sub("F0", 150, Named("F0", 4)), Sub("F1", 50, Named("F1", 5)));

        /// <summary>Without fallbacks the tag is the picked SubShader's, its
        /// own before its Fallback's; with them always the first SubShader's tag. None in reach:
        /// the first SubShader.</summary>
        [Fact]
        public void TheLimitPicksTheSubShaderTag()
        {
            var shader = LodChain();
            var material = new Material(shader);
            foreach (var (limit, id, count) in new[] { (-1, "L0", 3), (350, "L1", 1), (250, "L2", 2), (175, "F0", 4), (100, "F1", 5), (25, "L0", 3) })
            {
                shader.maximumLOD = limit;
                Assert.Equal(id, material.GetTag("SubId", false, "none"));
                Assert.Equal("L0", material.GetTag("SubId", true, "none"));
            }
            shader.maximumLOD = -1;
        }

        /// <summary>A per-shader limit replaces the global one, also under a global limit that
        /// leaves nothing in reach; a shader without its own limit then answers from its first
        /// SubShader.</summary>
        [Fact]
        public void APerShaderLimitOverridesTheGlobalOneForTags()
        {
            var shader = LodChain();
            var fallback = Make(Sub("F0", 150, Named("F0", 4)), Sub("F1", 50, Named("F1", 5)));
            var material = new Material(shader);
            var other = new Material(fallback);
            Shader.globalMaximumLOD = 100;
            Assert.Equal("F1", material.GetTag("SubId", false, "none"));
            Assert.Equal("F1", other.GetTag("SubId", false, "none"));
            Assert.Equal("F0", other.GetTag("SubId", true, "none"));
            Shader.globalMaximumLOD = 25;
            Assert.Equal("L0", material.GetTag("SubId", false, "none"));
            Assert.Equal("F0", other.GetTag("SubId", false, "none"));
            shader.maximumLOD = 350;
            Assert.Equal("L1", material.GetTag("SubId", false, "none"));
            Assert.Equal("L0", material.GetTag("SubId", true, "none"));
            shader.maximumLOD = 100;
            Assert.Equal("F1", material.GetTag("SubId", false, "none"));
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
        /// but its tag is the one found with fallbacks.</summary>
        [Fact]
        public void APipelineTaggedFirstSubShaderIsSearchedWithFallbacksOnly()
        {
            var shader = Make(Sub("T0", 0, Named("T0", 3), "UniversalPipeline"), Sub("T1", 0, Named("T1", 2)));
            var material = new Material(shader);
            foreach (var global in new[] { m_GlobalLod, 25 })
            {
                Shader.globalMaximumLOD = global;
                Assert.Equal("T1", material.GetTag("SubId", false, "none"));
                Assert.Equal("T0", material.GetTag("SubId", true, "none"));
            }
        }

        /// <summary>Surface-program expansion is irrelevant to tag selection. A cached renderer
        /// tag for the first SubShader cannot override the declared selection after an LOD change.</summary>
        [Fact]
        public void SurfaceSubShaderTagsFollowTheLimitIndependentlyOfRendererPasses()
        {
            var surface = Make(Sub("S0", 300, Named("Generated", 4)), Sub("S1", 200, Named("S1", 3)));
            SeedRenderer(surface, "S0", "FORWARD", "FORWARD");
            var material = new Material(surface);
            Assert.Equal("S0", material.GetTag("SubId", false, "none"));
            Shader.globalMaximumLOD = 250;
            Assert.Equal("S1", material.GetTag("SubId", false, "none"));
            Assert.Equal("S0", material.GetTag("SubId", true, "none"));
            Shader.globalMaximumLOD = 100;
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

        /// <summary>Unknown declarations and SRP selection preserve the renderer's tag answers.
        /// Warning assertions count calls rather than depending on process-wide log deduplication.</summary>
        [Fact]
        public void UnknownConditionsKeepTheRenderersTagAnswers()
        {
            var unknown = Make(Sub("U0", 0, Named("U", 2)));
            m_Shaders[unknown.name] = m_Shaders[unknown.name].Replace("\"subshaders_known\":true", "\"subshaders_known\":false");
            SeedRenderer(unknown, "R", "R0", "R1");
            Assert.Equal("R", new Material(unknown).GetTag("SubId", false, "none"));
            Assert.Equal("R", new Material(unknown).GetTag("SubId", true, "none"));

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
                Assert.True(Hits("Material.GetTag with fallbacks under a render pipeline") > warned);
                Assert.True(Hits("Material.GetTag under a render pipeline") > 0);
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
