using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using Xunit;
using static UnityEngine.Tests.VisualEffectSimulationTests;

namespace UnityEngine.Tests
{
    /// <summary>What Debug logs while it is in use: "type: message" each (an exception as
    /// "Exception: message").</summary>
    internal sealed class RecordedLogs : ILogHandler, IDisposable
    {
        readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;
        public readonly List<string> Logged = new List<string>();
        public RecordedLogs() { Debug.unityLogger.logHandler = this; }
        public void Dispose() { Debug.unityLogger.logHandler = m_Previous; }
        public void LogException(Exception exception, Object context) => Logged.Add("Exception: " + exception.Message);
        public void LogFormat(LogType type, Object context, string format, params object[] args) =>
            Logged.Add(type + ": " + string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
    }

    /// <summary>The main camera as effects read it and the camera entry points a render
    /// pipeline calls (VFXManager), on compiled graphs made by hand.</summary>
    public class VisualEffectCameraTests
    {
        static uint Bits(float f) => BitConverter.SingleToUInt32Bits(f);

        /// <summary>A sheet with every operation that reads the main camera, in the enum's order;
        /// the buffer operations read a depth and a colour buffer.</summary>
        static VisualEffectCompiledData CameraSheet() => Data(new[]
        {
            Op(VFXExpressionOperation.ExtractMatrixFromMainCamera),
            Op(VFXExpressionOperation.ExtractFOVFromMainCamera),
            Op(VFXExpressionOperation.ExtractNearPlaneFromMainCamera),
            Op(VFXExpressionOperation.ExtractFarPlaneFromMainCamera),
            Op(VFXExpressionOperation.ExtractAspectRatioFromMainCamera),
            Op(VFXExpressionOperation.ExtractPixelDimensionsFromMainCamera),
            Op(VFXExpressionOperation.ExtractScaledPixelDimensionsFromMainCamera),
            Op(VFXExpressionOperation.ExtractLensShiftFromMainCamera),
            Op(VFXExpressionOperation.GetBufferFromMainCamera, -1, -1, -1, (int)VFXCameraBufferTypes.Depth),
            Op(VFXExpressionOperation.IsMainCameraOrthographic),
            Op(VFXExpressionOperation.GetOrthographicSizeFromMainCamera),
            Op(VFXExpressionOperation.GetBufferFromMainCamera, -1, -1, -1, (int)VFXCameraBufferTypes.Color),
        }, Array.Empty<VFXExpressionValueContainerDesc>());

        static VisualEffectValues Evaluated(VisualEffectCompiledData data)
        {
            var values = new VisualEffectValues(data);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
            return values;
        }


        [Fact]
        public void ASheetSaysWhetherItReadsTheMainCameraAndWhichOfItsBuffers()
        {
            var camera = CameraSheet();
            Assert.True(camera.ReadsMainCamera);
            Assert.Equal(VFXCameraBufferTypes.Depth | VFXCameraBufferTypes.Color, camera.CameraBuffers);
            // A graph whose programs read the buffers does not run yet (it still asks for them).
            Assert.Equal("the main camera's buffers read by its programs", VisualEffect.Unsupported(camera));
            var lens = Data(new[] { Op(VFXExpressionOperation.ExtractFOVFromMainCamera) }, Array.Empty<VFXExpressionValueContainerDesc>());
            Assert.True(lens.ReadsMainCamera);
            Assert.Null(VisualEffect.Unsupported(lens));
            Assert.Equal(VFXCameraBufferTypes.None, lens.CameraBuffers);
            // Among the expressions of a spawn event, a buffer is refused all the same.
            var perEvent = Data(new[] { Value(VFXValueType.Float) }, new[] { Constant(0, 1f) },
                perSpawnEvent: new[] { Op(VFXExpressionOperation.GetBufferFromMainCamera, -1, -1, -1, (int)VFXCameraBufferTypes.Depth) });
            Assert.Equal("the main camera's buffers read by its programs", VisualEffect.Unsupported(perEvent));
        }

        [Fact]
        public void TheEntryPointsRefuseNoCamera()
        {
            Assert.Equal("cam", Assert.Throws<ArgumentNullException>(() => VFXManager.IsCameraBufferNeeded(null)).ParamName);
            Assert.Equal("cam", Assert.Throws<ArgumentNullException>(() => VFXManager.SetCameraBuffer(null, VFXCameraBufferTypes.Depth, null, 0, 0, 1, 1)).ParamName);
            Assert.Equal("cam", Assert.Throws<ArgumentNullException>(() => VFXManager.PrepareCamera(null)).ParamName);
            Assert.Equal("cam", Assert.Throws<ArgumentNullException>(() => VFXManager.ProcessCameraCommand(null, null, default, default(CullingResults))).ParamName);
        }

        [Fact]
        public void TheMainCameraIsTakenAtTheUpdateWhileAnEnabledEffectReadsIt()
        {
            var eye = new GameObject("main") { tag = "MainCamera" };
            var main = eye.AddComponent<Camera>();
            main.targetTexture = new RenderTexture(64, 48, 24);
            main.fieldOfView = 40f;
            main.nearClipPlane = 0.5f;
            main.farClipPlane = 30f;
            eye.transform.SetPositionAndRotation(new Vector3(3f, 2f, -10f), Quaternion.Euler(5f, 10f, 0f));
            var other = new GameObject("other").AddComponent<Camera>();
            var asset = VisualEffectAsset.Create();
            asset.m_Data = CameraSheet();
            var effect = new GameObject("effect").AddComponent<VisualEffect>();
            var depth = new RenderTexture(16, 16, 0);
            var colour = new RenderTexture(8, 8, 0);
            try
            {
                // No effect reads the main camera: it is nobody's buffers.
                VFXManager.UpdateMainCamera();
                Assert.Null(VFXManager.MainCamera);
                Assert.Equal(VFXCameraBufferTypes.None, VFXManager.IsCameraBufferNeeded(main));

                effect.visualEffectAsset = asset;
                VFXManager.UpdateMainCamera();
                Assert.Same(main, VFXManager.MainCamera);
                Assert.Equal(VFXCameraBufferTypes.Depth | VFXCameraBufferTypes.Color, VFXManager.IsCameraBufferNeeded(main));
                Assert.Equal(VFXCameraBufferTypes.None, VFXManager.IsCameraBufferNeeded(other));

                // A buffer is the main camera's only, and is read from the next update on.
                VFXManager.SetCameraBuffer(other, VFXCameraBufferTypes.Depth, colour, 0, 0, 5, 5);
                VFXManager.SetCameraBuffer(main, VFXCameraBufferTypes.Depth, depth, 7, 9, 32, 24);
                Assert.Null(VFXManager.MainCameraBuffer((uint)VFXCameraBufferTypes.Depth));
                VFXManager.UpdateMainCamera();
                Assert.Same(depth, VFXManager.MainCameraBuffer((uint)VFXCameraBufferTypes.Depth));
                Assert.Null(VFXManager.MainCameraBuffer((uint)VFXCameraBufferTypes.Color));
                Assert.Null(VFXManager.MainCameraBuffer(3));

                var values = Evaluated(asset.m_Data);
                Assert.Equal(eye.transform.localToWorldMatrix, values.GetMatrix(0));
                Assert.Equal(Bits(40f * Mathf.Deg2Rad), values.GetWord(1));
                Assert.Equal(Bits(0.5f), values.GetWord(2));
                Assert.Equal(Bits(30f), values.GetWord(3));
                Assert.Equal(Bits(64f / 48f), values.GetWord(4));
                Assert.Equal(new Vector4(64f, 48f, 0f, 0f), values.GetVector(5, 2));
                // The size handed over with the buffer (the place in it is not kept).
                Assert.Equal(new Vector4(32f, 24f, 0f, 0f), values.GetVector(6, 2));
                Assert.Equal(new Vector4(0f, 0f, 0f, 0f), values.GetVector(7, 2));
                Assert.Same(depth, values.GetObject(8));
                Assert.Equal(0u, values.GetWord(9));
                Assert.Equal(Bits(main.orthographicSize), values.GetWord(10));
                Assert.Null(values.GetObject(11));

                // A value of no single kind is refused and one the effects do not read asserted,
                // with the Editor's messages; the size given with the last call is read.
                using (var logs = new RecordedLogs())
                {
                    VFXManager.SetCameraBuffer(main, VFXCameraBufferTypes.Depth | VFXCameraBufferTypes.Color, colour, 0, 0, 10, 10);
                    VFXManager.SetCameraBuffer(main, VFXCameraBufferTypes.Normal, colour, 0, 0, 12, 9);
                    Assert.Equal(new[]
                    {
                        "Error: Invalid value for bufferType: 3. One and only one type bit should be set",
                        "Assert: Assertion failed on expression: 'HasFlag(flag, bufferType)'",
                    }, logs.Logged);
                }
                VFXManager.UpdateMainCamera();
                Assert.Equal(new Vector2Int(12, 9), VFXManager.MainCameraScaledSize);
                Assert.Same(depth, VFXManager.MainCameraBuffer((uint)VFXCameraBufferTypes.Depth));

                // Without a camera tagged as the main one: said once;
                // the graph reads a new camera's lens, a 1920x1080 screen and no buffers.
                eye.tag = "Untagged";
                using (var logs = new RecordedLogs())
                {
                    VFXManager.UpdateMainCamera();
                    VFXManager.UpdateMainCamera();
                    Assert.Equal(new[] { "Warning: Visual effect requires the main camera but there are no active cameras with the \"MainCamera\" tag" }, logs.Logged);
                    Assert.Null(VFXManager.MainCamera);
                    Assert.Equal(VFXCameraBufferTypes.None, VFXManager.IsCameraBufferNeeded(main));
                    values = Evaluated(asset.m_Data);
                    Assert.Equal(Matrix4x4.identity, values.GetMatrix(0));
                    Assert.Equal(Bits(60f * Mathf.Deg2Rad), values.GetWord(1));
                    Assert.Equal(Bits(0.3f), values.GetWord(2));
                    Assert.Equal(Bits(1000f), values.GetWord(3));
                    Assert.Equal(Bits(1f), values.GetWord(4));
                    Assert.Equal(new Vector4(1920f, 1080f, 0f, 0f), values.GetVector(5, 2));
                    Assert.Null(values.GetObject(8));
                    Assert.Equal(0u, values.GetWord(9));
                    Assert.Equal(Bits(5f), values.GetWord(10));
                }
                // A destroyed buffer is none.
                eye.tag = "MainCamera";
                Object.DestroyImmediate(depth);
                VFXManager.UpdateMainCamera();
                Assert.Null(VFXManager.MainCameraBuffer((uint)VFXCameraBufferTypes.Depth));
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
                Object.DestroyImmediate(eye);
                Object.DestroyImmediate(other.gameObject);
                VFXManager.UpdateMainCamera();
            }
            Assert.Null(VFXManager.MainCamera);
        }
    }
}
