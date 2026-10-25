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
        internal readonly Dictionary<string, string> Tags = new Dictionary<string, string>();
        /// <summary>`Dependency` lines: name → shader name.</summary>
        internal readonly Dictionary<string, string> Dependencies = new Dictionary<string, string>();
        internal readonly List<(string Name, Dictionary<string, string> Tags)> Passes = new List<(string, Dictionary<string, string>)>();

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

        /// <summary>The shader's passes (null before the renderer is up).</summary>
        internal static ShaderPassInfo Of(Shader shader)
        {
            if (shader == null) return null;
            var key = (shader.name, Shader.globalRenderPipeline);
            if (s_Cache.TryGetValue(key, out var info)) return info;
            try
            {
                // The pipeline tag may still be in the stream.
                GfxStream.Flush();
                var root = JsonDocument.Parse(Native.Call("shader-passes", shader.name)).RootElement;
                info = new ShaderPassInfo
                {
                    DrawnShader = root.GetProperty("shader").GetString() ?? "",
                    Found = root.GetProperty("found").GetBoolean(),
                };
                foreach (var tag in root.GetProperty("tags").EnumerateObject()) info.Tags[tag.Name] = tag.Value.GetString() ?? "";
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
