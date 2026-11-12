using System;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    [Collection("Frames")]
    public sealed class MaterialReloadWrapperTests : IDisposable
    {
        readonly Material m_First = new Material { name = "saved" };
        public void Dispose() => Object.DestroyImmediate(m_First, true);

        [Fact]
        public void ReloadWrappersShareValuesAndHaveOneRegistryEntry()
        {
            var next = m_First.ReloadWrapper();
            Assert.NotSame(m_First, next);
            Assert.True(m_First == next);
            Assert.Equal(m_First.GetInstanceID(), next.GetInstanceID());
            Assert.Equal(m_First.GetHashCode(), next.GetHashCode());
            m_First.color = Color.green;
            Assert.Equal(Color.green, next.color);
            next.renderQueue = 2450;
            Assert.Equal(2450, m_First.renderQueue);
            next.enableInstancing = true;
            Assert.True(m_First.enableInstancing);
            m_First.SetFloat("_Custom", 3.5f);
            Assert.Equal(3.5f, next.GetFloat("_Custom"));
            m_First.name = "renamed";
            Assert.Equal("renamed", next.name);
            next.hideFlags = HideFlags.NotEditable;
            Assert.Equal(HideFlags.NotEditable, m_First.hideFlags);
            Assert.Same(next, ObjectRegistry.FindById(m_First.GetInstanceID()));
            Assert.Same(next, Assert.Single(ObjectRegistry.All(typeof(Material)).Where(m => m.GetInstanceID() == m_First.GetInstanceID())));
            using var clone = new OwnedMaterial(new Material(next));
            clone.Value.color = Color.blue;
            Assert.Equal(Color.green, next.color);
            Assert.False(clone.Value == next);
        }

        [Fact]
        public void AliasCopiesDoNotEraseSharedProperties()
        {
            var next = m_First.ReloadWrapper();
            m_First.color = Color.green;
            m_First.renderQueue = 2450;
            m_First.CopyPropertiesFromMaterial(next);
            next.CopyMatchingPropertiesFromMaterial(m_First);
            Assert.Equal(Color.green, next.color);
            Assert.Equal(2450, next.renderQueue);
        }

        [Fact]
        public void RestoringSavedValuesChangesAllWrappers()
        {
            string previous = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            try
            {
                var second = m_First.ReloadWrapper();
                second.name = "edited";
                second.hideFlags = HideFlags.NotEditable;
                second.color = Color.red;
                m_First.RevertTo(new Material { name = "saved", color = Color.white });
                var third = second.ReloadWrapper();
                Assert.Equal("saved", third.name);
                Assert.Equal("saved", second.name);
                Assert.Equal(HideFlags.None, m_First.hideFlags);
                Assert.Equal(Color.white, second.color);
                third.color = Color.blue;
                Assert.Equal(Color.blue, m_First.color);
                Assert.Equal(Color.blue, second.color);
            }
            finally { ProjectInfo.UnityVersion = previous; }
        }

        [Fact]
        public void DestroyingAnOldWrapperInvalidatesEveryAlias()
        {
            var second = m_First.ReloadWrapper();
            var third = second.ReloadWrapper();
            Object.DestroyImmediate(m_First, true);
            Assert.True(m_First == null);
            Assert.True(second == null);
            Assert.True(third == null);
            Assert.Null(ObjectRegistry.FindById(third.GetInstanceID()));
            Assert.Throws<MissingReferenceException>(() => _ = second.name);
        }

        [Fact]
        public void OtherReleasesKeepTheirUnmeasuredSavedMetadataBehavior()
        {
            string previous = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.70f1";
            try
            {
                m_First.name = "edited";
                m_First.hideFlags = HideFlags.NotEditable;
                m_First.RevertTo(new Material { name = "saved", color = Color.white });
                Assert.Equal("edited", m_First.name);
                Assert.Equal(HideFlags.NotEditable, m_First.hideFlags);
            }
            finally { ProjectInfo.UnityVersion = previous; }
        }

        public sealed class Holder : ScriptableObject
        {
            public Material material;
        }

        [Fact]
        public void SerializedAliasAssignmentPreservesTheExistingFieldReference()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            using var different = new OwnedMaterial(new Material());
            try
            {
                holder.material = m_First;
                var alias = m_First.ReloadWrapper();
                using var serialized = new UnityEditor.SerializedObject(holder);
                var property = serialized.FindProperty(nameof(Holder.material));
                property.objectReferenceValue = alias;
                Assert.False(serialized.hasModifiedProperties);
                Assert.False(serialized.ApplyModifiedProperties());
                Assert.Same(m_First, holder.material);
                property.objectReferenceValue = different.Value;
                Assert.True(serialized.hasModifiedProperties);
                Assert.True(serialized.ApplyModifiedProperties());
                Assert.Same(different.Value, holder.material);
            }
            finally { Object.DestroyImmediate(holder); }
        }

        sealed class OwnedMaterial : IDisposable
        {
            internal readonly Material Value;
            internal OwnedMaterial(Material value) => Value = value;
            public void Dispose() => Object.DestroyImmediate(Value, true);
        }
    }
}
