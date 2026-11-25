using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Experimental.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Edits that dirty an asset, and Texture2D.Reinitialize's colour space (matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/tmpatlas op.*), with the controls the rules leave clean.</summary>
    public sealed class AssetEditDirtTests : IDisposable
    {
        readonly List<Object> m_Made = new List<Object>();

        /// <summary>An asset as loaded: from a native asset file (.asset, .mat) or imported.</summary>
        T Asset<T>(T o, bool native = true) where T : Object
        {
            o.IsPersistentAsset = true;
            o.m_InNativeFile = native;
            EditorUtility.ClearDirty(o);
            m_Made.Add(o);
            return o;
        }

        static Texture2D Rgba(bool linear = false) => new Texture2D(4, 4, TextureFormat.RGBA32, false, linear);

        public void Dispose()
        {
            foreach (var o in m_Made)
            {
                EditorUtility.ClearDirty(o);
                o.IsPersistentAsset = false;
                Object.DestroyImmediate(o);
            }
        }

        [Fact]
        public void ReinitializeDirtiesATextureAssetAndTheEnginesOwnDoesNot()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var same = Asset(Rgba());
            Assert.True(same.Reinitialize(4, 4));
            Assert.True(EditorUtility.IsDirty(same));
            var format = Asset(Rgba());
            format.Reinitialize(2, 2, TextureFormat.Alpha8, false);
            Assert.True(EditorUtility.IsDirty(format));
            var graphics = Asset(Rgba());
            graphics.Reinitialize(2, 2, GraphicsFormat.R8G8B8A8_SRGB, false);
            Assert.True(EditorUtility.IsDirty(graphics));
            // Controls: an edit that only sets pixels, and the engine's own reinitialization.
            var pixels = Asset(Rgba());
            pixels.SetPixel(0, 0, Color.red);
            Assert.False(EditorUtility.IsDirty(pixels));
            var engine = Asset(Rgba());
            engine.ReinitializeForEngine(8, 8, TextureFormat.RGBA32, false);
            Assert.False(EditorUtility.IsDirty(engine));
        }

        [Fact]
        public void ReinitializeWithATextureFormatIsLinearAndWithAGraphicsFormatFollowsIt()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var rgb = Rgba();
            m_Made.Add(rgb);
            Assert.True(rgb.isDataSRGB);
            rgb.Reinitialize(2, 2, TextureFormat.RGB24, false);
            Assert.False(rgb.isDataSRGB);
            var kept = Rgba();
            m_Made.Add(kept);
            kept.Reinitialize(8, 8);
            Assert.True(kept.isDataSRGB);
            var srgb = Rgba(linear: true);
            m_Made.Add(srgb);
            srgb.Reinitialize(2, 2, GraphicsFormat.R8G8B8A8_SRGB, false);
            Assert.True(srgb.isDataSRGB);
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                var unmeasured = Rgba();
                m_Made.Add(unmeasured);
                unmeasured.Reinitialize(2, 2, TextureFormat.RGB24, false);
                Assert.True(unmeasured.isDataSRGB);
            }
        }

        [Fact]
        public void ASamplerSettingChangedDirtiesANativeTextureAsset()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var filter = Asset(Rgba());
            filter.filterMode = FilterMode.Point;
            Assert.True(EditorUtility.IsDirty(filter));
            var wrap = Asset(Rgba());
            wrap.wrapMode = TextureWrapMode.Clamp;
            Assert.True(EditorUtility.IsDirty(wrap));
            var bias = Asset(Rgba());
            bias.mipMapBias = 0.5f;
            Assert.True(EditorUtility.IsDirty(bias));
            // Controls: the value it has, an imported texture, a texture of no asset.
            var same = Asset(Rgba());
            same.filterMode = same.filterMode;
            Assert.False(EditorUtility.IsDirty(same));
            var imported = Asset(Rgba(), native: false);
            imported.anisoLevel = 4;
            Assert.False(EditorUtility.IsDirty(imported));
            var runtime = Rgba();
            m_Made.Add(runtime);
            runtime.filterMode = FilterMode.Point;
            Assert.False(EditorUtility.IsDirty(runtime));
        }

        [Fact]
        public void ARenameDirtiesANativeAsset()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var material = Asset(new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "Before" });
            material.name = "After";
            Assert.True(EditorUtility.IsDirty(material));
            var same = Asset(new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "Same" });
            same.name = "Same";
            Assert.False(EditorUtility.IsDirty(same));
            var imported = Asset(Rgba(), native: false);
            imported.name = "Renamed";
            Assert.False(EditorUtility.IsDirty(imported));
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                var unmeasured = Asset(Rgba());
                unmeasured.name = "Renamed";
                Assert.False(EditorUtility.IsDirty(unmeasured));
            }
        }
    }
}
