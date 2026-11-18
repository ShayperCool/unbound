using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Procedural quads without a batch (tests/projects/brg procedural_graphics): direct
    /// draws make a quad of each four ids; non-indexed indirect ones are refused with the Editor's
    /// error when drawn.</summary>
    public sealed class ProceduralQuadsTests : IDisposable
    {
        readonly Shader m_Shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/ProceduralQuads\" { SubShader { Pass { } } }");
        readonly Material m_Material;
        readonly GraphicsBuffer m_Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawArgs.size);
        readonly List<string> m_Errors = new List<string>();

        public ProceduralQuadsTests()
        {
            Immediate.Queued.Clear();
            m_Material = new Material(m_Shader);
            Application.logMessageReceivedThreaded += Capture;
        }

        public void Dispose()
        {
            Application.logMessageReceivedThreaded -= Capture;
            Immediate.Queued.Clear();
            m_Args.Dispose();
            Object.DestroyImmediate(m_Material);
            Object.DestroyImmediate(m_Shader);
        }

        void Capture(string message, string stack, LogType type)
        {
            if (type == LogType.Error) m_Errors.Add(message);
        }

        Immediate.QueuedMesh Last => Immediate.Queued[Immediate.Queued.Count - 1];

        [Fact]
        public void IndirectQuadsAloneAreRefused()
        {
            var rp = new RenderParams(m_Material);
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Quads, m_Args);
            Assert.True(ProceduralMeshes.IsIndirectQuads(Last));
            Graphics.DrawProceduralIndirect(m_Material, new Bounds(Vector3.zero, Vector3.one), MeshTopology.Quads, m_Args);
            Assert.True(ProceduralMeshes.IsIndirectQuads(Last));
            // Controls: indirect triangles, direct quads.
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, m_Args);
            Assert.False(ProceduralMeshes.IsIndirectQuads(Last));
            Graphics.RenderPrimitives(rp, MeshTopology.Quads, 8);
            Assert.False(ProceduralMeshes.IsIndirectQuads(Last));
            // Queuing logs nothing: the error comes when a camera draws it.
            Assert.Empty(m_Errors);
        }

        [Fact]
        public void DirectQuadsDrawAQuadOfEachFourIds()
        {
            var mesh = ProceduralMeshes.Get(MeshTopology.Quads, 8);
            Assert.Equal(MeshTopology.Quads, mesh.GetTopology(0));
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, mesh.GetIndices(0));
        }

        [Fact]
        public void AnIndirectQuadsDrawNowLogsTheEditorsError()
        {
            Graphics.DrawProceduralIndirectNow(MeshTopology.Quads, m_Args);
            Assert.Equal(new[] { ProceduralMeshes.IndirectQuadsError }, m_Errors);
            Assert.Equal("DrawProceduralIndirect can't draw quads if platform does not support quad topology", ProceduralMeshes.IndirectQuadsError);
        }
    }
}
