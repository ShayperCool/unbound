using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A project's shader editor that does what URP's base editor does with the main
    /// colour (copies <c>_BaseColor</c> into <c>_Color</c> where the material has both) and records
    /// each validation: the material's shader and the <c>_Color</c> it left.</summary>
    sealed class ImportValidationTestGUI : ShaderGUI
    {
        internal static readonly List<(Material Material, string Shader, Color Color)> Calls = new List<(Material, string, Color)>();

        public override void ValidateMaterial(Material material)
        {
            if (material.HasProperty("_Color") && material.HasProperty("_BaseColor"))
                material.SetColor("_Color", material.GetColor("_BaseColor"));
            Calls.Add((material, material.shader.name, material.GetColor("_Color")));
        }
    }

    /// <summary>
    /// How a model's import validates its materials (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/modelimport <c>stages.*</c> with URP): with the new shader's editor each time
    /// an import step sets the material's shader, at once, before the step's next values; not when
    /// the same shader is set again, and not outside an import.
    /// </summary>
    [Collection("RenderSettings state")]
    public sealed class ImportValidationTests : IDisposable
    {
        readonly List<System.Reflection.Assembly> m_Assemblies = (List<System.Reflection.Assembly>)typeof(GameDomain)
            .GetField("s_Assemblies", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
        readonly bool m_Added;
        readonly List<Object> m_Owned = new List<Object>();

        public ImportValidationTests()
        {
            var own = typeof(ImportValidationTestGUI).Assembly;
            m_Added = !m_Assemblies.Contains(own);
            if (m_Added) m_Assemblies.Add(own);
            ImportValidationTestGUI.Calls.Clear();
        }

        public void Dispose()
        {
            if (m_Added) m_Assemblies.Remove(typeof(ImportValidationTestGUI).Assembly);
            ImportValidationTestGUI.Calls.Clear();
            foreach (var o in m_Owned) Object.DestroyImmediate(o);
        }

        Shader Make(string properties) => Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/ImportValidation " + Guid.NewGuid().ToString("N")
            + "\"\n{\n    Properties { " + properties + " }\n    SubShader { Pass { } }\n    CustomEditor \"UnityEngine.Tests.ImportValidationTestGUI\"\n}"));

        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        const string LitProperties = "_BaseColor (\"Base\", Color) = (1, 1, 1, 1) _Color (\"Color\", Color) = (1, 1, 1, 1)";
        const string GraphProperties = "_Color (\"Color\", Color) = (1, 1, 1, 1)";

        [Fact]
        public void AnImportedMaterialIsValidatedByTheNewShadersEditorAsItsShaderIsSet()
        {
            var lit = Make(LitProperties);
            var graph = Make(GraphProperties);
            var m = Own(new Material(graph));
            m.m_ValidateOnShaderChange = true;
            m.shader = lit;
            // Validated at once, before the base colour set next: _Color took the default.
            Assert.Single(ImportValidationTestGUI.Calls);
            Assert.Equal(lit.name, ImportValidationTestGUI.Calls[0].Shader);
            m.SetColor("_BaseColor", new Color(0.5f, 0.25f, 0.125f, 1f));
            Assert.Equal(Color.white, m.GetColor("_Color"));
            // The same shader again: no validation.
            m.shader = lit;
            Assert.Single(ImportValidationTestGUI.Calls);
            // Another shader: validated with it; it has no _BaseColor, so _Color stays.
            m.shader = graph;
            Assert.Equal(2, ImportValidationTestGUI.Calls.Count);
            Assert.Equal(graph.name, ImportValidationTestGUI.Calls[1].Shader);
            Assert.Equal(Color.white, m.GetColor("_Color"));
        }

        [Fact]
        public void OutsideAnImportAShaderChangeValidatesNothing()
        {
            var lit = Make(LitProperties);
            var m = Own(new Material(Make(GraphProperties)));
            m.shader = lit;
            Assert.Empty(ImportValidationTestGUI.Calls);
        }
    }
}
