using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    public class CustomRenderTextureTests
    {
        /// <summary>A recorded CopyTexture command (CommandStream.cs CopyTexture).</summary>
        readonly record struct Copy(ulong Source, int SourceSlice, int SourceMip, ulong Destination, int DestinationSlice, int DestinationMip);

        const byte CopyTextureOp = 10;
        const int CopyBytes = 1 + 8 + 4 * 6 + 8 + 4 * 4;

        static List<Copy> Copies(ReadOnlySpan<byte> recorded)
        {
            Assert.Equal(0, recorded.Length % CopyBytes);
            var copies = new List<Copy>();
            for (int at = 0; at < recorded.Length; at += CopyBytes)
            {
                var c = recorded.Slice(at, CopyBytes);
                Assert.Equal(CopyTextureOp, c[0]);
                copies.Add(new Copy(BitConverter.ToUInt64(c.Slice(1)), BitConverter.ToInt32(c.Slice(9)), BitConverter.ToInt32(c.Slice(13)),
                    BitConverter.ToUInt64(c.Slice(33)), BitConverter.ToInt32(c.Slice(41)), BitConverter.ToInt32(c.Slice(45))));
                Assert.Equal(-1, BitConverter.ToInt32(c.Slice(25)));
            }
            return copies;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DoubleBufferSwapCopiesEveryMipOfEveryFace(bool cube)
        {
            var crt = new CustomRenderTexture(16, 16) { useMipMap = true, autoGenerateMips = true, doubleBuffered = true };
            if (cube) crt.dimension = TextureDimension.Cube;
            try
            {
                crt.EnsureDoubleBufferConsistency();
                var other = crt.GetDoubleBufferRenderTexture();
                Assert.NotNull(other);
                Assert.Equal(5, crt.mipmapCount);
                Assert.Equal(crt.mipmapCount, other.mipmapCount);
                crt.Swap(); // creates the scratch texture
                GfxStream.Flush();
                crt.Swap();
                var copies = Copies(GfxStream.Recorded);
                GfxStream.Flush();

                int faces = cube ? 6 : 1, mips = crt.mipmapCount;
                ulong a = crt.EnsureCreated(), b = other.EnsureCreated();
                Assert.Equal(3 * faces * mips, copies.Count);
                ulong scratch = copies[0].Destination;
                Assert.NotEqual(a, scratch);
                Assert.NotEqual(b, scratch);
                // this -> scratch, other -> this, scratch -> other, each over the whole chain.
                var expected = new List<Copy>();
                foreach (var (from, to) in new[] { (a, scratch), (b, a), (scratch, b) })
                    for (int face = 0; face < faces; face++)
                        for (int mip = 0; mip < mips; mip++)
                            expected.Add(new Copy(from, face, mip, to, face, mip));
                Assert.Equal(expected, copies);
            }
            finally
            {
                GfxStream.Flush();
                var other = crt.GetDoubleBufferRenderTexture();
                if (other != null) Object.DestroyImmediate(other);
                Object.DestroyImmediate(crt);
            }
        }

        /// <summary>A volume's swap copies every slice of every mip: the mips have half the depth
        /// per level (16x16x8: 8, 4, 2, 1 and 1 slices).</summary>
        [Fact]
        public void DoubleBufferSwapCopiesEverySliceOfEveryMipOfAVolume()
        {
            var crt = new CustomRenderTexture(16, 16) { dimension = TextureDimension.Tex3D, volumeDepth = 8, depth = 0, useMipMap = true, autoGenerateMips = true, doubleBuffered = true };
            try
            {
                crt.EnsureDoubleBufferConsistency();
                crt.Swap();
                GfxStream.Flush();
                crt.Swap();
                var copies = Copies(GfxStream.Recorded);
                GfxStream.Flush();
                Assert.Equal(5, crt.mipmapCount);
                ulong a = crt.EnsureCreated();
                var fromThis = copies.FindAll(c => c.Source == a);
                var expected = new List<(int, int)>();
                for (int slice = 0; slice < 8; slice++)
                    for (int mip = 0; mip < 5 && slice < Math.Max(1, 8 >> mip); mip++)
                        expected.Add((slice, mip));
                Assert.Equal(expected, fromThis.ConvertAll(c => (c.SourceSlice, c.SourceMip)));
                Assert.Equal(3 * (8 + 4 + 2 + 1 + 1), copies.Count);
                Assert.All(copies, c => Assert.Equal(c.SourceSlice, c.DestinationSlice));
            }
            finally
            {
                GfxStream.Flush();
                var other = crt.GetDoubleBufferRenderTexture();
                if (other != null) Object.DestroyImmediate(other);
                Object.DestroyImmediate(crt);
            }
        }

        /// <summary>A loaded texture is queued again by a change of its material, initialization
        /// material or color, modes or face mask (its OnLoad initialization and update, by the
        /// modes it has then), not by the same value again, nor by the shader pass, the
        /// initialization texture or source; a texture not loaded yet is not queued.</summary>
        [Fact]
        public void ChangesQueueTheOnLoadWorkOfALoadedTextureAgain()
        {
            var crt = new CustomRenderTexture(4, 4) { initializationMode = CustomRenderTextureUpdateMode.OnLoad, updateMode = CustomRenderTextureUpdateMode.OnDemand };
            var material = new Material(Shader.Find("Hidden/BlitCopy"));
            var other = new Material(material);
            try
            {
                crt.material = material;
                Assert.False(crt.m_ReloadInit || crt.m_ReloadUpdate);
                crt.m_Loaded = true;
                (bool, bool) After(Action change)
                {
                    crt.m_ReloadInit = crt.m_ReloadUpdate = false;
                    change();
                    return (crt.m_ReloadInit, crt.m_ReloadUpdate);
                }
                Assert.Equal((false, false), After(() => crt.material = material));
                Assert.Equal((true, false), After(() => crt.material = other));
                Assert.Equal((false, false), After(() => crt.shaderPass = 2));
                Assert.Equal((false, false), After(() => crt.initializationTexture = Texture2D.whiteTexture));
                Assert.Equal((false, false), After(() => crt.initializationSource = CustomRenderTextureInitializationSource.Material));
                Assert.Equal((false, false), After(() => crt.initializationColor = crt.initializationColor));
                Assert.Equal((true, false), After(() => crt.initializationColor = new Color(0, 1, 0, 1)));
                Assert.Equal((true, false), After(() => crt.cubemapFaceMask = 5));
                Assert.Equal((true, true), After(() => crt.updateMode = CustomRenderTextureUpdateMode.OnLoad));
                // By the modes after the change.
                Assert.Equal((false, true), After(() => crt.initializationMode = CustomRenderTextureUpdateMode.OnDemand));
                Assert.Equal((false, false), After(() => crt.updateMode = CustomRenderTextureUpdateMode.Realtime));
                // A NaN is always a change.
                Assert.Equal((false, false), After(() => crt.initializationColor = new Color(float.NaN, 0, 0, 0)));
                crt.initializationMode = CustomRenderTextureUpdateMode.OnLoad;
                Assert.Equal((true, false), After(() => crt.initializationColor = crt.initializationColor));
            }
            finally
            {
                Object.DestroyImmediate(crt);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(other);
            }
        }

        /// <summary>Setting an initialization material clears the initialization texture and
        /// makes the color white; setting none keeps both.</summary>
        [Fact]
        public void AnInitializationMaterialClearsTheTextureAndWhitensTheColor()
        {
            var crt = new CustomRenderTexture(4, 4) { initializationTexture = Texture2D.whiteTexture, initializationColor = new Color(1, 0, 0, 0.5f) };
            var material = new Material(Shader.Find("Hidden/BlitCopy"));
            try
            {
                crt.initializationMaterial = material;
                Assert.Null(crt.initializationTexture);
                Assert.Equal(Color.white, crt.initializationColor);
                crt.initializationTexture = Texture2D.whiteTexture;
                crt.initializationColor = Color.red;
                crt.initializationMaterial = null;
                Assert.NotNull(crt.initializationTexture);
                Assert.Equal(Color.red, crt.initializationColor);
            }
            finally
            {
                Object.DestroyImmediate(crt);
                Object.DestroyImmediate(material);
            }
        }

        [Fact]
        public void DoubleBufferWithoutMipsCopiesOneLevel()
        {
            var crt = new CustomRenderTexture(8, 8) { doubleBuffered = true };
            try
            {
                crt.EnsureDoubleBufferConsistency();
                crt.Swap();
                GfxStream.Flush();
                crt.Swap();
                var copies = Copies(GfxStream.Recorded);
                GfxStream.Flush();
                Assert.Equal(3, copies.Count);
                Assert.All(copies, c => Assert.Equal(0, c.SourceMip));
            }
            finally
            {
                GfxStream.Flush();
                var other = crt.GetDoubleBufferRenderTexture();
                if (other != null) Object.DestroyImmediate(other);
                Object.DestroyImmediate(crt);
            }
        }
    }
}
