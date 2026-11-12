using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using UObject = UnityEngine.Object;

namespace Unbound.Tests
{
    [Collection("Frames")]
    public sealed class HideFlagsRootOwnershipTests : IDisposable
    {
        readonly string m_Release = ProjectInfo.UnityVersion;
        readonly bool m_Playing = EditorSession.IsPlaying;
        readonly LightProbesTests.Scenes m_Scenes = new LightProbesTests.Scenes();
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public HideFlagsRootOwnershipTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            EditorSession.IsPlaying = true;
        }

        public void Dispose()
        {
            foreach (var go in m_Objects) UObject.DestroyImmediate(go);
            m_Scenes.Dispose();
            EditorSession.IsPlaying = m_Playing;
            ProjectInfo.UnityVersion = m_Release;
        }

        GameObject Root(string name)
        {
            var go = new GameObject(name);
            m_Objects.Add(go);
            return go;
        }

        public sealed class Witness : MonoBehaviour
        {
            public int Enables, Disables;
            public Action DisableAction;
            public bool EnableSceneValid, DisableSceneValid;
            public HideFlags EnableFlags, DisableFlags;
            void OnEnable()
            {
                Enables++;
                EnableSceneValid = gameObject.scene.IsValid();
                EnableFlags = hideFlags;
            }
            void OnDisable()
            {
                Disables++;
                DisableSceneValid = gameObject.scene.IsValid();
                DisableFlags = hideFlags;
                DisableAction?.Invoke();
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(4, false)]
        [InlineData(8, false)]
        [InlineData(16, false)]
        [InlineData(32, false)]
        [InlineData(52, false)]
        [InlineData(5, false)]
        [InlineData(17, false)]
        [InlineData(20, false)]
        [InlineData(21, true)]
        [InlineData(53, true)]
        [InlineData(61, true)]
        public void RootOwnershipAndCallbacksFollowTheMeasuredFlagCombination(int flags, bool detached)
        {
            var original = m_Scenes.Loaded("Original");
            var root = Root("root");
            var witness = root.AddComponent<Witness>();
            var child = Root("child");
            child.transform.parent = root.transform;
            var childWitness = child.AddComponent<Witness>();
            root.hideFlags = (HideFlags)flags;

            Assert.Equal(!detached, root.scene.IsValid());
            Assert.Equal(!detached, child.scene.IsValid());
            Assert.Equal(!detached, original.Roots.Contains(root));
            Assert.Equal((HideFlags)flags, witness.hideFlags);
            Assert.Equal(HideFlags.None, child.hideFlags);
            Assert.Equal(HideFlags.None, childWitness.hideFlags);
            Assert.Equal(detached ? 2 : 1, witness.Enables);
            Assert.Equal(detached ? 1 : 0, witness.Disables);
            Assert.Equal(witness.Enables, childWitness.Enables);
            Assert.Equal(witness.Disables, childWitness.Disables);
            if (detached)
            {
                Assert.False(witness.EnableSceneValid);
                Assert.False(witness.DisableSceneValid);
                Assert.Equal(HideFlags.None, witness.EnableFlags);
                Assert.Equal(HideFlags.None, witness.DisableFlags);
                Assert.Contains(root, SceneRegistry.Detached.Roots);
            }

            var target = m_Scenes.Loaded("Target");
            SceneRegistry.Active = target;
            root.hideFlags = HideFlags.None;
            Assert.Equal(detached ? target.Handle : original.Handle, root.m_Scene);
            Assert.Equal(root.m_Scene, child.m_Scene);
            Assert.Equal(detached ? 2 : 1, witness.Enables);
            Assert.Equal(detached ? 1 : 0, witness.Disables);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DisabledAndInactiveScriptsStayDisabledThroughOwnershipChanges(bool inactive)
        {
            m_Scenes.Loaded("Disabled");
            var root = Root("root");
            var witness = root.AddComponent<Witness>();
            if (inactive) root.SetActive(false);
            else witness.enabled = false;
            root.hideFlags = HideFlags.HideAndDontSave;
            Assert.False(root.scene.IsValid());
            Assert.Equal(1, witness.Enables);
            Assert.Equal(1, witness.Disables);
            root.hideFlags = HideFlags.None;
            Assert.True(root.scene.IsValid());
            Assert.Equal(1, witness.Enables);
            Assert.Equal(1, witness.Disables);
        }

        [Fact]
        public void ComponentRemovalIsRefusedDuringHierarchyTransition()
        {
            m_Scenes.Loaded("Callback removal");
            var root = Root("root");
            var witness = root.AddComponent<Witness>();
            var collider = root.AddComponent<BoxCollider>();
            witness.DisableAction = () => UObject.DestroyImmediate(collider);
            root.hideFlags = HideFlags.HideAndDontSave;
            Assert.False(collider == null);
            Assert.True(collider.m_Active);
            witness.DisableAction = null;
        }

        [Fact]
        public void BehaviourDisableAllowsComponentRemoval()
        {
            m_Scenes.Loaded("Behaviour removal");
            var root = Root("root");
            var witness = root.AddComponent<Witness>();
            var collider = root.AddComponent<BoxCollider>();
            witness.DisableAction = () => UObject.DestroyImmediate(collider);
            witness.enabled = false;
            Assert.True(collider == null);
            witness.DisableAction = null;
        }

        [Fact]
        public void ChildComponentRemovalIsNotReactivated()
        {
            m_Scenes.Loaded("Child removal");
            var root = Root("root");
            var witness = root.AddComponent<Witness>();
            var child = Root("child");
            child.transform.parent = root.transform;
            var collider = child.AddComponent<BoxCollider>();
            witness.DisableAction = () => UObject.DestroyImmediate(collider);
            root.hideFlags = HideFlags.HideAndDontSave;
            Assert.True(collider == null);
            Assert.False(collider.m_Active);
            witness.DisableAction = null;
        }

        [Fact]
        public void ReentrantSetActiveDoesNotChangeActiveSelf()
        {
            m_Scenes.Loaded("Reentry");
            var root = Root("root");
            var witness = root.AddComponent<Witness>();
            witness.DisableAction = () => root.SetActive(false);
            root.hideFlags = HideFlags.HideAndDontSave;
            Assert.True(root.activeSelf);
            Assert.Equal(1, witness.Disables);
            Assert.Equal(2, witness.Enables);
            witness.DisableAction = null;
        }

        [Fact]
        public void OtherReleasesRetainTheirUnmeasuredSetterBehavior()
        {
            ProjectInfo.UnityVersion = "6000.0.70f1";
            var scene = m_Scenes.Loaded("Other release");
            var root = Root("root");
            root.hideFlags = HideFlags.HideAndDontSave;
            Assert.Equal(scene.Handle, root.m_Scene);
            Assert.True(root.scene.IsValid());
        }
    }
}
