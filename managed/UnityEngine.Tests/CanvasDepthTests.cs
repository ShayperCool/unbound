using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>
    /// CanvasRenderer.absoluteDepth and relativeDepth as the Unity 6000.0.71f1 Editor numbers them
    /// (tests/projects/uidepth): from 0 in each canvas that sorts on its own, relative to the
    /// nearest canvas with one number per non-empty nested canvas, -1 below disabled canvases and
    /// at once for a renderer whose canvas changes.
    /// </summary>
    public class CanvasDepthTests : IDisposable
    {
        readonly List<GameObject> m_Roots = new List<GameObject>();

        public void Dispose()
        {
            foreach (var root in m_Roots)
                if (root != null) Object.DestroyImmediate(root);
            CanvasSystem.UpdateDepths();
        }

        Canvas Root(string name, RenderMode mode = RenderMode.ScreenSpaceOverlay)
        {
            var go = new GameObject(name, typeof(RectTransform));
            m_Roots.Add(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = mode;
            return canvas;
        }

        static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static CanvasRenderer Renderer(string name, Transform parent) => Node(name, parent).AddComponent<CanvasRenderer>();

        static Canvas Nested(string name, Transform parent, bool overrideSorting)
        {
            var canvas = Node(name, parent).AddComponent<Canvas>();
            canvas.overrideSorting = overrideSorting;
            return canvas;
        }

        static (int, int) Depths(CanvasRenderer cr) => (cr.absoluteDepth, cr.relativeDepth);

        [Fact]
        public void EveryCanvasThatSortsOnItsOwnNumbersFromZero()
        {
            // Overlay, camera without a camera and world canvases with no camera at all: each
            // numbers its own renderers, whatever was numbered before it.
            var a = Root("a");
            var a0 = Renderer("a0", a.transform);
            var a1 = Renderer("a1", a.transform);
            var b = Root("b", RenderMode.WorldSpace);
            var b0 = Renderer("b0", b.transform);
            var c = Root("c", RenderMode.ScreenSpaceCamera);
            c.sortingOrder = -7;
            var c0 = Renderer("c0", c.transform);
            var nested = Nested("over", a.transform, overrideSorting: true);
            var o0 = Renderer("o0", nested.transform);
            var a2 = Renderer("a2", a.transform);
            CanvasSystem.UpdateDepths();
            Assert.Equal((0, 0), Depths(a0));
            Assert.Equal((1, 1), Depths(a1));
            Assert.Equal((2, 2), Depths(a2));
            Assert.Equal((0, 0), Depths(b0));
            Assert.Equal((0, 0), Depths(c0));
            Assert.Equal((0, 0), Depths(o0));
        }

        [Fact]
        public void NestedCanvasesDrawnWithTheirParentTakeOneRelativeNumberWhenTheyNumberARenderer()
        {
            // A control deeper than the probe's: a nested canvas inside a nested canvas, an
            // overriding canvas two levels down, and a renderer on a nested canvas's own object.
            var root = Root("root");
            var r0 = Renderer("r0", root.transform);
            var n = Nested("n", root.transform, false);
            var n0 = n.gameObject.AddComponent<CanvasRenderer>();
            var nn = Nested("nn", n.transform, false);
            var nn0 = Renderer("nn0", nn.transform);
            var n1 = Renderer("n1", n.transform);
            var r1 = Renderer("r1", root.transform);
            Nested("empty", root.transform, false);
            Node("nothing", Nested("onlyEmptyChild", root.transform, false).transform);
            var r2 = Renderer("r2", root.transform);
            var holder = Nested("holder", root.transform, false);
            var deep = Nested("deep", Node("plain", holder.transform).transform, true);
            var d0 = Renderer("d0", deep.transform);
            var r3 = Renderer("r3", root.transform);
            CanvasSystem.UpdateDepths();
            Assert.Equal((0, 0), Depths(r0));
            Assert.Equal((1, 0), Depths(n0));
            Assert.Equal((2, 0), Depths(nn0));
            Assert.Equal((3, 2), Depths(n1));
            Assert.Equal((4, 2), Depths(r1));
            Assert.Equal((5, 3), Depths(r2));
            Assert.Equal((0, 0), Depths(d0));
            Assert.Equal((6, 5), Depths(r3));
        }

        [Fact]
        public void ADisabledCanvasNumbersNothingButOverridingCanvasesBelowItNumberThemselves()
        {
            var root = Root("root");
            var off = Nested("off", root.transform, false);
            var hidden = Renderer("hidden", off.transform);
            var drawnWith = Nested("drawnWith", off.transform, false);
            var hiddenToo = Renderer("hiddenToo", drawnWith.transform);
            var over = Nested("over", off.transform, true);
            var own = Renderer("own", over.transform);
            var after = Renderer("after", root.transform);
            off.enabled = false;
            CanvasSystem.UpdateDepths();
            Assert.Equal((-1, -1), Depths(hidden));
            Assert.Equal((-1, -1), Depths(hiddenToo));
            Assert.Equal((0, 0), Depths(own));
            Assert.Equal((0, 0), Depths(after));
            root.enabled = false;
            var inactive = Renderer("inactive", Root("other").transform);
            inactive.gameObject.SetActive(false);
            CanvasSystem.UpdateDepths();
            Assert.Equal((-1, -1), Depths(after));
            Assert.Equal((0, 0), Depths(own));
            Assert.Equal((-1, -1), Depths(inactive));
        }

        [Fact]
        public void ARendererThatChangesCanvasLosesItsDepthAtOnce()
        {
            var a = Root("a");
            var b = Root("b");
            var moved = Renderer("moved", a.transform);
            var stays = Renderer("stays", a.transform);
            var sibling = Renderer("sibling", a.transform);
            var nested = Nested("nested", a.transform, false);
            var inNested = Renderer("inNested", nested.transform);
            var b0 = Renderer("b0", b.transform);
            CanvasSystem.UpdateDepths();
            Assert.Equal((1, 1), Depths(stays));

            moved.transform.SetParent(b.transform, false);
            sibling.transform.SetAsFirstSibling();
            Assert.Equal((-1, -1), Depths(moved));
            Assert.Equal((1, 1), Depths(stays));
            Assert.Equal((2, 2), Depths(sibling));

            nested.overrideSorting = true;
            Assert.Equal((-1, -1), Depths(inNested));
            b.gameObject.SetActive(false);
            Assert.Equal((-1, -1), Depths(b0));
            // A renderer's own object deactivated keeps its number until the next update.
            stays.gameObject.SetActive(false);
            Assert.Equal((1, 1), Depths(stays));

            // Canvas.ForceUpdateCanvases numbers nothing; the canvas update does.
            Canvas.ForceUpdateCanvases();
            Assert.Equal((-1, -1), Depths(inNested));
            CanvasSystem.UpdateDepths();
            Assert.Equal((-1, -1), Depths(stays));
            Assert.Equal((0, 0), Depths(sibling));
            Assert.Equal((0, 0), Depths(inNested));
            Assert.Equal((-1, -1), Depths(moved));

            // A canvas added above renderers, and one removed from under them.
            var wrap = Node("wrap", a.transform);
            var w0 = Renderer("w0", wrap.transform);
            CanvasSystem.UpdateDepths();
            Assert.Equal((1, 1), Depths(w0));
            var added = wrap.AddComponent<Canvas>();
            Assert.Equal((-1, -1), Depths(w0));
            CanvasSystem.UpdateDepths();
            Assert.Equal((1, 0), Depths(w0));
            Object.DestroyImmediate(added);
            Assert.Equal((-1, -1), Depths(w0));
        }

        [Fact]
        public void NumberingAllocatesNothing()
        {
            var root = Root("root");
            for (int i = 0; i < 20; i++)
            {
                var n = Nested("n" + i, root.transform, i % 3 == 0);
                Renderer("r" + i, n.transform);
                Renderer("s" + i, root.transform);
            }
            CanvasSystem.UpdateDepths();
            CanvasSystem.UpdateDepths();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) CanvasSystem.UpdateDepths();
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
