using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>
    /// Graphics.RenderPrimitives, RenderMeshPrimitives, RenderPrimitivesIndirect and
    /// RenderMeshIndirect (Scripting API 6000.0): the draws they queue for this frame's cameras,
    /// the RenderParams settings carried onto them, the ones warned as not honoured, and the
    /// argument checks. Indirect draws read GraphicsBuffer.IndirectDrawArgs (16 bytes) or
    /// IndirectDrawIndexedArgs (20 bytes) per command, from startCommand, one draw per command.
    /// </summary>
    public class GraphicsRenderPrimitivesTests : IDisposable
    {
        readonly List<GraphicsBuffer> m_Buffers = new List<GraphicsBuffer>();
        static int s_Made;

        public GraphicsRenderPrimitivesTests() => Immediate.Queued.Clear();

        public void Dispose()
        {
            Immediate.Queued.Clear();
            foreach (var b in m_Buffers) b.Dispose();
        }

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        Material NewMaterial()
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/RenderPrimitives/S" + (++s_Made) + "\" { SubShader { Pass { } } }");
            return new Material(shader);
        }

        Mesh NewMesh(int submeshes = 1)
        {
            var mesh = new Mesh { vertices = new Vector3[6] };
            mesh.subMeshCount = submeshes;
            for (int s = 0; s < submeshes; s++) mesh.SetTriangles(new[] { 0, 1, 2 }, s);
            return mesh;
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

        // ---------------------------------------------------------------- RenderPrimitives

        [Fact]
        public void RenderPrimitivesQueuesAProceduralDrawWithItsVertexAndInstanceCounts()
        {
            var rp = new RenderParams(NewMaterial()) { layer = 3 };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, 4);
            var q = Assert.Single(TakeQueued());
            Assert.Same(ProceduralMeshes.Get(MeshTopology.Triangles, 6), q.Mesh);
            Assert.Equal(6, q.Mesh.vertexCount);
            Assert.Same(rp.material, q.Material);
            Assert.Equal(3, q.Layer);
            Assert.Null(q.Camera);
            Assert.Equal(0, q.Submesh);
            Assert.Equal(Matrix4x4.identity, q.Matrix);
            var setup = Assert.IsType<DrawSetup>(q.Setup);
            Assert.Equal(4u, setup.Instances);
            Assert.Equal(0ul, setup.Args);
        }

        [Fact]
        public void RenderPrimitivesOfOneInstanceIsAPlainDrawAndEmptyCountsDrawNothing()
        {
            var rp = new RenderParams(NewMaterial());
            Graphics.RenderPrimitives(rp, MeshTopology.Lines, 4);
            var q = Assert.Single(TakeQueued());
            Assert.Same(ProceduralMeshes.Get(MeshTopology.Lines, 4), q.Mesh);
            Assert.Null(q.Setup);
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 0, 3);
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 3, 0);
            Assert.Empty(TakeQueued());
        }

        // ---------------------------------------------------------------- RenderMeshPrimitives

        [Fact]
        public void RenderMeshPrimitivesQueuesTheSubmeshWithItsInstanceCount()
        {
            var mesh = NewMesh(2);
            var rp = new RenderParams(NewMaterial());
            Graphics.RenderMeshPrimitives(rp, mesh, 1, 7);
            var q = Assert.Single(TakeQueued());
            Assert.Same(mesh, q.Mesh);
            Assert.Equal(1, q.Submesh);
            Assert.Equal(Matrix4x4.identity, q.Matrix);
            var setup = Assert.IsType<DrawSetup>(q.Setup);
            Assert.Equal(7u, setup.Instances);
            Assert.Equal(0ul, setup.Args);
            Graphics.RenderMeshPrimitives(rp, mesh, 0, 0);
            Assert.Empty(TakeQueued());
        }

        // ---------------------------------------------------------------- indirect

        [Fact]
        public void RenderPrimitivesIndirectDrawsEachCommandFromStartCommand()
        {
            var args = NewBuffer(GraphicsBuffer.Target.IndirectArguments, 5, GraphicsBuffer.IndirectDrawArgs.size);
            var rp = new RenderParams(NewMaterial());
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, args, 3, 2);
            var queued = TakeQueued();
            Assert.Equal(3, queued.Length);
            int argsId = Shader.PropertyToID("unity_IndirectDrawArgs"), commandId = Shader.PropertyToID("unity_BaseCommandID");
            for (int i = 0; i < 3; i++)
            {
                var q = queued[i];
                Assert.Same(ProceduralMeshes.Get(MeshTopology.Triangles, ProceduralMeshes.IndirectVertices), q.Mesh);
                var setup = Assert.IsType<DrawSetup>(q.Setup);
                Assert.Equal(args.Handle, setup.Args);
                Assert.Equal((uint)((2 + i) * 16), setup.Offset);
                Assert.False(setup.Indexed);
                Assert.Equal(1u, setup.Instances);
                Assert.Same(args, q.Properties[argsId]);
                Assert.Equal(2 + i, q.Properties[commandId]);
            }
        }

        [Fact]
        public void RenderPrimitivesIndirectDefaultsToTheFirstCommandAndZeroCommandsDrawNothing()
        {
            var args = NewBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawArgs.size);
            var rp = new RenderParams(NewMaterial());
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Points, args);
            var setup = Assert.IsType<DrawSetup>(Assert.Single(TakeQueued()).Setup);
            Assert.Equal(0u, setup.Offset);
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Points, args, 0);
            Assert.Empty(TakeQueued());
        }

        [Fact]
        public void RenderMeshIndirectDrawsTheMeshIndexedPerCommand()
        {
            var mesh = NewMesh();
            var args = NewBuffer(GraphicsBuffer.Target.IndirectArguments, 2, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            var rp = new RenderParams(NewMaterial());
            Graphics.RenderMeshIndirect(rp, mesh, args, 2);
            var queued = TakeQueued();
            Assert.Equal(2, queued.Length);
            for (int i = 0; i < 2; i++)
            {
                Assert.Same(mesh, queued[i].Mesh);
                Assert.Equal(0, queued[i].Submesh);
                var setup = Assert.IsType<DrawSetup>(queued[i].Setup);
                Assert.Equal(args.Handle, setup.Args);
                Assert.Equal((uint)(i * 20), setup.Offset);
                Assert.True(setup.Indexed);
            }
        }

        // ---------------------------------------------------------------- RenderParams

        [Fact]
        public void HonouredRenderParamsReachEveryDrawAndTheCallersBlockIsUnchanged()
        {
            var block = new MaterialPropertyBlock();
            block.SetFloat("_Tint", 0.25f);
            var rp = new RenderParams(NewMaterial())
            {
                layer = 5,
                matProps = block,
                shadowCastingMode = ShadowCastingMode.TwoSided,
                receiveShadows = true,
                lightProbeUsage = LightProbeUsage.BlendProbes,
                reflectionProbeUsage = ReflectionProbeUsage.Simple,
            };
            var args = NewBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 3);
            Graphics.RenderMeshPrimitives(rp, NewMesh(), 0, 2);
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, args);
            Graphics.RenderMeshIndirect(rp, NewMesh(), args);
            var queued = TakeQueued();
            Assert.Equal(4, queued.Length);
            int tint = Shader.PropertyToID("_Tint");
            foreach (var q in queued)
            {
                Assert.Equal(5, q.Layer);
                Assert.Equal(0.25f, q.Properties[tint]);
                Assert.Equal(ShadowCastingMode.TwoSided, q.Lighting.Cast);
                Assert.True(q.Lighting.Receive);
                Assert.Equal(LightProbeUsage.BlendProbes, q.Lighting.Probes);
                Assert.Equal(ReflectionProbeUsage.Simple, q.Lighting.Reflections);
            }
            Assert.Single(block.m_Values);
        }

        [Fact]
        public void UnhonouredRenderParamsAreCountedAndWarnedOncePerSetting()
        {
            const string bounds = "Graphics.RenderPrimitives RenderParams.worldBounds";
            const string mask = "Graphics.Render* RenderParams.renderingLayerMask";
            const string priority = "Graphics.Render* RenderParams.rendererPriority";
            const string motion = "Graphics.Render* RenderParams.motionVectorMode";
            var plain = new RenderParams(NewMaterial()) { worldBounds = new Bounds(Vector3.zero, Vector3.one * 10f) };
            int b0 = Hits(bounds), m0 = Hits(mask), p0 = Hits(priority), v0 = Hits(motion);
            Graphics.RenderPrimitives(plain, MeshTopology.Triangles, 3);
            Assert.Equal(b0, Hits(bounds));
            Assert.Equal((m0, p0, v0), (Hits(mask), Hits(priority), Hits(motion)));
            var set = new RenderParams(NewMaterial())
            {
                renderingLayerMask = 4u,
                rendererPriority = 2,
                motionVectorMode = MotionVectorGenerationMode.Object,
            };
            Graphics.RenderPrimitives(set, MeshTopology.Triangles, 3);
            Assert.Equal(b0, Hits(bounds));
            Assert.Equal((m0 + 1, p0 + 1, v0 + 1), (Hits(mask), Hits(priority), Hits(motion)));
            Assert.Equal(2, TakeQueued().Length);
        }

        // ---------------------------------------------------------------- argument checks

        [Fact]
        public void ANullMaterialMeshOrCommandBufferThrows()
        {
            var none = new RenderParams(null);
            var mesh = NewMesh();
            var args = NewBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            Assert.Equal("rparams.material", Assert.Throws<ArgumentNullException>(() => Graphics.RenderPrimitives(none, MeshTopology.Triangles, 3)).ParamName);
            Assert.Equal("rparams.material", Assert.Throws<ArgumentNullException>(() => Graphics.RenderMeshPrimitives(none, mesh, 0)).ParamName);
            Assert.Equal("rparams.material", Assert.Throws<ArgumentNullException>(() => Graphics.RenderPrimitivesIndirect(none, MeshTopology.Triangles, args)).ParamName);
            Assert.Equal("rparams.material", Assert.Throws<ArgumentNullException>(() => Graphics.RenderMeshIndirect(none, mesh, args)).ParamName);
            var rp = new RenderParams(NewMaterial());
            Assert.Equal("mesh", Assert.Throws<ArgumentNullException>(() => Graphics.RenderMeshPrimitives(rp, null, 0)).ParamName);
            Assert.Equal("mesh", Assert.Throws<ArgumentNullException>(() => Graphics.RenderMeshIndirect(rp, null, args)).ParamName);
            Assert.Equal("commandBuffer", Assert.Throws<ArgumentNullException>(() => Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, null)).ParamName);
            Assert.Equal("commandBuffer", Assert.Throws<ArgumentNullException>(() => Graphics.RenderMeshIndirect(rp, mesh, null)).ParamName);
            Assert.Empty(TakeQueued());
        }

        [Fact]
        public void ACommandBufferWithoutTheIndirectArgumentsTargetThrows()
        {
            var structured = NewBuffer(GraphicsBuffer.Target.Structured, 4, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            var rp = new RenderParams(NewMaterial());
            Assert.Equal("commandBuffer", Assert.Throws<ArgumentException>(() => Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, structured)).ParamName);
            Assert.Equal("commandBuffer", Assert.Throws<ArgumentException>(() => Graphics.RenderMeshIndirect(rp, NewMesh(), structured)).ParamName);
            Assert.Empty(TakeQueued());
        }

        [Fact]
        public void NegativeOrOutOfBufferCommandRangesThrow()
        {
            var args = NewBuffer(GraphicsBuffer.Target.IndirectArguments, 3, GraphicsBuffer.IndirectDrawArgs.size);
            var indexedArgs = NewBuffer(GraphicsBuffer.Target.IndirectArguments, 2, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            var rp = new RenderParams(NewMaterial());
            var mesh = NewMesh();
            Assert.Equal("commandCount", Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, args, -1)).ParamName);
            Assert.Equal("startCommand", Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, args, 1, -1)).ParamName);
            Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, args, 2, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.RenderMeshIndirect(rp, mesh, indexedArgs, 3));
            Assert.Empty(TakeQueued());
            // The last commands that fit are drawn.
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, args, 1, 2);
            Graphics.RenderMeshIndirect(rp, mesh, indexedArgs, 1, 1);
            Assert.Equal(new uint[] { 32, 20 }, TakeQueued().Select(q => q.Setup.Value.Offset).ToArray());
        }

        // ---------------------------------------------------------------- guards (green before and after)

        [Fact]
        public void RenderMeshStillQueuesOnePlainDraw()
        {
            var mesh = NewMesh();
            var rp = new RenderParams(NewMaterial()) { layer = 2 };
            var matrix = Matrix4x4.Translate(new Vector3(1f, 2f, 3f));
            Graphics.RenderMesh(rp, mesh, 0, matrix);
            var q = Assert.Single(TakeQueued());
            Assert.Same(mesh, q.Mesh);
            Assert.Equal(matrix, q.Matrix);
            Assert.Equal(2, q.Layer);
            Assert.Null(q.Setup);
        }

        [Fact]
        public void TheIndexedVariantsStayLoudStubs()
        {
            var index = NewBuffer(GraphicsBuffer.Target.Index, 3, 4);
            var args = NewBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            var rp = new RenderParams(NewMaterial());
            int indexed = Hits("UnityEngine.Graphics.RenderPrimitivesIndexed"), indirect = Hits("UnityEngine.Graphics.RenderPrimitivesIndexedIndirect");
            Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, index, 3);
            Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, index, args);
            Assert.Equal(indexed + 1, Hits("UnityEngine.Graphics.RenderPrimitivesIndexed"));
            Assert.Equal(indirect + 1, Hits("UnityEngine.Graphics.RenderPrimitivesIndexedIndirect"));
            Assert.Empty(TakeQueued());
        }
    }
}
