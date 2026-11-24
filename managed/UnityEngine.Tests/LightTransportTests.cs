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

        struct NonBlittableStruct
        {
            public string Label;
        }

        [Fact]
        public void BufferSliceReinterpretsDivisibleElementSizesAndRejectsInvalidOnes()
        {
            var id = new BufferID(7UL);
            var vec4Slice = id.Slice<Vector4>(3UL);
            var floatSlice = vec4Slice.SafeReinterpret<float>();
            Assert.Equal(id, floatSlice.Id);
            Assert.Equal(12UL, floatSlice.Offset);

            // Unity checks oldSize % newSize == 0 (4 % 16 != 0 throws ArgumentException even when offset is aligned).
            Assert.Throws<ArgumentException>(() => floatSlice.SafeReinterpret<Vector4>());
            Assert.Throws<ArgumentException>(() => id.Slice<float>(1UL).SafeReinterpret<Vector4>());
            Assert.Throws<ArgumentException>(() => vec4Slice.SafeReinterpret<NonBlittableStruct>());

            Assert.Equal(3UL, floatSlice.UnsafeReinterpret<Vector4>().Offset);
            Assert.Equal(0UL, id.Slice<float>(1UL).UnsafeReinterpret<Vector4>().Offset);
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
            raw[1, 5] = 6f;   // L2_-1 G
            raw[2, 6] = 7f;   // L2_0 B
            raw[0, 7] = 8f;   // L2_1 R
            raw[1, 8] = 9f;   // L2_2 G

            using var input = new NativeArray<SphericalHarmonicsL2>(new[] { raw }, Allocator.Temp);
            using var output = new NativeArray<SphericalHarmonicsL2>(1, Allocator.Temp);
            ctx.WriteBuffer(aSlice, input);

            // Windowing scales L1 by 0.922066 and L2 by 0.731864 across all L2 indices.
            Assert.True(pp.WindowSphericalHarmonicsL2(ctx, aSlice, bSlice, 1));
            ctx.ReadBuffer(bSlice, output);
            Assert.Equal(1f, output[0][0, 0], 5);
            Assert.Equal(4f * 0.922066f, output[0][0, 1], 5);
            Assert.Equal(5f * 0.731864f, output[0][0, 4], 5);
            Assert.Equal(6f * 0.731864f, output[0][1, 5], 5);
            Assert.Equal(7f * 0.731864f, output[0][2, 6], 5);
            Assert.Equal(8f * 0.731864f, output[0][0, 7], 5);
            Assert.Equal(9f * 0.731864f, output[0][1, 8], 5);

            // Scale + Add
            Assert.True(pp.ScaleSphericalHarmonicsL2(ctx, aSlice, bSlice, 1, 2f));
            Assert.True(pp.AddSphericalHarmonicsL2(ctx, aSlice, bSlice, outSlice, 1));
            ctx.ReadBuffer(outSlice, output);
            Assert.Equal(3f, output[0][0, 0], 5);
            Assert.Equal(27f, output[0][1, 8], 5);

            // Convolve + ConvertToUnityFormat (in-place aliasing supported).
            Assert.True(pp.ConvolveRadianceToIrradiance(ctx, aSlice, outSlice, 1));
            Assert.True(pp.ConvertToUnityFormat(ctx, outSlice, outSlice, 1));
            ctx.ReadBuffer(outSlice, output);

            // Independent analytical aHat_l * Y_lm / pi factors:
            // L0: 1 * sqrt(1/pi)/2; L1: (2/3) * sqrt(3/pi)/2; L2_±2,±1: (1/4) * sqrt(15/pi)/2;
            // L2_0: (1/4) * sqrt(5/pi)/4; L2_2: (1/4) * sqrt(15/pi)/4.
            float f0 = MathF.Sqrt(1f / MathF.PI) / 2f;
            float f1 = (2f / 3f) * MathF.Sqrt(3f / MathF.PI) / 2f;
            float f2Off = 0.25f * MathF.Sqrt(15f / MathF.PI) / 2f;
            float f20 = 0.25f * MathF.Sqrt(5f / MathF.PI) / 4f;
            float f22 = 0.25f * MathF.Sqrt(15f / MathF.PI) / 4f;
            Assert.Equal(1f * f0, output[0][0, 0], 1e-5f);
            Assert.Equal(4f * f1, output[0][0, 3], 1e-5f);
            Assert.Equal(2f * f1, output[0][1, 1], 1e-5f);
            Assert.Equal(3f * f1, output[0][2, 2], 1e-5f);
            Assert.Equal(5f * f2Off, output[0][0, 4], 1e-5f);
            Assert.Equal(6f * f2Off, output[0][1, 5], 1e-5f);
            Assert.Equal(7f * f20, output[0][2, 6], 1e-5f);
            Assert.Equal(8f * f2Off, output[0][0, 7], 1e-5f);
            Assert.Equal(9f * f22, output[0][1, 8], 1e-5f);
        }

        [Fact]
        public void ReferenceContextAndPostProcessorMatchUnityContracts()
        {
            using var refCtx = new ReferenceContext();
            Assert.True(refCtx.Initialize());
            using var rrCtx = new RadeonRaysContext();
            Assert.True(rrCtx.Initialize());

            var shBuf = refCtx.CreateBuffer(27UL, sizeof(float));
            var outBuf = refCtx.CreateBuffer(27UL, sizeof(float));
            var raw = new SphericalHarmonicsL2();
            raw[0, 0] = 2f;
            raw[0, 1] = 3f;
            raw[2, 6] = 4f;
            raw[1, 8] = 5f;
            using var input = new NativeArray<SphericalHarmonicsL2>(new[] { raw }, Allocator.Temp);
            using var output = new NativeArray<SphericalHarmonicsL2>(1, Allocator.Temp);
            refCtx.WriteBuffer(shBuf.Slice<SphericalHarmonicsL2>(0UL), input);

            // ReferenceContext.GetNativeArray exposes the backing NativeArray<byte>.
            var nativeBytes = refCtx.GetNativeArray(shBuf);
            Assert.True(nativeBytes.IsCreated);
            Assert.Equal(108, nativeBytes.Length);
            Assert.Equal(2f, nativeBytes.Reinterpret<float>(1)[0], 1e-5f);

            // ReferenceProbePostProcessor works without Initialize() and rejects RadeonRaysContext.
            using var refPp = new ReferenceProbePostProcessor();
            Assert.True(refPp.ConvolveRadianceToIrradiance(refCtx, shBuf.Slice<SphericalHarmonicsL2>(0UL), outBuf.Slice<SphericalHarmonicsL2>(0UL), 1));
            Assert.True(refPp.ConvertToUnityFormat(refCtx, outBuf.Slice<SphericalHarmonicsL2>(0UL), outBuf.Slice<SphericalHarmonicsL2>(0UL), 1));
            refCtx.ReadBuffer(outBuf.Slice<SphericalHarmonicsL2>(0UL), output);
            Assert.Equal(2f * (MathF.Sqrt(1f / MathF.PI) / 2f), output[0][0, 0], 1e-5f);
            Assert.Equal(3f * ((2f / 3f) * MathF.Sqrt(3f / MathF.PI) / 2f), output[0][0, 3], 1e-5f);
            Assert.Equal(4f * (0.25f * MathF.Sqrt(5f / MathF.PI) / 4f), output[0][2, 6], 1e-5f);
            Assert.Equal(5f * (0.25f * MathF.Sqrt(15f / MathF.PI) / 4f), output[0][1, 8], 1e-5f);

            var rrBuf = rrCtx.CreateBuffer(27UL, sizeof(float));
            Assert.False(refPp.WindowSphericalHarmonicsL2(rrCtx, rrBuf.Slice<SphericalHarmonicsL2>(0UL), rrBuf.Slice<SphericalHarmonicsL2>(0UL), 1));

            // RadeonRaysProbePostProcessor rejects ReferenceContext on Initialize and GPU ops,
            // while Dering accepts any IDeviceContext (returns true for 0 probes, false without native for >0).
            using var rrPp = new RadeonRaysProbePostProcessor();
            Assert.False(rrPp.Initialize(refCtx));
            Assert.True(rrPp.Initialize(rrCtx));
            Assert.False(rrPp.WindowSphericalHarmonicsL2(refCtx, shBuf.Slice<SphericalHarmonicsL2>(0UL), outBuf.Slice<SphericalHarmonicsL2>(0UL), 1));
            Assert.True(rrPp.DeringSphericalHarmonicsL2(refCtx, shBuf.Slice<SphericalHarmonicsL2>(0UL), outBuf.Slice<SphericalHarmonicsL2>(0UL), 0));
            Assert.False(rrPp.DeringSphericalHarmonicsL2(refCtx, shBuf.Slice<SphericalHarmonicsL2>(0UL), outBuf.Slice<SphericalHarmonicsL2>(0UL), 1));

            refCtx.DestroyBuffer(shBuf);
            refCtx.DestroyBuffer(outBuf);
        }

        [Fact]
        public void UvScaleOffsetNormalizesEachAxisIndependentlyAndResultFormatsLikeUnity()
        {
            // Normal 2D bounds
            var normal = InputExtraction.UvScaleOffset(new[] { new Vector2(2f, 4f), new Vector2(6f, 12f) });
            Assert.Equal(0.25f, normal.x, 5);
            Assert.Equal(0.125f, normal.y, 5);
            Assert.Equal(-0.5f, normal.z, 5);
            Assert.Equal(-0.5f, normal.w, 5);

            // 1D degenerate U (constant u = 2.0, v in [2.0, 4.0]): V is still normalized while U scale is 0.
            var oneD = InputExtraction.UvScaleOffset(new[] { new Vector2(2f, 2f), new Vector2(2f, 4f) });
            Assert.Equal(0f, oneD.x, 5);
            Assert.Equal(0.5f, oneD.y, 5);
            Assert.Equal(0f, oneD.z, 5);
            Assert.Equal(-1f, oneD.w, 5);

            Assert.Equal(Vector4.zero, InputExtraction.UvScaleOffset(Array.Empty<Vector2>()));

            Assert.Equal("Result type: 'Success'", new IProbeIntegrator.Result(IProbeIntegrator.ResultType.Success, "").ToString());
            Assert.Equal("Result type: 'InvalidInput', message: 'bad'", new IProbeIntegrator.Result(IProbeIntegrator.ResultType.InvalidInput, "bad").ToString());
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

        [Fact]
        public void ComputeOcclusionLightIndicesFiltersSpotConesAndAvoidsDuplicateChannels()
        {
            // Two mixed spot lights whose range spheres overlap at (-2, 0, 3) but whose 30-deg cones
            // do not overlap, so shadowmask::assign puts both on channel 0.
            var input = new InputExtraction.BakeInput
            {
                Lights = new[]
                {
                    new InputExtraction.ExtractedLightMeta
                    {
                        Kind = 2, Mode = 1, Position = new Vector3(-2f, 3f, 3f),
                        Forward = Vector3.down, Range = 6f, SpotAngle = 30f, ShadowmaskChannel = 0,
                    },
                    new InputExtraction.ExtractedLightMeta
                    {
                        Kind = 2, Mode = 1, Position = new Vector3(2f, 3f, 3f),
                        Forward = Vector3.down, Range = 6f, SpotAngle = 30f, ShadowmaskChannel = 0,
                    },
                }
            };
            var positions = new[] { new Vector3(-2f, 0f, 3f), new Vector3(2f, 0f, 3f) };
            var indices = InputExtraction.ComputeOcclusionLightIndicesFromBakeInput(input, positions, 4U);
            Assert.Equal(new[] { 0, -1, -1, -1, 1, -1, -1, -1 }, indices);

            // Two overlapping in-range lights sharing ShadowmaskChannel 0 must be deduplicated
            // per probe (only the first light is kept), and a non-positive range light is rejected.
            var overlapInput = new InputExtraction.BakeInput
            {
                Lights = new[]
                {
                    new InputExtraction.ExtractedLightMeta
                    {
                        Kind = 1, Mode = 1, Position = Vector3.zero, Range = -5f, ShadowmaskChannel = 0,
                    },
                    new InputExtraction.ExtractedLightMeta
                    {
                        Kind = 1, Mode = 1, Position = Vector3.zero, Range = 10f, ShadowmaskChannel = 0,
                    },
                    new InputExtraction.ExtractedLightMeta
                    {
                        Kind = 0, Mode = 1, Position = Vector3.zero, Range = 0f, ShadowmaskChannel = 0,
                    },
                    new InputExtraction.ExtractedLightMeta
                    {
                        Kind = 0, Mode = 1, Position = Vector3.zero, Range = 0f, ShadowmaskChannel = 1,
                    },
                }
            };
            var dedup = InputExtraction.ComputeOcclusionLightIndicesFromBakeInput(overlapInput, new[] { new Vector3(1f, 0f, 0f) }, 4U);
            Assert.Equal(new[] { 1, 3, -1, -1 }, dedup);

            // NaN range, spot angle, forward, or probe position must be rejected rather than passing comparisons.
            var nanLights = new InputExtraction.BakeInput
            {
                Lights = new[]
                {
                    new InputExtraction.ExtractedLightMeta { Kind = 1, Mode = 1, Position = Vector3.zero, Range = float.NaN, ShadowmaskChannel = 0 },
                    new InputExtraction.ExtractedLightMeta { Kind = 2, Mode = 1, Position = Vector3.zero, Forward = Vector3.forward, Range = 10f, SpotAngle = float.NaN, ShadowmaskChannel = 1 },
                    new InputExtraction.ExtractedLightMeta { Kind = 2, Mode = 1, Position = Vector3.zero, Forward = new Vector3(float.NaN, 0f, 0f), Range = 10f, SpotAngle = 60f, ShadowmaskChannel = 2 },
                    new InputExtraction.ExtractedLightMeta { Kind = 1, Mode = 1, Position = Vector3.zero, Range = 10f, ShadowmaskChannel = 3 },
                }
            };
            var nanLightResult = InputExtraction.ComputeOcclusionLightIndicesFromBakeInput(nanLights, new[] { new Vector3(0f, 0f, 2f) }, 4U);
            Assert.Equal(new[] { 3, -1, -1, -1 }, nanLightResult);
            var nanProbeResult = InputExtraction.ComputeOcclusionLightIndicesFromBakeInput(nanLights, new[] { new Vector3(float.NaN, 0f, 0f) }, 4U);
            Assert.Equal(new[] { -1, -1, -1, -1 }, nanProbeResult);
        }

        sealed class LogCapture : ILogHandler
        {
            internal readonly System.Collections.Generic.List<string> Logs = new System.Collections.Generic.List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Logs.Add(string.Format(format, args));
            public void LogException(Exception exception, Object context) => Logs.Add(exception.Message);
        }

        [Fact]
        public void CollectSceneLightsWarnsLinearIntensityOnlyForNonDirectionalOrColorTemperature()
        {
            var prevHandler = Debug.unityLogger.logHandler;
            var capture = new LogCapture();
            bool prevLinear = GraphicsSettings.lightsUseLinearIntensity;
            var dirGo = new GameObject("LTDir");
            var pointGo = new GameObject("LTPoint");
            try
            {
                Debug.unityLogger.logHandler = capture;
                GraphicsSettings.lightsUseLinearIntensity = true;
                InputExtraction.s_Warned.Clear();

                var dir = dirGo.AddComponent<Light>();
                dir.type = LightType.Directional;
                dir.lightmapBakeType = LightmapBakeType.Baked;
                dir.useColorTemperature = false;
                var point = pointGo.AddComponent<Light>();
                point.type = LightType.Point;
                point.lightmapBakeType = LightmapBakeType.Baked;

                InputExtraction.CollectSceneLights(new[] { dir });
                Assert.DoesNotContain(capture.Logs, l => l.Contains("lightsUseLinearIntensity"));

                capture.Logs.Clear();
                dir.useColorTemperature = true;
                InputExtraction.CollectSceneLights(new[] { dir });
                Assert.Contains(capture.Logs, l => l.Contains("color temperature"));

                capture.Logs.Clear();
                InputExtraction.s_Warned.Clear();
                InputExtraction.CollectSceneLights(new[] { point });
                Assert.Contains(capture.Logs, l => l.Contains("lightsUseLinearIntensity"));

                // Custom RequestLightsDelegate is invoked by CollectSceneLights identically to BakedAtlas.Lights:
                // custom color/orientation is extracted, InitNoBake skips, throwing/non-Legacy falloff warns.
                dir.useColorTemperature = false;
                var skipGo = new GameObject("LTSkip");
                try
                {
                    var skip = skipGo.AddComponent<Light>();
                    skip.type = LightType.Directional;
                    skip.lightmapBakeType = LightmapBakeType.Baked;

                    capture.Logs.Clear();
                    InputExtraction.s_Warned.Clear();
                    var customRot = Quaternion.Euler(45f, 0f, 0f);
                    UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate((Light[] reqs, Unity.Collections.NativeArray<UnityEngine.Experimental.GlobalIllumination.LightDataGI> outs) =>
                    {
                        for (int i = 0; i < reqs.Length; i++)
                        {
                            var d = new UnityEngine.Experimental.GlobalIllumination.LightDataGI();
                            if (reqs[i] == skip)
                            {
                                d.InitNoBake(reqs[i].GetInstanceID());
                            }
                            else
                            {
                                var dl = new UnityEngine.Experimental.GlobalIllumination.DirectionalLight
                                {
                                    instanceID = reqs[i].GetInstanceID(),
                                    mode = UnityEngine.Experimental.GlobalIllumination.LightMode.Baked,
                                    color = new UnityEngine.Experimental.GlobalIllumination.LinearColor { red = 0.5f, green = 1f, blue = 0.25f, intensity = 4f },
                                    indirectColor = new UnityEngine.Experimental.GlobalIllumination.LinearColor { red = 1f, green = 1f, blue = 1f, intensity = 2f },
                                    orientation = customRot,
                                    shadow = true,
                                };
                                d.Init(ref dl);
                            }
                            outs[i] = d;
                        }
                    });
                    var (jsonList, metas) = InputExtraction.CollectSceneLights(new[] { dir, skip });
                    Assert.Single(jsonList);
                    Assert.Single(metas);
                    Assert.Empty(capture.Logs);
                    var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(jsonList[0])).RootElement;
                    Assert.Equal(2f, doc.GetProperty("color")[0].GetSingle(), 4);
                    Assert.Equal(4f, doc.GetProperty("color")[1].GetSingle(), 4);
                    Assert.Equal(1f, doc.GetProperty("color")[2].GetSingle(), 4);
                    Assert.Equal(customRot * Vector3.forward, metas[0].Forward);
                }
                finally
                {
                    UnityEngine.Experimental.GlobalIllumination.Lightmapping.ResetDelegate();
                    Object.DestroyImmediate(skipGo);
                }
            }
            finally
            {
                Debug.unityLogger.logHandler = prevHandler;
                GraphicsSettings.lightsUseLinearIntensity = prevLinear;
                InputExtraction.s_Warned.Clear();
                Object.DestroyImmediate(dirGo);
                Object.DestroyImmediate(pointGo);
            }
        }

        [Fact]
        public void IntegratorAndPostProcessorRejectInvalidSlicesAndNegativeCountsWithoutThrowing()
        {
            using var ctx = new RadeonRaysContext();
            Assert.True(ctx.Initialize());
            using var pp = new RadeonRaysProbePostProcessor();
            Assert.True(pp.Initialize(ctx));

            var unknownSlice = new BufferID(999UL).Slice<SphericalHarmonicsL2>(0UL);
            Assert.False(pp.WindowSphericalHarmonicsL2(ctx, unknownSlice, unknownSlice, 1));
            Assert.False(pp.DeringSphericalHarmonicsL2(ctx, unknownSlice, unknownSlice, 1));

            var shBuf = ctx.CreateBuffer(27UL, sizeof(float));
            var shSlice = shBuf.Slice<SphericalHarmonicsL2>(0UL);
            Assert.False(pp.ConvolveRadianceToIrradiance(ctx, shSlice, shSlice, -1));
            Assert.False(pp.ScaleSphericalHarmonicsL2(ctx, shBuf.Slice<SphericalHarmonicsL2>(ulong.MaxValue), shSlice, 1, 2f));
            // Without native runtime, Dering fails loudly instead of silently returning un-deringed input.
            Assert.False(pp.DeringSphericalHarmonicsL2(ctx, shSlice, shSlice, 1));
            Assert.True(pp.DeringSphericalHarmonicsL2(ctx, shSlice, shSlice, 0));

            using var world = new RadeonRaysWorld();
            world.WorldId = 1UL;
            using var integrator = new RadeonRaysProbeIntegrator();
            var posBuf = ctx.CreateBuffer(10UL, 12UL);
            var batchIdxBuf = ctx.CreateBuffer(16UL, sizeof(int));
            var batchOccBuf = ctx.CreateBuffer(16UL, sizeof(float));
            integrator.Prepare(ctx, world, posBuf.Slice<Vector3>(0UL), 0.0001f, 2);

            Assert.Equal(IProbeIntegrator.ResultType.InvalidInput,
                integrator.IntegrateDirectRadiance(ctx, -1, 1, 16, false, shSlice).type);
            Assert.Equal(IProbeIntegrator.ResultType.InvalidInput,
                integrator.IntegrateDirectRadiance(ctx, 0, -1, 16, false, shSlice).type);
            Assert.Equal(IProbeIntegrator.ResultType.InvalidInput,
                integrator.IntegrateDirectRadiance(ctx, 0, 1, 16, false, unknownSlice).type);
            Assert.Equal(IProbeIntegrator.ResultType.InvalidInput,
                integrator.IntegrateOcclusion(ctx, 0, 1, 16, 0, new BufferID(999UL).Slice<int>(0UL), new BufferID(999UL).Slice<float>(0UL)).type);
            // positionOffset offsets only positions, NOT perProbeLightIndices: a batch-sized 16-int
            // index buffer passes slice validation at positionOffset=4, positionCount=4 (reaching
            // Native.Call -> JobFailed without native runtime rather than InvalidInput), whereas
            // positionOffset=8, positionCount=4 exceeds the 10-probe position buffer -> InvalidInput.
            var occWithOffset = integrator.IntegrateOcclusion(ctx, 4, 4, 16, 4,
                batchIdxBuf.Slice<int>(0UL), batchOccBuf.Slice<float>(0UL));
            Assert.Equal(IProbeIntegrator.ResultType.JobFailed, occWithOffset.type);
            Assert.Equal(IProbeIntegrator.ResultType.InvalidInput,
                integrator.IntegrateOcclusion(ctx, 8, 4, 16, 4,
                    batchIdxBuf.Slice<int>(0UL), batchOccBuf.Slice<float>(0UL)).type);

            using var progress = new BakeProgressState();
            progress.Cancel();
            integrator.SetProgressReporter(progress);
            Assert.Equal(IProbeIntegrator.ResultType.Cancelled,
                integrator.IntegrateDirectRadiance(ctx, 0, 1, 16, false, shSlice).type);

            world.WorldId = 0UL; // Avoid native call on dispose in managed-only unit test.
        }
    }
}
