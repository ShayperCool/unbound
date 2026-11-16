using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Editor's validation of a loaded material with its shader's editor
    /// (ShaderGUI.ValidateMaterial; the Standard shaders' as tests/projects/materialvalidation
    /// measures it), and what happens without a usable editor.</summary>
    public class MaterialValidationTests
    {
        sealed class Logs : ILogHandler
        {
            internal readonly List<(LogType Type, string Message)> Messages = new List<(LogType, string)>();
            public void LogException(Exception exception, Object context) => Messages.Add((LogType.Exception, exception.Message));
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Messages.Add((logType, args.Length == 0 ? format : string.Format(format, args)));
        }

        static Logs Validate(Material material)
        {
            var logs = new Logs();
            var handler = Debug.unityLogger.logHandler;
            try
            {
                Debug.unityLogger.logHandler = logs;
                MaterialValidation.Validate(material);
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
            }
            return logs;
        }

        /// <summary>A shader with the Standard shader's properties the validation reads, validated by
        /// the Standard shaders' editor.</summary>
        static Material Standard() => new Material(ShaderUtil.CreateShaderAsset(
            "Shader \"Tests/Standard Validation " + Guid.NewGuid().ToString("N") + "\"\n{\n    Properties\n    {\n"
            + "        _Mode (\"Mode\", Float) = 0\n        _SrcBlend (\"Src\", Float) = 1\n        _DstBlend (\"Dst\", Float) = 0\n"
            + "        _ZWrite (\"ZWrite\", Float) = 1\n        _EmissionColor (\"Emission\", Color) = (0,0,0,1)\n"
            + "        _MainTex (\"Albedo\", 2D) = \"white\" {}\n        _BumpMap (\"Normal\", 2D) = \"bump\" {}\n"
            + "        _DetailAlbedoMap (\"Detail\", 2D) = \"grey\" {}\n        _DetailNormalMap (\"Detail Normal\", 2D) = \"bump\" {}\n"
            + "        _ParallaxMap (\"Height\", 2D) = \"black\" {}\n        _MetallicGlossMap (\"Metallic Map\", 2D) = \"white\" {}\n"
            + "        _Metallic (\"Metallic\", Range(0,1)) = 0\n        _SmoothnessTextureChannel (\"Channel\", Float) = 0\n"
            + "    }\n    SubShader { Pass { } }\n    CustomEditor \"StandardShaderGUI\"\n}"));

        static string Keywords(Material m)
        {
            var keywords = new List<string>(m.shaderKeywords);
            keywords.Sort(StringComparer.Ordinal);
            return string.Join(" ", keywords);
        }

        [Fact]
        public void AStandardCutoutBelowItsRangeTakesTheModesStatesAndSaysSo()
        {
            var m = Standard();
            m.SetFloat("_Mode", 1);
            m.renderQueue = 2000;
            var logs = Validate(m);
            Assert.Equal("_ALPHATEST_ON", Keywords(m));
            Assert.Equal(2450, m.renderQueue);
            Assert.Equal("TransparentCutout", m.GetTag("RenderType", false));
            Assert.Equal(new[] { (LogType.Log, "Render queue value outside of the allowed range (2450 - 2500) for selected Blend mode, resetting render queue to default") },
                logs.Messages);
            // Control: validating again changes nothing and logs nothing (the queue is in range).
            Assert.Empty(Validate(m).Messages);
            Assert.Equal(2450, m.renderQueue);
        }

        [Fact]
        public void EmissionFollowsTheColourAndGIFlags()
        {
            var lit = Standard();
            lit.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.25f));
            lit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            Validate(lit);
            Assert.True(lit.IsKeywordEnabled("_EMISSION"));
            Assert.Equal(MaterialGlobalIlluminationFlags.RealtimeEmissive, lit.globalIlluminationFlags);

            // Black baked emission is marked black and loses the keyword.
            var black = Standard();
            black.SetColor("_EmissionColor", Color.black);
            black.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            black.EnableKeyword("_EMISSION");
            Validate(black);
            Assert.False(black.IsKeywordEnabled("_EMISSION"));
            Assert.Equal(MaterialGlobalIlluminationFlags.BakedEmissive | MaterialGlobalIlluminationFlags.EmissiveIsBlack,
                black.globalIlluminationFlags);
        }

        [Fact]
        public void ABlendModeOutsideTheEnumChangesNoStatesOrQueue()
        {
            var m = Standard();
            m.SetFloat("_Mode", 7);
            m.SetFloat("_SrcBlend", 5);
            m.renderQueue = 4500;
            m.EnableKeyword("_ALPHABLEND_ON");
            var logs = Validate(m);
            Assert.Equal(5f, m.GetFloat("_SrcBlend"));
            Assert.Equal(4500, m.renderQueue);
            Assert.True(m.IsKeywordEnabled("_ALPHABLEND_ON"));
            Assert.Empty(logs.Messages);
        }

        [Fact]
        public void WithoutAUsableEditorTheMaterialIsLeftAsItIs()
        {
            static Material Made(string editor)
            {
                var source = "Shader \"Tests/Validation " + Guid.NewGuid().ToString("N") + "\"\n{\n    Properties { _Mode (\"Mode\", Float) = 1 }\n    SubShader { Pass { } }\n"
                    + (editor != null ? "    CustomEditor \"" + editor + "\"\n" : "") + "}";
                var m = new Material(ShaderUtil.CreateShaderAsset(source));
                m.EnableKeyword("PROBE_OTHER");
                m.renderQueue = 2600;
                return m;
            }
            // No editor, and a name no ShaderGUI class has: nothing happens, nothing is said.
            foreach (var editor in new[] { null, "Tests.NoSuchEditor" })
            {
                var m = Made(editor);
                Assert.Empty(Validate(m).Messages);
                Assert.Equal("PROBE_OTHER", Keywords(m));
                Assert.Equal(2600, m.renderQueue);
            }
            // An editor of the Editor's that Unbound only stubs: left as it is, said once.
            var stubbed = Made("StandardParticlesShaderGUI");
            var first = Validate(stubbed);
            Assert.Contains(first.Messages, l => l.Type == LogType.Warning
                && l.Message.Contains("UnityEditor.StandardParticlesShaderGUI.ValidateMaterial is not implemented"));
            Assert.Equal("PROBE_OTHER", Keywords(stubbed));
            Assert.Empty(Validate(Made("StandardParticlesShaderGUI")).Messages);
        }
    }
}
