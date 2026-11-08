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

        /// <summary>A queued draw is lit as a renderer at its bounds: the additional lights whose
        /// range reaches them.</summary>
        [Fact]
        public void QueuedDrawsTakeThePerObjectLightsAtTheirBounds()
        {
            var lightObject = new GameObject("srp queued point light");
            try
            {
                lightObject.transform.position = new Vector3(0f, 0f, 2f);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 3f;
                var data = new CullingData { Camera = m_Camera };
                data.Lights = new Unity.Collections.NativeArray<VisibleLight>(1, Unity.Collections.Allocator.Persistent);
                data.Lights[0] = new VisibleLight(light, new Rect(0, 0, 1, 1), false, true, false);
                try
                {
                    var near = FrameBuilder.PerObjectLights(data, new Bounds(Vector3.zero, Vector3.one), false, -1);
                    var far = FrameBuilder.PerObjectLights(data, new Bounds(new Vector3(0f, 0f, 20f), Vector3.one), false, -1);
                    int id = Shader.PropertyToID("unity_LightData");
                    Assert.Equal(1f, ((Vector4)near[id]).y);
                    Assert.Equal(0f, ((Vector4)far[id]).y);
                }
                finally { data.Dispose(); }
            }
            finally { Object.DestroyImmediate(lightObject); }
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

        /// <summary>A renderer list's per-object lights: a queued draw's at the bounds it is culled
        /// by (its set world bounds, not its mesh's); a queued draw's and a renderer's own block
        /// values win (tests/projects/urp light_data_block); draws sharing a block share one
        /// answer; a renderer's draws with another block (a submesh's) get their own.</summary>
        [Fact]
        public void RendererListsLightQueuedDrawsAtTheirCullBoundsUnderTheirOwnValues()
        {
            var lightObject = new GameObject("srp queued point light");
            var rendererObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                lightObject.transform.position = new Vector3(50f, 0f, 0f);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 3f;
                rendererObject.transform.position = new Vector3(50f, 0f, 0f);
                var data = new CullingData { Camera = m_Camera };
                data.Lights = new Unity.Collections.NativeArray<VisibleLight>(1, Unity.Collections.Allocator.Persistent);
                data.Lights[0] = new VisibleLight(light, new Rect(0, 0, 1, 1), false, true, false);
                int lightData = Shader.PropertyToID("unity_LightData");
                var own = new System.Collections.Generic.Dictionary<int, object> { [lightData] = new Vector4(1f, 1f, 1f, 0f) };
                var near = new Bounds(new Vector3(50f, 0f, 0f), Vector3.one);
                FrameBuilder.DrawItem Queued(System.Collections.Generic.Dictionary<int, object> properties) => new FrameBuilder.DrawItem
                {
                    Lighting = ImmediateLighting.Default,
                    Bounds = new Bounds(Vector3.zero, Vector3.one),
                    CullBounds = near,
                    Properties = properties,
                };
                var renderer = rendererObject.GetComponent<MeshRenderer>();
                var items = new System.Collections.Generic.List<FrameBuilder.DrawItem>
                {
                    Queued(null), Queued(own), Queued(own),
                    new FrameBuilder.DrawItem { Renderer = renderer, Properties = own },
                    new FrameBuilder.DrawItem { Renderer = renderer },
                    new FrameBuilder.DrawItem { Renderer = renderer, Properties = own },
                };
                try
                {
                    FrameBuilder.ApplyPerObjectLights(data, items, -1, false);
                    Assert.Equal(new Vector4(0f, 1f, 1f, 0f), (Vector4)items[0].Properties[lightData]);
                    Assert.Equal(new Vector4(1f, 1f, 1f, 0f), (Vector4)items[1].Properties[lightData]);
                    Assert.Same(items[1].Properties, items[2].Properties);
                    Assert.Equal(new Vector4(1f, 1f, 1f, 0f), (Vector4)items[3].Properties[lightData]);
                    Assert.Equal(new Vector4(0f, 1f, 1f, 0f), (Vector4)items[4].Properties[lightData]);
                    Assert.Same(items[3].Properties, items[5].Properties);
                    Assert.NotSame(items[3].Properties, items[4].Properties);
                    Assert.Single(own);
                }
                finally { data.Dispose(); }
            }
            finally
            {
                Object.DestroyImmediate(lightObject);
                Object.DestroyImmediate(rendererObject);
            }
        }

        /// <summary>A renderer's property block (drawn through a material made under it) wins over
        /// its per-object lights where it sets the same constants; its other slot keeps them
        /// (tests/projects/urp light_data_block).</summary>
        [Fact]
        public void ARenderersBlockWinsOverItsPerObjectLights()
        {
            var lightObject = new GameObject("srp block point light");
            var rendererObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 3f;
                var renderer = rendererObject.GetComponent<MeshRenderer>();
                var shared = m_Material;
                renderer.sharedMaterials = new[] { shared, shared };
                var block = new MaterialPropertyBlock();
                block.SetVector("unity_LightData", Vector4.zero);
                renderer.SetPropertyBlock(block, 0);
                var data = new CullingData { Camera = m_Camera };
                data.Lights = new Unity.Collections.NativeArray<VisibleLight>(1, Unity.Collections.Allocator.Persistent);
                data.Lights[0] = new VisibleLight(light, new Rect(0, 0, 1, 1), false, true, false);
                int lightData = Shader.PropertyToID("unity_LightData");
                var items = new System.Collections.Generic.List<FrameBuilder.DrawItem>
                {
                    new FrameBuilder.DrawItem { Renderer = renderer, Material = renderer.DrawnMaterial(0, shared) },
                    new FrameBuilder.DrawItem { Renderer = renderer, Material = renderer.DrawnMaterial(1, shared) },
                };
                try
                {
                    Assert.NotSame(shared, items[0].Material);
                    FrameBuilder.ApplyPerObjectLights(data, items, -1, false);
                    Assert.Equal(Vector4.zero, (Vector4)items[0].Properties[lightData]);
                    Assert.Equal(new Vector4(0f, 1f, 1f, 0f), (Vector4)items[1].Properties[lightData]);
                }
                finally { data.Dispose(); }
            }
            finally
            {
                Object.DestroyImmediate(lightObject);
                Object.DestroyImmediate(rendererObject);
            }
        }

        /// <summary>Every draw of an instanced RenderParams call carries its mask.</summary>
        [Fact]
        public void InstancedRenderParamsDrawsKeepTheirRenderingLayerMask()
        {
            m_Material.enableInstancing = true;
            var rp = new RenderParams(m_Material) { layer = 30, camera = m_Camera, renderingLayerMask = 8u };
            Graphics.RenderMeshInstanced(rp, m_Mesh, 0, new[] { Matrix4x4.identity, Matrix4x4.Translate(Vector3.right) });
            Assert.Equal(2, Immediate.Queued.Count);
            Assert.All(Immediate.Queued, q => Assert.Equal(8u, q.RenderingLayerMask));
        }
    }
}
