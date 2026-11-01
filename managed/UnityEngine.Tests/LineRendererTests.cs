using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Line and trail strips. Trails age with the global clock and frame count, so this
    /// shares the frame-stepping collection.</summary>
    [Collection("Frames")]
    public class LineRendererTests
    {
        static void Frame(float seconds)
        {
            Time.s_FrameCount++;
            Time.s_Time += seconds;
        }

        [Fact]
        public void ALineIsAStripOfItsWidth()
        {
            var line = new GameObject("line").AddComponent<LineRenderer>();
            line.alignment = LineAlignment.TransformZ;
            line.positionCount = 3;
            line.SetPositions(new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f) });
            line.widthMultiplier = 0.5f;
            line.startColor = Color.red;
            line.endColor = Color.blue;
            var camera = new GameObject("camera").AddComponent<Camera>();
            var mesh = new Mesh();
            // Unity's BakeMesh needs a camera, even for a strip that does not face it (measured).
            Assert.Throws<System.ArgumentNullException>(() => line.BakeMesh(mesh, null));
            line.BakeMesh(mesh, camera);
            Assert.Equal(6, mesh.vertexCount);
            Assert.Equal(12, mesh.triangles.Length);
            Assert.Equal(0.5f, mesh.bounds.size.y, 4);
            Assert.Equal(2f, mesh.bounds.size.x, 4);
            Assert.Equal(Color.red, mesh.colors[0]);
            Assert.Equal(Color.blue, mesh.colors[5]);
            // Stretch: u runs 0..1 along the length.
            Assert.Equal(0.5f, mesh.uv[2].x, 4);
            Object.DestroyImmediate(line.gameObject);
            Object.DestroyImmediate(camera.gameObject);
        }

        /// <summary>Segments shorter than twice the width blend their directions: a chain along the
        /// line plus a look at the next segment, and joints that turn by only a few degrees ease
        /// their miter out (Unity 6000.0.71f1, tests/projects/lines blend.asym.30.r1.5_1.25 and
        /// blend.rev.Z.run.r1.25x3_1.25, width 0.4, View alignment, orthographic camera).</summary>
        [Theory]
        [InlineData(new[] { -0.5f, -0.4f, 0.1f, -0.4f, 0.5330126881599426f, -0.15f },
            new[] { -0.461263955f, -0.5962129f, -0.538736045f, -0.203787073f, 0.150479227f, -0.5938946f,
                0.049520772f, -0.2061054f, 0.597572744f, -0.33929342f, 0.468452632f, 0.0392933935f })]
        [InlineData(new[] { 1f, 0.1f, 1f, -0.4f, 0.5f, -0.4f, 0f, -0.4f, -0.5f, -0.4f },
            new[] { 0.8285014f, 0.202899158f, 1.17149854f, -0.00289914757f, 0.865540445f, -0.235369086f,
                1.1344595f, -0.5646309f, 0.453644425f, -0.204268366f, 0.5463556f, -0.5957316f,
                -0.0244478714f, -0.201498836f, 0.0244478714f, -0.5985012f, -0.5150504f, -0.2005671f,
                -0.484949619f, -0.599432945f })]
        public void ShortSegmentsBlendTheirDirections(float[] xy, float[] expected)
        {
            var points = new Vector3[xy.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector3(xy[2 * i], xy[2 * i + 1], 0f);
            var line = new GameObject("line").AddComponent<LineRenderer>();
            line.widthMultiplier = 0.4f;
            line.positionCount = points.Length;
            line.SetPositions(points);
            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            var mesh = new Mesh();
            line.BakeMesh(mesh, camera);
            var vertices = mesh.vertices;
            Assert.Equal(expected.Length / 2, vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.InRange(vertices[i].x, expected[2 * i] - 1e-5f, expected[2 * i] + 1e-5f);
                Assert.InRange(vertices[i].y, expected[2 * i + 1] - 1e-5f, expected[2 * i + 1] + 1e-5f);
            }
            Object.DestroyImmediate(line.gameObject);
            Object.DestroyImmediate(camera.gameObject);
        }

        static Mesh BakeFacing(System.Action<LineRenderer> configure, params Vector3[] points)
        {
            var line = new GameObject("line").AddComponent<LineRenderer>();
            line.alignment = LineAlignment.TransformZ;
            line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            line.widthMultiplier = 0.4f;
            line.positionCount = points.Length;
            line.SetPositions(points);
            configure?.Invoke(line);
            var camera = new GameObject("camera").AddComponent<Camera>();
            var mesh = new Mesh();
            line.BakeMesh(mesh, camera);
            Object.DestroyImmediate(line.gameObject);
            Object.DestroyImmediate(camera.gameObject);
            return mesh;
        }

        [Fact]
        public void ANegativeWidthKeepsItsHalfWidthAndTurnsItsCapsBack()
        {
            var mesh = BakeFacing(l => { l.widthMultiplier = -0.5f; l.numCornerVertices = 2; l.numCapVertices = 2; },
                new Vector3(-1f, -0.5f, 0f), new Vector3(1f, -0.5f, 0f), new Vector3(1f, 1f, 0f));
            var v = mesh.vertices;
            // Measured (line.width.negative): the start cap bulges over the strip...
            Unbound.Tests.Near.Equal(-0.783493638f, v[2].x, 1e-5f);
            // ...and the corner pivots half a width outside the turn, with the arc on the other side.
            Unbound.Tests.Near.Equal(1.1767776f, v[7].x, 1e-5f);
            Unbound.Tests.Near.Equal(-0.6767758f, v[7].y, 1e-5f);
            Unbound.Tests.Near.Equal(0.6767776f, v[12].x, 1e-5f);
            Unbound.Tests.Near.Equal(0.783493638f, v[17].y, 1e-5f);
        }

        [Fact]
        public void LocalLinesFollowTheirTransformAndCapsRoundTheEnds()
        {
            var go = new GameObject("line");
            go.transform.position = new Vector3(10f, 0f, 0f);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.TransformZ;
            line.numCapVertices = 4;
            line.SetPositions(new[] { Vector3.zero, new Vector3(0f, 2f, 0f) });
            var camera = new GameObject("camera").AddComponent<Camera>();
            var mesh = new Mesh();
            // A local-space line bakes in its own space (measured).
            line.BakeMesh(mesh, camera);
            Assert.Equal(0f, mesh.bounds.center.x, 4);
            // Two caps of a center and an arc of the four cap vertices between the strip's two end vertices.
            Assert.Equal(4 + 2 * 7, mesh.vertexCount);
            // The caps reach about half a width beyond each end (a polygon short of the full radius).
            Assert.InRange(mesh.bounds.size.y, 2.9f, 3f);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(camera.gameObject);
        }

        [Fact]
        public void TrailsRecordMovementAndForgetOldPoints()
        {
            var go = new GameObject("trail");
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 1f;
            trail.minVertexDistance = 0.5f;
            // Nothing until the object moves; the first move records where it was and where it is (measured).
            Frame(0.1f);
            Assert.Equal(0, trail.positionCount);
            for (int i = 1; i < 5; i++)
            {
                go.transform.position = new Vector3(i, 0f, 0f);
                Frame(0.1f);
                Assert.Equal(i + 1, trail.positionCount);
            }
            // Under the minimum distance: no new point.
            go.transform.position = new Vector3(4.2f, 0f, 0f);
            Frame(0.1f);
            Assert.Equal(5, trail.positionCount);
            Assert.Equal(new Vector3(0f, 0f, 0f), trail.GetPosition(0));
            // Points age in the per-frame update: after the trail's time they are all gone.
            Frame(2f);
            Assert.Equal(5, trail.positionCount);
            trail.UpdateTrail();
            Assert.Equal(0, trail.positionCount);
            go.transform.position = new Vector3(6f, 0f, 0f);
            Assert.Equal(2, trail.positionCount);
            trail.Clear();
            Assert.Equal(0, trail.positionCount);
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void LineUtilityDropsPointsWithinTolerance()
        {
            var points = new System.Collections.Generic.List<Vector3> { Vector3.zero, new Vector3(1f, 0.01f, 0f), new Vector3(2f, 0f, 0f), new Vector3(3f, 1f, 0f) };
            var kept = new System.Collections.Generic.List<int>();
            LineUtility.Simplify(points, 0.1f, kept);
            Assert.Equal(new[] { 0, 2, 3 }, kept);
        }
    }
}
