using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// Edit mode: when a prefab instance's property modifications take a change made by script: a
/// Transform's position and a script field set directly, then after a frame, after
/// RecordPrefabInstancePropertyModifications (after the frame and, on other instances, before any),
/// after EditorUtility.SetDirty, after Undo.RecordObject
/// before the change, after a change through SerializedObject, and after the scene is saved. Each
/// row lists the instance's modifications of the changed property.
/// </summary>
public class ModificationTimingTests
{
    const string Thing = "Assets/Prefabs/Thing.prefab";

    static string Mods(GameObject instance, string property)
    {
        var mods = PrefabUtility.GetPropertyModifications(instance);
        if (mods == null) return "null";
        return string.Join("; ", mods.Where(m => m.propertyPath == property).Select(m => (m.target == null ? "null" : m.target.GetType().Name) + "=" + m.value).OrderBy(s => s, System.StringComparer.Ordinal));
    }

    static GameObject Instance()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Thing);
        return (GameObject)PrefabUtility.InstantiatePrefab(asset);
    }

    [UnityTest]
    public IEnumerator ModificationTiming()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return null;

        var moved = Instance();
        moved.transform.localPosition = new Vector3(1f, 0f, 0f);
        ProbeLog.Row("transform.direct", Mods(moved, "m_LocalPosition.x"));
        yield return null;
        ProbeLog.Row("transform.frame", Mods(moved, "m_LocalPosition.x"));
        PrefabUtility.RecordPrefabInstancePropertyModifications(moved.transform);
        ProbeLog.Row("transform.recorded", Mods(moved, "m_LocalPosition.x"));

        var counted = Instance();
        var holder = counted.GetComponent<Holder>();
        holder.count = 7;
        ProbeLog.Row("script.direct", Mods(counted, "count"));
        yield return null;
        ProbeLog.Row("script.frame", Mods(counted, "count"));
        PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
        ProbeLog.Row("script.recorded", Mods(counted, "count"));

        // Recorded before any frame passes.
        var recordedMove = Instance();
        recordedMove.transform.localPosition = new Vector3(6f, 0f, 0f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(recordedMove.transform);
        ProbeLog.Row("transformRecord.direct", Mods(recordedMove, "m_LocalPosition.x"));
        var recordedCount = Instance();
        var recordedHolder = recordedCount.GetComponent<Holder>();
        recordedHolder.count = 8;
        PrefabUtility.RecordPrefabInstancePropertyModifications(recordedHolder);
        ProbeLog.Row("scriptRecord.direct", Mods(recordedCount, "count"));

        var dirtied = Instance();
        dirtied.transform.localPosition = new Vector3(2f, 0f, 0f);
        EditorUtility.SetDirty(dirtied.transform);
        ProbeLog.Row("setDirty.direct", Mods(dirtied, "m_LocalPosition.x"));
        yield return null;
        ProbeLog.Row("setDirty.frame", Mods(dirtied, "m_LocalPosition.x"));

        var undone = Instance();
        Undo.RecordObject(undone.transform, "Move");
        undone.transform.localPosition = new Vector3(3f, 0f, 0f);
        ProbeLog.Row("undo.direct", Mods(undone, "m_LocalPosition.x"));
        Undo.FlushUndoRecordObjects();
        ProbeLog.Row("undo.flushed", Mods(undone, "m_LocalPosition.x"));
        yield return null;
        ProbeLog.Row("undo.frame", Mods(undone, "m_LocalPosition.x"));

        var serialized = Instance();
        var so = new SerializedObject(serialized.transform);
        so.FindProperty("m_LocalPosition.x").floatValue = 4f;
        so.ApplyModifiedProperties();
        ProbeLog.Row("serialized.direct", Mods(serialized, "m_LocalPosition.x"));

        var saved = Instance();
        saved.transform.localPosition = new Vector3(5f, 0f, 0f);
        ProbeLog.Row("save.before", Mods(saved, "m_LocalPosition.x"));
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Timing.unity");
        ProbeLog.Row("save.after", Mods(saved, "m_LocalPosition.x"));
        ProbeLog.Snapshot("save.scene", "Assets/Scenes/Timing.unity");
    }
}
