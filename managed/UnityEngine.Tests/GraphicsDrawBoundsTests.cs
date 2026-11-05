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
    /// The bounds of the instanced and procedural draws (Scripting API 6000.0):
    /// Graphics.RenderMeshInstanced ("Unity automatically calculates bounds for all the instances
    /// of this Mesh unless you override the bounds using RenderParams.worldBounds. Unity uses the
    /// bounds to cull and sort all the instances of this Mesh as a single entity") and the
    /// <c>bounds</c> of DrawProcedural, DrawProceduralIndirect, DrawMeshInstancedIndirect and
    /// DrawMeshInstancedProcedural ("The bounding volume surrounding the instances you intend to
    /// draw"; the legacy draws' cull and sort are (inferred from DrawMeshInstanced's "culls and sorts instanced Meshes as a group" and the replacements' "cull and sort ... as a single entity"; unmeasured)): the draws a camera collects from the queue, culled by those bounds with the
    /// renderers' frustum test and sorted by their centre, the whole call as one entity. Bounds
    /// equal to default(Bounds) are not set (Unbound's choice, unmeasured): those draws are never
    /// culled and sort by their mesh's bounds, as before. Graphics.DrawMeshInstanced ("Unity culls
    /// and sorts instanced Meshes as a group ... does not further cull individual instances ...
    /// does not sort individual instances") and RenderMeshInstanced with unset worldBounds use the
    /// box around every instance's mesh bounds (Unbound's choice of the box and of the call order
    /// within it, unmeasured); each instance keeps its own lighting, probe and shadow data. The
    /// camera sits at the origin looking along +Z (60°, 0.3 to 100).
    /// </summary>
    public class GraphicsDrawBoundsTests : IDisposable
    {
        static readonly MethodInfo s_CollectQueued = typeof(FrameBuilder).GetMethod("CollectQueued", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo s_Casters = typeof(FrameBuilder).GetField("s_Queued", BindingFlags.Static | BindingFlags.NonPublic);
        static int s_Made;

        // Argument strides: five uints for indexed draws, four for non-indexed ones.
        const int IndexedArgsStride = 20, ArgsStride = 16;

        readonly List<UnityEngine.Object> m_Objects = new List<UnityEngine.Object>();
        readonly List<GraphicsBuffer> m_Buffers = new List<GraphicsBuffer>();
        readonly Camera m_Camera;

        public GraphicsDrawBoundsTests()
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

        Material NewMaterial(int queue = -1, bool instancing = false)
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/GraphicsDrawBounds/S" + (++s_Made) + "\" { SubShader { Pass { } } }");
            var material = new Material(shader) { enableInstancing = instancing };
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

        GraphicsBuffer NewArgs(int stride)
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, stride);
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

        // ---------------------------------------------------------------- RenderMeshInstanced

        [Fact]
        public void RenderMeshInstancedWithWorldBoundsOutsideTheFrustumCollectsNoInstance()
        {
            // Every instance is in view; the call's world bounds are behind the camera.
            var rp = new RenderParams(NewMaterial(instancing: true)) { worldBounds = Box(-20f) };
            Graphics.RenderMeshInstanced(rp, NewMesh(), 0, new[] { At(10f), At(12f), At(14f) });
            Assert.Equal(3, Immediate.Queued.Count);
            Assert.Empty(Collect());
        }

        [Fact]
        public void RenderMeshInstancedWithWorldBoundsInViewCollectsEveryInstanceSortedAtTheBoundsCentre()
        {
            // Every instance is behind the camera; the call's world bounds are in view.
            var rp = new RenderParams(NewMaterial(3000, instancing: true)) { worldBounds = Box(10f) };
            Graphics.RenderMeshInstanced(rp, NewMesh(), 0, new[] { At(-20f), At(-30f), At(-40f) });
            Graphics.DrawMesh(NewMesh(), At(15f), NewMaterial(3000), 0);
            var collected = Collect();
            Assert.Equal(new[] { 100f, 100f, 100f, 225f }, collected.Select(d => d.Distance).ToArray());
            // Transparent, back to front: the other draw first, then the instances as one
            // entity, in their call's order (Unbound's tie-break, unmeasured).
            FrameBuilder.SortForCamera(collected);
            Assert.Equal(new[] { At(15f), At(-20f), At(-30f), At(-40f) }, collected.Select(d => d.Model).ToArray());
        }

        [Fact]
        public void RenderMeshInstancedWithDefaultWorldBoundsSortsAllInstancesAsOneGroup()
        {
            // "Unity automatically calculates bounds for all the instances": the box around the
            // instances' bounds (z -30.5 to 10.5) is in view and its centre (z -10) is every
            // instance's sort point. Opaque, front to back: the instances keep the call's order.
            var rp = new RenderParams(NewMaterial(instancing: true));
            Assert.Equal(default(Bounds), rp.worldBounds);
            Graphics.RenderMeshInstanced(rp, NewMesh(), 0, new[] { At(-20f), At(10f), At(-30f) });
            var collected = Collect();
            Assert.Equal(new[] { 100f, 100f, 100f }, collected.Select(d => d.Distance).ToArray());
            // Each instance's own bounds (lighting and probes) are unchanged.
            Assert.Equal(new[] { -20f, 10f, -30f }, collected.Select(d => d.Bounds.center.z).ToArray());
            FrameBuilder.SortForCamera(collected);
            Assert.Equal(new[] { At(-20f), At(10f), At(-30f) }, collected.Select(d => d.Model).ToArray());
        }

        [Fact]
        public void RenderMeshInstancedWithDefaultWorldBoundsAndEveryInstanceOutsideTheFrustumCollectsNone()
        {
            var rp = new RenderParams(NewMaterial(instancing: true));
            Graphics.RenderMeshInstanced(rp, NewMesh(), 0, new[] { At(-20f), At(-30f) });
            Assert.Equal(2, Immediate.Queued.Count);
            Assert.Empty(Collect());
        }

        [Fact]
        public void RenderMeshInstancedWithSetWorldBoundsStillOverridesTheGroupBounds()
        {
            // Guard (green before and after): the instances' own box would sort at z -5 (25);
            // the set bounds at z 30 win.
            var rp = new RenderParams(NewMaterial(instancing: true)) { worldBounds = Box(30f) };
            Graphics.RenderMeshInstanced(rp, NewMesh(), 0, new[] { At(-20f), At(10f) });
            var collected = Collect();
            Assert.Equal(new[] { 900f, 900f }, collected.Select(d => d.Distance).ToArray());
            Assert.Equal(new[] { -20f, 10f }, collected.Select(d => d.Bounds.center.z).ToArray());
        }

        // ---------------------------------------------------------------- the legacy draws' bounds

        [Fact]
        public void DrawProceduralWithBoundsOutsideTheFrustumIsNotCollected()
        {
            Graphics.DrawProcedural(NewMaterial(), Box(-20f), MeshTopology.Triangles, 3);
            Assert.Single(Immediate.Queued);
            Assert.Empty(Collect());
            // Still a shadow caster, as other queued draws.
            Assert.Single((List<FrameBuilder.DrawItem>)s_Casters.GetValue(null));
            Immediate.Queued.Clear();
            Graphics.DrawProcedural(NewMaterial(), Box(10f), MeshTopology.Triangles, 3, 4);
            var item = Assert.Single(Collect());
            Assert.Equal(100f, item.Distance);
            Assert.Equal(4u, item.Setup.Value.Instances);
        }

        [Fact]
        public void DrawMeshInstancedIndirectWithBoundsOutsideTheFrustumIsNotCollected()
        {
            var args = NewArgs(IndexedArgsStride);
            Graphics.DrawMeshInstancedIndirect(NewMesh(), 0, NewMaterial(), Box(-20f), args);
            Assert.Single(Immediate.Queued);
            Assert.Empty(Collect());
            Immediate.Queued.Clear();
            Graphics.DrawMeshInstancedIndirect(NewMesh(), 0, NewMaterial(), Box(10f), args);
            var item = Assert.Single(Collect());
            Assert.Equal(100f, item.Distance);
            Assert.True(item.Setup.Value.Indexed);
        }

        [Fact]
        public void DrawProceduralIndirectWithBoundsOutsideTheFrustumIsNotCollected()
        {
            var args = NewArgs(ArgsStride);
            Graphics.DrawProceduralIndirect(NewMaterial(), Box(-20f), MeshTopology.Triangles, args);
            Assert.Single(Immediate.Queued);
            Assert.Empty(Collect());
            Immediate.Queued.Clear();
            Graphics.DrawProceduralIndirect(NewMaterial(), Box(10f), MeshTopology.Triangles, args);
            var item = Assert.Single(Collect());
            Assert.Equal(100f, item.Distance);
            Assert.False(item.Setup.Value.Indexed);
        }

        [Fact]
        public void DrawMeshInstancedProceduralWithBoundsOutsideTheFrustumIsNotCollected()
        {
            Graphics.DrawMeshInstancedProcedural(NewMesh(), 0, NewMaterial(), Box(-20f), 4);
            Assert.Single(Immediate.Queued);
            Assert.Empty(Collect());
            Immediate.Queued.Clear();
            Graphics.DrawMeshInstancedProcedural(NewMesh(), 0, NewMaterial(), Box(10f), 4);
            var item = Assert.Single(Collect());
            Assert.Equal(100f, item.Distance);
            Assert.Equal(4u, item.Setup.Value.Instances);
        }

        // ---------------------------------------------------------------- unset and point bounds (Unbound's choice, unmeasured)

        [Fact]
        public void LegacyDrawsWithDefaultBoundsAreNeitherCulledNorSortedDifferently()
        {
            // Guard (green before and after): default(Bounds) is not set; the draws sort at their
            // mesh bounds' centre through the identity matrix, the origin.
            Graphics.DrawProcedural(NewMaterial(), default, MeshTopology.Triangles, 3);
            Graphics.DrawMeshInstancedProcedural(NewMesh(), 0, NewMaterial(), default, 2);
            var collected = Collect();
            Assert.Equal(new[] { 0f, 0f }, collected.Select(d => d.Distance).ToArray());
        }

        [Fact]
        public void ZeroSizeBoundsAtAPointAreNeverCulledAndSortAtThatPoint()
        {
            // Unbound's choice (unmeasured; the renderers' InFrustum rule): set bounds without size
            // count as inside, here behind the camera.
            Graphics.DrawProcedural(NewMaterial(), new Bounds(new Vector3(0f, 0f, -30f), Vector3.zero), MeshTopology.Triangles, 3);
            Assert.Equal(900f, Assert.Single(Collect()).Distance);
        }

        // ---------------------------------------------------------------- DrawMeshInstanced: one group

        [Fact]
        public void DrawMeshInstancedSortsAllInstancesAtTheirGroupBoundsCentre()
        {
            // The box around both instances (z -20.5 to 10.5) has its centre at z -5.
            var matrices = new[] { At(-20f), At(10f) };
            Graphics.DrawMeshInstanced(NewMesh(), 0, NewMaterial(instancing: true), matrices);
            var collected = Collect();
            Assert.Equal(matrices, collected.Select(d => d.Model).ToArray());
            Assert.Equal(new[] { 25f, 25f }, collected.Select(d => d.Distance).ToArray());
            Assert.Equal(new[] { -20f, 10f }, collected.Select(d => d.Bounds.center.z).ToArray());
        }

        [Fact]
        public void DrawMeshInstancedWithEveryInstanceOutsideTheFrustumCollectsNone()
        {
            var material = NewMaterial(instancing: true);
            Graphics.DrawMeshInstanced(NewMesh(), 0, material, new[] { At(-20f), At(-30f) });
            Assert.Equal(2, Immediate.Queued.Count);
            Assert.Empty(Collect());
            // Still shadow casters, as other queued draws.
            Assert.Equal(2, ((List<FrameBuilder.DrawItem>)s_Casters.GetValue(null)).Count);
            Immediate.Queued.Clear();
            Graphics.DrawMeshInstanced(NewMesh(), 0, material, new List<Matrix4x4> { At(-20f), At(-30f) });
            Assert.Equal(2, Immediate.Queued.Count);
            Assert.Empty(Collect());
        }

        [Fact]
        public void DrawMeshInstancedWithOneInstanceInViewCollectsEveryInstance()
        {
            // Two instances far to the sides, out of view, one ahead: the group box (x -50.5 to
            // 50.5 at z 10) is in view, so all three are drawn, at its centre.
            var matrices = new[]
            {
                Matrix4x4.Translate(new Vector3(-50f, 0f, 10f)), At(10f), Matrix4x4.Translate(new Vector3(50f, 0f, 10f)),
            };
            Graphics.DrawMeshInstanced(NewMesh(), 0, NewMaterial(instancing: true), new List<Matrix4x4>(matrices));
            var collected = Collect();
            Assert.Equal(matrices, collected.Select(d => d.Model).ToArray());
            Assert.Equal(new[] { 100f, 100f, 100f }, collected.Select(d => d.Distance).ToArray());
        }

        [Fact]
        public void DrawMeshInstancedGroupSortsAmongOtherDrawsKeepingItsCallOrder()
        {
            // Transparent, back to front: the group (centre z -10, 100) between the draws at
            // 15 (225) and 5 (25); within the group, the call's order.
            Graphics.DrawMeshInstanced(NewMesh(), 0, NewMaterial(3000, instancing: true), new[] { At(-20f), At(10f), At(-30f) });
            Graphics.DrawMesh(NewMesh(), At(15f), NewMaterial(3000), 0);
            Graphics.DrawMesh(NewMesh(), At(5f), NewMaterial(3000), 0);
            var collected = Collect();
            Assert.Equal(new[] { 100f, 100f, 100f, 225f, 25f }, collected.Select(d => d.Distance).ToArray());
            FrameBuilder.SortForCamera(collected);
            Assert.Equal(new[] { At(15f), At(-20f), At(10f), At(-30f), At(5f) }, collected.Select(d => d.Model).ToArray());
        }

        // ---------------------------------------------------------------- guard (green before and after)

        [Fact]
        public void InstancedDrawsKeepTheirPerInstanceLightingProbesAndProperties()
        {
            // DrawMeshInstanced: one blended probe for the batch at its bounds' centre (measured:
            // tests/projects/lightprobes), each instance its own bounds and array element.
            var colors = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(0f, 1f, 0f, 1f) };
            var block = new MaterialPropertyBlock();
            block.SetVectorArray("_Color", colors);
            Graphics.DrawMeshInstanced(NewMesh(), 0, NewMaterial(instancing: true), new[] { At(-20f), At(10f) }, 2, block);
            var collected = Collect();
            Assert.Equal(2, collected.Count);
            Assert.All(collected, d => Assert.Equal((Vector3?)new Vector3(0f, 0f, -5f), d.Lighting.ProbePosition));
            Assert.All(collected, d => Assert.Equal(LightProbeUsage.BlendProbes, d.Lighting.Probes));
            Assert.Equal(new[] { -20f, 10f }, collected.Select(d => d.Bounds.center.z).ToArray());
            int color = Shader.PropertyToID("_Color");
            Assert.Equal(colors, collected.Select(d => (Vector4)d.Properties[color]).ToArray());

            // RenderMeshInstanced with unset world bounds: probes for each instance at its bounds.
            Immediate.Queued.Clear();
            var rp = new RenderParams(NewMaterial(instancing: true)) { lightProbeUsage = LightProbeUsage.BlendProbes };
            Graphics.RenderMeshInstanced(rp, NewMesh(), 0, new[] { At(-20f), At(10f) });
            collected = Collect();
            Assert.Equal(2, collected.Count);
            Assert.All(collected, d => Assert.False(d.Lighting.ProbePosition.HasValue));
            Assert.All(collected, d => Assert.Equal(LightProbeUsage.BlendProbes, d.Lighting.Probes));
            Assert.Equal(new[] { -20f, 10f }, collected.Select(d => d.Bounds.center.z).ToArray());
        }
    }
}
