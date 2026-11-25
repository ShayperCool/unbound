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

        static LoadedFile FileOf(Material material)
        {
            var file = new LoadedFile { MainObject = material };
            file.Objects[1] = material;
            return file;
        }

        /// <summary>Runs <paramref name="action"/> with this assembly's shader editors among the game's.</summary>
        static void WithProjectEditors(Action action)
        {
            var assemblies = (List<System.Reflection.Assembly>)typeof(GameDomain)
                .GetField("s_Assemblies", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            var own = typeof(ValidationTestProjectGUI).Assembly;
            bool added = !assemblies.Contains(own);
            if (added) assemblies.Add(own);
            try { action(); }
            finally { if (added) assemblies.Remove(own); }
        }

        /// <summary>A loaded material asset with a render pipeline's or project's editor keeps its
        /// saved state as it loads, through reads of its keywords, queue, tags and passes, keyword
        /// and pass changes and draws, and its first property value access validates it, once
        /// (tests/projects/urpwrites load.*, read.*, meta.*, drawn.*). A model's imported material
        /// is validated by a draw (the control).</summary>
        [Fact]
        public void ALoadedAssetIsValidatedAtItsFirstPropertyValueAccessNotAsItLoadsOrDraws()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            WithProjectEditors(() =>
            {
                var m = ProjectEdited();
                Assert.Empty(CaptureLogs(() => MaterialValidation.ValidateLoaded(FileOf(m))).Messages);
                _ = m.renderQueue;
                _ = m.GetTag("RenderType", false);
                _ = m.passCount;
                _ = m.IsKeywordEnabled("PROJECT_VALIDATED");
                _ = m.enabledKeywords;
                m.EnableKeyword("OTHER");
                m.SetShaderPassEnabled("Always", false);
                m.QueueForDraw();
                m.Sync();
                Assert.Equal("OTHER", Keywords(m));
                Assert.Equal(PendingValidation.Loaded, m.PendingEditorValidation);

                m.GetFloat("_Value");
                Assert.Equal("OTHER PROJECT_VALIDATED", Keywords(m));
                m.DisableKeyword("PROJECT_VALIDATED");
                m.GetFloat("_Value");
                Assert.Equal("OTHER", Keywords(m));
            });

            var imported = Standard();
            imported.SetFloat("_Mode", 1);
            MaterialValidation.DeferImportedEditor(imported);
            imported.QueueForDraw();
            Assert.Equal(new[] { "_ALPHATEST_ON" }, imported.shaderKeywords);
        }

        /// <summary>The accesses that validate a loaded asset first (GetFloat, SetPass and a shader
        /// set measured by tests/projects/urpwrites read.*, setPass.*, shader.*; the others found
        /// the same in exploratory captures, see <see cref="PendingValidation.Loaded"/>).</summary>
        [Theory]
        [InlineData("has")]
        [InlineData("hasfloat")]
        [InlineData("getfloat")]
        [InlineData("getcolor")]
        [InlineData("setcolor")]
        [InlineData("setpass")]
        [InlineData("shader")]
        public void ALoadedAssetsFirstPropertyAccessValidatesIt(string access)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            WithProjectEditors(() =>
            {
                var m = ProjectEdited();
                MaterialValidation.ValidateLoaded(FileOf(m));
                Assert.Equal("", Keywords(m));
                switch (access)
                {
                    case "has": m.HasProperty("_Value"); break;
                    case "hasfloat": m.HasFloat("_Value"); break;
                    case "getfloat": m.GetFloat("_Value"); break;
                    case "getcolor": m.GetColor("_Tint"); break;
                    case "setcolor": m.SetColor("_Tint", Color.green); break;
                    case "setpass": m.SetPass(0); break;
                    case "shader": m.shader = m.shader; break;
                }
                Assert.Equal("PROJECT_VALIDATED", Keywords(m));
                Assert.Equal(PendingValidation.None, m.PendingEditorValidation);
            });
        }

        /// <summary>A write to a loaded asset is made before the validation it causes, which sees it
        /// (a mode written gives its states); the material then reads the value written while its
        /// data holds the validation's (tests/projects/materialvalidation LaterWrittenCutout,
        /// tests/projects/urpwrites edit.LitStaleSrcBlend), until the float is written again. A
        /// model's imported material reads the factor written too (tests/projects/modelimport
        /// BuiltinBlend).</summary>
        [Fact]
        public void AWriteThatValidatesALoadedAssetReadsAsWrittenAndIsSavedAsValidated()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            int srcBlend = Shader.PropertyToID("_SrcBlend");
            var factor = Standard();
            MaterialValidation.ValidateLoaded(FileOf(factor));
            factor.SetFloat("_SrcBlend", 7);
            Assert.Equal(7f, factor.GetFloat("_SrcBlend"));
            Assert.Equal(1f, factor.SheetFloat(srcBlend));
            factor.SetFloat("_SrcBlend", 7);
            Assert.Equal(7f, factor.SheetFloat(srcBlend));

            var mode = Standard();
            MaterialValidation.ValidateLoaded(FileOf(mode));
            mode.SetFloat("_Mode", 2);
            Assert.Equal(5f, mode.GetFloat("_SrcBlend"));
            Assert.Equal(10f, mode.GetFloat("_DstBlend"));
            Assert.Equal(2f, mode.SheetFloat(Shader.PropertyToID("_Mode")));

            var imported = Standard();
            MaterialValidation.DeferImportedEditor(imported);
            imported.SetFloat("_SrcBlend", 7);
            Assert.Equal(7f, imported.GetFloat("_SrcBlend"));
        }

        /// <summary>A clean asset stays clean through a validation that changes nothing and a float
        /// written with the value it holds (tests/projects/urpwrites read.LitCleanRead,
        /// same.LitCleanSame); a validation that changes it dirties it (read.LitStaleRead), and so
        /// does a variant's write of the value it takes from its parent, which becomes its own.</summary>
        [Fact]
        public void OnlyAChangeOfItsDataDirtiesALoadedAsset()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            // Saved as its editor leaves it (validated), or saved stale; then loaded.
            Material Asset(int mode, bool saved = true)
            {
                var m = Standard();
                m.SetFloat("_Mode", mode);
                if (saved) MaterialValidation.Validate(m);
                m.IsPersistentAsset = true;
                EditorUtility.ClearDirty(m);
                MaterialValidation.ValidateLoaded(FileOf(m));
                return m;
            }
            var clean = Asset(0);
            clean.GetFloat("_Mode");
            Assert.False(EditorUtility.IsDirty(clean));
            var same = Asset(0);
            same.SetFloat("_Mode", 0);
            Assert.False(EditorUtility.IsDirty(same));
            same.SetFloat("_Mode", 0.5f);
            Assert.True(EditorUtility.IsDirty(same));
            // A cutout saved with opaque states: its validation changes it.
            var stale = Asset(1, saved: false);
            stale.GetFloat("_Mode");
            Assert.True(EditorUtility.IsDirty(stale));

            var parent = Asset(0);
            var variant = new Material(parent.shader) { parent = parent, IsPersistentAsset = true };
            variant.GetFloat("_Mode");
            EditorUtility.ClearDirty(variant);
            variant.SetFloat("_Mode", 0);
            Assert.True(EditorUtility.IsDirty(variant));
            Assert.Contains(Shader.PropertyToID("_Mode"), variant.SheetNames(Material.SheetKind.Floats));
        }

        /// <summary>Play mode starts as in an Editor whose views drew the startup scene: the pending
        /// materials of its active, enabled renderers are validated (tests/projects/graphqueue
        /// scene.start); a disabled renderer's and an inactive object's are not.</summary>
        [Fact]
        public void TheStartupScenesDrawnMaterialsAreValidatedBeforePlay()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var scenes = new Unbound.Tests.LightProbesTests.Scenes();
            WithProjectEditors(() =>
            {
                var scene = scenes.Loaded("Startup");
                Material Drawn(string name, bool enabled, bool active)
                {
                    var m = ProjectEdited();
                    MaterialValidation.ValidateLoaded(FileOf(m));
                    var go = new GameObject(name);
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = m;
                    renderer.enabled = enabled;
                    go.SetActive(active);
                    SceneRegistry.MoveToScene(go, scene);
                    return m;
                }
                var drawn = Drawn("drawn", true, true);
                var disabled = Drawn("disabled", false, true);
                var inactive = Drawn("inactive", true, false);
                var child = ProjectEdited();
                MaterialValidation.ValidateLoaded(FileOf(child));
                var parent = new GameObject("inactive parent");
                var childObject = new GameObject("child");
                childObject.AddComponent<MeshRenderer>().sharedMaterial = child;
                childObject.transform.SetParent(parent.transform);
                parent.SetActive(false);
                SceneRegistry.MoveToScene(parent, scene);
                // A scene's objects wait for its Awake as Play mode starts: still drawn by the views.
                var waiting = Drawn("waiting", true, true);
                var sky = ProjectEdited();
                MaterialValidation.ValidateLoaded(FileOf(sky));
                var cameraObject = new GameObject("camera");
                cameraObject.AddComponent<Skybox>().material = sky;
                SceneRegistry.MoveToScene(cameraObject, scene);
                var waitingRoot = scene.Roots.Count - 2;
                scene.Roots[waitingRoot].m_AwaitingSceneAwake = true;
                MaterialValidation.ValidateDrawnBeforePlay(scene);
                scene.Roots[waitingRoot].m_AwaitingSceneAwake = false;
                Assert.Equal("PROJECT_VALIDATED", Keywords(sky));
                Assert.Equal("PROJECT_VALIDATED", Keywords(drawn));
                Assert.Equal("PROJECT_VALIDATED", Keywords(waiting));
                Assert.Equal("", Keywords(disabled));
                Assert.Equal("", Keywords(inactive));
                Assert.Equal("", Keywords(child));
                foreach (var root in scene.Roots.ToArray()) Object.DestroyImmediate(root);
            });
        }

        /// <summary>An Edit mode draw validates a loaded asset it draws, a Play mode draw does not
        /// (tests/projects/graphqueue scene.*: the Edit mode render before Play;
        /// tests/projects/urpwrites played.LitStaleDrawn).</summary>
        [Fact]
        public void AnEditModeDrawValidatesALoadedAssetAPlayModeDrawDoesNot()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            bool playing = EditorSession.IsPlaying;
            WithProjectEditors(() =>
            {
                try
                {
                    EditorSession.IsPlaying = true;
                    var played = ProjectEdited();
                    MaterialValidation.ValidateLoaded(FileOf(played));
                    played.QueueForDraw();
                    played.Sync();
                    Assert.Equal("", Keywords(played));
                    EditorSession.IsPlaying = false;
                    var edited = ProjectEdited();
                    MaterialValidation.ValidateLoaded(FileOf(edited));
                    edited.QueueForDraw();
                    Assert.Equal("PROJECT_VALIDATED", Keywords(edited));
                }
                finally { EditorSession.IsPlaying = playing; }
            });
        }

        /// <summary>A release whose validation timing is unmeasured validates a loaded material as it
        /// loads, as before.</summary>
        [Fact]
        public void AnUnmeasuredReleaseValidatesALoadedAssetAsItLoads()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            WithProjectEditors(() =>
            {
                var m = ProjectEdited();
                MaterialValidation.ValidateLoaded(FileOf(m));
                Assert.Equal("PROJECT_VALIDATED", Keywords(m));
                Assert.Equal(PendingValidation.None, m.PendingEditorValidation);
            });
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
