using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public class SavedCounter : ScriptableObject
    {
        public int count;
    }

    public class SavedMeshHolder : ScriptableObject
    {
        public Mesh mesh;
    }

    public class SavedHolderUser : MonoBehaviour
    {
        public SavedMeshHolder holder;
    }

    /// <summary>
    /// What makes a scene or an asset need saving, and what saving refuses, as the Unity 6000.0.71f1
    /// Editor and its Scripting API have it (tests/projects/scenesave: SetDirty and a flushed Undo
    /// recording dirty an object's scene, SerializedObject.ApplyModifiedProperties dirties its
    /// targets; objects marked DontSave are not written; AssetDatabase.DeleteAsset deletes assets
    /// only).
    /// </summary>
    [Collection("Frames")]
    public class SceneSaveStateTests : IDisposable
    {
        readonly bool m_Playing = EditorSession.IsPlaying;
        readonly ILogHandler m_PreviousLog = Debug.unityLogger.logHandler;
        readonly Messages m_Messages = new Messages();

        sealed class Messages : ILogHandler
        {
            internal readonly List<string> Values = new List<string>();
            public void LogFormat(LogType type, Object context, string format, params object[] args) => Values.Add(string.Format(format, args));
            public void LogException(Exception exception, Object context) => Values.Add(exception.Message);
        }

        public SceneSaveStateTests()
        {
            Debug.unityLogger.logHandler = m_Messages;
        }

        public void Dispose()
        {
            EditorSession.IsPlaying = m_Playing;
            Debug.unityLogger.logHandler = m_PreviousLog;
        }

        static SceneData SceneOf(GameObject go) => SceneRegistry.Get(go.m_Scene);

        [Fact]
        public void SetDirtyOnASceneObjectDirtiesItsSceneInEditModeOnly()
        {
            var go = new GameObject("dirtied");
            var scene = SceneOf(go);
            bool wasDirty = scene.IsDirty;
            try
            {
                EditorSession.IsPlaying = true;
                scene.IsDirty = false;
                EditorUtility.SetDirty(go.transform);
                Assert.False(scene.IsDirty);

                EditorSession.IsPlaying = false;
                EditorUtility.SetDirty(go.transform);
                Assert.True(scene.IsDirty);
                Assert.True(EditorUtility.IsDirty(go.transform));
            }
            finally
            {
                scene.IsDirty = wasDirty;
                EditorUtility.ClearDirty(go.transform);
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void AFlushedRecordingDirtiesWhatChangedOnly()
        {
            EditorSession.IsPlaying = false;
            var moved = new GameObject("moved");
            var still = new GameObject("still");
            var scene = SceneOf(moved);
            bool wasDirty = scene.IsDirty;
            try
            {
                scene.IsDirty = false;
                Undo.RecordObject(moved.transform, "move");
                Undo.RecordObject(still.transform, "keep");
                moved.transform.position = new Vector3(1f, 2f, 3f);
                Assert.False(scene.IsDirty);
                Undo.FlushUndoRecordObjects();
                Assert.True(EditorUtility.IsDirty(moved.transform));
                Assert.False(EditorUtility.IsDirty(still.transform));
                Assert.True(scene.IsDirty);
            }
            finally
            {
                scene.IsDirty = wasDirty;
                EditorUtility.ClearDirty(moved.transform);
                Object.DestroyImmediate(moved);
                Object.DestroyImmediate(still);
            }
        }

        [Fact]
        public void ApplyingASerializedObjectDirtiesItsTarget()
        {
            var counter = ScriptableObject.CreateInstance<SavedCounter>();
            try
            {
                var serialized = new SerializedObject(counter);
                Assert.False(serialized.ApplyModifiedProperties());
                Assert.False(EditorUtility.IsDirty(counter));
                serialized.FindProperty("count").intValue = 4;
                Assert.True(serialized.ApplyModifiedProperties());
                Assert.Equal(4, counter.count);
                Assert.True(EditorUtility.IsDirty(counter));
            }
            finally
            {
                EditorUtility.ClearDirty(counter);
                Object.DestroyImmediate(counter);
            }
        }

        [Fact]
        public void ATransformListsOnlyTheChildrenThatAreSaved()
        {
            var parent = new GameObject("parent");
            var kept = new GameObject("kept");
            var temporary = new GameObject("temporary") { hideFlags = HideFlags.DontSave };
            kept.transform.SetParent(parent.transform, false);
            temporary.transform.SetParent(parent.transform, false);
            try
            {
                var ids = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance) { [kept.transform] = 7, [temporary.transform] = 8 };
                var body = SceneFiles.Body(parent.transform.WriteSerialized, ids, new AssetDatabaseRuntime.Locations());
                var children = body.Get("m_Children");
                Assert.Single(children.Items);
                Assert.Equal(7, SceneFiles.ParseFileId(children.Items[0]));
            }
            finally { Object.DestroyImmediate(parent); }
        }

        /// <summary>An untitled-then-named scene of a temporary project, active, with its file's text.</summary>
        static (SceneData Scene, string Project, SceneData Previous) TemporaryScene(string fileText)
        {
            var project = Directory.CreateTempSubdirectory("unbound-scene-").FullName;
            Directory.CreateDirectory(Path.Combine(project, "Assets"));
            File.WriteAllText(Path.Combine(project, "Assets/Saved.unity"), fileText);
            var previous = SceneRegistry.Active;
            var data = UnityEngine.SceneManagement.SceneManagerInternal.NewEmpty(additive: true).Data;
            data.Path = "Assets/Saved.unity";
            SceneRegistry.Active = data;
            return (data, project, previous);
        }

        static void Close(SceneData scene, SceneData previous)
        {
            foreach (var root in scene.Roots.ToArray()) Object.DestroyImmediate(root);
            SceneRegistry.Active = previous;
            SceneRegistry.Forget(scene);
        }

        const string FileWithEmbeddedMesh = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!43 &500\nMesh:\n  m_ObjectHideFlags: 0\n  m_Name: embedded\n  m_Unread: 7\n";

        [Fact]
        public void ASceneSaveKeepsItsEmbeddedObjectsAndRefusesWhatItCannotWrite()
        {
            // Measured on 6000.0 (other families keep their earlier, unmeasured behaviour).
            using var release = TestRelease.Pin(TestRelease.Measured);
            var previousProject = ProjectInfo.ProjectPath;
            var (scene, project, previous) = TemporaryScene(FileWithEmbeddedMesh);
            try
            {
                ProjectInfo.ProjectPath = project;
                EditorSession.IsPlaying = false;
                var mesh = new Mesh { name = "embedded" };
                scene.FileIds[mesh] = 500;
                var go = new GameObject("holder");
                Assert.Same(scene, SceneRegistry.Get(go.m_Scene));
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                // The embedded mesh stays as the file has it (named as kept as read), and the filter names it.
                var text = SceneFiles.Write(scene);
                Assert.NotNull(text);
                Assert.Contains(m_Messages.Values, m => m.Contains("Mesh objects as they were read"));
                Assert.Contains("--- !u!43 &500\nMesh:\n  m_ObjectHideFlags: 0\n  m_Name: embedded\n  m_Unread: 7\n", text);
                Assert.Contains("m_Mesh: {fileID: 500}", text);
                Assert.True(SceneFiles.Save(scene, "Assets/Saved.unity"));
                Assert.Equal(text, File.ReadAllText(Path.Combine(project, "Assets/Saved.unity")));
                // A new path gets the file (the scene itself keeps its path here); its .meta is the
                // engine's import's (tests/projects/scenesave Fresh.unity.meta).
                Assert.True(SceneFiles.Save(scene, Path.Combine(project, "Assets/New/Copy.unity")));
                Assert.Equal(text, File.ReadAllText(Path.Combine(project, "Assets/New/Copy.unity")));
                Assert.Empty(Directory.GetFiles(Path.Combine(project, "Assets/New"), "*.unbound-save"));

                // A mesh made by script is embedded by the Editor; Unbound refuses the save.
                filter.sharedMesh = new Mesh();
                Assert.Null(SceneFiles.Write(scene));
                Assert.Contains(m_Messages.Values, m => m.Contains("Mesh objects made by script"));
                // One marked DontSave is not saved: the reference is written as none.
                filter.sharedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                var unsaved = SceneFiles.Write(scene);
                Assert.NotNull(unsaved);
                Assert.Contains("m_Mesh: {fileID: 0}", unsaved);
                // The embedded mesh nothing references any more is not written.
                Assert.DoesNotContain("--- !u!43 &500", unsaved);
                filter.sharedMesh = mesh;

                // A new component Unbound has no writer for would be saved with the release's defaults.
                var source = go.AddComponent<AudioSource>();
                m_Messages.Values.Clear();
                Assert.False(SceneFiles.Save(scene, "Assets/Saved.unity"));
                Assert.Contains(m_Messages.Values, m => m.Contains("new AudioSource components"));
                Assert.Equal(text, File.ReadAllText(Path.Combine(project, "Assets/Saved.unity")));
                Object.DestroyImmediate(source);

                // Lighting Settings made by script are no asset to name.
                scene.LightingSettings = new LightingSettings();
                m_Messages.Values.Clear();
                Assert.Null(SceneFiles.Write(scene));
                Assert.Contains(m_Messages.Values, m => m.Contains("Lighting Settings"));
                scene.LightingSettings = null;

                // Paths outside Assets/, an unloaded scene and a failed write are refused.
                Assert.False(SceneFiles.Save(scene, "../Escaped.unity"));
                Assert.False(SceneFiles.Save(scene, "Assets/Saved.txt"));
                Directory.CreateDirectory(Path.Combine(project, "Assets/Taken.unity"));
                Assert.False(SceneFiles.Save(scene, "Assets/Taken.unity"));
                Assert.False(File.Exists(Path.Combine(project, "Assets/Taken.unity.unbound-save")));
                scene.IsLoaded = false;
                Assert.False(SceneFiles.Save(scene, "Assets/Saved.unity"));
                scene.IsLoaded = true;
                Assert.Equal(text, File.ReadAllText(Path.Combine(project, "Assets/Saved.unity")));
            }
            finally
            {
                Close(scene, previous);
                ProjectInfo.ProjectPath = previousProject;
                Directory.Delete(project, true);
            }
        }

        [Fact]
        public void AnEmbeddedObjectDroppedFromTheFileIsNoObjectOfItAnyMore()
        {
            // Measured on 6000.0 (other families keep their earlier, unmeasured behaviour).
            using var release = TestRelease.Pin(TestRelease.Measured);
            var previousProject = ProjectInfo.ProjectPath;
            var (scene, project, previous) = TemporaryScene(FileWithEmbeddedMesh);
            try
            {
                ProjectInfo.ProjectPath = project;
                EditorSession.IsPlaying = false;
                var mesh = new Mesh { name = "embedded" };
                scene.FileIds[mesh] = 500;
                var filter = new GameObject("holder").AddComponent<MeshFilter>();
                filter.sharedMesh = null;
                Assert.True(SceneFiles.Save(scene, "Assets/Saved.unity"));
                Assert.DoesNotContain("--- !u!43 &500", File.ReadAllText(Path.Combine(project, "Assets/Saved.unity")));
                // Referenced again, it is an object made by script the Editor would embed: refused.
                filter.sharedMesh = mesh;
                m_Messages.Values.Clear();
                Assert.False(SceneFiles.Save(scene, "Assets/Saved.unity"));
                Assert.Contains(m_Messages.Values, m => m.Contains("Mesh objects made by script"));
            }
            finally
            {
                Close(scene, previous);
                ProjectInfo.ProjectPath = previousProject;
                Directory.Delete(project, true);
            }
        }

        [Fact]
        public void ASavedCopyLeavesTheScenesEmbeddedObjectsItsOwn()
        {
            // Measured on 6000.0 (other families keep their earlier, unmeasured behaviour).
            using var release = TestRelease.Pin(TestRelease.Measured);
            var previousProject = ProjectInfo.ProjectPath;
            var (scene, project, previous) = TemporaryScene(FileWithEmbeddedMesh);
            try
            {
                ProjectInfo.ProjectPath = project;
                EditorSession.IsPlaying = false;
                var mesh = new Mesh { name = "embedded" };
                scene.FileIds[mesh] = 500;
                var filter = new GameObject("holder").AddComponent<MeshFilter>();
                // A copy that drops the unreferenced mesh leaves the scene's own file holding it.
                Assert.True(SceneFiles.Save(scene, "Assets/Copy.unity"));
                Assert.DoesNotContain("--- !u!43 &500", File.ReadAllText(Path.Combine(project, "Assets/Copy.unity")));
                filter.sharedMesh = mesh;
                Assert.True(SceneFiles.Save(scene, "Assets/Saved.unity"));
                var text = File.ReadAllText(Path.Combine(project, "Assets/Saved.unity"));
                Assert.Contains("--- !u!43 &500", text);
                Assert.Contains("m_Mesh: {fileID: 500}", text);
            }
            finally
            {
                Close(scene, previous);
                ProjectInfo.ProjectPath = previousProject;
                Directory.Delete(project, true);
            }
        }

        [Fact]
        public void AnEmbeddedObjectsReferencesAreCheckedAsTheScenesAre()
        {
            // Measured on 6000.0 (other families keep their earlier, unmeasured behaviour).
            using var release = TestRelease.Pin(TestRelease.Measured);
            var previousProject = ProjectInfo.ProjectPath;
            var (scene, project, previous) = TemporaryScene("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &600\nMonoBehaviour:\n  m_Name: embedded\n  mesh: {fileID: 0}\n");
            try
            {
                ProjectInfo.ProjectPath = project;
                EditorSession.IsPlaying = false;
                var holder = ScriptableObject.CreateInstance<SavedMeshHolder>();
                holder.name = "embedded";
                scene.FileIds[holder] = 600;
                var user = new GameObject("user").AddComponent<SavedHolderUser>();
                user.holder = holder;
                var text = SceneFiles.Write(scene);
                Assert.Contains("holder: {fileID: 600}", text);
                Assert.Contains("--- !u!114 &600", text);
                holder.mesh = new Mesh();
                m_Messages.Values.Clear();
                Assert.Null(SceneFiles.Write(scene));
                Assert.Contains(m_Messages.Values, m => m.Contains("Mesh objects made by script"));
            }
            finally
            {
                Close(scene, previous);
                ProjectInfo.ProjectPath = previousProject;
                Directory.Delete(project, true);
            }
        }

        [Fact]
        public void ALinkedSceneFileIsWrittenWhereItLinksTo()
        {
            // Measured on 6000.0 (other families keep their earlier, unmeasured behaviour).
            using var release = TestRelease.Pin(TestRelease.Measured);
            var previousProject = ProjectInfo.ProjectPath;
            var (scene, project, previous) = TemporaryScene(FileWithEmbeddedMesh);
            try
            {
                ProjectInfo.ProjectPath = project;
                EditorSession.IsPlaying = false;
                var target = Path.Combine(project, "Shared.unity");
                File.WriteAllText(target, "old");
                var link = Path.Combine(project, "Assets/Linked.unity");
                File.CreateSymbolicLink(link, target);
                Assert.True(SceneFiles.Save(scene, "Assets/Linked.unity"));
                Assert.NotNull(new FileInfo(link).LinkTarget);
                Assert.StartsWith("%YAML 1.1", File.ReadAllText(target));
                Assert.Empty(Directory.GetFiles(project, "*.unbound-save", SearchOption.AllDirectories));
            }
            finally
            {
                Close(scene, previous);
                ProjectInfo.ProjectPath = previousProject;
                Directory.Delete(project, true);
            }
        }

        [Fact]
        public void AnAssetFileNotInTheTextSerializationIsNotWritten()
        {
            var counter = ScriptableObject.CreateInstance<SavedCounter>();
            try
            {
                var objects = new Dictionary<long, Object> { [11400000] = counter };
                var dirty = new List<Object> { counter };
                Assert.Null(AssetFiles.TextDocuments("\0\0\0\u0016binary serialized file", objects, dirty));
                Assert.Null(AssetFiles.TextDocuments(UnityYamlText.Preamble + "--- !u!114 &2\nMonoBehaviour:\n  m_Name: other\n", objects, dirty));
                var docs = AssetFiles.TextDocuments(UnityYamlText.Preamble + "--- !u!114 &11400000\nMonoBehaviour:\n  m_Name: x\n", objects, dirty);
                Assert.Single(docs);
            }
            finally { Object.DestroyImmediate(counter); }
        }

        [Fact]
        public void SettingsNumbersUseTheSameLayout()
        {
            Assert.Equal("1e+10", TypeTreeSerialized.Number(1e10f));
            Assert.Equal("0.0000001", TypeTreeSerialized.Number(1e-7f));
            Assert.Equal("1e+17", TypeTreeSerialized.Number(1e17));
        }

        [Fact]
        public void AMaterialListItemIsMatchedByItsPropertyName()
        {
            var old = UnityYamlText.Parse("--- !u!21 &1\nMaterial:\n  m_TexEnvs:\n  - _BumpMap:\n      m_Texture: {fileID: 5, guid: 0000000000000000e000000000000000, type: 0}\n  - _MainTex:\n      m_Texture: {fileID: 9, guid: 0000000000000000e000000000000000, type: 0}\n")[0].Body;
            // The written list lost _BumpMap: _MainTex keeps its own texture, not the first item's.
            var written = UnityYamlText.Parse("--- !u!21 &1\nMaterial:\n  m_TexEnvs:\n  - _MainTex:\n      m_Texture: " + UnityYamlWriter.Writer.UnlocatedReference + "\n")[0].Body;
            SceneFiles.Overlay(old, written);
            var item = old.Get("m_TexEnvs").Items[0];
            Assert.Equal("9", item.Get("_MainTex").Get("m_Texture").Get("fileID").Text);
        }
    }
}
