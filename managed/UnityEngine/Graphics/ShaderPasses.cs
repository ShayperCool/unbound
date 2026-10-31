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
        internal readonly Dictionary<string, string> Tags = new Dictionary<string, string>();
        /// <summary>The properties marked [Gamma] (interpolated in linear space).</summary>
        internal readonly HashSet<string> Gamma = new HashSet<string>();
        // Working-space bindings from the actual selected ShaderLab source, including built-ins.
        internal HashSet<int> ColorProperties;
        /// <summary>`Dependency` lines: name → shader name.</summary>
        internal readonly Dictionary<string, string> Dependencies = new Dictionary<string, string>();
        internal readonly List<(string Name, Dictionary<string, string> Tags)> Passes = new List<(string, Dictionary<string, string>)>();

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
                var root = JsonDocument.Parse(Native.Call("shader-passes", name)).RootElement;
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
                    info.ColorProperties = new HashSet<int>();
                    foreach (var c in colors.EnumerateArray()) info.ColorProperties.Add(Shader.PropertyToID(c.GetString() ?? ""));
                }
                if (root.TryGetProperty("gamma", out var gamma))
                    foreach (var g in gamma.EnumerateArray()) info.Gamma.Add(g.GetString() ?? "");
                if (root.TryGetProperty("dependencies", out var dependencies))
                    foreach (var d in dependencies.EnumerateObject()) info.Dependencies[d.Name] = d.Value.GetString() ?? "";
                foreach (var pass in root.GetProperty("passes").EnumerateArray())
                {
                    var tags = new Dictionary<string, string>();
                    foreach (var tag in pass.GetProperty("tags").EnumerateObject()) tags[tag.Name] = tag.Value.GetString() ?? "";
                    info.Passes.Add((pass.GetProperty("name").GetString() ?? "", tags));
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
        public int passCount => Unbound.Runtime.ShaderPassInfo.Of(this)?.Passes.Count ?? 1;

        /// <summary>The shader a `Dependency` line names (null when there is none).</summary>
        public Shader GetDependency(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name");
            var info = Unbound.Runtime.ShaderPassInfo.Of(this);
            return info != null && info.Dependencies.TryGetValue(name, out var shader) ? Find(shader) : null;
        }

        public int GetPassCountInSubshader(int subshaderIndex) => subshaderIndex == 0 ? passCount : 0;

        public ShaderTagId FindPassTagValue(int passIndex, ShaderTagId tagName)
        {
            var info = Unbound.Runtime.ShaderPassInfo.Of(this);
            if (info == null || passIndex < 0 || passIndex >= info.Passes.Count) return ShaderTagId.none;
            return info.Passes[passIndex].Tags.TryGetValue(tagName.name, out var value) ? new ShaderTagId(value) : ShaderTagId.none;
        }

        public ShaderTagId FindPassTagValue(int subshaderIndex, int passIndex, ShaderTagId tagName) =>
            subshaderIndex == 0 ? FindPassTagValue(passIndex, tagName) : ShaderTagId.none;

        public ShaderTagId FindSubshaderTagValue(int subshaderIndex, ShaderTagId tagName)
        {
            var info = Unbound.Runtime.ShaderPassInfo.Of(this);
            if (info == null || subshaderIndex != 0) return ShaderTagId.none;
            return info.Tags.TryGetValue(tagName.name, out var value) ? new ShaderTagId(value) : ShaderTagId.none;
        }
    }
}
