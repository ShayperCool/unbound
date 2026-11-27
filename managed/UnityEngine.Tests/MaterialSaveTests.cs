using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    // Expected values: tests/projects/materialsave (Unity 6000.0.71f1, two identical captures).
    [Collection("Serial")]
    public sealed class MaterialSaveTests : IDisposable
    {
        readonly IDisposable m_Release = TestRelease.Pin(TestRelease.Measured);
        readonly List<Object> m_Made = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Made) if (o) Object.DestroyImmediate(o);
            m_Release.Dispose();
        }

        T Made<T>(T o) where T : Object { m_Made.Add(o); return o; }

        Shader LockedShader()
        {
            var shader = Made(Shader.Create("Tests/MaterialSave/Locked", 2000));
            shader.m_Properties.Clear();
            foreach (var name in new[] { "_MainTex", "_Other", "_Locked" })
                shader.m_Properties.Add(new ShaderPropertyInfo { Name = name, Type = ShaderPropertyType.Texture });
            shader.PropertiesChanged();
            return shader;
        }

        static YNode Body(string texEnvs) => UnityYaml.Parse(
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  m_Name: M\n  m_Shader: {fileID: 4800000, guid: a5a00000000000000000000000000003, type: 3}\n"
            + "  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs:\n" + texEnvs + "    m_Ints: []\n    m_Floats: []\n    m_Colors: []\n")[0].Body;

        static string Env(string name, string reference) =>
            $"    - {name}:\n        m_Texture: {reference}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n";

        static string Saved(Material m) => UnityYamlText.Write(new List<YTextDocument>
        {
            new YTextDocument { Header = "", ClassName = "Material", Body = MaterialTypeTree.FileBody(m, o => YText.Scalar(o == null ? "none" : "'" + o.name + "'")) },
        });

        [Fact]
        public void AShadersNonModifiableTextureIsWhatEveryMaterialHasAndSaves()
        {
            var shader = LockedShader();
            var locked = Made(new Texture2D(1, 1) { name = "Tex2" });
            var tex = Made(new Texture2D(1, 1) { name = "Tex" });
            EditorMaterialUtility.SetShaderNonModifiableDefaults(shader, new[] { "_Locked" }, new Texture[] { locked });
            // A new material starts with it (file.New).
            var made = Made(new Material(shader));
            Assert.Same(locked, made.GetTexture("_Locked"));
            // A loaded one takes it whatever its data held (file.OtherLocked).
            var loaded = Made(new Material(shader));
            loaded.ReadSerialized(Body(Env("_Locked", "{fileID: 2800000, guid: a5a00000000000000000000000000001, type: 3}")), new ShaderResolver(shader, tex));
            Assert.Same(locked, loaded.GetTexture("_Locked"));
            Assert.Contains("_Locked:\n        m_Texture: 'Tex2'", Saved(loaded));
            // A texture set from script is what it saves, GetTexture still the shader's (saved.SetLocked).
            loaded.SetTexture("_Locked", tex);
            Assert.Same(locked, loaded.GetTexture("_Locked"));
            Assert.Contains("_Locked:\n        m_Texture: 'Tex'", Saved(loaded));
            // Another property is the material's own.
            loaded.SetTexture("_MainTex", tex);
            Assert.Same(tex, loaded.GetTexture("_MainTex"));
        }

        /// <summary>Resolves the material's shader reference to <paramref name="shader"/>.</summary>
        sealed class ShaderResolver : IReferenceResolver
        {
            readonly Shader m_Shader;
            readonly Texture m_Tex;
            internal ShaderResolver(Shader shader, Texture tex) { m_Shader = shader; m_Tex = tex; }
            public Object Resolve(ObjectRef reference, Type expected) =>
                expected == typeof(Shader) ? m_Shader
                : reference.Guid == "a5a00000000000000000000000000001" && reference.FileId == 2800000 ? m_Tex : null;
        }

        [Fact]
        public void AReferenceToATextureThatDoesNotLoadIsSavedAsItWas()
        {
            var shader = LockedShader();
            var tex = Made(new Texture2D(1, 1) { name = "Tex" });
            var material = Made(new Material(shader));
            material.ReadSerialized(Body(
                Env("_MainTex", "{fileID: 2800000, guid: a5a000000000000000000000000000ff, type: 3}")
                + Env("_Other", "{fileID: 12345, guid: a5a00000000000000000000000000001, type: 3}")
                + Env("_Missing", "{fileID: 2800000, guid: a5a000000000000000000000000000fe, type: 3}")), new ShaderResolver(shader, tex));
            Assert.Null(material.GetTexture("_MainTex"));
            var saved = Saved(material);
            // file.MissingProps: a GUID no asset has and an object its file lacks; file.NoLocked._Missing.
            Assert.Contains("_MainTex:\n        m_Texture: {fileID: 2800000, guid: a5a000000000000000000000000000ff, type: 3}", saved);
            Assert.Contains("_Other:\n        m_Texture: {fileID: 12345, guid: a5a00000000000000000000000000001, type: 3}", saved);
            Assert.Contains("_Missing:\n        m_Texture: {fileID: 2800000, guid: a5a000000000000000000000000000fe, type: 3}", saved);
            // A texture set replaces it (file.Replaced), and so does none.
            material.SetTexture("_MainTex", tex);
            material.SetTexture("_Other", null);
            saved = Saved(material);
            Assert.Contains("_MainTex:\n        m_Texture: 'Tex'", saved);
            Assert.Contains("_Other:\n        m_Texture: none", saved);
            // A copy keeps the kept reference.
            Assert.Contains("_Missing:\n        m_Texture: {fileID: 2800000, guid: a5a000000000000000000000000000fe, type: 3}", Saved(Made(new Material(material))));
        }
    }
}
