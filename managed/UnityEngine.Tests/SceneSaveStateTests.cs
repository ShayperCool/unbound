using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public class SavedCounter : ScriptableObject
    {
        public int count;
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

        [Fact]
        public void DeleteAssetDeletesAnAssetWithItsMetaAndNothingElse()
        {
            var previous = ProjectInfo.ProjectPath;
            var project = Directory.CreateTempSubdirectory("unbound-delete-").FullName;
            try
            {
                ProjectInfo.ProjectPath = project;
                Directory.CreateDirectory(Path.Combine(project, "Assets"));
                Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
                File.WriteAllText(Path.Combine(project, "Assets/Old.unity"), "x");
                File.WriteAllText(Path.Combine(project, "Assets/Old.unity.meta"), "x");
                File.WriteAllText(Path.Combine(project, "ProjectSettings/TagManager.asset"), "x");
                Assert.True(AssetDatabase.DeleteAsset("Assets/Old.unity"));
                Assert.False(File.Exists(Path.Combine(project, "Assets/Old.unity")));
                Assert.False(File.Exists(Path.Combine(project, "Assets/Old.unity.meta")));
                Assert.False(AssetDatabase.DeleteAsset("Assets/Old.unity"));
                // Not assets: refused, the files stay.
                Assert.False(AssetDatabase.DeleteAsset("ProjectSettings/TagManager.asset"));
                Assert.False(AssetDatabase.DeleteAsset("Assets/../ProjectSettings/TagManager.asset"));
                Assert.False(AssetDatabase.DeleteAsset(Path.Combine(project, "ProjectSettings/TagManager.asset")));
                Assert.True(File.Exists(Path.Combine(project, "ProjectSettings/TagManager.asset")));
                Assert.False(AssetDatabase.DeleteAsset("Assets"));
            }
            finally
            {
                ProjectInfo.ProjectPath = previous;
                Directory.Delete(project, true);
            }
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
