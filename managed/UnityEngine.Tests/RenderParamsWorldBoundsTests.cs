using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>
    /// RenderParams.worldBounds on the RenderParams draws (Scripting API 6000.0: "Used to cull and
    /// sort the rendered geometry"; the indirect pages: "to cull and sort the geometry rendered
    /// with the method as a single entity"): the draws a camera collects from the queue, culled
    /// by set world bounds with the renderers' frustum test and sorted by their centre. Bounds
    /// left at the constructor's zero value are not set (Unbound's choice): the draw is never
    /// culled and sorts by its mesh's bounds, like Graphics.DrawMesh, which stays as it was.
    /// The camera sits at the origin looking along +Z (60°, 0.3 to 100).
    /// </summary>
    public class RenderParamsWorldBoundsTests : IDisposable
    {
        static readonly MethodInfo s_CollectQueued = typeof(FrameBuilder).GetMethod("CollectQueued", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo s_Casters = typeof(FrameBuilder).GetField("s_Queued", BindingFlags.Static | BindingFlags.NonPublic);
        static int s_Made;

        readonly List<UnityEngine.Object> m_Objects = new List<UnityEngine.Object>();
        readonly List<GraphicsBuffer> m_Buffers = new List<GraphicsBuffer>();
        readonly Camera m_Camera;

        public RenderParamsWorldBoundsTests()
        {
            Immediate.Queued.Clear();
            var go = new GameObject("Camera");
            m_Objects.Add(go);
            m_Camera = go.AddComponent<Camera>();
            m_Camera.transform.position = Vector3.zero;
            m_Camera.fieldOfView = 60;
            m_Camera.nearClipPlane = 0.3f;
            m_Camera.farClipPlane = 100;
            m_Camera.aspect = 16f / 9f;
        }

        public void Dispose()
        {
            Immediate.Queued.Clear();
            foreach (var b in m_Buffers) b.Dispose();
            foreach (var o in m_Objects) UnityEngine.Object.DestroyImmediate(o);
        }

        Material NewMaterial(int queue = -1)
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/RenderParamsWorldBounds/S" + (++s_Made) + "\" { SubShader { Pass { } } }");
            var material = new Material(shader);
            if (queue >= 0) material.renderQueue = queue;
            return material;
        }

        /// <summary>A unit-bounds triangle mesh around its origin.</summary>
        static Mesh NewMesh()
        {
            var mesh = new Mesh { vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f) } };
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            return mesh;
        }

        GraphicsBuffer NewBuffer(int count, int stride)
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, count, stride);
            m_Buffers.Add(buffer);
            return buffer;
        }

        static Bounds Box(float z) => new Bounds(new Vector3(0f, 0f, z), Vector3.one * 2f);

        static Matrix4x4 At(float z) => Matrix4x4.Translate(new Vector3(0f, 0f, z));

        /// <summary>The draws the camera collects from the queue (squared distances: perspective).</summary>
        List<FrameBuilder.DrawItem> Collect()
        {
            var context = FrameBuilder.ContextOf(m_Camera);
            context.Metric = DistanceMetric.Perspective;
            var into = new List<FrameBuilder.DrawItem>();
            s_CollectQueued.Invoke(null, new object[] { context, into });
            return into;
        }

        // ---------------------------------------------------------------- culling

        [Fact]
        public void ARenderMeshWhoseWorldBoundsAreOutsideTheFrustumIsNotCollected()
        {
            var mesh = NewMesh();
            // Its mesh is in view; its world bounds are behind the camera.
            var behind = new RenderParams(NewMaterial()) { worldBounds = Box(-20f) };
            Graphics.RenderMesh(behind, mesh, 0, At(10f));
            Assert.Empty(Collect());
            Immediate.Queued.Clear();
            // In view: drawn, wherever its mesh is.
            var seen = new RenderParams(NewMaterial()) { worldBounds = Box(10f) };
            Graphics.RenderMesh(seen, mesh, 0, At(-20f));
            Assert.Equal(At(-20f), Assert.Single(Collect()).Model);
        }

        [Fact]
        public void ACulledRenderMeshStillCastsShadowsAsOtherQueuedDraws()
        {
            var rp = new RenderParams(NewMaterial()) { worldBounds = Box(-20f), shadowCastingMode = ShadowCastingMode.On };
            Graphics.RenderMesh(rp, NewMesh(), 0, At(10f));
            Collect();
            var casters = (List<FrameBuilder.DrawItem>)s_Casters.GetValue(null);
            Assert.Equal(ShadowCastingMode.On, Assert.Single(casters).Lighting.Cast);
        }

        [Fact]
        public void EveryInstanceAndCommandOfOneCallIsCulledOrKeptTogether()
        {
            var args = NewBuffer(3, GraphicsBuffer.IndirectDrawArgs.size);
            var outside = new RenderParams(NewMaterial()) { worldBounds = new Bounds(new Vector3(500f, 0f, 10f), Vector3.one * 4f) };
            Graphics.RenderPrimitives(outside, MeshTopology.Triangles, 3, 8);
            Graphics.RenderMeshPrimitives(outside, NewMesh(), 0, 5);
            Graphics.RenderPrimitivesIndirect(outside, MeshTopology.Triangles, args, 3);
            Assert.Equal(5, Immediate.Queued.Count);
            Assert.Empty(Collect());
            Immediate.Queued.Clear();

            var inside = new RenderParams(NewMaterial()) { worldBounds = new Bounds(new Vector3(0f, 0f, 30f), Vector3.one * 4f) };
            Graphics.RenderPrimitives(inside, MeshTopology.Triangles, 3, 8);
            Graphics.RenderMeshPrimitives(inside, NewMesh(), 0, 5);
            Graphics.RenderPrimitivesIndirect(inside, MeshTopology.Triangles, args, 3);
            var collected = Collect();
            Assert.Equal(5, collected.Count);
            Assert.Equal(8u, collected[0].Setup.Value.Instances);
            Assert.Equal(5u, collected[1].Setup.Value.Instances);
            // One entity: every draw sorts at the bounds' centre.
            Assert.All(collected, item => Assert.Equal(900f, item.Distance));
        }

        // ---------------------------------------------------------------- sorting

        [Fact]
        public void TheSortDistanceIsTheWorldBoundsCentres()
        {
            var mesh = NewMesh();
            var material = NewMaterial(3000);
            // Near mesh, far bounds; far mesh, near bounds.
            Graphics.RenderMesh(new RenderParams(material) { worldBounds = Box(20f) }, mesh, 0, At(5f));
            Graphics.RenderMesh(new RenderParams(material) { worldBounds = Box(5f) }, mesh, 0, At(20f));
            var collected = Collect();
            Assert.Equal(new[] { 400f, 25f }, collected.Select(d => d.Distance).ToArray());
            // Transparent, back to front: the far bounds first.
            FrameBuilder.SortForCamera(collected);
            Assert.Equal(new[] { At(5f), At(20f) }, collected.Select(d => d.Model).ToArray());
        }

        // ---------------------------------------------------------------- unset bounds (Unbound's choice)

        [Fact]
        public void DefaultWorldBoundsNeverCullAndSortByTheMeshBounds()
        {
            // RenderParams(Material) leaves worldBounds zero: drawn even with its mesh behind the
            // camera (queued draws are not culled by their mesh), sorted at the mesh's centre.
            var rp = new RenderParams(NewMaterial());
            Assert.Equal(default(Bounds), rp.worldBounds);
            Graphics.RenderMesh(rp, NewMesh(), 0, At(-20f));
            var item = Assert.Single(Collect());
            Assert.Equal(400f, item.Distance);
            Assert.Equal(new Vector3(0f, 0f, -20f), item.Bounds.center);
        }

        [Fact]
        public void ZeroSizeWorldBoundsAtAPointAreNeverCulledAndSortAtThatPoint()
        {
            // Unbound's choice (unmeasured; the renderers' InFrustum rule): set bounds without size count as inside.
            var rp = new RenderParams(NewMaterial()) { worldBounds = new Bounds(new Vector3(0f, 0f, -30f), Vector3.zero) };
            Graphics.RenderMesh(rp, NewMesh(), 0, At(10f));
            Assert.Equal(900f, Assert.Single(Collect()).Distance);
        }

        // ---------------------------------------------------------------- guard (green before and after)

        [Fact]
        public void GraphicsDrawMeshIsNeitherCulledNorSortedDifferently()
        {
            var mesh = NewMesh();
            Graphics.DrawMesh(mesh, At(-20f), NewMaterial(), 0);
            Graphics.DrawMesh(mesh, At(10f), NewMaterial(), 0);
            var collected = Collect();
            Assert.Equal(new[] { At(-20f), At(10f) }, collected.Select(d => d.Model).ToArray());
            Assert.Equal(new[] { 400f, 100f }, collected.Select(d => d.Distance).ToArray());
            Assert.Equal(new[] { -20f, 10f }, collected.Select(d => d.Bounds.center.z).ToArray());
        }
    }
}
