using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class CanvasOrderTests
    {
        static Canvas Overlay(string name, float z, int order)
        {
            var parent = new GameObject(name + " screen");
            parent.transform.localPosition = new Vector3(0, 0, z);
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            return canvas;
        }

        [Fact]
        public void OverlayLayoutResetsInheritedDepthBeforeSorting()
        {
            // Native canvaslifecycle transformParent.0.mode resets overlay world Z to zero,
            // compensating for the ordinary parent's depth in local Z. Parent depth therefore
            // cannot decide which overlay draws last; an explicit sorting order still does.
            var near = Overlay("near", 100f, -2);
            var far = Overlay("far", 500f, -2);
            var above = Overlay("above", 900f, 0);
            try
            {
                Assert.Equal(0f, near.transform.position.z);
                Assert.Equal(0f, far.transform.position.z);
                Assert.Equal(0f, above.transform.position.z);
                Assert.Equal(-100f, near.transform.localPosition.z);
                Assert.Equal(-500f, far.transform.localPosition.z);
                Assert.Equal(-900f, above.transform.localPosition.z);
                var order = CanvasSystem.OverlayCanvases();
                Assert.True(order.IndexOf(near) < order.IndexOf(above));
                Assert.True(order.IndexOf(far) < order.IndexOf(above));
            }
            finally
            {
                foreach (var c in new[] { near, far, above })
                    Object.DestroyImmediate(c.transform.parent.gameObject);
            }
        }

        [Fact]
        public void EveryCanvasRendererHasADepthDrawnOrNot()
        {
            // The 6000.0 Editor gives each renderer of an active object its depth in hierarchy
            // order, with no mesh or material too: an invisible raycast target (a Graphic that
            // never makes its mesh) is hit by the GraphicRaycaster, which skips depth -1.
            var canvas = Overlay("depths", 0f, 0);
            var bare = new GameObject("bare", typeof(RectTransform), typeof(CanvasRenderer));
            bare.transform.SetParent(canvas.transform, false);
            var second = new GameObject("second", typeof(RectTransform), typeof(CanvasRenderer));
            second.transform.SetParent(canvas.transform, false);
            second.GetComponent<CanvasRenderer>().cull = true;
            CanvasSystem.Instance.CollectOverlayDraws(new System.Collections.Generic.List<FrameBuilder.DrawItem>());
            int a = bare.GetComponent<CanvasRenderer>().absoluteDepth;
            int b = second.GetComponent<CanvasRenderer>().absoluteDepth;
            Assert.True(a >= 0 && b == a + 1, $"{a}, {b}");
            Object.DestroyImmediate(canvas.transform.parent.gameObject);
        }

        [Fact]
        public void ANestedCanvasReportsItsRootsModeAndCamera()
        {
            // Measured: a canvas saved as World Space, put under a Screen Space - Camera canvas,
            // answers the root's mode and camera (its GraphicRaycaster casts with them).
            var camera = new GameObject("ui camera").AddComponent<Camera>();
            var root = new GameObject("root", typeof(RectTransform)).AddComponent<Canvas>();
            root.renderMode = RenderMode.ScreenSpaceCamera;
            root.worldCamera = camera;
            root.planeDistance = 7;
            var nested = new GameObject("nested", typeof(RectTransform)).AddComponent<Canvas>();
            nested.renderMode = RenderMode.WorldSpace;
            Assert.Equal(RenderMode.WorldSpace, nested.renderMode);
            nested.transform.SetParent(root.transform, false);
            Assert.Equal(RenderMode.ScreenSpaceCamera, nested.renderMode);
            Assert.Same(camera, nested.worldCamera);
            Assert.Equal(7, nested.planeDistance);
            Object.DestroyImmediate(root.gameObject);
            Object.DestroyImmediate(camera.gameObject);
        }

        [Fact]
        public void AVertexStreamSplitsIntoATriangleList()
        {
            // Measured: indices are each vertex in order, whatever the count.
            var verts = new System.Collections.Generic.List<UIVertex>();
            for (int i = 0; i < 6; i++) verts.Add(new UIVertex { position = new Vector3(i, 0, 0) });
            var positions = new System.Collections.Generic.List<Vector3>();
            var indices = new System.Collections.Generic.List<int>();
            CanvasRenderer.SplitUIVertexStreams(verts, positions, new System.Collections.Generic.List<Color32>(),
                new System.Collections.Generic.List<Vector4>(), new System.Collections.Generic.List<Vector4>(),
                new System.Collections.Generic.List<Vector4>(), new System.Collections.Generic.List<Vector4>(),
                new System.Collections.Generic.List<Vector3>(), new System.Collections.Generic.List<Vector4>(), indices);
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, indices);
            Assert.Equal(6, positions.Count);
        }

        [Fact]
        public void ANestedCanvasWithoutOverrideAnswersItsSortingSourcesOrder()
        {
            // Measured: a nested canvas that does not override sorting answers the order of the
            // canvas it draws with; its own shows once it overrides.
            var root = new GameObject("root", typeof(RectTransform)).AddComponent<Canvas>();
            root.sortingOrder = 3;
            var mid = new GameObject("mid", typeof(RectTransform)).AddComponent<Canvas>();
            mid.transform.SetParent(root.transform, false);
            mid.overrideSorting = true;
            mid.sortingOrder = 21;
            var leaf = new GameObject("leaf", typeof(RectTransform)).AddComponent<Canvas>();
            leaf.sortingOrder = 20;
            leaf.transform.SetParent(mid.transform, false);
            Assert.Equal(21, leaf.sortingOrder);
            leaf.overrideSorting = true;
            Assert.Equal(20, leaf.sortingOrder);
            Object.DestroyImmediate(root.gameObject);
        }
    }
}
