using System;
using Unbound.Runtime;
using UnityEngine.Profiling;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public sealed class TransformHierarchyMemoryTests
    {
        [Fact]
        public void LoadedPrefabKeepsIndependentCapacityInTheMeasuredFamily()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            const string yaml = """
                --- !u!1 &1
                GameObject:
                  m_Name: Root
                --- !u!4 &2
                Transform:
                  m_GameObject: {fileID: 1}
                  m_Children:
                  - {fileID: 4}
                --- !u!1 &3
                GameObject:
                  m_Name: Child
                --- !u!4 &4
                Transform:
                  m_GameObject: {fileID: 3}
                  m_Father: {fileID: 2}
                """;
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "hierarchy.prefab", null, ObjectGraphBuilder.Kind.PrefabAsset, null);
            var root = file.Roots[0].transform;
            try
            {
                Assert.True(root.gameObject.m_IsPrefabAsset);
                var child = root.GetChild(0);
                Assert.Equal(1, root.hierarchyCapacity);
                Assert.Equal(1, child.hierarchyCapacity);
                Assert.Equal(2, root.hierarchyCount);
                Assert.Equal(1, child.hierarchyCount);
                child.hierarchyCapacity = 9;
                Assert.Equal(9, child.hierarchyCapacity);
                Assert.Equal(1, root.hierarchyCapacity);
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject, true);
            }
        }

        [Fact]
        public void CapacityWarningCannotLeaveTheCurrentTreeLargerThanItsReserve()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var owner = new GameObject("Capacity warning owner");
            var caller = new GameObject("Capacity warning caller");
            caller.transform.SetParent(owner.transform, false);
            owner.transform.hierarchyCapacity = 32;
            bool added = false;
            void AddChild(string message, string stack, LogType type)
            {
                if (added || type != LogType.Warning || !message.StartsWith("Changing desired transform hierarchy capacity", StringComparison.Ordinal)) return;
                added = true;
                new GameObject("Child added during warning").transform.SetParent(owner.transform, false);
            }
            Application.logMessageReceived += AddChild;
            try
            {
                caller.transform.hierarchyCapacity = 0;
                Assert.True(added);
                Assert.Equal(3, owner.transform.hierarchyCount);
                Assert.True(owner.transform.hierarchyCapacity >= owner.transform.hierarchyCount);
            }
            finally
            {
                Application.logMessageReceived -= AddChild;
                Object.DestroyImmediate(owner);
            }
        }

        [Fact]
        public void UnmeasuredFamilyKeepsIndependentHierarchyCapacityAndSubtreeCount()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var parent = new GameObject("Unmeasured hierarchy");
            var child = new GameObject("Unmeasured child");
            try
            {
                child.transform.SetParent(parent.transform, false);
                parent.transform.hierarchyCapacity = 13;
                child.transform.hierarchyCapacity = 4;

                Assert.Equal(13, parent.transform.hierarchyCapacity);
                Assert.Equal(4, child.transform.hierarchyCapacity);
                Assert.Equal(2, parent.transform.hierarchyCount);
                Assert.Equal(1, child.transform.hierarchyCount);
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(parent.transform));
            }
            finally
            {
                if (child != null) Object.DestroyImmediate(child);
                Object.DestroyImmediate(parent);
            }
        }
    }
}
