using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.LightTransport;
using UnityEngine.LightTransport.PostProcessing;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    public class LightTransportTests
    {
        [Fact]
        public void BakeProgressStateTracksStepsAndCancellation()
        {
            using var progress = new BakeProgressState();
            Assert.Equal(0f, progress.Progress());
            Assert.False(progress.WasCancelled());

            progress.SetTotalWorkSteps(10UL);
            progress.IncrementCompletedWorkSteps(4UL);
            Assert.Equal(0.4f, progress.Progress(), 5);

            progress.IncrementCompletedWorkSteps(20UL);
            Assert.Equal(1f, progress.Progress(), 5);

            progress.Cancel();
            Assert.True(progress.WasCancelled());
        }

        [Fact]
        public void BufferSliceReinterpretsAlignedOffsetsAndRejectsMisaligned()
        {
            var id = new BufferID(7UL);
            var vec4Slice = id.Slice<Vector4>(3UL);
            var floatSlice = vec4Slice.SafeReinterpret<float>();
            Assert.Equal(id, floatSlice.Id);
            Assert.Equal(12UL, floatSlice.Offset);
            Assert.Equal(vec4Slice, floatSlice.SafeReinterpret<Vector4>());

            var misaligned = id.Slice<float>(1UL);
            Assert.Throws<InvalidOperationException>(() => misaligned.SafeReinterpret<Vector4>());
            Assert.Equal(0UL, misaligned.UnsafeReinterpret<Vector4>().Offset);
        }

        [Fact]
        public void RadeonRaysContextBuffersAndEventsRoundTrip()
        {
            using var ctx = new RadeonRaysContext();
            Assert.True(ctx.Initialize());

            var buf = ctx.CreateBuffer(8UL, (ulong)sizeof(float));
            var ev = ctx.CreateEvent();
            using var src = new NativeArray<float>(new[] { 1.5f, 2.5f, 3.5f, 4.5f }, Allocator.Temp);
            using var dst = new NativeArray<float>(4, Allocator.Temp);

            ctx.WriteBuffer(buf.Slice<float>(2UL), src, ev);
            Assert.True(ctx.Flush());
            Assert.True(ctx.Wait(ev));
            Assert.True(ctx.IsCompleted(ev));

            ctx.ReadBuffer(buf.Slice<float>(2UL), dst, ev);
            Assert.Equal(new[] { 1.5f, 2.5f, 3.5f, 4.5f }, dst.ToArray());

            Assert.Throws<ArgumentOutOfRangeException>(() => ctx.WriteBuffer(buf.Slice<float>(6UL), src));

            ctx.DestroyEvent(ev);
            Assert.False(ctx.IsCompleted(ev));
            ctx.DestroyBuffer(buf);
        }

        [Fact]
        public void PostProcessorMatchesUnityConvolutionAndBasisOrder()
        {
            using var ctx = new RadeonRaysContext();
            Assert.True(ctx.Initialize());
            using var pp = new RadeonRaysProbePostProcessor();
            Assert.True(pp.Initialize(ctx));

            var aId = ctx.CreateBuffer(27UL, sizeof(float));
            var bId = ctx.CreateBuffer(27UL, sizeof(float));
            var outId = ctx.CreateBuffer(27UL, sizeof(float));
            var aSlice = aId.Slice<SphericalHarmonicsL2>(0UL);
            var bSlice = bId.Slice<SphericalHarmonicsL2>(0UL);
            var outSlice = outId.Slice<SphericalHarmonicsL2>(0UL);

            var raw = new SphericalHarmonicsL2();
            raw[0, 0] = 1f;   // L0 R
            raw[0, 1] = 4f;   // raw L1_x R -> Unity index 3
            raw[1, 2] = 2f;   // raw L1_y G -> Unity index 1
            raw[2, 3] = 3f;   // raw L1_z B -> Unity index 2
            raw[0, 4] = 5f;   // L2_-2 R

            using var input = new NativeArray<SphericalHarmonicsL2>(new[] { raw }, Allocator.Temp);
            using var output = new NativeArray<SphericalHarmonicsL2>(1, Allocator.Temp);
            ctx.WriteBuffer(aSlice, input);

            // Windowing scales L1 by 0.922066 and L2 by 0.731864.
            Assert.True(pp.WindowSphericalHarmonicsL2(ctx, aSlice, bSlice, 1));
            ctx.ReadBuffer(bSlice, output);
            Assert.Equal(1f, output[0][0, 0], 5);
            Assert.Equal(4f * 0.922066f, output[0][0, 1], 5);
            Assert.Equal(5f * 0.731864f, output[0][0, 4], 5);

            // Scale + Add
            Assert.True(pp.ScaleSphericalHarmonicsL2(ctx, aSlice, bSlice, 1, 2f));
            Assert.True(pp.AddSphericalHarmonicsL2(ctx, aSlice, bSlice, outSlice, 1));
            ctx.ReadBuffer(outSlice, output);
            Assert.Equal(3f, output[0][0, 0], 5);

            // Convolve + ConvertToUnityFormat (in-place aliasing supported).
            Assert.True(pp.ConvolveRadianceToIrradiance(ctx, aSlice, outSlice, 1));
            Assert.True(pp.ConvertToUnityFormat(ctx, outSlice, outSlice, 1));
            ctx.ReadBuffer(outSlice, output);

            // Product of aHat * (SH_NORM / PI): L0=0.28209478, L1=0.325735, L2_2=0.2731371;
            // L1 indices permute (1->3, 2->1, 3->2).
            Assert.Equal(0.28209478f, output[0][0, 0], 5);
            Assert.Equal(4f * 0.325735f, output[0][0, 3], 4);
            Assert.Equal(2f * 0.325735f, output[0][1, 1], 4);
            Assert.Equal(3f * 0.325735f, output[0][2, 2], 4);
            Assert.Equal(5f * 0.2731371f, output[0][0, 4], 4);
        }

        [Fact]
        public void ComputeOcclusionLightIndicesFiltersMixedLightsByRange()
        {
            var input = new InputExtraction.BakeInput
            {
                Lights = new[]
                {
                    new InputExtraction.ExtractedLightMeta { Kind = 0, Mode = 2, ShadowmaskChannel = -1 }, // baked directional
                    new InputExtraction.ExtractedLightMeta { Kind = 0, Mode = 1, ShadowmaskChannel = 0 },  // mixed directional
                    new InputExtraction.ExtractedLightMeta { Kind = 1, Mode = 1, Position = new Vector3(10f, 0f, 0f), Range = 3f, ShadowmaskChannel = 1 }, // mixed point
                }
            };
            var positions = new[] { Vector3.zero, new Vector3(9f, 0f, 0f) };
            var indices = InputExtraction.ComputeOcclusionLightIndicesFromBakeInput(input, positions, 4U);
            Assert.Equal(new[] { 1, -1, -1, -1, 1, 2, -1, -1 }, indices);

            var channels = InputExtraction.GetShadowmaskChannelsFromLightIndices(input, indices);
            Assert.Equal(new[] { 0, -1, -1, -1, 0, 1, -1, -1 }, channels);
        }
    }
}
