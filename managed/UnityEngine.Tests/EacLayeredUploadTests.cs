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
    /// <summary>EAC R and RG layered textures upload the 16-bit normalized channels their blocks
    /// decode to, image by image, level by level and a volume level's depth slices in turn, as
    /// one packet of the channels' format. A stand-in native side decodes each level to texels
    /// that carry its first block byte, and keeps the uploaded packet.</summary>
    public unsafe class EacLayeredUploadTests : IDisposable
    {
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", PrivateStatic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", PrivateStatic);

        /// <summary>Each decode: (format, width, height, first block byte).</summary>
        static readonly List<(int Format, int Width, int Height, byte Marker)> s_Decodes = new List<(int, int, int, byte)>();
        static (int Format, int MipCount, int Layers, int Dimension, byte[] Data)? s_Packet;

        readonly object m_PreviousApi = Api.GetValue(null), m_PreviousReady = Ready.GetValue(null);
        readonly GraphicsDeviceProfile m_PreviousDevice = GraphicsDeviceProfile.Current;

        public EacLayeredUploadTests()
        {
            s_Decodes.Clear();
            s_Packet = null;
            var api = new NativeApi
            {
                Version = NativeApi.CurrentVersion, Call = &Call, FreeBuffer = &Free, SubmitCommands = &Submit,
                ReleaseMaterial = &Release, ReleaseTexture = &Release, ReleaseMesh = &Release, UploadTexture = &Upload,
            };
            Native.Initialize(&api);
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
            int status = 1;
            string result = "not answered";
            if (Encoding.UTF8.GetString(method, methodLength) == "texture-decode-blocks")
            {
                using var input = JsonDocument.Parse(Encoding.UTF8.GetString(request, requestLength));
                var root = input.RootElement;
                int format = root.GetProperty("format").GetInt32(), w = root.GetProperty("width").GetInt32(), h = root.GetProperty("height").GetInt32();
                byte marker = Convert.FromBase64String(root.GetProperty("data").GetString())[0];
                s_Decodes.Add((format, w, h, marker));
                // 16-bit channels: one for EAC R, two for EAC RG.
                int bytes = w * h * (format == (int)TextureFormat.EAC_R ? 2 : 4);
                result = JsonSerializer.Serialize(new { rgba = Convert.ToBase64String(Enumerable.Repeat(marker, bytes).ToArray()) });
                status = 0;
            }
            var encoded = Encoding.UTF8.GetBytes(result);
            response->Data = (byte*)NativeMemory.Alloc((nuint)Math.Max(1, encoded.Length));
            response->Length = encoded.Length;
            encoded.CopyTo(new Span<byte>(response->Data, encoded.Length));
            return status;
        }

        [UnmanagedCallersOnly]
        static void Free(NativeBuffer buffer) => NativeMemory.Free(buffer.Data);

        [UnmanagedCallersOnly]
        static void Submit(byte* commands, long length) { }

        [UnmanagedCallersOnly]
        static void Release(ulong handle) { }

        [UnmanagedCallersOnly]
        static void Upload(ulong handle, TexturePacket* packet) =>
            s_Packet = (packet->Format, packet->MipCount, packet->Layers, packet->Dimension,
                new ReadOnlySpan<byte>(packet->Data, (int)packet->DataLength).ToArray());

        static byte[] Blocks(int count, int blockBytes, byte marker)
        {
            var data = new byte[count * blockBytes];
            for (int i = 0; i < count; i++) data[i * blockBytes] = marker;
            return data;
        }

        [Fact]
        public void EacArraysUploadTheirDecodedChannelsInOrder()
        {
            // EAC R, 4x4, two slices of one block: R16 channels, slice 0 then slice 1.
            var r = new Texture2DArray(4, 4, 2, TextureFormat.EAC_R, false, true);
            try
            {
                r.SetPixelData(Blocks(1, 8, 200), 0, 0);
                r.SetPixelData(Blocks(1, 8, 40), 0, 1);
                r.Apply(false);
                Assert.Equal(new[] { (41, 4, 4, (byte)200), (41, 4, 4, (byte)40) }, s_Decodes);
                var packet = s_Packet.Value;
                Assert.Equal(PixelFormats.GpuR16UNorm, packet.Format);
                Assert.Equal(2, packet.Layers);
                Assert.Equal(2 * 16 * 2, packet.Data.Length);
                Assert.All(packet.Data.Take(32), b => Assert.Equal(200, b));
                Assert.All(packet.Data.Skip(32), b => Assert.Equal(40, b));
            }
            finally { Object.DestroyImmediate(r); }

            // EAC RG, 8x8 with two levels, two slices: each slice's levels in turn, RG16.
            s_Decodes.Clear();
            var rg = new Texture2DArray(8, 8, 2, TextureFormat.EAC_RG, 2, true);
            try
            {
                rg.SetPixelData(Blocks(4, 16, 1), 0, 0);
                rg.SetPixelData(Blocks(1, 16, 2), 1, 0);
                rg.SetPixelData(Blocks(4, 16, 3), 0, 1);
                rg.SetPixelData(Blocks(1, 16, 4), 1, 1);
                rg.Apply(false);
                Assert.Equal(new[] { (43, 8, 8, (byte)1), (43, 4, 4, (byte)2), (43, 8, 8, (byte)3), (43, 4, 4, (byte)4) }, s_Decodes);
                var packet = s_Packet.Value;
                Assert.Equal(PixelFormats.GpuRG16UNorm, packet.Format);
                Assert.Equal(2, packet.MipCount);
                Assert.Equal(2, packet.Layers);
                Assert.Equal(2 * (64 + 16) * 4, packet.Data.Length);
                // Slice 0 level 0 (256 bytes), level 1 (64), then slice 1's.
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, new[] { 0, 256, 320, 576 }.Select(at => packet.Data[at]).ToArray());
            }
            finally { Object.DestroyImmediate(rg); }
        }

        /// <summary>A volume's level decodes depth slice by depth slice (OpenGL Core: Metal refuses
        /// EAC volumes).</summary>
        [Fact]
        public void EacVolumesUploadEachDepthSlice()
        {
            GraphicsDeviceProfile.Select("OpenGLCore");
            var volume = new Texture3D(4, 4, 2, TextureFormat.EAC_R, false);
            try
            {
                var blocks = Blocks(1, 8, 7).Concat(Blocks(1, 8, 9)).ToArray();
                volume.SetPixelData(blocks, 0);
                volume.Apply(false);
                Assert.Equal(new[] { (41, 4, 4, (byte)7), (41, 4, 4, (byte)9) }, s_Decodes);
                var packet = s_Packet.Value;
                Assert.Equal(3, packet.Dimension);
                Assert.Equal(2 * 16 * 2, packet.Data.Length);
            }
            finally { Object.DestroyImmediate(volume); }
        }
    }
}
