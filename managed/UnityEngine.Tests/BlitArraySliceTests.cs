using System;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    public class BlitArraySliceTests
    {
        const byte CopyTextureOp = 10;
        const int CopyBytes = 1 + 8 + 4 * 6 + 8 + 4 * 4;

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        static RenderTexture Target(TextureDimension dimension, int depth = 1, int samples = 1)
        {
            var d = new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 0)
            {
                dimension = dimension,
                volumeDepth = depth,
                msaaSamples = samples,
            };
            var rt = new RenderTexture(d);
            rt.EnsureCreated();
            return rt;
        }

        /// <summary>The copies one array blit records: (source slice, destination slice) each, or
        /// none, with the warning it gave.</summary>
        static (int SourceSlice, int DestinationSlice)[] Blit(Texture source, Texture destination, int sourceSlice, int destinationSlice)
        {
            GfxStream.Flush();
            Assert.True(Immediate.BlitArraySlice(source, destination, sourceSlice, destinationSlice, Vector2.one, Vector2.zero));
            var recorded = GfxStream.Recorded.ToArray();
            GfxStream.Flush();
            Assert.Equal(0, recorded.Length % CopyBytes);
            return Enumerable.Range(0, recorded.Length / CopyBytes).Select(i =>
            {
                var c = recorded.AsSpan(i * CopyBytes, CopyBytes);
                Assert.Equal(CopyTextureOp, c[0]);
                return (BitConverter.ToInt32(c.Slice(9)), BitConverter.ToInt32(c.Slice(41)));
            }).ToArray();
        }

        [Fact]
        public void InRangeSlicesAreCopied()
        {
            var array = Target(TextureDimension.Tex2DArray, 3);
            var other = Target(TextureDimension.Tex2DArray, 3);
            var flat = Target(TextureDimension.Tex2D);
            var cube = Target(TextureDimension.Cube);
            var volume = Target(TextureDimension.Tex3D, 4);
            try
            {
                Assert.Equal(new[] { (2, 1) }, Blit(array, other, 2, 1));
                Assert.Equal(new[] { (1, 0) }, Blit(array, flat, 1, 0));
                Assert.Equal(new[] { (0, 5) }, Blit(array, cube, 0, 5));
                Assert.Equal(new[] { (1, 3) }, Blit(array, volume, 1, 3));
            }
            finally
            {
                foreach (var t in new[] { array, other, flat, cube, volume }) Object.DestroyImmediate(t);
            }
        }

        [Fact]
        public void ASlicePastTheSourceIsSkippedWithAWarning()
        {
            var array = Target(TextureDimension.Tex2DArray, 3);
            var flat = Target(TextureDimension.Tex2D);
            const string member = "Blit from a texture array slice past its slices";
            int before = Hits(member);
            try
            {
                Assert.Empty(Blit(array, flat, 3, 0));
                Assert.Empty(Blit(array, flat, -1, 0));
                Assert.Equal(before + 2, Hits(member));
            }
            finally
            {
                Object.DestroyImmediate(array);
                Object.DestroyImmediate(flat);
            }
        }

        [Theory]
        [InlineData(TextureDimension.Tex2D, 1, 1)]
        [InlineData(TextureDimension.Tex2DArray, 2, 2)]
        [InlineData(TextureDimension.Cube, 1, 6)]
        [InlineData(TextureDimension.Tex3D, 4, 4)]
        [InlineData(TextureDimension.CubeArray, 2, 1)]
        public void ASlicePastTheDestinationLayersIsSkippedWithAWarning(TextureDimension dimension, int depth, int slice)
        {
            var array = Target(TextureDimension.Tex2DArray, 3);
            var destination = Target(dimension, depth);
            const string member = "Blit from a texture array into a slice past the target's layers";
            int before = Hits(member);
            try
            {
                Assert.Empty(Blit(array, destination, 0, slice));
                Assert.Equal(before + 1, Hits(member));
            }
            finally
            {
                Object.DestroyImmediate(array);
                Object.DestroyImmediate(destination);
            }
        }

        [Fact]
        public void AMultisampledDestinationIsSkippedWithAWarning()
        {
            var array = Target(TextureDimension.Tex2DArray, 3);
            var msaa = Target(TextureDimension.Tex2D, 1, 4);
            const string member = "Blit from a texture array with multisampling";
            int before = Hits(member);
            try
            {
                Assert.Equal(4, msaa.antiAliasing);
                Assert.Empty(Blit(array, msaa, 0, 0));
                Assert.Equal(before + 1, Hits(member));
            }
            finally
            {
                Object.DestroyImmediate(array);
                Object.DestroyImmediate(msaa);
            }
        }
    }
}
