using System;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A scriptable pipeline's culling takes the meshes queued with Graphics.DrawMesh and
    /// its kin as the built-in pipeline's camera render does (URP's screen space decals draw
    /// through it, tests/projects/urp decal_*): culled by the camera's mask and frustum, a draw
    /// for this camera taken by its culling, one for every camera kept for the frame.</summary>
    public sealed class SrpQueuedDrawTests : IDisposable
    {
        readonly GameObject m_CameraObject = new GameObject("srp queued camera");
        readonly Camera m_Camera;
        readonly Camera m_Other;
        readonly GameObject m_OtherObject = new GameObject("srp queued other camera");
        readonly Mesh m_Mesh = new Mesh();
        readonly Shader m_Shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/SrpQueued\" { SubShader { Pass { } } }");
        readonly Material m_Material;

        public SrpQueuedDrawTests()
        {
            Immediate.Queued.Clear();
            m_Camera = m_CameraObject.AddComponent<Camera>();
            m_Camera.transform.position = new Vector3(0f, 0f, -10f);
            m_Camera.cullingMask = 1 << 30;
            m_Other = m_OtherObject.AddComponent<Camera>();
            m_Mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f) };
            m_Mesh.triangles = new[] { 0, 1, 2 };
            m_Material = new Material(m_Shader);
        }

        public void Dispose()
        {
            Immediate.Queued.Clear();
            Culling.EndFrame();
            Object.DestroyImmediate(m_CameraObject);
            Object.DestroyImmediate(m_OtherObject);
            Object.DestroyImmediate(m_Material);
            Object.DestroyImmediate(m_Shader);
            Object.DestroyImmediate(m_Mesh);
        }

        CullingData Cull()
        {
            Assert.True(m_Camera.TryGetCullingParameters(out var parameters));
            return Culling.Get(Culling.Cull(ref parameters));
        }

        [Fact]
        public void CullingTakesTheCamerasQueuedDrawsAndKeepsEveryCamerasOnes()
        {
            Graphics.DrawMesh(m_Mesh, Matrix4x4.identity, m_Material, 30, m_Camera);
            Graphics.DrawMesh(m_Mesh, Matrix4x4.Translate(new Vector3(1f, 0f, 0f)), m_Material, 30);
            Graphics.DrawMesh(m_Mesh, Matrix4x4.identity, m_Material, 30, m_Other);
            var data = Cull();
            Assert.Equal(2, data.Queued.Count);
            Assert.Equal(2, data.QueuedVisible.Count);
            // The camera's own draw is taken; the every-camera and the other camera's draws stay.
            Assert.Equal(2, Immediate.Queued.Count);
            Assert.DoesNotContain(Immediate.Queued, q => q.Camera == m_Camera);
            var again = Cull();
            Assert.Single(again.Queued);
            Assert.Null(again.Queued[0].Camera);
        }

        [Fact]
        public void TheMaskAndTheFrustumCullQueuedDraws()
        {
            // Another layer: not seen. Behind the camera: cast shadows from there, not drawn.
            Graphics.DrawMesh(m_Mesh, Matrix4x4.identity, m_Material, 3, m_Camera);
            Graphics.DrawMesh(m_Mesh, Matrix4x4.Translate(new Vector3(0f, 0f, -30f)), m_Material, 30, m_Camera);
            var data = Cull();
            Assert.Single(data.Queued);
            Assert.Empty(data.QueuedVisible);
            Assert.Empty(Immediate.Queued);
        }

        [Fact]
        public void ARenderParamsDrawKeepsItsRenderingLayerMask()
        {
            var rp = new RenderParams(m_Material) { layer = 30, camera = m_Camera, renderingLayerMask = 4u };
            Graphics.RenderMesh(rp, m_Mesh, 0, Matrix4x4.identity);
            Graphics.DrawMesh(m_Mesh, Matrix4x4.identity, m_Material, 30, m_Camera);
            var data = Cull();
            Assert.Equal(2, data.QueuedVisible.Count);
            Assert.Equal(4u, data.QueuedVisible[0].RenderingLayerMask);
            Assert.Equal(GraphicsSettings.defaultRenderingLayerMask, data.QueuedVisible[1].RenderingLayerMask);
        }
    }
}
