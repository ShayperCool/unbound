using UnityEditor;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    public class SerializedColliderTests
    {
        static Collider Add(GameObject owner, string kind) => kind switch
        {
            "sphere" => owner.AddComponent<SphereCollider>(),
            "capsule" => owner.AddComponent<CapsuleCollider>(),
            _ => owner.AddComponent<BoxCollider>(),
        };

        [Theory]
        [InlineData("box")]
        [InlineData("sphere")]
        [InlineData("capsule")]
        public void BufferedColliderEditsReachLivePropertiesAndRefreshAfterExternalChanges(string kind)
        {
            var owner = new GameObject("serialized collider");
            var material = new PhysicsMaterial("serialized material");
            try
            {
                var collider = Add(owner, kind);
                using var serialized = new SerializedObject(collider);
                var enabled = serialized.FindProperty("m_Enabled");
                Assert.NotNull(enabled);
                enabled.boolValue = false;
                serialized.FindProperty("m_IsTrigger").boolValue = true;
                serialized.FindProperty("m_ProvidesContacts").boolValue = true;
                serialized.FindProperty("m_IncludeLayers").intValue = 1 << 12;
                serialized.FindProperty("m_ExcludeLayers").intValue = 1 << 11;
                serialized.FindProperty("m_LayerOverridePriority").intValue = 7;
                serialized.FindProperty("m_Material").objectReferenceValue = material;
                Assert.True(collider.enabled);
                Assert.False(collider.isTrigger);
                Assert.True(serialized.ApplyModifiedProperties());
                Assert.False(collider.enabled);
                Assert.True(collider.isTrigger);
                Assert.True(collider.providesContacts);
                Assert.Equal(1 << 12, collider.includeLayers.value);
                Assert.Equal(1 << 11, collider.excludeLayers.value);
                Assert.Equal(7, collider.layerOverridePriority);
                Assert.Same(material, collider.sharedMaterial);
                collider.enabled = true;
                Assert.False(serialized.FindProperty("m_Enabled").boolValue);
                serialized.Update();
                Assert.True(serialized.FindProperty("m_Enabled").boolValue);
                Assert.False(serialized.ApplyModifiedProperties());
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(material);
            }
        }

        [Fact]
        public void BoxGeometryChangesOnlyOnApplyAndSupportsVectorComponentEdits()
        {
            var owner = new GameObject("serialized box");
            try
            {
                var box = owner.AddComponent<BoxCollider>();
                using var serialized = new SerializedObject(box);
                var size = serialized.FindProperty("m_Size");
                Assert.NotNull(size);
                Assert.Equal("Vector3", size.type);
                Assert.Equal("Vector3", serialized.FindProperty("m_Center").type);
                size.vector3Value = new Vector3(2, 4, 6);
                serialized.FindProperty("m_Center").FindPropertyRelative("x").floatValue = 4;
                Assert.Equal(Vector3.one, box.size);
                Assert.Equal(Vector3.zero, box.center);
                Assert.True(serialized.ApplyModifiedProperties());
                Assert.Equal(new Vector3(2, 4, 6), box.size);
                Assert.Equal(new Vector3(4, 0, 0), box.center);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Fact]
        public void MultiObjectComponentEditsPreserveOtherComponentsPerTarget()
        {
            var first = ScriptableObject.CreateInstance<SerializedSample>();
            var second = ScriptableObject.CreateInstance<SerializedSample>();
            try
            {
                first.box = new Bounds(new Vector3(1, 2, 3), Vector3.one * 2);
                second.box = new Bounds(new Vector3(9, 8, 7), Vector3.one * 4);
                using var serialized = new SerializedObject(new Object[] { first, second });
                serialized.FindProperty("box").FindPropertyRelative("m_Center").FindPropertyRelative("x").floatValue = 1;
                Assert.True(serialized.ApplyModifiedProperties());
                Assert.Equal(new Vector3(1, 8, 7), second.box.center);
                Assert.Equal(Vector3.one * 4, second.box.size);
                serialized.FindProperty("box").FindPropertyRelative("m_Center").FindPropertyRelative("x").floatValue = 4;
                Assert.True(serialized.ApplyModifiedProperties());
                Assert.Equal(new Vector3(4, 8, 7), second.box.center);
                Assert.Equal(new Vector3(4, 2, 3), first.box.center);
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Theory]
        [InlineData("2022.3.10f1", "PPtr<PhysicMaterial>")]
        [InlineData("6000.0.71f1", "PPtr<PhysicsMaterial>")]
        public void MaterialPropertyUsesTheReleaseTypeName(string release, string typeName)
        {
            string previous = ProjectInfo.UnityVersion;
            var owner = new GameObject("serialized material name");
            try
            {
                ProjectInfo.UnityVersion = release;
                using var serialized = new SerializedObject(owner.AddComponent<BoxCollider>());
                Assert.NotNull(serialized.FindProperty("m_Material"));
                Assert.Equal(typeName, serialized.FindProperty("m_Material").type);
            }
            finally { ProjectInfo.UnityVersion = previous; Object.DestroyImmediate(owner); }
        }

        [Fact]
        public void CommonColliderFieldsEditDifferentColliderTypesTogether()
        {
            var first = new GameObject("serialized box");
            var second = new GameObject("serialized sphere");
            try
            {
                var box = first.AddComponent<BoxCollider>();
                var sphere = second.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                using var serialized = new SerializedObject(new Object[] { box, sphere });
                var trigger = serialized.FindProperty("m_IsTrigger");
                Assert.NotNull(trigger);
                Assert.True(trigger.hasMultipleDifferentValues);
                Assert.False(trigger.boolValue);
                trigger.boolValue = false;
                Assert.True(serialized.ApplyModifiedProperties());
                Assert.False(box.isTrigger);
                Assert.False(sphere.isTrigger);
                serialized.FindProperty("m_IncludeLayers").FindPropertyRelative("m_Bits").intValue = 1 << 13;
                Assert.True(serialized.ApplyModifiedProperties());
                Assert.Equal(1 << 13, box.includeLayers.value);
                Assert.Equal(1 << 13, sphere.includeLayers.value);
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }
    }
}
