using System;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Particle geometry of stretched (freeform and plain) and mesh particles, as BakeMesh and
    /// BakeTexture return it. The 6000.0 Scripting API documents neither the normals and tangents
    /// of particle geometry nor how the emitter's scale combines with a particle's rotation; these
    /// are measurements of 6000.0.71f1 (tests/projects/particlerender freenorm/*, meshscale/*,
    /// meshindex/*).
    /// </summary>
    public class ParticleRendererScaledGeometryTests
    {
        static ParticleSystem Make(Action<ParticleSystem> configure = null)
        {
            var go = new GameObject("geometry");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            configure?.Invoke(ps);
            go.SetActive(true);
            return ps;
        }

        /// <summary>A camera at (0, 0, -10) looking down +Z.</summary>
        static ParticleSystemRenderer.View View() =>
            ParticleSystemRenderer.View.FromMatrix(Matrix4x4.TRS(new Vector3(0f, 0f, 10f), Quaternion.identity, new Vector3(1f, 1f, -1f)),
                new Vector3(0f, 0f, -10f), false, null);

        static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float rotation = 0f) =>
            ps.Emit(new ParticleSystem.EmitParams { position = position, velocity = velocity, startSize = size, rotation = rotation, startLifetime = 5f }, 1);

        static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
        {
            Assert.InRange((expected - actual).magnitude, 0f, tolerance);
        }

        [Theory]
        [InlineData(1f, 0f)]
        [InlineData(2f, 0f)]
        [InlineData(3f, 30f)]
        public void FreeformNormalsAndTangentsComeFromTheFirstTwoCorners(float lengthScale, float rotation)
        {
            var ps = Make();
            try
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.freeformStretching = true;
                renderer.lengthScale = lengthScale;
                renderer.velocityScale = 0f;
                Emit(ps, new Vector3(0.3f, -0.2f, 0f), new Vector3(2f, 0f, 0f), 0.4f, rotation);
                var geometry = new ParticleSystemRenderer.Geometry();
                Assert.True(geometry.Build(renderer, ps, View()));
                var p = new Vector3(0.3f, -0.2f, 0f);
                var c0 = geometry.Positions[0] - p; var c1 = geometry.Positions[1] - p;
                var normal = Vector3.Cross(c0.normalized, c1.normalized);
                for (int v = 0; v < 4; v++)
                {
                    AssertNear(normal, geometry.Normals[v]);
                    var t = geometry.Tangents[v];
                    AssertNear((c1 - c0).normalized, new Vector3(t.x, t.y, t.z));
                    Assert.Equal(-1f, t.w);
                }
                // Facing the camera; a stretched quad's normal is shorter than one (2k / (1 + k²)
                // for a square stretched k times along its side).
                Assert.True(normal.z < 0f);
                if (rotation == 0f) Assert.Equal(2f * lengthScale / (1f + lengthScale * lengthScale), normal.magnitude, 3);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void FreeformQuadsScaleWithTheEmitterAlongTheCameraAxes()
        {
            // A non-uniformly scaled emitter: the quad is scale.x times wider along the camera's
            // right (here the stretch direction) and scale.y times taller along its up; the stretch
            // length takes the x scale as well.
            var ps = Make(s => { var m = s.main; m.scalingMode = ParticleSystemScalingMode.Local; });
            try
            {
                ps.transform.localScale = new Vector3(2f, 1.5f, 1f);
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.freeformStretching = true;
                renderer.lengthScale = 2f;
                renderer.velocityScale = 0f;
                Emit(ps, Vector3.zero, new Vector3(1f, 0f, 0f), 0.3f);
                var geometry = new ParticleSystemRenderer.Geometry();
                Assert.True(geometry.Build(renderer, ps, View(), Matrix4x4.identity));
                // Along x: 0.3 × 2 (scale) × 2 × 2 (length scale × x scale); along y: 0.3 × 1.5.
                Assert.Equal(2.4f, Mathf.Abs(geometry.Positions[0].x - geometry.Positions[1].x), 3);
                Assert.Equal(0.45f, Mathf.Abs(geometry.Positions[0].y - geometry.Positions[3].y), 3);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void PlainStretchedQuadsTakeTheHeadToTailTangent()
        {
            var ps = Make();
            try
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 2f;
                renderer.velocityScale = 0f;
                Emit(ps, new Vector3(0.3f, -0.2f, 0f), new Vector3(0.3f, 0.2f, 3f), 0.25f);
                var geometry = new ParticleSystemRenderer.Geometry();
                Assert.True(geometry.Build(renderer, ps, View()));
                var t = geometry.Tangents[0];
                AssertNear(-new Vector3(0.3f, 0.2f, 3f).normalized, new Vector3(t.x, t.y, t.z));
                Assert.Equal(-1f, t.w);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        static Mesh Tetra()
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), Vector3.zero };
            mesh.normals = new[] { new Vector3(1f, 1f, 0f).normalized, Vector3.up, Vector3.forward, Vector3.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 3, 1 };
            return mesh;
        }

        [Theory]
        [InlineData(ParticleSystemRenderSpace.World)]
        [InlineData(ParticleSystemRenderSpace.Local)]
        public void MeshParticlesTurnBeforeTheEmitterScaleAndTakeInverseTransposeNormals(ParticleSystemRenderSpace alignment)
        {
            var mesh = Tetra();
            var ps = Make(s => { var m = s.main; m.scalingMode = ParticleSystemScalingMode.Local; m.startRotation3D = true; });
            try
            {
                ps.transform.localScale = new Vector3(2f, 1f, 1f);
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = mesh;
                renderer.alignment = alignment;
                ps.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, startSize = 1f, rotation3D = new Vector3(0f, 0f, 90f), startLifetime = 5f }, 1);
                var geometry = new ParticleSystemRenderer.Geometry();
                Assert.True(geometry.Build(renderer, ps, View(), Matrix4x4.identity));
                // The mesh's +X turns to +Y (a z rotation of 90°; meshes turn counterclockwise)
                // and is then not stretched by the emitter's x scale; its +Y turns to -X and is.
                AssertNear(new Vector3(0f, 1f, 0f), geometry.Positions[0]);
                AssertNear(new Vector3(-2f, 0f, 0f), geometry.Positions[1]);
                // Normals by the inverse transpose: (1, 1, 0) turns to (-1, 1, 0), then the x
                // scale halves its x.
                AssertNear(new Vector3(-0.5f, 1f, 0f).normalized, geometry.Normals[0]);
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Fact]
        public void AMirroringEmitterScaleKeepsMeshNormalsOutward()
        {
            var mesh = Tetra();
            var ps = Make(s => { var m = s.main; m.scalingMode = ParticleSystemScalingMode.Local; });
            try
            {
                ps.transform.localScale = new Vector3(-1f, 1f, 1f);
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = mesh;
                renderer.alignment = ParticleSystemRenderSpace.World;
                Emit(ps, Vector3.zero, Vector3.zero, 1f);
                var geometry = new ParticleSystemRenderer.Geometry();
                Assert.True(geometry.Build(renderer, ps, View(), Matrix4x4.identity));
                AssertNear(new Vector3(-1f, 0f, 0f), geometry.Positions[0]);
                // The vertex at the origin with a +X normal: mirrored, it faces -X.
                AssertNear(Vector3.left, geometry.Normals[3]);
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Theory]
        [InlineData(IndexFormat.UInt16, IndexFormat.UInt16)]
        [InlineData(IndexFormat.UInt32, IndexFormat.UInt32)]
        public void BakeMeshTakesThirtyTwoBitIndicesForAThirtyTwoBitParticleMesh(IndexFormat source, IndexFormat baked)
        {
            var mesh = Tetra();
            mesh.indexFormat = source;
            mesh.triangles = new[] { 0, 1, 2, 0, 3, 1 };
            var ps = Make();
            var cameraObject = new GameObject("bake-camera");
            var bakedMesh = new Mesh();
            try
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = mesh;
                Emit(ps, new Vector3(0f, 0f, 5f), Vector3.zero, 1f);
                renderer.BakeMesh(bakedMesh, cameraObject.AddComponent<Camera>(), ParticleSystemBakeMeshOptions.Default);
                Assert.Equal(baked, bakedMesh.indexFormat);
                Assert.Equal(new[] { 0, 1, 2, 0, 3, 1 }, bakedMesh.triangles);
            }
            finally
            {
                Object.DestroyImmediate(bakedMesh);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Theory]
        [InlineData("6000.0.71f1", true)]
        [InlineData("2022.3.10f1", false)]
        public void MeshBakeTexturesOfSixteenBitBakesHoldIndexPairs(string release, bool pairs)
        {
            string version = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = release;
            var mesh = Tetra();
            var ps = Make();
            var cameraObject = new GameObject("bake-camera");
            Texture2D vertices = null, indices = null;
            try
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = mesh;
                Emit(ps, new Vector3(0f, 0f, 5f), Vector3.zero, 1f);
                int n = renderer.BakeTexture(ref vertices, ref indices, cameraObject.AddComponent<Camera>(), ParticleSystemBakeTextureOptions.Default);
                Assert.Equal(6, n);
                var texels = indices.GetPixels();
                float[] expected = pairs
                    ? new float[] { 0 | 1 << 16, 2 | 0 << 16, 3 | 1 << 16, 0, 0, 0 }
                    : new float[] { 0, 1, 2, 0, 3, 1 };
                for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], texels[i].r);
            }
            finally
            {
                ProjectInfo.UnityVersion = version;
                if (vertices != null) Object.DestroyImmediate(vertices);
                if (indices != null) Object.DestroyImmediate(indices);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
