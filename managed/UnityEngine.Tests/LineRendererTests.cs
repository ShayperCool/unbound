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

        /// <summary>A negative width's caps turn back over the strip: both half discs, with the
        /// exact measured positions of their centre and arc (Unity 6000.0.71f1, tests/projects/lines
        /// line.width.negative, width multiplier -0.5, two corner and two cap vertices, View
        /// alignment). Only the caps are checked: the negative-width corner stays open.</summary>
        [Fact]
        public void ANegativeWidthTurnsItsCapsBackOverTheStrip()
        {
            var line = new GameObject("line").AddComponent<LineRenderer>();
            line.widthMultiplier = -0.5f;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.positionCount = 3;
            line.SetPositions(new[] { new Vector3(-1f, -0.5f, 0f), new Vector3(1f, -0.5f, 0f), new Vector3(1f, 1f, 0f) });
            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            var mesh = new Mesh();
            line.BakeMesh(mesh, camera);
            var v = mesh.vertices;
            // The reference's first five vertices (start cap) and last five (end cap).
            var start = new[] { -1f, -0.5f, -1f, -0.75f, -0.783493638f, -0.625f, -0.783493638f, -0.375f, -1f, -0.25f };
            var end = new[] { 1f, 1f, 1.25f, 1f, 1.125f, 0.783493638f, 0.875f, 0.783493638f, 0.75f, 1f };
            Assert.True(v.Length >= 10);
            for (int i = 0; i < 5; i++)
            {
                Assert.InRange(v[i].x, start[2 * i] - 1e-5f, start[2 * i] + 1e-5f);
                Assert.InRange(v[i].y, start[2 * i + 1] - 1e-5f, start[2 * i + 1] + 1e-5f);
                var e = v[v.Length - 5 + i];
                Assert.InRange(e.x, end[2 * i] - 1e-5f, end[2 * i] + 1e-5f);
                Assert.InRange(e.y, end[2 * i + 1] - 1e-5f, end[2 * i + 1] + 1e-5f);
            }
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
            // A still trail ages when a camera's render collects it, not in the per-frame
            // movement update: after the trail's time they are all gone.
            Frame(2f);
            Assert.Equal(5, trail.positionCount);
            trail.UpdateTrail();
            Assert.Equal(5, trail.positionCount);
            trail.AgeForRender();
            Assert.Equal(0, trail.positionCount);
            go.transform.position = new Vector3(6f, 0f, 0f);
            Assert.Equal(2, trail.positionCount);
            trail.Clear();
            Assert.Equal(0, trail.positionCount);
            Object.DestroyImmediate(go);
        }

        /// <summary>Ages <paramref name="trail"/> at its render after each step and checks it keeps
        /// exactly the points the one-at-a-time rule keeps: drop the oldest while the next one is
        /// older than the trail's time, then a last point that is.</summary>
        static void CheckPrefixExpiry(TrailRenderer trail, System.Collections.Generic.List<Vector3> positions,
            System.Collections.Generic.List<float> times, float[] steps)
        {
            Assert.Equal(positions.Count, trail.positionCount);
            foreach (float step in steps)
            {
                Frame(step);
                trail.AgeForRender();
                float now = Time.time;
                while (positions.Count >= 2 && now - times[1] > trail.time)
                {
                    positions.RemoveAt(0);
                    times.RemoveAt(0);
                }
                if (positions.Count == 1 && now - times[0] > trail.time)
                {
                    positions.Clear();
                    times.Clear();
                }
                Assert.Equal(positions.Count, trail.positionCount);
                for (int i = 0; i < positions.Count; i++) Assert.Equal(positions[i], trail.GetPosition(i));
            }
        }

        /// <summary>A still trail with <paramref name="count"/> points added one at a time, a frame
        /// apart, and the times AddPosition stamps them with (half way from the newest point's time
        /// to now for one point).</summary>
        static TrailRenderer AddedTrail(int count, System.Func<int, float> frame,
            System.Collections.Generic.List<Vector3> positions, System.Collections.Generic.List<float> times)
        {
            var trail = new GameObject("trail").AddComponent<TrailRenderer>();
            trail.time = 1f;
            trail.AddPosition(Vector3.zero);
            positions.Add(Vector3.zero);
            times.Add(Time.time);
            for (int k = 1; k < count; k++)
            {
                Frame(frame(k));
                var p = new Vector3(k, 0f, 0f);
                trail.AddPosition(p);
                float now = Time.time, from = times[times.Count - 1];
                positions.Add(p);
                times.Add(from + (now - from) * 1 / 2);
            }
            return trail;
        }

        /// <summary>Expiring many points at once keeps exactly the points the one-at-a-time rule
        /// kept.</summary>
        [Fact]
        public void TrailsExpireAPrefixAsTheOneAtATimeRuleDid()
        {
            var positions = new System.Collections.Generic.List<Vector3>();
            var times = new System.Collections.Generic.List<float>();
            var trail = AddedTrail(41, k => k % 3 == 0 ? 0.07f : 0.03f, positions, times);
            CheckPrefixExpiry(trail, positions, times, new[] { 1.13f, 0.41f, 5f });
            Assert.Equal(0, trail.positionCount);
            Object.DestroyImmediate(trail.gameObject);
        }

        /// <summary>A long trail whose thousands of points expire in a few renders, most of them in
        /// one long prefix, keeps exactly the points the one-at-a-time rule kept.</summary>
        [Fact]
        public void ALongTrailExpiresItsLongPrefixAsTheOneAtATimeRuleDid()
        {
            var positions = new System.Collections.Generic.List<Vector3>();
            var times = new System.Collections.Generic.List<float>();
            var trail = AddedTrail(3000, k => k % 7 == 0 ? 0.0011f : 0.0004f, positions, times);
            CheckPrefixExpiry(trail, positions, times, new[] { 0.05f, 0.6f, 0.001f, 0.3f, 5f });
            Assert.Equal(0, trail.positionCount);
            Object.DestroyImmediate(trail.gameObject);
        }

        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        static readonly System.Reflection.FieldInfo s_Dir = typeof(LineStrip).GetField("m_Dir", Private);
        static readonly System.Reflection.FieldInfo s_Zero = typeof(LineStrip).GetField("m_Zero", Private);
        static readonly System.Reflection.FieldInfo s_Effective = typeof(LineStrip).GetField("m_Effective", Private);
        static readonly System.Reflection.MethodInfo s_LoopDirections = typeof(LineStrip).GetMethod("LoopDirections", Private);

        /// <summary>Runs a loop's direction pass over the zero pattern and checks it against the
        /// per-segment forward scan: a zero-length segment takes the next non-zero segment's
        /// direction around the loop, all zero keeps the raw directions. The scratch arrays are
        /// grown, not trimmed, so they keep stale entries past the segments, which stay as they are.</summary>
        static void CheckLoopDirections(bool[] zeroPattern)
        {
            Assert.NotNull(s_Dir);
            Assert.NotNull(s_Zero);
            Assert.NotNull(s_Effective);
            Assert.NotNull(s_LoopDirections);
            int segments = zeroPattern.Length;
            var dir = new Vector3[segments + 2];
            var zero = new bool[segments + 2];
            var effective = new Vector3[segments + 2];
            var stale = new Vector3(-99f, -99f, -99f);
            for (int j = 0; j < dir.Length; j++)
            {
                dir[j] = new Vector3(j + 1, 0.5f * j, -j);
                effective[j] = stale;
            }
            for (int j = 0; j < segments; j++) zero[j] = zeroPattern[j];
            var expected = new Vector3[segments];
            for (int j = 0; j < segments; j++)
            {
                expected[j] = dir[j];
                if (!zero[j]) continue;
                for (int k = 1; k < segments; k++)
                {
                    int at = (j + k) % segments;
                    if (!zero[at]) { expected[j] = dir[at]; break; }
                }
            }
            var strip = new LineStrip();
            s_Dir.SetValue(strip, dir);
            s_Zero.SetValue(strip, zero);
            s_Effective.SetValue(strip, effective);
            s_LoopDirections.Invoke(strip, new object[] { segments });
            for (int j = 0; j < segments; j++) Assert.Equal(expected[j], effective[j]);
            for (int j = segments; j < effective.Length; j++) Assert.Equal(stale, effective[j]);
        }

        /// <summary>Every zero pattern up to nine segments (runs at the start, the end and across
        /// the wrap, and all zero).</summary>
        [Fact]
        public void LoopZeroRunsTakeTheNextNonZeroDirectionAsTheScanDid()
        {
            for (int segments = 1; segments <= 9; segments++)
                for (int mask = 0; mask < 1 << segments; mask++)
                {
                    var pattern = new bool[segments];
                    for (int j = 0; j < segments; j++) pattern[j] = (mask & (1 << j)) != 0;
                    CheckLoopDirections(pattern);
                }
        }

        /// <summary>Long duplicate runs in a closed loop: one non-zero segment among thousands
        /// (first, last, middle), runs across the wrap, alternating long runs, and all zero.</summary>
        [Fact]
        public void LongLoopZeroRunsTakeTheNextNonZeroDirectionAsTheScanDid()
        {
            foreach (int segments in new[] { 600, 2001 })
            {
                foreach (int only in new[] { 0, 1, segments / 2, segments - 2, segments - 1 })
                {
                    var single = new bool[segments];
                    for (int j = 0; j < segments; j++) single[j] = j != only;
                    CheckLoopDirections(single);
                }
                // A zero run across the wrap: the last third and the first third.
                var wrap = new bool[segments];
                for (int j = 0; j < segments; j++) wrap[j] = j < segments / 3 || j >= segments - segments / 3;
                CheckLoopDirections(wrap);
                // Long zero runs of growing length between single non-zero segments.
                var runs = new bool[segments];
                for (int j = 0, run = 1; j < segments; run++)
                {
                    for (int k = 0; k < run && j < segments; k++) runs[j++] = true;
                    if (j < segments) runs[j++] = false;
                }
                CheckLoopDirections(runs);
                CheckLoopDirections(new bool[segments]);
                var all = new bool[segments];
                for (int j = 0; j < segments; j++) all[j] = true;
                CheckLoopDirections(all);
            }
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
