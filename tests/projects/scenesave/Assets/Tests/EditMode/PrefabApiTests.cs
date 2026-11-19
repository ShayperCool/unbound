using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Edit mode: the prefab, stage and scene queries editor code and packages use (stages, culling
/// masks, instance roots, property modifications and overrides, instances of a prefab), the
/// prefab-instance edits (SetPropertyModifications, ApplyPrefabInstance, ConvertToPrefabInstance,
/// the obsolete CreatePrefab and ReplacePrefab) with the prefabInstanceUpdated callbacks they raise,
/// Undo.postprocessModifications and a SerializedObject's native pointer. Rows record values and
/// file hashes; snapshots keep the saved files.
/// </summary>
public class PrefabApiTests
{
    static void OnLog(string message, string stack, LogType type) => ProbeLog.Row("throw.log", type + ": " + message);

    static string Name(UnityEngine.Object o) => o == null ? "null" : o.GetType().Name + ":" + o.name;

    [Test]
    public void PrefabApi()
    {
        PrefabUtility.prefabInstanceUpdated += go => ProbeLog.Row("updated", Name(go));
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Base.unity", OpenSceneMode.Single);
        var instance = GameObject.Find("Thing");
        var part = instance.transform.Find("Part").gameObject;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Thing.prefab");

        ProbeLog.Try("cull.mask", () => EditorSceneManager.GetSceneCullingMask(scene).ToString());
        ProbeLog.Try("cull.default", () => SceneCullingMasks.DefaultSceneCullingMask.ToString());
        ProbeLog.Try("cull.combined", () => StageUtility.GetMainStage().GetCombinedSceneCullingMaskForCamera().ToString());
        ProbeLog.Try("preview.scene", () => EditorSceneManager.IsPreviewScene(scene));
        ProbeLog.Try("preview.object", () => EditorSceneManager.IsPreviewSceneObject(instance));
        ProbeLog.Try("preview.prefabAsset", () => EditorSceneManager.IsPreviewSceneObject(prefab));
        ProbeLog.Try("stage.prefab", () => PrefabStageUtility.GetCurrentPrefabStage() == null ? "null" : "stage");
        ProbeLog.Try("stage.current", () => StageUtility.GetCurrentStage().GetType().Name);
        ProbeLog.Try("stage.main", () => StageUtility.GetMainStage() == StageUtility.GetCurrentStage());
        ProbeLog.Try("stage.ofInstance", () => StageUtility.GetStage(instance) == StageUtility.GetMainStage());
        ProbeLog.Try("stage.ofScene", () => StageUtility.GetStage(scene) == StageUtility.GetMainStage());
        ProbeLog.Try("stage.prefabOfInstance", () => PrefabStageUtility.GetPrefabStage(instance) == null ? "null" : "stage");
        var placed = new GameObject("Placed");
        ProbeLog.Try("stage.place", () => { StageUtility.PlaceGameObjectInCurrentStage(placed); return placed.scene.name + "|" + (placed.transform.parent == null); });
        var placedChild = new GameObject("Placed Child");
        placedChild.transform.SetParent(placed.transform, false);
        ProbeLog.Try("stage.placeChild", () => { StageUtility.PlaceGameObjectInCurrentStage(placedChild); return Name(placedChild.transform.parent); });
        ProbeLog.Try("stage.placeNull", () => { StageUtility.PlaceGameObjectInCurrentStage(null); return "done"; });

#pragma warning disable CS0618
        ProbeLog.Try("findPrefabRoot.part", () => Name(PrefabUtility.FindPrefabRoot(part)));
        ProbeLog.Try("findPrefabRoot.plain", () => Name(PrefabUtility.FindPrefabRoot(placed)));
        ProbeLog.Try("findPrefabRoot.plainChild", () => Name(PrefabUtility.FindPrefabRoot(placedChild)));
        ProbeLog.Try("findPrefabRoot.null", () => Name(PrefabUtility.FindPrefabRoot(null)));
#pragma warning restore CS0618
        ProbeLog.Try("instances", () => string.Join(",", PrefabUtility.FindAllInstancesOfPrefab(prefab).Select(Name)));
        ProbeLog.Try("instances.scene", () => string.Join(",", PrefabUtility.FindAllInstancesOfPrefab(prefab, scene).Select(Name)));
        ProbeLog.Try("instances.child", () => string.Join(",", PrefabUtility.FindAllInstancesOfPrefab(prefab.transform.GetChild(0).gameObject).Select(Name)));
        ProbeLog.Try("instances.sceneObject", () => string.Join(",", PrefabUtility.FindAllInstancesOfPrefab(instance).Select(Name)));
        ProbeLog.Try("instances.null", () => string.Join(",", PrefabUtility.FindAllInstancesOfPrefab(null).Select(Name)));
        ProbeLog.Try("instances.invalidScene", () => string.Join(",", PrefabUtility.FindAllInstancesOfPrefab(prefab, default).Select(Name)));

        var mods = PrefabUtility.GetPropertyModifications(instance);
        ProbeLog.Row("mods.count", mods == null ? -1 : mods.Length);
        if (mods != null)
            foreach (var m in mods)
                ProbeLog.Row("mods", $"{Name(m.target)}|{m.propertyPath}|{m.value}|{Name(m.objectReference)}");
        ProbeLog.Try("mods.part", () => PrefabUtility.GetPropertyModifications(part).Length);
        ProbeLog.Try("mods.plain", () => PrefabUtility.GetPropertyModifications(placed) == null ? "null" : "array");
        foreach (var o in PrefabUtility.GetObjectOverrides(instance, false)) ProbeLog.Row("overrides", Name(o.instanceObject));
        foreach (var o in PrefabUtility.GetObjectOverrides(instance, true)) ProbeLog.Row("overrides.default", Name(o.instanceObject));
        ProbeLog.Try("overrides.plain", () => PrefabUtility.GetObjectOverrides(placed).Count);
        ProbeLog.Try("overrides.null", () => PrefabUtility.GetObjectOverrides(null).Count);
        ProbeLog.Try("overrides.part", () => PrefabUtility.GetObjectOverrides(part, true).Count);

        var sourceHolder = prefab.GetComponent<Holder>();
        var edited = mods.ToList();
        edited.Add(new PropertyModification { target = sourceHolder, propertyPath = "count", value = "21" });
        edited.Add(new PropertyModification { target = sourceHolder, propertyPath = "target", objectReference = placed });
        ProbeLog.Try("set.result", () => { PrefabUtility.SetPropertyModifications(instance, edited.ToArray()); return "done"; });
        ProbeLog.Row("set.count", instance.GetComponent<Holder>().count);
        ProbeLog.Row("set.target", Name(instance.GetComponent<Holder>().target));
        ProbeLog.Row("set.isDirty", scene.isDirty);
        ProbeLog.Row("set.mods.count", PrefabUtility.GetPropertyModifications(instance).Length);
        ProbeLog.Try("set.save", () => EditorSceneManager.SaveScene(scene, "Assets/Scenes/ApiSet.unity", true));
        ProbeLog.Snapshot("set.scene", "Assets/Scenes/ApiSet.unity");

        ProbeLog.Try("apply.result", () => { PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction); return "done"; });
        ProbeLog.Snapshot("apply.prefab", "Assets/Prefabs/Thing.prefab");
        ProbeLog.Row("apply.mods.count", PrefabUtility.GetPropertyModifications(instance).Length);

        var plain = new GameObject("Thing");
        plain.AddComponent<Holder>().count = 77;
        var plainPart = new GameObject("Part");
        plainPart.transform.SetParent(plain.transform, false);
        plainPart.transform.localPosition = new Vector3(0f, 1f, 0f);
        ProbeLog.Try("convert.result", () => { PrefabUtility.ConvertToPrefabInstance(plain, prefab, new ConvertToPrefabInstanceSettings(), InteractionMode.AutomatedAction); return "done"; });
        ProbeLog.Row("convert.isInstance", PrefabUtility.IsPartOfPrefabInstance(plain));
        ProbeLog.Row("convert.count", plain.GetComponent<Holder>().count);
        ProbeLog.Try("convert.mods", () => PrefabUtility.GetPropertyModifications(plain).Length);
        ProbeLog.Try("convert.save", () => EditorSceneManager.SaveScene(scene, "Assets/Scenes/ApiConverted.unity", true));
        ProbeLog.Snapshot("convert.scene", "Assets/Scenes/ApiConverted.unity");

#pragma warning disable CS0618
        var made = new GameObject("Created");
        made.AddComponent<BoxCollider>().size = new Vector3(3f, 3f, 3f);
        ProbeLog.Try("create.result", () => Name(PrefabUtility.CreatePrefab("Assets/Prefabs/Created.prefab", made)));
        ProbeLog.Snapshot("create.prefab", "Assets/Prefabs/Created.prefab");
        ProbeLog.Row("create.isInstance", PrefabUtility.IsPartOfPrefabInstance(made));
        var created = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Created.prefab");
        var replacement = new GameObject("Replacement");
        replacement.AddComponent<SphereCollider>();
        ProbeLog.Try("replace.result", () => Name(PrefabUtility.ReplacePrefab(replacement, created)));
        ProbeLog.Snapshot("replace.prefab", "Assets/Prefabs/Created.prefab");
        ProbeLog.Row("replace.isInstance", PrefabUtility.IsPartOfPrefabInstance(replacement));
#pragma warning restore CS0618

        Undo.postprocessModifications += modifications =>
        {
            foreach (var m in modifications)
                ProbeLog.Row("postprocess", $"{Name(m.currentValue.target)}|{m.currentValue.propertyPath}|{m.currentValue.value}|{m.previousValue?.value}");
            return modifications;
        };
        var serialized = new SerializedObject(placed.transform);
        serialized.FindProperty("m_LocalPosition.x").floatValue = 9f;
        ProbeLog.Row("postprocess.applied", serialized.ApplyModifiedProperties());
        Undo.FlushUndoRecordObjects();
        ProbeLog.Row("postprocess.position", placed.transform.localPosition.x.ToString("R"));
        // A handler that records nothing, then one that throws.
        Undo.PostprocessModifications filter = modifications => Array.Empty<UndoPropertyModification>();
        Undo.postprocessModifications += filter;
        var filtered = new SerializedObject(placed.transform);
        filtered.FindProperty("m_LocalPosition.y").floatValue = 5f;
        ProbeLog.Try("filter.applied", () => filtered.ApplyModifiedProperties());
        ProbeLog.Row("filter.position", placed.transform.localPosition.y.ToString("R"));
        Undo.FlushUndoRecordObjects();
        ProbeLog.Row("filter.flushed", placed.transform.localPosition.y.ToString("R"));
        Undo.postprocessModifications -= filter;
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
        Application.logMessageReceived += OnLog;
        Undo.PostprocessModifications thrower = modifications => throw new InvalidOperationException("probe handler failed");
        Undo.postprocessModifications += thrower;
        var thrown = new SerializedObject(placed.transform);
        thrown.FindProperty("m_LocalPosition.z").floatValue = 6f;
        ProbeLog.Try("throw.applied", () => thrown.ApplyModifiedProperties());
        ProbeLog.Row("throw.position", placed.transform.localPosition.z.ToString("R"));
        ProbeLog.Try("throw.flush", () => { Undo.FlushUndoRecordObjects(); return "done"; });
        ProbeLog.Row("throw.flushed", placed.transform.localPosition.z.ToString("R"));
        Undo.postprocessModifications -= thrower;
        Application.logMessageReceived -= OnLog;

        var pointer = typeof(SerializedObject).GetField("m_NativeObjectPtr", BindingFlags.Instance | BindingFlags.NonPublic);
        ProbeLog.Row("nativePtr.field", pointer != null);
        if (pointer != null)
        {
            ProbeLog.Row("nativePtr.live", (IntPtr)pointer.GetValue(serialized) != IntPtr.Zero);
            serialized.Dispose();
            ProbeLog.Row("nativePtr.disposed", (IntPtr)pointer.GetValue(serialized) != IntPtr.Zero);
        }
    }
}
