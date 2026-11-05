using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>
    /// Graphics.RenderPrimitivesIndexed and RenderPrimitivesIndexedIndirect (Scripting API 6000.0:
    /// the shader reads SV_VertexID, "set with values in the indexBuffer"). Unbound draws the
    /// direct call from a 16-bit index buffer (stride 2) of triangles, lines or points: one draw of
    /// the 65536-vertex procedural stand-in whose setup carries the buffer's indices (render
    /// protocol 8, DrawIndices after DrawSetup), so every index value has a vertex. It refuses the
    /// rest loudly, counted (Stubs.HitReport) and warned once, before anything is queued or
    /// recorded: 32-bit indices (the documented example's), every indirect call, quads, line
    /// strips and other strides. Arguments are checked as the neighbouring RenderPrimitives*
    /// methods check theirs. Unbound's warnings never reach Application's log callbacks, so they
    /// are observed at the logger's handler (installed here, restored in <see cref="Dispose"/>).
    /// </summary>
    public class GraphicsRenderPrimitivesIndexedTests : IDisposable
    {
        const string Indexed = "UnityEngine.Graphics.RenderPrimitivesIndexed";
        const string Indirect = "UnityEngine.Graphics.RenderPrimitivesIndexedIndirect";
        const byte DrawSetupOp = 22, DrawIndicesOp = 28;

        readonly List<GraphicsBuffer> m_Buffers = new List<GraphicsBuffer>();
        readonly List<string> m_Warnings = new List<string>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;
        static int s_Made;

        public GraphicsRenderPrimitivesIndexedTests()
        {
            Immediate.Queued.Clear();
            m_PreviousHandler = m_Logger.logHandler;
            m_PreviousLogEnabled = m_Logger.logEnabled;
            m_PreviousFilter = m_Logger.filterLogType;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Warnings);
            m_Logger.logEnabled = true;
            m_Logger.filterLogType = LogType.Log;
        }

        public void Dispose()
        {
            try
            {
                Immediate.Queued.Clear();
                foreach (var b in m_Buffers) b.Dispose();
            }
            finally
            {
                m_Logger.logHandler = m_PreviousHandler;
                m_Logger.logEnabled = m_PreviousLogEnabled;
                m_Logger.filterLogType = m_PreviousFilter;
            }
        }

        /// <summary>Forwards every log call, keeping the warnings' text.</summary>
        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Warnings;

            internal CaptureHandler(ILogHandler inner, List<string> warnings)
            {
                m_Inner = inner;
                m_Warnings = warnings;
            }

            public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args)
            {
                if (type == LogType.Warning)
                    m_Warnings.Add(args == null || args.Length == 0 ? format
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, UnityEngine.Object context) => m_Inner.LogException(exception, context);
        }

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        int Warned(string text) => m_Warnings.Count(w => w.Contains(text, StringComparison.Ordinal));

        Material NewMaterial()
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/RenderPrimitivesIndexed/S" + (++s_Made) + "\" { SubShader { Pass { } } }");
            return new Material(shader);
        }

        GraphicsBuffer NewBuffer(GraphicsBuffer.Target target, int count, int stride)
        {
            var buffer = new GraphicsBuffer(target, count, stride);
            m_Buffers.Add(buffer);
            return buffer;
        }

        static Immediate.QueuedMesh[] TakeQueued()
        {
            var queued = Immediate.Queued.ToArray();
            Immediate.Queued.Clear();
            return queued;
        }

        /// <summary>The commands a draw setup records.</summary>
        static byte[] Encoded(DrawSetup setup)
        {
            GfxStream.Flush();
            GfxStream.DrawSetup(setup);
            var bytes = GfxStream.Recorded.ToArray();
            GfxStream.Flush();
            return bytes;
        }

        /// <summary>The bytes <paramref name="call"/> records (nothing for a refused call).</summary>
        static int RecordedBy(Action call)
        {
            GfxStream.Flush();
            call();
            int length = GfxStream.Recorded.Length;
            GfxStream.Flush();
            return length;
        }

        static byte[] SetupBytes(uint instances, ulong args, uint offset, bool indexed)
        {
            var bytes = new List<byte> { DrawSetupOp };
            bytes.AddRange(BitConverter.GetBytes(instances));
            bytes.AddRange(BitConverter.GetBytes(args));
            bytes.AddRange(BitConverter.GetBytes(offset));
            bytes.Add(indexed ? (byte)1 : (byte)0);
            return bytes.ToArray();
        }

        /// <summary>Render protocol 8's DrawIndices: buffer, index size 2, first index, count.</summary>
        static byte[] IndicesBytes(ulong buffer, uint first, uint count)
        {
            var bytes = new List<byte> { DrawIndicesOp };
            bytes.AddRange(BitConverter.GetBytes(buffer));
            bytes.Add(2);
            bytes.AddRange(BitConverter.GetBytes(first));
            bytes.AddRange(BitConverter.GetBytes(count));
            return bytes.ToArray();
        }

        // ---------------------------------------------------------------- 16-bit indices: drawn

        [Fact]
        public void SixteenBitIndicesQueueOneDrawOfTheStandInThatReadsTheBuffersIndices()
        {
            var index = NewBuffer(GraphicsBuffer.Target.Index, 6, 2);
            var rp = new RenderParams(NewMaterial()) { layer = 3 };
            int warned = Hits(Indexed);
            Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, index, 3, 2, 4);
            var q = Assert.Single(TakeQueued());
            Assert.Same(ProceduralMeshes.Get(MeshTopology.Triangles, ProceduralMeshes.IndirectVertices), q.Mesh);
            Assert.Equal(65536, q.Mesh.vertexCount);
            Assert.Same(rp.material, q.Material);
            Assert.Equal(3, q.Layer);
            Assert.Equal(0, q.Submesh);
            Assert.Equal(Matrix4x4.identity, q.Matrix);
            var setup = Assert.IsType<DrawSetup>(q.Setup);
            Assert.Equal(4u, setup.Instances);
            Assert.Equal(0ul, setup.Args);
            Assert.False(setup.Indexed);
            // The setup, then indices 2 to 4 of the buffer for the next draw.
            Assert.Equal(SetupBytes(4, 0, 0, false).Concat(IndicesBytes(index.Handle, 2, 3)).ToArray(), Encoded(setup));
            Assert.Equal(warned, Hits(Indexed));
        }

        [Fact]
        public void OneInstanceFromTheFirstIndexStillCarriesTheIndices()
        {
            var index = NewBuffer(GraphicsBuffer.Target.Index, 6, 2);
            Graphics.RenderPrimitivesIndexed(new RenderParams(NewMaterial()), MeshTopology.Triangles, index, 6);
            var setup = Assert.IsType<DrawSetup>(Assert.Single(TakeQueued()).Setup);
            Assert.Equal(SetupBytes(1, 0, 0, false).Concat(IndicesBytes(index.Handle, 0, 6)).ToArray(), Encoded(setup));
        }

        [Fact]
        public void LinesAndPointsDrawTheirOwnStandIns()
        {
            var index = NewBuffer(GraphicsBuffer.Target.Index, 4, 2);
            var rp = new RenderParams(NewMaterial());
            Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Lines, index, 4);
            Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Points, index, 1, 3);
            var queued = TakeQueued();
            Assert.Equal(2, queued.Length);
            Assert.Same(ProceduralMeshes.Get(MeshTopology.Lines, ProceduralMeshes.IndirectVertices), queued[0].Mesh);
            Assert.Same(ProceduralMeshes.Get(MeshTopology.Points, ProceduralMeshes.IndirectVertices), queued[1].Mesh);
            Assert.Equal(IndicesBytes(index.Handle, 3, 1), Encoded(queued[1].Setup.Value).Skip(18).ToArray());
        }

        // ---------------------------------------------------------------- argument checks

        [Fact]
        public void NullArgumentsAndIndexRangesOutsideTheBufferThrow()
        {
            var none = new RenderParams(null);
            var rp = new RenderParams(NewMaterial());
            var index16 = NewBuffer(GraphicsBuffer.Target.Index, 6, 2);
            var index32 = NewBuffer(GraphicsBuffer.Target.Structured, 6, 4);
            Assert.Equal("rparams.material", Assert.Throws<ArgumentNullException>(() => Graphics.RenderPrimitivesIndexed(none, MeshTopology.Triangles, index16, 3)).ParamName);
            Assert.Equal("indexBuffer", Assert.Throws<ArgumentNullException>(() => Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, null, 3)).ParamName);
            Assert.Equal("startIndex", Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, index16, 3, -1)).ParamName);
            Assert.Equal("indexCount", Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, index16, 3, 4)).ParamName);
            Assert.Equal("indexCount", Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, index32, 7)).ParamName);
            Assert.Empty(TakeQueued());
            // The last indices that fit are drawn; no indices or instances draw nothing.
            Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, index16, 3, 3);
            Assert.Single(TakeQueued());
            int warned = Hits(Indexed);
            Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, index16, 0);
            Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, index16, 3, 0, 0);
            Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, index32, 0);
            Assert.Empty(TakeQueued());
            Assert.Equal(warned, Hits(Indexed));
        }

        [Fact]
        public void IndirectArgumentsAreCheckedAsRenderPrimitivesIndirectChecksThem()
        {
            var none = new RenderParams(null);
            var rp = new RenderParams(NewMaterial());
            var index = NewBuffer(GraphicsBuffer.Target.Index, 6, 2);
            var args = NewBuffer(GraphicsBuffer.Target.IndirectArguments, 2, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            var structured = NewBuffer(GraphicsBuffer.Target.Structured, 2, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            Assert.Equal("rparams.material", Assert.Throws<ArgumentNullException>(() => Graphics.RenderPrimitivesIndexedIndirect(none, MeshTopology.Triangles, index, args)).ParamName);
            Assert.Equal("indexBuffer", Assert.Throws<ArgumentNullException>(() => Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, null, args)).ParamName);
            Assert.Equal("commandBuffer", Assert.Throws<ArgumentNullException>(() => Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, index, null)).ParamName);
            Assert.Equal("commandBuffer", Assert.Throws<ArgumentException>(() => Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, index, structured)).ParamName);
            Assert.Equal("commandCount", Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, index, args, -1)).ParamName);
            Assert.Equal("startCommand", Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, index, args, 1, -1)).ParamName);
            Assert.Equal("commandCount", Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, index, args, 2, 1)).ParamName);
            Assert.Empty(TakeQueued());
        }

        // ---------------------------------------------------------------- refused loudly

        [Fact]
        public void ThirtyTwoBitIndicesAreCountedWarnedOnceAndNothingIsQueuedOrRecorded()
        {
            // The documented example's index buffer: Structured, sizeof(int).
            var structured = NewBuffer(GraphicsBuffer.Target.Structured, 6, 4);
            var index = NewBuffer(GraphicsBuffer.Target.Index, 6, 4);
            var rp = new RenderParams(NewMaterial());
            const string text = Indexed + " with 32-bit indices is not drawn";
            int hits = Hits(Indexed);
            bool first = hits == 0;
            Assert.Equal(0, RecordedBy(() => Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, structured, 6)));
            Assert.Equal(0, RecordedBy(() => Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, index, 3, 3, 2)));
            Assert.Empty(TakeQueued());
            Assert.Equal(hits + 2, Hits(Indexed));
            Assert.Equal(first ? 1 : 0, Warned(text));
        }

        [Fact]
        public void IndirectCallsAreCountedWarnedOnceAndNothingIsQueuedOrRecorded()
        {
            var index = NewBuffer(GraphicsBuffer.Target.Index, 6, 2);
            var args = NewBuffer(GraphicsBuffer.Target.IndirectArguments, 3, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            var rp = new RenderParams(NewMaterial());
            const string text = Indirect + " is not drawn";
            int hits = Hits(Indirect);
            bool first = hits == 0;
            Assert.Equal(0, RecordedBy(() => Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, index, args)));
            Assert.Equal(0, RecordedBy(() => Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Points, index, args, 2, 1)));
            Assert.Empty(TakeQueued());
            Assert.Equal(hits + 2, Hits(Indirect));
            Assert.Equal(first ? 1 : 0, Warned(text));
            // No commands: nothing to refuse.
            Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, index, args, 0);
            Assert.Equal(hits + 2, Hits(Indirect));
        }

        [Fact]
        public void QuadsLineStripsAndOtherStridesAreCountedAndNothingIsQueued()
        {
            var index = NewBuffer(GraphicsBuffer.Target.Index, 8, 2);
            var wide = NewBuffer(GraphicsBuffer.Target.Structured, 8, 8);
            var rp = new RenderParams(NewMaterial());
            string quads = Indexed + " MeshTopology.Quads", strips = Indexed + " MeshTopology.LineStrip", stride = Indexed + " index stride";
            int q0 = Hits(quads), s0 = Hits(strips), w0 = Hits(stride), all = Hits(Indexed);
            Assert.Equal(0, RecordedBy(() => Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Quads, index, 8)));
            Assert.Equal(0, RecordedBy(() => Graphics.RenderPrimitivesIndexed(rp, MeshTopology.LineStrip, index, 4)));
            Assert.Equal(0, RecordedBy(() => Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, wide, 3)));
            Assert.Empty(TakeQueued());
            Assert.Equal((q0 + 1, s0 + 1, w0 + 1, all), (Hits(quads), Hits(strips), Hits(stride), Hits(Indexed)));
        }

        // ---------------------------------------------------------------- guards (green before and after)

        [Fact]
        public void NonIndexedDrawSetupsRecordNoIndices()
        {
            Assert.Equal(SetupBytes(4, 0, 0, false), Encoded(new DrawSetup { Instances = 4 }));
            Assert.Equal(SetupBytes(1, 9, 40, true), Encoded(new DrawSetup { Instances = 1, Args = 9, Offset = 40, Indexed = true }));
            var rp = new RenderParams(NewMaterial());
            var args = NewBuffer(GraphicsBuffer.Target.IndirectArguments, 2, GraphicsBuffer.IndirectDrawArgs.size);
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, 3);
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, args, 1, 1);
            var queued = TakeQueued();
            Assert.Equal(2, queued.Length);
            Assert.Same(ProceduralMeshes.Get(MeshTopology.Triangles, 6), queued[0].Mesh);
            Assert.Equal(SetupBytes(3, 0, 0, false), Encoded(queued[0].Setup.Value));
            Assert.Equal(SetupBytes(1, args.Handle, 16, false), Encoded(queued[1].Setup.Value));
        }
    }
}
