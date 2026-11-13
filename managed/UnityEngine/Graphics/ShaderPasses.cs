using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>The SubShader a shader draws with under the active render pipeline (after its
    /// fallbacks), as the renderer parsed it: its tags and its passes' names and tags.</summary>
    internal sealed class ShaderPassInfo
    {
        internal string DrawnShader = "";
        internal bool Found;
        /// <summary>The drawn shader's `Fallback` (null: none): passes it lacks, such as its
        /// shadow caster, come from there.</summary>
        internal string Fallback;
        internal int ShadowCasterPass = -1;
        // The same bounded traversal serves preparation and shadow drawing, including cycles.
        const int FallbackPassLimit = 8;
        internal static Func<string, string> s_CallForTests;
        internal static void ResetForTests() { s_Cache.Clear(); s_CallForTests = null; }

        internal static Shader FindPassSource(Shader shader, Func<ShaderPassInfo, bool> matches, out ShaderPassInfo info)
        {
            info = null;
            for (int depth = 0; shader != null && depth <= FallbackPassLimit; depth++)
            {
                info = Of(shader);
                if (info == null) return null;
                if (matches(info)) return shader;
                if (!info.Found || string.IsNullOrEmpty(info.Fallback)) return null;
                shader = Shader.Find(info.Fallback);
            }
            info = null;
            return null;
        }
        internal readonly Dictionary<string, string> Tags = new Dictionary<string, string>();
        /// <summary>The properties marked [Gamma] (interpolated in linear space).</summary>
        internal readonly HashSet<string> Gamma = new HashSet<string>();
        // Working-space bindings from the actual selected ShaderLab source, including built-ins.
        internal HashSet<string> ColorProperties;
        /// <summary>`Dependency` lines: name → shader name.</summary>
        internal readonly Dictionary<string, string> Dependencies = new Dictionary<string, string>();
        internal readonly List<(string Name, Dictionary<string, string> Tags)> Passes = new List<(string, Dictionary<string, string>)>();
        /// <summary>Per pass: whether it says Lighting On.</summary>
        internal readonly List<bool> Lighting = new List<bool>();

        /// <summary>A pass's LightMode as the built-in pipeline reads it: its tag, else Always
        /// (empty for an untagged pass named like a shadow caster or meta pass, which no forward
        /// draw runs).</summary>
        internal string BuiltinLightMode(int pass)
        {
            if (Passes[pass].Tags.TryGetValue("LightMode", out var mode)) return mode.Length == 0 ? "Always" : mode;
            var name = Passes[pass].Name.ToUpperInvariant();
            return name.EndsWith("CASTER") || name.EndsWith("META") ? "" : "Always";
        }

        List<int> m_ForwardPasses;

        /// <summary>The passes the built-in forward renderer draws for a renderer, in order:
        /// Always and ForwardBase ones, Vertex ones when there are no forward ones (as the
        /// renderer's <c>forward_passes</c>).</summary>
        internal List<int> ForwardPasses
        {
            get
            {
                if (m_ForwardPasses != null) return m_ForwardPasses;
                var list = new List<int>();
                bool forward = false;
                for (int i = 0; i < Passes.Count; i++)
                {
                    var mode = BuiltinLightMode(i);
                    forward |= mode.Equals("ForwardBase", StringComparison.OrdinalIgnoreCase) || mode.Equals("ForwardAdd", StringComparison.OrdinalIgnoreCase);
                }
                for (int i = 0; i < Passes.Count; i++)
                {
                    var mode = BuiltinLightMode(i);
                    if (mode.Equals("Always", StringComparison.OrdinalIgnoreCase) || mode.Equals("ForwardBase", StringComparison.OrdinalIgnoreCase)
                        || (!forward && mode.Equals("Vertex", StringComparison.OrdinalIgnoreCase)))
                        list.Add(i);
                }
                return m_ForwardPasses = list;
            }
        }

        /// <summary>A pass's LightMode as renderer lists match it (untagged: SRPDefaultUnlit).</summary>
        internal string LightMode(int pass) =>
            Passes[pass].Tags.TryGetValue("LightMode", out var mode) ? mode : "SRPDefaultUnlit";

        /// <summary>Whether a pass has one of the LightMode tags.</summary>
        internal bool HasPassFor(List<string> lightModes)
        {
            for (int i = 0; i < Passes.Count; i++)
                if (lightModes.Contains(LightMode(i))) return true;
            return false;
        }

        static readonly Dictionary<(string Shader, string Pipeline), ShaderPassInfo> s_Cache = new Dictionary<(string, string), ShaderPassInfo>();

        /// <summary>The shader's passes under its maximum LOD (null before the renderer is up).</summary>
        internal static ShaderPassInfo Of(Shader shader) => shader == null ? null : OfName(shader.DrawnName);

        /// <summary>The passes of the shader the renderer knows by <paramref name="name"/>.</summary>
        internal static ShaderPassInfo OfName(string name)
        {
            if (name == null) return null;
            var key = (name, Shader.globalRenderPipeline);
            if (s_Cache.TryGetValue(key, out var info)) return info;
            try
            {
                // The pipeline tag may still be in the stream.
                GfxStream.Flush();
                var root = JsonDocument.Parse(s_CallForTests != null ? s_CallForTests(name) : Native.Call("shader-passes", name)).RootElement;
                info = new ShaderPassInfo
                {
                    DrawnShader = root.GetProperty("shader").GetString() ?? "",
                    Found = root.GetProperty("found").GetBoolean(),
                };
                foreach (var tag in root.GetProperty("tags").EnumerateObject()) info.Tags[tag.Name] = tag.Value.GetString() ?? "";
                if (root.TryGetProperty("fallback", out var fallback) && fallback.ValueKind == JsonValueKind.String)
                    info.Fallback = fallback.GetString();
                if (root.TryGetProperty("colors", out var colors))
                {
                    info.ColorProperties = new HashSet<string>();
                    foreach (var c in colors.EnumerateArray()) info.ColorProperties.Add(c.GetString() ?? "");
                }
                if (root.TryGetProperty("gamma", out var gamma))
                    foreach (var g in gamma.EnumerateArray()) info.Gamma.Add(g.GetString() ?? "");
                if (root.TryGetProperty("dependencies", out var dependencies))
                    foreach (var d in dependencies.EnumerateObject()) info.Dependencies[d.Name] = d.Value.GetString() ?? "";
                foreach (var pass in root.GetProperty("passes").EnumerateArray())
                {
                    var tags = new Dictionary<string, string>();
                    foreach (var tag in pass.GetProperty("tags").EnumerateObject()) tags[tag.Name] = tag.Value.GetString() ?? "";
                    if (info.ShadowCasterPass < 0 && tags.TryGetValue("LightMode", out var lightMode) && lightMode == "ShadowCaster")
                        info.ShadowCasterPass = info.Passes.Count;
                    info.Passes.Add((pass.GetProperty("name").GetString() ?? "", tags));
                    info.Lighting.Add(pass.TryGetProperty("lighting", out var lighting) && lighting.ValueKind == JsonValueKind.True);
                }
            }
            catch (Exception)
            {
                return null;
            }
            s_Cache[key] = info;
            return info;
        }
    }
}

namespace UnityEngine
{
    public sealed partial class Shader
    {
        /// <summary>The declared pass count of the SubShader the shader runs (see
        /// <see cref="DeclaredPassCount"/>); the renderer's pass list while that is not known
        /// (the behaviour before SubShader LODs were declared).</summary>
        public int passCount => DeclaredPassCount() ?? Unbound.Runtime.ShaderPassInfo.Of(this)?.Passes.Count ?? 1;

        /// <summary>
        /// The pass count of the SubShader the shader's queries answer from
        /// (<see cref="ActiveSubShader"/>), counting every declared pass (a surface program's
        /// generated ones too). Null (unknown) when that SubShader is unknown.
        /// </summary>
        int? DeclaredPassCount() =>
            ActiveSubShader("Shader.passCount", "follows the renderer's pass list")?.Passes.Count;

        /// <summary>
        /// The names of the passes <see cref="passCount"/> counts, in order: those of the
        /// SubShader the shader's queries answer from (<see cref="ActiveSubShader"/>), as
        /// declared. Null (unknown) when that SubShader is unknown, the host does not name its
        /// passes, or under a render pipeline (which declared SubShader it selects is not
        /// measured; nothing is warned about, the pipeline's own code asks every frame).
        /// </summary>
        internal List<string> ActivePassNames(string api)
        {
            if (globalRenderPipeline.Length != 0) return null;
            var sub = ActiveSubShader(api, "follows the renderer's pass list");
            if (sub == null) return null;
            foreach (var name in sub.PassNames)
                if (name == null) return null;
            return sub.PassNames;
        }

        /// <summary>
        /// The SubShader Shader.passCount and Material.GetTag without fallbacks answer from: the one the built-in pipeline picks from the declarations, the
        /// first of its own then its Fallback chain's SubShaders without a <c>RenderPipeline</c>
        /// tag whose LOD is at most this shader's limit (<see cref="maximumLOD"/>, else
        /// <see cref="globalMaximumLOD"/>; the Fallback's own limit does not apply). When none
        /// is in reach, the first SubShader (canonical tests/projects/shaderpasscount: Shader and
        /// Material counts, before and after drawing, and SubId tags of the first SubShader in the nine cells where nothing is drawn; a Fallback's SubShader when
        /// it is the one picked; an observation of that fixture, not a rule measured for every
        /// shader). Null (unknown, warned once per <paramref name="api"/> and condition) under a
        /// render pipeline, for a negative limit, a SubShader that may be limited to some
        /// devices, an unread or older-host answer, or a first SubShader the description does
        /// not cover; the caller then keeps its renderer-based answer.
        /// </summary>
        internal Unbound.Runtime.ShaderDeclarations.SubShader ActiveSubShader(string api, string keeps)
        {
            var d = Unbound.Runtime.ShaderDeclarations.Of(this);
            if (!d.SubShadersKnown || d.SubShaders.Count == 0) return null;
            if (globalRenderPipeline.Length != 0)
                return SelectionUnknown(api, keeps, "under a render pipeline", "its SubShader selection is not measured");
            int limit = EffectiveLOD;
            if (limit < 0) return SelectionUnknown(api, keeps, "under a negative maximum LOD", "it is not measured");
            foreach (var sub in d.SubShaders)
            {
                if (sub.Lod is not int lod) return SelectionUnknown(api, keeps, "without SubShader LODs", "this host does not send them");
                var pipeline = Unbound.Runtime.ShaderDeclarations.Tag(sub.Tags, "RenderPipeline");
                if (pipeline != null && pipeline.Trim().Length == 0)
                    return SelectionUnknown(api, keeps, "with an empty RenderPipeline tag", "its selection is not measured");
                if (pipeline != null || lod > limit) continue;
                if (sub.Restricted)
                    return SelectionUnknown(api, keeps, "with SubShaders limited to some devices", "their selection is not measured");
                return sub;
            }
            var first = d.SubShaders[0];
            if (first.Restricted || Unbound.Runtime.ShaderDeclarations.Tag(first.Tags, "RenderPipeline") != null)
                return SelectionUnknown(api, keeps, "with no SubShader in reach", "a first SubShader for another pipeline or device is not measured");
            return first;
        }

        /// <summary>Warns once per query and condition and gives no answer (the caller keeps its
        /// renderer-based answer).</summary>
        static Unbound.Runtime.ShaderDeclarations.SubShader SelectionUnknown(string api, string keeps, string condition, string why)
        {
            Unbound.Runtime.Stubs.Ignored(api + " " + condition, keeps + ": " + why);
            return null;
        }

        /// <summary>The shader a `Dependency` line names (null when there is none).</summary>
        public Shader GetDependency(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name");
            var info = Unbound.Runtime.ShaderPassInfo.Of(this);
            return info != null && info.Dependencies.TryGetValue(name, out var shader) ? Find(shader) : null;
        }

        /// <summary>A SubShader's pass count (canonical: -1 for an index out of range). The
        /// renderer's SubShader alone while the host cannot say (the behaviour before SubShaders
        /// were read).</summary>
        public int GetPassCountInSubshader(int subshaderIndex)
        {
            var d = Unbound.Runtime.ShaderDeclarations.Of(this);
            if (!d.SubShadersKnown) return subshaderIndex == 0 ? passCount : 0;
            return subshaderIndex >= 0 && subshaderIndex < d.SubShaders.Count ? d.SubShaders[subshaderIndex].Passes.Count : -1;
        }

        public ShaderTagId FindPassTagValue(int passIndex, ShaderTagId tagName)
        {
            var info = Unbound.Runtime.ShaderPassInfo.Of(this);
            if (info == null || passIndex < 0 || passIndex >= info.Passes.Count) return ShaderTagId.none;
            return info.Passes[passIndex].Tags.TryGetValue(tagName.name, out var value) ? new ShaderTagId(value) : ShaderTagId.none;
        }

        /// <summary>A pass's own tag (without its SubShader's) as Unity stores it: built-in light
        /// modes upper-cased (canonical *.passLightModes).</summary>
        public ShaderTagId FindPassTagValue(int subshaderIndex, int passIndex, ShaderTagId tagName)
        {
            var d = Unbound.Runtime.ShaderDeclarations.Of(this);
            if (!d.SubShadersKnown) return subshaderIndex == 0 ? FindPassTagValue(passIndex, tagName) : ShaderTagId.none;
            if (subshaderIndex < 0 || subshaderIndex >= d.SubShaders.Count) return ShaderTagId.none;
            var passes = d.SubShaders[subshaderIndex].Passes;
            if (passIndex < 0 || passIndex >= passes.Count) return ShaderTagId.none;
            var value = Unbound.Runtime.ShaderDeclarations.Tag(passes[passIndex], tagName.name);
            return value != null ? new ShaderTagId(value) : ShaderTagId.none;
        }

        /// <summary>A SubShader's tag as Unity stores it. An index out of range throws (canonical
        /// *.subshaderTag[count] and [-1]; Unity's C# checks the index before the native call and
        /// passes its text as the parameter name). Preserve ParamName; the runtime's standard
        /// exception-message formatting may differ between .NET and Mono.</summary>
        public ShaderTagId FindSubshaderTagValue(int subshaderIndex, ShaderTagId tagName)
        {
            var d = Unbound.Runtime.ShaderDeclarations.Of(this);
            if (!d.SubShadersKnown)
            {
                var info = Unbound.Runtime.ShaderPassInfo.Of(this);
                if (info == null || subshaderIndex != 0) return ShaderTagId.none;
                return info.Tags.TryGetValue(tagName.name, out var tag) ? new ShaderTagId(tag) : ShaderTagId.none;
            }
            if (subshaderIndex < 0 || subshaderIndex >= d.SubShaders.Count)
                throw new ArgumentOutOfRangeException($"Invalid subshaderIndex {subshaderIndex}. Value must be in the range [0, {d.SubShaders.Count})");
            var value = Unbound.Runtime.ShaderDeclarations.Tag(d.SubShaders[subshaderIndex].Tags, tagName.name);
            return value != null ? new ShaderTagId(value) : ShaderTagId.none;
        }
    }
}
