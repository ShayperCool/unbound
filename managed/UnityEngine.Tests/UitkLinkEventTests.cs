using System.Collections.Generic;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Rich-text link events (Unity 6000.0.71f1, tests/projects/uitklinks). Controls outside
    /// the probe: the events copy their trigger's pointer data and carry the link, they bubble and
    /// trickle down, and a pooled event does not keep the link of its previous use.</summary>
    [Collection("UXML custom controls")]
    public class UitkLinkEventTests
    {
        static PointerMoveEvent Trigger(Vector2 position)
        {
            var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = position });
            return move;
        }

        [Fact]
        public void LinkEventsCarryTheLinkAndTheTriggersPointer()
        {
            using var trigger = Trigger(new Vector2(12, 34));
            using var over = PointerOverLinkTagEvent.GetPooled(trigger, "id", "text");
            Assert.Equal("id", over.linkID);
            Assert.Equal("text", over.linkText);
            Assert.Equal(new Vector3(12, 34, 0), over.position);
            Assert.Equal(trigger.pointerId, over.pointerId);
            Assert.True(over.bubbles);
            Assert.True(over.tricklesDown);
            using var outEvent = PointerOutLinkTagEvent.GetPooled(trigger, "ignored");
            Assert.True(outEvent.bubbles && outEvent.tricklesDown);
        }

        // A bubbling event reaches the text element's ancestors after the text element itself (whose
        // callbacks run in the bubble-up phase, as the probe's).
        [Fact]
        public void LinkEventsBubbleThroughAncestors()
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            var parent = new VisualElement();
            var label = new Label("x");
            parent.Add(label);
            panel.visualTree.Add(parent);
            var order = new List<string>();
            parent.RegisterCallback<PointerDownLinkTagEvent>(e => order.Add("parent " + e.propagationPhase), TrickleDown.TrickleDown);
            parent.RegisterCallback<PointerDownLinkTagEvent>(e => order.Add("parent " + e.propagationPhase));
            label.RegisterCallback<PointerDownLinkTagEvent>(e => order.Add("label " + e.propagationPhase));
            using var trigger = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = Vector2.zero });
            using (var down = PointerDownLinkTagEvent.GetPooled(trigger, "a", "b"))
            {
                down.elementTarget = label;
                label.SendEvent(down);
            }
            Assert.Equal(new[] { "parent TrickleDown", "label BubbleUp", "parent BubbleUp" }, order);
        }
    }
}
