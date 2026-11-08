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
    /// they force no motion, and skinned mesh renderers, moving as their renderer does (their
    /// deformation adds none: tests/projects/urp motion_blur_skinned_moved, motion_blur_skinned);
    /// never camera-motion renderers or queued draws.</summary>
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
            internal static bool ForceAll;

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
                    var filtering = new FilteringSettings(RenderQueueRange.opaque) { forceAllMotionVectorObjects = ForceAll };
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
            MotionListPipeline.ForceAll = false;
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

        /// <summary>A list that forces every object's motion vectors is unmeasured: it warns and
        /// draws what it matches without the engine's per-object motion (the renderer's matrix
        /// is not taken).</summary>
        [Fact]
        public void AListForcingAllMotionVectorObjectsWarns()
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
            var logs = new List<string>();
            var logger = Debug.unityLogger;
            var previousHandler = logger.logHandler;
            GraphicsSettings.defaultRenderPipeline = asset;
            MotionListPipeline.Lists = 0;
            MotionListPipeline.ForceAll = true;
            FrameBuilder.s_WarnedForcedMotionVectors = false;
            logger.logHandler = new WarningCapture(logs);
            try
            {
                RenderPipelineManager.Render(cameras);
                RenderPipelineManager.Render(cameras);
                Assert.Equal(2, MotionListPipeline.Lists);
                Assert.False(renderer.m_HasPreviousModel);
                // Once, for both lists.
                Assert.Single(logs, l => l.Contains("forceAllMotionVectorObjects"));
            }
            finally
            {
                logger.logHandler = previousHandler;
                MotionListPipeline.ForceAll = false;
                GraphicsSettings.defaultRenderPipeline = saved;
                RenderPipelineManager.CleanupRenderPipeline();
                Object.DestroyImmediate(asset);
            }
        }

        sealed class WarningCapture : ILogHandler
        {
            readonly List<string> m_Logs;
            internal WarningCapture(List<string> logs) => m_Logs = logs;
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning) m_Logs.Add(args.Length > 0 ? string.Format(format, args) : format);
            }
            public void LogException(Exception exception, Object context) => m_Logs.Add(exception.ToString());
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
            Assert.Equal(new Renderer[] { still, forced, skinned }, items.ConvertAll(i => i.Renderer));
            // A skinned renderer that has not moved is still.
            Assert.False(FrameBuilder.MovingObject(items[2]));
            // A still object is not moving; one that forces no motion always draws as moving.
            Assert.False(FrameBuilder.MovingObject(items[0]));
            Assert.True(FrameBuilder.MovingObject(items[1]));
            FrameBuilder.ObjectMotion(items[1], out _, out _, out bool force);
            Assert.True(force);
        }

        /// <summary>A two-bone ribbon: the top row follows the upper bone.</summary>
        SkinnedMeshRenderer Ribbon(out Transform upper)
        {
            var root = new GameObject("ribbon");
            m_Objects.Add(root);
            var lower = new GameObject("lower").transform;
            lower.SetParent(root.transform, false);
            upper = new GameObject("upper").transform;
            upper.SetParent(lower, false);
            upper.localPosition = new Vector3(0f, 1f, 0f);
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 2f, 0f), new Vector3(1f, 2f, 0f) },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f }, new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                },
                bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(new Vector3(0f, -1f, 0f)) },
            };
            var skin = root.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { lower, upper };
            return skin;
        }

        /// <summary>The motion pass's view of a skinned renderer's draw this frame, and its
        /// deformed mesh (in world space, drawn with the identity).</summary>
        static Mesh MotionDraw(SkinnedMeshRenderer skin, out bool moving, out Matrix4x4 previous)
        {
            Time.s_FrameCount++;
            var mesh = skin.DrawGeometry(out var model);
            Assert.Equal(Matrix4x4.identity, model);
            Assert.True(FrameBuilder.ObjectMotion(new FrameBuilder.DrawItem { Renderer = skin, Model = model }, out previous, out moving, out _));
            return mesh;
        }

        [Fact]
        public void AMovedSkinMovesAsItsRenderer()
        {
            var skin = Ribbon(out _);
            var root = skin.transform;
            root.position = new Vector3(5f, 0f, 0f);
            MotionDraw(skin, out bool moving, out var previous);
            Assert.False(moving);
            Assert.Equal(Matrix4x4.identity, previous);
            // Turned a quarter and moved up: the previous matrix puts this frame's world-space
            // skin back where the renderer was.
            root.SetPositionAndRotation(new Vector3(5f, 1f, 0f), Quaternion.Euler(0f, 0f, 90f));
            var mesh = MotionDraw(skin, out moving, out previous);
            Assert.True(moving);
            var was = new[] { new Vector3(5f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(5f, 2f, 0f), new Vector3(6f, 2f, 0f) };
            for (int i = 0; i < 4; i++)
                Assert.True((previous.MultiplyPoint3x4(mesh.m_Vertices[i]) - was[i]).magnitude < 1e-4f, $"{i}");
            // Kept again (a player-loop motion render's frame end): still.
            skin.m_PreviousModel = root.localToWorldMatrix;
            MotionDraw(skin, out moving, out previous);
            Assert.False(moving);
            Assert.True((previous.MultiplyPoint3x4(Vector3.one) - Vector3.one).magnitude < 1e-5f);
        }

        /// <summary>Bending in place adds no motion, with skinnedMotionVectors or without
        /// (measured on the Editor's OpenGL Core device).</summary>
        [Fact]
        public void DeformationAloneIsNoMotion()
        {
            foreach (bool skinnedMotionVectors in new[] { true, false })
            {
                var skin = Ribbon(out var upper);
                skin.skinnedMotionVectors = skinnedMotionVectors;
                MotionDraw(skin, out _, out _);
                upper.localRotation = Quaternion.Euler(0f, 0f, 90f);
                MotionDraw(skin, out bool moving, out var previous);
                Assert.False(moving);
                Assert.Equal(Matrix4x4.identity, previous);
            }
        }
    }
}
