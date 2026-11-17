using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Engine members render pipelines and packages call, as the Unity 6000.0.71f1 Editor answers
    /// them (tests/projects/runtimeapis): stereo matrices without an XR device, color gamuts,
    /// built-in shader modes, rotation-order Euler angles, the screen's MSAA samples and the fast
    /// memory commands, each next to a control.
    /// </summary>
    public sealed class RuntimeApisTests : IDisposable
    {
        const string UnsetProjection = "Warning: Camera.GetStereoProjectionMatrix is returning an invalid valid value since the value has not been set.\n";
        readonly List<string> m_Logs = new List<string>();
        readonly List<Object> m_Owned = new List<Object>();

        public RuntimeApisTests() => Application.logMessageReceived += Record;

        public void Dispose()
        {
            Application.logMessageReceived -= Record;
            foreach (var o in m_Owned) if (o != null) Object.DestroyImmediate(o);
        }

        void Record(string message, string stack, LogType type) => m_Logs.Add(type + ": " + message);

        string[] Logs(Action action)
        {
            m_Logs.Clear();
            action();
            return m_Logs.ToArray();
        }

        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        static readonly Matrix4x4 P = Matrix4x4.Perspective(50f, 1.5f, 0.2f, 300f);
        static readonly Matrix4x4 P2 = Matrix4x4.Perspective(70f, 2f, 0.5f, 100f);
        static readonly Matrix4x4 V = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 30f), Vector3.one);

        [Fact]
        public void StereoMatricesAreThoseSetPerEyeAndIdentityUntilThen()
        {
            var cam = Own(new GameObject("Stereo")).AddComponent<Camera>();
            var left = Camera.StereoscopicEye.Left;
            var right = Camera.StereoscopicEye.Right;
            var own = cam.projectionMatrix;
            Matrix4x4 m = default;
            Assert.Equal(new[] { UnsetProjection }, Logs(() => m = cam.GetStereoProjectionMatrix(left)));
            Assert.Equal(Matrix4x4.identity, m);
            // The non-jittered and view getters answer identity without a warning.
            Assert.Empty(Logs(() => m = cam.GetStereoNonJitteredProjectionMatrix(left)));
            Assert.Equal(Matrix4x4.identity, m);
            Assert.Equal(Matrix4x4.identity, cam.GetStereoViewMatrix(left));

            cam.SetStereoProjectionMatrix(left, P);
            Assert.Empty(Logs(() => Assert.Equal(P, cam.GetStereoProjectionMatrix(left))));
            Assert.Equal(new[] { UnsetProjection }, Logs(() => Assert.Equal(Matrix4x4.identity, cam.GetStereoProjectionMatrix(right))));
            Assert.Equal(P, cam.GetStereoNonJitteredProjectionMatrix(left));
            // Control: the camera's own projection is not the eye's.
            Assert.Equal(own, cam.projectionMatrix);

            // The device copy and the resets keep what was set; the non-jittered matrix follows
            // the projection set after a copy.
            cam.CopyStereoDeviceProjectionMatrixToNonJittered(left);
            cam.SetStereoProjectionMatrix(left, P2);
            Assert.Equal(P2, cam.GetStereoNonJitteredProjectionMatrix(left));
            cam.ResetStereoProjectionMatrices();
            Assert.Equal(P2, cam.GetStereoProjectionMatrix(left));

            cam.SetStereoViewMatrix(left, V);
            Assert.Equal(V, cam.GetStereoViewMatrix(left));
            Assert.Equal(Matrix4x4.identity, cam.GetStereoViewMatrix(right));
            cam.ResetStereoViewMatrices();
            Assert.Equal(V, cam.GetStereoViewMatrix(left));
            Assert.NotEqual(V, cam.worldToCameraMatrix);
            Assert.Equal(Camera.SceneViewFilterMode.Off, cam.sceneViewFilterMode);
        }

        [Fact]
        public void ColorGamutsHaveTheirPrimariesWhitePointAndTransfer()
        {
            (ColorGamut, ColorPrimaries, TransferFunction)[] table =
            {
                (ColorGamut.sRGB, ColorPrimaries.Rec709, TransferFunction.sRGB),
                (ColorGamut.Rec709, ColorPrimaries.Rec709, TransferFunction.Linear),
                (ColorGamut.Rec2020, ColorPrimaries.Rec2020, TransferFunction.Linear),
                (ColorGamut.DisplayP3, ColorPrimaries.P3, TransferFunction.sRGB),
                (ColorGamut.HDR10, ColorPrimaries.Rec2020, TransferFunction.PQ),
                (ColorGamut.DolbyHDR, ColorPrimaries.Rec2020, TransferFunction.PQ),
                (ColorGamut.P3D65G22, ColorPrimaries.P3, TransferFunction.Gamma22),
            };
            foreach (var (gamut, primaries, transfer) in table)
            {
                Assert.Equal(primaries, ColorGamutUtility.GetColorPrimaries(gamut));
                Assert.Equal(WhitePoint.D65, ColorGamutUtility.GetWhitePoint(gamut));
                Assert.Equal(transfer, ColorGamutUtility.GetTransferFunction(gamut));
            }
            Assert.Equal(Enum.GetValues(typeof(ColorGamut)).Length, table.Length);
        }

        [Fact]
        public void ShaderModesAreTheProjectsAndTheRemovedTypeAsserts()
        {
            try
            {
                GraphicsSettings.LoadShaderModes(new Dictionary<string, int> { ["m_DeferredReflections"] = 0, ["m_LensFlare"] = 2 });
                Assert.Equal(BuiltinShaderMode.Disabled, GraphicsSettings.GetShaderMode(BuiltinShaderType.DeferredReflections));
                Assert.Equal(BuiltinShaderMode.UseCustom, GraphicsSettings.GetShaderMode(BuiltinShaderType.LensFlare));
                // Control: a type the settings do not list uses the built-in shader, quietly.
                Assert.Empty(Logs(() => Assert.Equal(BuiltinShaderMode.UseBuiltin, GraphicsSettings.GetShaderMode(BuiltinShaderType.DepthNormals))));
                BuiltinShaderMode mode = default;
                Assert.Equal(new[] { "Assert: Unknown builtin shader type 2" }, Logs(() => mode = GraphicsSettings.GetShaderMode((BuiltinShaderType)2)));
                Assert.Equal(BuiltinShaderMode.UseBuiltin, mode);
            }
            finally
            {
                GraphicsSettings.LoadShaderModes(new Dictionary<string, int>());
            }
        }

        [Fact]
        public void RotationOrderAnglesAreNearestTheHintInThousandths()
        {
            var t = Own(new GameObject("Rotated")).transform;
            t.localRotation = Quaternion.Euler(30f, 50f, 70f);
            Assert.Equal(RotationOrder.OrderZXY, t.rotationOrder);
            Assert.Equal(new Vector3(56.143f, -2.293f, 54.533f), t.GetLocalEulerAngles(RotationOrder.OrderXYZ));
            Assert.Equal(new Vector3(59.358f, -3.948f, 54.469f), t.GetLocalEulerAngles(RotationOrder.OrderXZY));
            Assert.Equal(new Vector3(70.355f, 48.849f, 28.231f), t.GetLocalEulerAngles(RotationOrder.OrderYZX));
            Assert.Equal(new Vector3(56.075f, -4.111f, 57.946f), t.GetLocalEulerAngles(RotationOrder.OrderYXZ));
            Assert.Equal(new Vector3(30f, 50f, 70f), t.GetLocalEulerAngles(RotationOrder.OrderZXY));
            Assert.Equal(new Vector3(41.930f, 41.561f, 39.210f), t.GetLocalEulerAngles(RotationOrder.OrderZYX));

            // A hint turns each angle by whole turns towards it; the rotation stays.
            var euler = t.localEulerAngles;
            t.SetLocalEulerHint(new Vector3(390f, 50f, 70f));
            Assert.Equal(new Vector3(390f, 50f, 70f), t.GetLocalEulerAngles(RotationOrder.OrderZXY));
            Assert.Equal(new Vector3(416.143f, -2.293f, 54.533f), t.GetLocalEulerAngles(RotationOrder.OrderXYZ));
            Assert.Equal(euler, t.localEulerAngles);

            // Of the two sets of angles, the one nearer the hint.
            t.SetLocalEulerHint(new Vector3(10f, 10f, 10f));
            t.localRotation = Quaternion.Euler(-20f, 200f, 300f);
            Assert.Equal(new Vector3(-20f, -160f, -60f), t.GetLocalEulerAngles(RotationOrder.OrderZXY));
            Assert.Equal(new Vector3(-27.358f, 173.839f, 54.937f), t.GetLocalEulerAngles(RotationOrder.OrderXYZ));
            t.localRotation = Quaternion.Euler(12.34567f, 0f, 0f);
            Assert.Equal(new Vector3(12.346f, 0f, 0f), t.GetLocalEulerAngles(RotationOrder.OrderZXY));
            // localEulerAngles sets no hint.
            t.localEulerAngles = new Vector3(400f, 0f, 0f);
            Assert.Equal(new Vector3(40f, 0f, 0f), t.GetLocalEulerAngles(RotationOrder.OrderZXY));

            // The order changes no rotation.
            var rotation = t.localRotation;
            t.rotationOrder = RotationOrder.OrderYXZ;
            Assert.Equal(RotationOrder.OrderYXZ, t.rotationOrder);
            Assert.Equal(rotation, t.localRotation);
        }

        sealed class EmptyPipelineAsset : RenderPipelineAsset
        {
            protected override RenderPipeline CreatePipeline() => new EmptyPipeline();
        }

        sealed class EmptyPipeline : RenderPipeline
        {
            protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }
        }

        [Fact]
        public void ScreenSamplesFollowQualityUntilAPipelineSetsThem()
        {
            const string BuiltinOnly = "Error: SetMSAASamples is only available when using a scriptable render pipeline. Please use the Graphics Settings for the built-in renderer.";
            var previous = GraphicsSettings.defaultRenderPipeline;
            var quality = QualitySettings.renderPipeline;
            int antiAliasing = QualitySettings.antiAliasing;
            var asset = Own(ScriptableObject.CreateInstance<EmptyPipelineAsset>());
            try
            {
                QualitySettings.renderPipeline = null;
                GraphicsSettings.defaultRenderPipeline = null;
                QualitySettings.antiAliasing = 4;
                Assert.Equal(4, Screen.msaaSamples);
                QualitySettings.antiAliasing = 0;
                Assert.Equal(0, Screen.msaaSamples);
                Assert.Equal(new[] { BuiltinOnly }, Logs(() => Screen.SetMSAASamples(4)));
                Assert.Equal(0, Screen.msaaSamples);

                GraphicsSettings.defaultRenderPipeline = asset;
                Assert.Equal(0, Screen.msaaSamples);
                Assert.Empty(Logs(() => Device.Screen.SetMSAASamples(3)));
                Assert.Equal(3, Screen.msaaSamples);
                Assert.Equal(0, QualitySettings.antiAliasing);
                QualitySettings.antiAliasing = 8;
                Assert.Equal(3, Screen.msaaSamples);
                GraphicsSettings.defaultRenderPipeline = null;
                Assert.Equal(3, Screen.msaaSamples);
            }
            finally
            {
                Screen.s_MsaaSamples = null;
                GraphicsSettings.defaultRenderPipeline = previous;
                QualitySettings.renderPipeline = quality;
                QualitySettings.antiAliasing = antiAliasing;
            }
        }

        [Fact]
        public void FastMemoryCommandsAreRecordedWithoutEffect()
        {
            var cb = new CommandBuffer();
            cb.SwitchIntoFastMemory(BuiltinRenderTextureType.CameraTarget, FastMemoryFlags.SpillTop, 0.5f, true);
            Assert.Equal(1, cb.CommandCount);
            cb.SwitchOutOfFastMemory(BuiltinRenderTextureType.CameraTarget, true);
            Assert.Equal(2, cb.CommandCount);
            Assert.Empty(Logs(() => Graphics.ExecuteCommandBuffer(cb)));
            cb.Release();
            Assert.Equal(0, cb.CommandCount);
        }

        [Fact]
        public void ImportExceptionsCarryTheirMessage()
        {
            Exception e = new InvalidImportException("bad import");
            Assert.Equal(typeof(Exception), e.GetType().BaseType);
            Assert.Equal("bad import", e.Message);
        }
    }
}
