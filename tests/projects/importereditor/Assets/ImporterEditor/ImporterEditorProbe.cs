using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode (no Inspector window shows the editors), what AssetImporterEditor and
/// ScriptedImporterEditor do for editors made with Editor.CreateEditor: which class inspects an
/// importer, the members an Inspector would fill, and how HasModified, Apply, SaveChanges and
/// DiscardChanges treat settings changed through the SerializedObject or on the importer itself,
/// with the .meta file's value and the number of imports after each step. Every scenario has an
/// asset of its own.
/// </summary>
static class ImporterEditorProbe
{
    static readonly StringBuilder s_Output = new StringBuilder();
    static readonly List<string> s_Logs = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("IMPORTER_EDITOR_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        Application.logMessageReceived += (condition, stackTrace, type) => s_Logs.Add(type + ":" + condition.Replace("\n", "\\n"));

        Classes();
        BaseEditor("Assets/Things/First.probeimp");
        SettingsFlow("Assets/Things/First.probeimp");
        ImporterFlow("Assets/Things/Second.probeimp");
        SharedState("Assets/Things/Third.probeimp");
        ExtraData("Assets/Things/Fourth.probeimp");
        ExtraButtons("Assets/Things/Twelfth.probeimp");
        Mistakes("Assets/Things/Fifth.probeimp", "Assets/Things/Tenth.probeimp");
        Unsaved("Assets/Things/Eleventh.probeimp");
        Several("Assets/Things/Sixth.probeimp", "Assets/Things/Seventh.probeimp");
        ApplyRevert("Assets/Things/Eighth.probeimp");
        OutsideGUI("Assets/Things/Ninth.probeimp");
        Texture("Assets/Sprites/Single.png");

        s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), s_Output.ToString());
        EditorApplication.Exit(0);
    }

    static ProbeImporter Importer(string path) => (ProbeImporter)AssetImporter.GetAtPath(path);

    static void Classes()
    {
        Check("class.custom", () => Made(Editor.CreateEditor(AssetImporter.GetAtPath("Assets/Things/First.probeimp"))));
        Check("class.default", () => Made(Editor.CreateEditor(AssetImporter.GetAtPath("Assets/Things/Plain.plainimp"))));
        Check("class.named", () => Made(Editor.CreateEditor(AssetImporter.GetAtPath("Assets/Things/Plain.plainimp"), typeof(ProbeImporterEditor))));
        Check("class.baseForScripted", () => Made(Editor.CreateEditor(AssetImporter.GetAtPath("Assets/Things/Plain.plainimp"), typeof(PlainAssetImporterEditor))));
        Check("class.abstract", () => Made(Editor.CreateEditor(AssetImporter.GetAtPath("Assets/Things/Plain.plainimp"), typeof(AssetImporterEditor))));
        ScriptedImporterEditor made = null;
        Step("class.new", null, () => made = ScriptableObject.CreateInstance<ScriptedImporterEditor>(),
            () => "made=" + (made != null) + "|unsaved=" + Try(() => made.hasUnsavedChanges.ToString())
                + "|message=" + Try(() => Text(made.saveChangesMessage)) + "|showImportedObject=" + Try(() => made.showImportedObject.ToString()));
        Check("class.newTargets", () => Count(made.targets));
        Step("class.newDestroy", null, () => Object.DestroyImmediate(made), () => "alive=" + (made != null));
    }

    /// <summary>The base class's SaveChanges and DiscardChanges, and overrides that do not call them.</summary>
    static void BaseEditor(string path)
    {
        foreach (var type in new[] { typeof(FlagEditor), typeof(NoBaseSaveEditor) })
        {
            var editor = (FlagEditor)Editor.CreateEditor(Importer(path), type);
            Check("base." + type.Name + ".made", () => "unsaved=" + editor.hasUnsavedChanges + "|message=" + Text(editor.saveChangesMessage));
            Check("base." + type.Name + ".save", () => { editor.SetUnsaved(); editor.SaveChanges(); return "unsaved=" + editor.hasUnsavedChanges; });
            Check("base." + type.Name + ".discard", () => { editor.SetUnsaved(); editor.DiscardChanges(); return "unsaved=" + editor.hasUnsavedChanges; });
            Object.DestroyImmediate(editor);
        }
    }

    static string Made(Editor editor)
    {
        if (editor == null) return "<null>";
        var text = "type=" + editor.GetType().FullName + "|target=" + Name(editor.target) + "|targets=" + editor.targets.Length + "|hideFlags=" + editor.hideFlags
            + "|unsaved=" + editor.hasUnsavedChanges + "|message=" + Text(editor.saveChangesMessage);
        if (editor is AssetImporterEditor importerEditor) text += "|showImportedObject=" + importerEditor.showImportedObject + "|modified=" + importerEditor.HasModified();
        Object.DestroyImmediate(editor);
        return text;
    }

    static void Edit(Editor editor, int number, string label = null)
    {
        editor.serializedObject.FindProperty("number").intValue = number;
        if (label != null) editor.serializedObject.FindProperty("label").stringValue = label;
        editor.serializedObject.ApplyModifiedProperties();
    }

    /// <summary>Settings changed through the editor's SerializedObject.</summary>
    static void SettingsFlow(string path)
    {
        ProbeImporterEditor editor = null;
        Step("settings.create", path, () => editor = (ProbeImporterEditor)Editor.CreateEditor(Importer(path)), () => State(editor, path));
        Check("settings.members", () => string.Join("|", new[]
        {
            "assetTarget=" + Name(editor.AssetTarget), "assetTargets=" + Count(editor.AssetTargets), "assetSerializedObject=" + (editor.AssetSerializedObject == null ? "<null>" : "set"),
            "needsApplyRevert=" + editor.NeedsApplyRevert, "showImportedObject=" + editor.showImportedObject, "useAssetDrawPreview=" + editor.UseAssetDrawPreview,
            "extraDataType=" + (editor.ExtraDataType == null ? "<null>" : editor.ExtraDataType.Name), "extraDataTargets=" + Count(editor.ExtraDataTargets),
            "extraDataSerializedObject=" + (editor.ExtraDataSerializedObject == null ? "<null>" : "set"), "extraDataTarget=" + Try(() => Name(editor.ExtraDataTarget)),
            "canApply=" + editor.CallCanApply(), "serializedObject=" + (editor.serializedObject == null ? "<null>" : "set"), "message=" + Text(editor.saveChangesMessage),
        }));
        Step("settings.editUnapplied", path, () => editor.serializedObject.FindProperty("number").intValue = 7,
            () => "importerBefore=" + Importer(path).number + "|dirtyBefore=" + EditorUtility.IsDirty(Importer(path)) + "|" + State(editor, path));
        Step("settings.discard", path, () => editor.DiscardChanges(), () => State(editor, path) + "|property=" + editor.serializedObject.FindProperty("number").intValue);
        Step("settings.editApplied", path, () => Edit(editor, 8), () => State(editor, path));
        Step("settings.apply", path, () => editor.CallApply(), () => State(editor, path));
        Step("settings.discardAfterApply", path, () => editor.DiscardChanges(), () => State(editor, path));
        Step("settings.editAgain", path, () => Edit(editor, 9, "edited"), () => State(editor, path));
        Step("settings.save", path, () => editor.SaveChanges(), () => State(editor, path));
        Step("settings.sameValue", path, () =>
        {
            editor.serializedObject.FindProperty("number").intValue = 10;
            editor.serializedObject.FindProperty("number").intValue = 9;
            editor.serializedObject.ApplyModifiedProperties();
        }, () => State(editor, path));
        Step("settings.resetValues", path, () => { Edit(editor, 12); editor.CallResetValues(); }, () => State(editor, path));
        Step("settings.applyAndImport", path, () => { Edit(editor, 13); editor.CallApplyAndImport(); }, () => State(editor, path));
        Step("settings.destroy", path, () => Object.DestroyImmediate(editor), () => Disk(path));
    }

    /// <summary>Settings changed on the importer itself, and what saving the importer does.</summary>
    static void ImporterFlow(string path)
    {
        ProbeImporterEditor editor = null;
        Step("importer.create", path, () => editor = (ProbeImporterEditor)Editor.CreateEditor(Importer(path)), () => State(editor, path));
        Step("importer.field", path, () => Importer(path).number = 21, () => State(editor, path) + "|property=" + editor.serializedObject.FindProperty("number").intValue);
        Step("importer.discard", path, () => editor.DiscardChanges(), () => State(editor, path) + "|property=" + editor.serializedObject.FindProperty("number").intValue);
        Step("importer.userData", path, () => Importer(path).userData = "probe", () => State(editor, path) + "|userData=" + Text(Importer(path).userData));
        Step("importer.discardUserData", path, () => editor.DiscardChanges(), () => State(editor, path) + "|userData=" + Text(Importer(path).userData));
        Step("importer.fieldDirty", path, () => { Importer(path).number = 22; EditorUtility.SetDirty(Importer(path)); }, () => State(editor, path));
        // The asset is loaded before the state is read: whether the import ran inside the call or runs with the load is not recorded.
        Step("importer.saveAndReimport", path, () => { Importer(path).SaveAndReimport(); AssetDatabase.LoadMainAssetAtPath(path); }, () => State(editor, path));
        Step("importer.discardAfterReimport", path, () => editor.DiscardChanges(), () => State(editor, path));
        // What reaches the .meta of a field set on the importer: with and without the dirty mark.
        bool wrote = false;
        Step("importer.writeFieldClean", path, () => { Importer(path).number = 23; wrote = AssetDatabase.WriteImportSettingsIfDirty(path); }, () => "wrote=" + wrote + "|" + State(editor, path));
        Step("importer.writeFieldDirty", path, () => { EditorUtility.SetDirty(Importer(path)); wrote = AssetDatabase.WriteImportSettingsIfDirty(path); }, () => "wrote=" + wrote + "|" + State(editor, path));
        Step("importer.writeAgain", path, () => wrote = AssetDatabase.WriteImportSettingsIfDirty(path), () => "wrote=" + wrote + "|" + State(editor, path));
        Step("importer.reimportFieldClean", path, () => { Importer(path).number = 24; Importer(path).SaveAndReimport(); AssetDatabase.LoadMainAssetAtPath(path); }, () => State(editor, path));
        Step("importer.destroy", path, () => Object.DestroyImmediate(editor), () => Disk(path));
    }

    /// <summary>Two editors of one importer, and an editor made after another was destroyed.</summary>
    static void SharedState(string path)
    {
        ProbeImporterEditor first = null, second = null, third = null;
        Step("shared.first", path, () => { first = (ProbeImporterEditor)Editor.CreateEditor(Importer(path)); Importer(path).number = 31; }, () => State(first, path));
        Step("shared.second", path, () => second = (ProbeImporterEditor)Editor.CreateEditor(Importer(path)), () => "second:" + State(second, path) + "|first:" + State(first, path));
        Step("shared.secondApplies", path, () => second.CallApply(), () => "second:" + State(second, path) + "|first:" + State(first, path));
        Step("shared.editAndDestroySecond", path, () => { Importer(path).number = 32; Object.DestroyImmediate(second); }, () => "first:" + State(first, path));
        Step("shared.destroyFirst", path, () => Object.DestroyImmediate(first), () => Disk(path));
        Step("shared.third", path, () => third = (ProbeImporterEditor)Editor.CreateEditor(Importer(path)), () => State(third, path));
        Step("shared.thirdDiscards", path, () => third.DiscardChanges(), () => State(third, path));
        Step("shared.destroyThird", path, () => Object.DestroyImmediate(third), () => Disk(path));
    }

    static string Extra(ProbeImporterEditor editor)
    {
        var extra = Try(() => editor.ExtraDataTarget is ProbeExtra e ? e.GetType().Name + ":" + Text(e.name) + ":" + e.hideFlags + ":" + e.extra + ":persistent=" + EditorUtility.IsPersistent(e) : "<null>");
        return "extraDataTargets=" + Try(() => Count(editor.ExtraDataTargets)) + "|extraDataTarget=" + extra
            + "|extraDataSerializedObject=" + Try(() => editor.ExtraDataSerializedObject == null ? "<null>" : editor.ExtraDataSerializedObject.GetType().Name + ":" + Name(editor.ExtraDataSerializedObject.targetObject));
    }

    static void ExtraData(string path)
    {
        ExtraImporterEditor editor = null;
        ProbeExtra extra = null;
        Step("extra.create", path, () => editor = (ExtraImporterEditor)Editor.CreateEditor(Importer(path), typeof(ExtraImporterEditor)), () =>
        {
            extra = editor.ExtraDataTarget as ProbeExtra;
            return State(editor, path) + "|" + Extra(editor) + "|same=" + (editor.ExtraDataTargets != null && editor.ExtraDataTargets.Length == 1 && editor.ExtraDataTargets[0] == editor.ExtraDataTarget);
        });
        Step("extra.editUnapplied", path, () => editor.ExtraDataSerializedObject.FindProperty("extra").intValue = 50, () => "extraBefore=" + extra.extra + "|" + State(editor, path) + "|extra=" + extra.extra);
        Step("extra.discard", path, () => editor.DiscardChanges(), () => State(editor, path) + "|extra=" + extra.extra + "|property=" + editor.ExtraDataSerializedObject.FindProperty("extra").intValue);
        Step("extra.field", path, () => extra.extra = 60, () => State(editor, path) + "|extra=" + extra.extra);
        Step("extra.apply", path, () => editor.CallApply(), () => State(editor, path) + "|extra=" + extra.extra);
        Step("extra.editImporterToo", path, () => { extra.extra = 61; Importer(path).number = 71; }, () => State(editor, path) + "|extra=" + extra.extra);
        Step("extra.discardBoth", path, () => editor.DiscardChanges(), () => State(editor, path) + "|extra=" + extra.extra);
        Step("extra.applyRevertGUI", path, () => { extra.extra = 62; editor.CallApplyRevertGUI(); }, () => State(editor, path) + "|extra=" + extra.extra);
        Step("extra.destroy", path, () => Object.DestroyImmediate(editor), () => "extraAlive=" + (extra != null) + (extra != null ? "|extra=" + extra.extra : ""));
        ExtraImporterEditor again = null;
        Step("extra.again", path, () => again = (ExtraImporterEditor)Editor.CreateEditor(Importer(path), typeof(ExtraImporterEditor)), () =>
        {
            var now = again.ExtraDataTarget as ProbeExtra;
            return "sameObject=" + (now != null && now == extra) + "|extra=" + (now == null ? "<null>" : now.extra.ToString());
        });
        ExtraImporterEditor both = null;
        Step("extra.secondEditor", path, () => both = (ExtraImporterEditor)Editor.CreateEditor(Importer(path), typeof(ExtraImporterEditor)), () =>
            "sameObject=" + (both.ExtraDataTarget != null && both.ExtraDataTarget == again.ExtraDataTarget) + "|extra=" + ((ProbeExtra)both.ExtraDataTarget).extra);
        Step("extra.destroyBoth", path, () => { Object.DestroyImmediate(both); Object.DestroyImmediate(again); }, () => "extraAlive=" + (extra != null));
    }

    /// <summary>What saving and discarding do to hasUnsavedChanges, which only ApplyRevertGUI sets.</summary>
    static void Unsaved(string path)
    {
        ProbeImporterEditor editor = null;
        Step("unsaved.create", path, () => editor = (ProbeImporterEditor)Editor.CreateEditor(Importer(path)), () => State(editor, path));
        Step("unsaved.set", path, () => { Importer(path).number = 91; editor.CallApplyRevertGUI(); }, () => State(editor, path));
        Step("unsaved.discard", path, () => editor.DiscardChanges(), () => State(editor, path));
        Step("unsaved.setAgain", path, () => { Importer(path).number = 92; editor.CallApplyRevertGUI(); }, () => State(editor, path));
        Step("unsaved.apply", path, () => editor.CallApply(), () => State(editor, path));
        Step("unsaved.save", path, () => editor.SaveChanges(), () => State(editor, path));
        Step("unsaved.destroy", path, () => Object.DestroyImmediate(editor), () => Disk(path));
    }

    /// <summary>An editor with extra data and buttons that is destroyed with unsaved changes.</summary>
    static void ExtraButtons(string path)
    {
        ExtraButtonsImporterEditor editor = null;
        ProbeExtra extra = null;
        Step("extraButtons.create", path, () => editor = (ExtraButtonsImporterEditor)Editor.CreateEditor(Importer(path), typeof(ExtraButtonsImporterEditor)), () =>
        {
            extra = editor.ExtraDataTarget as ProbeExtra;
            return State(editor, path) + "|" + Extra(editor);
        });
        Step("extraButtons.edit", path, () => { extra.extra = 77; Importer(path).number = 73; EditorUtility.SetDirty(Importer(path)); editor.CallApplyRevertGUI(); }, () => State(editor, path) + "|extra=" + extra.extra);
        Step("extraButtons.destroyUnsaved", path, () => Object.DestroyImmediate(editor), () => Disk(path) + "|extraAlive=" + (extra != null));
        ExtraButtonsImporterEditor again = null;
        Step("extraButtons.again", path, () => again = (ExtraButtonsImporterEditor)Editor.CreateEditor(Importer(path), typeof(ExtraButtonsImporterEditor)),
            () => State(again, path) + "|extra=" + ((ProbeExtra)again.ExtraDataTarget).extra);
        Step("extraButtons.destroy", path, () => Object.DestroyImmediate(again), () => Disk(path));
    }

    /// <summary>The second path has had no editor before: nothing of it is saved.</summary>
    static void Mistakes(string path, string freshPath)
    {
        BadExtraImporterEditor bad = null;
        Step("mistake.extraType", path, () => bad = (BadExtraImporterEditor)Editor.CreateEditor(Importer(path), typeof(BadExtraImporterEditor)),
            () => "extraDataTargets=" + Try(() => Count(bad.ExtraDataTargets)) + "|modified=" + Try(() => bad.HasModified().ToString()));
        Step("mistake.extraTypeDestroy", path, () => Object.DestroyImmediate(bad), () => "alive=" + (bad != null));
        UninitializedExtraImporterEditor uninitialized = null;
        Step("mistake.extraUninitialized", path, () => uninitialized = (UninitializedExtraImporterEditor)Editor.CreateEditor(Importer(path), typeof(UninitializedExtraImporterEditor)),
            () => "made=" + (uninitialized != null) + "|message=" + Try(() => Text(uninitialized.saveChangesMessage)) + "|" + Try(() => Extra(uninitialized)));
        Step("mistake.extraUninitializedDestroy", path, () => Object.DestroyImmediate(uninitialized), () => "alive=" + (uninitialized != null));
        NoBaseEnableImporterEditor noBase = null;
        Step("mistake.noBaseEnable", freshPath, () => noBase = (NoBaseEnableImporterEditor)Editor.CreateEditor(Importer(freshPath), typeof(NoBaseEnableImporterEditor)),
            () => "message=" + Text(noBase.saveChangesMessage) + "|modified=" + Try(() => noBase.HasModified().ToString()));
        Step("mistake.noBaseEnableEdit", freshPath, () => Importer(freshPath).number = 81, () => "modified=" + Try(() => noBase.HasModified().ToString()));
        Step("mistake.noBaseEnableApply", freshPath, () => noBase.CallApply(), () => "modified=" + Try(() => noBase.HasModified().ToString()) + "|importer=" + Importer(freshPath).number);
        Step("mistake.noBaseEnableDiscard", freshPath, () => { Importer(freshPath).number = 82; noBase.DiscardChanges(); }, () => "modified=" + Try(() => noBase.HasModified().ToString()) + "|importer=" + Importer(freshPath).number);
        Step("mistake.noBaseEnableDestroy", freshPath, () => Object.DestroyImmediate(noBase), () => "alive=" + (noBase != null));
        // The editors above that failed in OnEnable left the first path's settings saved.
        NoBaseEnableImporterEditor leftover = null;
        Step("mistake.leftover", path, () => { leftover = (NoBaseEnableImporterEditor)Editor.CreateEditor(Importer(path), typeof(NoBaseEnableImporterEditor)); Importer(path).number = 83; },
            () => "modified=" + Try(() => leftover.HasModified().ToString()));
        Step("mistake.leftoverDiscard", path, () => leftover.DiscardChanges(), () => "modified=" + Try(() => leftover.HasModified().ToString()) + "|importer=" + Importer(path).number);
        Step("mistake.leftoverDestroy", path, () => Object.DestroyImmediate(leftover), () => "alive=" + (leftover != null));
    }

    static void Several(string firstPath, string secondPath)
    {
        ProbeImporterEditor editor = null;
        Step("several.create", secondPath, () => editor = (ProbeImporterEditor)Editor.CreateEditor(new Object[] { Importer(firstPath), Importer(secondPath) }),
            () => "type=" + editor.GetType().Name + "|targets=" + editor.targets.Length + "|message=" + Text(editor.saveChangesMessage) + "|" + State(editor, secondPath));
        Step("several.editSecond", secondPath, () => Importer(secondPath).number = 41, () => State(editor, secondPath) + "|first=" + Importer(firstPath).number);
        Step("several.editBoth", secondPath, () => Edit(editor, 42), () => State(editor, secondPath) + "|first=" + Importer(firstPath).number);
        Step("several.discard", secondPath, () => editor.DiscardChanges(), () => State(editor, secondPath) + "|first=" + Importer(firstPath).number);
        Step("several.destroy", secondPath, () => Object.DestroyImmediate(editor), () => "second=" + Importer(secondPath).number + "|first=" + Importer(firstPath).number);
    }

    /// <summary>An editor that asks for the Apply and Revert buttons: saving imports the asset.</summary>
    static void ApplyRevert(string path)
    {
        CountImportsFromNow(path);
        ApplyRevertImporterEditor editor = null;
        Func<string> asset = () => "asset=" + Text(((TextAsset)AssetDatabase.LoadMainAssetAtPath(path)).text);
        Step("applyRevert.create", path, () => editor = (ApplyRevertImporterEditor)Editor.CreateEditor(Importer(path), typeof(ApplyRevertImporterEditor)),
            () => Loaded(asset, () => State(editor, path) + "|needsApplyRevert=" + editor.NeedsApplyRevert));
        Step("applyRevert.apply", path, () => { Edit(editor, 51); editor.CallApply(); }, () => Loaded(asset, () => State(editor, path)));
        Step("applyRevert.save", path, () => { Edit(editor, 52); editor.SaveChanges(); }, () => Loaded(asset, () => State(editor, path)));
        Step("applyRevert.saveUnchanged", path, () => editor.SaveChanges(), () => Loaded(asset, () => State(editor, path)));
        // A field set on the importer marks nothing dirty: is it written and imported?
        Step("applyRevert.saveField", path, () => { Importer(path).number = 53; editor.SaveChanges(); }, () => Loaded(asset, () => State(editor, path)));
        Step("applyRevert.editAfterImport", path, () => Edit(editor, 54), () => Loaded(asset, () => State(editor, path)));
        Step("applyRevert.discard", path, () => editor.DiscardChanges(), () => Loaded(asset, () => State(editor, path)));
        Step("applyRevert.destroyEdited", path, () => { Edit(editor, 55); Object.DestroyImmediate(editor); }, () => Loaded(asset, () => Disk(path)));
        Step("applyRevert.destroyUnused", path, () => Object.DestroyImmediate(Editor.CreateEditor(Importer(path), typeof(ApplyRevertImporterEditor))), () => Disk(path));
    }

    /// <summary>The state read after the asset was loaded, then the asset.</summary>
    static string Loaded(Func<string> asset, Func<string> state)
    {
        var loaded = asset();
        return state() + "|" + loaded;
    }

    /// <summary>The GUI members called where no GUI runs.</summary>
    static void OutsideGUI(string path)
    {
        CountImportsFromNow(path);
        ProbeImporterEditor editor = null;
        Step("outside.create", path, () => editor = (ProbeImporterEditor)Editor.CreateEditor(Importer(path)), () => State(editor, path));
        Step("outside.applyRevertGUIUnchanged", path, () => editor.CallApplyRevertGUI(), () => State(editor, path));
        Step("outside.applyRevertGUI", path, () => { Importer(path).number = 61; editor.CallApplyRevertGUI(); }, () => State(editor, path));
        Step("outside.applyRevertGUIUnapplied", path, () => { editor.serializedObject.FindProperty("number").intValue = 62; editor.CallApplyRevertGUI(); }, () => State(editor, path));
        Step("outside.onInspectorGUI", path, () => editor.OnInspectorGUI(), () => State(editor, path));
        Step("outside.applyButton", path, () => editor.CallApplyButton(), () => State(editor, path));
        Step("outside.revertButton", path, () => editor.CallRevertButton(), () => State(editor, path));
        Step("outside.onApplyRevertGUI", path, () => editor.CallOnApplyRevertGUI(), () => State(editor, path));
        Step("outside.destroyUnsaved", path, () => Object.DestroyImmediate(editor), () => Disk(path));
        ApplyRevertImporterEditor buttons = null;
        Step("outside.buttons.create", path, () => buttons = (ApplyRevertImporterEditor)Editor.CreateEditor(Importer(path), typeof(ApplyRevertImporterEditor)), () => State(buttons, path));
        Step("outside.buttons.applyRevertGUI", path, () => { Edit(buttons, 63); buttons.CallApplyRevertGUI(); }, () => State(buttons, path));
        Step("outside.buttons.destroyUnsaved", path, () => Object.DestroyImmediate(buttons), () => Disk(path));
        // After an editor that was destroyed with unsaved changes: is anything still saved of the importer?
        ProbeImporterEditor later = null;
        Step("outside.later.create", path, () => { Importer(path).number = 64; later = (ProbeImporterEditor)Editor.CreateEditor(Importer(path)); }, () => State(later, path));
        Step("outside.later.discard", path, () => later.DiscardChanges(), () => State(later, path));
        Step("outside.later.destroy", path, () => Object.DestroyImmediate(later), () => Disk(path));
    }

    /// <summary>An importer that is not scripted, with an editor of the abstract base class.</summary>
    static void Texture(string path)
    {
        Func<TextureImporter> importer = () => (TextureImporter)AssetImporter.GetAtPath(path);
        bool original = importer().mipmapEnabled;
        PlainAssetImporterEditor editor = null;
        Step("texture.create", null, () => editor = (PlainAssetImporterEditor)Editor.CreateEditor(importer(), typeof(PlainAssetImporterEditor)),
            () => "type=" + editor.GetType().Name + "|needsApplyRevert=" + editor.NeedsApplyRevert + "|assetTarget=" + Name(editor.AssetTarget) + "|message=" + Text(editor.saveChangesMessage)
                + "|modified=" + editor.HasModified() + "|unsaved=" + editor.hasUnsavedChanges);
        Step("texture.edit", null, () => importer().mipmapEnabled = !original, () => "modified=" + editor.HasModified() + "|changed=" + (importer().mipmapEnabled != original));
        Step("texture.discard", null, () => editor.DiscardChanges(), () => "modified=" + editor.HasModified() + "|changed=" + (importer().mipmapEnabled != original));
        Step("texture.editProperty", null, () =>
        {
            editor.serializedObject.FindProperty("m_EnableMipMap").intValue = original ? 0 : 1;
            editor.serializedObject.ApplyModifiedProperties();
        }, () => "modified=" + editor.HasModified() + "|changed=" + (importer().mipmapEnabled != original));
        Step("texture.apply", null, () => editor.CallApply(), () => "modified=" + editor.HasModified() + "|changed=" + (importer().mipmapEnabled != original));
        Step("texture.editBack", null, () => importer().mipmapEnabled = original, () => "modified=" + editor.HasModified() + "|changed=" + (importer().mipmapEnabled != original));
        Step("texture.applyBack", null, () => editor.CallApply(), () => "modified=" + editor.HasModified() + "|changed=" + (importer().mipmapEnabled != original));
        Step("texture.destroy", null, () => Object.DestroyImmediate(editor), () => "changed=" + (importer().mipmapEnabled != original));
    }

    /// <summary>What the editor says after a step: whether it is modified (asked last, since asking
    /// applies the SerializedObject's changes), the importer's value before and after asking, the
    /// .meta file's value and the imports of the path so far. The importer is asked for by path
    /// each time: an import leaves an earlier reference with the values it had.</summary>
    static string State(ProbeImporterEditor editor, string path)
    {
        int before = Importer(path).number;
        bool unsavedBefore = editor.hasUnsavedChanges;
        string modified = Try(() => editor.HasModified().ToString());
        int after = Importer(path).number;
        return "importer=" + before + (after != before ? "->" + after : "") + "|label=" + Importer(path).label + "|modified=" + modified
            + "|unsaved=" + unsavedBefore + (editor.hasUnsavedChanges != unsavedBefore ? "->" + editor.hasUnsavedChanges : "")
            + "|dirty=" + EditorUtility.IsDirty(Importer(path)) + "|" + Meta(path) + "|imports=" + Imports(path);
    }

    /// <summary>The importer and its file once no editor is left.</summary>
    static string Disk(string path) => "importer=" + Importer(path).number + "|dirty=" + EditorUtility.IsDirty(Importer(path)) + "|" + Meta(path) + "|imports=" + Imports(path);

    static string Meta(string path)
    {
        var text = File.ReadAllText(path + ".meta");
        var number = Regex.Match(text, @"^  number: (.*)$", RegexOptions.Multiline);
        return "meta=" + (number.Success ? number.Groups[1].Value.TrimEnd('\r') : "<none>");
    }

    static readonly Dictionary<string, int> s_ImportsBefore = new Dictionary<string, int>();

    /// <summary>The imports of the path since <see cref="CountImportsFromNow"/>, else in the session.</summary>
    static int Imports(string path) =>
        (ProbeImporter.Imports.TryGetValue(path, out var count) ? count : 0) - (s_ImportsBefore.TryGetValue(path, out var before) ? before : 0);

    /// <summary>Loads the asset and counts its imports from here on: whether the first load of a
    /// session imports the asset or takes an earlier session's import is not recorded.</summary>
    static void CountImportsFromNow(string path)
    {
        AssetDatabase.LoadMainAssetAtPath(path);
        s_ImportsBefore[path] = ProbeImporter.Imports.TryGetValue(path, out var count) ? count : 0;
    }

    /// <summary>One step: what it throws, the messages it logs, the calls the editors write down
    /// during it and during the reading of the state after it, and that state.</summary>
    static void Step(string key, string path, Action act, Func<string> read)
    {
        Check(key, () =>
        {
            s_Logs.Clear();
            ProbeImporterEditor.Calls.Clear();
            string thrown = "<none>";
            try { act(); }
            catch (Exception e) { thrown = e.GetType().Name; }
            var calls = ProbeImporterEditor.Calls.Count == 0 ? "<none>" : string.Join(" ", ProbeImporterEditor.Calls);
            var logs = s_Logs.Count == 0 ? "<none>" : string.Join("||", s_Logs);
            ProbeImporterEditor.Calls.Clear();
            s_Logs.Clear();
            var state = Try(read);
            var stateLogs = s_Logs.Count == 0 ? "" : "|stateLogs=" + string.Join("||", s_Logs);
            return "throws=" + thrown + "|calls=" + calls + "|logs=" + logs + "|" + state + stateLogs;
        });
    }

    static string Try(Func<string> read)
    {
        try { return read(); }
        catch (Exception e) { return "throws " + e.GetType().Name; }
    }

    static string Name(Object o) => o == null ? "<null>" : o.GetType().Name + ":" + o.name;

    static string Count(Object[] objects) => objects == null ? "<null>" : objects.Length.ToString();

    static string Text(string value) => value == null ? "<null>" : "\"" + value + "\"";

    static void Check(string key, Func<string> read)
    {
        string value;
        try { value = read(); }
        catch (Exception e) { value = "throws " + e.GetType().Name + ":" + e.Message; }
        s_Output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
