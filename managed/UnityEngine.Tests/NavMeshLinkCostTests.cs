using System.Collections.Generic;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A link's cost modifier in path search. Documented (Scripting API 6000.0
    /// NavMeshLinkData.costModifier: it multiplies the distance between the link's ends, a negative
    /// value keeps the area type's cost; AI Navigation 2.0 NavMeshLink.costModifier: "a positive or
    /// zero value overrides the cost associated with the area type"). Measured (Unity 6000.0.71f1,
    /// tests/projects/navlinks costhigh): a link of cost 3 loses to a bridge round it. Zero to one
    /// is unmeasured. The level is two 6 m platforms with a 4 m gap at z = 0, joined by a bridge
    /// along their north sides; a link crosses the gap from x = -3 to x = 3.</summary>
    [Collection("Frames")]
    public sealed class NavMeshLinkCostTests : System.IDisposable
    {
        readonly NavMeshDataInstance m_Data;
        NavMeshLinkInstance m_Link;
        bool m_HasLink;

        public NavMeshLinkCostTests()
        {
            var sources = new List<NavMeshBuildSource>
            {
                Box(new Vector3(-5f, 0f, 0f), new Vector3(6f, 1f, 6f)),
                Box(new Vector3(5f, 0f, 0f), new Vector3(6f, 1f, 6f)),
                Box(new Vector3(0f, 0f, 4.5f), new Vector3(16f, 1f, 3f)),
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(Vector3.zero, new Vector3(24f, 4f, 20f)), Vector3.zero, Quaternion.identity);
            m_Data = NavMesh.AddNavMeshData(data);
        }

        public void Dispose()
        {
            if (m_HasLink) NavMesh.RemoveLink(m_Link);
            NavMesh.RemoveNavMeshData(m_Data);
        }

        static NavMeshBuildSource Box(Vector3 centre, Vector3 size) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = size,
            transform = Matrix4x4.Translate(centre + new Vector3(0f, -0.5f, 0f)),
            area = 0,
        };

        bool PathTakesTheBridge(float costModifier, int area = 0)
        {
            if (m_HasLink) NavMesh.RemoveLink(m_Link);
            m_HasLink = true;
            m_Link = NavMesh.AddLink(new NavMeshLinkData
            {
                startPosition = new Vector3(-3f, 0f, 0f),
                endPosition = new Vector3(3f, 0f, 0f),
                bidirectional = true,
                costModifier = costModifier,
                width = 0f,
                area = area,
                agentTypeID = 0,
            });
            var path = new NavMeshPath();
            Assert.True(NavMesh.CalculatePath(new Vector3(-4.5f, 0f, 0f), new Vector3(4.5f, 0f, 0f), NavMesh.AllAreas, path));
            Assert.Equal(NavMeshPathStatus.PathComplete, path.status);
            bool bridge = false;
            foreach (var corner in path.corners)
                if (corner.z > 2f) bridge = true;
            return bridge;
        }

        // The link route costs about 3 plus 6 times the link's cost per metre; the bridge about 13.
        [Fact]
        public void ANegativeCostModifierKeepsTheAreaCost()
        {
            Assert.False(PathTakesTheBridge(-1f));
            // Jump (area 2) costs 2 per metre by default: the link loses.
            Assert.True(PathTakesTheBridge(-1f, area: 2));
        }

        [Fact]
        public void AHighCostModifierSendsThePathRound() => Assert.True(PathTakesTheBridge(3f));

        [Fact]
        public void AZeroCostModifierReplacesTheAreaCost() => Assert.False(PathTakesTheBridge(0f, area: 2));
    }
}
