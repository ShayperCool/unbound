using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>A screen camera's stand-in is a temporary stored as the screen is: its packet
    /// carries the screen-order flag (1 &lt;&lt; 16, above the sample count), and the pool never
    /// hands it out for an ordinary temporary of the same shape, nor an ordinary one for it.
    /// A stand-in native side keeps the uploaded packets' flags.</summary>
    public unsafe class ScreenOrderTemporaryTests : IDisposable
    {
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", PrivateStatic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", PrivateStatic);
        static readonly Dictionary<ulong, int> s_Flags = new();

        readonly object m_PreviousApi = Api.GetValue(null), m_PreviousReady = Ready.GetValue(null);

        public ScreenOrderTemporaryTests()
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
            Api.SetValue(null, m_PreviousApi);
            Ready.SetValue(null, m_PreviousReady);
        }

        [UnmanagedCallersOnly]
        static void Submit(byte* commands, long length) { }

        [UnmanagedCallersOnly]
        static void Release(ulong handle) { }

        [UnmanagedCallersOnly]
        static void Upload(ulong handle, TexturePacket* packet) => s_Flags[handle] = packet->RenderTarget;

        [Fact]
        public void StandInsAreStoredAsTheScreenAndPooledApart()
        {
            var desc = RenderTexture.TemporaryDescriptor(24, 16, 24, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 4);
            var standIn = RenderTexture.GetTemporary(desc, screenOrder: true);
            var plain = RenderTexture.GetTemporary(desc);
            try
            {
                Assert.NotSame(standIn, plain);
                Assert.Equal(1 << 16, s_Flags[standIn.EnsureCreated()] & (1 << 16));
                Assert.Equal(4, (s_Flags[standIn.EnsureCreated()] >> 8) & 0xff);
                Assert.Equal(0, s_Flags[plain.EnsureCreated()] & (1 << 16));
                // Released, each comes back only for its own kind.
                RenderTexture.ReleaseTemporary(standIn);
                var other = RenderTexture.GetTemporary(desc);
                Assert.NotSame(standIn, other);
                RenderTexture.ReleaseTemporary(other);
                RenderTexture.ReleaseTemporary(plain);
                Assert.Same(standIn, RenderTexture.GetTemporary(desc, screenOrder: true));
                var again = RenderTexture.GetTemporary(desc);
                Assert.True(ReferenceEquals(again, plain) || ReferenceEquals(again, other));
                RenderTexture.ReleaseTemporary(again);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(standIn);
                RenderTexture.ReleaseTemporary(plain);
            }
        }
    }
}
