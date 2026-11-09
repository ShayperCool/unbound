using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using Unity.Collections;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Which targets a render pass's subpasses bind and clear (tests/projects/urp
    /// subpass_mrt, subpass_mrt_cleared, subpass_mrt_mixed): every color output at once, in the
    /// subpass's order, with the depth attachment; each clearing output to its own color the
    /// first time a subpass uses it, a single clearing output of several clearing them all on the
    /// measured release. The clear tests set the project's release: they run alone.</summary>
    [Collection("Project release")]
    public sealed class RenderPassTargetTests : IDisposable
    {
        readonly RenderTexture m_A = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
        readonly RenderTexture m_B = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
        readonly RenderTexture m_C = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
        readonly RenderTexture m_Depth = new RenderTexture(8, 8, 24, RenderTextureFormat.Depth);

        public void Dispose()
        {
            RenderPasses.End();
            foreach (var t in new[] { m_A, m_B, m_C, m_Depth }) Object.DestroyImmediate(t);
        }

        /// <summary>A, B, C (colors) and a depth attachment; <paramref name="clearing"/> are the
        /// color attachments whose load action is Clear (to 0.1 * index, 0.2, 0.3).</summary>
        NativeArray<AttachmentDescriptor> Attachments(params int[] clearing)
        {
            var attachments = new NativeArray<AttachmentDescriptor>(4, Allocator.Temp);
            var textures = new[] { m_A, m_B, m_C, m_Depth };
            for (int i = 0; i < 4; i++)
            {
                var a = new AttachmentDescriptor(i == 3 ? RenderTextureFormat.Depth : RenderTextureFormat.ARGB32);
                a.ConfigureTarget(new RenderTargetIdentifier(textures[i]), true, true);
                if (Array.IndexOf(clearing, i) >= 0) a.ConfigureClear(ClearColor(i));
                attachments[i] = a;
            }
            return attachments;
        }

        static Color ClearColor(int index) => new Color(0.1f * index, 0.2f, 0.3f, 1f);

        const byte SetTargetOp = 3, ClearOp = 4, ViewportOp = 5, SetTargetsOp = 21;
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        static readonly FieldInfo s_Length = typeof(GfxStream).GetField("s_Length", StaticPrivate);

        /// <summary>The color clears <paramref name="record"/> emits: each the target bound
        /// alone when it ran and its color (the stream's SetTarget, SetTargets and Clear
        /// commands; another command fails the test).</summary>
        List<(ulong Target, Color Color)> ColorClears(Action record)
        {
            foreach (var t in new Texture[] { m_A, m_B, m_C, m_Depth }) TextureHandles.Of(t);
            int start = (int)s_Length.GetValue(null);
            record();
            var bytes = GfxStream.Recorded.Slice(start).ToArray();
            var clears = new List<(ulong, Color)>();
            ulong target = 0;
            for (int p = 0; p < bytes.Length;)
            {
                switch (bytes[p])
                {
                    case SetTargetOp:
                        target = BitConverter.ToUInt64(bytes, p + 1);
                        p += 25;
                        break;
                    case SetTargetsOp:
                        int n = (int)BitConverter.ToUInt32(bytes, p + 1);
                        target = 0;
                        p += 5 + 8 * n + 16;
                        break;
                    case ClearOp:
                        if ((bytes[p + 1] & GfxStream.ClearColor) != 0)
                            clears.Add((target, new Color(BitConverter.ToSingle(bytes, p + 2), BitConverter.ToSingle(bytes, p + 6),
                                BitConverter.ToSingle(bytes, p + 10), BitConverter.ToSingle(bytes, p + 14))));
                        p += 26;
                        break;
                    case ViewportOp:
                        p += 17;
                        break;
                    default:
                        throw new Xunit.Sdk.XunitException($"unexpected command {bytes[p]} at {p}");
                }
            }
            return clears;
        }

        List<(ulong, Color)> SubPassClears(string release, int[] clearing, params int[][] subpasses)
        {
            string version = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = release;
                using var attachments = Attachments(clearing);
                return ColorClears(() =>
                {
                    RenderPasses.Begin(attachments, 3, 8, 8);
                    foreach (var colors in subpasses) RenderPasses.SubPass(colors);
                });
            }
            finally { ProjectInfo.UnityVersion = version; }
        }

        /// <summary>One clearing output of two: on the measured release both clear to its color
        /// (subpass_mrt), on others only it does.</summary>
        [Fact]
        public void ASingleClearOfSeveralOutputsClearsThemAllOnTheMeasuredRelease()
        {
            var a = TextureHandles.Of(m_A);
            var b = TextureHandles.Of(m_B);
            Assert.Equal(new[] { (a, ClearColor(1)), (b, ClearColor(1)) }, SubPassClears("6000.0.71f1", new[] { 1 }, new[] { 0, 1 }));
            Assert.Equal(new[] { (b, ClearColor(1)) }, SubPassClears("6000.1.2f1", new[] { 1 }, new[] { 0, 1 }));
        }

        /// <summary>Two clearing outputs clear to their own colors, a loading one is kept, with
        /// or without a third output (subpass_mrt_cleared, subpass_mrt_mixed).</summary>
        [Fact]
        public void SeveralClearingOutputsClearToTheirOwnColors()
        {
            var a = TextureHandles.Of(m_A);
            var b = TextureHandles.Of(m_B);
            var c = TextureHandles.Of(m_C);
            Assert.Equal(new[] { (a, ClearColor(0)), (b, ClearColor(1)) }, SubPassClears("6000.0.71f1", new[] { 0, 1 }, new[] { 0, 1 }));
            Assert.Equal(new[] { (b, ClearColor(1)), (c, ClearColor(2)) }, SubPassClears("6000.0.71f1", new[] { 1, 2 }, new[] { 0, 1, 2 }));
        }

        /// <summary>An output clears the first time a subpass uses it, not again in a later one;
        /// a later subpass whose only clearing output joins an earlier output clears that one too
        /// on the measured release (as subpass_mrt's camera color, drawn before, is).</summary>
        [Fact]
        public void AnOutputClearsOncePerPass()
        {
            var a = TextureHandles.Of(m_A);
            var b = TextureHandles.Of(m_B);
            Assert.Equal(new[] { (a, ClearColor(0)), (a, ClearColor(1)), (b, ClearColor(1)) },
                SubPassClears("6000.0.71f1", new[] { 0, 1 }, new[] { 0 }, new[] { 0, 1 }, new[] { 1 }));
            Assert.Equal(new[] { (a, ClearColor(0)), (b, ClearColor(1)) },
                SubPassClears("6000.1.2f1", new[] { 0, 1 }, new[] { 0 }, new[] { 0, 1 }, new[] { 1 }, new[] { 0, 1 }));
        }

        [Fact]
        public void ASubpassBindsEveryColorOutputInItsOrder()
        {
            using var attachments = Attachments();
            RenderPasses.Begin(attachments, 3, 8, 8);
            RenderPasses.SubPass(new[] { 2, 0 });
            var state = GfxStream.Current;
            Assert.Equal(new[] { TextureHandles.Of(m_C), TextureHandles.Of(m_A) }, state.Colors);
            Assert.Equal(TextureHandles.Of(m_Depth), state.Depth);
            // One output: a single target (no color list).
            RenderPasses.SubPass(new[] { 1 });
            state = GfxStream.Current;
            Assert.Null(state.Colors);
            Assert.Equal(TextureHandles.Of(m_B), state.Color);
            Assert.Equal(TextureHandles.Of(m_Depth), state.Depth);
        }

        [Fact]
        public void ADepthOnlySubpassDrawsIntoTheDepthAttachment()
        {
            using var attachments = Attachments();
            RenderPasses.Begin(attachments, 3, 8, 8);
            RenderPasses.SubPass(ReadOnlySpan<int>.Empty);
            var state = GfxStream.Current;
            Assert.Null(state.Colors);
            Assert.Equal(TextureHandles.Of(m_Depth), state.Color);
        }

        /// <summary>An attachment without a target (the render graph's G-buffer between subpasses)
        /// draws into a texture of its own for the pass, which a later subpass reads as its
        /// input: input i is the global `_UnityFBInput{i}`.</summary>
        [Fact]
        public void ATargetlessAttachmentKeepsATextureThroughThePassAndIsReadAsInput()
        {
            using var attachments = Attachments();
            var gbuffer = new AttachmentDescriptor(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
            var withGBuffer = new NativeArray<AttachmentDescriptor>(5, Allocator.Temp);
            for (int i = 0; i < 4; i++) withGBuffer[i] = attachments[i];
            withGBuffer[4] = gbuffer;
            int start = (int)s_Length.GetValue(null);
            RenderPasses.Begin(withGBuffer, 3, 8, 8);
            RenderPasses.SubPass(new[] { 0, 4 });
            var colors = GfxStream.Current.Colors;
            Assert.Equal(TextureHandles.Of(m_A), colors[0]);
            ulong transient = colors[1];
            Assert.NotEqual(0UL, transient);
            Assert.DoesNotContain(transient, new[] { TextureHandles.Of(m_A), TextureHandles.Of(m_B), TextureHandles.Of(m_C), TextureHandles.Of(m_Depth) });
            // The next subpass reads it as input 0 and draws into B.
            RenderPasses.SubPass(new[] { 1 }, new[] { 4 });
            Assert.Equal(TextureHandles.Of(m_B), GfxStream.Current.Color);
            var bytes = GfxStream.Recorded.Slice(start).ToArray();
            var name = System.Text.Encoding.UTF8.GetBytes("_UnityFBInput0");
            int at = IndexOf(bytes, name);
            Assert.True(at >= 0, "the input is bound by name");
            Assert.True(IndexOf(bytes, BitConverter.GetBytes(transient), at + name.Length) >= 0, "the input is the attachment's texture");
            RenderPasses.End();
            withGBuffer.Dispose();
        }

        static int IndexOf(byte[] haystack, byte[] needle, int from = 0)
        {
            for (int i = from; i + needle.Length <= haystack.Length; i++)
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
            return -1;
        }

        /// <summary>Outputs outside the pass's attachments are left out (the others still bind).</summary>
        [Fact]
        public void OutputsOutsideTheAttachmentsAreLeftOut()
        {
            using var attachments = Attachments();
            RenderPasses.Begin(attachments, -1, 8, 8);
            RenderPasses.SubPass(new[] { 0, 7, 1 });
            Assert.Equal(new[] { TextureHandles.Of(m_A), TextureHandles.Of(m_B) }, GfxStream.Current.Colors);
        }
    }
}
