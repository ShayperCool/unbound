using System;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public class AssetCloneTests
    {
        // tests/projects/assetclones: two independent Unity 6000.0.71f1 captures.
        [Fact]
        public void ReadableTextureCloneOwnsEveryMipAndPreservesSampler()
        {
            var source = new Texture2D(4, 2, TextureFormat.RGBAHalf, true, true)
            {
                name = "texture", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point, wrapModeU = TextureWrapMode.Clamp,
                wrapModeV = TextureWrapMode.Mirror, wrapModeW = TextureWrapMode.MirrorOnce,
                anisoLevel = 3, mipMapBias = 0.25f,
            };
            for (int mip = 0; mip < source.mipmapCount; mip++)
            {
                var pixels = new Color[Math.Max(1, source.width >> mip) * Math.Max(1, source.height >> mip)];
                Array.Fill(pixels, new Color(0.25f * (mip + 1), 0.5f, 0.75f, 1));
                source.SetPixels(pixels, mip);
            }
            source.Apply(false);
            source.SetPixel(0, 0, Color.green); // Unapplied CPU edits are copied too.
            var copy = Object.Instantiate(source);
            Assert.NotSame(source, copy);
            Assert.NotEqual(source.GetInstanceID(), copy.GetInstanceID());
            Assert.Equal("texture(Clone)", copy.name);
            Assert.Equal(HideFlags.None, copy.hideFlags);
            Assert.False(copy.IsPersistentAsset);
            Assert.Equal(source.graphicsFormat, copy.graphicsFormat);
            Assert.Equal(source.mipmapCount, copy.mipmapCount);
            Assert.Equal(source.wrapModeW, copy.wrapModeW);
            Assert.Equal(source.wrapModeV, copy.wrapModeV);
            Assert.Equal(source.wrapModeU, copy.wrapModeU);
            Assert.Equal(source.filterMode, copy.filterMode);
            Assert.Equal(source.anisoLevel, copy.anisoLevel);
            Assert.Equal(source.mipMapBias, copy.mipMapBias);
            for (int mip = 0; mip < source.mipmapCount; mip++) Assert.Equal(source.GetPixels(mip), copy.GetPixels(mip));
            copy.SetPixel(0, 0, Color.magenta, 1);
            Assert.NotEqual(source.GetPixel(0, 0, 1), copy.GetPixel(0, 0, 1));
            Object.DestroyImmediate(source);
            Assert.Equal(Color.magenta, copy.GetPixel(0, 0, 1));
            Object.DestroyImmediate(copy);
        }

        [Fact]
        public void NonReadableTextureCloneKeepsReadabilityAndOwnRawSnapshot()
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            source.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
            source.Apply(false, true);
            var bytes = source.GetRawTextureData();
            var copy = Object.Instantiate(source);
            Assert.False(copy.isReadable);
            Assert.Equal(bytes, copy.GetRawTextureData());
            Assert.Throws<ArgumentException>(() => copy.GetPixels());
            source.Reinitialize(1, 1);
            Assert.Equal(bytes, copy.GetRawTextureData());
            Object.DestroyImmediate(source);
            Assert.Equal(bytes, copy.GetRawTextureData());
            Object.DestroyImmediate(copy);
        }

        [Fact]
        public void RenderTargetCloneStartsUncreatedAndReleasingActiveTargetUnbindsIt()
        {
            var source = new RenderTexture(4, 2, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "target", hideFlags = HideFlags.HideAndDontSave, useMipMap = true,
                autoGenerateMips = false, filterMode = FilterMode.Point, wrapModeU = TextureWrapMode.Repeat,
                mipMapBias = 0.25f,
            };
            RenderTexture.active = source;
            var copy = Object.Instantiate(source);
            Assert.Same(source, RenderTexture.active);
            Assert.True(source.IsCreated());
            Assert.False(copy.IsCreated());
            Assert.Equal(source.descriptor, copy.descriptor);
            Assert.Equal(source.filterMode, copy.filterMode);
            Assert.Equal(source.wrapModeU, copy.wrapModeU);
            Assert.Equal(source.mipMapBias, copy.mipMapBias);
            RenderTexture.active = copy;
            copy.Release();
            Assert.Null(RenderTexture.active);
            Assert.True(source.IsCreated());
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(source);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void TextureCopyUpdatesRetainedStorageWithoutChangingReadability(bool sourceReadable, bool destinationReadable)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            source.SetPixels(new[] { Color.green, Color.green, Color.green, Color.green });
            source.Apply(false, !sourceReadable);
            var destination = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            destination.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red });
            destination.Apply(false, !destinationReadable);
            Graphics.CopyTexture(source, destination);
            Assert.Equal(sourceReadable, source.isReadable);
            Assert.Equal(destinationReadable, destination.isReadable);
            Assert.Equal(source.GetRawTextureData(), destination.GetRawTextureData());
            var copy = Object.Instantiate(destination);
            Assert.Equal(source.GetRawTextureData(), copy.GetRawTextureData());
            Object.DestroyImmediate(destination);
            Object.DestroyImmediate(source);
            Assert.Equal(destinationReadable, copy.isReadable);
            Object.DestroyImmediate(copy);
        }

        [Fact]
        public void TextCloneOwnsItsBytesAndSafetyHandle()
        {
            var source = new TextAsset("snapshot") { name = "text", hideFlags = HideFlags.HideAndDontSave };
            var originalView = source.GetData<byte>();
            var copy = Object.Instantiate(source);
            var copyView = copy.GetData<byte>();
            Assert.Equal("text(Clone)", copy.name);
            Assert.Equal(HideFlags.None, copy.hideFlags);
            var bytes = copy.bytes;
            bytes[0] = 0;
            Assert.Equal(source.bytes, copy.bytes);
            Object.DestroyImmediate(copy);
            Assert.Throws<ObjectDisposedException>(() => copyView[0]);
            Assert.Equal((byte)'s', originalView[0]);
            Assert.Equal("snapshot", source.text);
            Object.DestroyImmediate(source);
        }
    }
}
