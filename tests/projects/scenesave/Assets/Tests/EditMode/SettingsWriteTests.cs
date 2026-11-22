using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// Edit mode: settings editor APIs change and when their files are written. EditorBuildSettings
/// config objects (added, added again with and without overwrite, removed), PlayerSettings'
/// preloaded assets, rendering layer names (added, renamed, a reserved and an out-of-range index),
/// tags and layers through a SerializedObject of the TagManager; each file's hash right after the
/// call and after SaveAssets, and changes left unsaved for the Editor's exit. run.py compares the
/// files on disk after the run.
/// </summary>
public class SettingsWriteTests
{
    const string BuildSettings = "ProjectSettings/EditorBuildSettings.asset";
    const string Player = "ProjectSettings/ProjectSettings.asset";
    const string Tags = "ProjectSettings/TagManager.asset";

    static string Name(Object o) => o == null ? "null" : o.GetType().Name + ":" + o.name;

    [Test]
    public void SettingsWrites()
    {
        var data = AssetDatabase.LoadAssetAtPath<ProbeData>("Assets/Data/DataSaved.asset");
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MatEdit.mat");

        ProbeLog.Try("config.add", () => { EditorBuildSettings.AddConfigObject("com.probe.data", data, false); return "done"; });
        ProbeLog.Row("config.add.file", ProbeLog.Hash(BuildSettings));
        ProbeLog.Try("config.addAgain", () => { EditorBuildSettings.AddConfigObject("com.probe.data", material, false); return "done"; });
        ProbeLog.Row("config.addAgain.value", EditorBuildSettings.TryGetConfigObject<Object>("com.probe.data", out var kept) ? Name(kept) : "none");
        ProbeLog.Try("config.overwrite", () => { EditorBuildSettings.AddConfigObject("com.probe.data", material, true); return "done"; });
        ProbeLog.Row("config.overwrite.value", EditorBuildSettings.TryGetConfigObject<Object>("com.probe.data", out var replaced) ? Name(replaced) : "none");
        EditorBuildSettings.AddConfigObject("com.probe.asset", data, false);
        ProbeLog.Row("config.names", string.Join(",", EditorBuildSettings.GetConfigObjectNames().OrderBy(n => n, System.StringComparer.Ordinal)));
        ProbeLog.Row("config.removeMissing", EditorBuildSettings.RemoveConfigObject("com.probe.none"));
        ProbeLog.Row("config.beforeSave.file", ProbeLog.Hash(BuildSettings));
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("config.saved.file", BuildSettings);
        // Left for the Editor's exit.
        ProbeLog.Row("config.remove", EditorBuildSettings.RemoveConfigObject("com.probe.asset"));

        PlayerSettings.SetPreloadedAssets(new Object[] { data, material });
        ProbeLog.Row("preloaded.value", string.Join(",", PlayerSettings.GetPreloadedAssets().Select(Name)));
        ProbeLog.Row("preloaded.set.file", ProbeLog.Hash(Player));
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("preloaded.saved.file", Player);

        ProbeLog.Try("layers.add", () => RenderPipelineEditorUtility.TryAddRenderingLayerName("Probe Layer"));
        ProbeLog.Row("layers.add.file", ProbeLog.Hash(Tags));
        ProbeLog.Try("layers.set", () => RenderPipelineEditorUtility.TrySetRenderingLayerName(1, "Renamed Layer"));
        // The refused renames log errors: recorded as rows, not failures of the test.
        Application.LogCallback log = (message, stack, type) => ProbeLog.Row("layers.log", type + ": " + message);
        Application.logMessageReceived += log;
        LogAssert.ignoreFailingMessages = true;
        ProbeLog.Try("layers.setReserved", () => RenderPipelineEditorUtility.TrySetRenderingLayerName(0, "Zero"));
        ProbeLog.Try("layers.setOutside", () => RenderPipelineEditorUtility.TrySetRenderingLayerName(9, "Nine"));
        LogAssert.ignoreFailingMessages = false;
        Application.logMessageReceived -= log;
        ProbeLog.Row("layers.names", string.Join(",", RenderingLayerMask.GetDefinedRenderingLayerNames()));
        ProbeLog.Row("layers.beforeSave.file", ProbeLog.Hash(Tags));
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("layers.saved.file", Tags);

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(Tags)[0]);
        var tags = tagManager.FindProperty("tags");
        tags.InsertArrayElementAtIndex(0);
        tags.GetArrayElementAtIndex(0).stringValue = "ProbeTag";
        tagManager.FindProperty("layers").GetArrayElementAtIndex(8).stringValue = "ProbeLayer";
        ProbeLog.Row("tags.applied", tagManager.ApplyModifiedProperties());
        ProbeLog.Row("tags.value", string.Join(",", UnityEditorInternal.InternalEditorUtility.tags));
        ProbeLog.Row("tags.layer", LayerMask.NameToLayer("ProbeLayer"));
        ProbeLog.Row("tags.apply.file", ProbeLog.Hash(Tags));
        // Left for the Editor's exit, with a second preloaded list.
        PlayerSettings.SetPreloadedAssets(new Object[] { material });
    }
}
