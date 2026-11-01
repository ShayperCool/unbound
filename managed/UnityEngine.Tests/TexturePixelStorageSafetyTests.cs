using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public class TexturePixelStorageSafetyTests
    {
        struct WriteBytes : IJob
        {
            public NativeArray<byte> Data;
            public void Execute() { for (int i = 0; i < Data.Length; i++) Data[i] = (byte)i; }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PixelApisRespectTheViewsScheduledWriter(bool cube)
        {
            var texture = cube ? (Texture)new Cubemap(2, TextureFormat.RGBA32, false) : new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var view = cube ? ((Cubemap)texture).GetPixelData<byte>(0, CubemapFace.PositiveX) : ((Texture2D)texture).GetPixelData<byte>(0);
            var job = new WriteBytes { Data = view }.Schedule();
            try
            {
                Assert.Throws<InvalidOperationException>(() => { if (cube) ((Cubemap)texture).GetPixel(CubemapFace.PositiveX, 0, 0); else ((Texture2D)texture).GetPixel(0, 0); });
                Assert.Throws<InvalidOperationException>(() => { if (cube) ((Cubemap)texture).SetPixel(CubemapFace.PositiveX, 0, 0, Color.red); else ((Texture2D)texture).SetPixel(0, 0, Color.red); });
                job.Complete();
                Assert.Equal((byte)3, view[3]);
            }
            finally { job.Complete(); Object.DestroyImmediate(texture); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DestroyCompletesPixelJobsBeforeUnpinning(bool cube)
        {
            var texture = cube ? (Texture)new Cubemap(2, TextureFormat.RGBA32, false) : new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var view = cube ? ((Cubemap)texture).GetPixelData<byte>(0, CubemapFace.PositiveX) : ((Texture2D)texture).GetPixelData<byte>(0);
            var job = new WriteBytes { Data = view }.Schedule();
            Object.DestroyImmediate(texture);
            Assert.True(job.IsCompleted);
            job.Complete();
            Assert.Throws<ObjectDisposedException>(() => view[0]);
        }

        [Fact]
        public void ReinitializeCompletesTheOldViewsWriter()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var old = texture.GetPixelData<byte>(0);
            var job = new WriteBytes { Data = old }.Schedule();
            try
            {
                Assert.True(texture.Reinitialize(4, 4));
                Assert.True(job.IsCompleted);
                job.Complete();
                Assert.Throws<ObjectDisposedException>(() => old[0]);
                Assert.Equal(4 * 4 * 4, texture.GetPixelData<byte>(0).Length);
            }
            finally { job.Complete(); Object.DestroyImmediate(texture); }
        }

        [Fact]
        public void GrowingTheFontAtlasInvalidatesItsOldViewBeforeReplacingPixels()
        {
            var font = new Font();
            var atlas = new FontAtlas(font);
            var old = atlas.Texture.GetPixelData<byte>(0);
            int oldLength = old.Length;
            var job = new WriteBytes { Data = old }.Schedule();
            try
            {
                var glyph = new AtlasGlyph { Width = 513, Height = 4, Coverage = new byte[513 * 4] };
                Array.Fill(glyph.Coverage, (byte)255);
                typeof(FontAtlas).GetMethod("Rebuild", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(atlas, new object[] { new List<AtlasGlyph> { glyph }, new HashSet<GlyphKey>() });
                Assert.True(job.IsCompleted);
                job.Complete();
                Assert.Throws<ObjectDisposedException>(() => old[0]);
                var current = atlas.Texture.GetPixelData<byte>(0);
                Assert.Equal(atlas.Texture.width * atlas.Texture.height, current.Length);
                Assert.True(current.Length > oldLength);
                Assert.Equal((byte)0, current[current.Length - 1]);
                Assert.Contains((byte)255, current.ToArray());
            }
            finally { job.Complete(); Object.DestroyImmediate(atlas.Texture); Object.DestroyImmediate(font); }
        }
    }
}
