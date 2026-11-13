using Unbound.Runtime;
using Unbound.Tests;
using UnityEditor;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class MetadataReferenceHolder : MonoBehaviour { public Collider target; }

    public class SerializedMetadataTests
    {
        [Fact]
        public void MetadataEditsAreBufferedAndStaticFlagWritesReachThePublicProperty()
        {
            var go = new GameObject("metadata");
            var icon = new Texture2D(1, 1);
            try
            {
                using var so = new SerializedObject(go);
                var flags = so.FindProperty("m_StaticEditorFlags");
                Assert.NotNull(flags);
                Assert.Equal("uint", flags.type);
                flags.intValue = 9;
                so.FindProperty("m_NavMeshLayer").intValue = 7;
                so.FindProperty("m_Icon").objectReferenceValue = icon;
                Assert.False(go.isStatic);
                Assert.True(so.ApplyModifiedProperties());
                Assert.True(go.isStatic);
                so.Update();
                Assert.Equal(9, so.FindProperty("m_StaticEditorFlags").intValue);
                Assert.Equal(7, so.FindProperty("m_NavMeshLayer").intValue);
                Assert.Same(icon, so.FindProperty("m_Icon").objectReferenceValue);
                go.isStatic = false;
                Assert.Equal(9, so.FindProperty("m_StaticEditorFlags").intValue);
                so.Update();
                Assert.Equal(0, so.FindProperty("m_StaticEditorFlags").intValue);
                go.isStatic = true;
                so.Update();
                Assert.Equal(int.MaxValue, so.FindProperty("m_StaticEditorFlags").intValue);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(icon); }
        }

        [Fact]
        public void EulerHintsStayIndependentOfRuntimeRotationAndClonesResetOnlyScaleLock()
        {
            var go = new GameObject("hint");
            GameObject copy = null;
            try
            {
                using var so = new SerializedObject(go.transform);
                var hint = so.FindProperty("m_LocalEulerAnglesHint");
                Assert.NotNull(hint);
                hint.vector3Value = new Vector3(13, 371, -26);
                so.FindProperty("m_ConstrainProportionsScale").boolValue = true;
                Assert.True(so.ApplyModifiedProperties());
                Assert.Equal(Quaternion.identity, go.transform.localRotation);
                go.transform.localEulerAngles = new Vector3(30, 50, 70);
                go.transform.localScale = new Vector3(2, 3, 4);
                so.Update();
                Assert.Equal(new Vector3(13, 371, -26), so.FindProperty("m_LocalEulerAnglesHint").vector3Value);
                Assert.True(so.FindProperty("m_ConstrainProportionsScale").boolValue);
                Assert.Equal(new Vector3(2, 3, 4), go.transform.localScale);
                copy = Object.Instantiate(go);
                using var clone = new SerializedObject(copy.transform);
                Assert.Equal(new Vector3(13, 371, -26), clone.FindProperty("m_LocalEulerAnglesHint").vector3Value);
                Assert.False(clone.FindProperty("m_ConstrainProportionsScale").boolValue);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(go); }
        }

        [Fact]
        public void ComponentSlotsRefuseStructuralEditsButApplyReferencesWithinTheOwner()
        {
            var go = new GameObject("slots");
            var box = go.AddComponent<BoxCollider>();
            var sphere = go.AddComponent<SphereCollider>();
            try
            {
                using var so = new SerializedObject(go);
                var components = so.FindProperty("m_Component");
                Assert.NotNull(components);
                Assert.Equal(3, components.arraySize);
                Assert.False(components.MoveArrayElement(2, 1));
                components.arraySize = 1;
                Assert.False(so.ApplyModifiedProperties());
                Assert.Equal(3, components.arraySize);
                components.GetArrayElementAtIndex(1).FindPropertyRelative("component").objectReferenceValue = sphere;
                Assert.Same(box, go.GetComponents<Component>()[1]);
                Assert.True(so.ApplyModifiedProperties());
                Assert.Same(sphere, go.GetComponents<Component>()[1]);
                Assert.Same(sphere, go.GetComponents<Component>()[2]);
                Object.DestroyImmediate(go);
                Assert.False(box == null);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(box); }
        }

        [Fact]
        public void ReorderingThreeChildrenChangesTheLiveOrderWithoutReparenting()
        {
            var go = new GameObject("children");
            var one = new GameObject("one");
            var two = new GameObject("two");
            var three = new GameObject("three");
            one.transform.SetParent(go.transform, false);
            two.transform.SetParent(go.transform, false);
            three.transform.SetParent(go.transform, false);
            try
            {
                using var so = new SerializedObject(go.transform);
                var children = so.FindProperty("m_Children");
                Assert.NotNull(children);
                Assert.True(children.MoveArrayElement(2, 0));
                Assert.Same(one.transform, go.transform.GetChild(0));
                Assert.True(so.ApplyModifiedProperties());
                Assert.Same(three.transform, go.transform.GetChild(0));
                Assert.Same(one.transform, go.transform.GetChild(1));
                Assert.Same(two.transform, go.transform.GetChild(2));
                Assert.Same(go.transform, three.transform.parent);
                so.Update();
                Assert.Same(three.transform, so.FindProperty("m_Children").GetArrayElementAtIndex(0).objectReferenceValue);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void JsonMetadataOverwriteKeepsHierarchyLinksOwnedByTheGraph()
        {
            var go = new GameObject("JSON metadata");
            var child = new GameObject("child");
            child.transform.SetParent(go.transform, false);
            try
            {
                EditorJsonUtility.FromJsonOverwrite("{\"Transform\":{\"m_Children\":[],\"m_ConstrainProportionsScale\":true}}", go.transform);
                Assert.Same(child.transform, go.transform.GetChild(0));
                Assert.True(new SerializedObject(go.transform).FindProperty("m_ConstrainProportionsScale").boolValue);
                EditorJsonUtility.FromJsonOverwrite("{\"GameObject\":{\"m_Component\":[],\"m_NavMeshLayer\":5}}", go);
                Assert.Same(go.transform, go.GetComponents<Component>()[0]);
                Assert.Equal(5, new SerializedObject(go).FindProperty("m_NavMeshLayer").intValue);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void CloningRepeatedComponentSlotsProducesOneObjectAndRemapsScriptReferences()
        {
            var go = new GameObject("repeated slots");
            var box = go.AddComponent<BoxCollider>();
            var sphere = go.AddComponent<SphereCollider>();
            var holder = go.AddComponent<MetadataReferenceHolder>();
            holder.target = sphere;
            GameObject copy = null;
            try
            {
                using var so = new SerializedObject(go);
                so.FindProperty("m_Component").GetArrayElementAtIndex(1).FindPropertyRelative("component").objectReferenceValue = sphere;
                Assert.True(so.ApplyModifiedProperties());
                Assert.Equal(4, go.GetComponentCount());
                copy = Object.Instantiate(go);
                Assert.Equal(3, copy.GetComponentCount());
                Assert.Single(copy.GetComponents<SphereCollider>());
                Assert.Same(copy.GetComponent<SphereCollider>(), copy.GetComponent<MetadataReferenceHolder>().target);
                Assert.NotSame(sphere, copy.GetComponent<MetadataReferenceHolder>().target);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(go); Object.DestroyImmediate(box); }
        }

        [Fact]
        public void RectTransformJsonOmitsHierarchyLinksAndRetainsMetadata()
        {
            var go = new GameObject("rect", typeof(RectTransform));
            var child = new GameObject("rect child", typeof(RectTransform));
            child.transform.SetParent(go.transform, false);
            try
            {
                using var so = new SerializedObject(go.transform);
                so.FindProperty("m_ConstrainProportionsScale").boolValue = true;
                Assert.True(so.ApplyModifiedProperties());
                var json = EditorJsonUtility.ToJson(go.transform);
                Assert.DoesNotContain("\"m_Children\"", json);
                Assert.Contains("\"m_ConstrainProportionsScale\":true", json);
                Assert.Same(child.transform, go.transform.GetChild(0));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void AuthoredMetadataIsPreservedWhenTheObjectGraphLoads()
        {
            var documents = UnityYaml.Parse("""
                %YAML 1.1
                %TAG !u! tag:unity3d.com,2011:
                --- !u!1 &1
                GameObject:
                  m_Name: Authored
                  m_StaticEditorFlags: 9
                  m_NavMeshLayer: 3
                  m_Component:
                  - component: {fileID: 2}
                --- !u!4 &2
                Transform:
                  m_GameObject: {fileID: 1}
                  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
                  m_LocalScale: {x: 1, y: 1, z: 1}
                  m_ConstrainProportionsScale: 1
                  m_LocalEulerAnglesHint: {x: 11, y: 372, z: -20}
                """);
            var file = ObjectGraphBuilder.Build(documents, "metadata.prefab", "", ObjectGraphBuilder.Kind.PrefabAsset, null);
            var go = (GameObject)file.Objects[1];
            try
            {
                using var so = new SerializedObject(go);
                Assert.Equal(9, so.FindProperty("m_StaticEditorFlags").intValue);
                Assert.Equal(3, so.FindProperty("m_NavMeshLayer").intValue);
                using var transform = new SerializedObject(go.transform);
                Assert.True(transform.FindProperty("m_ConstrainProportionsScale").boolValue);
                Assert.Equal(new Vector3(11, 372, -20), transform.FindProperty("m_LocalEulerAnglesHint").vector3Value);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
