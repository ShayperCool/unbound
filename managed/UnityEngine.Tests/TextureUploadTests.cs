using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public class TextureUploadTests
    {
        // tests/projects/assetclones texture.dirty (SetPixel, sampled through Blit) and
        // tests/projects/samplerbias cache/pixels/unapplied (SetPixels, sampled through a
        // material): two independent Unity 6000.0.71f1 captures each. After Apply, a CPU write
        // stays CPU-only until the next Apply; drawing the texture does not upload it.
        public static TheoryData<string> Writers => new() { "SetPixel", "SetPixels", "SetPixels32", "SetPixelData", "LoadRawTextureData" };

        static void Write(Texture2D texture, string writer)
        {
            switch (writer)
            {
                case "SetPixel": texture.SetPixel(0, 0, Color.green); break;
                case "SetPixels": texture.SetPixels(new[] { Color.green, Color.red, Color.red, Color.red }); break;
                case "SetPixels32": texture.SetPixels32(new Color32[] { new(0, 255, 0, 255), new(255, 0, 0, 255), new(255, 0, 0, 255), new(255, 0, 0, 255) }); break;
                case "SetPixelData": texture.SetPixelData(new byte[] { 0, 255, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255 }, 0); break;
                case "LoadRawTextureData": texture.LoadRawTextureData(new byte[] { 0, 255, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255 }); break;
                default: throw new ArgumentException(writer);
            }
        }

        sealed class Scope : IDisposable
        {
            readonly List<Texture2D> m_Textures = new List<Texture2D>();
            internal Texture2D Keep(Texture2D texture)
            {
                m_Textures.Add(texture);
                return texture;
            }
            public void Dispose()
            {
                foreach (var texture in m_Textures)
                    if (texture != null) Object.DestroyImmediate(texture);
            }
        }

        static Texture2D Applied(Scope scope)
        {
            var texture = scope.Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = "dirty" });
            texture.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red });
            texture.Apply();
            return texture;
        }

        [Theory]
        [MemberData(nameof(Writers))]
        public void CpuWriteAfterApplyIsNotUploadedByUse(string writer)
        {
            using var scope = new Scope();
            var texture = Applied(scope);
            uint applied = texture.updateCount;
            ulong handle = TextureHandles.Of(texture);
            Write(texture, writer);
            Assert.Equal(Color.green, texture.GetPixel(0, 0));
            Assert.Equal(handle, TextureHandles.Of(texture));
            Assert.Equal(applied, texture.updateCount);
            Assert.False(texture.m_PixelsDirty);
            texture.Apply(false);
            Assert.Equal(applied + 1, texture.updateCount);
        }

        [Fact]
        public void CloneUploadsUnappliedEditWithoutUploadingSource()
        {
            using var scope = new Scope();
            var source = Applied(scope);
            source.SetPixel(0, 0, Color.green);
            uint applied = source.updateCount;
            var copy = scope.Keep(Object.Instantiate(source));
            Assert.NotEqual(0UL, copy.m_NativeHandle);
            Assert.NotEqual(source.m_NativeHandle, copy.m_NativeHandle);
            Assert.False(copy.m_PixelsDirty);
            Assert.Equal(Color.green, copy.GetPixel(0, 0));
            TextureHandles.Of(source);
            Assert.Equal(applied, source.updateCount);
            Assert.Equal(Color.green, source.GetPixel(0, 0));
            Object.DestroyImmediate(copy);
            TextureHandles.Of(source);
            Assert.Equal(applied, source.updateCount);
        }

        [Fact]
        public void TextureWithoutGpuStorageUploadsCurrentPixelsAtFirstUse()
        {
            using var scope = new Scope();
            var texture = scope.Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            texture.SetPixel(0, 0, Color.green);
            Assert.Equal(0u, texture.updateCount);
            Assert.NotEqual(0UL, TextureHandles.Of(texture));
            Assert.Equal(1u, texture.updateCount);
            Assert.False(texture.m_PixelsDirty);
        }

        [Fact]
        public void ReinitializedTextureUploadsItsNewShapeAtNextUse()
        {
            using var scope = new Scope();
            var texture = Applied(scope);
            uint applied = texture.updateCount;
            texture.Reinitialize(1, 1);
            texture.SetPixel(0, 0, Color.green);
            TextureHandles.Of(texture);
            Assert.Equal(applied + 1, texture.updateCount);
        }
    }
}
