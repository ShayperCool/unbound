using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Preview scenes made by script, a SerializedObject's target count, the prefab
    /// stage's save events and Undo's record list (tests/projects/scenesave scene-api-rest
    /// records the Editor's).</summary>
    public class SceneApiRestTests
    {
        [Fact]
        public void APreviewSceneIsLoadedUnlistedAndClosesWithItsObjects()
        {
            int scenes = SceneManager.sceneCount, previews = EditorSceneManager.previewSceneCount;
            var active = SceneManager.GetActiveScene();
            var preview = EditorSceneManager.NewPreviewScene();
            var second = EditorSceneManager.NewPreviewScene();
            try
            {
                Assert.True(preview.IsValid());
                Assert.Equal(("", "", true, -1, 0), (preview.name, preview.path, preview.isLoaded, preview.buildIndex, preview.rootCount));
                Assert.True(EditorSceneManager.IsPreviewScene(preview));
                Assert.NotEqual(preview, second);
                Assert.Equal(scenes, SceneManager.sceneCount);
                Assert.Equal(previews + 2, EditorSceneManager.previewSceneCount);
                Assert.Equal(active, SceneManager.GetActiveScene());

                var root = new GameObject("InPreview");
                var child = new GameObject("Child");
                SceneManager.MoveGameObjectToScene(root, preview);
                child.transform.SetParent(root.transform, false);
                Assert.Equal(preview, root.scene);
                Assert.True(EditorSceneManager.IsPreviewSceneObject(child));
                Assert.Equal(1, preview.rootCount);

                Assert.True(EditorSceneManager.ClosePreviewScene(preview));
                Assert.False(preview.IsValid());
                Assert.True(root == null && child == null);
                Assert.Equal(previews + 1, EditorSceneManager.previewSceneCount);
                // A closed scene, or none, is no error.
                Assert.False(EditorSceneManager.ClosePreviewScene(preview));
                Assert.False(EditorSceneManager.ClosePreviewScene(default));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                EditorSceneManager.ClosePreviewScene(second);
            }
            Assert.Equal(previews, EditorSceneManager.previewSceneCount);
        }

        [Fact]
        public void ClosingASceneThatIsNoPreviewSceneThrowsAndLeavesIt()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var kept = new GameObject("Kept");
                SceneManager.MoveGameObjectToScene(kept, scene);
                var error = Assert.Throws<ArgumentException>(() => EditorSceneManager.ClosePreviewScene(scene));
                Assert.Equal("EditorSceneManager.ClosePreviewScene failed; a non preview scene was passed in", error.Message);
                Assert.True(scene.IsValid());
                Assert.False(kept == null);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Fact]
        public void ASerializedObjectCountsItsTargets()
        {
            var a = new GameObject("A");
            var b = new GameObject("B");
            try
            {
                Assert.Equal(2, new SerializedObject(new Object[] { a, b }).targetObjectsCount);
                Assert.Equal(1, new SerializedObject(a).targetObjectsCount);
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Fact]
        public void ThePrefabSaveEventsKeepTheirHandlers()
        {
            Action<GameObject> handler = _ => { };
            PrefabStage.prefabSaving += handler;
            PrefabStage.prefabSaved += handler;
            PrefabStage.prefabSaving -= handler;
            PrefabStage.prefabSaved -= handler;
            Func<Object, bool> allow = _ => true;
            PrefabUtility.allowRecordingPrefabPropertyOverridesFor += allow;
            PrefabUtility.allowRecordingPrefabPropertyOverridesFor -= allow;
        }

        static string Records()
        {
            var undo = new List<string> { "stale" };
            var redo = new List<string> { "stale" };
            Undo.GetRecords(undo, redo);
            return string.Join(",", undo) + " / " + string.Join(",", redo);
        }

        static string Cursor()
        {
            var all = new List<string> { "stale" };
            Undo.GetRecords(all, out int cursor);
            return string.Join(",", all) + " @" + cursor;
        }

        /// <summary>The list one closed group of records gives, before and after it is closed.</summary>
        static (string Open, string Closed) Kind(Action record)
        {
            Undo.ClearAll();
            Undo.IncrementCurrentGroup();
            record();
            string open = Records();
            Undo.IncrementCurrentGroup();
            string closed = Records();
            Undo.ClearAll();
            return (open, closed);
        }

        [Fact]
        public void RecordsListEntriesOldestFirstAndRedoInItsOrder()
        {
            var a = new GameObject("A");
            var b = new GameObject("B");
            try
            {
                Undo.ClearAll();
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(a.transform, "Move A");
                a.transform.position = Vector3.one;
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(b.transform, "Move B");
                b.transform.position = Vector3.one;
                Undo.IncrementCurrentGroup();
                // Two records of one group and one flush are one entry, named by the later.
                Undo.RecordObject(a.transform, "Move A again");
                a.transform.position = Vector3.up;
                Undo.RecordObject(b.transform, "Move B with A");
                b.transform.position = Vector3.up;
                Undo.IncrementCurrentGroup();
                Assert.Equal("Move A,Move B,Move B with A / ", Records());
                Undo.PerformUndo();
                Assert.Equal("Move A,Move B / Move B with A", Records());
                Undo.PerformUndo();
                Assert.Equal("Move A / Move B,Move B with A", Records());
                Assert.Equal("Move A,Move B,Move B with A @1", Cursor());
                Undo.PerformRedo();
                Assert.Equal("Move A,Move B,Move B with A @2", Cursor());
                Undo.ClearAll();
                Assert.Equal(" / ", Records());
                Assert.Equal("List must be non-null.", Assert.Throws<NullReferenceException>(() => Undo.GetRecords(null, new List<string>())).Message);
                Assert.Equal("List must be non-null.", Assert.Throws<NullReferenceException>(() => Undo.GetRecords(null, out _)).Message);
            }
            finally
            {
                Undo.ClearAll();
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Fact]
        public void EachKindOfRecordListsAsTheEditorListsIt()
        {
            var a = new GameObject("A");
            var b = new GameObject("B");
            var made = new List<GameObject>();
            GameObject Made(string name) { var go = new GameObject(name); made.Add(go); return go; }
            try
            {
                // A recorded object is listed once flushed, and only when it changed.
                Assert.Equal((" / ", "Rec / "), Kind(() => { Undo.RecordObject(a.transform, "Rec"); a.transform.position = Vector3.right; }));
                Assert.Equal((" / ", " / "), Kind(() => Undo.RecordObject(a.transform, "No Change")));
                Assert.Equal((" / ", "Record Modification / "), Kind(() => { Undo.RecordObject(a.transform, ""); a.transform.position = Vector3.left; }));
                // An unchanged record between two changed ones of the flush does not part them.
                Assert.Equal((" / ", "Last / "), Kind(() =>
                {
                    Undo.RecordObject(a.transform, "First");
                    a.transform.position = Vector3.one * 5f;
                    Undo.RecordObject(a, "Unchanged");
                    Undo.RecordObject(b.transform, "Last");
                    b.transform.position = Vector3.one * 5f;
                }));
                // Complete registrations are listed at once, a hierarchy as one entry.
                Assert.Equal(("Complete / ", "Complete / "), Kind(() => { Undo.RegisterCompleteObjectUndo(a.transform, "Complete"); a.transform.position = Vector3.forward; }));
                Assert.Equal(("Hierarchy / ", "Hierarchy / "), Kind(() => { Undo.RegisterFullObjectHierarchyUndo(a, "Hierarchy"); a.transform.position = Vector3.back; }));
                // A registered created object is two entries; records made before it come first.
                Assert.Equal(("Made,Made / ", "Made,Made / "), Kind(() => Undo.RegisterCreatedObjectUndo(Made("K1"), "Made")));
                Assert.Equal(("Create Object,Update Object / ", "Create Object,Update Object / "), Kind(() => Undo.RegisterCreatedObjectUndo(Made("K2"), "")));
                Assert.Equal(("Rec,Made,Made / ", "Rec,Made,Made / "), Kind(() =>
                {
                    Undo.RecordObject(a.transform, "Rec");
                    a.transform.position = Vector3.down;
                    Undo.RegisterCreatedObjectUndo(Made("K3"), "Made");
                }));
                Assert.Equal(("Made,Made / ", "Made,Made,Rec / "), Kind(() =>
                {
                    Undo.RegisterCreatedObjectUndo(Made("K4"), "Made");
                    Undo.RecordObject(a.transform, "Rec");
                    a.transform.position = Vector3.zero;
                }));
                Assert.Equal(("First,First,Second,Second / ", "First,First,Second,Second / "), Kind(() =>
                {
                    Undo.RegisterCreatedObjectUndo(Made("K5"), "First");
                    Undo.RegisterCreatedObjectUndo(Made("K6"), "Second");
                }));
                Assert.Equal(("Add BoxCollider / ", "Add BoxCollider / "), Kind(() => Undo.AddComponent<BoxCollider>(a)));
                Assert.Equal(("Clear Selection,Destroy Object / ", "Clear Selection,Destroy Object / "), Kind(() => Undo.DestroyObjectImmediate(new GameObject("Doomed"))));
                Assert.Equal(("Reparent / ", "Reparent / "), Kind(() => Undo.SetTransformParent(b.transform, a.transform, "Reparent")));
                // A group's own name is an entry before its records.
                Assert.Equal(("Group Name / ", "Group Name,Rec / "), Kind(() =>
                {
                    Undo.RecordObject(a.transform, "Rec");
                    a.transform.position = Vector3.one * 3f;
                    Undo.SetCurrentGroupName("Group Name");
                }));
                // One undo takes both entries of a created object.
                Undo.ClearAll();
                var undone = Made("K7");
                Undo.RegisterCreatedObjectUndo(undone, "Made");
                Undo.IncrementCurrentGroup();
                Undo.PerformUndo();
                Assert.True(undone == null);
                Assert.Equal(" / Made,Made", Records());
            }
            finally
            {
                Undo.ClearAll();
                foreach (var go in made)
                    if (go != null) Object.DestroyImmediate(go);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(a);
            }
        }
    }
}
