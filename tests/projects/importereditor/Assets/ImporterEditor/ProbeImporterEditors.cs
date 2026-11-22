using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>The custom editor of ProbeImporter: it writes down the messages it gets (all but
/// HasModified, which the Editor also asks on its own) and gives the probe its protected members.</summary>
[CustomEditor(typeof(ProbeImporter)), CanEditMultipleObjects]
public class ProbeImporterEditor : ScriptedImporterEditor
{
    public static readonly List<string> Calls = new List<string>();

    protected string Tag => GetType().Name;

    protected override void Awake() { Calls.Add(Tag + ".Awake"); base.Awake(); }
    public override void OnEnable() { Calls.Add(Tag + ".OnEnable"); base.OnEnable(); }
    public override void OnDisable() { Calls.Add(Tag + ".OnDisable"); base.OnDisable(); }
    protected override void Apply()
    {
        // An editor with extra data also says whether its extra data object is still there.
        Calls.Add(Tag + ".Apply" + (extraDataType == null ? "" : "(extraAlive=" + (extraDataTargets != null && extraDataTargets[0] != null) + ")"));
        base.Apply();
    }
    protected override bool CanApply() { Calls.Add(Tag + ".CanApply"); return base.CanApply(); }
    public override void SaveChanges() { Calls.Add(Tag + ".SaveChanges"); base.SaveChanges(); }
    public override void DiscardChanges() { Calls.Add(Tag + ".DiscardChanges"); base.DiscardChanges(); }

    public Object AssetTarget => assetTarget;
    public Object[] AssetTargets => assetTargets;
    public SerializedObject AssetSerializedObject => assetSerializedObject;
    public Object[] ExtraDataTargets => extraDataTargets;
    public Object ExtraDataTarget => extraDataTarget;
    public SerializedObject ExtraDataSerializedObject => extraDataSerializedObject;
    public bool NeedsApplyRevert => needsApplyRevert;
    public Type ExtraDataType => extraDataType;
    public bool UseAssetDrawPreview => useAssetDrawPreview;
    public void CallApply() => Apply();
    public bool CallCanApply() => CanApply();
    public void CallApplyRevertGUI() => ApplyRevertGUI();
    public bool CallApplyButton() => ApplyButton();
    public void CallRevertButton() => RevertButton();
    public bool CallOnApplyRevertGUI() => OnApplyRevertGUI();
#pragma warning disable 618
    public void CallApplyAndImport() => ApplyAndImport();
    public void CallResetValues() => ResetValues();
#pragma warning restore 618
}

/// <summary>An editor with an extra data object per target.</summary>
public class ExtraImporterEditor : ProbeImporterEditor
{
    protected override Type extraDataType => typeof(ProbeExtra);

    protected override void InitializeExtraDataInstance(Object extraData, int targetIndex)
    {
        Calls.Add(Tag + ".InitializeExtraDataInstance(" + (extraData == null ? "<null>" : extraData.GetType().Name) + "," + targetIndex + ")");
        ((ProbeExtra)extraData).extra = 40 + targetIndex;
    }
}

/// <summary>An editor with extra data that asks for the Apply and Revert buttons.</summary>
public class ExtraButtonsImporterEditor : ExtraImporterEditor
{
    protected override bool needsApplyRevert => true;
}

/// <summary>An editor with an extra data type that leaves the base class to initialize it.</summary>
public class UninitializedExtraImporterEditor : ProbeImporterEditor
{
    protected override Type extraDataType => typeof(ProbeExtra);
}

/// <summary>An editor whose extra data type is no ScriptableObject.</summary>
public class BadExtraImporterEditor : ProbeImporterEditor
{
    protected override Type extraDataType => typeof(Version);
}

/// <summary>An editor whose OnEnable does not call the base class's.</summary>
public class NoBaseEnableImporterEditor : ProbeImporterEditor
{
    public override void OnEnable() { Calls.Add(Tag + ".OnEnable"); }
}

/// <summary>An editor that asks for the Apply and Revert buttons.</summary>
public class ApplyRevertImporterEditor : ProbeImporterEditor
{
    protected override bool needsApplyRevert => true;
}

/// <summary>An editor of the abstract base class, for importers that are not scripted.</summary>
public class PlainAssetImporterEditor : AssetImporterEditor
{
    public bool NeedsApplyRevert => needsApplyRevert;
    public Object AssetTarget => assetTarget;
    public void CallApply() => Apply();
}

/// <summary>A plain editor that can say it has unsaved changes.</summary>
public class FlagEditor : Editor
{
    public void SetUnsaved() => hasUnsavedChanges = true;
}

/// <summary>An editor whose SaveChanges and DiscardChanges do not call the base class's.</summary>
public class NoBaseSaveEditor : FlagEditor
{
    public override void SaveChanges() { }
    public override void DiscardChanges() { }
}
