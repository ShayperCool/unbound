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
                ShaderUtil.RegisterShader(shader);
                Assert.Same(shader, Shader.Find("Tests/ImportTimeApis/Registered"));
                Assert.Throws<ArgumentNullException>(() => ShaderUtil.RegisterShader(null));
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
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            Assert.Empty(ActiveEditorTracker.sharedTracker.activeEditors);
            // A font's importer, as a font's import makes it from its .meta, and the preset settings.
            var font = AssetImporter.FromMeta("Assets/Fonts/Body.ttf", "9123456789abcdef0123456789abcdef", "TrueTypeFontImporter",
                TestYaml.DocumentBody("TrueTypeFontImporter:\n  fontSize: 16\n"));
            Assert.IsType<TrueTypeFontImporter>(font);
            Object.DestroyImmediate(font);
            var presets = new UnityEditor.Presets.PresetManager();
            Object.DestroyImmediate(presets);
            Assert.DoesNotContain(m_Logs.Lines, l => l.Contains("not implemented"));
        }
    }
}
