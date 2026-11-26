using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Editor APIs a project's import reaches that were stubs (they said "not implemented"): each
    /// now does what Unity's own C# does (ArrayUtility, Undo.selectionUndoRedoPerformed,
    /// ShaderUtil.RegisterShader) or what a project without an inspector window gets.
    /// </summary>
    public sealed class ImportTimeApisTests : IDisposable
    {
        readonly ILogHandler m_Handler = Debug.unityLogger.logHandler;
        readonly Capture m_Logs = new Capture();

        public ImportTimeApisTests() { Debug.unityLogger.logHandler = m_Logs; }

        public void Dispose() => Debug.unityLogger.logHandler = m_Handler;

        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType type, Object context, string format, params object[] args) =>
                Lines.Add(type + ": " + string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
        }

        [Fact]
        public void ArrayUtilityContainsFindsTheItem()
        {
            Assert.True(ArrayUtility.Contains(new[] { "a", "b" }, "b"));
            Assert.False(ArrayUtility.Contains(new[] { "a", "b" }, "c"));
            Assert.True(ArrayUtility.Contains(new string[] { null }, null));
            Assert.Throws<ArgumentNullException>(() => ArrayUtility.Contains<int>(null, 1));
            Assert.Empty(m_Logs.Lines);
        }

        [Fact]
        public void ARegisteredShaderIsFoundByItsName()
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/ImportTimeApis/Registered\" { SubShader { Pass { } } }");
            try
            {
                // Not findable until it is registered.
                Shader.Unregister(shader);
                Assert.Null(Shader.Find("Tests/ImportTimeApis/Registered"));
                ShaderUtil.RegisterShader(shader);
                Assert.Same(shader, Shader.Find("Tests/ImportTimeApis/Registered"));
                Assert.Throws<ArgumentNullException>(() => ShaderUtil.RegisterShader(null));
                // A destroyed shader is a missing reference, as for other object arguments.
                var gone = ShaderUtil.CreateShaderAsset("Shader \"Tests/ImportTimeApis/Gone\" { SubShader { Pass { } } }");
                Object.DestroyImmediate(gone);
                Assert.Throws<MissingReferenceException>(() => ShaderUtil.RegisterShader(gone));
                Assert.Empty(m_Logs.Lines);
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [Fact]
        public void UndoingASelectionChangeSaysSo()
        {
            var go = new GameObject("Selected");
            var heard = new List<Undo.UndoRedoType>();
            Action<Undo.UndoRedoType> listener = type => heard.Add(type);
            var previous = Selection.objects;
            Undo.selectionUndoRedoPerformed += listener;
            try
            {
                Undo.ClearAll();
                Undo.IncrementCurrentGroup();
                Selection.activeObject = go;
                Undo.IncrementCurrentGroup();
                Undo.PerformUndo();
                Undo.PerformRedo();
                Assert.Equal(new[] { Undo.UndoRedoType.Undo, Undo.UndoRedoType.Redo }, heard);
                Assert.DoesNotContain(m_Logs.Lines, l => l.Contains("not implemented"));
            }
            finally
            {
                Undo.selectionUndoRedoPerformed -= listener;
                Selection.objects = previous;
                Undo.ClearAll();
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void ImportTimeObjectsAreMadeWithoutNotices()
        {
            // A notice is logged once per member in a process: forget earlier tests' uses.
            foreach (var member in new[] { "UnityEditor.ActiveEditorTracker.ForceRebuild", "UnityEditor.TrueTypeFontImporter.ctor", "UnityEditor.Presets.PresetManager.ctor", "UnityEditor.ComputeShaderImporter.ctor" })
                Stubs.Forget(member);
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            Assert.Empty(ActiveEditorTracker.sharedTracker.activeEditors);
            // A font's importer, as a font's import makes it from its .meta, and the preset settings.
            var font = AssetImporter.FromMeta("Assets/Fonts/Body.ttf", "9123456789abcdef0123456789abcdef", "TrueTypeFontImporter",
                TestYaml.DocumentBody("TrueTypeFontImporter:\n  fontSize: 16\n"));
            Assert.IsType<TrueTypeFontImporter>(font);
            Object.DestroyImmediate(font);
            var compute = AssetImporter.FromMeta("Assets/Shaders/Blur.compute", "a123456789abcdef0123456789abcdef", "ComputeShaderImporter",
                TestYaml.DocumentBody("ComputeShaderImporter:\n  externalObjects: {}\n"));
            Assert.IsType<UnityEditor.ComputeShaderImporter>(compute);
            Object.DestroyImmediate(compute);
            var presets = new UnityEditor.Presets.PresetManager();
            Object.DestroyImmediate(presets);
            Assert.DoesNotContain(m_Logs.Lines, l => l.Contains("not implemented"));
        }

        [Fact]
        public void PresetSettingsWithDefaultPresetsSayTheyAreNotApplied()
        {
            Stubs.Forget(UnityEditor.Presets.PresetManager.DefaultPresetsNotice);
            UnityEditor.Presets.PresetManager.Loaded(UnityYamlText.Parse(Settings("m_DefaultPresets: {}"))[0].Body);
            Assert.Empty(m_Logs.Lines);
            UnityEditor.Presets.PresetManager.Loaded(UnityYamlText.Parse(Settings(
                "m_DefaultPresets:\n    1060208752:\n    - m_Preset: {fileID: 2655988077585873504, guid: 0123456789abcdef0123456789abcdef, type: 2}\n      m_Filter: \n      m_Disabled: 0"))[0].Body);
            Assert.Equal(new[] { "Warning: Unbound: PresetManager default presets are not applied to new imports or objects yet" }, m_Logs.Lines);
            Stubs.Forget(UnityEditor.Presets.PresetManager.DefaultPresetsNotice);
        }

        static string Settings(string presets) =>
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1386491679 &1\nPresetManager:\n  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  " + presets + "\n";
    }
}
