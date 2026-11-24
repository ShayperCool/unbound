using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>A keyword a shader or compute shader declares (its keyword space's entry).</summary>
    internal readonly struct DeclaredKeyword
    {
        internal readonly string Name;
        internal readonly bool Local;
        internal readonly bool Dynamic;

        internal DeclaredKeyword(string name, bool local, bool dynamic)
        {
            Name = name;
            Local = local;
            Dynamic = dynamic;
        }
    }

    /// <summary>
    /// What a shader declares, as the host's provider reads it (crates/unbound-runtime
    /// shader_declarations.rs gives the answers' format): its SubShaders (then its fallbacks', as
    /// <c>Shader.subshaderCount</c> counts them) with tags as Unity stores them, and its keyword
    /// space in Unity's order. Each part is known or not: an unknown part is never an empty one.
    /// Callers use a part only when it is known and otherwise keep their name-based behaviour
    /// (the behaviour before declarations were read), so a missing or older host, an unmeasured
    /// release or an unreadable source never makes keywords invalid or keyword calls no-ops.
    /// </summary>
    internal sealed class ShaderDeclarations
    {
        internal sealed class SubShader
        {
            internal readonly Dictionary<string, string> Tags = new Dictionary<string, string>(StringComparer.Ordinal);
            internal readonly List<Dictionary<string, string>> Passes = new List<Dictionary<string, string>>();
            /// <summary>Each pass's name as declared (empty: unnamed; names are compared
            /// case-insensitively); null for a pass the host sends no name for (an older host).</summary>
            internal readonly List<string> PassNames = new List<string>();
            /// <summary>Per pass: whether its program declares procedural instancing
            /// (`#pragma instancing_options procedural:...`); null where the host does not know.</summary>
            internal readonly List<bool?> ProceduralInstancing = new List<bool?>();
            /// <summary>Each pass's variant choices, null when the host cannot read them.</summary>
            internal readonly List<string[][]> KeywordSets = new List<string[][]>();
            /// <summary>Each pass's reason for unknown choices, empty when known.</summary>
            internal readonly List<string> KeywordSetReasons = new List<string>();
            /// <summary>Its <c>LOD</c> (0: none); null when the host does not send it (an older
            /// host): Shader.passCount then keeps the renderer's pass list.</summary>
            internal int? Lod;
            /// <summary>Whether the device runs it (the host leaves out a SubShader the device
            /// runs none of the passes of; false: a stage the device does not run), null when
            /// that is not known (unmeasured stages or pragmas, or an older host that does not
            /// say).</summary>
            internal bool? Runs;
        }

        /// <summary>Whether the host knows a shader (a compute file) by the name.</summary>
        internal bool Found;
        /// <summary>Whether <see cref="SubShaders"/> is complete (own SubShaders, then the Fallback
        /// chain's, every pass resolved).</summary>
        internal bool SubShadersKnown;
        /// <summary>Whether <see cref="Keywords"/> is the complete keyword space (it may be known and
        /// empty: a compute file that declares nothing).</summary>
        internal bool KeywordsKnown;
        /// <summary>Whether something unknown may become known (an input that could not be read):
        /// the answer is kept for the current frame only and asked for again later.</summary>
        internal bool Transient;
        internal string Reason = "";
        internal readonly List<SubShader> SubShaders = new List<SubShader>();
        internal readonly List<DeclaredKeyword> Keywords = new List<DeclaredKeyword>();
        readonly Dictionary<string, int> m_Index = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The keyword of a pass's DOTS instancing variant (BatchRendererGroup draws).</summary>
        internal const string DotsInstancingKeyword = "DOTS_INSTANCING_ON";

        /// <summary>The keyword of a GPU instancing variant (`multi_compile_instancing`).</summary>
        internal const string InstancingKeyword = "INSTANCING_ON";

        /// <summary>Whether a pass of a shader offers <paramref name="keyword"/> (false for a
        /// shader whose passes are not known).</summary>
        internal static bool AnyPassOffers(Shader shader, string keyword)
        {
            if (shader == null) return false;
            var declarations = Of(shader);
            if (!declarations.SubShadersKnown) return false;
            foreach (var sub in declarations.SubShaders)
                foreach (var sets in sub.KeywordSets)
                    if (Offers(sets, keyword)) return true;
            return false;
        }

        /// <summary>Whether a pass's variant choices offer <paramref name="keyword"/> (false for
        /// unknown choices).</summary>
        internal static bool Offers(string[][] keywordSets, string keyword)
        {
            if (keywordSets == null) return false;
            foreach (var set in keywordSets)
                if (set != null && Array.IndexOf(set, keyword) >= 0) return true;
            return false;
        }

        /// <summary>A keyword's index in the space (-1: not declared, or the space is unknown).</summary>
        internal int IndexOf(string name) => KeywordsKnown && name != null && m_Index.TryGetValue(name, out var i) ? i : -1;

        /// <summary>The answer when nothing can be asked (no host yet, no name): everything unknown.</summary>
        static readonly ShaderDeclarations s_None = new ShaderDeclarations { Reason = "not asked" };

        static readonly object s_Lock = new object();
        /// <summary>Answers whose every part is known, or unknown for a reason that does not pass
        /// (an unmeasured release, a GLSL pass): kept for the session. Authored shader, compute and
        /// include edits require a restart; content hot reload is not implemented. Generated files
        /// use content-hashed paths, and previously missing shaders are asked for again.</summary>
        static readonly Dictionary<(string, string), ShaderDeclarations> s_Kept = new Dictionary<(string, string), ShaderDeclarations>();
        /// <summary>Answers that may change (transient, or no such shader yet), with the frame they
        /// were asked in: asked again at most once a frame.</summary>
        static readonly Dictionary<(string, string), (ShaderDeclarations Answer, int Frame)> s_Recent = new Dictionary<(string, string), (ShaderDeclarations, int)>();
        /// <summary>Set when the host answers neither method (an older host, or one without a
        /// provider): it will not for the rest of the session.</summary>
        static bool s_Unavailable;

        /// <summary>Stands in for the native call in tests: (method, request) → answer, or throws as
        /// the native call does.</summary>
        internal static Func<string, string, string> s_CallForTests;
        /// <summary>The frame transient answers are kept for (tests set it).</summary>
        internal static int? s_FrameForTests;

        static int Frame => s_FrameForTests ?? Time.frameCount;

        internal static void ResetForTests()
        {
            lock (s_Lock)
            {
                s_Kept.Clear();
                s_Recent.Clear();
                s_Unavailable = false;
            }
        }

        /// <summary>A shader's declarations when they were read and kept already (null otherwise):
        /// nothing is asked.</summary>
        internal static ShaderDeclarations Kept(UnityEngine.Shader shader)
        {
            if (shader == null || string.IsNullOrEmpty(shader.name)) return null;
            lock (s_Lock) return s_Kept.TryGetValue(("shader-declarations", shader.SourceName), out var kept) ? kept : null;
        }

        /// <summary>A shader's declarations (by its name).</summary>
        internal static ShaderDeclarations Of(UnityEngine.Shader shader) =>
            Get("shader-declarations", shader != null ? shader.SourceName : null, "name");

        /// <summary>A compute shader's declared keywords (by its file).</summary>
        internal static ShaderDeclarations OfCompute(ComputeShader shader) =>
            Get("compute-declarations", shader != null ? shader.m_File : null, "file");

        static ShaderDeclarations Get(string method, string name, string field)
        {
            if (string.IsNullOrEmpty(name)) return s_None;
            var key = (method, name);
            lock (s_Lock)
            {
                if (s_Kept.TryGetValue(key, out var kept)) return kept;
                if (s_Recent.TryGetValue(key, out var recent) && recent.Frame == Frame) return recent.Answer;
                if (s_Unavailable) return s_None;
            }
            if (s_CallForTests == null && !Native.IsReady) return s_None;
            string json;
            try
            {
                var request = JsonSerializer.Serialize(new Dictionary<string, string> { [field] = name });
                json = s_CallForTests != null ? s_CallForTests(method, request) : Native.Call(method, request);
            }
            catch (Exception e)
            {
                lock (s_Lock) s_Unavailable = true;
                Stubs.Ignored("ShaderDeclarations",
                    "are not available from this host (" + e.Message + "): keyword spaces, SubShaders and their tags keep name-based answers");
                return s_None;
            }
            ShaderDeclarations d;
            try { d = Parse(json); }
            catch (Exception e)
            {
                d = new ShaderDeclarations { Transient = true, Reason = "an answer could not be read: " + e.Message };
            }
            if (d.Reason.Length > 0)
                Stubs.Ignored("ShaderDeclarations: " + d.Reason,
                    d.Transient ? "(asked again later; name-based answers meanwhile)" : "(name-based answers are used)");
            bool first = false;
            lock (s_Lock)
            {
                if (d.Found && !d.Transient) first = s_Kept.TryAdd(key, d);
                else s_Recent[key] = (d, Frame);
            }
            if (first && method == "shader-declarations") ReportUndefinedQueues(name, d);
            return d;
        }

        /// <summary>The Editor's error for each `Queue` value of the shader's SubShaders (its
        /// Fallback chain's too) it does not take, once per value (Unity 6000.0.71f1 Editor,
        /// tests/projects/graphicsgaps queue.errors). The Editor logs it as the shader loads;
        /// Unbound when the shader's declarations are first asked for.</summary>
        static void ReportUndefinedQueues(string name, ShaderDeclarations d)
        {
            if (!d.SubShadersKnown) return;
            HashSet<string> reported = null;
            foreach (var sub in d.SubShaders)
            {
                var queue = Tag(sub.Tags, "Queue");
                if (queue == null || ShaderLab.QueueValue(queue) != null) continue;
                reported ??= new HashSet<string>(StringComparer.Ordinal);
                if (reported.Add(queue)) Debug.LogError(ShaderLab.UndefinedQueueMessage(UnityEngine.Shader.ShownName(name), queue));
            }
        }

        /// <summary>The Editor's error for a selected SubShader's `Queue` value it does not take
        /// when a new pipeline tag selects again, which names no shader (tests/projects/graphicsgaps
        /// queue.errors.unknown).</summary>
        internal static void ReportUndefinedQueueAfterPipelineChange(SubShader active)
        {
            var queue = active != null ? Tag(active.Tags, "Queue") : null;
            if (queue != null && ShaderLab.QueueValue(queue) == null) Debug.LogError(ShaderLab.UndefinedQueueMessage("Unknown", queue));
        }

        /// <summary>An answer; a known flag that is missing counts as unknown.</summary>
        static ShaderDeclarations Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            bool Flag(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
            string text = "";
            if (root.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String) text = reason.GetString() ?? "";
            var d = new ShaderDeclarations
            {
                Found = Flag("found"),
                SubShadersKnown = Flag("subshaders_known"),
                KeywordsKnown = Flag("keywords_known"),
                Transient = Flag("transient"),
                Reason = text,
            };
            if (d.SubShadersKnown && root.TryGetProperty("subshaders", out var subs))
                foreach (var sub in subs.EnumerateArray())
                {
                    var s = new SubShader();
                    Tags(sub.GetProperty("tags"), s.Tags);
                    // Optional (an older host sends neither): a missing LOD or `runs` is unknown.
                    if (sub.TryGetProperty("lod", out var lod) && lod.ValueKind == JsonValueKind.Number && lod.TryGetInt64(out var l) && l >= 0)
                        s.Lod = (int)Math.Min(l, int.MaxValue);
                    s.Runs = sub.TryGetProperty("runs", out var runs) && runs.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? runs.GetBoolean() : null;
                    foreach (var pass in sub.GetProperty("passes").EnumerateArray())
                    {
                        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
                        Tags(pass.GetProperty("tags"), tags);
                        s.Passes.Add(tags);
                        s.PassNames.Add(pass.TryGetProperty("name", out var passName) && passName.ValueKind == JsonValueKind.String ? passName.GetString() : null);
                        s.ProceduralInstancing.Add(pass.TryGetProperty("procedural_instancing", out var procedural)
                            && procedural.ValueKind is JsonValueKind.True or JsonValueKind.False ? procedural.GetBoolean() : null);
                        string[][] sets = null;
                        if (pass.TryGetProperty("keyword_sets", out var choices) && choices.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<string[]>();
                            foreach (var group in choices.EnumerateArray())
                            {
                                var names = new List<string>();
                                foreach (var keyword in group.EnumerateArray()) names.Add(keyword.GetString());
                                list.Add(names.ToArray());
                            }
                            sets = list.ToArray();
                        }
                        s.KeywordSets.Add(sets);
                        string why = sets == null ? "variant choices not supplied by this host" : "";
                        if (pass.TryGetProperty("keyword_sets_reason", out var choiceReason)
                            && choiceReason.ValueKind == JsonValueKind.String)
                            why = choiceReason.GetString() ?? why;
                        s.KeywordSetReasons.Add(why);
                        d.Transient |= pass.TryGetProperty("keyword_sets_transient", out var retry)
                            && retry.ValueKind == JsonValueKind.True;
                    }
                    d.SubShaders.Add(s);
                }
            if (d.KeywordsKnown && root.TryGetProperty("keywords", out var keywords))
                foreach (var k in keywords.EnumerateArray())
                {
                    var name = k.GetProperty("name").GetString() ?? "";
                    if (d.m_Index.ContainsKey(name)) continue;
                    d.m_Index[name] = d.Keywords.Count;
                    d.Keywords.Add(new DeclaredKeyword(name, k.GetProperty("local").GetBoolean(), k.GetProperty("dynamic").GetBoolean()));
                }
            return d;
        }

        static void Tags(JsonElement tags, Dictionary<string, string> into)
        {
            foreach (var pair in tags.EnumerateArray())
                into[pair[0].GetString() ?? ""] = pair[1].GetString() ?? "";
        }

        /// <summary>Tag names Unity stores upper-cased and looks up whatever their case.</summary>
        static readonly string[] s_UpperNames = { "QUEUE", "IGNOREPROJECTOR", "LIGHTMODE", "SHADOWSUPPORT" };

        /// <summary>
        /// Whether a SubShader with this <c>RenderPipeline</c> tag runs under the pipeline tag
        /// (<see cref="UnityEngine.Shader.globalRenderPipeline"/>): one without the tag, or whose
        /// tag lists no name, runs under any; otherwise a comma-separated name of the tag must
        /// equal a comma-separated name of the pipeline tag, as written (no trimming, in case). A
        /// tag name that is a render pipeline's own tag in another case reads as that pipeline's
        /// spelling (<see cref="PipelineTagSpelling"/>). Matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/graphicsgaps select.*. Allocates nothing.
        /// </summary>
        internal static bool RunsUnder(string tag, string pipeline)
        {
            if (tag == null) return true;
            bool named = false;
            var rest = tag.AsSpan();
            while (true)
            {
                int comma = rest.IndexOf(',');
                var part = comma < 0 ? rest : rest[..comma];
                if (part.Length != 0)
                {
                    named = true;
                    if (Lists(pipeline, PipelineTagSpelling(part))) return true;
                }
                if (comma < 0) return !named;
                rest = rest[(comma + 1)..];
            }
        }

        /// <summary>Whether a comma-separated list names <paramref name="name"/> as written.</summary>
        static bool Lists(string list, ReadOnlySpan<char> name)
        {
            var rest = (list ?? "").AsSpan();
            while (true)
            {
                int comma = rest.IndexOf(',');
                var part = comma < 0 ? rest : rest[..comma];
                if (part.Length != 0 && part.SequenceEqual(name)) return true;
                if (comma < 0) return false;
                rest = rest[(comma + 1)..];
            }
        }

        /// <summary>The render pipelines' own shader tags (URP's and HDRP's
        /// <c>renderPipelineShaderTag</c>). The Editor keeps one spelling per tag name, whatever
        /// the case, the first it reads; with URP installed its tag is read before a project's
        /// shaders (tests/projects/graphicsgaps SelD: a "universalpipeline" SubShader runs under
        /// "UniversalPipeline" and not under "universalpipeline"). That HDRP's, or URP's without
        /// the package, is read first is not measured.</summary>
        static readonly string[] s_PipelineTags = { "UniversalPipeline", "HDRenderPipeline" };

        static ReadOnlySpan<char> PipelineTagSpelling(ReadOnlySpan<char> name)
        {
            foreach (var tag in s_PipelineTags)
                if (name.Equals(tag, StringComparison.OrdinalIgnoreCase)) return tag;
            return name;
        }

        /// <summary>A tag's value as Unity stores it (null: none).</summary>
        internal static string Tag(Dictionary<string, string> tags, string name)
        {
            if (name == null) return null;
            if (tags.TryGetValue(name, out var v)) return v;
            var upper = name.ToUpperInvariant();
            return Array.IndexOf(s_UpperNames, upper) >= 0 && tags.TryGetValue(upper, out v) ? v : null;
        }
    }
}
