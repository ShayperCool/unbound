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
        public void ANearReversalSlidesItsMiterAlongTheIncomingEdge()
        {
            float a = 179f * Mathf.Deg2Rad;
            var mesh = BakeFacing(null, new Vector3(-1f, 0f, 0f), Vector3.zero, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            // Measured (line.miter.179.w0.4): half the width across, h sin(turn) / 0.01 along.
            Assert.Equal(0.3490491f, mesh.vertices[2].x, 4);
            Assert.Equal(-0.2f, mesh.vertices[2].y, 4);
            // A 170-degree turn still takes the miter at three half widths.
            a = 170f * Mathf.Deg2Rad;
            mesh = BakeFacing(null, new Vector3(-1f, 0f, 0f), Vector3.zero, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            Assert.Equal(0.5977168f, mesh.vertices[2].x, 4);
            Assert.Equal(-0.0522935f, mesh.vertices[2].y, 4);
        }

        [Fact]
        public void ANegativeWidthKeepsItsHalfWidthAndTurnsItsCapsBack()
        {
            var mesh = BakeFacing(l => { l.widthMultiplier = -0.5f; l.numCornerVertices = 2; l.numCapVertices = 2; },
                new Vector3(-1f, -0.5f, 0f), new Vector3(1f, -0.5f, 0f), new Vector3(1f, 1f, 0f));
            var v = mesh.vertices;
            // Measured (line.width.negative): the start cap bulges over the strip...
            Assert.Equal(-0.7835f, v[2].x, 3);
            // ...and the corner pivots half a width outside the turn, with the arc on the other side.
            Assert.Equal(1.1768f, v[7].x, 3);
            Assert.Equal(-0.6768f, v[7].y, 3);
            Assert.Equal(0.6768f, v[12].x, 3);
            Assert.Equal(0.7835f, v[17].y, 3);
        }

        [Fact]
        public void PerceptualGradientsTakeTheirEndKeysThroughOklab()
        {
            var g = new Gradient { mode = GradientMode.PerceptualBlend };
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.2f, 0.1f), 0f), new GradientColorKey(new Color(0.2f, 0.3f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            // Measured (line.color.PerceptualBlend): the gamma round trip lands just under the key,
            // one step low at the line's 8 bits (rounded half up).
            int Byte(float value) => (int)(value * 255f + 0.5f);
            Assert.Equal(25, Byte(g.Evaluate(0f).b));
            Assert.Equal(76, Byte(g.Evaluate(1f).g));
            g.colorSpace = ColorSpace.Linear;
            Assert.Equal(26, Byte(g.Evaluate(0f).b));
            Assert.Equal(77, Byte(g.Evaluate(1f).g));
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
