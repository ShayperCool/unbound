using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The halo falloff and flare layout values the Editor shows (tests/projects/flares).</summary>
    public class HaloFlareTests
    {
        [Fact]
        public void FlareLayerRequiresCameraAndLoadingRepairsOnlyMissingCameras()
        {
            string release = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            var added = new GameObject("AddedLayer");
            var loaded = new GameObject("LoadedLayer");
            var disabled = new GameObject("DisabledCamera");
            try
            {
                added.AddComponent<FlareLayer>();
                Assert.NotNull(added.GetComponent<Camera>());
                var layer = ComponentFactory.Create(typeof(FlareLayer), loaded);
                loaded.m_Components.Add(layer);
                Assert.Null(loaded.GetComponent<Camera>());
                FlareLayer.RepairCamera(loaded, "fixture", false);
                Assert.Single(loaded.GetComponents<Camera>());
                FlareLayer.RepairCamera(loaded, "fixture", false);
                Assert.Single(loaded.GetComponents<Camera>());
                var camera = disabled.AddComponent<Camera>();
                camera.enabled = false;
                disabled.AddComponent<FlareLayer>();
                FlareLayer.RepairCamera(disabled, "fixture", false);
                Assert.Same(camera, disabled.GetComponent<Camera>());
                Assert.False(camera.enabled);
            }
            finally
            {
                ProjectInfo.UnityVersion = release;
                Object.DestroyImmediate(added);
                Object.DestroyImmediate(loaded);
                Object.DestroyImmediate(disabled);
            }
        }

        [Fact]
        public void UnmeasuredReleaseRejectsOnlyTheMissingCameraRepair()
        {
            string release = ProjectInfo.UnityVersion;
            var orphan = new GameObject("UnmeasuredOrphan");
            try
            {
                ProjectInfo.UnityVersion = "2022.3.0f1";
                orphan.m_Components.Add(ComponentFactory.Create(typeof(FlareLayer), orphan));
                Assert.Throws<System.NotSupportedException>(() => FlareLayer.RepairCamera(orphan, "fixture", false));
                var camera = orphan.AddComponent<Camera>();
                camera.enabled = false;
                FlareLayer.RepairCamera(orphan, "fixture", false);
                Assert.False(camera.enabled);
            }
            finally
            {
                ProjectInfo.UnityVersion = release;
                Object.DestroyImmediate(orphan);
            }
        }

        [Fact]
        public void FalloffIsTheEditorsTexture()
        {
            var falloff = Halos.Falloff;
            Assert.Equal("UnityHalo", falloff.name);
            Assert.Equal(64, falloff.width);
            Assert.Equal(FilterMode.Bilinear, falloff.filterMode);
            Assert.Equal(TextureWrapMode.Clamp, falloff.wrapMode);
            var row = new byte[64];
            for (int x = 0; x < 64; x++) row[x] = falloff.GetPixelData<byte>(0)[32 * 64 + x];
            // Row 32 of the Editor's UnityHalo, read back from its global _HaloFalloff.
            Assert.Equal(new byte[] { 8, 23, 38, 53, 67, 80, 93, 105, 117, 129, 140, 150, 160, 170, 179, 187 }, row[..16]);
            Assert.Equal(255, row[31]);
            Assert.Equal(0, falloff.GetPixelData<byte>(0)[0]);
        }

        [Fact]
        public void OnlyMeasuredClearingBuffersHideHalos()
        {
            var camera = new GameObject("HaloBuffers").AddComponent<Camera>();
            try
            {
                bool HiddenWith(CameraEvent e, CommandBuffer buffer)
                {
                    camera.AddCommandBuffer(e, buffer);
                    bool hidden = Halos.HalosHidden(camera);
                    camera.RemoveAllCommandBuffers();
                    return hidden;
                }
                Assert.False(Halos.HalosHidden(camera));
                // tests/projects/flares hb.*: buffers that do not clear leave the halos.
                Assert.False(HiddenWith(CameraEvent.BeforeForwardOpaque, new CommandBuffer()));
                Assert.False(HiddenWith(CameraEvent.AfterForwardAlpha, new CommandBuffer()));
                var setFloat = new CommandBuffer();
                setFloat.SetGlobalFloat("_HaloTestUnused", 1f);
                Assert.False(HiddenWith(CameraEvent.BeforeImageEffects, setFloat));
                var noClear = new CommandBuffer();
                noClear.ClearRenderTarget(RTClearFlags.None, new[] { Color.green }, 1f, 0);
                Assert.False(HiddenWith(CameraEvent.AfterForwardAlpha, noClear));
                Assert.False(HiddenWith(CameraEvent.BeforeHaloAndLensFlares, noClear));
                // ho.*: a color clear before the halos hides them; after them it cannot.
                var clear = new CommandBuffer();
                clear.ClearRenderTarget(false, true, Color.green);
                Assert.True(HiddenWith(CameraEvent.AfterForwardAlpha, clear));
                Assert.True(HiddenWith(CameraEvent.BeforeHaloAndLensFlares, clear));
                Assert.False(HiddenWith(CameraEvent.AfterHaloAndLensFlares, clear));
                // Not measured (warns): a clear at another event before the halos hides them.
                Assert.True(HiddenWith(CameraEvent.BeforeForwardOpaque, clear));
                clear.Clear();
                Assert.False(HiddenWith(CameraEvent.AfterForwardAlpha, clear));
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }
    }
}
