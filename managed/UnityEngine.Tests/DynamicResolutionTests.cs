using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>Dynamic resolution as the macOS Editor's Metal device does it
    /// (tests/projects/nativegfx <c>dynres.*</c>): <c>ScalableBufferManager.ResizeBuffers</c> keeps
    /// its factors within [0.05, 1]; a render texture marked <c>useDynamicScale</c> keeps its width
    /// and height while its images take the scaled size (rounded up), existing ones remade when
    /// next used; a camera drawing into one has a scaled pixelRect and scaledPixelWidth but an
    /// unscaled pixelWidth; ReadPixels past the scaled images is refused, inside them reads zeros.
    /// The Linux Editor's devices do not scale. A stand-in native side keeps the uploaded packets.</summary>
    public unsafe class DynamicResolutionTests : IDisposable
    {
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", PrivateStatic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", PrivateStatic);

        /// <summary>Each upload: (handle, width, height, logical width, logical height).</summary>
        static readonly List<(ulong Handle, int Width, int Height, int LogicalWidth, int LogicalHeight)> s_Uploads = new();

        readonly object m_PreviousApi = Api.GetValue(null), m_PreviousReady = Ready.GetValue(null);
        readonly GraphicsDeviceProfile m_PreviousDevice = GraphicsDeviceProfile.Current;
        readonly List<Object> m_Made = new List<Object>();

        public DynamicResolutionTests()
        {
            s_Uploads.Clear();
            var api = new NativeApi
            {
                Version = NativeApi.CurrentVersion, SubmitCommands = &Submit, ReleaseMaterial = &Release,
                ReleaseTexture = &Release, ReleaseMesh = &Release, UploadTexture = &Upload,
            };
            Native.Initialize(&api);
            GraphicsDeviceProfile.Select("Metal");
            ScalableBufferManager.ResizeBuffers(1f, 1f);
        }

        public void Dispose()
        {
            GraphicsDeviceProfile.Select("Metal");
            ScalableBufferManager.ResizeBuffers(1f, 1f);
            foreach (var o in m_Made) if (o != null) Object.DestroyImmediate(o);
            Api.SetValue(null, m_PreviousApi);
            Ready.SetValue(null, m_PreviousReady);
            GraphicsDeviceProfile.Select(m_PreviousDevice.Type.ToString());
        }

        [UnmanagedCallersOnly]
        static void Submit(byte* commands, long length) { }

        [UnmanagedCallersOnly]
        static void Release(ulong handle) { }

        [UnmanagedCallersOnly]
        static void Upload(ulong handle, TexturePacket* packet) =>
            s_Uploads.Add((handle, packet->Width, packet->Height, packet->LogicalWidth, packet->LogicalHeight));

        T Keep<T>(T o) where T : Object { m_Made.Add(o); return o; }

        RenderTexture Target(int width, int height, bool dynamic)
        {
            var texture = Keep(new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { useDynamicScale = dynamic });
            texture.Create();
            return texture;
        }

        [Fact]
        public void FactorsAreKeptWithinTheEditorsRange()
        {
            Assert.True(SystemInfo.supportsDynamicResolution);
            Assert.Equal((1f, 1f), (ScalableBufferManager.widthScaleFactor, ScalableBufferManager.heightScaleFactor));
            ScalableBufferManager.ResizeBuffers(0.5f, 0.25f);
            Assert.Equal((0.5f, 0.25f), (ScalableBufferManager.widthScaleFactor, ScalableBufferManager.heightScaleFactor));
            // dynres.factors.outside: 2 keeps 1, 0 keeps the smallest factor, each asserted against.
            var logs = new List<string>();
            void OnLog(string message, string stack, LogType type) => logs.Add(type + ": " + message);
            Application.logMessageReceived += OnLog;
            try { ScalableBufferManager.ResizeBuffers(2f, 0f); }
            finally { Application.logMessageReceived -= OnLog; }
            Assert.Equal(new[] { "Assert: Invalid widthScale: 2.000000", "Assert: Invalid heightScale: 0.000000" }, logs);
            Assert.Equal(1f, ScalableBufferManager.widthScaleFactor);
            Assert.Equal(0.0500000119f, ScalableBufferManager.heightScaleFactor);
            // The Editor's 0.0500000119 (Mono's "R" text): these bits, the float after 0.05f by three.
            Assert.Equal(0x3D4CCCD0, BitConverter.SingleToInt32Bits(ScalableBufferManager.heightScaleFactor));
        }

        [Fact]
        public void DynamicTexturesTakeTheScaledSizeAndAreRemadeWhenTheFactorsChange()
        {
            var dynamic = Target(15, 13, true);
            var fixedSize = Target(15, 13, false);
            Assert.Equal((15, 13, 0, 0), Last(dynamic));
            int uploads = s_Uploads.Count;
            ScalableBufferManager.ResizeBuffers(0.5f, 0.25f);
            // Nothing is remade until the texture is used; then it is, once, at 8x4 (7.5 and 3.25
            // rounded up), keeping its size before scaling for its _TexelSize.
            Assert.Equal(uploads, s_Uploads.Count);
            dynamic.EnsureCreated();
            dynamic.EnsureCreated();
            fixedSize.EnsureCreated();
            Assert.Equal(uploads + 1, s_Uploads.Count);
            Assert.Equal((8, 4, 15, 13), Last(dynamic));
            Assert.Equal((15, 13), (dynamic.width, dynamic.height));
            Assert.True(dynamic.DynamicallyScaled);
            Assert.False(fixedSize.DynamicallyScaled);
            // Made after the resize, and a temporary: scaled too; a pooled plain texture of the
            // same shape is not handed out for a dynamic one.
            var after = Target(15, 13, true);
            Assert.Equal((8, 4, 15, 13), Last(after));
            var plain = RenderTexture.GetTemporary(new RenderTextureDescriptor(15, 13, RenderTextureFormat.ARGB32, 0) { sRGB = false });
            plain.Create();
            RenderTexture.ReleaseTemporary(plain);
            var temporary = RenderTexture.GetTemporary(new RenderTextureDescriptor(15, 13, RenderTextureFormat.ARGB32, 0) { sRGB = false, useDynamicScale = true });
            Assert.NotSame(plain, temporary);
            temporary.Create();
            Assert.Equal((8, 4, 15, 13), Last(temporary));
            RenderTexture.ReleaseTemporary(temporary);
            // Back to 1: full size again, without a size before scaling.
            ScalableBufferManager.ResizeBuffers(1f, 1f);
            dynamic.EnsureCreated();
            Assert.Equal((15, 13, 0, 0), Last(dynamic));
        }

        [Fact]
        public void CamerasDrawingIntoADynamicTextureHaveItsScaledViewport()
        {
            var dynamic = Target(16, 16, true);
            var odd = Target(15, 13, true);
            var fixedSize = Target(16, 16, false);
            var camera = Keep(new GameObject("dynamic camera")).AddComponent<Camera>();
            camera.enabled = false;
            ScalableBufferManager.ResizeBuffers(0.5f, 0.25f);
            foreach (var allow in new[] { true, false })
            {
                camera.allowDynamicResolution = allow;
                Assert.Equal(allow, camera.allowDynamicResolution);
                camera.targetTexture = dynamic;
                Assert.Equal((16, 16, 8, 4), (camera.pixelWidth, camera.pixelHeight, camera.scaledPixelWidth, camera.scaledPixelHeight));
                Assert.Equal(new Rect(0, 0, 8, 4), camera.pixelRect);
                camera.targetTexture = odd;
                Assert.Equal((15, 13, 8, 4), (camera.pixelWidth, camera.pixelHeight, camera.scaledPixelWidth, camera.scaledPixelHeight));
                camera.targetTexture = fixedSize;
                Assert.Equal((16, 16, 16, 16), (camera.pixelWidth, camera.pixelHeight, camera.scaledPixelWidth, camera.scaledPixelHeight));
            }
            // A viewport of the left half (unmeasured beside the full one): the same proportion.
            camera.targetTexture = dynamic;
            camera.rect = new Rect(0f, 0f, 0.5f, 1f);
            Assert.Equal((8, 16, 4, 4), (camera.pixelWidth, camera.pixelHeight, camera.scaledPixelWidth, camera.scaledPixelHeight));
        }

        [Fact]
        public void ReadPixelsOfAScaledTextureIsRefusedPastItsImagesAndReadsZerosInside()
        {
            var dynamic = Target(16, 16, true);
            ScalableBufferManager.ResizeBuffers(0.5f, 0.25f);
            var cpu = Keep(new Texture2D(16, 16, TextureFormat.RGBA32, false, true));
            var grey = new Color32[256];
            Array.Fill(grey, new Color32(0xcd, 0xcd, 0xcd, 0xcd));
            cpu.SetPixels32(grey);
            var logs = new List<string>();
            void OnLog(string message, string stack, LogType type) => logs.Add(type + ": " + message);
            Application.logMessageReceived += OnLog;
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = dynamic;
                cpu.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
                Assert.Equal(new[] { "Error: Reading pixels out of bounds of the current active render texture" }, logs);
                Assert.All(cpu.GetPixels32(), c => Assert.Equal(new Color32(0xcd, 0xcd, 0xcd, 0xcd), c));
                cpu.ReadPixels(new Rect(0, 0, 8, 4), 0, 0);
                Assert.Single(logs);
                var texels = cpu.GetPixels32();
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                        Assert.Equal(x < 8 && y < 4 ? new Color32(0, 0, 0, 0) : new Color32(0xcd, 0xcd, 0xcd, 0xcd), texels[y * 16 + x]);
            }
            finally
            {
                RenderTexture.active = previous;
                Application.logMessageReceived -= OnLog;
            }
        }

        /// <summary>The Linux Editor's devices do not scale: the factors stay 1, a dynamic texture
        /// keeps its size and cameras report false.</summary>
        [Fact]
        public void TheLinuxDevicesDoNotScale()
        {
            foreach (var device in new[] { "OpenGLCore", "Vulkan" })
            {
                GraphicsDeviceProfile.Select(device);
                ScalableBufferManager.ResizeBuffers(0.5f, 0.25f);
                Assert.Equal((1f, 1f), (ScalableBufferManager.widthScaleFactor, ScalableBufferManager.heightScaleFactor));
                var dynamic = Target(16, 16, true);
                Assert.Equal((16, 16, 0, 0), Last(dynamic));
                Assert.False(dynamic.DynamicallyScaled);
                var camera = Keep(new GameObject("camera")).AddComponent<Camera>();
                camera.enabled = false;
                camera.allowDynamicResolution = true;
                Assert.False(camera.allowDynamicResolution);
                camera.targetTexture = dynamic;
                Assert.Equal((16, 16), (camera.scaledPixelWidth, camera.scaledPixelHeight));
            }
        }

        static (int, int, int, int) Last(RenderTexture texture)
        {
            ulong handle = texture.EnsureCreated();
            for (int i = s_Uploads.Count - 1; i >= 0; i--)
                if (s_Uploads[i].Handle == handle)
                    return (s_Uploads[i].Width, s_Uploads[i].Height, s_Uploads[i].LogicalWidth, s_Uploads[i].LogicalHeight);
            throw new InvalidOperationException("never uploaded");
        }
    }
}
