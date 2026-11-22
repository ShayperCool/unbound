using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.AssetImporters;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>AssetImporterEditor and ScriptedImporterEditor for editors made with
    /// Editor.CreateEditor, as the Unity 6000.0.71f1 Editor runs them where no Inspector shows
    /// them (tests/projects/importereditor).</summary>
    [Collection("EditorState")]
    public sealed class AssetImporterEditorTests : IDisposable
    {
        sealed class TestImporter : ScriptedImporter
        {
            public int number = 3;
            public string label = "start";
            public override void OnImportAsset(AssetImportContext ctx) { }
        }

        sealed class TestExtra : ScriptableObject
        {
            public int extra = 5;
        }

        class TestEditor : ScriptedImporterEditor
        {
            internal static readonly List<string> Calls = new List<string>();
            internal int Asked;
            public override bool HasModified() { Asked++; return base.HasModified(); }
            protected override void Apply() { Calls.Add("Apply"); base.Apply(); }
            public override void SaveChanges() { Calls.Add("SaveChanges"); base.SaveChanges(); }
            internal Object AssetTarget => assetTarget;
            internal Object[] AssetTargets => assetTargets;
            internal SerializedObject AssetSerializedObject => assetSerializedObject;
            internal bool NeedsApplyRevert => needsApplyRevert;
            internal bool UseAssetDrawPreview => useAssetDrawPreview;
            internal Object[] ExtraDataTargets => extraDataTargets;
            internal Object ExtraDataTarget => extraDataTarget;
            internal SerializedObject ExtraDataSerializedObject => extraDataSerializedObject;
            internal void CallApply() => Apply();
            internal bool CallCanApply() => CanApply();
            internal void CallApplyRevertGUI() => ApplyRevertGUI();
            internal bool CallApplyButton() => ApplyButton();
        }

        sealed class ExtraEditor : TestEditor
        {
            internal static int Initialized;
            protected override Type extraDataType => typeof(TestExtra);
            protected override void InitializeExtraDataInstance(Object extraData, int targetIndex)
            {
                Initialized++;
                ((TestExtra)extraData).extra = 40 + targetIndex;
            }
        }

        sealed class ExtraButtonsEditor : TestEditor
        {
            protected override Type extraDataType => typeof(TestExtra);
            protected override bool needsApplyRevert => true;
            protected override void InitializeExtraDataInstance(Object extraData, int targetIndex) { }
        }

        class FlagEditor : Editor
        {
            internal void SetUnsaved() => hasUnsavedChanges = true;
        }

        sealed class NoBaseSaveEditor : FlagEditor
        {
            public override void SaveChanges() { }
            public override void DiscardChanges() { }
        }

        sealed class ReferenceImporter : ScriptedImporter
        {
            public int number = 3;
            [SerializeReference] public object managed = new Version(1, 2);
            public override void OnImportAsset(AssetImportContext ctx) { }
        }

        sealed class UninitializedExtraEditor : TestEditor
        {
            protected override Type extraDataType => typeof(TestExtra);
        }

        sealed class BadExtraEditor : TestEditor
        {
            protected override Type extraDataType => typeof(Version);
        }

        sealed class NoBaseEnableEditor : TestEditor
        {
            public override void OnEnable() { }
        }

        readonly List<Object> m_Objects = new List<Object>();
        readonly List<string> m_Logs = new List<string>();
        readonly Application.LogCallback m_OnLog;

        public AssetImporterEditorTests()
        {
            TestEditor.Calls.Clear();
            ExtraEditor.Initialized = 0;
            m_OnLog = (condition, _, type) => m_Logs.Add(type + ":" + condition);
            Application.logMessageReceived += m_OnLog;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= m_OnLog;
            foreach (var o in m_Objects)
                if (o != null) Object.DestroyImmediate(o, true);
        }

        TestImporter Importer(string path = "Assets/Thing.test")
        {
            var importer = (TestImporter)ObjectConstruction.CreateWithBacking(typeof(TestImporter));
            importer.m_AssetPath = path;
            importer.m_Settings = TestYaml.DocumentBody("ScriptedImporter:\n  externalObjects: {}\n  userData: \n  number: 3\n  label: start\n");
            importer.m_SettingsType = "ScriptedImporter";
            importer.m_Loaded = true;
            m_Objects.Add(importer);
            return importer;
        }

        T Make<T>(params Object[] importers) where T : Editor
        {
            var editor = (T)Editor.CreateEditor(importers, typeof(T));
            m_Objects.Add(editor);
            return editor;
        }

        [Fact]
        public void AScriptedImporterWithoutAnEditorOfItsOwnGetsTheScriptedImporterEditor()
        {
            var editor = Editor.CreateEditor(Importer());
            m_Objects.Add(editor);
            Assert.IsType<ScriptedImporterEditor>(editor);
            Assert.Equal(HideFlags.HideAndDontSave, editor.hideFlags);
            Assert.Equal("Unapplied import settings for 'Assets/Thing.test'", editor.saveChangesMessage);
            Assert.Null(Editor.CreateEditor(Importer(), typeof(AssetImporterEditor)));
        }

        [Fact]
        public void WithoutAnInspectorThereIsNoAssetEditorAndChangesApplyAtOnce()
        {
            var editor = Make<TestEditor>(Importer());
            Assert.Null(editor.AssetTarget);
            Assert.Null(editor.AssetTargets);
            Assert.Null(editor.AssetSerializedObject);
            Assert.False(editor.NeedsApplyRevert);
            Assert.True(editor.showImportedObject);
            Assert.True(editor.UseAssetDrawPreview);
            Assert.True(editor.CallCanApply());
            Assert.Null(editor.ExtraDataTargets);
            Assert.Null(editor.ExtraDataSerializedObject);
            Assert.Throws<NullReferenceException>(() => editor.ExtraDataTarget);
            Assert.False(editor.hasUnsavedChanges);
            Assert.False(editor.HasModified());
            Assert.Empty(m_Logs);
        }

        [Fact]
        public void SeveralTargetsAreCountedInTheMessage()
        {
            var editor = Make<TestEditor>(Importer("Assets/A.test"), Importer("Assets/B.test"));
            Assert.Equal("Unapplied import settings for '2' files", editor.saveChangesMessage);
        }

        [Fact]
        public void ASerializedObjectShowsAScriptedImportersOwnFields()
        {
            var importer = Importer();
            var serialized = new SerializedObject(importer);
            Assert.Equal(3, serialized.FindProperty("number").intValue);
            serialized.FindProperty("label").stringValue = "edited";
            serialized.ApplyModifiedProperties();
            Assert.Equal("edited", importer.label);
            Assert.True(EditorUtility.IsDirty(importer));
        }

        [Fact]
        public void HasModifiedAppliesPendingChangesAndComparesWithTheSavedSettings()
        {
            var importer = Importer();
            var editor = Make<TestEditor>(importer);
            editor.serializedObject.FindProperty("number").intValue = 7;
            Assert.Equal(3, importer.number);
            Assert.True(editor.HasModified());
            Assert.Equal(7, importer.number);
            // Only ApplyRevertGUI sets the flag.
            Assert.False(editor.hasUnsavedChanges);

            editor.DiscardChanges();
            Assert.Equal(3, importer.number);
            Assert.Equal(3, editor.serializedObject.FindProperty("number").intValue);
            Assert.False(editor.HasModified());
            Assert.False(EditorUtility.IsDirty(importer));

            // A field set on the importer itself, and its user data, count too.
            importer.number = 21;
            Assert.True(editor.HasModified());
            editor.DiscardChanges();
            importer.userData = "probe";
            Assert.True(EditorUtility.IsDirty(importer));
            Assert.True(editor.HasModified());
            editor.DiscardChanges();
            Assert.Equal("", importer.userData);
            Assert.Equal(3, importer.number);
        }

        [Fact]
        public void ApplySavesThePresentSettingsAndSaveChangesAppliesWithoutImporting()
        {
            var importer = Importer();
            var editor = Make<TestEditor>(importer);
            importer.number = 8;
            editor.CallApply();
            Assert.False(editor.HasModified());
            editor.DiscardChanges();
            Assert.Equal(8, importer.number);

            importer.number = 9;
            TestEditor.Calls.Clear();
            editor.SaveChanges();
            Assert.Equal(new[] { "SaveChanges", "Apply" }, TestEditor.Calls);
            Assert.False(editor.HasModified());
            Assert.Equal(9, importer.number);
        }

        [Fact]
        public void EditorsOfOneImporterShareItsSavedSettingsUntilTheLastIsDestroyed()
        {
            var importer = Importer();
            var first = Make<TestEditor>(importer);
            importer.number = 31;
            var second = Make<TestEditor>(importer);
            Assert.True(second.HasModified());
            second.CallApply();
            Assert.False(first.HasModified());

            importer.number = 32;
            Object.DestroyImmediate(second);
            Assert.True(first.HasModified());
            Object.DestroyImmediate(first);
            // A new first editor saves what the importer has then.
            var third = Make<TestEditor>(importer);
            Assert.False(third.HasModified());
            third.DiscardChanges();
            Assert.Equal(32, importer.number);
        }

        [Fact]
        public void ExtraDataIsMadeOncePerImporterAndIsPartOfTheSavedSettings()
        {
            var importer = Importer();
            var editor = Make<ExtraEditor>(importer);
            var extra = Assert.IsType<TestExtra>(editor.ExtraDataTarget);
            Assert.Equal(1, ExtraEditor.Initialized);
            Assert.Equal(40, extra.extra);
            Assert.Equal(HideFlags.DontSaveInEditor | HideFlags.DontUnloadUnusedAsset, extra.hideFlags);
            Assert.Same(extra, Assert.Single(editor.ExtraDataTargets));
            Assert.Same(extra, editor.ExtraDataSerializedObject.targetObject);

            editor.ExtraDataSerializedObject.FindProperty("extra").intValue = 50;
            Assert.True(editor.HasModified());
            Assert.Equal(50, extra.extra);
            editor.DiscardChanges();
            Assert.Equal(40, extra.extra);

            // A second editor takes the same object without initializing it again.
            var second = Make<ExtraEditor>(importer);
            Assert.Same(extra, second.ExtraDataTarget);
            Assert.Equal(1, ExtraEditor.Initialized);

            // An editor without buttons applies changed extra data when its GUI ends.
            extra.extra = 62;
            TestEditor.Calls.Clear();
            editor.CallApplyRevertGUI();
            Assert.Equal(new[] { "Apply" }, TestEditor.Calls);
            Assert.False(editor.hasUnsavedChanges);
            Assert.False(second.HasModified());

            Object.DestroyImmediate(second);
            Assert.True(extra != null);
            Object.DestroyImmediate(editor);
            Assert.True(extra == null);
        }

        [Fact]
        public void AnExtraDataTypeNeedsItsInitializerAndAScriptableObject()
        {
            var uninitialized = Make<UninitializedExtraEditor>(Importer("Assets/A.test"));
            Assert.Contains("Exception:NotImplementedException: InitializeExtraDataInstance must be implemented when extraDataType is overridden.", m_Logs);
            // OnEnable did not reach its end: no message, and the editor fails again when destroyed.
            Assert.Null(uninitialized.saveChangesMessage);
            m_Logs.Clear();
            Object.DestroyImmediate(uninitialized);
            Assert.Equal(new[]
            {
                "Error:UninitializedExtraEditor.OnEnable must call base.OnEnable to avoid unexpected behaviour.",
                "Exception:NullReferenceException: Object reference not set to an instance of an object",
            }, m_Logs);

            m_Logs.Clear();
            var bad = Make<BadExtraEditor>(Importer("Assets/B.test"));
            Assert.Equal(new[] { "Error:Extra Data objects needs to be ScriptableObject to support assembly reloads and Undo/Redo" }, m_Logs);
            Assert.Null(bad.ExtraDataTargets);
            m_Logs.Clear();
            Assert.False(bad.HasModified());
            Assert.False(bad.HasModified());
            Assert.Equal(new[] { "Error:SerializedObject target has been destroyed." }, m_Logs);
        }

        [Fact]
        public void AnEditorWhoseOnEnableSkipsTheBaseHoldsNoSavedSettings()
        {
            var importer = Importer();
            var editor = Make<NoBaseEnableEditor>(importer);
            Assert.Null(editor.saveChangesMessage);
            // Nothing is saved of the importer: it counts as changed until something applies.
            Assert.True(editor.HasModified());
            importer.number = 81;
            editor.CallApply();
            Assert.False(editor.HasModified());
            importer.number = 82;
            editor.DiscardChanges();
            Assert.Equal(81, importer.number);
            m_Logs.Clear();
            Object.DestroyImmediate(editor);
            Assert.Equal(new[]
            {
                "Error:NoBaseEnableEditor.OnEnable must call base.OnEnable to avoid unexpected behaviour.",
                "Exception:NullReferenceException: Object reference not set to an instance of an object",
            }, m_Logs);
        }

        [Fact]
        public void ApplyRevertGUISetsTheUnsavedFlagAndADestroyedEditorSavesThen()
        {
            var importer = Importer();
            var editor = Make<TestEditor>(importer);
            editor.CallApplyRevertGUI();
            Assert.False(editor.hasUnsavedChanges);
            importer.number = 61;
            editor.CallApplyRevertGUI();
            Assert.True(editor.hasUnsavedChanges);
            // Applying, or discarding, leaves none.
            editor.CallApply();
            Assert.False(editor.hasUnsavedChanges);
            importer.number = 62;
            editor.CallApplyRevertGUI();
            editor.DiscardChanges();
            Assert.False(editor.hasUnsavedChanges);
            Assert.Equal(61, importer.number);

            importer.number = 63;
            editor.CallApplyRevertGUI();
            // The buttons are GUI: outside one they fail as the Editor's do, after asking CanApply.
            Assert.Throws<ArgumentException>(() => editor.CallApplyButton());
            Assert.Throws<NullReferenceException>(() => editor.OnInspectorGUI());
            TestEditor.Calls.Clear();
            Object.DestroyImmediate(editor);
            Assert.Equal(new[] { "SaveChanges", "Apply" }, TestEditor.Calls);
            Assert.Equal(63, importer.number);
        }

        [Fact]
        public void AScriptedImportersFieldsAreWrittenIntoItsMetaSectionWhole()
        {
            var importer = Importer();
            importer.number = 22;
            importer.label = "two words";
            var root = UnityYamlText.ParseTopLevel("ScriptedImporter:\n  externalObjects: {}\n  userData: \n  number: 3\n");
            Assert.True(importer.UsesDirtyFlagForWrite);
            Assert.True(importer.WriteCustomMeta(root.Get("ScriptedImporter")));
            // A field the file has keeps its place; one it lacks follows the last.
            Assert.Equal("ScriptedImporter:\n  externalObjects: {}\n  userData: \n  number: 22\n  label: two words\n", UnityYamlText.WriteTopLevel(root));
        }

        [Fact]
        public void TheBaseSaveAndDiscardResetTheUnsavedFlagAndOverridesThatSkipThemDoNot()
        {
            var editor = Make<FlagEditor>(Importer());
            Assert.Null(editor.saveChangesMessage);
            editor.SetUnsaved();
            editor.SaveChanges();
            Assert.False(editor.hasUnsavedChanges);
            editor.SetUnsaved();
            editor.DiscardChanges();
            Assert.False(editor.hasUnsavedChanges);

            var skipping = Make<NoBaseSaveEditor>(Importer("Assets/B.test"));
            skipping.SetUnsaved();
            skipping.SaveChanges();
            Assert.True(skipping.hasUnsavedChanges);
            skipping.DiscardChanges();
            Assert.True(skipping.hasUnsavedChanges);
        }

        [Fact]
        public void AnEditorDestroyedWithUnsavedChangesSavesAfterGivingUpItsExtraData()
        {
            var importer = Importer();
            var editor = Make<ExtraButtonsEditor>(importer);
            var extra = (TestExtra)editor.ExtraDataTarget;
            extra.extra = 77;
            importer.number = 73;
            // The buttons are GUI: outside one the call fails after it set the flag.
            Assert.Throws<NullReferenceException>(() => editor.CallApplyRevertGUI());
            Assert.True(editor.hasUnsavedChanges);
            m_Logs.Clear();
            TestEditor.Calls.Clear();
            Object.DestroyImmediate(editor);
            Assert.Equal(new[] { "SaveChanges", "Apply" }, TestEditor.Calls);
            Assert.True(extra == null);
            Assert.Contains("Error:SerializedObject target has been destroyed.", m_Logs);

            // What that save applied stays saved for the importer's next editor.
            importer.number = 74;
            var later = Make<TestEditor>(importer);
            Assert.True(later.HasModified());
            later.DiscardChanges();
            Assert.Equal(73, importer.number);
        }

        [Fact]
        public void ADestroyedEditorThatFailedInOnEnableIsNoLongerAsked()
        {
            var importer = Importer();
            var failed = Make<UninitializedExtraEditor>(importer);
            Object.DestroyImmediate(failed);
            int asked = failed.Asked;
            var editor = Make<TestEditor>(importer);
            importer.number = 9;
            m_Logs.Clear();
            editor.CallApply();
            editor.DiscardChanges();
            Assert.Empty(m_Logs);
            Assert.Equal(9, importer.number);
            Assert.Equal(asked, failed.Asked);
        }

        [Fact]
        public void FieldsTheWriterCannotWriteLeaveTheFilesFieldsAndAreReported()
        {
            var importer = (ReferenceImporter)ObjectConstruction.CreateWithBacking(typeof(ReferenceImporter));
            m_Objects.Add(importer);
            importer.m_AssetPath = "Assets/Thing.reference";
            importer.number = 22;
            var root = UnityYamlText.ParseTopLevel("ScriptedImporter:\n  externalObjects: {}\n  userData: \n  number: 3\n");
            var handler = new Recorder();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = handler;
            try
            {
                Assert.False(importer.WriteCustomMeta(root.Get("ScriptedImporter")));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
            Assert.Equal("ScriptedImporter:\n  externalObjects: {}\n  userData: \n  number: 3\n", UnityYamlText.WriteTopLevel(root));
            Assert.Equal(new[] { "Error:Unbound: the fields of the importer of 'Assets/Thing.reference' are not written into its .meta: its [SerializeReference] fields are not written yet" }, handler.Logs);
        }

        [Fact]
        public void ChangedUserDataMarksAScriptedImporterDirtyAndNoOther()
        {
            var scripted = Importer();
            scripted.userData = "";
            Assert.False(EditorUtility.IsDirty(scripted));
            scripted.userData = "x";
            Assert.True(EditorUtility.IsDirty(scripted));
            var texture = AssetImporter.FromMeta("Assets/T.png", "b123456789abcdef0123456789abcdef", "TextureImporter",
                TestYaml.DocumentBody("TextureImporter:\n  externalObjects: {}\n  serializedVersion: 13\n  userData: \n"));
            m_Objects.Add(texture);
            texture.userData = "x";
            Assert.False(EditorUtility.IsDirty(texture));
        }

        sealed class Recorder : ILogHandler
        {
            internal readonly List<string> Logs = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Logs.Add(logType + ":" + string.Format(format, args));
            public void LogException(Exception exception, Object context) => Logs.Add("Exception:" + exception.Message);
        }
    }
}
