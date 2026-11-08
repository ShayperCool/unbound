using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Which draws a renderer list with per-object motion vectors keeps and which of them
    /// moved (tests/projects/urp motion_blur_objects): mesh renderers with object motion, still
    /// the first time they are drawn, moving once their matrix differs from the kept one or when
    /// they force no motion; never camera-motion renderers, skinned meshes or queued draws.</summary>
    public sealed class ObjectMotionVectorTests : IDisposable
    {
        readonly List<GameObject> m_Objects = new List<GameObject>();
        readonly Shader m_Shader = ShaderUtil.CreateShaderAsset(
            "Shader \"Tests/ObjectMotion\" { SubShader { Pass { Tags { \"LightMode\"=\"MotionVectors\" } } } }");
        Material m_Material;

        public void Dispose()
        {
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
            if (m_Material != null) Object.DestroyImmediate(m_Material);
            Object.DestroyImmediate(m_Shader);
            Culling.EndFrame();
        }

        MeshRenderer Cube(MotionVectorGenerationMode mode)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Objects.Add(cube);
            var renderer = cube.GetComponent<MeshRenderer>();
            renderer.motionVectorGenerationMode = mode;
            return renderer;
        }

        static FrameBuilder.DrawItem Item(Renderer renderer) =>
            new FrameBuilder.DrawItem { Renderer = renderer, Model = renderer.transform.localToWorldMatrix };

        [Fact]
        public void AMovedObjectMovesFromWhereItWasFirstDrawn()
        {
            var renderer = Cube(MotionVectorGenerationMode.Object);
            var first = renderer.transform.localToWorldMatrix;
            Assert.True(FrameBuilder.ObjectMotion(Item(renderer), out var previous, out bool moving, out bool force));
            Assert.False(moving);
            Assert.False(force);
            Assert.Equal(first, previous);
            renderer.transform.position = new Vector3(0.3f, 0.1f, 0f);
            Assert.True(FrameBuilder.MovingObject(Item(renderer)));
            FrameBuilder.ObjectMotion(Item(renderer), out previous, out _, out _);
            Assert.Equal(first, previous);
        }

        /// <summary>A render pipeline that draws one opaque renderer list with per-object motion
        /// vectors per camera, as URP's motion vector pass does.</summary>
        sealed class MotionListPipelineAsset : RenderPipelineAsset
        {
            protected override RenderPipeline CreatePipeline() => new MotionListPipeline();
        }

        sealed class MotionListPipeline : RenderPipeline
        {
            internal static int Lists;

            protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }

            protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
            {
                foreach (var camera in cameras)
                {
                    if (!camera.TryGetCullingParameters(out var parameters)) continue;
                    var culling = context.Cull(ref parameters);
                    var drawing = new DrawingSettings(new ShaderTagId("MotionVectors"), new SortingSettings(camera))
                    {
                        perObjectData = PerObjectData.MotionVectors,
                    };
                    var filtering = new FilteringSettings(RenderQueueRange.opaque);
                    context.DrawRenderers(culling, ref drawing, ref filtering);
                    Lists++;
                }
            }
        }

        /// <summary>The player loop's pipeline render with per-object motion vectors makes the
        /// renderers' matrices the previous ones at the frame's end, so a moved object moves from
        /// last frame's pose and stops moving once it stays; the same pipeline rendering outside
        /// the player loop (a script's Camera.Render) keeps them, as the built-in renderer's does.</summary>
        [Fact]
        public void ThePlayerLoopsPipelineKeepsTheMatricesAtTheFramesEnd()
        {
            var renderer = Cube(MotionVectorGenerationMode.Object);
            m_Material = new Material(m_Shader);
            renderer.sharedMaterial = m_Material;
            var cameraObject = new GameObject("motion camera");
            m_Objects.Add(cameraObject);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var cameras = new List<Camera> { cameraObject.AddComponent<Camera>() };
            var asset = ScriptableObject.CreateInstance<MotionListPipelineAsset>();
            var saved = GraphicsSettings.defaultRenderPipeline;
            GraphicsSettings.defaultRenderPipeline = asset;
            MotionListPipeline.Lists = 0;
            try
            {
                Assert.True(FrameBuilder.RenderPipelineCameras(cameras));
                FrameBuilder.EndFrameMotion();
                renderer.transform.position = new Vector3(0.3f, 0.1f, 0f);
                var moved = renderer.transform.localToWorldMatrix;
                Assert.True(FrameBuilder.MovingObject(Item(renderer)));
                FrameBuilder.RenderPipelineCameras(cameras);
                FrameBuilder.EndFrameMotion();
                FrameBuilder.ObjectMotion(Item(renderer), out var previous, out bool moving, out _);
                Assert.Equal(moved, previous);
                Assert.False(moving);

                renderer.transform.position = new Vector3(0.6f, 0.1f, 0f);
                RenderPipelineManager.Render(cameras);
                FrameBuilder.EndFrameMotion();
                FrameBuilder.ObjectMotion(Item(renderer), out previous, out moving, out _);
                Assert.Equal(moved, previous);
                Assert.True(moving);
                // That render's list draws a new object, which is still where it was first drawn.
                var drawn = Cube(MotionVectorGenerationMode.Object);
                drawn.sharedMaterial = renderer.sharedMaterial;
                RenderPipelineManager.Render(cameras);
                FrameBuilder.EndFrameMotion();
                var first = drawn.transform.localToWorldMatrix;
                drawn.transform.position = new Vector3(-0.5f, 0f, 0f);
                FrameBuilder.ObjectMotion(Item(drawn), out previous, out moving, out _);
                Assert.Equal(first, previous);
                Assert.True(moving);
                Assert.Equal(4, MotionListPipeline.Lists);
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = saved;
                RenderPipelineManager.CleanupRenderPipeline();
                Object.DestroyImmediate(asset);
            }
        }

        [Fact]
        public void TheListKeepsObjectMotionRenderersAlone()
        {
            var still = Cube(MotionVectorGenerationMode.Object);
            var camera = Cube(MotionVectorGenerationMode.Camera);
            var forced = Cube(MotionVectorGenerationMode.ForceNoMotion);
            var skinnedObject = new GameObject("motion skinned");
            m_Objects.Add(skinnedObject);
            var skinned = skinnedObject.AddComponent<SkinnedMeshRenderer>();
            var items = new List<FrameBuilder.DrawItem>
            {
                Item(still), Item(camera), Item(forced), Item(skinned),
                new FrameBuilder.DrawItem { Model = Matrix4x4.identity },
            };
            FrameBuilder.KeepObjectMotion(items);
            Assert.Equal(new Renderer[] { still, forced }, items.ConvertAll(i => i.Renderer));
            // A still object is not moving; one that forces no motion always draws as moving.
            Assert.False(FrameBuilder.MovingObject(items[0]));
            Assert.True(FrameBuilder.MovingObject(items[1]));
            FrameBuilder.ObjectMotion(items[1], out _, out _, out bool force);
            Assert.True(force);
        }
    }
}
