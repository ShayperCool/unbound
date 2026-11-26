using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The names a script sets on a material for which its shader has no property: listed after the
    /// shader's by property id, arrays as properties of their element's kind, and dropped by a copy
    /// and a save but not by a renderer's own material or a property copy (Unity 6000.0.71f1 Editor,
    /// tests/projects/materialnames).
    /// </summary>
    public sealed class MaterialScriptNamesTests : IDisposable
    {
        static int s_Made;
        readonly List<Object> m_Owned = new List<Object>();
        readonly ILogHandler m_Handler = Debug.unityLogger.logHandler;
        readonly Capture m_Logs = new Capture();
        readonly IDisposable m_Release = TestRelease.Pin(TestRelease.Measured);

        public MaterialScriptNamesTests() { Debug.unityLogger.logHandler = m_Logs; }

        public void Dispose()
        {
            m_Release.Dispose();
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

        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        Shader Names() => Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/MaterialScriptNames/S" + (++s_Made) + "\" {\nProperties { "
            + "_Color (\"Color\", Color) = (1, 1, 1, 1) _MainTex (\"Texture\", 2D) = \"white\" {} "
            + "_Glossiness (\"Smoothness\", Range(0, 1)) = 0.5 _Count (\"Count\", Integer) = 2 }\nSubShader { Pass { } } }"));

        static string Unique(string name) => name + "_" + s_Made;

        [Fact]
        public void NamesSetFromScriptAreListedAfterTheShadersByPropertyId()
        {
            // Names no built-in shader uses, declared colour first: the shader's in declaration order.
            int n = ++s_Made;
            string tint = "_Tint" + n, albedo = "_Albedo" + n, gloss = "_Gloss" + n, count = "_Count" + n;
            var shader = Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/MaterialScriptNames/Order" + n + "\" {\nProperties { "
                + tint + " (\"Tint\", Color) = (1, 1, 1, 1) " + albedo + " (\"Albedo\", 2D) = \"white\" {} "
                + gloss + " (\"Gloss\", Range(0, 1)) = 0.5 " + count + " (\"Count\", Integer) = 2 }\nSubShader { Pass { } } }"));
            var m = Own(new Material(shader));
            // The later name gets its id first: it is listed first though set last.
            string pre = Unique("_Pre"), post = Unique("_Post");
            Shader.PropertyToID(pre);
            m.SetFloat(post, 1);
            m.SetFloat(pre, 2);
            m.SetFloat(gloss, 0.25f);
            m.SetInteger(Unique("_I"), 3);
            m.SetMatrix(Unique("_M"), Matrix4x4.identity);
            string tex = Unique("_T");
            m.SetTexture(tex, null);
            string mix = Unique("_Mix");
            m.SetFloat(mix, 1);
            m.SetColor(mix, Color.gray);

            Assert.Equal(new[] { gloss, pre, post, mix }, m.GetPropertyNames(MaterialPropertyType.Float));
            Assert.Equal(new[] { count, Unique("_I") }, m.GetPropertyNames(MaterialPropertyType.Int));
            Assert.Equal(new[] { Unique("_M") }, m.GetPropertyNames(MaterialPropertyType.Matrix));
            Assert.Equal(new[] { albedo, tex }, m.GetPropertyNames(MaterialPropertyType.Texture));
            Assert.Equal(new[] { tint, albedo + "_ST", albedo + "_TexelSize", albedo + "_HDR", tex + "_ST", tex + "_TexelSize", tex + "_HDR", mix },
                m.GetPropertyNames(MaterialPropertyType.Vector));
            Assert.Empty(m_Logs.Errors);
        }

        [Fact]
        public void AnArraySetFromScriptIsAPropertyOfItsElementsKind()
        {
            var m = Own(new Material(Names()));
            string floats = Unique("_Fa"), vectors = Unique("_Va"), matrices = Unique("_Ma");
            m.SetFloatArray(floats, new[] { 1f, 2f });
            m.SetVectorArray(vectors, new[] { new Vector4(5, 6, 7, 8), Vector4.one });
            m.SetMatrixArray(matrices, new[] { Matrix4x4.Scale(new Vector3(9, 9, 9)), Matrix4x4.identity });

            Assert.True(m.HasProperty(floats));
            Assert.True(m.HasFloat(floats));
            Assert.False(m.HasColor(floats));
            Assert.Equal(1f, m.GetFloat(floats));
            Assert.True(m.HasColor(vectors));
            Assert.Equal(new Vector4(5, 6, 7, 8), m.GetVector(vectors));
            Assert.True(m.HasMatrix(matrices));
            Assert.Equal(9f, m.GetMatrix(matrices).m00);
            Assert.Contains(floats, m.GetPropertyNames(MaterialPropertyType.Float));
            Assert.Contains(vectors, m.GetPropertyNames(MaterialPropertyType.Vector));
            Assert.Contains(matrices, m.GetPropertyNames(MaterialPropertyType.Matrix));
            Assert.Empty(m_Logs.Errors);
        }

        [Fact]
        public void ATextureSetFromScriptForANameTheShaderLacksIsNotReadBack()
        {
            var m = Own(new Material(Names()) { name = "Scripted" });
            var white = Texture2D.whiteTexture;
            string tex = Unique("_T"), scaled = Unique("_Sc"), offset = Unique("_Of");
            m.SetTexture(tex, white);
            m.SetTexture("_MainTex", white);
            m.SetTextureScale(scaled, new Vector2(2, 3));
            m.SetTextureOffset(offset, new Vector2(0.5f, 0.25f));
            // A name the shader declares as a float, given a texture.
            m.SetTexture("_Glossiness", white);

            Assert.True(m.HasTexture(tex));
            Assert.True(m.HasTexture(scaled));
            Assert.True(m.HasTexture(offset));
            Assert.True(m.HasTexture("_Glossiness"));
            Assert.Equal(new Vector2(2, 3), m.GetTextureScale(scaled));
            Assert.Equal(new Vector2(0.5f, 0.25f), m.GetTextureOffset(offset));
            Assert.Same(white, m.GetTexture("_MainTex"));
            // The shader's texture, then the others by property id (_Glossiness's may come first).
            Assert.Equal(new[] { "_MainTex" }.Concat(new[] { tex, scaled, offset, "_Glossiness" }.OrderBy(Shader.PropertyToID)),
                m.GetPropertyNames(MaterialPropertyType.Texture));
            Assert.Empty(m_Logs.Errors);
            Assert.Null(m.GetTexture(tex));
            Assert.Equal($"Material 'Scripted' with Shader '{m.shader.name}' doesn't have a texture property '{tex}'", Assert.Single(m_Logs.Errors));
            Assert.Null(m.GetTexture("_Glossiness"));
            Assert.Equal(2, m_Logs.Errors.Count);
            Assert.Equal(new[] { "_MainTex" }, m.GetTexturePropertyNames());
            // Unbound's own readers draw with the slot.
            Assert.Same(white, m.SlotTexture(Shader.PropertyToID(tex)));
        }

        [Fact]
        public void ACopyDropsTheNamesTheShaderLacksAndARenderersMaterialKeepsThem()
        {
            var m = Own(new Material(Names()) { name = "Scripted" });
            string f = Unique("_F"), c = Unique("_C"), mat = Unique("_M"), arr = Unique("_A");
            m.SetFloat(f, 6);
            m.SetColor(c, Color.red);
            m.SetMatrix(mat, Matrix4x4.Scale(new Vector3(2, 2, 2)));
            m.SetFloatArray(arr, new[] { 1f });
            m.SetFloat("_Glossiness", 0.75f);

            foreach (var copy in new[] { Own(new Material(m)), Own((Material)Object.Instantiate(m)) })
            {
                Assert.False(copy.HasProperty(f));
                Assert.False(copy.HasProperty(c));
                Assert.False(copy.HasMatrix(mat));
                Assert.False(copy.HasFloat(arr));
                Assert.Null(copy.GetFloatArray(arr));
                Assert.Equal(new[] { "_Glossiness" }, copy.GetPropertyNames(MaterialPropertyType.Float));
                Assert.Equal(0.75f, copy.GetFloat("_Glossiness"));
            }
            Assert.Empty(m_Logs.Errors);

            var given = Own(new Material(Names()));
            given.CopyPropertiesFromMaterial(m);
            Assert.True(given.HasProperty(f));
            Assert.Equal(6f, given.GetFloat(f));

            var holder = Own(new GameObject("Holder"));
            var renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = m;
            var own = Own(renderer.material);
            Assert.True(own.HasProperty(f));
            Assert.Equal(6f, own.GetFloat(f));
            Assert.True(own.HasMatrix(mat));
        }

        [Fact]
        public void AWrittenMaterialKeepsOnlyTheSavedValuesOfNamesItsShaderLacks()
        {
            var names = Names();
            var other = Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/MaterialScriptNames/Other" + (++s_Made) + "\" {\nProperties { "
                + "_Saved (\"Saved\", Float) = 9 _Fresh (\"Fresh\", Float) = 8 }\nSubShader { Pass { } } }"));
            var m = Own(new Material());
            m.ReadSerialized(TestYaml.DocumentBody(@"Material:
  m_Name: Saved
  m_Shader: {fileID: 4800000, guid: ab000000000000000000000000000002, type: 3}
  m_SavedProperties:
    m_TexEnvs: []
    m_Floats:
    - _Glossiness: 0.25
    - _Saved: 7
    m_Colors: []
"), new One(names));
            m.name = "Saved";
            m.SetFloat("_Saved", 2);
            // A colour for the name the data saves as a float: not kept.
            m.SetColor("_Saved", Color.red);
            m.SetFloat("_Fresh", 3);
            m.SetMatrix("_FreshM", Matrix4x4.Scale(new Vector3(2, 3, 4)));
            m.SetFloat("_Glossiness", 0.75f);
            Assert.True(m.HasProperty("_Saved"));

            AssetFiles.Written(new Object[] { m });
            Assert.False(m.HasProperty("_Saved"));
            Assert.False(m.HasProperty("_Fresh"));
            Assert.False(m.HasMatrix("_FreshM"));
            Assert.Equal(new[] { "_Glossiness" }, m.GetPropertyNames(MaterialPropertyType.Float));
            Assert.Equal(0.75f, m.GetFloat("_Glossiness"));
            Assert.Equal(0f, m.GetFloat("_Saved"));
            Assert.Equal("Material 'Saved' with Shader '" + names.name + "' doesn't have a float or range property '_Saved'", Assert.Single(m_Logs.Errors));

            // The saved sheet's value stays as the material's data, the other is gone.
            m.shader = other;
            Assert.Equal(2f, m.GetFloat("_Saved"));
            Assert.Equal(8f, m.GetFloat("_Fresh"));
            var colour = Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/MaterialScriptNames/Colour" + (++s_Made) + "\" {\nProperties { "
                + "_Saved (\"Saved\", Color) = (0, 0, 1, 1) }\nSubShader { Pass { } } }"));
            m.shader = colour;
            Assert.Equal(new Color(0, 0, 1, 1), m.GetColor("_Saved"));
        }

        [Fact]
        public void ASerializedObjectApplyDropsMatricesAndArraysSetFromScript()
        {
            var m = Own(new Material(Names()) { name = "Applied" });
            string matrix = Unique("_Sm"), array = Unique("_Sa"), single = Unique("_Sf");
            m.SetMatrix(matrix, Matrix4x4.Scale(new Vector3(2, 2, 2)));
            m.SetFloatArray(array, new[] { 3f, 4f });
            m.SetFloat(single, 5);
            var so = new SerializedObject(m);
            so.FindProperty("m_CustomRenderQueue").intValue = 2001;
            Assert.True(so.ApplyModifiedPropertiesWithoutUndo());
            Assert.Equal(2001, m.renderQueue);
            Assert.False(m.HasMatrix(matrix));
            Assert.False(m.HasFloat(array));
            Assert.Null(m.GetFloatArray(array));
            Assert.False(m.HasFloat(single));
            Assert.Equal(new[] { "_Glossiness" }, m.GetPropertyNames(MaterialPropertyType.Float));
            Assert.Empty(m.GetPropertyNames(MaterialPropertyType.Matrix));
        }

        [Fact]
        public void ACopyKeepsTheValuesSetForNamesTheShaderDeclares()
        {
            // Values of another kind than the declared one are not measured: a copy keeps them.
            var m = Own(new Material(Names()));
            m.SetInt("_Count", 3);
            m.SetInteger("_Glossiness", 1);
            m.SetTexture("_Glossiness", Texture2D.whiteTexture);
            m.SetColor("_Color", Color.red);
            var copy = Own(new Material(m));
            Assert.Equal(3, copy.GetInteger("_Count"));
            Assert.Equal(1, copy.GetInteger("_Glossiness"));
            Assert.True(copy.HasTexture("_Glossiness"));
            Assert.Equal(Color.red, copy.GetColor("_Color"));
            // The declared float given a texture is listed among the textures, its vectors are not.
            Assert.Contains("_Glossiness", copy.GetPropertyNames(MaterialPropertyType.Texture));
            Assert.DoesNotContain("_Glossiness_ST", copy.GetPropertyNames(MaterialPropertyType.Vector));
            Assert.DoesNotContain("_Glossiness", copy.GetPropertyNames(MaterialPropertyType.Int));
            Assert.Empty(m_Logs.Errors);
        }

        [Fact]
        public void AnUnmeasuredReleaseKeepsThePreviousModel()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var m = Own(new Material(Names()));
            string f = Unique("_F"), arr = Unique("_A"), tex = Unique("_T");
            m.SetFloat(f, 6);
            m.SetFloatArray(arr, new[] { 1f });
            m.SetTexture(tex, Texture2D.whiteTexture);
            Assert.Equal(new[] { "_Glossiness" }, m.GetPropertyNames(MaterialPropertyType.Float));
            Assert.False(m.HasFloat(arr));
            Assert.Same(Texture2D.whiteTexture, m.GetTexture(tex));
            var copy = Own(new Material(m));
            Assert.Equal(6f, copy.GetFloat(f));
            AssetFiles.Written(new Object[] { m });
            Assert.Equal(6f, m.GetFloat(f));
            Assert.Empty(m_Logs.Errors);
        }

        sealed class One : IReferenceResolver
        {
            readonly Shader m_Shader;
            internal One(Shader shader) => m_Shader = shader;
            public Object Resolve(ObjectRef reference, Type expected) => reference.FileId == 4800000 ? m_Shader : null;
        }
    }
}
