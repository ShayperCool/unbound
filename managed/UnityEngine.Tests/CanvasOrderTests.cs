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
    }
}
