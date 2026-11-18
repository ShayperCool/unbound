using System;
using System.Collections.Generic;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// What a material has of the values set from script once its shader changes (Unity
    /// 6000.0.71f1, tests/projects/materials scripted.*): the new shader's names only, so a name
    /// set under the first shader that the second lacks is unseen; back under a shader declaring
    /// it the value is there again, and a name neither declares stays unseen until it is set
    /// again. A model's material description preprocessors depend on it: one sets the surface
    /// type of URP's Lit, a later one gives the material a Shader Graph without it, and the
    /// graph's editor then reads no surface type (tests/projects/modelimport).
    /// </summary>
    public sealed class MaterialShaderChangeTests : IDisposable
    {
        static int s_Made;
        readonly List<Object> m_Owned = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Owned) Object.DestroyImmediate(o);
        }

        Shader Make(string properties) =>
            Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/MaterialShaderChange/S" + (++s_Made) + "\" {\nProperties { "
                + properties + " }\nSubShader { Pass { } } }"));

        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        [Fact]
        public void NamesSetFromScriptFollowTheShader()
        {
            var first = Make("_BaseColor (\"Base\", Color) = (1, 1, 1, 1) _Gloss (\"Gloss\", Float) = 0.5");
            var second = Make("_Color (\"Color\", Color) = (1, 1, 1, 1) _Gloss (\"Gloss\", Float) = 0.125");
            var m = Own(new Material(first));
            m.SetColor("_BaseColor", new Color(0.25f, 0.5f, 0.75f, 1f));
            m.SetFloat("_New", 3f);
            m.SetFloat("_Gloss", 0.375f);
            Assert.True(m.HasProperty("_New"));

            m.shader = second;
            Assert.False(m.HasProperty("_BaseColor"));
            Assert.False(m.HasColor("_BaseColor"));
            Assert.False(m.HasProperty("_New"));
            Assert.False(m.HasFloat("_New"));
            Assert.True(m.HasProperty("_Gloss"));
            Assert.Equal(0.375f, m.GetFloat("_Gloss"));

            m.shader = first;
            Assert.True(m.HasProperty("_BaseColor"));
            Assert.Equal(new Color(0.25f, 0.5f, 0.75f, 1f), m.GetColor("_BaseColor"));
            Assert.False(m.HasProperty("_New"));

            m.shader = second;
            m.SetFloat("_New", 5f);
            Assert.True(m.HasProperty("_New"));
            Assert.Equal(5f, m.GetFloat("_New"));
        }

        /// <summary>Assigning the shader it has is no change: what was set stays seen.</summary>
        [Fact]
        public void TheSameShaderKeepsTheNames()
        {
            var shader = Make("_Gloss (\"Gloss\", Float) = 0.5");
            var m = Own(new Material(shader));
            m.SetFloat("_New", 3f);
            m.shader = shader;
            Assert.True(m.HasProperty("_New"));
            Assert.Equal(3f, m.GetFloat("_New"));
        }

        /// <summary>A surface type set under a shader declaring it is not read under one that
        /// does not (the model import order: URP's description preprocessor, then a Shader
        /// Graph's).</summary>
        [Fact]
        public void ASurfaceTypeOfTheFormerShaderIsNotRead()
        {
            var lit = Make("_Surface (\"Surface\", Float) = 0 _BaseColor (\"Base\", Color) = (1, 1, 1, 1)");
            var graph = Make("_BASE_COLOR (\"Base\", Color) = (1, 1, 1, 1) _QueueControl (\"Queue\", Float) = -1");
            var m = Own(new Material(lit));
            m.SetFloat("_Surface", 0f);
            m.shader = graph;
            Assert.False(m.HasProperty("_Surface"));
            Assert.True(m.HasProperty("_QueueControl"));
        }
    }
}
