using System;
using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>CullingGroup as measured in the Editor (tests/projects/cullinggroup).</summary>
    public class CullingGroupTests : IDisposable
    {
        readonly List<CullingGroup> m_Groups = new List<CullingGroup>();
        readonly List<UnityEngine.Object> m_Objects = new List<UnityEngine.Object>();

        public void Dispose()
        {
            foreach (var group in m_Groups)
                try { group.Dispose(); } catch (NullReferenceException) { }
            foreach (var o in m_Objects) UnityEngine.Object.DestroyImmediate(o);
        }

        Camera MakeCamera(Vector3 position)
        {
            var camera = new GameObject("Camera").AddComponent<Camera>();
            m_Objects.Add(camera.gameObject);
            camera.transform.position = position;
            camera.fieldOfView = 60;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 100;
            camera.aspect = 16f / 9f;
            return camera;
        }

        CullingGroup MakeGroup(List<string> events, string name)
        {
            var group = new CullingGroup();
            m_Groups.Add(group);
            group.onStateChanged = e => events.Add($"{name}{e.index} {(e.isVisible ? 1 : 0)}{(e.wasVisible ? 1 : 0)} {e.currentDistance}{e.previousDistance}" +
                (e.hasBecomeVisible ? " +" : "") + (e.hasBecomeInvisible ? " -" : ""));
            return group;
        }

        [Fact]
        public void ReplacingTheCallbackTakesEffectForTheNextSphere()
        {
            var camera = MakeCamera(Vector3.zero);
            var events = new List<string>();
            var group = MakeGroup(events, "unused");
            group.targetCamera = camera;
            group.SetBoundingSpheres(new[] {
                new BoundingSphere(new Vector3(0, 0, 10), 1),
                new BoundingSphere(new Vector3(1, 0, 10), 1),
                new BoundingSphere(new Vector3(2, 0, 10), 1),
            });
            group.onStateChanged = e => {
                events.Add("old" + e.index);
                group.onStateChanged = next => events.Add("new" + next.index);
            };
            CullingGroup.EvaluateForCamera(camera);
            Assert.Equal(new[] { "old0", "new1", "new2" }, events);
        }

        [Fact]
        public void BandsAndFrustumPlanesDecideTheStateAndChangesSendCallbacks()
        {
            var camera = MakeCamera(Vector3.zero);
            var events = new List<string>();
            var group = MakeGroup(events, "");
            group.targetCamera = camera;
            var spheres = new[]
            {
                new BoundingSphere(new Vector3(0, 0, 10), 1),        // 0 band 1
                new BoundingSphere(new Vector3(0, 0, -10), 1),       // 1 behind
                new BoundingSphere(new Vector3(11.5f, 0, 10), 1),    // 2 crossing the right plane
                new BoundingSphere(new Vector3(11.9f, 0, 10), 1),    // 3 outside it
                new BoundingSphere(new Vector3(0, 0, 0.1f), 0.05f),  // 4 before the near plane: unchanged state
                new BoundingSphere(new Vector3(0, 0, 6), 1),         // 5 surface exactly at 5
                new BoundingSphere(new Vector3(0, 0, 20), 0),        // 6 exactly at the last distance
                new BoundingSphere(new Vector3(0, -3, 30), 1),       // 7 in view, beyond the last distance
                new BoundingSphere(new Vector3(0, 0, 25), 10),       // 8 surface 15
            };
            group.SetBoundingSpheres(spheres);
            group.SetBoundingDistances(new float[] { 5, 10, 20 });
            Assert.False(group.IsVisible(0));
            Assert.Equal(0, group.GetDistance(0));

            CullingGroup.EvaluateForCamera(camera);
            Assert.Equal(new[] { "0 10 10 +", "1 00 10", "2 10 20 +", "3 00 20", "5 10 00 +", "6 10 20 +", "7 00 30", "8 10 20 +" }, events);

            // The array is read by reference; only changed states call back, from their last callback's state.
            events.Clear();
            spheres[0].position = new Vector3(0, 0, -10);
            CullingGroup.EvaluateForCamera(camera);
            CullingGroup.EvaluateForCamera(MakeCamera(Vector3.zero)); // not the target
            Assert.Equal(new[] { "0 01 11 -" }, events);
            var result = new int[16];
            Assert.Equal(4, group.QueryIndices(true, result, 0));
            Assert.Equal(new[] { 2, 5, 6, 8 }, result[..4]);
            Assert.Equal(3, group.QueryIndices(true, 2, result, 0));
            Assert.Equal(new[] { 2, 6, 8 }, result[..3]);
            Assert.Equal(1, group.QueryIndices(3, result, 0));
            Assert.Equal(7, result[0]);
            Assert.Equal(5, group.QueryIndices(false, null, 0));
            Assert.Equal(2, group.QueryIndices(false, new int[2], 0));
            Assert.Equal(3, group.QueryIndices(false, result, 3));
            Assert.Throws<IndexOutOfRangeException>(() => group.QueryIndices(false, result, -1));
        }

        [Fact]
        public void UntargetedGroupsOnlyUpdateBandsAndDisabledOnesNothing()
        {
            var camera = MakeCamera(Vector3.zero);
            var events = new List<string>();
            var group = MakeGroup(events, "");
            group.targetCamera = camera;
            var spheres = new[] { new BoundingSphere(new Vector3(0, 0, 10), 1) };
            group.SetBoundingSpheres(spheres);
            group.SetBoundingDistances(new float[] { 5, 10, 20 });
            CullingGroup.EvaluateForCamera(camera);
            group.targetCamera = null;
            spheres[0].position = new Vector3(0, 0, -15);
            CullingGroup.EvaluateForCamera(camera);
            Assert.True(group.IsVisible(0));
            Assert.Equal(2, group.GetDistance(0));
            group.enabled = false;
            group.targetCamera = camera;
            CullingGroup.EvaluateForCamera(camera);
            Assert.True(group.IsVisible(0));
            group.enabled = true;
            CullingGroup.EvaluateForCamera(camera);
            Assert.Equal(new[] { "0 10 10 +", "0 01 21 -" }, events);
        }

        [Fact]
        public void ReferencePointsCountsAndDisposal()
        {
            var camera = MakeCamera(new Vector3(0, 0, -20));
            var events = new List<string>();
            var a = MakeGroup(events, "a");
            var b = MakeGroup(events, "b");
            var c = MakeGroup(events, "c");
            var spheres = new[] { new BoundingSphere(new Vector3(0, 0, 10), 1), new BoundingSphere(new Vector3(0, 0, 20), 1) };
            foreach (var g in new[] { a, b, c })
            {
                g.targetCamera = camera;
                g.SetBoundingDistances(new float[] { 15, 40 });
            }
            a.SetBoundingSpheres(spheres);
            // The reference is the origin, not the camera; a null transform keeps the last point.
            b.SetBoundingSpheres(new[] { spheres[0] });
            b.SetBoundingDistances(new float[] { 15, 40, 500 });
            b.SetDistanceReferencePoint(new Vector3(0, 0, -100));
            b.SetDistanceReferencePoint((Transform)null);
            c.SetBoundingSpheres(new[] { spheres[0] });
            a.Dispose();
            CullingGroup.EvaluateForCamera(camera);
            // Disposing a moved the last group, c, into its place.
            Assert.Equal(new[] { "c0 10 00 +", "b0 10 20 +" }, events);

            Assert.Throws<NullReferenceException>(() => a.Dispose());
            Assert.Throws<NullReferenceException>(() => a.IsVisible(0));
            Assert.NotNull(a.onStateChanged);

            var d = MakeGroup(events, "d");
            var four = new BoundingSphere[4];
            for (int i = 0; i < 4; i++) four[i] = new BoundingSphere(new Vector3(i, 0, 0), 1);
            d.SetBoundingSpheres(four);
            d.SetBoundingSphereCount(5);
            Assert.Equal(4, d.QueryIndices(false, null, 0));
            Assert.Throws<IndexOutOfRangeException>(() => d.IsVisible(4));
            d.EraseSwapBack(0);
            Assert.Equal(3f, four[0].position.x);
            Assert.Equal(3, d.QueryIndices(false, null, 0));
            int size = 3;
            var ints = new[] { 10, 11, 12, 13 };
            CullingGroup.EraseSwapBack(1, ints, ref size);
            Assert.Equal(2, size);
            Assert.Equal(new[] { 10, 12, 12, 13 }, ints);
        }
    }
}
