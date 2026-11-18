using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>Copies between Unity's two 8-bit channel orders. A B8G8R8A8 color is kept red
    /// first by the renderer and flagged so (texture packet bit 19), where the Metal device stores
    /// it blue first and its copies to and from red-first textures move the stored bytes
    /// (tests/projects/cmdbuffers metal.jsonl `metal.copy.*`); a screen stand-in keeps the screen's
    /// order instead. A command buffer's whole-level copy between readable Texture2Ds copies their CPU
    /// bytes too, as they are (`metal.copy.bgra32.rgba32.cpu`).</summary>
    public unsafe class BlueFirstCopyTests : IDisposable
    {
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", PrivateStatic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", PrivateStatic);
        static readonly Dictionary<ulong, int> s_Flags = new();

        readonly object m_PreviousApi = Api.GetValue(null), m_PreviousReady = Ready.GetValue(null);
        readonly GraphicsDeviceProfile m_PreviousDevice = GraphicsDeviceProfile.Current;
        readonly List<Object> m_Made = new();

        public BlueFirstCopyTests()
        {
            s_Flags.Clear();
            var api = new NativeApi
            {
                Version = NativeApi.CurrentVersion, SubmitCommands = &Submit, ReleaseMaterial = &Release,
                ReleaseTexture = &Release, ReleaseMesh = &Release, UploadTexture = &Upload,
            };
            Native.Initialize(&api);
        }

        public void Dispose()
        {
            foreach (var o in m_Made) Object.DestroyImmediate(o);
            GraphicsDeviceProfile.Select(m_PreviousDevice.Type.ToString());
            Api.SetValue(null, m_PreviousApi);
            Ready.SetValue(null, m_PreviousReady);
        }

        [UnmanagedCallersOnly]
        static void Submit(byte* commands, long length) { }

        [UnmanagedCallersOnly]
        static void Release(ulong handle) { }

        [UnmanagedCallersOnly]
        static void Upload(ulong handle, TexturePacket* packet) => s_Flags[handle] = packet->RenderTarget;

        T Keep<T>(T o) where T : Object { m_Made.Add(o); return o; }

        int Flags(Texture t) => s_Flags[TextureHandles.Of(t)];

        [Fact]
        public void B8G8R8A8ColorsAreFlaggedBlueFirst()
        {
            // B8G8R8A8 render textures are Metal's (OpenGL Core refuses the format for them).
            GraphicsDeviceProfile.Select("Metal");
            var bgra = Keep(new RenderTexture(2, 2, GraphicsFormat.B8G8R8A8_UNorm, GraphicsFormat.None));
            var bgraSrgb = Keep(new RenderTexture(2, 2, GraphicsFormat.B8G8R8A8_SRGB, GraphicsFormat.None));
            var rgba = Keep(new RenderTexture(2, 2, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None));
            foreach (var rt in new[] { bgra, bgraSrgb, rgba }) rt.Create();
            Assert.Equal(TexturePacket.BlueFirst, Flags(bgra) & TexturePacket.BlueFirst);
            Assert.Equal(TexturePacket.BlueFirst, Flags(bgraSrgb) & TexturePacket.BlueFirst);
            Assert.Equal(0, Flags(rgba) & TexturePacket.BlueFirst);
            var texture = Keep(new Texture2D(2, 2, TextureFormat.BGRA32, false, true));
            texture.Apply(false);
            Assert.Equal(TexturePacket.BlueFirst, Flags(texture) & TexturePacket.BlueFirst);
            var plain = Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            plain.Apply(false);
            Assert.Equal(0, Flags(plain) & TexturePacket.BlueFirst);
        }

        [Fact]
        public void AScreenStandInKeepsTheScreensOrderInstead()
        {
            GraphicsDeviceProfile.Select("Metal");
            var desc = RenderTexture.TemporaryDescriptor(8, 8, 24, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1);
            var standIn = RenderTexture.GetTemporary(desc, screenOrder: true);
            var plain = RenderTexture.GetTemporary(desc);
            try
            {
                // Metal's default color is B8G8R8A8: an ordinary temporary is flagged, the stand-in
                // (stored as the screen is) is not.
                Assert.True(TexturePacket.IsBlueFirst(plain.graphicsFormat));
                Assert.Equal(TexturePacket.BlueFirst, s_Flags[plain.EnsureCreated()] & TexturePacket.BlueFirst);
                int standInFlags = s_Flags[standIn.EnsureCreated()];
                Assert.Equal(TexturePacket.ScreenOrder, standInFlags & TexturePacket.ScreenOrder);
                Assert.Equal(0, standInFlags & TexturePacket.BlueFirst);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(standIn);
                RenderTexture.ReleaseTemporary(plain);
            }
        }

        static Texture2D Pixels(TextureFormat format)
        {
            var t = new Texture2D(2, 2, format, false, true);
            t.SetPixels32(Enumerable.Range(0, 4).Select(i => new Color32((byte)(20 + 37 * i), (byte)(200 - 23 * i), (byte)(64 + 11 * i), (byte)(255 - 9 * i))).ToArray());
            t.Apply(false);
            return t;
        }

        [Fact]
        public void ACommandBufferCopyMovesTheCpuBytesAsTheyAre()
        {
            var source = Keep(Pixels(TextureFormat.BGRA32));
            var destination = Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            using (var cmd = new CommandBuffer())
            {
                cmd.CopyTexture(source, destination);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            // The Metal Editor's bytes: the source's stored BGRA order read as RGBA.
            Assert.Equal("40c814ff4bb139f6569a5eed618383e4",
                string.Concat(destination.GetPixelData<byte>(0).ToArray().Select(b => b.ToString("x2"))));
            // A control: between the same order the channels stay.
            var same = Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            var rgba = Keep(Pixels(TextureFormat.RGBA32));
            using (var cmd = new CommandBuffer())
            {
                cmd.CopyTexture(rgba, same);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            Assert.Equal(rgba.GetPixels32(), same.GetPixels32());
            // Another control: an unreadable source copies on the GPU alone, the CPU data stays.
            var unreadable = Keep(Pixels(TextureFormat.RGBA32));
            unreadable.Apply(false, true);
            var untouched = Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            var before = untouched.GetPixels32();
            using (var cmd = new CommandBuffer())
            {
                cmd.CopyTexture(unreadable, untouched);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            Assert.Equal(before, untouched.GetPixels32());
            // A region copies its pixels' bytes (the documented CPU copy of readable textures); one
            // that leaves the destination copies nothing on the CPU.
            var region = Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            var blank = region.GetPixels32();
            using (var cmd = new CommandBuffer())
            {
                cmd.CopyTexture(rgba, 0, 0, 1, 1, 1, 1, region, 0, 0, 0, 0);
                cmd.CopyTexture(rgba, 0, 0, 0, 0, 2, 2, region, 0, 0, 1, 1);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            var expected = (Color32[])blank.Clone();
            expected[0] = rgba.GetPixels32()[3];
            Assert.Equal(expected, region.GetPixels32());
        }
    }
}
