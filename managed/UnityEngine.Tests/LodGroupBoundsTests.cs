using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>
    /// LODGroup.RecalculateBounds: the renderers' local bounds carried into the group's space
    /// (Unity 6000.0.71f1, tests/projects/lodbounds; the expected values are the Editor's, to the
    /// probe's tolerance of 4e-6).
    /// </summary>
    public class LodGroupBoundsTests : IDisposable
    {
        const float Tolerance = 4e-6f;
        readonly List<GameObject> m_Owned = new List<GameObject>();

        public void Dispose()
        {
            foreach (var go in m_Owned) if (go) Object.DestroyImmediate(go);
        }

        static float Q(int n, int d) => n / (float)d;

        Renderer Cube(Transform parent, Vector3 position, Vector3 euler, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            return go.GetComponent<Renderer>();
        }

        /// <summary>The probe's rotated children under a group placed at (x, 0, 0).</summary>
        (LODGroup, Renderer[]) Group(Vector3 position, Vector3 euler, Vector3 scale)
        {
            var root = new GameObject("LodGroupBoundsTests");
            m_Owned.Add(root);
            root.transform.position = position;
            root.transform.eulerAngles = euler;
            root.transform.localScale = scale;
            var renderers = new[]
            {
                Cube(root.transform, new Vector3(Q(61, 100), Q(133, 100), Q(-13, 100)), new Vector3(0, 30, 0), new Vector3(Q(13, 10), Q(7, 10), Q(9, 10))),
                Cube(root.transform, new Vector3(Q(-47, 100), Q(82, 100), Q(31, 100)), new Vector3(15, 0, 40), new Vector3(Q(9, 10), Q(11, 10), Q(5, 10))),
                Cube(root.transform, new Vector3(Q(23, 100), Q(-58, 100), Q(77, 100)), new Vector3(70, 20, 10), new Vector3(Q(3, 10), Q(17, 10), Q(6, 10))),
            };
            return (root.AddComponent<LODGroup>(), renderers);
        }

        static void Near(float[] expected, LODGroup group)
        {
            var actual = new[] { group.localReferencePoint.x, group.localReferencePoint.y, group.localReferencePoint.z, group.size };
            for (int i = 0; i < 4; i++) Assert.True(Math.Abs(expected[i] - actual[i]) <= Tolerance, $"[{i}] {expected[i]:R} != {actual[i]:R}");
        }

        [Fact]
        public void ARotatedAndScaledGroupBoundsItsRenderersInItsOwnSpace()
        {
            // case.skewed: corners of the world boxes would give a size near 3.7, the local bounds 2.84.
            var (group, renderers) = Group(new Vector3(Q(7, 10), 0, Q(-11, 10)), new Vector3(0, 45, 20), new Vector3(Q(3, 1), 1, Q(1, 2)));
            group.SetLODs(renderers.Select((r, i) => new LOD(Q(6, 10) / (i + 1), new[] { r })).ToArray());
            group.RecalculateBounds();
            Near(new[] { 0.114831924f, 0.261441648f, 0.419438928f, 2.83711767f }, group);
        }

        [Fact]
        public void DisabledAndInactiveRenderersCountMissingOnesDoNot()
        {
            var (group, renderers) = Group(new Vector3(Q(1, 3), 0, 0), Vector3.zero, Vector3.one);
            group.SetLODs(renderers.Select((r, i) => new LOD(Q(6, 10) / (i + 1), new[] { r })).ToArray());
            renderers[1].enabled = false;
            group.RecalculateBounds();
            Near(new[] { 0.114831686f, 0.2614416f, 0.419439375f, 2.837117f }, group);
            renderers[1].enabled = true;
            renderers[2].gameObject.SetActive(false);
            group.RecalculateBounds();
            Near(new[] { 0.114831686f, 0.2614416f, 0.419439375f, 2.837117f }, group);
            renderers[2].gameObject.SetActive(true);
            // SetLODs recalculates (counted.setlods), a null entry is skipped.
            group.size = 7;
            group.localReferencePoint = new Vector3(1, 2, 3);
            group.SetLODs(new[] { new LOD(Q(6, 10), new[] { renderers[0], null, renderers[1] }) });
            Near(new[] { 0.114831686f, 0.8744644f, -0.054659307f, 2.56616974f }, group);
            // Without renderers: the point at zero and size 1 (counted.none).
            group.SetLODs(new[] { new LOD(Q(6, 10), new Renderer[0]) });
            group.size = 7;
            group.localReferencePoint = new Vector3(1, 2, 3);
            group.RecalculateBounds();
            Near(new[] { 0f, 0f, 0f, 1f }, group);
        }

        /// <summary>Another renderer kind is not measured: its world bounds are boxed (a skinned
        /// renderer's local bounds are its root bone's).</summary>
        [Fact]
        public void ASkinnedRendererCountsWithItsWorldBounds()
        {
            var root = new GameObject("LodGroupBoundsTests Skinned");
            m_Owned.Add(root);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Owned.Add(cube);
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var hips = new GameObject("Hips");
            hips.transform.SetParent(root.transform, false);
            hips.transform.localPosition = new Vector3(0, 1, 0);
            var skinned = body.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            skinned.rootBone = hips.transform;
            skinned.localBounds = new Bounds(Vector3.zero, new Vector3(0.6f, 1.8f, 0.4f));
            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.5f, new Renderer[] { skinned }) });
            var world = skinned.bounds;
            Assert.Equal(world.center, group.localReferencePoint);
            Assert.Equal(2f * Mathf.Max(world.extents.x, Mathf.Max(world.extents.y, world.extents.z)), group.size);
        }

        [Fact]
        public void ACopiedGroupKeepsItsBounds()
        {
            var (group, renderers) = Group(Vector3.zero, Vector3.zero, Vector3.one);
            group.SetLODs(new[] { new LOD(Q(6, 10), renderers) });
            group.size = 7;
            group.localReferencePoint = new Vector3(1, 2, 3);
            var copy = Object.Instantiate(group.gameObject).GetComponent<LODGroup>();
            m_Owned.Add(copy.gameObject);
            Assert.Equal(7f, copy.size);
            Assert.Equal(new Vector3(1, 2, 3), copy.localReferencePoint);
        }
    }
}
