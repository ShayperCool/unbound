using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>The anisotropic level a render texture is sampled at before the global setting:
    /// a 2D or 2D array color target with a depth buffer turns anisotropy off, one without keeps
    /// its anisoLevel (Unity 6000.0.71f1, tests/projects/samplerbias `--target-control`).</summary>
    public class RenderTextureAnisotropyTests
    {
        static RenderTexture Make(GraphicsFormat color, GraphicsFormat depth, TextureDimension dimension, int aniso)
        {
            var desc = new RenderTextureDescriptor(16, 8, color, depth)
            {
                dimension = dimension,
                volumeDepth = dimension == TextureDimension.Tex2DArray ? 2 : dimension == TextureDimension.Tex3D ? 4 : 1,
            };
            if (dimension == TextureDimension.Cube) desc.width = desc.height = 16;
            return new RenderTexture(desc) { anisoLevel = aniso };
        }

        [Theory]
        [InlineData(GraphicsFormat.None, TextureDimension.Tex2D, 16, 16)]
        [InlineData(GraphicsFormat.D16_UNorm, TextureDimension.Tex2D, 16, 0)]
        [InlineData(GraphicsFormat.D24_UNorm_S8_UInt, TextureDimension.Tex2D, 2, 0)]
        [InlineData(GraphicsFormat.None, TextureDimension.Tex2DArray, 2, 2)]
        [InlineData(GraphicsFormat.D24_UNorm_S8_UInt, TextureDimension.Tex2DArray, 16, 0)]
        [InlineData(GraphicsFormat.None, TextureDimension.Tex2D, 0, 0)]
        // Outside the measured dimensions: a 3D target (never with depth) keeps its level.
        [InlineData(GraphicsFormat.None, TextureDimension.Tex3D, 4, 4)]
        public void ColorTargetsWithDepthSampleWithoutAnisotropy(GraphicsFormat depth, TextureDimension dimension, int aniso, int expected)
        {
            var rt = Make(GraphicsFormat.R32_SFloat, depth, dimension, aniso);
            try
            {
                Assert.Equal(aniso, rt.anisoLevel);
                Assert.Equal(expected, rt.SamplerAnisoLevel);
            }
            finally { Object.DestroyImmediate(rt); }
        }

        /// <summary>A depth-only target reports level 0 and samples so, whatever was asked.</summary>
        [Fact]
        public void DepthOnlyTargetsSampleWithoutAnisotropy()
        {
            var rt = Make(GraphicsFormat.None, GraphicsFormat.D24_UNorm_S8_UInt, TextureDimension.Tex2D, 0);
            try
            {
                rt.anisoLevel = 8;
                Assert.Equal(0, rt.SamplerAnisoLevel);
            }
            finally { Object.DestroyImmediate(rt); }
        }

        /// <summary>A level raised on a live target with depth still samples without anisotropy
        /// (samplerbias `targetMadeForced/16/raised`); without depth the raised level is used.</summary>
        [Theory]
        [InlineData(GraphicsFormat.D16_UNorm, 0)]
        [InlineData(GraphicsFormat.None, 16)]
        public void RaisedLevelsFollowTheDepthBuffer(GraphicsFormat depth, int expected)
        {
            var rt = Make(GraphicsFormat.R32_SFloat, depth, TextureDimension.Tex2D, 1);
            try
            {
                rt.anisoLevel = 16;
                Assert.Equal(expected, rt.SamplerAnisoLevel);
            }
            finally { Object.DestroyImmediate(rt); }
        }
    }
}
