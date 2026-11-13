using System;
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
