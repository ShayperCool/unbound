using System;
using Unity.Collections;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Members of smaller systems once stubs (Q820), as the Unity 6000.0.71f1 Editor
    /// answers them (tests/projects/smallstubs).</summary>
    public sealed class SmallStubsTests
    {
        [Fact]
        public void LoadRawTextureDataTakesANativeArraysBytes()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            using (var colors = new NativeArray<Color32>(new[] { new Color32(1, 2, 3, 4), new Color32(5, 6, 7, 8), new Color32(9, 10, 11, 12), new Color32(13, 14, 15, 16) }, Allocator.Temp))
                texture.LoadRawTextureData(colors);
            Assert.Equal(new Color32(13, 14, 15, 16), texture.GetPixels32()[3]);
            using (var longer = new NativeArray<byte>(24, Allocator.Temp))
                texture.LoadRawTextureData(longer);
            Assert.Equal(new Color32(0, 0, 0, 0), texture.GetPixels32()[0]);
            using (var shorter = new NativeArray<byte>(3, Allocator.Temp))
                Assert.Equal("LoadRawTextureData: not enough data provided (will result in overread).", Assert.Throws<UnityException>(() => texture.LoadRawTextureData(shorter)).Message);
            using (var empty = new NativeArray<byte>(0, Allocator.Temp))
                Assert.Equal("No texture data provided to LoadRawTextureData", Assert.Throws<UnityException>(() => texture.LoadRawTextureData(empty)).Message);
            Assert.Equal("No texture data provided to LoadRawTextureData", Assert.Throws<UnityException>(() => texture.LoadRawTextureData(default(NativeArray<byte>))).Message);
        }

        [Fact]
        public void ShaderUtilQueriesNeedAShaderOrMaterial()
        {
            Assert.Equal("s", Assert.Throws<ArgumentNullException>(() => ShaderUtil.HasProceduralInstancing(null)).ParamName);
            Assert.Equal("material", Assert.Throws<ArgumentNullException>(() => ShaderUtil.IsPassCompiled(null, 0)).ParamName);
        }
    }
}
