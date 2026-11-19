using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The stage and scene queries editor code and packages ask, as the Unity 6000.0.71f1 Editor
    /// answers them outside Prefab mode (tests/projects/scenesave prefab-api): the main stage for
    /// every scene object, the default scene culling mask, no preview scenes; a SerializedObject's
    /// native pointer until it is disposed; Undo.postprocessModifications on a recorded apply.
    /// </summary>
    [Collection("Frames")]
    public class StageQueriesTests
    {
        [Fact]
        public void TheMainStageHoldsEverySceneObject()
        {
            var go = new GameObject("staged");
            try
            {
                var scene = go.scene;
                Assert.Equal(SceneCullingMasks.DefaultSceneCullingMask, EditorSceneManager.GetSceneCullingMask(scene));
                Assert.Equal(16140901064495857664UL, SceneCullingMasks.DefaultSceneCullingMask);
                Assert.Equal(0UL, EditorSceneManager.GetSceneCullingMask(default));
                Assert.False(EditorSceneManager.IsPreviewScene(scene));
                Assert.False(EditorSceneManager.IsPreviewSceneObject(go));
                Assert.Null(PrefabStageUtility.GetCurrentPrefabStage());
                var main = StageUtility.GetMainStage();
                Assert.Same(main, StageUtility.GetCurrentStage());
                Assert.Same(main, StageUtility.GetStage(go));
                Assert.Same(main, StageUtility.GetStage(scene));
                Assert.Null(StageUtility.GetStage(default(Scene)));
                Assert.Equal(SceneCullingMasks.MainStageSceneViewObjects, main.GetCombinedSceneCullingMaskForCamera());
                // A root of a loaded scene stays where it is; a child stays a child; none is ignored.
                StageUtility.PlaceGameObjectInCurrentStage(go);
                Assert.Equal(scene, go.scene);
                var child = new GameObject("child");
                child.transform.SetParent(go.transform, false);
                StageUtility.PlaceGameObjectInCurrentStage(child);
                Assert.Same(go.transform, child.transform.parent);
                StageUtility.PlaceGameObjectInCurrentStage(null);
                Assert.Contains(go.GetComponent<Transform>(), main.FindComponentsOfType<Transform>());
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void APlainGameObjectIsItsOwnPrefabRoot()
        {
            var root = new GameObject("root");
            var child = new GameObject("child");
            child.transform.SetParent(root.transform, false);
            try
            {
#pragma warning disable CS0618
                // Not of an instance: the object itself (measured for a plain child).
                Assert.Same(root, PrefabUtility.FindPrefabRoot(root));
                Assert.Same(child, PrefabUtility.FindPrefabRoot(child));
                Assert.Throws<ArgumentNullException>(() => PrefabUtility.FindPrefabRoot(null));
#pragma warning restore CS0618
                Assert.Null(PrefabUtility.GetPropertyModifications(child));
                Assert.Equal("Provided GameObject is not a Prefab instance", Assert.Throws<ArgumentException>(() => PrefabUtility.GetObjectOverrides(root, true)).Message);
                Assert.Throws<ArgumentNullException>(() => PrefabUtility.GetObjectOverrides(null));
                Assert.Throws<ArgumentNullException>(() => PrefabUtility.FindAllInstancesOfPrefab(null));
                Assert.Equal("FindAllInstancesOfPrefab must be called with a Prefab asset.", Assert.Throws<ArgumentException>(() => PrefabUtility.FindAllInstancesOfPrefab(root)).Message);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Fact]
        public void ASerializedObjectHasANativePointerUntilDisposed()
        {
            var go = new GameObject("pointer");
            try
            {
                var field = typeof(SerializedObject).GetField("m_NativeObjectPtr", BindingFlags.Instance | BindingFlags.NonPublic);
                var serialized = new SerializedObject(go.transform);
                Assert.NotEqual(IntPtr.Zero, (IntPtr)field.GetValue(serialized));
                serialized.Dispose();
                Assert.Equal(IntPtr.Zero, (IntPtr)field.GetValue(serialized));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void ARecordedApplyPassesItsChangesToPostprocessModifications()
        {
            var go = new GameObject("moved");
            var seen = new List<string>();
            Undo.PostprocessModifications handler = modifications =>
            {
                foreach (var m in modifications) seen.Add($"{m.currentValue.target.name}|{m.currentValue.propertyPath}|{m.currentValue.value}|{m.previousValue.value}");
                return modifications;
            };
            Undo.postprocessModifications += handler;
            try
            {
                var serialized = new SerializedObject(go.transform);
                serialized.FindProperty("m_LocalPosition.x").floatValue = 9f;
                Assert.True(serialized.ApplyModifiedProperties());
                Assert.Equal(new[] { "moved|m_LocalPosition.x|9|0" }, seen);
                Assert.Equal(9f, go.transform.localPosition.x);
                // Without Undo nothing is recorded.
                seen.Clear();
                serialized.FindProperty("m_LocalPosition.y").floatValue = 2f;
                Assert.True(serialized.ApplyModifiedPropertiesWithoutUndo());
                Assert.Empty(seen);
            }
            finally
            {
                Undo.postprocessModifications -= handler;
                Object.DestroyImmediate(go);
            }
        }
    }
}
