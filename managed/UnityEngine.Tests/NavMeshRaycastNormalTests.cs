using System;
using System.Collections.Generic;
using DotRecast.Detour;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The hit normal of a NavMesh raycast stopped by a wall (DtNavMeshQuery.Raycast, which
    /// NavMesh.Raycast and NavMeshAgent.Raycast report as NavMeshHit.normal).
    /// Documented (Scripting API 6000.0 NavMeshHit.normal): "Normal at the point of hit"; no
    /// direction is documented. Observed, not documented (tests/projects/navmesh/Reference/
    /// unity-6000.0.71f1.json, "raycast"): blocked rays report a horizontal unit normal facing back
    /// along the ray, also on a wall that is not axis-aligned (n0.8,0,-0.6 for a ray along +z).
    /// The tests check the wall polygon edge the query itself reports (the last visited polygon and
    /// its hit edge index): the normal is horizontal, unit length, perpendicular to that edge and
    /// faces back along the ray, into the polygon the ray leaves (Detour's side, which the capture
    /// shows). Nothing here fixes Unity's choice between equally near walls or any order.
    /// The level is one 4 m by 8 m box platform per test, rotated about y, in one NavMesh tile. The
    /// red cases sit far from the line x = z (x near 10, z near -10), so a wall edge's start x and
    /// start z differ by meters; the guard sits on that line (x and z near 10).
    /// NavMesh data is global, so this shares the frame-stepping collection with the other NavMesh
    /// tests.</summary>
    [Collection("Frames")]
    public sealed class NavMeshRaycastNormalTests : IDisposable
    {
        static readonly Vector3 OffDiagonal = new Vector3(10f, 0f, -10f);
        static readonly Vector3 OnDiagonal = new Vector3(10f, 0f, 10f);
        static readonly Vector3 PlatformSize = new Vector3(4f, 1f, 8f);
        const float RayLength = 10f;

        readonly List<NavMeshDataInstance> m_Data = new List<NavMeshDataInstance>();

        public void Dispose()
        {
            foreach (var data in m_Data) NavMesh.RemoveNavMeshData(data);
        }

        /// <summary>A box platform centred on <paramref name="center"/> (top at y = 0), turned by
        /// <paramref name="yaw"/> degrees about y; its NavMesh lies in one tile.</summary>
        void Platform(Vector3 center, float yaw)
        {
            var source = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = PlatformSize,
                transform = Matrix4x4.TRS(center + new Vector3(0f, -0.5f, 0f), Quaternion.Euler(0f, yaw, 0f), Vector3.one),
                area = 0,
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> { source },
                new Bounds(center, new Vector3(12f, 4f, 12f)), Vector3.zero, Quaternion.identity);
            m_Data.Add(NavMesh.AddNavMeshData(data));
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Casts from <paramref name="from"/> towards <paramref name="to"/> with
        /// DtNavMeshQuery.Raycast as NavMesh.Raycast starts it (the nearest NavMesh point within the
        /// agent's extents). Returns the hit parameter, the hit normal and, for a wall hit, the hit
        /// polygon edge's start and end vertices (world space).</summary>
        static float Cast(Vector3 from, Vector3 to, out Vector3 normal, out Vector3 edgeStart, out Vector3 edgeEnd)
        {
            var surface = NavWorld.Get(0);
            Assert.NotNull(surface);
            var filter = AreaFilter.Of(NavMesh.AllAreas);
            Assert.True(NavMesh.Nearest(surface, from, NavMesh.AgentExtents(0), filter, out long startRef, out var start));
            var hit = new DtRaycastHit { path = new long[64], maxPath = 64 };
            var status = surface.Query.Raycast(startRef, NavMesh.V(start), NavMesh.V(to), filter, 0, ref hit, 0);
            Assert.True(status.Succeeded());
            Assert.True(hit.pathCount > 0);
            normal = NavMesh.V(hit.hitNormal);
            edgeStart = edgeEnd = Vector3.zero;
            if (hit.t >= 1f) return hit.t;
            Assert.True(surface.Mesh.GetTileAndPolyByRef(hit.path[hit.pathCount - 1], out var tile, out var poly).Succeeded());
            Assert.InRange(hit.hitEdgeIndex, 0, poly.vertCount - 1);
            var v = tile.data.verts;
            int a = poly.verts[hit.hitEdgeIndex] * 3;
            int b = poly.verts[(hit.hitEdgeIndex + 1) % poly.vertCount] * 3;
            edgeStart = new Vector3(v[a], v[a + 1], v[a + 2]);
            edgeEnd = new Vector3(v[b], v[b + 1], v[b + 2]);
            return hit.t;
        }

        /// <summary>The checks every wall hit passes: a horizontal unit normal perpendicular to the
        /// hit edge, facing back along the ray; NavMesh.Raycast reports the same normal.</summary>
        static void WallNormal(Vector3 from, Vector3 to, Vector3 normal, Vector3 edgeStart, Vector3 edgeEnd)
        {
            var edge = Flat(edgeEnd - edgeStart);
            Assert.True(edge.magnitude > 0.1f, $"degenerate hit edge {edgeStart} -> {edgeEnd}");
            Assert.Equal(0f, normal.y);
            Assert.InRange(normal.magnitude, 0.9999f, 1.0001f);
            float alongEdge = Vector3.Dot(normal, edge.normalized);
            Assert.True(Mathf.Abs(alongEdge) < 1e-4f,
                $"normal {normal} not perpendicular to the hit edge {edgeStart} -> {edgeEnd} (dot {alongEdge})");
            var dir = Flat(to - from).normalized;
            Assert.True(Vector3.Dot(normal, dir) < 0f, $"normal {normal} does not face back along the ray {dir}");

            Assert.True(NavMesh.Raycast(from, to, out var navHit, NavMesh.AllAreas));
            Assert.True(navHit.hit);
            Assert.True((navHit.normal - normal).magnitude < 1e-6f, $"NavMesh.Raycast normal {navHit.normal}, query {normal}");
        }

        /// <summary>Rays from the platform's centre straight at each of its four walls, the platform
        /// turned to several angles: walls that are not axis-aligned, with edges whose start x and
        /// start z differ (red before the fix: the normal was not perpendicular to the edge). Yaw 0
        /// with rays along ±z is the axis-aligned wall along x (also red before the fix).</summary>
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(0f, 180f)]
        [InlineData(30f, 0f)]
        [InlineData(30f, 90f)]
        [InlineData(30f, 180f)]
        [InlineData(30f, 270f)]
        [InlineData(120f, 0f)]
        [InlineData(120f, 90f)]
        [InlineData(120f, 180f)]
        [InlineData(120f, 270f)]
        [InlineData(200f, 0f)]
        [InlineData(200f, 90f)]
        [InlineData(200f, 180f)]
        [InlineData(200f, 270f)]
        [InlineData(315f, 0f)]
        [InlineData(315f, 90f)]
        [InlineData(315f, 180f)]
        [InlineData(315f, 270f)]
        public void NormalIsPerpendicularToTheWallItHits(float yaw, float rayYaw)
        {
            Platform(OffDiagonal, yaw);
            var dir = Quaternion.Euler(0f, yaw + rayYaw, 0f) * Vector3.forward;
            var from = OffDiagonal;
            var to = OffDiagonal + dir * RayLength;
            float t = Cast(from, to, out var normal, out var a, out var b);
            Assert.True(t > 0f && t < 1f, $"ray not stopped by a wall (t {t})");
            // The case discriminates: the edge's start x and z differ, and it is not parallel to z.
            Assert.True(Mathf.Abs(a.x - a.z) > 1f, $"edge start {a} has x close to z");
            var edge = Flat(b - a);
            Assert.True(Mathf.Abs(edge.x) >= 0.2f * edge.magnitude, $"edge {a} -> {b} nearly parallel to z");

            WallNormal(from, to, normal, a, b);
            // The wall it hits is the platform side the ray points at (within the voxel steps of a
            // turned side): the normal faces back along the ray.
            Assert.True(Vector3.Dot(normal, -dir) > 0.9f, $"normal {normal} not the turned side's, ray {dir}");
        }

        /// <summary>Guard: an axis-aligned wall along z whose edge start x lies between the edge's
        /// start and end z, where the old expression gave the same normal (passes before and after
        /// the fix).</summary>
        [Theory]
        [InlineData(90f)]
        [InlineData(270f)]
        public void NormalOfAnAxisAlignedWallAlongZ(float rayYaw)
        {
            Platform(OnDiagonal, 0f);
            var dir = Quaternion.Euler(0f, rayYaw, 0f) * Vector3.forward;
            var from = OnDiagonal;
            var to = OnDiagonal + dir * RayLength;
            float t = Cast(from, to, out var normal, out var a, out var b);
            Assert.True(t > 0f && t < 1f, $"ray not stopped by a wall (t {t})");
            Assert.Equal(a.x, b.x);
            Assert.True(a.x > Mathf.Min(a.z, b.z) && a.x < Mathf.Max(a.z, b.z), $"edge {a} -> {b}: start x not between its z");

            WallNormal(from, to, normal, a, b);
            Assert.True((normal + new Vector3(Mathf.Sign(dir.x), 0f, 0f)).magnitude < 1e-6f, $"normal {normal}, ray {dir}");
        }

        /// <summary>Guard: a ray ending on the NavMesh hits no wall; NavMesh.Raycast reports no hit
        /// and a zero normal (passes before and after the fix).</summary>
        [Fact]
        public void RayEndingOnTheNavMeshHitsNoWall()
        {
            Platform(OffDiagonal, 30f);
            var to = OffDiagonal + Quaternion.Euler(0f, 30f, 0f) * Vector3.forward;
            float t = Cast(OffDiagonal, to, out var normal, out _, out _);
            Assert.Equal(float.MaxValue, t);
            Assert.Equal(Vector3.zero, normal);
            Assert.False(NavMesh.Raycast(OffDiagonal, to, out var navHit, NavMesh.AllAreas));
            Assert.False(navHit.hit);
            Assert.Equal(Vector3.zero, navHit.normal);
        }
    }
}
