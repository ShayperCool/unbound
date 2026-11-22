using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Edit mode: scene and prefab API members tools use: a preview scene made and closed
/// (EditorSceneManager.NewPreviewScene, ClosePreviewScene, with an object moved into it), a prefab
/// edited through PrefabUtility.EditPrefabContentsScope (its file after the scope), a
/// SerializedObject's targetObjectsCount, Undo.GetRecords before and after an undo, and the
/// PrefabStage.prefabSaving and PrefabUtility.allowRecordingPrefabPropertyOverridesFor events
/// subscribed. Each row records a value, a flag or a file's hash.
/// </summary>
public class SceneApiRestTests
{
    static string D(Scene s) => s.IsValid()
        ? $"valid name='{s.name}' path='{s.path}' loaded={s.isLoaded} build={s.buildIndex} roots={s.rootCount} dirty={s.isDirty} preview={EditorSceneManager.IsPreviewScene(s)}"
        : "invalid";

    [Test]
    public void SceneApiRest()
    {
        LogAssert.ignoreFailingMessages = true;
        var logs = new List<string>();
        Application.LogCallback log = (message, stack, type) => logs.Add(type + ": " + message);
        Application.logMessageReceived += log;
        try
        {
            int before = SceneManager.sceneCount;
            int previewsBefore = EditorSceneManager.previewSceneCount;
            var preview = EditorSceneManager.NewPreviewScene();
            ProbeLog.Row("preview.scene", D(preview));
            ProbeLog.Row("preview.sceneCount", before + "->" + SceneManager.sceneCount);
            ProbeLog.Row("preview.active", SceneManager.GetActiveScene() == preview);
            var go = new GameObject("InPreview");
            SceneManager.MoveGameObjectToScene(go, preview);
            ProbeLog.Row("preview.object", (go.scene == preview) + " " + EditorSceneManager.IsPreviewSceneObject(go) + " roots=" + preview.rootCount);
            var second = EditorSceneManager.NewPreviewScene();
            ProbeLog.Row("preview.second", D(second) + " same=" + (second == preview));
            ProbeLog.Row("preview.count", previewsBefore + "->" + EditorSceneManager.previewSceneCount);
            ProbeLog.Row("preview.lookup", SceneManager.GetSceneByName("").IsValid() + " " + (SceneManager.GetSceneAt(SceneManager.sceneCount - 1) == second)
                + " found=" + (GameObject.Find("InPreview") != null));
            var inSecond = new GameObject("InSecond");
            SceneManager.MoveGameObjectToScene(inSecond, second);
            var child = new GameObject("Child");
            child.transform.SetParent(inSecond.transform, false);
            ProbeLog.Row("preview.close", EditorSceneManager.ClosePreviewScene(preview));
            ProbeLog.Row("preview.afterClose", D(preview) + " object=" + (go == null));
            ProbeLog.Row("preview.closeAgain", EditorSceneManager.ClosePreviewScene(preview));
            ProbeLog.Row("preview.closeSecond", EditorSceneManager.ClosePreviewScene(second) + " objects=" + (inSecond == null) + "," + (child == null)
                + " count=" + EditorSceneManager.previewSceneCount);
            ProbeLog.Try("preview.closeInvalid", () => EditorSceneManager.ClosePreviewScene(default));
            ProbeLog.Try("preview.closeActive", () => EditorSceneManager.ClosePreviewScene(SceneManager.GetActiveScene()));
            ProbeLog.Row("preview.logs", string.Join(" | ", logs));
            logs.Clear();

            // Prefab contents loaded directly: their scene, and that scene closed as a preview scene.
            var loaded = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Thing.prefab");
            var loadedScene = loaded.scene;
            ProbeLog.Row("contents.scene", D(loadedScene) + " count=" + EditorSceneManager.previewSceneCount);
            ProbeLog.Try("contents.close", () => EditorSceneManager.ClosePreviewScene(loadedScene) + " root=" + (loaded == null) + " " + D(loadedScene)
                + " count=" + EditorSceneManager.previewSceneCount);
            ProbeLog.Try("contents.unloadAfterClose", () => { PrefabUtility.UnloadPrefabContents(loaded); return "unloaded"; });
            ProbeLog.Row("contents.logs", string.Join(" | ", logs));
            logs.Clear();

            using (var scope = new PrefabUtility.EditPrefabContentsScope("Assets/Prefabs/Thing.prefab"))
            {
                var root = scope.prefabContentsRoot;
                ProbeLog.Row("scope.path", scope.assetPath);
                ProbeLog.Row("scope.root", root.name + " " + EditorSceneManager.IsPreviewScene(root.scene) + " " + D(root.scene));
                new GameObject("ScopeAdded").transform.SetParent(root.transform, false);
            }
            ProbeLog.Snapshot("scope.prefab", "Assets/Prefabs/Thing.prefab");
            ProbeLog.Try("scope.missing", () => { using (new PrefabUtility.EditPrefabContentsScope("Assets/Prefabs/Missing.prefab")) { } return "no throw"; });
            ProbeLog.Row("scope.logs", string.Join(" | ", logs));
            logs.Clear();

            var a = new GameObject("A");
            var b = new GameObject("B");
            // Internal members packages use (InternalsVisibleTo): read by reflection.
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var count = typeof(SerializedObject).GetProperty("targetObjectsCount", Any);
            ProbeLog.Row("so.count", count.GetValue(new SerializedObject(new Object[] { a, b })) + " " + count.GetValue(new SerializedObject(a)));

            Undo.ClearAll();
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(a.transform, "Move A");
            a.transform.position = Vector3.one;
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(b.transform, "Move B");
            b.transform.position = Vector3.one;
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(a.transform, "Move A again");
            a.transform.position = Vector3.up;
            Undo.RecordObject(b.transform, "Move B with A");
            b.transform.position = Vector3.up;
            Undo.IncrementCurrentGroup();
            var undo = new List<string>();
            var redo = new List<string>();
            var getRecords = typeof(Undo).GetMethod("GetRecords", Any, null, new[] { typeof(List<string>), typeof(List<string>) }, null);
            getRecords.Invoke(null, new object[] { undo, redo });
            ProbeLog.Row("undo.records", string.Join(",", undo) + " / " + string.Join(",", redo));
            Undo.PerformUndo();
            getRecords.Invoke(null, new object[] { undo, redo });
            ProbeLog.Row("undo.afterUndo", string.Join(",", undo) + " / " + string.Join(",", redo));
            Undo.PerformUndo();
            getRecords.Invoke(null, new object[] { undo, redo });
            ProbeLog.Row("undo.afterTwoUndos", string.Join(",", undo) + " / " + string.Join(",", redo));
            // The other overload: one list and a cursor.
            var getCursor = typeof(Undo).GetMethod("GetRecords", Any, null, new[] { typeof(List<string>), typeof(int).MakeByRefType() }, null);
            var all = new List<string> { "stale" };
            var arguments = new object[] { all, -5 };
            getCursor.Invoke(null, arguments);
            ProbeLog.Row("undo.cursor", string.Join(",", all) + " @" + arguments[1]);
            Undo.PerformRedo();
            getCursor.Invoke(null, arguments);
            ProbeLog.Row("undo.cursorAfterRedo", string.Join(",", all) + " @" + arguments[1]);
            // A group with no name of its own (a created object) and lists that held entries.
            Undo.RegisterCreatedObjectUndo(new GameObject("Made"), "");
            Undo.IncrementCurrentGroup();
            undo.Add("stale");
            getRecords.Invoke(null, new object[] { undo, redo });
            ProbeLog.Row("undo.unnamed", string.Join(",", undo) + " / " + string.Join(",", redo));
            Undo.ClearAll();
            getRecords.Invoke(null, new object[] { undo, redo });
            ProbeLog.Row("undo.cleared", string.Join(",", undo) + " / " + string.Join(",", redo));
            // Each alone: a created object named and not, a recorded object not named.
            Undo.RegisterCreatedObjectUndo(new GameObject("MadeNamed"), "Make It");
            Undo.IncrementCurrentGroup();
            getRecords.Invoke(null, new object[] { undo, redo });
            ProbeLog.Row("undo.createdNamed", string.Join(",", undo) + " / " + string.Join(",", redo));
            Undo.ClearAll();
            Undo.RegisterCreatedObjectUndo(new GameObject("MadeUnnamed"), "");
            Undo.IncrementCurrentGroup();
            getRecords.Invoke(null, new object[] { undo, redo });
            ProbeLog.Row("undo.createdUnnamed", string.Join(",", undo) + " / " + string.Join(",", redo));
            Undo.ClearAll();
            Undo.RecordObject(a.transform, "");
            a.transform.position = Vector3.right;
            Undo.IncrementCurrentGroup();
            getRecords.Invoke(null, new object[] { undo, redo });
            ProbeLog.Row("undo.recordedUnnamed", string.Join(",", undo) + " / " + string.Join(",", redo));
            Undo.ClearAll();
            // Recorded and read in the same group, before any flush.
            Undo.RecordObject(a.transform, "Open Group");
            a.transform.position = Vector3.left;
            getRecords.Invoke(null, new object[] { undo, redo });
            ProbeLog.Row("undo.openGroup", string.Join(",", undo) + " / " + string.Join(",", redo));
            Undo.ClearAll();
            // What each kind of record lists, alone in a closed group and with others.
            System.Action<string, System.Action> kind = (key, record) =>
            {
                Undo.ClearAll();
                Undo.IncrementCurrentGroup();
                record();
                getRecords.Invoke(null, new object[] { undo, redo });
                string open = string.Join(",", undo);
                Undo.IncrementCurrentGroup();
                getRecords.Invoke(null, new object[] { undo, redo });
                ProbeLog.Row("undo.kind." + key, open + " -> " + string.Join(",", undo) + " / " + string.Join(",", redo));
            };
            kind("addComponent", () => Undo.AddComponent<BoxCollider>(a));
            kind("destroy", () => Undo.DestroyObjectImmediate(new GameObject("Doomed")));
            kind("parent", () => Undo.SetTransformParent(b.transform, a.transform, "Reparent"));
            kind("complete", () => { Undo.RegisterCompleteObjectUndo(a.transform, "Complete"); a.transform.position = Vector3.forward; });
            kind("hierarchy", () => { Undo.RegisterFullObjectHierarchyUndo(a, "Hierarchy"); a.transform.position = Vector3.back; });
            kind("unchanged", () => Undo.RecordObject(a.transform, "No Change"));
            kind("created", () => Undo.RegisterCreatedObjectUndo(new GameObject("K1"), "Made"));
            kind("recordThenCreated", () =>
            {
                Undo.RecordObject(a.transform, "Rec");
                a.transform.position = Vector3.down;
                Undo.RegisterCreatedObjectUndo(new GameObject("K2"), "Made");
            });
            kind("createdThenRecord", () =>
            {
                Undo.RegisterCreatedObjectUndo(new GameObject("K3"), "Made");
                Undo.RecordObject(a.transform, "Rec");
                a.transform.position = Vector3.zero;
            });
            kind("twoCreated", () =>
            {
                Undo.RegisterCreatedObjectUndo(new GameObject("K4"), "First");
                Undo.RegisterCreatedObjectUndo(new GameObject("K5"), "Second");
            });
            kind("createdThenAdd", () =>
            {
                var made = new GameObject("K6");
                Undo.RegisterCreatedObjectUndo(made, "Made");
                Undo.AddComponent<BoxCollider>(made);
            });
            kind("namedGroup", () =>
            {
                Undo.RecordObject(a.transform, "Rec");
                a.transform.position = Vector3.one * 3f;
                Undo.SetCurrentGroupName("Group Name");
            });
            kind("sameNameTwoGroups", () =>
            {
                Undo.RecordObject(a.transform, "Same");
                a.transform.position = Vector3.one * 4f;
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(b.transform, "Same");
                b.transform.position = Vector3.one * 4f;
            });
            // One undo of a created object's group, and what stays listed.
            Undo.ClearAll();
            var undone = new GameObject("K7");
            Undo.RegisterCreatedObjectUndo(undone, "Made");
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            getRecords.Invoke(null, new object[] { undo, redo });
            ProbeLog.Row("undo.createdUndone", (undone == null) + " " + string.Join(",", undo) + " / " + string.Join(",", redo));
            Undo.ClearAll();
            ProbeLog.Try("undo.nullList", () => { try { getRecords.Invoke(null, new object[] { null, redo }); return "no throw"; } catch (TargetInvocationException e) { throw e.InnerException; } });

            System.Action<GameObject> saving = _ => { };
            PrefabStage.prefabSaving += saving;
            PrefabStage.prefabSaving -= saving;
            var allow = typeof(PrefabUtility).GetEvent("allowRecordingPrefabPropertyOverridesFor", Any);
            ProbeLog.Row("events.allowType", allow == null ? "none" : allow.EventHandlerType.ToString());
            ProbeLog.Row("events.logs", string.Join(" | ", logs));
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
        finally
        {
            Application.logMessageReceived -= log;
            LogAssert.ignoreFailingMessages = false;
        }
    }
}
