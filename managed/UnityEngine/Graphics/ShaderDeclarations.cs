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

        /// <summary>A keyword's index in the space (-1: not declared, or the space is unknown).</summary>
        internal int IndexOf(string name) => KeywordsKnown && name != null && m_Index.TryGetValue(name, out var i) ? i : -1;

        /// <summary>The answer when nothing can be asked (no host yet, no name): everything unknown.</summary>
        static readonly ShaderDeclarations s_None = new ShaderDeclarations { Reason = "not asked" };

        static readonly object s_Lock = new object();
        /// <summary>Answers whose every part is known, or unknown for a reason that does not pass
        /// (an unmeasured release, a GLSL pass): kept for the session, as the renderer keeps its
        /// reading of each shader (the host reads shader sources once a session; it picks up
        /// generated shaders when they appear, which are never answered from here before).</summary>
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

        /// <summary>A shader's declarations (by its name).</summary>
        internal static ShaderDeclarations Of(UnityEngine.Shader shader) =>
            Get("shader-declarations", shader != null ? shader.name : null, "name");

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
            lock (s_Lock)
            {
                if (d.Found && !d.Transient) s_Kept[key] = d;
                else s_Recent[key] = (d, Frame);
            }
            return d;
        }

        /// <summary>An answer; a known flag that is missing counts as unknown.</summary>
        static ShaderDeclarations Parse(string json)
        {
            var root = JsonDocument.Parse(json).RootElement;
            bool Flag(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
            var d = new ShaderDeclarations
            {
                Found = Flag("found"),
                SubShadersKnown = Flag("subshaders_known"),
                KeywordsKnown = Flag("keywords_known"),
                Transient = Flag("transient"),
                Reason = root.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : "",
            };
            if (d.SubShadersKnown && root.TryGetProperty("subshaders", out var subs))
                foreach (var sub in subs.EnumerateArray())
                {
                    var s = new SubShader();
                    Tags(sub.GetProperty("tags"), s.Tags);
                    foreach (var pass in sub.GetProperty("passes").EnumerateArray())
                    {
                        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
                        Tags(pass.GetProperty("tags"), tags);
                        s.Passes.Add(tags);
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
