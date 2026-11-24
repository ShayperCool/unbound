using System;
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
    /// Shader.renderQueue is the Queue tag of the SubShader the shader runs (Unity 6000.0
    /// Shader.renderQueue and the SubShader tags pages), 2000 without one (the Manual's default
    /// queue), as the declared selection (Shader.ActiveSubShader) picks it. The host is a
    /// stand-in that answers declarations; every shader's source also has Queue text the
    /// selected SubShader does not declare, which is the answer only while the selection is
    /// unknown. Mechanism tests over source definitions, not measured values of a fixture.
    /// </summary>
    public class ShaderRenderQueueTests : IDisposable
    {
        readonly Dictionary<string, string> m_Shaders = new Dictionary<string, string>();
        readonly Dictionary<string, int> m_Calls = new Dictionary<string, int>();
        readonly int m_GlobalLod = Shader.globalMaximumLOD;
        readonly string m_Pipeline = Shader.globalRenderPipeline;
        static int s_Made;

        const string NotFound = "{\"found\":false,\"subshaders_known\":false,\"keywords_known\":false,\"transient\":false,\"reason\":\"\",\"subshaders\":[],\"keywords\":[]}";

        public ShaderRenderQueueTests()
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
        }

        string Call(string method, string request)
        {
            using var document = JsonDocument.Parse(request);
            if (method != "shader-declarations") return NotFound;
            var name = document.RootElement.GetProperty("name").GetString();
            m_Calls[name] = Calls(name) + 1;
            return m_Shaders.TryGetValue(name, out var s) ? s : NotFound;
        }

        int Calls(string name) => m_Calls.TryGetValue(name, out var n) ? n : 0;

        /// <summary>A declared SubShader as the host sends it: its tags as Unity stores them
        /// (<c>QUEUE</c> upper-cased; null: no Queue tag), one pass, its LOD.</summary>
        static string Sub(string queue, int lod = 0, string pipeline = null, bool? runs = true)
        {
            var tags = new List<string>();
            if (queue != null) tags.Add("[\"QUEUE\",\"" + queue + "\"]");
            if (pipeline != null) tags.Add("[\"RenderPipeline\",\"" + pipeline + "\"]");
            return "{\"tags\":[" + string.Join(",", tags) + "],\"passes\":[{\"name\":\"P\",\"tags\":[]}],\"lod\":" + lod
                + ",\"runs\":" + (runs == null ? "null" : runs.Value ? "true" : "false") + "}";
        }

        static string Answer(string[] subshaders, bool known = true, bool transient = false) =>
            "{\"found\":true,\"builtin\":false,\"subshaders_known\":" + (known ? "true" : "false") + ",\"keywords_known\":true,\"transient\":"
            + (transient ? "true" : "false") + ",\"reason\":\"\",\"subshaders\":[" + (known ? string.Join(",", subshaders) : "") + "],\"keywords\":[]}";

        /// <summary>A shader from <paramref name="body"/> (its ShaderLab inside the Shader block)
        /// whose host declares <paramref name="subshaders"/> (own, then its Fallback chain's).</summary>
        Shader Make(string body, params string[] subshaders)
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/RenderQueue/S" + (++s_Made) + "\" {\n" + body + "\n}");
            m_Shaders[shader.name] = Answer(subshaders);
            return shader;
        }

        /// <summary>Source whose first SubShader Queue tag is Geometry+7 (2007), a queue no
        /// declaration below gives: the answer while the selection is unknown.</summary>
        const string Decoy = "SubShader { Tags { \"Queue\"=\"Geometry+7\" } Pass { } }";

        static int Hits(string member) =>
            Stubs.HitReport().Where(h => h.Key == member).Select(h => h.Value).FirstOrDefault();

        /// <summary>Queue text in a comment, a program block, a pass or another pipeline's
        /// SubShader is not the selected SubShader's tag; a property named like the tag is no tag.</summary>
        [Fact]
        public void QueueTextOutsideTheSelectedSubShaderIsNotRead()
        {
            var cases = new (string Body, string[] Subs, int Want)[]
            {
                ("// \"Queue\"=\"Transparent\"\nSubShader { Tags { \"Queue\"=\"AlphaTest\" } Pass { } }", new[] { Sub("AlphaTest") }, 2450),
                ("/* Tags { \"Queue\"=\"Overlay\" } */\nSubShader { Tags { \"Queue\"=\"AlphaTest\" } Pass { } }", new[] { Sub("AlphaTest") }, 2450),
                ("CGINCLUDE\n// \"Queue\"=\"Overlay\"\nENDCG\nSubShader { Pass { } }", new[] { Sub(null) }, 2000),
                ("SubShader { Pass { Tags { \"Queue\"=\"Background\" } } }", new[] { Sub(null) }, 2000),
                ("SubShader { Tags { \"RenderPipeline\"=\"UniversalPipeline\" \"Queue\"=\"Transparent\" } Pass { } }\nSubShader { Tags { \"Queue\"=\"AlphaTest\" } Pass { } }",
                    new[] { Sub("Transparent", pipeline: "UniversalPipeline"), Sub("AlphaTest") }, 2450),
                ("Properties { _Queue (\"Queue\", Float) = 3000 }\nSubShader { Pass { } }", new[] { Sub(null) }, 2000),
            };
            foreach (var (body, subs, want) in cases)
                Assert.Equal(want, Make(body, subs).renderQueue);
        }

        /// <summary>Tags are the selected SubShader's own: a later SubShader's or the
        /// Fallback's queue does not fill in a missing one.</summary>
        [Fact]
        public void ASelectedSubShaderWithoutAQueueTagIsGeometry()
        {
            var shader = Make("SubShader { LOD 300 Pass { } }\nSubShader { LOD 100 Tags { \"Queue\"=\"Transparent\" } Pass { } }\nFallback \"Tests/Other\"",
                Sub(null, 300), Sub("Transparent", 100), Sub("Overlay", 50));
            Assert.Equal(2000, shader.renderQueue);
        }

        /// <summary>Own SubShaders LOD 400/300/200, then the Fallback's 150/50, each with another
        /// queue (LOD 300 without a tag).</summary>
        Shader LodChain() => Make(Decoy, Sub("Transparent", 400), Sub(null, 300), Sub("AlphaTest+1", 200), Sub("Overlay-1", 150), Sub("Background", 50));

        /// <summary>The shader's limit picks the SubShader whose queue it answers, its own before
        /// its Fallback's; none in reach: the first SubShader (as the admitted selection);
        /// a material without its own queue follows.</summary>
        [Fact]
        public void TheLimitPicksTheSubShaderQueue()
        {
            var shader = LodChain();
            var material = new Material(shader);
            foreach (var (limit, want) in new[] { (-1, 3000), (350, 2000), (250, 2451), (175, 3999), (100, 1000), (25, 3000), (-1, 3000) })
            {
                shader.maximumLOD = limit;
                Assert.Equal(want, shader.renderQueue);
                Assert.Equal(want, material.renderQueue);
            }
        }

        /// <summary>A per-shader limit replaces the global one; the Fallback shader's own limit
        /// does not change its referring shader's answer.</summary>
        [Fact]
        public void APerShaderLimitOverridesTheGlobalOneAndTheFallbacksDoesNotApply()
        {
            var shader = LodChain();
            var fallback = Make(Decoy, Sub("Overlay-1", 150), Sub("Background", 50));
            Shader.globalMaximumLOD = 250;
            Assert.Equal(2451, shader.renderQueue);
            Assert.Equal(3999, fallback.renderQueue);
            shader.maximumLOD = 350;
            Assert.Equal(2000, shader.renderQueue);
            Shader.globalMaximumLOD = m_GlobalLod;
            shader.maximumLOD = 175;
            fallback.maximumLOD = 100;
            Assert.Equal(3999, shader.renderQueue);
            Assert.Equal(1000, fallback.renderQueue);
            shader.maximumLOD = -1;
            fallback.maximumLOD = -1;
            Assert.Equal(3000, shader.renderQueue);
            Assert.Equal(3999, fallback.renderQueue);
        }

        /// <summary>The forms the Editor takes: a name in any case, a signed offset right after
        /// it, trailing whitespace (canonical tests/projects/graphicsgaps queue.*).</summary>
        [Fact]
        public void NamedAndOffsetQueuesAreTheirValues()
        {
            foreach (var (value, want) in new[]
            {
                ("Background", 1000), ("Geometry", 2000), ("AlphaTest", 2450), ("Transparent", 3000), ("Overlay", 4000),
                ("Geometry+1", 2001), ("Transparent-1", 2999), ("AlphaTest+10", 2460), ("Overlay-4000", 0),
                ("transparent", 3000), ("GEOMETRY", 2000), ("Geometry+0", 2000), ("Geometry-1", 1999), ("Geometry+1 ", 2001),
                ("Overlay+5000", 9000),
            })
            {
                Assert.Equal(want, Make(Decoy, Sub(value)).renderQueue);
                Assert.Equal(want, ShaderLab.QueueTag(value));
            }
            Assert.Equal(2001, ShaderLab.Queue("SubShader { Tags { \"Queue\" = \"Geometry+1\" } Pass { } }"));
        }

        /// <summary>The source fallback reads SubShader Tags only: a Pass's `Queue` (URP Simple
        /// Lit's Universal2D pass) is no SubShader queue, nor is text in comments, strings or
        /// programs; Stencil's `Pass Replace` opens no pass.</summary>
        [Fact]
        public void TheSourceQueueIsTheFirstSubShaderTag()
        {
            Assert.Equal(2000, ShaderLab.Queue("SubShader { Tags { \"RenderType\" = \"Opaque\" } Pass { Tags { \"LightMode\" = \"Universal2D\" \"Queue\" = \"Transparent\" } } }"));
            Assert.Equal(2450, ShaderLab.Queue(
                "Shader \"A\" { Properties { _Q(\"Queue\", Float) = 0 } // \"Queue\" = \"Overlay\"\n" +
                "SubShader { HLSLINCLUDE struct S { int q; }; static const char* t = \"\\\"Queue\\\" = \\\"Overlay\\\"\"; ENDHLSL\n" +
                "Pass { Stencil { Ref 1 Pass Replace } Tags { \"Queue\" = \"Transparent\" } }\n" +
                "Tags { \"Queue\" = \"AlphaTest\" } } }"));
            Assert.Equal(3000, ShaderLab.Queue("SubShader { Tags { \"Queue\"=\"Transparent\" \"RenderType\"=\"Transparent\" } }"));
            Assert.Equal(2000, ShaderLab.Queue("Shader \"B\" { }"));
            // A Category's tags are its SubShaders' (the legacy particle shaders); a SubShader's
            // own Queue comes first, a SubShader outside the Category does not take it.
            Assert.Equal(3000, ShaderLab.Queue("Category { Tags { \"Queue\"=\"Transparent\" } SubShader { Pass { } } }"));
            Assert.Equal(2450, ShaderLab.Queue("Category { Tags { \"Queue\"=\"Transparent\" } SubShader { Tags { \"Queue\"=\"AlphaTest\" } Pass { } } }"));
            Assert.Equal(2000, ShaderLab.Queue("Category { Tags { \"Queue\"=\"Transparent\" } } SubShader { Pass { } }"));
            Assert.Equal(4000, ShaderLab.Queue("SubShader { Pass { } } SubShader { Tags { \"Queue\"=\"Overlay\" } }"));
            // Nested Categories pass their tags down; commands match in any case.
            Assert.Equal(3000, ShaderLab.Queue("Category { Tags { \"Queue\"=\"Transparent\" } Category { SubShader { } } }"));
            Assert.Equal(1000, ShaderLab.Queue("Subshader { tags { \"Queue\"=\"Background\" } }"));
        }

        /// <summary>A value the Editor does not take is Geometry, and the Editor logs it once per
        /// value as the shader loads, its Fallback's SubShaders' too (canonical
        /// tests/projects/graphicsgaps queue.*, queue.errors).</summary>
        [Fact]
        public void AnUndefinedQueueValueIsGeometryAndLogged()
        {
            foreach (var value in new[] { "2450", "-5", "2000.5", " Transparent", "Geometry + 1", "Geometry+-3", "Bogus", "Opaque", "Geometry+", "+5", "" })
            {
                var logged = new List<string>();
                Application.LogCallback listen = (message, _, type) => { if (type == LogType.Error) logged.Add(message); };
                Application.logMessageReceived += listen;
                try
                {
                    var shader = Make(Decoy, Sub(value), Sub(value));
                    Assert.Equal(2000, shader.renderQueue);
                    Assert.Equal(new[] { "Shader " + shader.name + " uses undefined Queue: '" + value + "'" }, logged);
                }
                finally { Application.logMessageReceived -= listen; }
                Assert.Null(ShaderLab.QueueValue(value));
            }
        }

        /// <summary>While the selection is unknown the source's first Queue text stays the
        /// answer (also from a comment: a fallback, not a selected tag), with a warning for the
        /// reason; once the input that made it unknown changes the selected queue returns.</summary>
        [Fact]
        public void UnknownSelectionsKeepTheSourcesFirstQueueTextAndSayWhy()
        {
            var unknown = Make(Decoy, Sub("Transparent"));
            m_Shaders[unknown.name] = Answer(new[] { Sub("Transparent") }, known: false);
            int before = Hits("Shader.renderQueue for a shader whose SubShaders the host does not know");
            Assert.Equal(2007, unknown.renderQueue);
            Assert.True(Hits("Shader.renderQueue for a shader whose SubShaders the host does not know") > before);

            var restricted = Make(Decoy, Sub("Transparent", runs: null));
            before = Hits("Shader.renderQueue with a SubShader the device may not run");
            Assert.Equal(2007, restricted.renderQueue);
            Assert.True(Hits("Shader.renderQueue with a SubShader the device may not run") > before);
            // One the device does not run is passed over (graphicsgaps select.restricted.*).
            var skipped = Make(Decoy, Sub("Transparent", runs: false), Sub("Overlay"));
            Assert.Equal(4000, skipped.renderQueue);

            var shader = Make(Decoy, Sub("Overlay", pipeline: "UniversalPipeline"), Sub("Transparent"));
            Assert.Equal(3000, shader.renderQueue);
            Shader.globalMaximumLOD = -5;
            before = Hits("Shader.renderQueue under a negative maximum LOD");
            Assert.Equal(2007, shader.renderQueue);
            Assert.True(Hits("Shader.renderQueue under a negative maximum LOD") > before);
            Shader.globalMaximumLOD = m_GlobalLod;
            Assert.Equal(3000, shader.renderQueue);

            var length = typeof(GfxStream).GetField("s_Length", BindingFlags.NonPublic | BindingFlags.Static);
            var stream = length.GetValue(null);
            try
            {
                // Under URP its UniversalPipeline SubShader is selected (graphicsgaps select.*).
                Shader.globalRenderPipeline = "UniversalPipeline";
                Assert.Equal(4000, shader.renderQueue);
            }
            finally
            {
                Shader.globalRenderPipeline = m_Pipeline;
                length.SetValue(null, stream);
            }
            Assert.Equal(3000, shader.renderQueue);
        }

        /// <summary>A kept answer is not asked for again, also for a shader the host does not
        /// know (no retry every frame); a transient one is asked again in a later frame.</summary>
        [Fact]
        public void AnswersAreAskedForOnlyWhileTheyMayChange()
        {
            var kept = Make(Decoy, Sub("Transparent"));
            Assert.Equal(3000, kept.renderQueue);
            int calls = Calls(kept.name);
            var missing = Make(Decoy);
            m_Shaders.Remove(missing.name);
            Assert.Equal(2007, missing.renderQueue);
            int missingCalls = Calls(missing.name);
            for (int frame = 2; frame < 6; frame++)
            {
                ShaderDeclarations.s_FrameForTests = frame;
                Assert.Equal(3000, kept.renderQueue);
                Assert.Equal(2007, missing.renderQueue);
            }
            Assert.Equal(calls, Calls(kept.name));
            Assert.Equal(missingCalls, Calls(missing.name));

            var transient = Make(Decoy, Sub("Transparent"));
            m_Shaders[transient.name] = Answer(new[] { Sub("Transparent") }, known: false, transient: true);
            ShaderDeclarations.s_FrameForTests = 10;
            Assert.Equal(2007, transient.renderQueue);
            m_Shaders[transient.name] = Answer(new[] { Sub("Transparent") });
            Assert.Equal(2007, transient.renderQueue);
            ShaderDeclarations.s_FrameForTests = 11;
            Assert.Equal(3000, transient.renderQueue);
        }

        /// <summary>The engine's replacement and vertex-lit variants are no declared shaders:
        /// they keep the queue they were made with and ask the host nothing.</summary>
        [Fact]
        public void EngineVariantsKeepTheQueueTheyWereMadeWith()
        {
            var shader = Make(Decoy, Sub("Transparent"));
            var replacement = Shader.Replacement(shader, "RenderType", "Opaque");
            var vertexLit = Shader.VertexLit(shader);
            Assert.Equal(2007, replacement.renderQueue);
            Assert.Equal(2007, vertexLit.renderQueue);
            Assert.Equal(0, Calls(replacement.name));
            Assert.Equal(0, Calls(vertexLit.name));
        }

        /// <summary>A material without its own queue (-1) uses its shader's selected queue; its
        /// own queue wins until it is set back to -1.</summary>
        [Fact]
        public void MaterialsFollowTheSelectedQueueUntilTheyHaveTheirOwn()
        {
            var shader = Make(Decoy, Sub("AlphaTest"));
            var material = new Material(shader);
            Assert.Equal(-1, material.rawRenderQueue);
            Assert.Equal(2450, material.renderQueue);
            material.renderQueue = 2100;
            Assert.Equal(2100, material.renderQueue);
            material.renderQueue = -1;
            Assert.Equal(2450, material.renderQueue);
        }
    }
}
