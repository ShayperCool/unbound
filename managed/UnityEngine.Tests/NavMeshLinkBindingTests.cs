using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Which NavMesh a point link's ends attach to, among stacked data (Unity 6000.0.71f1,
    /// tests/projects/navpath `--profile layers`): the polygon nearest each end within the agent
    /// type's climb, bound while its data stays; another release attaches to every tile over it.
    /// Two floors at x -6..-2 and 2..6, a link from x -2.7 to 2.7.</summary>
    [Collection("Frames")]
    public sealed class NavMeshLinkBindingTests
    {
        static NavMeshBuildSource FloorSource(float x) =>
            new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(4f, 1f, 8f), transform = Matrix4x4.Translate(new Vector3(x + 90f, -0.5f, 0f)) };

        static readonly Bounds s_Bounds = new Bounds(new Vector3(90f, 0f, 0f), new Vector3(16f, 4f, 12f));

        static NavMeshData Floor(float x, float climb = -1f)
        {
            var settings = NavMesh.GetSettingsByID(0);
            if (climb >= 0f) settings.agentClimb = climb;
            return NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { FloorSource(x) }, s_Bounds, Vector3.zero, Quaternion.identity);
        }

        static Vector3 At(float x, float y) => new Vector3(x + 90f, y, 0f);

        static NavMeshPathStatus Path(float fromY, float toY)
        {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(At(-3.7f, fromY), At(3.7f, toY), NavMesh.AllAreas, path);
            return path.status;
        }

        static NavMeshLinkInstance Link(float y) => NavMesh.AddLink(new NavMeshLinkData { startPosition = At(-2.7f, y), endPosition = At(2.7f, y), bidirectional = true, costModifier = -1f });

        [Theory]
        [InlineData("6000.0.71f1", NavMeshPathStatus.PathPartial)]
        [InlineData("6000.0.72f1", NavMeshPathStatus.PathComplete)]
        public void ALinkOnTheLowerFloorDoesNotServeTheUpperOne(string release, NavMeshPathStatus upper)
        {
            NavigationTestRelease.At(release, () =>
            {
                var left = Floor(-4f);
                var right = Floor(4f);
                var data = new List<NavMeshDataInstance>
                {
                    NavMesh.AddNavMeshData(left), NavMesh.AddNavMeshData(right),
                    NavMesh.AddNavMeshData(left, new Vector3(0f, 0.3f, 0f), Quaternion.identity),
                    NavMesh.AddNavMeshData(right, new Vector3(0f, 0.3f, 0f), Quaternion.identity),
                };
                var link = Link(0.0833f);
                try
                {
                    Assert.Equal(NavMeshPathStatus.PathComplete, Path(0.0833f, 0.0833f));
                    Assert.Equal(upper, Path(0.3833f, 0.3833f));
                }
                finally
                {
                    NavMesh.RemoveLink(link);
                    foreach (var d in data) NavMesh.RemoveNavMeshData(d);
                }
            });
        }

        [Fact]
        public void BindingsLastWhileTheirDataStays() => NavigationTestRelease.At("6000.0.71f1", () =>
        {
            var left = Floor(-4f);
            var right = Floor(4f);
            var highLeft = NavMesh.AddNavMeshData(left, new Vector3(0f, 0.3f, 0f), Quaternion.identity);
            var highRight = NavMesh.AddNavMeshData(right, new Vector3(0f, 0.3f, 0f), Quaternion.identity);
            var lowLeft = NavMesh.AddNavMeshData(left);
            var lowRight = NavMesh.AddNavMeshData(right);
            var link = Link(0.0833f);
            try
            {
                Assert.Equal(NavMeshPathStatus.PathComplete, Path(0.0833f, 0.0833f));
                // Its lower floor removed: the start attaches nowhere (the upper floor is not taken).
                NavMesh.RemoveNavMeshData(lowLeft);
                NavMesh.RemoveNavMeshData(lowRight);
                Assert.Equal(NavMeshPathStatus.PathPartial, Path(0.3833f, 0.3833f));
                // Added again: the start binds to it; the end, bound to the upper floor meanwhile, stays.
                lowLeft = NavMesh.AddNavMeshData(left);
                lowRight = NavMesh.AddNavMeshData(right);
                var path = new NavMeshPath();
                NavMesh.CalculatePath(At(-3.7f, 0.0833f), At(3.7f, 0.0833f), NavMesh.AllAreas, path);
                // Through the link to the upper floor, short of the lower target (as the Editor).
                Assert.Equal(NavMeshPathStatus.PathPartial, path.status);
                Assert.Equal(4, path.corners.Length);
                Assert.True(path.corners[2].y > 0.3f, "the end lands on the upper floor");
                Assert.Equal(NavMeshPathStatus.PathPartial, Path(0.3833f, 0.3833f));
                // The link added again binds both ends to the nearest floor.
                NavMesh.RemoveLink(link);
                link = Link(0.0833f);
                NavMesh.CalculatePath(At(-3.7f, 0.0833f), At(3.7f, 0.0833f), NavMesh.AllAreas, path);
                Assert.True(path.corners[2].y < 0.2f, "the end lands on the lower floor");
            }
            finally
            {
                NavMesh.RemoveLink(link);
                foreach (var d in new[] { lowLeft, lowRight, highLeft, highRight }) NavMesh.RemoveNavMeshData(d);
            }
        });

        [Theory]
        // The reach is the agent type's climb (0.75), whatever the data was built with.
        [InlineData(0.1f, 0.25f, NavMeshPathStatus.PathComplete)]
        [InlineData(1.2f, 1f, NavMeshPathStatus.PathPartial)]
        public void TheAgentTypesClimbDecidesTheReach(float dataClimb, float above, NavMeshPathStatus status) => NavigationTestRelease.At("6000.0.71f1", () =>
        {
            var left = NavMesh.AddNavMeshData(Floor(-4f, dataClimb));
            var right = NavMesh.AddNavMeshData(Floor(4f, dataClimb));
            var link = Link(0.0833f + above);
            try { Assert.Equal(status, Path(0.0833f, 0.0833f)); }
            finally
            {
                NavMesh.RemoveLink(link);
                NavMesh.RemoveNavMeshData(left);
                NavMesh.RemoveNavMeshData(right);
            }
        });

        [Fact]
        public void AStartRebindsWhenItsDataIsReplacedBeforeAnyQuery() => NavigationTestRelease.At("6000.0.71f1", () =>
        {
            // As NavMeshSurface.BuildNavMesh does: the old data removed and the new added at once.
            var leftData = Floor(-4f);
            var left = NavMesh.AddNavMeshData(leftData);
            var right = NavMesh.AddNavMeshData(Floor(4f));
            var link = Link(0.0833f);
            try
            {
                Assert.Equal(NavMeshPathStatus.PathComplete, Path(0.0833f, 0.0833f));
                NavMesh.RemoveNavMeshData(left);
                left = NavMesh.AddNavMeshData(Floor(-4f));
                Assert.Equal(NavMeshPathStatus.PathComplete, Path(0.0833f, 0.0833f));
            }
            finally
            {
                NavMesh.RemoveLink(link);
                NavMesh.RemoveNavMeshData(left);
                NavMesh.RemoveNavMeshData(right);
            }
        });

        [Fact]
        public void AWaitingStartIgnoresUnrelatedDataAdded() => NavigationTestRelease.At("6000.0.71f1", () =>
        {
            // Pins this implementation's choice (rebinding among data added since the removal);
            // a start rebinding to other data is unmeasured in Unity.
            var left = Floor(-4f);
            var right = Floor(4f);
            var highLeft = NavMesh.AddNavMeshData(left, new Vector3(0f, 0.3f, 0f), Quaternion.identity);
            var highRight = NavMesh.AddNavMeshData(right, new Vector3(0f, 0.3f, 0f), Quaternion.identity);
            var lowLeft = NavMesh.AddNavMeshData(left);
            var lowRight = NavMesh.AddNavMeshData(right);
            var link = Link(0.0833f);
            var far = default(NavMeshDataInstance);
            try
            {
                Assert.Equal(NavMeshPathStatus.PathComplete, Path(0.0833f, 0.0833f));
                NavMesh.RemoveNavMeshData(lowLeft);
                NavMesh.RemoveNavMeshData(lowRight);
                Assert.Equal(NavMeshPathStatus.PathPartial, Path(0.3833f, 0.3833f));
                // Data far away does not hand the waiting start to the upper floor.
                far = NavMesh.AddNavMeshData(left, new Vector3(0f, 0f, 40f), Quaternion.identity);
                Assert.Equal(NavMeshPathStatus.PathPartial, Path(0.3833f, 0.3833f));
            }
            finally
            {
                NavMesh.RemoveLink(link);
                foreach (var d in new[] { highLeft, highRight, far }) NavMesh.RemoveNavMeshData(d);
            }
        });

        [Fact]
        public void AWaitingStartBindsToDataUpdatedInPlace() => NavigationTestRelease.At("6000.0.71f1", () =>
        {
            // Pins this implementation's choice (data updated in place counts as added); unmeasured
            // in Unity.
            var settings = NavMesh.GetSettingsByID(0);
            var leftData = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource>(), s_Bounds, Vector3.zero, Quaternion.identity);
            var left = NavMesh.AddNavMeshData(leftData);
            var right = NavMesh.AddNavMeshData(Floor(4f));
            var link = Link(0.0833f);
            try
            {
                Path(0.0833f, 0.0833f); // the bindings made while the left data is empty
                Assert.True(NavMeshBuilder.UpdateNavMeshData(leftData, settings, new List<NavMeshBuildSource> { FloorSource(-4f) }, s_Bounds));
                Assert.Equal(NavMeshPathStatus.PathComplete, Path(0.0833f, 0.0833f));
            }
            finally
            {
                NavMesh.RemoveLink(link);
                NavMesh.RemoveNavMeshData(left);
                NavMesh.RemoveNavMeshData(right);
            }
        });

        [Fact]
        public void AnEndAboveASlopeAttachesStraightBelow() => NavigationTestRelease.At("6000.0.71f1", () =>
        {
            // Agent radius 0.2: an end 0.6 above a 30 degree slope is over a polygon within the
            // climb, though the slope's nearest point is 0.26 away across (Detour projects down).
            // Away from the tile edges (z 0, x 94), where an end lands in one tile only.
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.2f;
            var ramp = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box, size = new Vector3(4f, 1f, 8f),
                transform = Matrix4x4.TRS(new Vector3(94f, 0.5f, 0f), Quaternion.Euler(0f, 0f, 30f), Vector3.one),
            };
            var left = NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { FloorSource(-4f) }, s_Bounds, Vector3.zero, Quaternion.identity));
            var right = NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { ramp }, s_Bounds, Vector3.zero, Quaternion.identity));
            var link = default(NavMeshLinkInstance);
            try
            {
                Assert.True(NavMesh.SamplePosition(At(3.5f, 5f) + Vector3.forward, out var hit, 6f, NavMesh.AllAreas));
                var end = hit.position + new Vector3(0f, 0.6f, 0f);
                link = NavMesh.AddLink(new NavMeshLinkData { startPosition = At(-2.7f, 0.0833f), endPosition = end, bidirectional = true, costModifier = -1f });
                var path = new NavMeshPath();
                NavMesh.CalculatePath(At(-3.7f, 0.0833f), hit.position, NavMesh.AllAreas, path);
                Assert.Equal(NavMeshPathStatus.PathComplete, path.status);
            }
            finally
            {
                NavMesh.RemoveLink(link);
                NavMesh.RemoveNavMeshData(left);
                NavMesh.RemoveNavMeshData(right);
            }
        });

        [Fact]
        public void AnEndOnACarvedTileOfMovedDataAttaches() => NavigationTestRelease.At("6000.0.71f1", () =>
        {
            // The right floor's data is placed 1 m along x (a moved surface), and a carving
            // obstacle cuts the tile its link end is on, away from the end.
            ObstacleSystem.ResetCarving();
            var left = NavMesh.AddNavMeshData(Floor(-4f));
            var right = NavMesh.AddNavMeshData(Floor(3f), Vector3.right, Quaternion.identity);
            var go = new GameObject("obstacle");
            go.transform.position = new Vector3(95f, 0.5f, 2.5f);
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(0.5f, 1f, 0.5f);
            obstacle.carveOnlyStationary = false;
            obstacle.carving = true;
            var link = NavMesh.AddLink(new NavMeshLinkData { startPosition = At(-2.7f, 0.0833f) + Vector3.forward, endPosition = At(2.7f, 0.0833f) + Vector3.forward, bidirectional = true, costModifier = -1f });
            try
            {
                ObstacleSystem.UpdateCarving();
                ObstacleSystem.UpdateCarving();
                Assert.False(NavMesh.SamplePosition(new Vector3(95f, 0.0833f, 2.5f), out _, 0.05f, NavMesh.AllAreas));
                var path = new NavMeshPath();
                NavMesh.CalculatePath(At(-3.7f, 0.0833f) + Vector3.forward, At(3.7f, 0.0833f) + Vector3.forward, NavMesh.AllAreas, path);
                Assert.Equal(NavMeshPathStatus.PathComplete, path.status);
            }
            finally
            {
                NavMesh.RemoveLink(link);
                Object.DestroyImmediate(go);
                ObstacleSystem.ResetCarving();
                NavMesh.RemoveNavMeshData(left);
                NavMesh.RemoveNavMeshData(right);
            }
        });
    }
}
