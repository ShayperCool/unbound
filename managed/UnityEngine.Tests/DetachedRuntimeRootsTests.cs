using System;
using System.Linq;
using Unbound.Runtime;
using Xunit;
using UObject = UnityEngine.Object;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public sealed class DetachedRuntimeRootsTests
    {
        [Fact]
        public void StoppingPlayKeepsDetachedHierarchyRegisteredUntilShutdown()
        {
            using var scenes = new Unbound.Tests.LightProbesTests.Scenes();
            var detached = SceneRegistry.Detached;
            var persistent = SceneRegistry.DontDestroyOnLoad;
            var savedDetached = detached.Roots.ToArray();
            var savedPersistent = persistent.Roots.ToArray();
            detached.Roots.Clear();
            persistent.Roots.Clear();
            scenes.Loaded("Detached lifetime");
            var root = GameObject.CreateDetached("internal root");
            var child = new GameObject("internal child");
            child.transform.parent = root.transform;
            var witness = child.AddComponent<RecordsEnableDisable>();
            var camera = root.AddComponent<Camera>();
            try
            {
                EditorSession.DestroyPlayModeObjects();
                SceneRegistry.ResetToUntitled();
                Assert.True(root != null && child != null && camera != null);
                Assert.Contains(root, detached.Roots);
                Assert.Contains(root, SceneRegistry.AllGameObjects(true));
                Assert.Contains(child, SceneRegistry.AllGameObjects(true));
                Assert.Equal(new[] { "enable" }, witness.log);
                // Final process cleanup, unlike stopping Play, also owns the internal holders.
                EditorSession.DestroyPlayModeObjects(destroyDetached: true);
                Assert.True(root == null);
                Assert.True(child == null);
                Assert.True(camera == null);
                Assert.True(witness == null);
                Assert.Equal(new[] { "enable", "disable" }, witness.log);
                Assert.Empty(detached.Roots);
                EditorSession.DestroyPlayModeObjects();
                Assert.Equal(new[] { "enable", "disable" }, witness.log);
            }
            finally
            {
                UObject.DestroyImmediate(root);
                UObject.DestroyImmediate(child);
                foreach (var scene in SceneRegistry.Loaded.ToArray()) SceneRegistry.Forget(scene);
                detached.Roots.Clear();
                detached.Roots.AddRange(savedDetached);
                persistent.Roots.Clear();
                persistent.Roots.AddRange(savedPersistent);
            }
        }

        [Fact]
        public void ParticleModuleLightsStayOutsideSceneRootsAndReuseTheirDetachedHolders()
        {
            using var scenes = new Unbound.Tests.LightProbesTests.Scenes();
            var scene = scenes.Loaded("Module lights");
            var detached = SceneRegistry.Detached;
            var previous = detached.Roots.ToArray();
            detached.Roots.Clear();
            var template = new GameObject("template").AddComponent<Light>();
            var particles = new GameObject("particles").AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.startLifetime = 1000;
            var lights = particles.lights;
            lights.enabled = true;
            lights.light = template;
            lights.ratio = 1;
            lights.maxLights = 2;
            particles.Emit(2);
            try
            {
                particles.UpdateModuleLights();
                Assert.Equal(2, scene.Roots.Count);
                Assert.Equal(2, detached.Roots.Count);
                var holders = detached.Roots.ToArray();
                particles.UpdateModuleLights();
                Assert.Equal(holders, detached.Roots);
                lights.enabled = false;
                particles.UpdateModuleLights();
                Assert.Equal(holders, detached.Roots);
                Assert.All(holders, go => Assert.False(go.GetComponent<Light>().enabled));
                particles.DestroyModuleLights();
                Assert.All(holders, go => Assert.True(go == null));
                Assert.Empty(detached.Roots);
                Assert.Equal(2, scene.Roots.Count);
            }
            finally
            {
                UObject.DestroyImmediate(particles.gameObject);
                UObject.DestroyImmediate(template.gameObject);
                foreach (var go in detached.Roots.ToArray()) UObject.DestroyImmediate(go);
                detached.Roots.Clear();
                detached.Roots.AddRange(previous);
            }
        }

        [Fact]
        public void DetachedAndPublicConstructorsEachRegisterOneTransformAndRoot()
        {
            using var scenes = new Unbound.Tests.LightProbesTests.Scenes();
            var scene = scenes.Loaded("Construction");
            var hidden = GameObject.CreateDetached("internal");
            var visible = new GameObject("visible", typeof(Light), typeof(RecordsEnableDisable));
            try
            {
                Assert.False(hidden.scene.IsValid());
                Assert.Equal(HideFlags.HideAndDontSave, hidden.hideFlags);
                Assert.Single(hidden.GetComponents<Transform>());
                Assert.Single(SceneRegistry.Detached.Roots.Where(go => ReferenceEquals(go, hidden)));
                Assert.Equal(new[] { visible }, scene.Roots);
                Assert.Equal(0, visible.transform.GetSiblingIndex());
                Assert.Single(visible.GetComponents<Transform>());
                Assert.NotNull(visible.GetComponent<Light>());
                Assert.Equal(new[] { "enable" }, visible.GetComponent<RecordsEnableDisable>().log);
            }
            finally
            {
                UObject.DestroyImmediate(hidden);
                UObject.DestroyImmediate(visible);
            }
        }
    }
}
