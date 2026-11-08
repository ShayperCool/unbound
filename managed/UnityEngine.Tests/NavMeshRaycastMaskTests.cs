using System;
using System.Collections.Generic;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The area mask of a NavMesh raycast's hit. Documented (Scripting API 6000.0,
    /// NavMesh.Raycast, NavMeshHit.mask): the hit describes where the ray stopped, its mask the
    /// area there. Measured (tests/projects/navcost raycast, tests/projects/navbaked raycast, Unity
    /// 6000.0.71f1): a ray stopped where the filter excludes the polygon beyond reports that
    /// polygon's area, whichever allowed areas it crossed; one stopped at the NavMesh's border
    /// reports none. The level is one runtime-built ground with a Mud box across it, away from
    /// other tests' levels; NavMesh data is global, so this shares the frame-stepping collection.</summary>
    [Collection("Frames")]
    public sealed class NavMeshRaycastMaskTests : IDisposable
    {
        const int Mud = 4;
        readonly List<NavMeshDataInstance> m_Data = new List<NavMeshDataInstance>();

        public void Dispose()
        {
            foreach (var data in m_Data) NavMesh.RemoveNavMeshData(data);
        }

        /// <summary>A runtime-built ground with a Mud box across it.</summary>
        void MudLevel()
        {
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(12f, 1f, 12f), transform = Matrix4x4.Translate(new Vector3(30f, -0.5f, 30f)), area = 0 },
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(10f, 0.04f, 1f), transform = Matrix4x4.Translate(new Vector3(29f, 0.02f, 30f)), area = Mud },
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, new Bounds(new Vector3(30f, 0f, 30f), new Vector3(14f, 4f, 14f)),
                Vector3.zero, Quaternion.identity);
            m_Data.Add(NavMesh.AddNavMeshData(data));
        }

        /// <summary>Mud's 16 for a ray stopped at the Mud the mask excludes, from either side (navcost
        /// noMud, noMud.overWater; navbaked mud.masked); none at the border; a clear ray its end
        /// polygon's area.</summary>
        [Fact]
        public void AStoppedRayReportsTheExcludedAreaOrNone()
        {
            MudLevel();
            int noMud = NavMesh.AllAreas & ~(1 << Mud);
            Assert.True(NavMesh.Raycast(new Vector3(27f, 0f, 27f), new Vector3(27f, 0f, 33f), out var hit, noMud));
            Assert.Equal(1 << Mud, hit.mask);
            Assert.True(hit.position.z < 30f);
            // From the other side as well.
            Assert.True(NavMesh.Raycast(new Vector3(27f, 0f, 33f), new Vector3(27f, 0f, 27f), out hit, noMud));
            Assert.Equal(1 << Mud, hit.mask);
            // Off the NavMesh's edge: no area.
            Assert.True(NavMesh.Raycast(new Vector3(27f, 0f, 27f), new Vector3(27f, 0f, 20f), out hit, noMud));
            Assert.Equal(0, hit.mask);
            // Clear into the Mud: its area.
            Assert.False(NavMesh.Raycast(new Vector3(27f, 0f, 27f), new Vector3(27f, 0f, 30f), out hit, NavMesh.AllAreas));
            Assert.Equal(1 << Mud, hit.mask);
        }
    }
}
