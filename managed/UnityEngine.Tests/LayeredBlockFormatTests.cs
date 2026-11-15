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
                DecodeBlockTexel = &Texel,
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

        /// <summary>A texel of one block: its first and last bytes and the texel's index.</summary>
        [UnmanagedCallersOnly]
        static int Texel(int format, byte* block, uint index, float* output)
        {
            if (format != (int)TextureFormat.ETC2_RGBA1) return 0;
            output[0] = block[0] / 255f;
            output[1] = block[7] / 255f;
            output[2] = index / 255f;
            output[3] = 1f;
            return 1;
        }

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

        /// <summary>An ETC2_RGBA1 texel reads through its 8-byte block (a volume on a device that
        /// makes ETC2 volumes, OpenGL Core): block (1, 0) of the 8x8 level holds bytes 8..15.</summary>
        [Fact]
        public void AnEtc2Rgba1TexelReadsItsBlock()
        {
            GraphicsDeviceProfile.Select("OpenGLCore");
            var volume = new Texture3D(8, 8, 1, TextureFormat.ETC2_RGBA1, false);
            try
            {
                var bytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
                volume.SetPixelData(bytes, 0);
                // Texel (5, 2): block (1, 0), its index 2 * 4 + 1 in the block.
                Assert.Equal(new Color32(8, 15, 9, 255), (Color32)volume.GetPixel(5, 2, 0));
            }
            finally { Object.DestroyImmediate(volume); }
        }

        sealed class Capture : ILogHandler
        {
            public readonly List<string> Messages = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Messages.Add(logType + ": " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
            public void LogException(Exception exception, Object context) => Messages.Add("Exception: " + exception.Message);
        }

        /// <summary>PVRTC of a size whose padded levels have block counts that are not powers of
        /// two (which the decoder cannot read; the Editor's answer is unmeasured) keeps RGBA32
        /// texels, warned, as before; small sides pad to a readable level and keep blocks.</summary>
        [Fact]
        public void PvrtcOfUnreadableSizesKeepsTexels()
        {
            Assert.False(LayeredPixels.Keeps(TextureFormat.PVRTC_RGBA4, 12, 12));
            Assert.True(LayeredPixels.Keeps(TextureFormat.PVRTC_RGBA4, 6, 6));
            Assert.False(LayeredPixels.Keeps(TextureFormat.PVRTC_RGB2, 24, 8));
            Assert.True(LayeredPixels.Keeps(TextureFormat.PVRTC_RGB2, 32, 8));
            Assert.True(LayeredPixels.Keeps(TextureFormat.ETC2_RGBA1, 12, 12));
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            Texture2DArray array = null, small = null;
            try
            {
                array = new Texture2DArray(12, 12, 1, TextureFormat.PVRTC_RGBA4, false, true);
                small = new Texture2DArray(6, 6, 1, TextureFormat.PVRTC_RGBA4, false, true);
            }
            finally { Debug.unityLogger.logHandler = previous; }
            try
            {
                Assert.Equal(12 * 12 * 4, array.GetPixelData<byte>(0, 0).Length);
                Assert.Equal(32, small.GetPixelData<byte>(0, 0).Length);
                Assert.Contains(capture.Messages, m => m.Contains("layered PVRTC_RGBA4 textures"));
            }
            finally
            {
                Object.DestroyImmediate(array);
                Object.DestroyImmediate(small);
            }
        }

        /// <summary>A serialized PVRTC array of such a size is read as Unbound reads formats it
        /// keeps nothing of (warned, no pixels); a readable size keeps its blocks.</summary>
        [Fact]
        public void SerializedPvrtcOfUnreadableSizesHasNoPixels()
        {
            var host = new Texture2DArray(4, 4, 1, TextureFormat.RGBA32, false) { name = "serialized" };
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                LayeredTextures.Serialized Read(int side) => LayeredTextures.Read(host, YNode.MakeMap(
                    ("m_Format", YNode.MakeScalar(((int)UnityEngine.Experimental.Rendering.GraphicsFormat.RGBA_PVRTC_4Bpp_UNorm).ToString())),
                    ("m_Width", YNode.MakeScalar(side.ToString())), ("m_Height", YNode.MakeScalar(side.ToString())),
                    ("image data", YNode.MakeScalar(new string('7', 2 * BlockFormats.LevelSize(TextureFormat.PVRTC_RGBA4, side, side))))));
                var unreadable = Read(12);
                Assert.Null(unreadable.Data);
                Assert.Contains(capture.Messages, m => m.Contains("'serialized' is stored as RGBA_PVRTC_4Bpp_UNorm, which Unbound does not read yet"));
                var readable = Read(16);
                Assert.Equal(TextureFormat.PVRTC_RGBA4, readable.Format);
                Assert.Equal(128, readable.Data.Length);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                Object.DestroyImmediate(host);
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
