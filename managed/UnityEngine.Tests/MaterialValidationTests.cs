using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A project's shader editor (outside Unbound's UnityEditor).</summary>
    sealed class ValidationTestProjectGUI : ShaderGUI
    {
        public override void ValidateMaterial(Material material) => material.EnableKeyword("PROJECT_VALIDATED");
    }

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

        static Logs Validate(Material material) => CaptureLogs(() => MaterialValidation.Validate(material));

        static Logs CaptureLogs(Action action)
        {
            var logs = new Logs();
            var handler = Debug.unityLogger.logHandler;
            try
            {
                Debug.unityLogger.logHandler = logs;
                action();
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
            }
            return logs;
        }

        /// <summary>A shader with the Standard shader's properties the validation reads, validated by
        /// the Standard shaders' editor.</summary>
        static Material Standard(string editor = "StandardShaderGUI") => new Material(ShaderUtil.CreateShaderAsset(
            "Shader \"Tests/Standard Validation " + Guid.NewGuid().ToString("N") + "\"\n{\n    Properties\n    {\n"
            + "        _Mode (\"Mode\", Float) = 0\n        _SrcBlend (\"Src\", Float) = 1\n        _DstBlend (\"Dst\", Float) = 0\n"
            + "        _ZWrite (\"ZWrite\", Float) = 1\n        _EmissionColor (\"Emission\", Color) = (0,0,0,1)\n"
            + "        _MainTex (\"Albedo\", 2D) = \"white\" {}\n        _BumpMap (\"Normal\", 2D) = \"bump\" {}\n"
            + "        _DetailAlbedoMap (\"Detail\", 2D) = \"grey\" {}\n        _DetailNormalMap (\"Detail Normal\", 2D) = \"bump\" {}\n"
            + "        _ParallaxMap (\"Height\", 2D) = \"black\" {}\n        _MetallicGlossMap (\"Metallic Map\", 2D) = \"white\" {}\n"
            + "        _Metallic (\"Metallic\", Range(0,1)) = 0\n        _SmoothnessTextureChannel (\"Channel\", Float) = 0\n"
            + "    }\n    SubShader { Pass { } }\n    CustomEditor \"" + editor + "\"\n}"));

        static string Keywords(Material m)
        {
            var keywords = new List<string>(m.shaderKeywords);
            keywords.Sort(StringComparer.Ordinal);
            return string.Join(" ", keywords);
        }

        static Material ProjectEdited() => new Material(ShaderUtil.CreateShaderAsset(
            "Shader \"Tests/Project Validation " + Guid.NewGuid().ToString("N") + "\"\n{\n    Properties\n    {\n        _Value (\"Value\", Float) = 0\n        _Tint (\"Tint\", Color) = (1,1,1,1)\n    }\n"
            + "    SubShader { Pass { } }\n    CustomEditor \"UnityEngine.Tests.ValidationTestProjectGUI\"\n}"));

        /// <summary>While a project's OnPostprocessMaterial has a model's material, the first
        /// property value access validates it with its shader's editor, once; reading its shader
        /// or keywords does not (tests/projects/modelimport URPHookRecord; HasProperty, GetFloat
        /// and GetColor measured; writes are not). Without the flag nothing is validated (the
        /// control).</summary>
        [Theory]
        [InlineData("has")]
        [InlineData("float")]
        [InlineData("color")]
        public void APropertyAccessDuringAMaterialPostprocessorValidates(string access)
        {
            var assemblies = (List<System.Reflection.Assembly>)typeof(GameDomain)
                .GetField("s_Assemblies", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            var own = typeof(ValidationTestProjectGUI).Assembly;
            bool added = !assemblies.Contains(own);
            if (added) assemblies.Add(own);
            try { AccessValidates(access); }
            finally { if (added) assemblies.Remove(own); }
        }

        static void AccessValidates(string access)
        {
            var m = ProjectEdited();
            m.m_ValidateOnPropertyAccess = true;
            _ = m.shader;
            _ = m.shaderKeywords;
            Assert.Equal("", Keywords(m));
            if (access == "has") m.HasProperty("_Value");
            else if (access == "float") m.GetFloat("_Value");
            else m.GetColor("_Tint");
            Assert.Equal("PROJECT_VALIDATED", Keywords(m));
            Assert.False(m.m_ValidateOnPropertyAccess);
            m.DisableKeyword("PROJECT_VALIDATED");
            m.GetFloat("_Value");
            Assert.Equal("", Keywords(m));

            var control = ProjectEdited();
            control.GetFloat("_Value");
            control.HasProperty("_Value");
            Assert.Equal("", Keywords(control));
        }

        [Theory]
        [InlineData("StandardShaderGUI")]
        [InlineData("AutodeskInteractiveShaderGUI")]
        public void ALoadedMaterialKeepsItsSavedMetadataUntilAPropertyRead(string editor)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var material = Standard(editor);
            material.SetFloat("_Mode", 1);
            material.SetFloat("_DstBlend", 10);
            material.renderQueue = 2999;
            material.SetOverrideTag("RenderType", "Opaque");
            var file = new LoadedFile { MainObject = material };
            file.Objects[1] = material;

            Assert.Empty(CaptureLogs(() => MaterialValidation.ValidateLoaded(file)).Messages);
            Assert.Equal(2999, material.rawRenderQueue);
            Assert.Equal("Opaque", material.GetTag("RenderType", false));
            Assert.Empty(material.shaderKeywords);

            Assert.Equal(0f, material.GetFloat("_DstBlend"));
            Assert.Equal(2450, material.rawRenderQueue);
            Assert.Equal("TransparentCutout", material.GetTag("RenderType", false));
            Assert.Equal(new[] { "_ALPHATEST_ON" }, material.shaderKeywords);
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
            // First use of an imported material reaches its unsupported editor and reports once.
            using var release = TestRelease.Pin(TestRelease.Measured);
            var stubbed = Made("StandardParticlesShaderGUI");
            var metadata = CaptureLogs(() =>
            {
                MaterialValidation.DeferImportedEditor(stubbed);
                Assert.Equal(2600, stubbed.rawRenderQueue);
            });
            Assert.Empty(metadata.Messages);
            var first = CaptureLogs(() => Assert.Equal(1f, stubbed.GetFloat("_Mode")));
            Assert.Contains(first.Messages, l => l.Type == LogType.Warning
                && l.Message.Contains("UnityEditor.StandardParticlesShaderGUI.ValidateMaterial is not implemented"));
            Assert.Equal("PROBE_OTHER", Keywords(stubbed));
            Assert.Empty(Validate(Made("StandardParticlesShaderGUI")).Messages);
            // One that has no validation of its own (Unity's does nothing): nothing is said or made.
            var plain = Made("SkyboxProceduralShaderGUI");
            Assert.Empty(Validate(plain).Messages);
            Assert.Equal("PROBE_OTHER", Keywords(plain));
        }

        [Fact]
        public void AModelsMaterialsAreNotValidatedByTheEditorsOwnEditors()
        {
            // As a model imports, only render pipelines' and projects' editors validate
            // (tests/projects/modelimport: emissive Standard materials keep their flags).
            var m = Standard();
            m.SetColor("_EmissionColor", new Color(2f, 1f, 0f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive | MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            var logs = new Logs();
            var handler = Debug.unityLogger.logHandler;
            try
            {
                Debug.unityLogger.logHandler = logs;
                MaterialValidation.Validate(m, editorsOfTheEditor: false);
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
            }
            Assert.Empty(logs.Messages);
            Assert.Equal(MaterialGlobalIlluminationFlags.RealtimeEmissive | MaterialGlobalIlluminationFlags.EmissiveIsBlack, m.globalIlluminationFlags);
            // Control: a project's editor still validates a model's material.
            var assemblies = (List<System.Reflection.Assembly>)typeof(GameDomain)
                .GetField("s_Assemblies", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            var own = typeof(ValidationTestProjectGUI).Assembly;
            bool added = !assemblies.Contains(own);
            if (added) assemblies.Add(own);
            try
            {
                var project = new Material(ShaderUtil.CreateShaderAsset("Shader \"Tests/Project Validation " + Guid.NewGuid().ToString("N")
                    + "\"\n{\n    SubShader { Pass { } }\n    CustomEditor \"UnityEngine.Tests.ValidationTestProjectGUI\"\n}"));
                MaterialValidation.Validate(project, editorsOfTheEditor: false);
                Assert.True(project.IsKeywordEnabled("PROJECT_VALIDATED"));
            }
            finally
            {
                if (added) assemblies.Remove(own);
            }
            // Control: loaded from a material file, the Standard editor validates it.
            Validate(m);
            Assert.Equal(MaterialGlobalIlluminationFlags.RealtimeEmissive, m.globalIlluminationFlags);
            Assert.True(m.IsKeywordEnabled("_EMISSION"));
        }
    }
}
