using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Editor's error for a BatchRendererGroup command whose material draws a pass
    /// without a DOTS_INSTANCING_ON variant (tests/projects/brg errors, Probe/NoDots): named
    /// for the list's pass, each distinct message once, none for a pass with the variant or one
    /// whose variants are unknown.</summary>
    public sealed class BrgDotsVariantErrorTests : IDisposable
    {
        const string ShaderName = "Tests/BrgNoDots";
        readonly Shader m_Shader = ShaderUtil.CreateShaderAsset("Shader \"" + ShaderName + "\" { SubShader { Pass { } } }");
        readonly Material m_Material;
        readonly Mesh m_Mesh = new Mesh { name = "Cube" };
        readonly List<string> m_Errors = new List<string>();

        public BrgDotsVariantErrorTests()
        {
            m_Material = new Material(m_Shader) { name = ShaderName };
            ShaderPassInfo.ResetForTests();
            ShaderDeclarations.ResetForTests();
            // The renderer's passes (the declarations' tags are name/value pairs): one without the variant, one with it, one with unknown variants.
            ShaderPassInfo.s_CallForTests = name => JsonSerializer.Serialize(new
            {
                shader = name, found = true, tags = new Dictionary<string, string>(),
                passes = new[]
                {
                    new { name = "NoDotsForward", tags = new Dictionary<string, string> { ["LightMode"] = "UniversalForward" } },
                    new { name = "ShadowCaster", tags = new Dictionary<string, string> { ["LightMode"] = "ShadowCaster" } },
                    new { name = "DepthOnly", tags = new Dictionary<string, string> { ["LightMode"] = "DepthOnly" } },
                },
            });
            ShaderDeclarations.s_CallForTests = (method, request) => JsonSerializer.Serialize(new
            {
                found = true, subshaders_known = true, keywords_known = false,
                subshaders = new[]
                {
                    new
                    {
                        tags = Array.Empty<string[]>(),
                        passes = new object[]
                        {
                            new { name = "NoDotsForward", tags = Array.Empty<string[]>(), keyword_sets = new[] { new[] { "_", "FOG_LINEAR" } } },
                            new { name = "ShadowCaster", tags = Array.Empty<string[]>(), keyword_sets = new[] { new[] { "_", "DOTS_INSTANCING_ON" } } },
                            new { name = "DepthOnly", tags = Array.Empty<string[]>() },
                        },
                    },
                },
            });
            Application.logMessageReceivedThreaded += Capture;
        }

        public void Dispose()
        {
            Application.logMessageReceivedThreaded -= Capture;
            ShaderPassInfo.ResetForTests();
            ShaderDeclarations.s_CallForTests = null;
            ShaderDeclarations.ResetForTests();
            Object.DestroyImmediate(m_Material);
            Object.DestroyImmediate(m_Shader);
            Object.DestroyImmediate(m_Mesh);
        }

        void Capture(string message, string stack, LogType type)
        {
            if (type == LogType.Error) m_Errors.Add(message);
        }

        void Log(string lightMode, uint material = 3, uint mesh = 3) =>
            BatchCulling.LogMissingDotsPasses(new List<string> { lightMode }, m_Material, new BatchMaterialID { value = material }, m_Mesh,
                new BatchMeshID { value = mesh }, new BatchID { value = 2 });

        [Fact]
        public void APassWithoutTheVariantLogsTheEditorsErrorOncePerMessage()
        {
            var info = ShaderPassInfo.Of(m_Shader);
            Assert.True(info != null && info.Known, "pass info");
            Assert.True(BatchCulling.DeclaresNoDots(m_Shader, "NoDotsForward", "", out _), "declarations");
            Log("UniversalForward");
            Log("UniversalForward");
            Assert.Equal(new[]
            {
                "A BatchDrawCommand is using the pass \"NoDotsForward\" from the shader \"Tests/BrgNoDots\" which does not define a DOTS_INSTANCING_ON variant. "
                + "This is not supported when rendering with a BatchRendererGroup or anything based on it like GPU Resident Drawer or Entities Graphics.\n"
                + "MaterialID: 3 (\"Tests/BrgNoDots\"), MeshID: 3 (\"Cube\"), BatchID: 2.",
            }, m_Errors);
            // Another command's IDs make another message.
            Log("UniversalForward", mesh: 4);
            Assert.Equal(2, m_Errors.Count);
            Assert.Contains("MeshID: 4 (\"Cube\")", m_Errors[1]);
        }

        [Theory]
        // The pass declares the variant.
        [InlineData("ShadowCaster")]
        // The host gives no variant choices for the pass.
        [InlineData("DepthOnly")]
        // No pass of the list's LightMode.
        [InlineData("SRPDefaultUnlit")]
        public void OtherPassesLogNothing(string lightMode)
        {
            Log(lightMode);
            Assert.Empty(m_Errors);
        }
    }
}
