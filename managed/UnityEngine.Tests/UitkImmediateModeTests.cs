using System.Collections.Generic;
using System.Linq;
using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

// ImmediateModeElement (Unity 6000.0.71f1, tests/projects/uitkimmediate). Controls outside the
// probe: the callback's place among the panel's draws, kept across repaints without generating
// the element again, its scissor from clipping ancestors, no callback for hidden elements, and
// transparent subtrees that generate their content but draw nothing.

namespace UnityEngine.Tests
{
    public class UitkImmediateModeTests
    {
        sealed class Probe : ImmediateModeElement
        {
            internal int Calls;
            protected override void ImmediateRepaint() => Calls++;
        }

        static T Place<T>(T e, float left, float top, float width, float height, Color? background = null) where T : VisualElement
        {
            e.style.position = Position.Absolute;
            e.style.left = left;
            e.style.top = top;
            e.style.width = width;
            e.style.height = height;
            if (background is Color c) e.style.backgroundColor = c;
            return e;
        }

        static Panel NewPanel()
        {
            var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            panel.visualTree.style.width = 100;
            panel.visualTree.style.height = 100;
            return panel;
        }

        static UaPainter Paint(Panel panel)
        {
            panel.ApplyStyles();
            panel.ValidateLayout();
            var painter = new UaPainter();
            painter.Paint(panel, 100);
            return painter;
        }

        [Fact]
        public void CullingEnabledIsOffByDefault()
        {
            var e = new Probe();
            Assert.False(e.cullingEnabled);
            e.cullingEnabled = true;
            Assert.True(e.cullingEnabled);
        }

        [Fact]
        public void TheCallbackTakesTheElementsPlaceAmongTheDrawsAndIsKeptAcrossRepaints()
        {
            using var panel = NewPanel();
            panel.visualTree.Add(Place(new VisualElement(), 0, 0, 10, 10, Color.red));
            var probe = Place(new Probe(), 20, 20, 10, 10);
            panel.visualTree.Add(probe);
            panel.visualTree.Add(Place(new VisualElement(), 40, 40, 10, 10, Color.blue));
            var painter = Paint(panel);
            for (int repaint = 0; repaint < 2; repaint++)
            {
                var batches = painter.Batches;
                int at = batches.FindIndex(b => b.Immediate != null);
                Assert.True(at > 0);
                Assert.Single(batches, b => b.Immediate != null);
                Assert.Same(probe, batches[at].ImmediateOwner);
                // The first background before it, the last one after it.
                Assert.True(batches.Take(at).Sum(b => b.IndexCount) > 0);
                Assert.True(batches.Skip(at + 1).Sum(b => b.IndexCount) > 0);
                Assert.Null(batches[at].ImmediateScissor);
                Assert.False(batches[at].ImmediateCull);
                painter.Paint(panel, 100);
            }
            // Painting only places the callback; the panel's rendering runs it.
            Assert.Equal(0, probe.Calls);
            painter.Batches.First(b => b.Immediate != null).Immediate();
            Assert.Equal(1, probe.Calls);
            probe.cullingEnabled = true;
            painter = Paint(panel);
            Assert.True(painter.Batches.Single(b => b.Immediate != null).ImmediateCull);
        }

        [Fact]
        public void ClippingAncestorsScissorTheCallbackAndHiddenElementsHaveNone()
        {
            using var panel = NewPanel();
            var clipper = Place(new VisualElement(), 10, 20, 30, 40);
            clipper.style.overflow = Overflow.Hidden;
            panel.visualTree.Add(clipper);
            var probe = Place(new Probe(), 5, 5, 50, 50);
            clipper.Add(probe);
            var painter = Paint(panel);
            // The parent's padding box in target pixels from the bottom.
            Assert.Equal(new RectInt(10, 40, 30, 40), painter.Batches.Single(b => b.Immediate != null).ImmediateScissor);
            probe.style.visibility = Visibility.Hidden;
            Assert.DoesNotContain(Paint(panel).Batches, b => b.Immediate != null);
            probe.style.visibility = Visibility.Visible;
            probe.style.display = DisplayStyle.None;
            Assert.DoesNotContain(Paint(panel).Batches, b => b.Immediate != null);
        }

        // The probe's transparent elements sit directly in the root; here the transparent ancestor
        // is two levels up and clips, and its own background draws nothing.
        [Fact]
        public void TransparentSubtreesStillPlaceTheirCallbacksButDrawNothing()
        {
            using var panel = NewPanel();
            var faded = Place(new VisualElement(), 10, 20, 60, 60, Color.red);
            faded.style.opacity = 0f;
            faded.style.overflow = Overflow.Hidden;
            panel.visualTree.Add(faded);
            var middle = Place(new VisualElement(), 0, 0, 50, 50, Color.blue);
            faded.Add(middle);
            var probe = Place(new Probe(), 5, 5, 20, 20);
            middle.Add(probe);
            var painter = Paint(panel);
            var immediate = painter.Batches.Single(b => b.Immediate != null);
            Assert.Same(probe, immediate.ImmediateOwner);
            Assert.Equal(new RectInt(10, 20, 60, 60), immediate.ImmediateScissor);
            Assert.Equal(0, painter.Batches.Sum(b => b.IndexCount));
        }

        // Content generation does not depend on ImmediateModeElements: a transparent element's
        // generateVisualContent runs in a panel without any, and what it draws is not drawn.
        [Fact]
        public void TransparentElementsGenerateTheirContent()
        {
            using var panel = NewPanel();
            var faded = Place(new VisualElement(), 10, 20, 60, 60);
            faded.style.opacity = 0f;
            int calls = 0;
            faded.generateVisualContent += mgc =>
            {
                calls++;
                mgc.painter2D.BeginPath();
                mgc.painter2D.MoveTo(Vector2.zero);
                mgc.painter2D.LineTo(new Vector2(10f, 10f));
                mgc.painter2D.LineTo(new Vector2(0f, 10f));
                mgc.painter2D.Fill();
            };
            panel.visualTree.Add(faded);
            var painter = Paint(panel);
            Assert.Equal(1, calls);
            Assert.Equal(0, painter.Batches.Sum(b => b.IndexCount));
            faded.style.opacity = 1f;
            painter = Paint(panel);
            Assert.True(painter.Batches.Sum(b => b.IndexCount) > 0);
        }
    }
}
