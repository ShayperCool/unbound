using System;
using System.IO;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.AssetImporters;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Shaders an importer generates (Shader Graph, Visual Effect Graph outputs) are kept
    /// in Library/Unbound/GeneratedShaders and known to the renderer by their ShaderLab name with
    /// their source's hash: Unity draws each material with its own shader asset, also when two
    /// generated shaders share a name (graphs of one file name in different folders, outputs of
    /// one system named alike, a graph's earlier versions left in the folder). Scripts still see
    /// the ShaderLab name.</summary>
    public sealed class GeneratedShadersTests : IDisposable
    {
        readonly string m_Project = Path.Combine(Path.GetTempPath(), "unbound-generated-shaders-" + Guid.NewGuid().ToString("N"));
        readonly string m_Previous = ProjectInfo.ProjectPath;

        public GeneratedShadersTests()
        {
            Directory.CreateDirectory(m_Project);
            ProjectInfo.ProjectPath = m_Project;
        }

        public void Dispose()
        {
            ProjectInfo.ProjectPath = m_Previous;
            try { Directory.Delete(m_Project, true); } catch (IOException) { }
        }

        static string Source(string body) => "Shader \"Hidden/VFX/Twin/System/Output Particle|Unlit Quad\" { SubShader { Pass { " + body + " } } }";

        [Fact]
        public void TwoGeneratedShadersOfOneNameAreKnownToTheRendererApart()
        {
            var a = ShaderUtil.CreateShaderAsset(new AssetImportContext("Assets/A/Twin.vfx"), Source("Blend One One"), false);
            var b = ShaderUtil.CreateShaderAsset(new AssetImportContext("Assets/B/Twin.vfx"), Source("ZWrite On"), false);
            try
            {
                Assert.Equal("Hidden/VFX/Twin/System/Output Particle|Unlit Quad", a.name);
                Assert.Equal(a.name, b.name);
                Assert.NotEqual(a.DrawnName, b.DrawnName);
                Assert.StartsWith(a.name + "#", a.DrawnName);
                // Each file declares the name the renderer knows its shader by.
                var files = Directory.GetFiles(Path.Combine(m_Project, "Library", "Unbound", "GeneratedShaders"), "*.shader");
                Assert.Equal(2, files.Length);
                foreach (var shader in new[] { a, b })
                    Assert.Single(files, f => ShaderLab.Name(File.ReadAllText(f)) == shader.SourceName);
                // The same source of the same asset again: the same key and file.
                var again = ShaderUtil.CreateShaderAsset(new AssetImportContext("Assets/A/Twin.vfx"), Source("Blend One One"), false);
                Assert.Equal(a.DrawnName, again.DrawnName);
                Object.DestroyImmediate(again);
                Assert.Equal(2, Directory.GetFiles(Path.Combine(m_Project, "Library", "Unbound", "GeneratedShaders"), "*.shader").Length);
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Fact]
        public void AShaderMadeFromSourceAloneIsKnownByItsName()
        {
            var shader = ShaderUtil.CreateShaderAsset(Source(""), false);
            try { Assert.Equal(shader.name, shader.DrawnName); }
            finally { Object.DestroyImmediate(shader); }
        }

        [Fact]
        public void AnUpdatedGeneratedShaderTakesItsNewSourcesKey()
        {
            var context = new AssetImportContext("Assets/A/Twin.vfx");
            var shader = ShaderUtil.CreateShaderAsset(context, Source("Blend One One"), false);
            try
            {
                string before = shader.DrawnName;
                ShaderUtil.UpdateShaderAsset(context, shader, Source("ZWrite Off"), false);
                Assert.NotEqual(before, shader.DrawnName);
                Assert.StartsWith(shader.name + "#", shader.DrawnName);
            }
            finally { Object.DestroyImmediate(shader); }
        }
    }
}
