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
            ShaderPassInfo.ResetForTests();
            Shader.globalMaximumLOD = int.MaxValue;
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
        public void AGeneratedShaderHiddenByTheMaximumLodIsAskedForByItsKey()
        {
            // Its SubShaders all above the maximum LOD: hidden (not drawn), which the renderer
            // tells for the name it knows the shader by.
            var shader = ShaderUtil.CreateShaderAsset(new AssetImportContext("Assets/A/Twin.vfx"), Source(""), false);
            try
            {
                var asked = new System.Collections.Generic.List<string>();
                ShaderPassInfo.s_CallForTests = name =>
                {
                    asked.Add(name);
                    bool found = name == shader.SourceName;
                    return System.Text.Json.JsonSerializer.Serialize(new { shader = name, found, tags = new System.Collections.Generic.Dictionary<string, string>(), passes = Array.Empty<object>() });
                };
                Shader.globalMaximumLOD = 200;
                Assert.True(shader.IsLodHidden);
                Assert.Contains(shader.SourceName, asked);
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [Fact]
        public void AGeneratedSourceWithoutADeclarationOrContextIsWritten()
        {
            // No Shader declaration: no key, known by its (empty) name as before.
            var plain = ShaderUtil.CreateShaderAsset(new AssetImportContext("Assets/A/Odd.vfx"), "SubShader { Pass { } }", false);
            // No import context: the asset path is empty, the key is still made.
            var bare = ShaderUtil.CreateShaderAsset(null, Source(""), false);
            try
            {
                Assert.Null(plain.m_SourceKey);
                Assert.StartsWith(bare.name + "#", bare.DrawnName);
            }
            finally
            {
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(bare);
            }
        }

        [Fact]
        public void AProjectShadersContentKeyIsTheNativeSides()
        {
            // FNV-1a 64 of the file's bytes, as crates/unbound-app's content_key computes it.
            Assert.Equal("N#cbf29ce484222325", Shader.ContentKey("N", Array.Empty<byte>()));
            Assert.Equal("N#af63dc4c8601ec8c", Shader.ContentKey("N", new[] { (byte)'a' }));
            Assert.Equal("Unbound/Dup", Shader.ShownName(Shader.ContentKey("Unbound/Dup", new byte[] { 1, 2, 3 })));
        }

        [Fact]
        public void AShownNameLeavesOutTheKey()
        {
            Assert.Equal("Hidden/VFX/T/S/Output Particle|Unlit Quad", Shader.ShownName("Hidden/VFX/T/S/Output Particle|Unlit Quad#0123456789abcdef"));
            Assert.Equal("A#0123456789abcdeg", Shader.ShownName("A#0123456789abcdeg"));
            Assert.Equal("Plain", Shader.ShownName("Plain"));
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
                var material = new Material(shader);
                var sent = typeof(Material).GetProperty("m_SentShader", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                try
                {
                    material.Sync();
                    string before = shader.DrawnName;
                    Assert.Equal(before, (string)sent.GetValue(material));
                    ShaderUtil.UpdateShaderAsset(context, shader, Source("ZWrite Off"), false);
                    Assert.NotEqual(before, shader.DrawnName);
                    Assert.StartsWith(shader.name + "#", shader.DrawnName);
                    // A material sends the new name with its next sync.
                    material.Sync();
                    Assert.Equal(shader.DrawnName, (string)sent.GetValue(material));
                    // Updated without a generated file: known by its name only.
                    ShaderUtil.UpdateShaderAsset(shader, Source("Cull Off"), false);
                    Assert.Equal(shader.name, shader.DrawnName);
                }
                finally { Object.DestroyImmediate(material); }
            }
            finally { Object.DestroyImmediate(shader); }
        }
    }
}
