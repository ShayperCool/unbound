using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>An importer's settings at one moment: its section of the .meta as it stands in
    /// memory, the user data and bundle names, and a scripted importer's own fields.</summary>
    sealed class ImporterSettingsCopy
    {
        internal YNode Tree;
        internal string TreeText, UserData, BundleName, BundleVariant, Fields;

        internal bool Same(ImporterSettingsCopy other) =>
            TreeText == other.TreeText && UserData == other.UserData && BundleName == other.BundleName
            && BundleVariant == other.BundleVariant && Fields == other.Fields;
    }

    public partial class AssetImporter
    {
        internal ImporterSettingsCopy CopySettings() => new ImporterSettingsCopy
        {
            Tree = MetaSettings.Clone(m_Settings),
            TreeText = m_Settings == null ? "" : MetaSettings.ToJson(m_Settings),
            UserData = m_UserData,
            BundleName = m_AssetBundleName,
            BundleVariant = m_AssetBundleVariant,
            Fields = this is AssetImporters.ScriptedImporter ? JsonUtility.FieldsToJson(this) : "",
        };

        /// <summary>The copied settings put back, without writing anything; the importer is no
        /// longer dirty.</summary>
        internal void RestoreSettings(ImporterSettingsCopy copy)
        {
            m_Settings = MetaSettings.Clone(copy.Tree);
            m_UserData = copy.UserData;
            m_AssetBundleName = copy.BundleName;
            m_AssetBundleVariant = copy.BundleVariant;
            if (copy.Fields.Length > 0) JsonUtility.FieldsFromJson(copy.Fields, this);
            ClearModifiedState();
        }
    }
}

namespace UnityEditor.AssetImporters
{
    /// <summary>
    /// The settings each inspected importer had when its first editor was enabled, or was last
    /// applied, kept while an editor of it is enabled: what <see cref="AssetImporterEditor"/>
    /// compares with, reverts to and applies over. The editors of one importer share it, and its
    /// extra data object with it (matches the Unity 6000.0.71f1 Editor, tests/projects/importereditor
    /// shared.* and extra.secondEditor).
    /// </summary>
    static class ImporterSavedSettings
    {
        sealed class Saved
        {
            internal readonly List<AssetImporterEditor> Editors = new List<AssetImporterEditor>();
            internal ImporterSettingsCopy Settings;
            internal Object Extra;
            internal string ExtraState;
        }

        static readonly Dictionary<int, Saved> s_Saved = new Dictionary<int, Saved>();

        static string StateOf(Object extra) => extra == null ? null : EditorJsonUtility.ToJson(extra);

        /// <summary>One more editor of the importer; its extra data object when an earlier editor
        /// left one, else null.</summary>
        internal static Object Acquire(Object target, AssetImporterEditor editor)
        {
            if (!s_Saved.TryGetValue(target.GetInstanceID(), out var saved)) saved = Take(target);
            saved.Editors.Add(editor);
            return saved.Extra;
        }

        internal static void SetExtra(Object target, Object extra)
        {
            if (!s_Saved.TryGetValue(target.GetInstanceID(), out var saved)) return;
            saved.Extra = extra;
            saved.ExtraState = StateOf(extra);
        }

        /// <summary>One editor less; the last one takes the saved settings and the extra data
        /// object with it.</summary>
        internal static void Release(int instanceID, AssetImporterEditor editor)
        {
            if (!s_Saved.TryGetValue(instanceID, out var saved)) return;
            saved.Editors.Remove(editor);
            if (saved.Editors.Count > 0) return;
            s_Saved.Remove(instanceID);
            if (saved.Extra != null) Object.DestroyImmediate(saved.Extra);
        }

        /// <summary>Saves the settings the importer has now, also for an importer no enabled
        /// editor holds (an editor whose OnEnable did not reach the base class's can still apply).</summary>
        static Saved Take(Object target)
        {
            if (!s_Saved.TryGetValue(target.GetInstanceID(), out var saved))
            {
                saved = new Saved();
                s_Saved[target.GetInstanceID()] = saved;
            }
            if (target is AssetImporter importer) saved.Settings = importer.CopySettings();
            saved.ExtraState = StateOf(saved.Extra);
            return saved;
        }

        /// <summary>Whether the importer and its extra data are as saved; an importer nothing is
        /// saved of counts as changed (tests/projects/importereditor mistake.noBaseEnable).</summary>
        internal static bool IsEqual(Object target)
        {
            if (!s_Saved.TryGetValue(target.GetInstanceID(), out var saved)) return false;
            if (target is AssetImporter importer && !importer.CopySettings().Same(saved.Settings)) return false;
            return StateOf(saved.Extra) == saved.ExtraState;
        }

        internal static void Update(Object target) => Changed(Take(target));

        internal static void Revert(Object target)
        {
            if (!s_Saved.TryGetValue(target.GetInstanceID(), out var saved)) return;
            if (target is AssetImporter importer && saved.Settings != null) importer.RestoreSettings(saved.Settings);
            if (saved.Extra != null && saved.ExtraState != null) EditorJsonUtility.FromJsonOverwrite(saved.ExtraState, saved.Extra);
            Changed(saved);
        }

        /// <summary>The editors of an importer whose saved settings were set or put back learn
        /// whether they still have unsaved changes (when exactly the Editor tells them is not
        /// measured; after an Apply or a discard none is left, tests/projects/importereditor
        /// unsaved.apply and extra.applyRevertGUI).</summary>
        static void Changed(Saved saved)
        {
            foreach (var editor in saved.Editors.ToArray()) editor.RefreshUnsavedChanges();
        }
    }

    /// <summary>
    /// The base class of importer inspectors, as the Unity 6000.0.71f1 Editor runs one made with
    /// Editor.CreateEditor where no Inspector window shows it (tests/projects/importereditor): no
    /// asset editor stands beside it, so the asset members are null and changes apply at once
    /// (<see cref="needsApplyRevert"/> is false unless a subclass says otherwise).
    /// <para>The importers' settings are saved when the editor is enabled (see
    /// <see cref="ImporterSavedSettings"/>). <see cref="HasModified"/> applies the
    /// SerializedObject's pending changes and compares the importers and their extra data with the
    /// saved settings; <see cref="Apply"/> saves the present ones; <see cref="DiscardChanges"/>
    /// puts the saved ones back; <see cref="SaveChanges"/> applies and, when the editor has Apply
    /// and Revert buttons, writes the .meta files and imports the assets.</para>
    /// <para>The buttons and the Asset PostProcessors list are GUI the batch-mode probe cannot
    /// record: the buttons are laid out as the Scripting API describes them, the list is not drawn.</para>
    /// </summary>
    public abstract partial class AssetImporterEditor : Editor
    {
        /// <summary>The SerializedObject of the extra data objects.</summary>
        protected sealed partial class ExtraDataSerializedObject : SerializedObject
        {
            internal ExtraDataSerializedObject(Object[] objs, AssetImporterEditor editor) : base(objs) { }

            public new void Update() => base.Update();

            public new void UpdateIfRequiredOrScript() => base.UpdateIfRequiredOrScript();

            public new void ApplyModifiedProperties() => base.ApplyModifiedProperties();

            public new void SetIsDifferentCacheDirty() => base.SetIsDifferentCacheDirty();
        }

        const string ApplyText = "Apply", RevertText = "Revert";
        /// <summary>The message of the Editor's null reference failures.</summary>
        const string NoObject = "Object reference not set to an instance of an object";

        Editor m_AssetEditor;
        Object[] m_ExtraDataTargets;
        ExtraDataSerializedObject m_ExtraDataSerializedObject;
        bool m_InstantApply = true;
        /// <summary>The importers whose saved settings this editor holds: null until the base
        /// OnEnable ran to its end.</summary>
        List<int> m_TargetsInstanceID;
        bool m_OnEnableCalled;
        bool m_ExtraDataMissingReported;

        protected AssetImporterEditor() { }

        protected internal Object[] assetTargets => m_AssetEditor != null ? m_AssetEditor.targets : null;

        protected internal Object assetTarget => m_AssetEditor != null ? m_AssetEditor.target : null;

        protected internal SerializedObject assetSerializedObject => m_AssetEditor != null ? m_AssetEditor.serializedObject : null;

        protected Object[] extraDataTargets => m_ExtraDataTargets;

        /// <summary>The first target's extra data object; with no extra data type there is no
        /// list to take it from (a NullReferenceException in the Editor too).</summary>
        protected Object extraDataTarget => m_ExtraDataTargets[0];

        protected ExtraDataSerializedObject extraDataSerializedObject
        {
            get
            {
                if (extraDataType == null || m_ExtraDataSerializedObject != null) return m_ExtraDataSerializedObject;
                if (m_ExtraDataTargets == null)
                {
                    // An extra data type that is no ScriptableObject has no objects to serialize:
                    // the Editor says so once and goes on (tests/projects/importereditor
                    // mistake.extraType).
                    if (!m_ExtraDataMissingReported) Debug.LogError("SerializedObject target has been destroyed.");
                    m_ExtraDataMissingReported = true;
                    return null;
                }
                return m_ExtraDataSerializedObject = new ExtraDataSerializedObject(m_ExtraDataTargets, this);
            }
        }

        internal void RefreshUnsavedChanges() => hasUnsavedChanges = HasModified();

        protected virtual bool needsApplyRevert => !m_InstantApply && (target.hideFlags & HideFlags.HideInInspector) == 0;

        [Unbound.EditorFaithful("Unity's base implementation (tests/projects/importereditor settings.members)")]
        protected virtual Type extraDataType => null;

        [Unbound.EditorFaithful("Unity's base implementation (tests/projects/importereditor settings.members)")]
        protected virtual bool useAssetDrawPreview => true;

        [Unbound.EditorFaithful("Unity's base implementation (tests/projects/importereditor settings.members)")]
        public virtual bool showImportedObject => true;

        /// <summary>The Inspector's editor of the imported asset, shown below this one.</summary>
        internal void InternalSetAssetImporterTargetEditor(Object editor)
        {
            m_AssetEditor = editor as Editor;
            m_InstantApply = m_AssetEditor == null || m_AssetEditor.target == null;
        }

        protected virtual void InitializeExtraDataInstance(Object extraData, int targetIndex) =>
            throw new NotImplementedException("InitializeExtraDataInstance must be implemented when extraDataType is overridden.");

        [Unbound.EditorFaithful("Unity's base implementation (tests/projects/importereditor settings.members)")]
        protected virtual void Awake() { }

        public virtual void OnEnable()
        {
            // An editor made without targets has no list of them to walk (the Editor's fails so too).
            if (targets.Length == 0) throw new NullReferenceException(NoObject);
            if (extraDataType == null)
            {
                m_ExtraDataTargets = null;
            }
            else if (!typeof(ScriptableObject).IsAssignableFrom(extraDataType))
            {
                Debug.LogError("Extra Data objects needs to be ScriptableObject to support assembly reloads and Undo/Redo");
                m_ExtraDataTargets = null;
            }
            else
            {
                m_ExtraDataTargets = new Object[targets.Length];
            }
            var held = new List<int>(targets.Length);
            for (int i = 0; i < targets.Length; i++)
            {
                held.Add(targets[i].GetInstanceID());
                var extra = ImporterSavedSettings.Acquire(targets[i], this);
                if (m_ExtraDataTargets == null) continue;
                if (extra == null)
                {
                    extra = ScriptableObject.CreateInstance(extraDataType);
                    extra.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontUnloadUnusedAsset;
                    m_ExtraDataTargets[i] = extra;
                    InitializeExtraDataInstance(extra, i);
                    ImporterSavedSettings.SetExtra(targets[i], extra);
                }
                m_ExtraDataTargets[i] = extra;
            }
            m_TargetsInstanceID = held;
            saveChangesMessage = targets.Length == 1
                ? $"Unapplied import settings for '{(target as AssetImporter)?.assetPath}'"
                : $"Unapplied import settings for '{targets.Length}' files";
            m_OnEnableCalled = true;
        }

        public virtual void OnDisable()
        {
            if (!m_OnEnableCalled) Debug.LogError(GetType().Name + ".OnEnable must call base.OnEnable to avoid unexpected behaviour.");
            m_OnEnableCalled = false;
            // Without a completed base OnEnable there is no list of held importers (the Editor's
            // fails on it likewise, and so leaves what was taken).
            if (m_TargetsInstanceID == null) throw new NullReferenceException(NoObject);
            foreach (int instanceID in m_TargetsInstanceID) ImporterSavedSettings.Release(instanceID, this);
            if (hasUnsavedChanges) SaveChanges();
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (extraDataType != null) DrawFields(extraDataSerializedObject);
            ApplyRevertGUI();
        }

        static void DrawFields(SerializedObject serialized)
        {
            EditorGUI.RequireGUI();
            serialized.UpdateIfRequiredOrScript();
            var property = serialized.GetIterator();
            for (bool enter = true; property.NextVisible(enter); enter = false)
                if (property.propertyPath != "m_Script") EditorGUILayout.PropertyField(property, true);
            serialized.ApplyModifiedProperties();
        }

        public virtual bool HasModified()
        {
            serializedObject.ApplyModifiedProperties();
            extraDataSerializedObject?.ApplyModifiedProperties();
            foreach (var importer in targets)
                if (!ImporterSavedSettings.IsEqual(importer)) return true;
            return false;
        }

        [Unbound.EditorFaithful("Unity's base implementation (tests/projects/importereditor settings.members)")]
        protected virtual bool CanApply() => true;

        protected virtual void Apply()
        {
            serializedObject.ApplyModifiedProperties();
            extraDataSerializedObject?.ApplyModifiedProperties();
            foreach (var importer in targets) ImporterSavedSettings.Update(importer);
        }

        /// <summary>Applies the changes; an editor with Apply and Revert buttons then writes the
        /// importers' .meta files as WriteImportSettingsIfDirty does and imports every target's
        /// asset, changed or not.</summary>
        public override void SaveChanges()
        {
            base.SaveChanges();
            Apply();
            if (!needsApplyRevert) return;
            var importers = new List<AssetImporter>();
            foreach (var importer in targets)
                if (importer is AssetImporter asset) importers.Add(asset);
            foreach (var importer in importers) ImporterMeta.Write(importer);
            foreach (var importer in importers)
            {
                if (!System.IO.File.Exists(AssetDatabase.FileOf(importer.assetPath))) continue;
                ImporterMeta.Reimport(importer);
                // The Editor imports during the call; Unbound's imports run when the asset loads.
                AssetDatabase.LoadMainAssetAtPath(importer.assetPath);
            }
            foreach (var importer in targets) ImporterSavedSettings.Update(importer);
        }

        [Obsolete("UnityUpgradeable () -> SaveChanges")]
        protected internal void ApplyAndImport() => SaveChanges();

        public override void DiscardChanges()
        {
            base.DiscardChanges();
            serializedObject.SetIsDifferentCacheDirty();
            extraDataSerializedObject?.SetIsDifferentCacheDirty();
            foreach (var importer in targets) ImporterSavedSettings.Revert(importer);
            extraDataSerializedObject?.Update();
            serializedObject.Update();
        }

        [Obsolete("UnityUpgradeable () -> DiscardChanges")]
        protected virtual void ResetValues() => DiscardChanges();

        protected void RevertButton()
        {
            if (!GUILayout.Button(RevertText)) return;
            GUI.FocusControl(null);
            DiscardChanges();
            if (HasModified()) Debug.LogError("Importer reports modified values after reset.");
        }

        protected bool ApplyButton()
        {
            using (new EditorGUI.DisabledScope(!CanApply()))
            {
                if (!GUILayout.Button(ApplyText)) return false;
                GUI.FocusControl(null);
                SaveChanges();
                return true;
            }
        }

        protected virtual bool OnApplyRevertGUI()
        {
            using (new EditorGUI.DisabledScope(!hasUnsavedChanges))
            {
                RevertButton();
                return ApplyButton();
            }
        }

        /// <summary>The end of an importer inspector: whether there are unsaved changes, and the
        /// Revert and Apply buttons at the right when the editor has them; an editor without them
        /// applies changed extra data at once.</summary>
        protected void ApplyRevertGUI()
        {
            hasUnsavedChanges = HasModified();
            if (serializedObject.hasModifiedProperties)
            {
                Debug.LogWarning("OnInspectorGUI should call serializedObject.Update() at its beginning and serializedObject.ApplyModifiedProperties() before calling ApplyRevertGUI() method.");
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
            }
            var extra = extraDataSerializedObject;
            if (extra != null && extra.hasModifiedProperties)
            {
                Debug.LogWarning("OnInspectorGUI should call extraDataSerializedObject.Update() at its beginning and extraDataSerializedObject.ApplyModifiedProperties() before calling ApplyRevertGUI() method.");
                extra.ApplyModifiedProperties();
                extra.Update();
            }
            if (needsApplyRevert)
            {
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (OnApplyRevertGUI()) GUIUtility.ExitGUI();
                }
            }
            else if (extra != null && HasModified())
            {
                Apply();
            }
            if (Event.current != null && ProjectHasPostprocessors())
                Stubs.Ignored("AssetImporterEditor's Asset PostProcessors list", "is not drawn");
        }

        /// <summary>Whether the project's scripts have an asset postprocessor: the Editor lists
        /// those that apply to the importer below the buttons.</summary>
        static bool ProjectHasPostprocessors()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<AssetPostprocessor>())
                if (!type.IsAbstract && type.Assembly != typeof(AssetImporter).Assembly) return true;
            return false;
        }
    }

    /// <summary>The inspector of a scripted importer that has no editor of its own.</summary>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(ScriptedImporter), true)]
    public partial class ScriptedImporterEditor : AssetImporterEditor
    {
        public ScriptedImporterEditor() { }
    }
}
