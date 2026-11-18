using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A SerializedObject over a Material: its saved property sheet (m_SavedProperties), keyword
    /// lists and settings, read and applied, as the Unity 6000.0.71f1 Editor answers them
    /// (tests/projects/toolapis matx.*; the Visual Effect Graph's material settings replace the
    /// float list this way). Each case changes the material's answer: a dropped property with a
    /// default that is not zero, a name the shader does not declare, a value the sheet changes.
    /// </summary>
    public class MaterialSerializedObjectTests : IDisposable
    {
        readonly List<Object> m_Owned = new List<Object>();
        readonly Dictionary<string, string> m_Spaces = new Dictionary<string, string>();
        static int s_Made;

        public MaterialSerializedObjectTests()
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

        const string NotFound = "{\"found\":false,\"subshaders_known\":false,\"keywords_known\":false,\"transient\":false,\"reason\":\"\",\"subshaders\":[],\"keywords\":[]}";

        string Call(string method, string request)
        {
            using var document = JsonDocument.Parse(request);
            return method == "shader-declarations" && m_Spaces.TryGetValue(document.RootElement.GetProperty("name").GetString(), out var s) ? s : NotFound;
        }

        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        /// <summary>A shader whose property names sort differently by ordinal, ignoring case and as
        /// declared, with every kind, and a known keyword space with _MODE_A.</summary>
        Shader Make()
        {
            var shader = Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/MaterialSerialized/S" + (++s_Made) + "\" {\n"
                + "Properties { _Zeta (\"Zeta\", Float) = 2.5 _glossy (\"g\", Float) = 1 _Glossiness (\"G\", Range(0, 1)) = 0.5 Bare (\"B\", Float) = 6"
                + " _Legacy (\"L\", Int) = 3 _Count (\"C\", Integer) = 4 _Tint (\"T\", Color) = (1, 0, 0, 1) _Dir (\"D\", Vector) = (1, 2, 3, 4)"
                + " _BaseMap (\"Base\", 2D) = \"white\" {} }\n"
                + "SubShader { Pass { } } }"));
            m_Spaces[shader.name] = "{\"found\":true,\"builtin\":false,\"subshaders_known\":true,\"keywords_known\":true,\"transient\":false,\"reason\":\"\",\"subshaders\":[],"
                + "\"keywords\":[{\"name\":\"_MODE_A\",\"local\":true,\"dynamic\":false}]}";
            return shader;
        }

        static string Names(Material m, string list)
        {
            var array = new SerializedObject(m).FindProperty("m_SavedProperties." + list);
            var names = new List<string>();
            for (int i = 0; i < array.arraySize; i++) names.Add(array.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue);
            return string.Join(" ", names);
        }

        static void SetFloats(SerializedObject so, params (string Name, float Value)[] entries)
        {
            var floats = so.FindProperty("m_SavedProperties.m_Floats");
            floats.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = floats.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("first").stringValue = entries[i].Name;
                e.FindPropertyRelative("second").floatValue = entries[i].Value;
            }
        }

        [Fact]
        public void ANewMaterialListsItsShadersPropertiesByOrdinalName()
        {
            var m = Own(new Material(Make()));
            m.SetFloat("_Undeclared", 1f);
            // Legacy Int properties are floats, Integer ones ints, Vectors colours; a script's
            // value for an undeclared name is not listed.
            Assert.Equal("Bare _Glossiness _Legacy _Zeta _glossy", Names(m, "m_Floats"));
            Assert.Equal("_Count", Names(m, "m_Ints"));
            Assert.Equal("_Dir _Tint", Names(m, "m_Colors"));
            Assert.Equal("_BaseMap", Names(m, "m_TexEnvs"));
            var so = new SerializedObject(m);
            Assert.Equal("pair", so.FindProperty("m_SavedProperties.m_Floats").arrayElementType);
            Assert.Equal("map", so.FindProperty("m_SavedProperties.m_Floats").type);
            Assert.True(so.FindProperty("m_Name").isArray);
            Assert.Equal(6f, so.FindProperty("m_SavedProperties.m_Floats").GetArrayElementAtIndex(0).FindPropertyRelative("second").floatValue);
        }

        [Fact]
        public void AReplacedFloatListIsTheMaterialsUntilAPropertyIsUsed()
        {
            var m = Own(new Material(Make()));
            m.SetFloat("_Zeta", 9f);
            m.SetFloat("_Extra", 2f);
            var so = new SerializedObject(m);
            SetFloats(so, ("_glossy", 0.25f), ("_Other", 7f));
            Assert.True(so.ApplyModifiedProperties());
            Assert.Equal("_Other _glossy", Names(m, "m_Floats"));
            // The other lists are as they were.
            Assert.Equal("_Dir _Tint", Names(m, "m_Colors"));
            Assert.Equal(0.25f, m.GetFloat("_glossy"));
            // A declared property the list left out reads its shader's default, not the value a
            // script had set; names the shader does not declare are unseen.
            Assert.Equal(2.5f, m.GetFloat("_Zeta"));
            Assert.False(m.HasFloat("_Extra"));
            Assert.False(m.HasFloat("_Other"));
            // Using a property lists every shader property again; the other names stay.
            Assert.Equal("Bare _Glossiness _Legacy _Other _Zeta _glossy", Names(m, "m_Floats"));
            // Control: a keyword change does not.
            var emptied = Own(new Material(Make()));
            so = new SerializedObject(emptied);
            SetFloats(so);
            so.ApplyModifiedProperties();
            emptied.EnableKeyword("_MODE_A");
            emptied.renderQueue = 2100;
            Assert.Equal("", Names(emptied, "m_Floats"));
            emptied.shader = emptied.shader;
            Assert.Equal("Bare _Glossiness _Legacy _Zeta _glossy", Names(emptied, "m_Floats"));
        }

        [Fact]
        public void TexturesSettingsAndKeywordsApply()
        {
            var shader = Make();
            var m = Own(new Material(shader));
            var texture = Own(new Texture2D(2, 2) { name = "Runtime" });
            m.EnableKeyword("_ZED");
            m.EnableKeyword("_MODE_A");
            var so = new SerializedObject(m);
            Assert.Equal("_MODE_A", so.FindProperty("m_ValidKeywords").GetArrayElementAtIndex(0).stringValue);
            Assert.Equal("_ZED", so.FindProperty("m_InvalidKeywords").GetArrayElementAtIndex(0).stringValue);
            var env = so.FindProperty("m_SavedProperties.m_TexEnvs").GetArrayElementAtIndex(0).FindPropertyRelative("second");
            env.FindPropertyRelative("m_Texture").objectReferenceValue = texture;
            env.FindPropertyRelative("m_Scale").vector2Value = new Vector2(2, 3);
            so.FindProperty("m_CustomRenderQueue").intValue = 2450;
            so.FindProperty("m_DoubleSidedGI").boolValue = true;
            var valid = so.FindProperty("m_ValidKeywords");
            valid.arraySize = 0;
            Assert.True(so.ApplyModifiedProperties());
            Assert.Same(texture, m.GetTexture("_BaseMap"));
            Assert.Equal(new Vector2(2, 3), m.GetTextureScale("_BaseMap"));
            Assert.Equal(2450, m.renderQueue);
            Assert.True(m.doubleSidedGI);
            Assert.False(m.IsKeywordEnabled("_MODE_A"));
            Assert.Equal(new[] { "_ZED" }, m.shaderKeywords);
            Assert.Equal(texture.GetInstanceID(),
                new SerializedObject(m).FindProperty("m_SavedProperties.m_TexEnvs.Array.data[0].second.m_Texture.m_FileID").intValue);
        }

        [Fact]
        public void ACopyKeepsTheSheetAndAShaderChangeAddsTheNewShadersProperties()
        {
            var first = Make();
            var m = Own(new Material(first));
            var so = new SerializedObject(m);
            SetFloats(so, ("_Other", 7f));
            so.ApplyModifiedProperties();
            var copy = Own(new Material(m));
            Assert.Equal("_Other", Names(copy, "m_Floats"));
            var second = Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/MaterialSerialized/Second" + (++s_Made) + "\" { Properties { _New (\"N\", Float) = 1 } SubShader { Pass { } } }"));
            m.shader = second;
            Assert.Equal("_New _Other", Names(m, "m_Floats"));
            // A new material's sheet keeps the shader it leaves too.
            var fresh = Own(new Material(first));
            fresh.shader = second;
            Assert.Equal("Bare _Glossiness _Legacy _New _Zeta _glossy", Names(fresh, "m_Floats"));
        }
    }
}
