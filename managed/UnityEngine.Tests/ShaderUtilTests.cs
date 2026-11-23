using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public class ShaderUtilTests
    {
        [Fact]
        public void ShadersCanBeMadeFromSource()
        {
            var shader = ShaderUtil.CreateShaderAsset(@"Shader ""Tests/Made From Source""
{
    Properties { _Color (""Color"", Color) = (1,1,1,1) }
    SubShader { Tags { ""Queue"" = ""Transparent"" } Pass { } }
}");
            Assert.Equal("Tests/Made From Source", shader.name);
            Assert.Equal(3000, shader.renderQueue);
            Assert.Same(shader, Shader.Find("Tests/Made From Source"));
            Assert.NotNull(new Material(shader));
        }

        [Fact]
        public void PropertiesAreReadAsTheShaderAnswersThem()
        {
            var shader = ShaderUtil.CreateShaderAsset(@"Shader ""Tests/Property Reads""
{
    Properties
    {
        _Tint (""Tint"", Color) = (1,0.5,0.25,1)
        _Offset (""Offset"", Vector) = (1,2,3,4)
        [HideInInspector] _Weight (""Weight"", Float) = 2
        _Gloss (""Gloss"", Range(0.25, 4)) = 1.5
        [NonModifiableTextureData] _Ramp (""Ramp"", 3D) = ""white"" {}
        _Count (""Count"", Integer) = 3
    }
    SubShader { Pass { } }
}");
            Assert.Equal(6, ShaderUtil.GetPropertyCount(shader));
            Assert.Equal(new[] { "_Tint", "_Offset", "_Weight", "_Gloss", "_Ramp", "_Count" }, System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, 6), i => ShaderUtil.GetPropertyName(shader, i)));
            Assert.Equal("Gloss", ShaderUtil.GetPropertyDescription(shader, 3));
            // The editor's names of the property types, value for value with the runtime's.
            Assert.Equal(new[] { ShaderUtil.ShaderPropertyType.Color, ShaderUtil.ShaderPropertyType.Vector, ShaderUtil.ShaderPropertyType.Float, ShaderUtil.ShaderPropertyType.Range, ShaderUtil.ShaderPropertyType.TexEnv, ShaderUtil.ShaderPropertyType.Int },
                System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, 6), i => ShaderUtil.GetPropertyType(shader, i)));
            Assert.Equal((1.5f, 0.25f, 4f), (ShaderUtil.GetRangeLimits(shader, 3, 0), ShaderUtil.GetRangeLimits(shader, 3, 1), ShaderUtil.GetRangeLimits(shader, 3, 2)));
            Assert.Equal("defminmax should be one of 0,1,2.", Assert.Throws<System.ArgumentException>(() => ShaderUtil.GetRangeLimits(shader, 3, 3)).Message);
            Assert.Equal(UnityEngine.Rendering.TextureDimension.Tex3D, ShaderUtil.GetTexDim(shader, 4));
            Assert.Equal(new[] { false, false, true, false, false, false }, System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, 6), i => ShaderUtil.IsShaderPropertyHidden(shader, i)));
            Assert.Equal(new[] { false, false, false, false, true, false }, System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, 6), i => ShaderUtil.IsShaderPropertyNonModifiableTexureProperty(shader, i)));
            // An index the shader has not: its exception; no shader: the argument's.
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ShaderUtil.GetPropertyName(shader, 6));
            Assert.Equal("s", Assert.Throws<System.ArgumentNullException>(() => ShaderUtil.GetPropertyCount(null)).ParamName);
            Assert.Equal("s", Assert.Throws<System.ArgumentNullException>(() => ShaderUtil.GetRangeLimits(null, 0, 3)).ParamName);
        }

        [Fact]
        public void ATexturesScaleAndOffsetAreItsSTVector()
        {
            // Measured on the 6000.0 Editor.
            var shader = ShaderUtil.CreateShaderAsset(@"Shader ""Tests/Textured ST""
{
    Properties { _MainTex (""Tex"", 2D) = ""white"" {} _Color (""Color"", Color) = (1,1,1,1) }
    SubShader { Pass { } }
}");
            var m = new Material(shader);
            Assert.Equal(new Vector4(1, 1, 0, 0), m.GetVector("_MainTex_ST"));
            Assert.True(m.HasProperty("_MainTex_ST"));
            Assert.True(m.HasVector("_MainTex_ST"));
            m.mainTextureScale = new Vector2(2, 3);
            m.mainTextureOffset = new Vector2(0.5f, 0.25f);
            Assert.Equal(new Vector4(2, 3, 0.5f, 0.25f), m.GetVector("_MainTex_ST"));
            m.SetVector("_MainTex_ST", new Vector4(4, 5, 6, 7));
            Assert.Equal(new Vector2(4, 5), m.mainTextureScale);
            Assert.Equal(new Vector2(6, 7), m.mainTextureOffset);
            Assert.Equal(Vector4.zero, m.GetVector("_Nope_ST"));
            Assert.False(m.HasProperty("_Color_ST"));
        }

        [Fact]
        public void InlinePropertiesRetainDefaultsAndIgnoreCommentDeclarations()
        {
            var shader = ShaderUtil.CreateShaderAsset(@"Shader ""Tests/Inline Properties"" {
                // Properties { _Fake (""Ignored"", Float) = 9 }
                Properties { _Color (""Color } // label"", Color) = (1,0,0,1) _Value (""Value"", Float) = 0.5
                    /* _Hidden (""Ignored"", Float) = 7 */
                    _Other (""Other"", Range(0,2)) = 1 _MainTex (""Texture"", 2D) = ""white"" {} [Toggle] _Toggle (""Toggle"", Float) = 0
                } SubShader { Pass { } }
            }");
            Assert.Equal(5, shader.GetPropertyCount());
            var material = new Material(shader);
            Assert.True(material.HasFloat("_Value"));
            Assert.True(material.HasFloat("_Other"));
            Assert.True(material.HasTexture("_MainTex"));
            Assert.False(material.HasProperty("_Fake"));
            Assert.False(material.HasProperty("_Hidden"));
            Assert.Equal(new Color(1,0,0,1), material.GetColor("_Color"));
            Assert.Equal(0.5f, material.GetFloat("_Value"));
            Assert.Equal(1f, material.GetFloat("_Other"));
        }

        [Fact]
        public void BuiltinShadersDeclareNoPropertiesWithoutAPropertiesBlock()
        {
            // Canonical shaderkeywords builtin.Hidden/InternalErrorShader and
            // builtin.Hidden/Internal-StencilWrite: propertyCount 0, properties [].
            static Shader Builtin(string source)
            {
                var shader = Shader.Create("Tests/Builtin Source", 2000);
                global::Unbound.Runtime.BuiltinShaders.ApplySource(shader, source);
                return shader;
            }
            int defaults = Shader.Create("Tests/Builtin Source", 2000).GetPropertyCount();
            Assert.True(defaults > 0);

            var bare = Builtin("Shader \"Hidden/Tests/Bare\" {\n    // Properties { _Color (\"Color\", Color) = (1,1,1,1) }\n"
                + "    SubShader { Pass { ColorMask 0 ZWrite Off } }\n}");
            Assert.Equal(0, bare.GetPropertyCount());
            var material = new Material(bare);
            Assert.False(material.HasProperty("_Color"));
            Assert.False(material.HasProperty("_MainTex"));
            Assert.Empty(material.GetPropertyNames(MaterialPropertyType.Vector));

            // An empty block declares none too.
            Assert.Equal(0, Builtin("Shader \"Hidden/Tests/Empty\" { Properties { /* none */ } SubShader { Pass { } } }").GetPropertyCount());

            // A block's declarations replace the defaults, as before.
            var declared = Builtin("Shader \"Hidden/Tests/Declared\" { Properties { _Tint (\"Tint\", Color) = (1,0,0,1) } SubShader { Pass { } } }");
            Assert.Equal(1, declared.GetPropertyCount());
            Assert.Equal("_Tint", declared.GetPropertyName(0));

            // No source, or a block none of whose declarations is read: not known to be empty, the
            // defaults stay.
            Assert.Equal(defaults, Builtin(null).GetPropertyCount());
            Assert.Equal(defaults, Builtin("").GetPropertyCount());
            Assert.Equal(defaults, Builtin("Shader \"Hidden/Tests/Unread\" { Properties { ??? } SubShader { Pass { } } }").GetPropertyCount());
        }
    }
}
