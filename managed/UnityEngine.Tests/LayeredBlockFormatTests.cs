using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>ETC2_RGBA1, signed EAC and PVRTC layered textures keep their blocks, as the macOS
    /// Editor's do (tests/projects/nativegfx array.make.*): PVRTC levels of at least 8x8 texels at
    /// 4 bits (16x8 at 2), the others 4x4 blocks. Signed EAC is not read (as in the Editor) and
    /// goes to the renderer as signed 16-bit channels; the others decode to RGBA8. A stand-in
    /// native side answers the decodes with texels carrying the block's first byte.</summary>
    public unsafe class LayeredBlockFormatTests : IDisposable
    {
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", PrivateStatic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", PrivateStatic);
        static readonly List<(int Format, int Width, int Height, bool Eac16)> s_Decodes = new();
        static (int Format, byte[] Data)? s_Packet;

        readonly object m_PreviousApi = Api.GetValue(null), m_PreviousReady = Ready.GetValue(null);
        readonly GraphicsDeviceProfile m_PreviousDevice = GraphicsDeviceProfile.Current;

        public LayeredBlockFormatTests()
        {
            s_Decodes.Clear();
            s_Packet = null;
            var api = new NativeApi
            {
                Version = NativeApi.CurrentVersion, Call = &Call, FreeBuffer = &Free, SubmitCommands = &Submit,
                ReleaseMaterial = &Release, ReleaseTexture = &Release, ReleaseMesh = &Release, UploadTexture = &Upload,
            };
            Native.Initialize(&api);
            GraphicsDeviceProfile.Select("Metal");
        }

        public void Dispose()
        {
            Api.SetValue(null, m_PreviousApi);
            Ready.SetValue(null, m_PreviousReady);
            GraphicsDeviceProfile.Select(m_PreviousDevice.Type.ToString());
        }

        [UnmanagedCallersOnly]
        static int Call(byte* method, int methodLength, byte* request, int requestLength, NativeBuffer* response)
        {
            using var input = JsonDocument.Parse(Encoding.UTF8.GetString(request, requestLength));
            var root = input.RootElement;
            int format = root.GetProperty("format").GetInt32(), w = root.GetProperty("width").GetInt32(), h = root.GetProperty("height").GetInt32();
            bool eac16 = root.TryGetProperty("eac16", out var e) && e.GetBoolean();
            byte marker = Convert.FromBase64String(root.GetProperty("data").GetString())[0];
            s_Decodes.Add((format, w, h, eac16));
            int texel = eac16 ? (format == (int)TextureFormat.EAC_R_SIGNED ? 2 : 4) : 4;
            var encoded = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { rgba = Convert.ToBase64String(Enumerable.Repeat(marker, w * h * texel).ToArray()) }));
            response->Data = (byte*)NativeMemory.Alloc((nuint)encoded.Length);
            response->Length = encoded.Length;
            encoded.CopyTo(new Span<byte>(response->Data, encoded.Length));
            return 0;
        }

        [UnmanagedCallersOnly]
        static void Free(NativeBuffer buffer) => NativeMemory.Free(buffer.Data);

        [UnmanagedCallersOnly]
        static void Submit(byte* commands, long length) { }

        [UnmanagedCallersOnly]
        static void Release(ulong handle) { }

        [UnmanagedCallersOnly]
        static void Upload(ulong handle, TexturePacket* packet) =>
            s_Packet = (packet->Format, new ReadOnlySpan<byte>(packet->Data, (int)packet->DataLength).ToArray());

        [Theory]
        [InlineData(TextureFormat.ETC2_RGBA1, 4, 4, 8)]
        [InlineData(TextureFormat.EAC_R_SIGNED, 4, 4, 8)]
        [InlineData(TextureFormat.EAC_RG_SIGNED, 4, 4, 16)]
        [InlineData(TextureFormat.PVRTC_RGB2, 4, 4, 32)]
        [InlineData(TextureFormat.PVRTC_RGBA4, 4, 4, 32)]
        [InlineData(TextureFormat.PVRTC_RGBA2, 32, 8, 64)]
        [InlineData(TextureFormat.PVRTC_RGB4, 16, 16, 128)]
        [InlineData(TextureFormat.ETC2_RGBA1, 8, 4, 16)]
        public void TheBlocksAreKept(TextureFormat format, int width, int height, int levelBytes)
        {
            var array = new Texture2DArray(width, height, 2, format, false, true);
            try { Assert.Equal(levelBytes, array.GetPixelData<byte>(0, 1).Length); }
            finally { Object.DestroyImmediate(array); }
        }

        [Fact]
        public void SignedEacIsNotReadAndGoesUpAsSignedChannels()
        {
            var array = new Texture2DArray(4, 4, 2, TextureFormat.EAC_RG_SIGNED, false, true);
            try
            {
                array.SetPixelData(Enumerable.Repeat((byte)9, 16).ToArray(), 0, 0);
                array.SetPixelData(Enumerable.Repeat((byte)7, 16).ToArray(), 0, 1);
                var error = Assert.Throws<ArgumentException>(() => array.GetPixels32(0));
                Assert.Equal("Texture2DArray.GetPixels32: texture uses an unsupported format. (Texture '')", error.Message);
                Assert.Throws<ArgumentException>(() => array.GetPixels(1));
                array.Apply(false);
                Assert.Equal(new[] { (44, 4, 4, true), (44, 4, 4, true) }, s_Decodes);
                var packet = s_Packet.Value;
                Assert.Equal(PixelFormats.GpuRG16SNorm, packet.Format);
                Assert.Equal(2 * 16 * 4, packet.Data.Length);
                Assert.Equal(new byte[] { 9, 7 }, new[] { 0, 64 }.Select(at => packet.Data[at]).ToArray());
            }
            finally { Object.DestroyImmediate(array); }
        }

        [Fact]
        public void PvrtcAndEtc2Rgba1DecodeOnReadAndForTheRenderer()
        {
            foreach (var format in new[] { TextureFormat.PVRTC_RGBA4, TextureFormat.ETC2_RGBA1 })
            {
                s_Decodes.Clear();
                var array = new Texture2DArray(8, 8, 1, format, false, false);
                try
                {
                    int size = array.GetPixelData<byte>(0, 0).Length;
                    array.SetPixelData(Enumerable.Repeat((byte)200, size).ToArray(), 0, 0);
                    Assert.All(array.GetPixels32(0), c => Assert.Equal(new Color32(200, 200, 200, 200), c));
                    array.Apply(false);
                    Assert.Equal(0, s_Packet.Value.Format); // sRGB RGBA8
                    Assert.Equal(8 * 8 * 4, s_Packet.Value.Data.Length);
                    Assert.Contains(s_Decodes, d => d == ((int)format, 8, 8, false));
                }
                finally { Object.DestroyImmediate(array); }
            }
        }

        /// <summary>A PVRTC texel reads through its decoded level (its neighbours' blocks take
        /// part): a volume's GetPixel on a device that makes PVRTC volumes (OpenGL Core).</summary>
        [Fact]
        public void APvrtcTexelReadsThroughItsLevel()
        {
            GraphicsDeviceProfile.Select("OpenGLCore");
            var volume = new Texture3D(8, 8, 1, TextureFormat.PVRTC_RGBA4, false);
            try
            {
                volume.SetPixelData(Enumerable.Repeat((byte)120, volume.GetPixelData<byte>(0).Length).ToArray(), 0);
                Assert.Equal(new Color32(120, 120, 120, 120), (Color32)volume.GetPixel(3, 5, 0));
                Assert.Contains(s_Decodes, d => d == ((int)TextureFormat.PVRTC_RGBA4, 8, 8, false));
            }
            finally { Object.DestroyImmediate(volume); }
        }
    }
}
