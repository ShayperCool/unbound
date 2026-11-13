using System;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public class ExternalTextureTests
    {
        [Fact]
        public void ExternalFactoriesRejectNullNativeStorage()
        {
            Assert.Equal("nativeTex can not be null", Assert.Throws<ArgumentException>(() =>
                Cubemap.CreateExternalTexture(4, TextureFormat.RGBA32, false, IntPtr.Zero)).Message);
            Assert.Equal("nativeTex may not be zero", Assert.Throws<ArgumentException>(() =>
                Texture3D.CreateExternalTexture(4, 2, 2, TextureFormat.RGBA32, false, IntPtr.Zero)).Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void ExternalTexturesKeepMetadataAndRebindTheirNativePointer(int kind)
        {
            Texture texture = kind == 0 ? Texture2D.CreateExternalTexture(7, 3, TextureFormat.RGBA32, false, true, (IntPtr)77)
                : kind == 1 ? (Texture)Cubemap.CreateExternalTexture(8, TextureFormat.RGBA32, true, (IntPtr)77)
                : Texture3D.CreateExternalTexture(7, 3, 2, TextureFormat.RGBA32, false, (IntPtr)77);
            texture.name = "borrowed";
            try
            {
                Assert.True(texture.isReadable);
                Assert.Equal(kind == 1 ? TextureDimension.Cube : kind == 2 ? TextureDimension.Tex3D : TextureDimension.Tex2D, texture.dimension);
                Assert.Equal((IntPtr)77, texture.GetNativeTexturePtr());
                Assert.Equal(kind == 1 ? 8 : 7, texture.width);
                Assert.Equal(kind == 1 ? 8 : 3, texture.height);
                Assert.Equal(kind == 1 ? 4 : 1, texture.mipmapCount);
                string api;
                ArgumentException error;
                if (texture is Texture2D twoD)
                {
                    api = "Texture2D.GetPixels";
                    error = Assert.Throws<ArgumentException>(() => twoD.GetPixels());
                    twoD.UpdateExternalTexture((IntPtr)78);
                }
                else if (texture is Cubemap cube)
                {
                    api = "Cubemap.GetPixels";
                    error = Assert.Throws<ArgumentException>(() => cube.GetPixels(CubemapFace.PositiveX));
                    cube.UpdateExternalTexture((IntPtr)78);
                }
                else
                {
                    var volume = (Texture3D)texture;
                    Assert.Equal(2, volume.depth);
                    api = "Texture3D.GetPixels";
                    error = Assert.Throws<ArgumentException>(() => volume.GetPixels());
                    volume.UpdateExternalTexture((IntPtr)78);
                }
                Assert.Equal(api + ": not allowed on native textures. (Texture 'borrowed')", error.Message);
                Assert.Equal((IntPtr)78, texture.GetNativeTexturePtr());
                Assert.Equal(kind == 1 ? 8 : 7, texture.width);
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
