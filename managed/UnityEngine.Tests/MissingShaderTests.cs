using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A material whose shader reference names no shader that loads has the error shader and keeps
    /// the reference for its data (Unity 6000.0.71f1 Editor, tests/projects/missingshader).
    /// </summary>
    public class MissingShaderTests : IDisposable
    {
        const string ShaderGuid = "ab000000000000000000000000000001";
        const string MissingGuid = "ab0000000000000000000000000000ff";
        readonly List<Object> m_Owned = new List<Object>();
        readonly ILogHandler m_Handler = Debug.unityLogger.logHandler;
        readonly Capture m_Logs = new Capture();

        public MissingShaderTests() { Debug.unityLogger.logHandler = m_Logs; }

        public void Dispose()
        {
            Debug.unityLogger.logHandler = m_Handler;
            foreach (var o in m_Owned) if (o) Object.DestroyImmediate(o, true);
        }

        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Errors = new List<string>();
            public void LogException(Exception exception, Object context) => Errors.Add(exception.Message);
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error) Errors.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
            }
        }

        sealed class ByGuid : IReferenceResolver
        {
            readonly Dictionary<string, Object> m_Objects;
            internal ByGuid(Dictionary<string, Object> objects) => m_Objects = objects;
            public Object Resolve(ObjectRef reference, Type expected) =>
                reference.Guid != null && reference.FileId == 4800000 && m_Objects.TryGetValue(reference.Guid, out var o) ? o : null;
        }

        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        Shader Plain() => Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/MissingShader/Plain\" {\n"
            + "Properties { _Color (\"Color\", Color) = (1, 1, 1, 1) }\nSubShader { Tags { \"Queue\" = \"Transparent\" } Pass { } } }"));

        Material Read(string shaderReference, int queue = -1, Shader present = null)
        {
            var objects = new Dictionary<string, Object>();
            if (present != null) objects[ShaderGuid] = present;
            var m = Own(new Material());
            m.ReadSerialized(TestYaml.DocumentBody(@"Material:
  m_Name: Read
  m_Shader: " + shaderReference + @"
  m_ValidKeywords:
  - _KWA
  m_InvalidKeywords: []
  m_CustomRenderQueue: " + queue + @"
  m_SavedProperties:
    m_TexEnvs:
    - _SavedTex:
        m_Texture: {fileID: 0}
        m_Scale: {x: 2, y: 3}
        m_Offset: {x: 0, y: 0}
    m_Floats:
    - _SavedFloat: 0.25
    m_Colors:
    - _SavedColor: {r: 0.5, g: 0.25, b: 0.75, a: 1}
"), new ByGuid(objects));
            m.name = "Read";
            return m;
        }

        static Shader Error => Shader.Find("Hidden/InternalErrorShader");

        static string ShaderLine(Material m) =>
            UnityYamlText.Write(new List<YTextDocument> { new YTextDocument { Header = "--- !u!21 &2100000", ClassName = "Material",
                Body = MaterialTypeTree.FileBody(m, o => YText.Scalar(o == null ? "{fileID: 0}" : "{fileID: 46, guid: 0000000000000000f000000000000000, type: 0}")) } })
                .Split('\n').First(line => line.StartsWith("  m_Shader:", StringComparison.Ordinal));

        [Theory]
        [InlineData("{fileID: 4800000, guid: " + MissingGuid + ", type: 3}")]
        [InlineData("{fileID: -6465566751694194690, guid: " + MissingGuid + ", type: 3}")]
        [InlineData("{fileID: 4800001, guid: " + ShaderGuid + ", type: 3}")]
        [InlineData("{fileID: 99999, guid: 0000000000000000f000000000000000, type: 0}")]
        [InlineData("{fileID: 0}")]
        public void AReferenceOfNoShaderGivesTheErrorShaderAndASaveWritesItAsRead(string reference)
        {
            var m = Read(reference, present: Plain());
            Assert.NotNull(Error);
            Assert.Same(Error, m.shader);
            Assert.Equal(Error.renderQueue, m.renderQueue);
            // Saved values of names the error shader lacks are unseen, without an error.
            Assert.False(m.HasProperty("_SavedColor"));
            Assert.Equal(new Color(0, 0, 0, 0), m.GetColor("_SavedColor"));
            Assert.Equal(0f, m.GetFloat("_SavedFloat"));
            Assert.Empty(m_Logs.Errors);
            Assert.Equal(new Vector2(2, 3), m.GetTextureScale("_SavedTex"));
            Assert.Equal("  m_Shader: " + reference, ShaderLine(m));
            // A copy has the same shader and data.
            var copy = Own(new Material(m));
            Assert.Same(Error, copy.shader);
            Assert.Equal("  m_Shader: " + reference, ShaderLine(copy));
        }

        [Fact]
        public void AnEditThroughASerializedObjectKeepsTheReference()
        {
            string reference = "{fileID: 4800000, guid: " + MissingGuid + ", type: 3}";
            var m = Read(reference);
            var so = new SerializedObject(m);
            so.FindProperty("m_CustomRenderQueue").intValue = 2450;
            Assert.True(so.ApplyModifiedProperties());
            Assert.Equal(2450, m.rawRenderQueue);
            Assert.Same(Error, m.shader);
            Assert.Equal("  m_Shader: " + reference, ShaderLine(m));
        }

        [Fact]
        public void AResolvedShaderIsWrittenAsItsOwnReference()
        {
            var plain = Plain();
            var m = Read("{fileID: 4800000, guid: " + ShaderGuid + ", type: 3}", present: plain);
            Assert.Same(plain, m.shader);
            Assert.Null(m.UnresolvedShaderReference);
            Assert.Equal(3000, m.renderQueue);
            Assert.Equal("  m_Shader: {fileID: 46, guid: 0000000000000000f000000000000000, type: 0}", ShaderLine(m));
            // A getter of a name the shader lacks logs its error under any other shader.
            Assert.Equal(0f, m.GetFloat("_SavedFloat"));
            Assert.Contains("Material 'Read' with Shader 'Tests/MissingShader/Plain' doesn't have a float or range property '_SavedFloat'", m_Logs.Errors);
        }

        [Fact]
        public void AMaterialWithTheErrorShaderKeepsItsQueueUntilItsShaderChanges()
        {
            var plain = Plain();
            var m = Read("{fileID: 4800000, guid: " + MissingGuid + ", type: 3}", queue: 2450);
            Assert.Same(Error, m.shader);
            Assert.Equal(2450, m.renderQueue);
            m.shader = plain;
            Assert.Same(plain, m.shader);
            Assert.Equal(-1, m.rawRenderQueue);
            Assert.Equal(3000, m.renderQueue);
            // The data then names the new shader.
            Assert.Null(m.UnresolvedShaderReference);
        }

        [Fact]
        public void CopyingPropertiesKeepsEachMaterialsOwnReference()
        {
            string reference = "{fileID: 4800000, guid: " + MissingGuid + ", type: 3}";
            var plain = Plain();
            var missing = Read(reference);
            var present = Read("{fileID: 4800000, guid: " + ShaderGuid + ", type: 3}", present: plain);
            var fromMissing = Own(new Material(plain));
            fromMissing.CopyPropertiesFromMaterial(missing);
            Assert.Same(plain, fromMissing.shader);
            Assert.Null(fromMissing.UnresolvedShaderReference);
            var intoMissing = Read(reference);
            intoMissing.CopyPropertiesFromMaterial(present);
            Assert.Same(Error, intoMissing.shader);
            Assert.Equal("  m_Shader: " + reference, ShaderLine(intoMissing));
        }

        [Fact]
        public void AssigningTheShaderItHasNamesTheErrorShaderInItsData()
        {
            var m = Read("{fileID: 4800000, guid: " + MissingGuid + ", type: 3}");
            m.shader = m.shader;
            Assert.Same(Error, m.shader);
            Assert.Null(m.UnresolvedShaderReference);
        }

        [Fact]
        public void NoShaderAssignedGivesTheErrorShaderAndDataOfNoShader()
        {
            var m = Own(new Material(Plain()));
            m.shader = null;
            Assert.Same(Error, m.shader);
            Assert.Equal("  m_Shader: {fileID: 0}", ShaderLine(m));
        }
    }
}
