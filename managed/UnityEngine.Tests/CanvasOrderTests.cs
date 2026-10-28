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
        public void OverlayCanvasesOfEqualOrderDrawFarthestFirst()
        {
            // Games stack overlay screens by z (the test game: the always-present screen at z 100
            // over the menu at z 500, both sorting order -2): the nearer one is drawn last, on top,
            // whatever order the canvases were created in.
            var near = Overlay("near", 100f, -2);
            var far = Overlay("far", 500f, -2);
            var above = Overlay("above", 900f, 0);
            var order = CanvasSystem.OverlayCanvases();
            Assert.True(order.IndexOf(far) < order.IndexOf(near));
            Assert.True(order.IndexOf(near) < order.IndexOf(above));
            Assert.True(near.renderOrder > far.renderOrder);
            foreach (var c in new[] { near, far, above })
                Object.DestroyImmediate(c.transform.parent.gameObject);
        }

        [Fact]
        public void EveryCanvasRendererHasADepthDrawnOrNot()
        {
            // The 6000.0 Editor gives each renderer of an active object its depth in hierarchy
            // order, with no mesh or material too: DIY's invisible raycast target (a Graphic that
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
    }
}
