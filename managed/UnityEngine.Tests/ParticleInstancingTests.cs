using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;
using UnityEditor;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>ParticleSystemRenderer.supportsMeshInstancing answers whether the material's active
    /// SubShader has a pass that declares procedural instancing, in any render mode; ordinary
    /// instancing, no material and unread passes answer false (Unity 6000.0.71f1,
    /// tests/projects/particlerender instancing/*).</summary>
    public class ParticleInstancingTests : IDisposable
    {
        readonly List<Object> m_Owned = new List<Object>();
        readonly Dictionary<string, string> m_Answers = new Dictionary<string, string>();
        static int s_Made;

        public ParticleInstancingTests()
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
            for (int i = m_Owned.Count - 1; i >= 0; i--)
                if (m_Owned[i]) Object.DestroyImmediate(m_Owned[i], true);
        }

        string Call(string method, string request)
        {
            using var document = JsonDocument.Parse(request);
            return m_Answers.TryGetValue(document.RootElement.GetProperty("name").GetString() ?? "", out var answer) ? answer
                : "{\"found\":false,\"subshaders_known\":false,\"keywords_known\":false,\"transient\":false,\"reason\":\"\",\"subshaders\":[],\"keywords\":[]}";
        }

        /// <summary>A material whose shader has one SubShader with passes of these procedural flags.</summary>
        Material With(params string[] procedural)
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/ParticleInstancing/S" + (++s_Made) + "\" { SubShader { Pass { } } }");
            m_Owned.Add(shader);
            var passes = new List<string>();
            foreach (var p in procedural)
                passes.Add("{\"name\":\"\",\"tags\":[],\"keyword_sets\":null,\"keyword_sets_reason\":\"\",\"keyword_sets_transient\":false,\"procedural_instancing\":" + p + "}");
            m_Answers[shader.name] = "{\"found\":true,\"builtin\":false,\"subshaders_known\":true,\"keywords_known\":false,\"transient\":false,\"reason\":\"\","
                + "\"subshaders\":[{\"tags\":[],\"lod\":0,\"runs\":true,\"passes\":[" + string.Join(",", passes) + "]}],\"keywords\":[]}";
            var material = new Material(shader);
            m_Owned.Add(material);
            return material;
        }

        bool Supports(Material material, ParticleSystemRenderMode mode)
        {
            var go = new GameObject("instancing");
            m_Owned.Add(go);
            go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            r.sharedMaterial = material;
            return r.supportsMeshInstancing;
        }

        [Fact]
        public void AProceduralInstancingPassOfTheActiveSubShaderCounts()
        {
            foreach (var mode in new[] { ParticleSystemRenderMode.Mesh, ParticleSystemRenderMode.Billboard })
            {
                Assert.True(Supports(With("false", "true"), mode));
                Assert.False(Supports(With("false", "false"), mode));
                Assert.False(Supports(With("null"), mode));
                Assert.False(Supports(null, mode));
            }
        }
    }
}
