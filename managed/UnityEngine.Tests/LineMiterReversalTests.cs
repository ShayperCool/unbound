using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public class LineMiterReversalTests
    {
        // Joint offsets from the independent 6000.0.71f1 miterrev reference.
        // Both renderers have nonzero width; corners preserve the same inner joint.
        [Theory]
        [InlineData(172f, 0.598457158f, -0.0430008955f)]
        [InlineData(176f, 0.593928158f, -0.08514309f)]
        [InlineData(179f, 0.3490491f, -0.199999988f)]
        [InlineData(179.9f, 0.03490748f, -0.199999988f)]
        [InlineData(180f, 0f, -0.199999988f)]
        [InlineData(181f, -0.3490478f, -0.199999988f)]
        public void ReversalJointsMatchMeasuredLineAndEmittingTrail(float angle, float x, float y)
        {
            string previous = ProjectInfo.UnityVersion;
            var camera = new GameObject("camera").AddComponent<Camera>();
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                camera.orthographic = true;
                camera.transform.position = new Vector3(0, 0, -10);
                float a = angle * Mathf.Deg2Rad;
                var points = new[] { Vector3.left, Vector3.zero,
                    angle == 180f ? Vector3.left : new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) };
                foreach (bool trail in new[] { false, true })
                    foreach (int corners in new[] { 0, 2 })
                    {
                        var go = new GameObject("strip");
                        var mesh = new Mesh();
                        try
                        {
                            if (trail)
                            {
                                go.transform.position = points[0];
                                var r = go.AddComponent<TrailRenderer>();
                                r.emitting = true;
                                r.time = 10000f;
                                r.widthMultiplier = 0.4f;
                                r.numCornerVertices = corners;
                                r.Clear();
                                r.AddPositions(new[] { points[2], points[1] });
                                r.BakeMesh(mesh, camera);
                            }
                            else
                            {
                                var r = go.AddComponent<LineRenderer>();
                                r.widthMultiplier = 0.4f;
                                r.numCornerVertices = corners;
                                r.positionCount = points.Length;
                                r.SetPositions(points);
                                r.BakeMesh(mesh, camera);
                            }
                            int at = corners > 0 && angle < 180f ? 3 : 2;
                            float sign = at == 3 ? -1f : 1f;
                            Assert.InRange(mesh.vertices[at].x, sign*x - 1e-5f, sign*x + 1e-5f);
                            Assert.InRange(mesh.vertices[at].y, sign*y - 1e-5f, sign*y + 1e-5f);
                            Assert.InRange(mesh.vertices[at].z, -1e-5f, 1e-5f);
                        }
                        finally
                        {
                            Object.DestroyImmediate(mesh);
                            Object.DestroyImmediate(go);
                        }
                    }
            }
            finally
            {
                ProjectInfo.UnityVersion = previous;
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Fact]
        public void ReachUsesEarlierSegmentAcrossAnglesAndMultipleReversals()
        {
            string previous = ProjectInfo.UnityVersion;
            var go = new GameObject("line");
            var camera = new GameObject("camera").AddComponent<Camera>();
            var mesh = new Mesh();
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                camera.transform.position = new Vector3(0, 0, -10);
                var line = go.AddComponent<LineRenderer>();
                line.widthMultiplier = 0.3f;
                // The shortPrefix controls clamp even mild turns, while lagVsMinimum
                // distinguishes the earlier segment from the minimum adjacent length.
                var controls = new[]
                {
                    (new[] { new Vector3(-0.01f, 0), Vector3.zero, Vector3.right, new Vector3(1, 1) },
                        4, new Vector3(1.10606658f, -0.106065489f)),
                    (new[] { new Vector3(-0.01f, 0), Vector3.zero, Vector3.right, new Vector3(0.23395556f, 0.6427876f) },
                        4, new Vector3(1.1409539f, -0.0513030179f)),
                    (new[] { new Vector3(-0.01f, 0), Vector3.zero, Vector3.right, new Vector3(1.9848077f, 0.17364818f) },
                        4, new Vector3(1.01307344f, -0.1494292f)),
                    (new[] { new Vector3(-0.01f, 0), Vector3.zero, new Vector3(-1, 0.05f),
                        new Vector3(-0.5f, 0.075f), new Vector3(-1.5f, 0.1f) },
                        6, new Vector3(-0.0501472056f, 0.0634899139f)),
                };
                foreach (var (points, at, expected) in controls)
                {
                    line.positionCount = points.Length;
                    line.SetPositions(points);
                    line.BakeMesh(mesh, camera);
                    for (int axis = 0; axis < 3; axis++)
                        Assert.InRange(mesh.vertices[at][axis], expected[axis] - 1e-5f, expected[axis] + 1e-5f);
                }
            }
            finally
            {
                ProjectInfo.UnityVersion = previous;
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void UnmeasuredReleaseRetainsExistingReversalBisector()
        {
            string previous = ProjectInfo.UnityVersion;
            var go = new GameObject("line");
            var camera = new GameObject("camera").AddComponent<Camera>();
            var mesh = new Mesh();
            try
            {
                ProjectInfo.UnityVersion = "6000.0.72f1";
                var line = go.AddComponent<LineRenderer>();
                line.widthMultiplier = 0.4f;
                line.positionCount = 3;
                float a = 179f * Mathf.Deg2Rad;
                line.SetPositions(new[] { Vector3.left, Vector3.zero, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) });
                camera.transform.position = new Vector3(0, 0, -10);
                line.BakeMesh(mesh, camera);
                // Preservation check only, not an oracle for this unmeasured release.
                Assert.InRange(mesh.vertices[2].magnitude, 0.59999f, 0.60001f);
            }
            finally
            {
                ProjectInfo.UnityVersion = previous;
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(go);
            }
        }
    }
}
