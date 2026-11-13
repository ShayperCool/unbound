using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A directional cascade's conservative culling sphere and caster culling planes
    /// (tests/projects/brg shadows compares both with the Editor through BatchRendererGroup light
    /// cullings), and the single-precision inverse their corners come from.</summary>
    public sealed class CascadeCullingTests : IDisposable
    {
        readonly GameObject m_Object = new GameObject("cascade camera");
        readonly Camera m_Camera;

        public CascadeCullingTests()
        {
            m_Camera = m_Object.AddComponent<Camera>();
            m_Object.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, -6f), Quaternion.Euler(10f, 0f, 0f));
            m_Camera.fieldOfView = 60f;
            m_Camera.aspect = 320f / 180f;
            m_Camera.nearClipPlane = 0.3f;
            m_Camera.farClipPlane = 1000f;
        }

        public void Dispose() => Object.DestroyImmediate(m_Object);

        ShadowSplitData Planes(Vector3 light, float from, float to, float cullingNear)
        {
            Assert.True(m_Camera.TryGetCullingParameters(out var parameters));
            parameters.shadowDistance = 50f;
            var sphere = CullingResults.ConservativeSliceSphere(m_Camera, from, to, 50f, 64);
            var split = new ShadowSplitData();
            CullingResults.CasterCullingPlanes(ref parameters, m_Camera, cullingNear, sphere, light.normalized, ref split);
            return split;
        }

        static void Near(float[] expected, Plane actual)
        {
            Assert.Equal(expected[0], actual.normal.x, 3);
            Assert.Equal(expected[1], actual.normal.y, 3);
            Assert.Equal(expected[2], actual.normal.z, 3);
            Assert.Equal(expected[3], actual.distance, 2);
        }

        [Fact]
        public void TheConservativeSphereIsTheEditors()
        {
            var first = CullingResults.ConservativeSliceSphere(m_Camera, 0f, 0.25f, 50f, 64);
            Assert.Equal(new Vector4(0.0085f, -0.7278f, 6.5276f, 15.003f), Round(first));
            var second = CullingResults.ConservativeSliceSphere(m_Camera, 0.25f, 1f, 50f, 64);
            Assert.Equal(new Vector4(0.0334f, -7.2547f, 43.2304f, 58.951f), Round(second));
        }

        static Vector4 Round(Vector4 v) =>
            new Vector4((float)Math.Round(v.x, 4), (float)Math.Round(v.y, 4), (float)Math.Round(v.z, 4), (float)Math.Round(v.w, 3));

        static string UrpReference([CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "..", "tests", "projects", "urp", "Reference", "scenarios", "conservative-shadow-matrices.jsonl"));

        /// <summary>Every conservative cascade the Editor recorded in tests/projects/urp (fields of
        /// view 30-90, aspects 0.5-1.8, near 0.3 and 2, pitches 0 and 45, distances 20-100, 64 and 8
        /// rounds, one and four cascades, a near plane offset of 3): the sphere and the
        /// projection's depth range.</summary>
        [Fact]
        public void EveryRecordedConservativeCascadeIsTheEditors()
        {
            int checkedRecords = 0;
            float worst = 0f;
            foreach (var line in File.ReadLines(UrpReference()))
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                if (!root.TryGetProperty("grid", out var grid)) continue;
                m_Camera.fieldOfView = grid[0].GetSingle();
                m_Camera.aspect = grid[1].GetSingle();
                m_Camera.nearClipPlane = grid[2].GetSingle();
                m_Object.transform.rotation = Quaternion.Euler(grid[3].GetSingle(), 0f, 0f);
                float distance = root.GetProperty("distance").GetSingle();
                int count = root.GetProperty("count").GetInt32(), split = root.GetProperty("split").GetInt32();
                var ratios = count == 1 ? new[] { 1f } : new[] { 0.067f, 0.2f, 0.467f, 1f };
                var sphere = CullingResults.ConservativeSliceSphere(m_Camera, split > 0 ? ratios[split - 1] : 0f, ratios[split], distance,
                    root.GetProperty("iterations").GetInt32());
                var expected = root.GetProperty("sphere");
                for (int i = 0; i < 4; i++)
                {
                    float e = expected[i].GetSingle(), error = Math.Abs(sphere[i] - e) / Math.Max(1f, Math.Abs(e));
                    worst = Math.Max(worst, error);
                }
                // The projection's depth range: 0.1 radii minus 30 (an offset of 3) to 2.3 radii.
                var proj = root.GetProperty("proj");
                float r = expected[3].GetSingle(), near = 0.1f * r - 30f, far = 2.3f * r;
                Assert.Equal(-2f / (far - near), proj[10].GetSingle(), 5);
                checkedRecords++;
            }
            Assert.Equal(1080, checkedRecords);
            Assert.True(worst < 2e-4f, $"worst relative difference {worst}");
        }

        [Fact]
        public void CasterPlanesKeepTheFacesAgainstTheLightThenTheSilhouette()
        {
            var light = new Vector3(-0.3214f, -0.766f, 0.5567f);
            var first = Planes(light, 0f, 0.25f, 0.3f);
            Assert.Equal(8, first.cullingPlaneCount);
            Near(new[] { 0f, 0.766f, 0.6428f, 2.7077f }, first.GetCullingPlane(0));
            Near(new[] { 0f, 0.1736f, -0.9848f, 21.5578f }, first.GetCullingPlane(1));
            Near(new[] { 0f, 0.5879f, 0.809f, 3.8882f }, first.GetCullingPlane(2));
            Near(new[] { 0.3932f, 0.4268f, 0.8144f, 4.246f }, first.GetCullingPlane(7));
            // The second cascade's near plane moves back by its near plane, behind the camera.
            var second = Planes(light, 0.25f, 1f, 12.725f);
            Near(new[] { 0f, 0.1736f, -0.9848f, 43.8307f }, second.GetCullingPlane(1));
            Near(new[] { 0f, 0.5879f, 0.809f, 7.3578f }, second.GetCullingPlane(2));
            // Every silhouette plane runs along the light.
            for (int i = 2; i < first.cullingPlaneCount; i++)
                Assert.Equal(0f, Vector3.Dot(first.GetCullingPlane(i).normal, light.normalized), 4);
        }

        /// <summary>A pipeline that lowers the plane count (no far plane culling) still gets the
        /// slice of the six planes the parameters hold.</summary>
        [Fact]
        public void APipelineWithFivePlanesGetsTheSameCasterPlanes()
        {
            var light = new Vector3(-0.3214f, -0.766f, 0.5567f).normalized;
            var six = Planes(light, 0f, 0.25f, 0.3f);
            Assert.True(m_Camera.TryGetCullingParameters(out var parameters));
            parameters.shadowDistance = 50f;
            parameters.cullingPlaneCount = 5;
            var split = new ShadowSplitData();
            CullingResults.CasterCullingPlanes(ref parameters, m_Camera, 0.3f, CullingResults.ConservativeSliceSphere(m_Camera, 0f, 0.25f, 50f, 64), light, ref split);
            Assert.Equal(six.cullingPlaneCount, split.cullingPlaneCount);
            for (int i = 0; i < split.cullingPlaneCount; i++) Assert.Equal(six.GetCullingPlane(i), split.GetCullingPlane(i));
        }

        [Fact]
        public void ALightAlongTheViewKeepsTheFarFaceAndItsFourEdges()
        {
            var split = Planes(m_Object.transform.forward, 0f, 0.25f, 0.3f);
            Assert.Equal(5, split.cullingPlaneCount);
            Assert.Equal(-1f, Vector3.Dot(split.GetCullingPlane(0).normal, m_Object.transform.forward), 4);
            // A point the camera sees and one before it towards the light are inside every plane.
            var seen = m_Object.transform.position + m_Object.transform.forward * 5f;
            var before = seen - m_Object.transform.forward * 100f;
            for (int i = 0; i < split.cullingPlaneCount; i++)
            {
                Assert.True(split.GetCullingPlane(i).GetDistanceToPoint(seen) > 0f);
                Assert.True(split.GetCullingPlane(i).GetDistanceToPoint(before) > 0f);
            }
        }

        [Fact]
        public void TheSinglePrecisionInverseInvertsAndRefusesSingularMatrices()
        {
            var m = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.Euler(20f, 30f, 40f), new Vector3(2f, 3f, 4f));
            var product = Matrix4x4.InverseSingle(m) * m;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    Assert.Equal(r == c ? 1f : 0f, product[r, c], 5);
            Assert.Equal(Matrix4x4.zero, Matrix4x4.InverseSingle(Matrix4x4.Scale(new Vector3(1f, 0f, 1f))));
            // A perspective projection's far corner comes out past the far plane, as in the Editor.
            var corner = Matrix4x4.InverseSingle(m_Camera.projectionMatrix) * new Vector4(1f, 1f, 1f, 1f);
            Assert.Equal(-1000.07f, corner.z / corner.w, 1);
        }
    }
}
