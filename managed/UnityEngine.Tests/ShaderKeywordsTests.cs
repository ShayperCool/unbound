using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Shader keyword spaces, global keywords, material and compute keywords, SubShader metadata
    /// and material variants, against the answers of the canonical
    /// tests/projects/shaderkeywords/Reference/unity-6000.0.71f1.jsonl (keys named in each test),
    /// with a stand-in host answering the declarations the provider would. Also the fallbacks: an
    /// older host, an unmeasured release, unknown and transient answers keep the name-based
    /// behaviour and recover.
    /// </summary>
    public class ShaderKeywordsTests : IDisposable
    {
        static readonly string[] Stereo = { "STEREO_INSTANCING_ON", "UNITY_SINGLE_PASS_STEREO", "STEREO_MULTIVIEW_ON", "STEREO_CUBEMAP_RENDER_ON" };

        readonly string m_Version = ProjectInfo.UnityVersion;
        readonly Dictionary<string, string> m_Shaders = new Dictionary<string, string>();
        readonly Dictionary<string, string> m_Computes = new Dictionary<string, string>();
        int m_Calls;
        bool m_Throw;

        readonly string[] m_EnabledBefore = Shader.s_GlobalKeywords.ToArray();

        public ShaderKeywordsTests()
        {
            foreach (var k in m_EnabledBefore) Shader.DisableKeyword(k);
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Shader.ResetGlobalKeywordsForTests();
            ShaderDeclarations.ResetForTests();
            ShaderDeclarations.s_FrameForTests = 1;
            ShaderDeclarations.s_CallForTests = Call;
        }

        public void Dispose()
        {
            ShaderDeclarations.s_CallForTests = null;
            ShaderDeclarations.s_FrameForTests = null;
            ShaderDeclarations.ResetForTests();
            foreach (var k in Shader.s_GlobalKeywords.ToArray()) Shader.DisableKeyword(k);
            ProjectInfo.UnityVersion = m_Version;
            Shader.ResetGlobalKeywordsForTests();
            foreach (var k in m_EnabledBefore) Shader.EnableKeyword(k);
        }

        string Call(string method, string request)
        {
            m_Calls++;
            if (m_Throw) throw new InvalidOperationException($"native `{method}` failed: unknown method");
            var root = JsonDocument.Parse(request).RootElement;
            if (method == "shader-declarations")
                return m_Shaders.TryGetValue(root.GetProperty("name").GetString(), out var s) ? s : NotFound;
            return m_Computes.TryGetValue(root.GetProperty("file").GetString(), out var c) ? c : NotFound;
        }

        const string NotFound = "{\"found\":false,\"subshaders_known\":false,\"keywords_known\":false,\"transient\":false,\"reason\":\"\",\"subshaders\":[],\"keywords\":[]}";

        static string K(string name, bool local = false, bool dynamic = false) =>
            "{\"name\":\"" + name + "\",\"local\":" + (local ? "true" : "false") + ",\"dynamic\":" + (dynamic ? "true" : "false") + "}";

        static string Answer(IEnumerable<string> keywords, string subshaders = "[]", bool subshadersKnown = true, bool keywordsKnown = true, bool transient = false, string reason = "") =>
            "{\"found\":true,\"builtin\":false,\"subshaders_known\":" + (subshadersKnown ? "true" : "false")
            + ",\"keywords_known\":" + (keywordsKnown ? "true" : "false") + ",\"transient\":" + (transient ? "true" : "false")
            + ",\"reason\":\"" + reason + "\",\"subshaders\":" + subshaders + ",\"keywords\":[" + string.Join(",", keywords) + "]}";

        static IEnumerable<string> WithStereo(params string[] rest) => Stereo.Select(n => K(n)).Concat(rest);

        /// <summary>A part of Probe/KwForms's space: its first keywords of each kind.</summary>
        static readonly string[] FormsKeywords =
        {
            K("VTX_GLOBAL"), K("GLOBAL_A"), K("FEATURE_A"), K("DYN_GLOBAL", dynamic: true), K("SHARED_KW"), K("TOGGLE_KW"),
            K("LOCAL_A", local: true), K("LOCAL_B", local: true), K("LOCAL_C", local: true), K("DYN_LOCAL_A", local: true, dynamic: true),
        };

        static int s_Made;

        Shader Forms()
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/Kw/Forms" + (++s_Made) + "\" {\n"
                + "Properties { _Color (\"Color\", Color) = (1, 0, 0, 1) _Value (\"Value\", Float) = 0.5 _Other (\"Other\", Range(0, 2)) = 1 _MainTex (\"Texture\", 2D) = \"white\" {} [Toggle(TOGGLE_KW)] _Toggle (\"Toggle\", Float) = 0 }\n"
                + "SubShader { Tags { \"Queue\"=\"Geometry+5\" } Pass { } } }");
            m_Shaders[shader.name] = Answer(WithStereo(FormsKeywords));
            return shader;
        }

        Shader Plain()
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/Kw/Plain" + (++s_Made) + "\" { SubShader { Pass { } } }");
            m_Shaders[shader.name] = Answer(WithStereo(K("FOG_LINEAR"), K("FOG_EXP"), K("FOG_EXP2")));
            return shader;
        }

        static List<string> Errors(Action action)
        {
            var logs = new List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error || type == LogType.Warning) logs.Add(type + ": " + message); };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return logs;
        }

        static string[] Names(IEnumerable<LocalKeyword> keywords) => keywords.Select(k => k.name).ToArray();

        // ------------------------------------------------------------------ keyword spaces

        /// <summary>probe.*.space.*, local.*: names in order, stereo keywords built-in and
        /// overridable, local ones not, dynamic_branch ones dynamic; undeclared names invalid.</summary>
        [Fact]
        public void AKnownSpaceListsItsKeywordsWithTypesAndFlags()
        {
            var forms = Forms();
            var plain = Plain();
            var space = forms.keywordSpace;
            Assert.Equal(14u, space.keywordCount);
            Assert.Equal(Stereo.Concat(new[] { "VTX_GLOBAL", "GLOBAL_A" }), space.keywordNames.Take(6));
            var stereo = space.FindKeyword("STEREO_INSTANCING_ON");
            Assert.Equal(ShaderKeywordType.BuiltinDefault, stereo.type);
            Assert.True(stereo.isOverridable && !stereo.isDynamic && stereo.isValid);
            var local = space.FindKeyword("DYN_LOCAL_A");
            Assert.Equal(ShaderKeywordType.UserDefined, local.type);
            Assert.True(local.isDynamic && !local.isOverridable);
            Assert.True(space.FindKeyword("DYN_GLOBAL").isOverridable);
            Assert.False(space.FindKeyword("NOT_DECLARED").isValid);
            Assert.False(space.FindKeyword("").isValid);
            Assert.Equal(space.keywords.Select(k => k.name), space.keywordNames);

            List<string> logs = null;
            LocalKeyword a = default, other = default;
            logs = Errors(() =>
            {
                a = new LocalKeyword(forms, "LOCAL_A");
                Assert.False(new LocalKeyword(forms, "NOT_DECLARED").isValid);
                Assert.True(new LocalKeyword(forms, "GLOBAL_A").isOverridable);
                other = new LocalKeyword(plain, "LOCAL_A");
            });
            Assert.Equal(new[] { "Error: Local keyword NOT_DECLARED doesn't exist in the shader.", "Error: Local keyword LOCAL_A doesn't exist in the shader." }, logs);
            Assert.True(a == forms.keywordSpace.FindKeyword("LOCAL_A"));
            Assert.False(other.isValid);
            Assert.False(other == a);
            Assert.True(forms.keywordSpace != plain.keywordSpace);
            Assert.Equal("LOCAL_A", a.ToString());

            // local.default and local.ctor(null shader).
            logs = Errors(() => Assert.False(default(LocalKeyword).isValid));
            Assert.Equal(new[] { "Error: LocalKeyword not initialized." }, logs);
            Assert.Null(default(LocalKeyword).ToString());
            NullReferenceException nre = null;
            logs = Errors(() => nre = Assert.Throws<NullReferenceException>(() => new LocalKeyword((Shader)null, "LOCAL_A")));
            Assert.Equal(new[] { "Error: Cannot initialize a LocalKeyword with a null Shader." }, logs);
            Assert.Equal("Object reference not set to an instance of an object", nre.Message);
        }

        // ------------------------------------------------------------------ materials

        /// <summary>mat.new … mat.back, in the probe's order.</summary>
        [Fact]
        public void MaterialsSeeOnlyDeclaredKeywordsButKeepEveryName()
        {
            var forms = Forms();
            var plain = Plain();
            LocalKeyword Kw(string n) => forms.keywordSpace.FindKeyword(n);
            var m = new Material(forms);
            Assert.Empty(m.shaderKeywords);
            m.SetKeyword(Kw("LOCAL_A"), true);
            m.EnableKeyword(Kw("GLOBAL_A"));
            m.EnableKeyword(Kw("DYN_LOCAL_A"));
            Assert.Equal(new[] { "DYN_LOCAL_A", "GLOBAL_A", "LOCAL_A" }, m.shaderKeywords);
            Assert.Equal(new[] { "GLOBAL_A", "LOCAL_A", "DYN_LOCAL_A" }, Names(m.enabledKeywords));
            Assert.True(m.IsKeywordEnabled(Kw("LOCAL_A")) && m.IsKeywordEnabled("LOCAL_A"));
            m.EnableKeyword("NOT_DECLARED");
            m.EnableKeyword("LOCAL_B");
            Assert.Equal(new[] { "DYN_LOCAL_A", "GLOBAL_A", "LOCAL_A", "LOCAL_B", "NOT_DECLARED" }, m.shaderKeywords);
            Assert.Equal(new[] { "GLOBAL_A", "LOCAL_A", "LOCAL_B", "DYN_LOCAL_A" }, Names(m.enabledKeywords));
            Assert.False(m.IsKeywordEnabled("NOT_DECLARED"));
            Assert.False(m.IsKeywordEnabled(Kw("NOT_DECLARED")));
            m.DisableKeyword(Kw("LOCAL_A"));
            m.SetKeyword(Kw("GLOBAL_A"), false);
            Assert.Equal(new[] { "DYN_LOCAL_A", "LOCAL_B", "NOT_DECLARED" }, m.shaderKeywords);
            Assert.Equal(new[] { "LOCAL_B", "DYN_LOCAL_A" }, Names(m.enabledKeywords));
            m.shader = plain;
            Assert.Equal(new[] { "DYN_LOCAL_A", "LOCAL_B", "NOT_DECLARED" }, m.shaderKeywords);
            Assert.Empty(m.enabledKeywords);
            Assert.False(m.IsKeywordEnabled("LOCAL_B"));
            m.shader = forms;
            Assert.Equal(new[] { "LOCAL_B", "DYN_LOCAL_A" }, Names(m.enabledKeywords));
            // mat.toggle: a [Toggle] drawer's keyword is not set by SetFloat.
            var toggled = new Material(forms);
            Assert.False(toggled.IsKeywordEnabled("TOGGLE_KW"));
            toggled.SetFloat("_Toggle", 1);
            Assert.False(toggled.IsKeywordEnabled("TOGGLE_KW"));
        }

        /// <summary>mat.set and mat.setEnabled: declared names sorted, then the others sorted, as
        /// often and as empty as given; enabledKeywords replaces only declared ones.</summary>
        [Fact]
        public void ShaderKeywordsKeepRepeatedAndEmptyNames()
        {
            var forms = Forms();
            var m = new Material(forms);
            m.shaderKeywords = new[] { "GLOBAL_A", "BOGUS", "LOCAL_A", "BOGUS", "", "DYN_GLOBAL", "AAA_FIRST" };
            Assert.Equal(new[] { "DYN_GLOBAL", "GLOBAL_A", "LOCAL_A", "", "AAA_FIRST", "BOGUS", "BOGUS" }, m.shaderKeywords);
            Assert.Equal(new[] { "GLOBAL_A", "DYN_GLOBAL", "LOCAL_A" }, Names(m.enabledKeywords));
            m.enabledKeywords = new[] { forms.keywordSpace.FindKeyword("LOCAL_C"), forms.keywordSpace.FindKeyword("FEATURE_A") };
            Assert.Equal(new[] { "FEATURE_A", "LOCAL_C", "", "AAA_FIRST", "BOGUS", "BOGUS" }, m.shaderKeywords);
            Assert.Equal(new[] { "FEATURE_A", "LOCAL_C" }, Names(m.enabledKeywords));
            // The renderer and the CRC see the enabled names once, as before.
            var plainSet = new Material(forms) { shaderKeywords = new[] { "FEATURE_A", "LOCAL_C", "AAA_FIRST", "BOGUS" } };
            Assert.Equal(plainSet.ComputeCRC(), m.ComputeCRC());
            Assert.Equal(new HashSet<string> { "FEATURE_A", "LOCAL_C", "AAA_FIRST", "BOGUS" }, m.m_Keywords);
        }

        /// <summary>mat.otherSpace: another shader's keyword logs and changes nothing.</summary>
        [Fact]
        public void AnotherShadersKeywordChangesNothing()
        {
            var forms = Forms();
            var plain = Plain();
            var m = new Material(forms);
            var logs = Errors(() => m.SetKeyword(new LocalKeyword(plain, "LOCAL_A"), true));
            Assert.Equal(new[] { "Error: Local keyword LOCAL_A doesn't exist in the shader.", "Error: Local keyword LOCAL_A comes from a different shader." }, logs);
            Assert.Empty(m.shaderKeywords);
            Assert.Empty(m.enabledKeywords);
        }

        /// <summary>saved.*: a saved material's valid and invalid keywords; a copy keeps them, also
        /// on another shader.</summary>
        [Fact]
        public void SavedKeywordsAreKeptOnCopiesAndShaderChanges()
        {
            var forms = Forms();
            var plain = Plain();
            var m = Read(@"Material:
  m_Name: KwSaved
  m_Shader: {fileID: 4800000, guid: aa000000000000000000000000000001, type: 3}
  m_ValidKeywords:
  - GLOBAL_A
  - LOCAL_A
  m_InvalidKeywords:
  - STALE_KW
  m_CustomRenderQueue: -1
  m_LockedProperties:
", new Dictionary<string, Object> { ["aa000000000000000000000000000001"] = forms });
            Assert.Equal(new[] { "GLOBAL_A", "LOCAL_A", "STALE_KW" }, m.shaderKeywords);
            Assert.Equal(new[] { "GLOBAL_A", "LOCAL_A" }, Names(m.enabledKeywords));
            Assert.False(m.IsKeywordEnabled("STALE_KW"));
            var copy = new Material(m);
            Assert.Equal(new[] { "GLOBAL_A", "LOCAL_A", "STALE_KW" }, copy.shaderKeywords);
            copy.shader = plain;
            Assert.Equal(new[] { "GLOBAL_A", "LOCAL_A", "STALE_KW" }, copy.shaderKeywords);
            Assert.Empty(copy.enabledKeywords);
            // Reverting to the saved settings (Resources.UnloadAsset) takes their keywords again.
            copy.shaderKeywords = new[] { "X", "X" };
            copy.RevertTo(new Material(m));
            Assert.Equal(new[] { "GLOBAL_A", "LOCAL_A", "STALE_KW" }, copy.shaderKeywords);
        }

        // ------------------------------------------------------------------ unknown spaces

        /// <summary>An older host (no such method): one warning, every call by name as before,
        /// never asked again.</summary>
        [Fact]
        public void AnOlderHostKeepsNameBasedKeywords()
        {
            m_Throw = true;
            var forms = Forms();
            Material m = null;
            LocalKeyword k = default;
            var logs = Errors(() =>
            {
                k = new LocalKeyword(forms, "ANYTHING");
                m = new Material(forms);
                m.SetKeyword(k, true);
                m.EnableKeyword("B_NAME");
            });
            Assert.DoesNotContain(logs, l => l.StartsWith("Error"));
            Assert.Single(logs, l => l.Contains("ShaderDeclarations"));
            Assert.True(k.isValid);
            Assert.True(m.IsKeywordEnabled("ANYTHING") && m.IsKeywordEnabled(k));
            Assert.Equal(new[] { "ANYTHING", "B_NAME" }, m.shaderKeywords);
            Assert.Equal(new[] { "ANYTHING", "B_NAME" }, Names(m.enabledKeywords));
            Assert.Equal(1, forms.subshaderCount);
            Assert.Equal(1, m_Calls);
        }

        /// <summary>A space reported unknown for a reason that does not pass (an unmeasured release,
        /// a GLSL pass) is name-based and is not asked again; it is not an empty space.</summary>
        [Fact]
        public void AnUnknownSpaceIsNotAnEmptyOne()
        {
            var forms = Forms();
            m_Shaders[forms.name] = Answer(new string[0], keywordsKnown: false, subshadersKnown: false, reason: "not measured");
            var m = new Material(forms);
            m.SetKeyword(new LocalKeyword(forms, "LOCAL_A"), true);
            Assert.True(m.IsKeywordEnabled("LOCAL_A"));
            Assert.True(forms.keywordSpace.FindKeyword("ANY").isValid);
            Assert.Equal(0u, forms.keywordSpace.keywordCount);
            int calls = m_Calls;
            ShaderDeclarations.s_FrameForTests = 2;
            Assert.True(m.IsKeywordEnabled("LOCAL_A"));
            Assert.Equal(calls, m_Calls);
        }

        /// <summary>A transient answer is name-based meanwhile, asked again at most once a frame, and
        /// replaced by the known one when the host has it.</summary>
        [Fact]
        public void ATransientUnknownRecovers()
        {
            var forms = Forms();
            var known = m_Shaders[forms.name];
            m_Shaders[forms.name] = Answer(new string[0], keywordsKnown: false, transient: true, reason: "includes not available");
            var m = new Material(forms);
            m.EnableKeyword("NOT_DECLARED");
            Assert.True(m.IsKeywordEnabled("NOT_DECLARED"));
            int calls = m_Calls;
            Assert.True(m.IsKeywordEnabled("NOT_DECLARED"));
            Assert.Equal(calls, m_Calls);
            m_Shaders[forms.name] = known;
            ShaderDeclarations.s_FrameForTests = 2;
            Assert.False(m.IsKeywordEnabled("NOT_DECLARED"));
            Assert.Equal(calls + 1, m_Calls);
            ShaderDeclarations.s_FrameForTests = 3;
            Assert.False(m.IsKeywordEnabled("NOT_DECLARED"));
            Assert.Equal(calls + 1, m_Calls);
        }

        /// <summary>A shader the host does not know yet (one generated later) is asked for again.</summary>
        [Fact]
        public void AShaderNotFoundIsAskedForAgain()
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/Kw/Later" + (++s_Made) + "\" { SubShader { Pass { } } }");
            Assert.Equal(1, shader.subshaderCount);
            m_Shaders[shader.name] = Answer(WithStereo(), "[{\"tags\":[],\"passes\":[{\"name\":\"\",\"tags\":[]}]},{\"tags\":[],\"passes\":[]}]");
            ShaderDeclarations.s_FrameForTests = 2;
            Assert.Equal(2, shader.subshaderCount);
        }

        // ------------------------------------------------------------------ compute shaders

        /// <summary>compute.*: locals first, undeclared names ignored, sorted shaderKeywords; a known
        /// empty space declares nothing; an unknown one keeps every name.</summary>
        [Fact]
        public void ComputeShadersIgnoreUndeclaredNames()
        {
            var cs = ComputeShader.FromFile("Tests/Kw.compute", "Kw", "#pragma kernel CSMain\n");
            m_Computes["Tests/Kw.compute"] = Answer(new[] { K("CS_LOCAL", local: true), K("CS_GLOBAL_A"), K("CS_GLOBAL_B"), K("CS_DYN", dynamic: true), K("LIGHTMAP_ON") });
            var space = cs.keywordSpace;
            Assert.Equal(new[] { "CS_LOCAL", "CS_GLOBAL_A", "CS_GLOBAL_B", "CS_DYN", "LIGHTMAP_ON" }, space.keywordNames);
            Assert.False(space.FindKeyword("CS_LOCAL").isOverridable);
            Assert.True(space.FindKeyword("CS_DYN").isDynamic);
            cs.EnableKeyword(space.FindKeyword("CS_LOCAL"));
            cs.SetKeyword(space.FindKeyword("CS_GLOBAL_A"), true);
            cs.EnableKeyword("NOT_DECLARED");
            Assert.Equal(new[] { "CS_GLOBAL_A", "CS_LOCAL" }, cs.shaderKeywords);
            Assert.Equal(new[] { "CS_LOCAL", "CS_GLOBAL_A" }, Names(cs.enabledKeywords));
            Assert.False(cs.IsKeywordEnabled("NOT_DECLARED"));
            Assert.True(cs.IsKeywordEnabled(space.FindKeyword("CS_LOCAL")));

            var empty = ComputeShader.FromFile("Tests/Empty.compute", "Empty", "#pragma kernel CSMain\n");
            m_Computes["Tests/Empty.compute"] = Answer(new string[0]);
            Assert.Equal(0u, empty.keywordSpace.keywordCount);
            empty.EnableKeyword("ANY");
            Assert.False(empty.IsKeywordEnabled("ANY"));

            var unknown = ComputeShader.FromFile("Tests/Unknown.compute", "Unknown", "#pragma kernel CSMain\n");
            m_Computes["Tests/Unknown.compute"] = Answer(new string[0], keywordsKnown: false, reason: "cannot be read");
            unknown.EnableKeyword("ANY");
            Assert.True(unknown.IsKeywordEnabled("ANY"));
            Assert.True(unknown.keywordSpace.FindKeyword("ANY").isValid);
        }

        // ------------------------------------------------------------------ global keywords

        /// <summary>global.*: built-in keywords exist, declarations make none, unknown names log and
        /// stand for nothing, Create and enabling by name make keywords; nothing starts enabled.</summary>
        [Fact]
        public void GlobalKeywordsFollowTheMeasuredRegistry()
        {
            Assert.Empty(Shader.enabledGlobalKeywords);
            var all = new HashSet<string>(Shader.globalKeywords.Select(k => k.name));
            Assert.Contains("DIRECTIONAL", all);
            Assert.Contains("STEREO_INSTANCING_ON", all);
            Assert.DoesNotContain("GLOBAL_A", all);
            Assert.DoesNotContain("UNITY_UI_CLIP_RECT", all);

            var created = GlobalKeyword.Create("CREATED_KW");
            Assert.Equal("CREATED_KW", created.ToString());
            Assert.Single(Shader.globalKeywords, k => k.name == "CREATED_KW");
            Assert.False(Shader.IsKeywordEnabled(created));
            GlobalKeyword.Create("CREATED_KW");
            Assert.Single(Shader.globalKeywords, k => k.name == "CREATED_KW");

            var logs = Errors(() =>
            {
                Assert.Equal("NEVER_SEEN_CTOR", new GlobalKeyword("NEVER_SEEN_CTOR").name);
                Shader.EnableKeyword("STRING_ENABLED_KW");
                Shader.EnableKeyword(new GlobalKeyword("GLOBAL_A"));
                Shader.SetKeyword(GlobalKeyword.Create("SET_KW"), true);
            });
            Assert.Equal(new[] { "Error: Global keyword NEVER_SEEN_CTOR doesn't exist.", "Error: Global keyword GLOBAL_A doesn't exist." }, logs);
            Assert.DoesNotContain(Shader.globalKeywords, k => k.name == "NEVER_SEEN_CTOR");
            Assert.Equal(new[] { "SET_KW", "STRING_ENABLED_KW" }, Shader.enabledGlobalKeywords.Select(k => k.name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Contains(Shader.globalKeywords, k => k.name == "STRING_ENABLED_KW");
            Assert.False(Shader.IsKeywordEnabled("GLOBAL_A"));
            Assert.True(Shader.IsKeywordEnabled(new GlobalKeyword("STRING_ENABLED_KW")));
            Shader.DisableKeyword("STRING_ENABLED_KW");
            Shader.SetKeyword(new GlobalKeyword("SET_KW"), false);
            Assert.Empty(Shader.enabledGlobalKeywords);
        }

        /// <summary>Another release (even of the family): every name is a global keyword, as before.</summary>
        [Fact]
        public void AnUnmeasuredReleaseKeepsNameBasedGlobalKeywords()
        {
            ProjectInfo.UnityVersion = "6000.0.72f1";
            Shader.ResetGlobalKeywordsForTests();
            var logs = Errors(() =>
            {
                Shader.EnableKeyword(new GlobalKeyword("GLOBAL_A"));
                Assert.True(Shader.IsKeywordEnabled(new GlobalKeyword("GLOBAL_A")));
            });
            Assert.DoesNotContain(logs, l => l.StartsWith("Error"));
            Assert.Equal(new[] { "GLOBAL_A" }, Shader.globalKeywords.Select(k => k.name));
            Assert.Equal(ShaderKeywordType.UserDefined, default(LocalKeyword).type);
        }

        // ------------------------------------------------------------------ SubShader metadata

        /// <summary>probe.Probe/KwForms meta and outOfRange: the fallback's SubShader counts, -1 out of
        /// range, out-of-range SubShader tags throw, tags as stored.</summary>
        [Fact]
        public void SubShadersComeFromTheDeclarations()
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/Kw/Subs" + (++s_Made) + "\" { SubShader { Pass { } } }");
            m_Shaders[shader.name] = Answer(WithStereo(),
                "[{\"tags\":[[\"QUEUE\",\"Geometry+5\"],[\"RenderType\",\"Opaque\"]],\"passes\":[{\"name\":\"MAIN\",\"tags\":[[\"LIGHTMODE\",\"FORWARDBASE\"]]},{\"name\":\"SECOND\",\"tags\":[[\"LIGHTMODE\",\"SHADOWCASTER\"]]}]},"
                + "{\"tags\":[[\"RenderPipeline\",\"UniversalPipeline\"]],\"passes\":[{\"name\":\"\",\"tags\":[[\"LIGHTMODE\",\"UniversalForward\"]]},{\"name\":\"EXTRA\",\"tags\":[]}]},"
                + "{\"tags\":[[\"RenderType\",\"Opaque\"]],\"passes\":[{\"name\":\"\",\"tags\":[]}]}]");
            Assert.Equal(3, shader.subshaderCount);
            Assert.Equal(new[] { 2, 2, 1 }, Enumerable.Range(0, 3).Select(shader.GetPassCountInSubshader));
            Assert.Equal(-1, shader.GetPassCountInSubshader(-1));
            Assert.Equal(-1, shader.GetPassCountInSubshader(3));
            Assert.Equal("Geometry+5", shader.FindSubshaderTagValue(0, new ShaderTagId("Queue")).name);
            Assert.Equal("UniversalPipeline", shader.FindSubshaderTagValue(1, new ShaderTagId("RenderPipeline")).name);
            Assert.Equal(ShaderTagId.none, shader.FindSubshaderTagValue(2, new ShaderTagId("ProbeTag")));
            Assert.Equal("FORWARDBASE", shader.FindPassTagValue(0, 0, new ShaderTagId("LightMode")).name);
            Assert.Equal("UniversalForward", shader.FindPassTagValue(1, 0, new ShaderTagId("LightMode")).name);
            Assert.Equal(ShaderTagId.none, shader.FindPassTagValue(1, 1, new ShaderTagId("LightMode")));
            // The canonical records type and message: the Editor's (Mono's) wording.
            var e = Assert.Throws<ArgumentOutOfRangeException>(() => shader.FindSubshaderTagValue(3, new ShaderTagId("RenderType")));
            Assert.Equal("Specified argument was out of the range of valid values.\nParameter name: Invalid subshaderIndex 3. Value must be in the range [0, 3)", e.Message);
            e = Assert.Throws<ArgumentOutOfRangeException>(() => shader.FindSubshaderTagValue(-1, new ShaderTagId("RenderType")));
            Assert.Equal("Specified argument was out of the range of valid values.\nParameter name: Invalid subshaderIndex -1. Value must be in the range [0, 3)", e.Message);
        }

        // ------------------------------------------------------------------ variants

        sealed class ByGuid : IReferenceResolver
        {
            internal readonly Dictionary<string, Object> Objects;
            internal ByGuid(Dictionary<string, Object> objects) => Objects = objects;
            public Object Resolve(ObjectRef reference, Type expected) => reference.Guid != null && Objects.TryGetValue(reference.Guid, out var o) ? o : null;
        }

        static Material Read(string yaml, Dictionary<string, Object> objects)
        {
            var m = new Material();
            ((ISerializedAsset)m).ReadSerialized(TestYaml.DocumentBody(yaml), new ByGuid(objects));
            return m;
        }

        const string ShaderGuid = "aa000000000000000000000000000002";
        const string ParentGuid = "aa000000000000000000000000000003";
        const string ChildGuid = "aa000000000000000000000000000004";

        (Material Parent, Material Child, Material Grand) Family()
        {
            var objects = new Dictionary<string, Object> { [ShaderGuid] = Forms() };
            var parent = Read(@"Material:
  m_Name: VarParent
  m_Shader: {fileID: 4800000, guid: " + ShaderGuid + @", type: 3}
  m_Parent: {fileID: 0}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords:
  - LOCAL_A
  m_InvalidKeywords: []
  m_CustomRenderQueue: -1
  m_LockedProperties: _Other
  m_SavedProperties:
    m_TexEnvs:
    - _MainTex:
        m_Texture: {fileID: 0}
        m_Scale: {x: 1, y: 1}
        m_Offset: {x: 0, y: 0}
    m_Floats:
    - _Other: 1
    - _Toggle: 0
    - _Value: 0.25
    m_Colors:
    - _Color: {r: 1, g: 0, b: 0, a: 1}
", objects);
            parent.name = "VarParent";
            objects[ParentGuid] = parent;
            var child = Read(@"Material:
  m_Name: VarChild
  m_Shader: {fileID: 4800000, guid: " + ShaderGuid + @", type: 3}
  m_Parent: {fileID: 2100000, guid: " + ParentGuid + @", type: 2}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords:
  - LOCAL_A
  m_InvalidKeywords: []
  m_CustomRenderQueue: -1
  m_LockedProperties: _Color
  m_SavedProperties:
    m_TexEnvs: []
    m_Floats: []
    m_Colors:
    - _Color: {r: 0, g: 1, b: 0, a: 1}
", objects);
            child.name = "VarChild";
            objects[ChildGuid] = child;
            var grand = Read(@"Material:
  m_Name: VarGrand
  m_Shader: {fileID: 4800000, guid: " + ShaderGuid + @", type: 3}
  m_Parent: {fileID: 2100000, guid: " + ChildGuid + @", type: 2}
  m_ModifiedSerializedProperties: 16
  m_ValidKeywords:
  - LOCAL_A
  m_InvalidKeywords: []
  m_CustomRenderQueue: 2100
  m_LockedProperties:
  m_SavedProperties:
    m_TexEnvs: []
    m_Floats:
    - _Value: 0.9
    m_Colors:
    - _Color: {r: 0, g: 1, b: 0, a: 1}
", objects);
            grand.name = "VarGrand";
            return (parent, child, grand);
        }

        static void State(Material m, string overridden, string locked, string lockedByAncestor)
        {
            foreach (var p in new[] { "_Color", "_Value", "_Other", "_MainTex", "_Missing" })
            {
                Assert.True(overridden.Contains(p) == m.IsPropertyOverriden(p), m.name + " overrides " + p);
                Assert.True(locked.Contains(p) == m.IsPropertyLocked(p), m.name + " locks " + p);
                Assert.True(lockedByAncestor.Contains(p) == m.IsPropertyLockedByAncestor(p), m.name + "'s ancestor locks " + p);
            }
        }

        /// <summary>variant.parent/child/grand, variant.instance and variant.newFrom.</summary>
        [Fact]
        public void VariantsOverrideLockAndInherit()
        {
            var (parent, child, grand) = Family();
            Assert.False(parent.isVariant);
            Assert.Same(parent, child.parent);
            Assert.True(grand.IsChildOf(parent) && grand.IsChildOf(child) && !grand.IsChildOf(grand) && !parent.IsChildOf(child));
            State(parent, "", "_Other", "");
            State(child, "_Color", "_Color", "_Other");
            // Saved by the grandchild but locked by its parent: not an override.
            State(grand, "_Value", "", "_Color _Other");
            Assert.Equal(new Color(0, 1, 0, 1), grand.GetColor("_Color"));
            Assert.Equal(0.9f, grand.GetFloat("_Value"));
            Assert.Equal(0.25f, child.GetFloat("_Value"));
            Assert.Equal(2005, child.renderQueue);
            Assert.Equal(2100, grand.renderQueue);
            Assert.Equal(new[] { "LOCAL_A" }, grand.shaderKeywords);

            var instance = Object.Instantiate(child);
            Assert.Same(parent, instance.parent);
            State(instance, "_Color", "_Color", "_Other");
            Assert.Equal(2005, instance.renderQueue);
            var made = new Material(grand);
            Assert.Same(child, made.parent);
            State(made, "_Value", "", "_Color _Other");
            Assert.Equal(2100, made.renderQueue);
            Assert.Equal(0.9f, made.GetFloat("_Value"));
        }

        /// <summary>variant.revert.*: one property, then all (the queue too), a set of a property an
        /// ancestor locks takes effect, a lock does not undo it.</summary>
        [Fact]
        public void RevertsTakeTheParentsValuesAgain()
        {
            var (parent, child, grand) = Family();
            var copy = Object.Instantiate(grand);
            copy.RevertPropertyOverride("_Value");
            State(copy, "", "", "_Color _Other");
            Assert.Equal(0.25f, copy.GetFloat("_Value"));
            Assert.Equal(2100, copy.renderQueue);
            copy.RevertPropertyOverride("_Missing");
            copy.RevertAllPropertyOverrides();
            Assert.Equal(2005, copy.renderQueue);
            Assert.Equal(new Color(0, 1, 0, 1), copy.GetColor("_Color"));
            copy.SetFloat("_Other", 1.75f);
            State(copy, "", "", "_Color _Other");
            Assert.Equal(1.75f, copy.GetFloat("_Other"));
            copy.SetPropertyLock("_Other", true);
            State(copy, "", "_Other", "_Color _Other");
            Assert.Equal(1.75f, copy.GetFloat("_Other"));
        }

        /// <summary>variant.script.*: a script-made variant overrides what differs from its parent,
        /// then what it sets; without a parent it keeps its values.</summary>
        [Fact]
        public void AScriptMadeVariantOverridesWhatDiffers()
        {
            var (parent, _, _) = Family();
            var m = new Material(parent.shader) { name = "Scripted" };
            m.parent = parent;
            State(m, "_Value", "", "_Other");
            Assert.Equal(new Color(1, 0, 0, 1), m.GetColor("_Color"));
            Assert.Equal(0.5f, m.GetFloat("_Value"));
            Assert.Equal(2005, m.renderQueue);
            Assert.Empty(m.shaderKeywords);
            m.SetColor("_Color", new Color(0.25f, 0.5f, 0.75f, 1));
            State(m, "_Color _Value", "", "_Other");
            // A later change of the parent reaches what the variant does not override.
            parent.SetFloat("_Other", 1.5f);
            Assert.Equal(1.5f, m.GetFloat("_Other"));
            Assert.Equal(0.5f, m.GetFloat("_Value"));
            m.parent = null;
            Assert.False(m.isVariant);
            State(m, "", "", "");
            Assert.Equal(0.5f, m.GetFloat("_Value"));
        }

        /// <summary>A private copy of a variant that takes its parent's keywords (the canvas clip
        /// copy, a property-block copy) keeps a keyword and a value set on it when the parent changes.</summary>
        [Fact]
        public void CopiesOfVariantsKeepWhatIsSetOnThem()
        {
            var (parent, child, _) = Family();
            var variant = Read(@"Material:
  m_Name: Inheriting
  m_Shader: {fileID: 4800000, guid: " + ShaderGuid + @", type: 3}
  m_Parent: {fileID: 2100000, guid: " + ParentGuid + @", type: 2}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_CustomRenderQueue: -1
  m_LockedProperties:
  m_SavedProperties:
    m_Floats: []
", new Dictionary<string, Object> { [ShaderGuid] = parent.shader, [ParentGuid] = parent });
            Assert.Equal(new[] { "LOCAL_A" }, variant.shaderKeywords);
            var copy = new Material(variant);
            copy.EnableKeyword("UNITY_UI_CLIP_RECT");
            copy.SetFloat("_Value", 0.125f);
            parent.SetFloat("_Other", 1.25f);
            copy.Inherit();
            Assert.Contains("UNITY_UI_CLIP_RECT", copy.m_Keywords);
            Assert.Contains("LOCAL_A", copy.m_Keywords);
            Assert.Equal(0.125f, copy.GetFloat("_Value"));
            Assert.Equal(1.25f, copy.GetFloat("_Other"));
        }

        [Fact]
        public void CanvasCopiesFollowTheSourceOverridesAfterAParentChange()
        {
            var (parent, child, _) = Family();
            var go = new GameObject("variant clip", typeof(RectTransform), typeof(CanvasRenderer));
            try
            {
                var canvas = go.GetComponent<CanvasRenderer>();
                canvas.EnableRectClipping(new Rect(0, 0, 10, 10));
                var copy = canvas.CanvasMaterial(child);
                Assert.Null(copy.parent);
                child.SetFloat("_Value", 0.9f);
                Assert.Equal(0.9f, canvas.CanvasMaterial(child).GetFloat("_Value"));
                parent.SetFloat("_Value", 0.2f);
                Assert.Equal(0.9f, child.GetFloat("_Value"));
                Assert.Equal(0.9f, canvas.CanvasMaterial(child).GetFloat("_Value"));
                Assert.Contains("UNITY_UI_CLIP_RECT", copy.m_Keywords);
                Assert.DoesNotContain("UNITY_UI_CLIP_RECT", child.m_Keywords);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void CyclicVariantQueriesFailExplicitly()
        {
            var shader = Forms();
            var a = new Material(shader);
            var b = new Material(shader);
            var unrelated = new Material(shader);
            a.parent = b;
            b.parent = a;
            a.SetFloat("_Value", 0.75f);
            Assert.Throws<InvalidOperationException>(() => a.IsChildOf(unrelated));
            Assert.Throws<InvalidOperationException>(() => a.IsPropertyLockedByAncestor("_Value"));
            Assert.Throws<InvalidOperationException>(() => a.IsPropertyOverriden("_Value"));
        }
    }
}
