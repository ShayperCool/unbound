using UnityEngine;
using Xunit;
using UObject = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>The camera the sky's capture keeps is the engine's own: an environment update
    /// adds no root to a scene and moves no sibling index (Unity 6000.0.71f1:
    /// tests/projects/coreapis, oroorder and instantiateasync record the roots' indices in scenes
    /// with a skybox).</summary>
    [Collection("Frames")]
    public sealed class EnvironmentCaptureCameraTests
    {
        [Fact]
        public void TheCaptureCameraIsInNoScene()
        {
            if (DynamicGI.s_SkyCamera != null) UObject.DestroyImmediate(DynamicGI.s_SkyCamera.gameObject);
            var before = new GameObject("Before");
            GameObject after = null;
            try
            {
                var scene = before.scene;
                int roots = scene.rootCount;

                var camera = DynamicGI.SkyCamera();
                Assert.Same(camera, DynamicGI.SkyCamera());
                var owner = camera.gameObject;
                Assert.Equal(HideFlags.HideAndDontSave, owner.hideFlags);
                Assert.False(camera.enabled);
                Assert.False(owner.scene.IsValid());
                Assert.Equal(roots, scene.rootCount);
                Assert.DoesNotContain(owner, scene.GetRootGameObjects());

                // The next root follows the last one, as it does without a capture.
                after = new GameObject("After");
                Assert.Equal(scene, after.scene);
                Assert.Equal(before.transform.GetSiblingIndex() + 1, after.transform.GetSiblingIndex());
                Assert.Equal(roots + 1, scene.rootCount);

                // The engine still finds its camera, and destroying it lets the next capture make one.
                Assert.Contains(camera, Resources.FindObjectsOfTypeAll<Camera>());
                UObject.DestroyImmediate(owner);
                Assert.Equal(roots + 1, scene.rootCount);
                Assert.NotSame(camera, DynamicGI.SkyCamera());
                Assert.Equal(roots + 1, scene.rootCount);
            }
            finally
            {
                UObject.DestroyImmediate(before);
                if (after != null) UObject.DestroyImmediate(after);
            }
        }
    }
}
