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
    /// Shader.passCount / Material.passCount from the declarations (SubShaders with their LOD,
    /// RenderPipeline tag and device limit, own then Fallback chain), with a stand-in host: the
    /// SubShader the built-in pipeline picks under the shader's maximum LOD, else the global one;
    /// the first SubShader when none is in reach (the canonical
    /// tests/projects/shaderpasscount/Reference/unity-6000.0.71f1.jsonl case.*.passCount,
    /// mat.passCount, newMat.passCount, *.after: an observation of that fixture). Every unknown
    /// condition keeps the renderer's pass list (seeded here), as before.
    /// </summary>
    public class ShaderPassCountTests : IDisposable
    {
        readonly Dictionary<string, string> m_Shaders = new Dictionary<string, string>();
        readonly List<(string, string)> m_Seeded = new List<(string, string)>();
        readonly int m_GlobalLod = Shader.globalMaximumLOD;
        readonly string m_Pipeline = Shader.globalRenderPipeline;
        static int s_Made;

        public ShaderPassCountTests()
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

        /// <summary>A SubShader: <paramref name="lod"/> or <paramref name="restricted"/> null
        /// leaves the field out (an older host).</summary>
        static string Sub(int? lod, int passes, string pipeline = null, bool? restricted = false)
        {
            var tags = pipeline != null ? "[[\"RenderPipeline\",\"" + pipeline + "\"]]" : "[]";
            var list = string.Join(",", Enumerable.Range(0, passes).Select(i => "{\"name\":\"P" + i + "\",\"tags\":[]}"));
            return "{\"tags\":" + tags + ",\"passes\":[" + list + "]"
                + (lod.HasValue ? ",\"lod\":" + lod.Value : "")
                + (restricted.HasValue ? ",\"restricted\":" + (restricted.Value ? "true" : "false") : "")
                + "}";
        }

        static string Answer(bool subshadersKnown, params string[] subshaders) =>
            "{\"found\":true,\"builtin\":false,\"subshaders_known\":" + (subshadersKnown ? "true" : "false")
            + ",\"keywords_known\":true,\"transient\":false,\"reason\":\"\",\"subshaders\":[" + string.Join(",", subshaders) + "],\"keywords\":[]}";

        Shader Make(params string[] subshaders) => MakeWith(true, subshaders);

        Shader MakeWith(bool known, params string[] subshaders)
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/PassCount/S" + (++s_Made) + "\" { SubShader { Pass { } } }");
            m_Shaders[shader.name] = Answer(known, subshaders);
            return shader;
        }

        static IDictionary PassInfoCache() =>
            (IDictionary)typeof(ShaderPassInfo).GetField("s_Cache", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        /// <summary>The renderer's pass list for the shader as it is drawn now (its name with
        /// the current LOD, the current pipeline), so no native call is made.</summary>
        void SeedRenderer(Shader shader, int passes)
        {
            var info = new ShaderPassInfo { Found = true, DrawnShader = shader.name };
            for (int i = 0; i < passes; i++) info.Passes.Add(("R" + i, new Dictionary<string, string>()));
            var key = (shader.DrawnName, Shader.globalRenderPipeline);
            PassInfoCache()[key] = info;
            m_Seeded.Add(key);
        }

        /// <summary>Every readout agrees: Shader, an existing and a new Material.</summary>
        static void Count(int expected, Shader shader, Material existing)
        {
            Assert.Equal(expected, shader.passCount);
            Assert.Equal(expected, existing.passCount);
            Assert.Equal(expected, new Material(shader).passCount);
        }

        static int Hits(string member) =>
            Stubs.HitReport().Where(h => h.Key == member).Select(h => h.Value).FirstOrDefault();

        /// <summary>The LOD/Fallback chain: own SubShaders LOD 400/300/200 (3/1/2 passes), the
        /// Fallback's 150/50 (4/5). A per-shader limit picks the first in reach, its own before
        /// the Fallback's; none in reach: the first SubShader; restoring returns.</summary>
        [Fact]
        public void TheShadersLimitPicksItsOwnThenTheFallbacksSubShader()
        {
            var shader = Make(Sub(400, 3), Sub(300, 1), Sub(200, 2), Sub(150, 4), Sub(50, 5));
            var material = new Material(shader);
            Count(3, shader, material);
            foreach (var (limit, count) in new[] { (350, 1), (250, 2), (175, 4), (100, 5), (25, 3), (-1, 3) })
            {
                shader.maximumLOD = limit;
                Count(count, shader, material);
            }
        }

        /// <summary>The global limit applies to shaders without their own; a per-shader limit
        /// replaces it (also under a global limit that leaves nothing in reach); each shader's
        /// limit is its own.</summary>
        [Fact]
        public void APerShaderLimitOverridesTheGlobalOne()
        {
            var shader = Make(Sub(400, 3), Sub(300, 1), Sub(200, 2), Sub(150, 4), Sub(50, 5));
            var other = Make(Sub(150, 4), Sub(50, 5));
            var material = new Material(shader);
            Shader.globalMaximumLOD = 250;
            Count(2, shader, material);
            Assert.Equal(4, other.passCount);
            Shader.globalMaximumLOD = 100;
            Count(5, shader, material);
            Assert.Equal(5, other.passCount);
            Shader.globalMaximumLOD = 25;
            Count(3, shader, material);
            Assert.Equal(4, other.passCount);
            shader.maximumLOD = 350;
            Count(1, shader, material);
            Assert.Equal(4, other.passCount);
            shader.maximumLOD = 100;
            Count(5, shader, material);
            shader.maximumLOD = -1;
            Shader.globalMaximumLOD = m_GlobalLod;
            Count(3, shader, material);
            Assert.Equal(4, other.passCount);
        }

        /// <summary>A Fallback shader's own limit does not change the SubShader its referring
        /// shader picks from it.</summary>
        [Fact]
        public void TheFallbacksOwnLimitDoesNotApplyToTheReferringShader()
        {
            var shader = Make(Sub(400, 3), Sub(150, 4), Sub(50, 5));
            var fallback = Make(Sub(150, 4), Sub(50, 5));
            shader.maximumLOD = 175;
            fallback.maximumLOD = 100;
            Assert.Equal(4, shader.passCount);
            Assert.Equal(5, fallback.passCount);
        }

        /// <summary>The built-in pipeline skips a SubShader for another pipeline, also as the
        /// first; one without a LOD is LOD 0 and stays in reach.</summary>
        [Fact]
        public void APipelineTaggedSubShaderIsSkipped()
        {
            var shader = Make(Sub(0, 3, "UniversalPipeline"), Sub(0, 2));
            Assert.Equal(2, shader.passCount);
            Shader.globalMaximumLOD = 25;
            Assert.Equal(2, shader.passCount);
        }

        /// <summary>A surface SubShader counts its declared generated passes (four), not the
        /// renderer's forward ones (two); a SubShader whose renderer list flattens others (four
        /// one-pass SubShaders drawn as one list) counts its own.</summary>
        [Fact]
        public void TheDeclaredPassesCountNotTheRenderersList()
        {
            var surface = Make(Sub(300, 4), Sub(200, 3));
            SeedRenderer(surface, 2);
            Assert.Equal(4, surface.passCount);
            Shader.globalMaximumLOD = 250;
            Assert.Equal(3, surface.passCount);
            Shader.globalMaximumLOD = 100;
            Assert.Equal(4, surface.passCount);
            Shader.globalMaximumLOD = m_GlobalLod;

            var flattened = Make(Sub(0, 1), Sub(0, 1), Sub(0, 1), Sub(0, 1));
            SeedRenderer(flattened, 4);
            Assert.Equal(1, flattened.passCount);
            Assert.Equal(1, new Material(flattened).passCount);
        }

        /// <summary>Without LODs (an older host), with unknown SubShaders or with a missing
        /// device-limit flag, the renderer's pass list is kept, with a warning.</summary>
        [Fact]
        public void AnOlderHostOrUnknownSubShadersKeepTheRenderersList()
        {
            var older = Make(Sub(null, 3, restricted: null), Sub(null, 1, restricted: null));
            SeedRenderer(older, 2);
            int before = Hits("Shader.passCount without SubShader LODs");
            Assert.Equal(2, older.passCount);
            Assert.Equal(2, new Material(older).passCount);
            Assert.True(Hits("Shader.passCount without SubShader LODs") > before);

            var noFlag = Make(Sub(0, 3, restricted: null));
            SeedRenderer(noFlag, 2);
            Assert.Equal(2, noFlag.passCount);

            var unknown = MakeWith(false);
            SeedRenderer(unknown, 2);
            Assert.Equal(2, unknown.passCount);
            Assert.Equal(2, unknown.GetPassCountInSubshader(0));
            Assert.Equal(0, unknown.GetPassCountInSubshader(1));
        }

        /// <summary>Unmeasured selections keep the renderer's pass list: a SubShader that may be
        /// limited to some devices where one would be picked, a negative limit, an empty
        /// RenderPipeline tag, nothing in reach with a first SubShader for another pipeline or
        /// a limited one. A limited SubShader out of reach does not matter.</summary>
        [Fact]
        public void UnmeasuredSelectionsKeepTheRenderersList()
        {
            var restricted = Make(Sub(400, 3, restricted: true), Sub(200, 2));
            SeedRenderer(restricted, 7);
            Assert.Equal(7, restricted.passCount);
            restricted.maximumLOD = 300;
            Assert.Equal(2, restricted.passCount);

            var negative = Make(Sub(0, 3));
            negative.maximumLOD = -1;
            Shader.globalMaximumLOD = -5;
            SeedRenderer(negative, 7);
            Assert.Equal(7, negative.passCount);
            Shader.globalMaximumLOD = m_GlobalLod;

            var empty = Make(Sub(0, 3, " "), Sub(0, 2));
            SeedRenderer(empty, 7);
            Assert.Equal(7, empty.passCount);

            var taggedFirst = Make(Sub(300, 3, "UniversalPipeline"), Sub(200, 2));
            var limitedFirst = Make(Sub(300, 3, restricted: true), Sub(200, 2));
            Shader.globalMaximumLOD = 100;
            SeedRenderer(taggedFirst, 7);
            SeedRenderer(limitedFirst, 7);
            Assert.Equal(7, taggedFirst.passCount);
            Assert.Equal(7, limitedFirst.passCount);
        }

        /// <summary>Under a render pipeline the selection is not measured: the renderer's list.
        /// The pipeline's command is discarded afterwards (no engine).</summary>
        [Fact]
        public void ARenderPipelineKeepsTheRenderersList()
        {
            var shader = Make(Sub(0, 3, "UniversalPipeline"), Sub(0, 2));
            var length = typeof(GfxStream).GetField("s_Length", BindingFlags.NonPublic | BindingFlags.Static);
            var before = length.GetValue(null);
            try
            {
                Shader.globalRenderPipeline = "UniversalPipeline";
                SeedRenderer(shader, 7);
                Assert.Equal(7, shader.passCount);
            }
            finally
            {
                Shader.globalRenderPipeline = m_Pipeline;
                length.SetValue(null, before);
            }
            Assert.Equal(2, shader.passCount);
        }
    }
}
