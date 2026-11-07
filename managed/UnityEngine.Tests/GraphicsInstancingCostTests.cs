using System;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>An instanced call's cost is linear in its instances: the block is copied once, every
    /// instance shares it, and the stream carries each array once per hand-over.</summary>
    public class GraphicsInstancingCostTests : IDisposable
    {
        const int Count = 1023;
        readonly Shader m_Shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/InstancingCost\" { SubShader { Pass {} } }");
        readonly Mesh m_Mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        readonly Material m_Material;
        readonly Matrix4x4[] m_Matrices = Enumerable.Range(0, Count).Select(i => Matrix4x4.Translate(Vector3.right * i)).ToArray();
        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();

        public GraphicsInstancingCostTests()
        {
            m_Material = new Material(m_Shader) { enableInstancing = true };
            m_Block.SetVectorArray("_Tint", Enumerable.Range(0, Count).Select(i => new Vector4(i, 0, 0, 1)).ToArray());
            m_Block.SetFloatArray("_Weight", Enumerable.Repeat(1f, Count).ToArray());
            m_Block.SetFloat("_Mode", 1);
            Immediate.Queued.Clear();
            GfxStream.Flush();
        }

        public void Dispose()
        {
            Immediate.Queued.Clear();
            GfxStream.Flush();
            UnityEngine.Object.DestroyImmediate(m_Mesh);
            UnityEngine.Object.DestroyImmediate(m_Material);
            UnityEngine.Object.DestroyImmediate(m_Shader);
        }

        void Queue(bool render)
        {
            if (render) Graphics.RenderMeshInstanced(new RenderParams(m_Material) { matProps = m_Block }, m_Mesh, 0, m_Matrices);
            else Graphics.DrawMeshInstanced(m_Mesh, 0, m_Material, m_Matrices, Count, m_Block);
        }

        /// <summary>Bytes of one Vector4[1023] and one float[1023] array.</summary>
        const long ArrayBytes = Count * 4 * 4 + Count * 4;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void QueuingCopiesTheBlockOnceForAllInstances(bool render)
        {
            Queue(render); // JIT and the queue's capacity
            Immediate.Queued.Clear();
            long before = GC.GetAllocatedBytesForCurrentThread();
            Queue(render);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(Count, Immediate.Queued.Count);
            var shared = Immediate.Queued[0].Properties;
            Assert.All(Immediate.Queued, q => Assert.Same(shared, q.Properties));
            Assert.Equal(Enumerable.Range(0, Count).Select(i => (uint?)i), Immediate.Queued.Select(q => q.Setup.Value.MeshInstance));
            // One copy of each array plus a small constant; a per-instance copy would be ~20 MB.
            Assert.True(allocated < ArrayBytes + 32 * 1024, $"{allocated} bytes allocated");
        }

        [Fact]
        public void TheStreamCarriesEachArrayOncePerHandOver()
        {
            Queue(false);
            var draws = Immediate.Queued.ToArray();
            Immediate.Queued.Clear();
            GfxStream.Flush();
            foreach (var q in draws)
            {
                GfxStream.DrawSetup(q.Setup.Value);
                GfxStream.Draw(1, 0, 2, 0, q.Matrix, Color.white, properties: q.Properties);
            }
            int all = GfxStream.Recorded.Length;
            GfxStream.Flush();
            // Each draw's own bytes (setup, matrix, names, references) are a few hundred.
            Assert.True(all < ArrayBytes + Count * 512, $"{all} bytes for {Count} draws");
            Assert.True(all > ArrayBytes, "the arrays are in the stream");
            // After a hand-over the next draw defines them again.
            GfxStream.DrawSetup(draws[5].Setup.Value);
            GfxStream.Draw(1, 0, 2, 0, draws[5].Matrix, Color.white, properties: draws[5].Properties);
            Assert.True(GfxStream.Recorded.Length > ArrayBytes);
            GfxStream.Flush();
        }

        [Fact]
        public void IndirectCommandsShareOneCopyOfTheBlock()
        {
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 64, GraphicsBuffer.IndirectDrawArgs.size);
            var rp = new RenderParams(m_Material) { matProps = m_Block };
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, args, 64);
            m_Block.SetFloat("_Mode", 2);
            Assert.Equal(64, Immediate.Queued.Count);
            var shared = Immediate.Queued[0].Properties;
            Assert.All(Immediate.Queued, q => Assert.Same(shared, q.Properties));
            Assert.Equal(1f, shared[Shader.PropertyToID("_Mode")]);
            Assert.Equal(Enumerable.Range(0, 64).Select(i => (int?)i), Immediate.Queued.Select(q => q.Setup.Value.Command));
        }
    }
}
