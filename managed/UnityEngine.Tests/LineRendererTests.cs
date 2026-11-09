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

        /// <summary>The authored tilted TransformZ image uses the object's forward for rendering,
        /// while its BakeMesh reference stays in XY. These render vertices are an offline replay
        /// whose raster matches the saved Native image at the original 2/255 threshold, not a new
        /// Native mesh capture. See docs/LINE_GEOMETRY_ATTRIBUTION.md.</summary>
        [Fact]
        public void WorldTransformZLineRendersInItsTransformPlaneAndKeepsItsBakePlane()
        {
            var go = new GameObject("line");
            var camera = new GameObject("camera").AddComponent<Camera>();
            var baked = new Mesh();
            try
            {
                camera.transform.position = new Vector3(0f, 0f, -10f);
                var line = go.AddComponent<LineRenderer>();
                line.alignment = LineAlignment.TransformZ;
                line.widthMultiplier = 0.4f;
                line.numCornerVertices = 2;
                line.numCapVertices = 2;
                line.positionCount = 3;
                line.SetPositions(new[] { new Vector3(-1f, -0.5f, 0f), new Vector3(1f, -0.5f, 0f), new Vector3(1f, 1f, 0f) });
                go.transform.rotation = Quaternion.Euler(30f, 20f, 0f);
                line.BakeMesh(baked, camera);
                var before = baked.vertices;
                Assert.Equal(20, before.Length);
                foreach (var v in before) Assert.InRange(v.z, -1e-5f, 1e-5f);

                // No material is needed to inspect the collection mesh, so this test needs no GPU.
                line.CollectDraws(new Unbound.Runtime.FrameBuilder.CameraContext
                {
                    Camera = camera, Position = camera.transform.position, Forward = Vector3.forward,
                }, new System.Collections.Generic.List<Unbound.Runtime.FrameBuilder.DrawItem>());
                var meshes = (System.Collections.Generic.Dictionary<Camera, Mesh>)typeof(LineRenderer)
                    .GetField("m_Meshes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(line);
                float[] expected = {
                    -1f, -0.5f, 0f, -1f, -0.329593742f, 0.104698171f, -1.16543277f, -0.441653534f, 0.0960608654f,
                    -1.16543277f, -0.612059792f, -0.00863730605f, -1f, -0.670406258f, -0.104698171f, -1f, -0.670406258f, -0.104698171f,
                    -1f, -0.329593742f, 0.104698171f, 0.840600981f, -0.696283391f, -0.0625806696f, 0.840600981f, -0.355470875f, 0.146815673f,
                    1.08537523f, -0.57741081f, -0.0786355013f, 0.840600981f, -0.355470875f, 0.146815673f, 1.21647803f, -0.355470875f, 0.010007616f,
                    0.840600981f, -0.355470875f, 0.146815673f, 1.18793852f, 1f, -0.0684040287f, 0.812061476f, 1f, 0.0684040287f,
                    1f, 1f, 0f, 0.812061476f, 1f, 0.0684040287f, 0.935650551f, 1.15f, 0.115581782f,
                    1.12358908f, 1.15f, 0.0471777538f, 1.18793852f, 1f, -0.0684040287f,
                };
                // A drawn strip's mesh is streamed: its arrays keep a capacity, the strip is the
                // streamed count.
                var drawn = meshes[camera];
                var rendered = new System.ArraySegment<Vector3>(drawn.m_Vertices, 0, drawn.m_StreamVertexCount).ToArray();
                Assert.Equal(expected.Length / 3, rendered.Length);
                for (int i = 0; i < rendered.Length; i++)
                    for (int axis = 0; axis < 3; axis++)
                        Assert.InRange(rendered[i][axis], expected[3 * i + axis] - 1e-5f, expected[3 * i + axis] + 1e-5f);
                line.BakeMesh(baked, camera);
                Assert.Equal(before, baked.vertices);
            }
            finally
            {
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>The saved first round trail has a repeated head. Length-normalized corner
        /// directions give zero turn, so its corner pairs coincide while every tangent component
        /// stays constant (Native trail.f1.round.mid.mesh and trail.f1.round.end.mesh).</summary>
        [Fact]
        public void CoincidentCornerDirectionsKeepAConstantTangent()
        {
            var go = new GameObject("line");
            var camera = new GameObject("camera").AddComponent<Camera>();
            var mesh = new Mesh();
            try
            {
                camera.transform.position = new Vector3(0f, 0f, -10f);
                var line = go.AddComponent<LineRenderer>();
                line.widthMultiplier = 0.3f;
                line.numCornerVertices = 2;
                line.numCapVertices = 2;
                line.generateLightingData = true;
                line.positionCount = 3;
                line.SetPositions(new[] { new Vector3(-2.3f, 0.325857f, 0f),
                    new Vector3(-2.3f, 0.325857f, 0f), new Vector3(-2.5f, 0.1f, 0f) });
                line.BakeMesh(mesh, camera);
                Assert.Equal(20, mesh.vertexCount);
                for (int i = 7; i < 13; i++)
                {
                    var t = mesh.tangents[i];
                    Assert.InRange(t.x, -0.198885649f - 1e-5f, -0.198885649f + 1e-5f);
                    Assert.InRange(t.y, -0.224598646f - 1e-5f, -0.224598646f + 1e-5f);
                    Assert.Equal(0f, t.z);
                    Assert.Equal(1f, t.w);
                }
                Assert.InRange((mesh.vertices[7] - mesh.vertices[11]).magnitude, 0f, 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>All 26 Native tangent vertices, including the 78 spatial components
        /// that a direction-space tangent revert would lose. Unity 6000.0.71f1,
        /// line.light.persp.spatial.c2cap2, unchanged canonical reference.</summary>
        [Fact]
        public void SpatialCornerTangentsStayInTheCameraPlane()
        {
            var go = new GameObject("line");
            var camera = new GameObject("camera").AddComponent<Camera>();
            var mesh = new Mesh();
            try
            {
                camera.transform.position = new Vector3(1.5f, 1.2f, -5f);
                camera.transform.LookAt(new Vector3(0.3f, 0.2f, 0f));
                var line = go.AddComponent<LineRenderer>();
                line.widthMultiplier = 0.4f;
                line.numCornerVertices = 2;
                line.numCapVertices = 2;
                line.generateLightingData = true;
                line.positionCount = 4;
                line.SetPositions(new[] { new Vector3(-1f, -0.5f, 0f), new Vector3(0f, 0.5f, 1f),
                    new Vector3(1f, -0.3f, -0.5f), new Vector3(1.5f, 0.8f, 0.5f) });
                line.BakeMesh(mesh, camera);
                float[] expected = {
                    0.695946038f, 0.653468668f, 0.2977208f, 1f, 0.695946038f, 0.653468668f, 0.2977208f, 1f,
                    0.695946038f, 0.653468668f, 0.2977208f, 1f, 0.695946038f, 0.653468668f, 0.2977208f, 1f,
                    0.695946038f, 0.653468668f, 0.2977208f, 1f, 0.695946038f, 0.653468668f, 0.2977208f, 1f,
                    0.695946038f, 0.653468668f, 0.2977208f, 1f, 0.278378457f, 0.261387467f, 0.119088322f, 1f,
                    0.278378457f, 0.261387467f, 0.119088322f, 1f, 0.387433857f, -0.0568770319f, 0.08160872f, 1f,
                    0.387433857f, -0.0568770319f, 0.08160872f, 1f, 0.219013751f, -0.334406883f, -0.0143180788f, 1f,
                    0.219013751f, -0.334406883f, -0.0143180788f, 1f, 0.219013721f, -0.3344069f, -0.0143180825f, 1f,
                    0.219013721f, -0.3344069f, -0.0143180825f, 1f, 0.3875888f, 0.0195121076f, 0.09692373f, 1f,
                    0.3875888f, 0.0195121076f, 0.09692373f, 1f, 0.153916985f, 0.353181124f, 0.107576311f, 1f,
                    0.153916985f, 0.353181124f, 0.107576311f, 1f, 0.38479206f, 0.882952869f, 0.2689407f, 1f,
                    0.38479206f, 0.882952869f, 0.2689407f, 1f, 0.38479206f, 0.882952869f, 0.2689407f, 1f,
                    0.38479206f, 0.882952869f, 0.2689407f, 1f, 0.38479206f, 0.882952869f, 0.2689407f, 1f,
                    0.38479206f, 0.882952869f, 0.2689407f, 1f, 0.38479206f, 0.882952869f, 0.2689407f, 1f,
                };
                Assert.Equal(expected.Length / 4, mesh.vertexCount);
                for (int i = 0; i < mesh.vertexCount; i++)
                    for (int axis = 0; axis < 4; axis++)
                    {
                        float value = expected[4 * i + axis];
                        float tolerance = 1e-5f + 1e-5f * System.Math.Abs(value);
                        Assert.InRange(mesh.tangents[i][axis], value - tolerance, value + tolerance);
                    }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>The independent Native capcount pair uses identical public positions,
        /// transforms and positive configured width. Non-emitting trails have zero effective
        /// width, but keep their cap topology. Both cap counts reverse only for TransformZ
        /// trails. Local is Unity's string alias for TransformZ in the raw records.</summary>
        [Theory]
        [InlineData(false, false, 1, new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 5, 7, 6, 8, 9, 10, 8, 10, 11 })]
        [InlineData(false, false, 2, new[] { 0, 2, 1, 0, 3, 2, 0, 4, 3, 5, 6, 7, 6, 8, 7, 9, 10, 11, 9, 11, 12, 9, 12, 13 })]
        [InlineData(false, true, 1, new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 5, 7, 6, 8, 9, 10, 8, 10, 11 })]
        [InlineData(false, true, 2, new[] { 0, 2, 1, 0, 3, 2, 0, 4, 3, 5, 6, 7, 6, 8, 7, 9, 10, 11, 9, 11, 12, 9, 12, 13 })]
        [InlineData(true, false, 1, new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 5, 7, 6, 6, 7, 8, 7, 9, 8, 10, 11, 12, 10, 12, 13 })]
        [InlineData(true, false, 2, new[] { 0, 2, 1, 0, 3, 2, 0, 4, 3, 5, 6, 7, 6, 8, 7, 7, 8, 9, 8, 10, 9, 11, 12, 13, 11, 13, 14, 11, 14, 15 })]
        [InlineData(true, true, 1, new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 5, 7, 6, 6, 7, 8, 7, 9, 8, 10, 12, 11, 10, 13, 12 })]
        [InlineData(true, true, 2, new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 5, 6, 7, 6, 8, 7, 7, 8, 9, 8, 10, 9, 11, 13, 12, 11, 14, 13, 11, 15, 14 })]
        public void CapFansFollowRendererAlignmentAcrossCounts(bool trail, bool transformZ, int caps, int[] expected)
        {
            var go = new GameObject("strip");
            var camera = new GameObject("camera").AddComponent<Camera>();
            var mesh = new Mesh();
            var points = new[] { new Vector3(-1f, 0f, 0f), new Vector3(1f, 0.5f, 0f) };
            try
            {
                camera.transform.position = new Vector3(0f, 0f, -10f);
                var alignment = transformZ ? LineAlignment.TransformZ : LineAlignment.View;
                if (trail)
                {
                    var r = go.AddComponent<TrailRenderer>();
                    r.emitting = false;
                    r.autodestruct = false;
                    r.time = 10000f;
                    r.minVertexDistance = 0f;
                    r.widthMultiplier = 0.4f;
                    r.numCapVertices = caps;
                    r.alignment = alignment;
                    r.generateLightingData = true;
                    r.Clear();
                    r.AddPositions(points);
                    r.BakeMesh(mesh, camera);
                    // Native still creates all cap and strip vertices when emission hides width.
                    foreach (var v in mesh.vertices)
                        Assert.Contains(v, new[] { Vector3.zero, points[0], points[1] });
                }
                else
                {
                    var r = go.AddComponent<LineRenderer>();
                    r.widthMultiplier = 0.4f;
                    r.numCapVertices = caps;
                    r.alignment = alignment;
                    r.generateLightingData = true;
                    r.SetPositions(points);
                    r.BakeMesh(mesh, camera);
                    // The line's cap is outward for both alignments and both counts.
                    Assert.InRange(mesh.vertices[2].x, -1.1940285f - 1e-5f, -1.14378f + 1e-5f);
                }
                Assert.Equal(expected, mesh.triangles);
                Assert.Equal(mesh.vertexCount, mesh.normals.Length);
                Assert.Equal(mesh.vertexCount, mesh.tangents.Length);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>The old nonzero TransformZ trail establishes the inward cap plane.
        /// Manual points reproduce its first cap without stepping the clock. Its complete
        /// mesh remains in the offline full union; this test targets the displacement.</summary>
        [Fact]
        public void TransformZTrailCapBowsOverTheStrip()
        {
            var go = new GameObject("trail");
            var camera = new GameObject("camera").AddComponent<Camera>();
            var mesh = new Mesh();
            try
            {
                go.transform.position = new Vector3(-2.3f, -0.374143f, 0f);
                camera.transform.position = new Vector3(0f, 0f, -10f);
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 10000f;
                trail.autodestruct = false;
                trail.widthMultiplier = 0.3f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.25f));
                trail.numCornerVertices = 1;
                trail.numCapVertices = 1;
                trail.alignment = LineAlignment.TransformZ;
                trail.Clear();
                trail.AddPositions(new[] { new Vector3(-2.5f, -0.6f, 0f), go.transform.position });
                trail.BakeMesh(mesh, camera);
                var expected = new[] { new Vector3(-2.3f, -0.374143f, 0f),
                    new Vector3(-2.18770075f, -0.473585844f, 0f),
                    new Vector3(-2.39944267f, -0.486442268f, 0f),
                    new Vector3(-2.41229916f, -0.274700165f, 0f) };
                for (int i = 0; i < expected.Length; i++)
                    for (int axis = 0; axis < 3; axis++)
                        Assert.InRange(mesh.vertices[i][axis], expected[i][axis] - 1e-5f, expected[i][axis] + 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        static Mesh BakeLine(Vector3[] points, float width, int corners, LineTextureMode mode = LineTextureMode.Stretch, AnimationCurve curve = null)
        {
            var line = new GameObject("line").AddComponent<LineRenderer>();
            var camera = new GameObject("camera").AddComponent<Camera>();
            try
            {
                camera.orthographic = true;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                if (curve != null) line.widthCurve = curve;
                line.widthMultiplier = width;
                line.numCornerVertices = corners;
                line.textureMode = mode;
                line.positionCount = points.Length;
                line.SetPositions(points);
                var mesh = new Mesh();
                line.BakeMesh(mesh, camera);
                return mesh;
            }
            finally
            {
                Object.DestroyImmediate(line.gameObject);
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-5f)
        {
            for (int axis = 0; axis < 3; axis++)
                Assert.InRange(actual[axis], expected[axis] - tolerance, expected[axis] + tolerance);
        }

        /// <summary>The per-segment texture modes evaluate the width curve by point index, other
        /// modes by distance (Unity 6000.0.71f1, tests/projects/lines line.tex.*.zigzag.curve,
        /// pairs one and two; the per-segment trails show the same index rule for the gradient).</summary>
        [Theory]
        [InlineData(LineTextureMode.DistributePerSegment, new[] { -1.34018052f, 0.409776568f, -1.45981944f, 0.7902235f, -0.118935607f, -0.7140598f, -0.2810644f, -0.08594015f })]
        [InlineData(LineTextureMode.RepeatPerSegment, new[] { -1.34018052f, 0.409776568f, -1.45981944f, 0.7902235f, -0.118935607f, -0.7140598f, -0.2810644f, -0.08594015f })]
        [InlineData(LineTextureMode.Stretch, new[] { -1.34449029f, 0.4234814f, -1.45550966f, 0.776518643f, -0.120708525f, -0.707191348f, -0.279291481f, -0.09280869f })]
        public void PerSegmentModesPlaceWidthByPointIndex(LineTextureMode mode, float[] expected)
        {
            var zigzag = new[] { new Vector3(-2f, -0.5f, 0f), new Vector3(-1.4f, 0.6f, 0f), new Vector3(-0.2f, -0.4f, 0f), new Vector3(0.3f, 0.8f, 0f), new Vector3(1.8f, 0.1f, 0f) };
            var mesh = BakeLine(zigzag, 0.4f, 0, mode, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2f)));
            try
            {
                for (int i = 0; i < 4; i++)
                    Near(new Vector3(expected[2 * i], expected[2 * i + 1], 0f), mesh.vertices[2 + i]);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        /// <summary>Coincident corner sides keep a zero arc: the position angle divides the cosine
        /// by the side lengths, as the tangent angle does (Unity 6000.0.71f1, tests/projects/lines
        /// line.sweep.L.c2.w2 and line.dup.near1e-3.c2, all inner arc vertices).</summary>
        [Fact]
        public void CoincidentCornerSidesKeepAZeroArc()
        {
            var sweep = BakeLine(new[] { new Vector3(-1f, 0f, 0f), Vector3.zero, new Vector3(0f, 1f, 0f) }, 2f, 2);
            var near = BakeLine(new[] { new Vector3(-1f, 0f, 0f), Vector3.zero, new Vector3(0.001f, 0f, 0f), new Vector3(1f, 1f, 0f) }, 0.4f, 2);
            try
            {
                foreach (int i in new[] { 2, 4, 6 })
                    Near(new Vector3(0.7071067f, -0.707106769f, 0f), sweep.vertices[i]);
                foreach (int i in new[] { 2, 4, 6 })
                    Near(new Vector3(0.07658291f, -0.184756771f, 0f), near.vertices[i]);
            }
            finally
            {
                Object.DestroyImmediate(sweep);
                Object.DestroyImmediate(near);
            }
        }

        /// <summary>Only the miter eases a small turn; the corner arc ends on the un-eased outgoing
        /// side, parallel to the next point's pair (measured on every eased corner of the saved
        /// round and TransformZ trails, tests/projects/lines trail.*.round / transformZ).</summary>
        [Fact]
        public void CornerArcsEndOnTheUnEasedOutgoingSide()
        {
            // A 2.9 degree turn: its cosine is above the 0.99 easing threshold.
            var mesh = BakeLine(new[] { new Vector3(-1f, 0f, 0f), Vector3.zero, new Vector3(1f, 0.05f, 0f) }, 0.4f, 2);
            try
            {
                var v = mesh.vertices;
                Assert.Equal(10, v.Length);
                // The eased arc would miss that side by 0.044 (offline float32 replay).
                Near((v[9] - v[8]).normalized, (v[7] - v[6]).normalized, 1e-5f);
                // The arc's inner vertex is shared by the corner's three pairs.
                Assert.True(v[2] == v[4] && v[4] == v[6] || v[3] == v[5] && v[5] == v[7]);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        /// <summary>A TransformZ trail's opposite face swaps each corner's side: its corner block is
        /// the point reflection of the View trail's block through the point, with the fan on the
        /// other side (Unity 6000.0.71f1, tests/projects/lines trail.f*.transformZ, all 30 meshes;
        /// lines keep their side).</summary>
        [Fact]
        public void TransformZTrailCornersTurnOnTheOppositeSide()
        {
            var meshes = new Mesh[2];
            var camera = new GameObject("camera").AddComponent<Camera>();
            var corner = new Vector3(0f, 0.3f, 0f);
            try
            {
                camera.orthographic = true;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                for (int j = 0; j < 2; j++)
                {
                    var go = new GameObject("trail");
                    go.transform.position = new Vector3(1f, 0f, 0f);
                    var trail = go.AddComponent<TrailRenderer>();
                    trail.time = 10000f;
                    trail.autodestruct = false;
                    trail.minVertexDistance = 0f;
                    trail.widthMultiplier = 0.3f;
                    trail.numCornerVertices = 1;
                    trail.alignment = j == 0 ? LineAlignment.View : LineAlignment.TransformZ;
                    trail.Clear();
                    trail.AddPositions(new[] { new Vector3(-1f, 0f, 0f), corner });
                    meshes[j] = new Mesh();
                    trail.BakeMesh(meshes[j], camera);
                    Object.DestroyImmediate(go);
                }
                Vector3[] view = meshes[0].vertices, transformZ = meshes[1].vertices;
                Assert.Equal(8, view.Length);
                Assert.Equal(8, transformZ.Length);
                // Pairs: head, the corner's two (vertices 2..5), the oldest point.
                for (int i = 2; i < 6; i++)
                    Near(2f * corner - view[i ^ 1], transformZ[i]);
                Assert.NotEqual(meshes[0].triangles[6..9], meshes[1].triangles[6..9]);
            }
            finally
            {
                foreach (var m in meshes) if (m != null) Object.DestroyImmediate(m);
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>Native 6000.0.71f1 (tests/projects/trailautodestruct, lane "passive"): created at
        /// x=-1.5, moved 0.5 a frame before anything read or rendered it. positionCount is 1, 2, 3
        /// on the moves and the step-2 lane renders 1.0 unit long, so x=-1.5 is never a point;
        /// render collection then keeps 3, 2, 0 points at 0.08, 0.1 and 0.12 s.</summary>
        [Fact]
        public void TrailMovedBeforeAnythingLookedAtItSkipsTheEnablePosition()
        {
            var go = new GameObject("trail");
            go.transform.position = new Vector3(-1.5f, -0.25f, 0f);
            var clock = (Time.s_Time, Time.s_FrameCount, Time.s_InFixedTimeStep);
            try
            {
                (Time.s_Time, Time.s_FrameCount, Time.s_InFixedTimeStep) = (0d, 0, false);
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 0.05f;
                trail.minVertexDistance = 0.01f;
                var context = new Unbound.Runtime.FrameBuilder.CameraContext { Forward = Vector3.forward };
                var draws = new System.Collections.Generic.List<Unbound.Runtime.FrameBuilder.DrawItem>();
                for (int step = 0; step < 3; step++)
                {
                    Frame(0.02f);
                    go.transform.position = new Vector3(-1f + step * 0.5f, -0.25f, 0f);
                    Assert.Equal(step + 1, trail.positionCount);
                    trail.CollectDraws(context, draws);
                    Assert.Equal(step + 1, trail.positionCount);
                }
                Assert.Equal(new Vector3(-1f, -0.25f, 0f), trail.GetPosition(0));
                foreach (int expected in new[] { 3, 2, 0 })
                {
                    Frame(0.02f);
                    trail.CollectDraws(context, draws);
                    Assert.Equal(expected, trail.positionCount);
                }
            }
            finally
            {
                try { Object.DestroyImmediate(go); }
                finally { (Time.s_Time, Time.s_FrameCount, Time.s_InFixedTimeStep) = clock; }
            }
        }

        /// <summary>Native 6000.0.71f1 (tests/projects/lines, trail "clear" f6..f7): Clear after a
        /// move, then one more move, records the cleared-at position and the new one.</summary>
        [Fact]
        public void TrailMovedAfterClearRecordsTheClearedAtPosition()
        {
            var go = new GameObject("trail");
            var clock = (Time.s_Time, Time.s_FrameCount, Time.s_InFixedTimeStep);
            try
            {
                (Time.s_Time, Time.s_FrameCount, Time.s_InFixedTimeStep) = (0d, 0, false);
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 1f;
                trail.minVertexDistance = 0.05f;
                Frame(0.02f);
                go.transform.position = new Vector3(-1.3f, -0.677f, 0f);
                trail.Clear();
                Assert.Equal(0, trail.positionCount);
                Frame(0.02f);
                go.transform.position = new Vector3(-1.1f, -0.849f, 0f);
                Assert.Equal(2, trail.positionCount);
                Assert.Equal(new Vector3(-1.3f, -0.677f, 0f), trail.GetPosition(0));
                Assert.Equal(new Vector3(-1.1f, -0.849f, 0f), trail.GetPosition(1));
            }
            finally
            {
                try { Object.DestroyImmediate(go); }
                finally { (Time.s_Time, Time.s_FrameCount, Time.s_InFixedTimeStep) = clock; }
            }
        }

        [Fact]
        public void TrailsRecordMovementAndForgetOldPoints()
        {
            var go = new GameObject("trail");
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 1f;
            trail.minVertexDistance = 0.5f;
            // Nothing until the object moves; read unmoved, the first move records where it was and where it is (measured).
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
            // Still points age during camera collection, before its empty-strip return.
            Frame(2f);
            Assert.Equal(5, trail.positionCount);
            trail.UpdateTrail();
            Assert.Equal(5, trail.positionCount);
            trail.CollectDraws(new Unbound.Runtime.FrameBuilder.CameraContext { Forward = Vector3.forward },
                new System.Collections.Generic.List<Unbound.Runtime.FrameBuilder.DrawItem>());
            Assert.Equal(0, trail.positionCount);
            go.transform.position = new Vector3(6f, 0f, 0f);
            Assert.Equal(2, trail.positionCount);
            trail.Clear();
            Assert.Equal(0, trail.positionCount);
            Object.DestroyImmediate(go);
        }

        /// <summary>Expiring many points at once keeps exactly the points the one-at-a-time rule
        /// kept: drop the oldest while the next one is older than the trail's time, then a last
        /// point that is.</summary>
        [Fact]
        public void TrailsExpireAPrefixAsTheOneAtATimeRuleDid()
        {
            var go = new GameObject("trail");
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 1f;
            var positions = new System.Collections.Generic.List<Vector3>();
            var times = new System.Collections.Generic.List<float>();
            // AddPositions stamps a point between the newest point's time and now (half way for one).
            trail.AddPosition(Vector3.zero);
            positions.Add(Vector3.zero);
            times.Add(Time.time);
            for (int k = 1; k <= 40; k++)
            {
                Frame(k % 3 == 0 ? 0.07f : 0.03f);
                var p = new Vector3(k, 0f, 0f);
                trail.AddPosition(p);
                float now = Time.time, from = times[times.Count - 1];
                positions.Add(p);
                times.Add(from + (now - from) * 1 / 2);
            }
            Assert.Equal(positions.Count, trail.positionCount);
            foreach (float step in new[] { 1.13f, 0.41f, 5f })
            {
                Frame(step);
                // Exercise the prefix algorithm directly, independently of its render request site.
                typeof(TrailRenderer).GetMethod("Age", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(trail, new object[] { Time.time });
                float now = Time.time;
                while (positions.Count >= 2 && now - times[1] > 1f)
                {
                    positions.RemoveAt(0);
                    times.RemoveAt(0);
                }
                if (positions.Count == 1 && now - times[0] > 1f)
                {
                    positions.Clear();
                    times.Clear();
                }
                Assert.Equal(positions.Count, trail.positionCount);
                for (int i = 0; i < positions.Count; i++) Assert.Equal(positions[i], trail.GetPosition(i));
            }
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
