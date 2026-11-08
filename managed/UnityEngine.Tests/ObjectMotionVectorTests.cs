using System;
using System.Collections.Generic;
using Unbound.Runtime;
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

        public void Dispose()
        {
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
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
