using System;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UitkRuntimeTests
    {
        sealed class Link : MonoBehaviour, IRuntimePanelComponent
        {
            public int assignments;
            IPanel current;
            public IPanel panel { get => current; set { current = value; assignments++; } }
        }

        [Fact]
        public void ChangingSelectableObjectReassignsAllPanelComponents()
        {
            var owner = ScriptableObject.CreateInstance<PanelSettings>();
            using var panel = RuntimePanel.Create(owner);
            var first = new GameObject("first");
            var second = new GameObject("second");
            var a = first.AddComponent<Link>();
            var b = first.AddComponent<Link>();
            var c = second.AddComponent<Link>();
            b.enabled = false;
            try
            {
                panel.selectableGameObject = first;
                Assert.Same(panel, a.panel);
                Assert.Same(panel, b.panel);
                panel.selectableGameObject = first;
                Assert.Equal(1, a.assignments);
                panel.selectableGameObject = second;
                Assert.Null(a.panel);
                Assert.Null(b.panel);
                Assert.Same(panel, c.panel);
                panel.selectableGameObject = null;
                Assert.Null(c.panel);
                Assert.Equal(2, c.assignments);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(owner);
            }
        }

        [Fact]
        public void ConversionHandlesUsedEventsAndRespectsOtherPressedButtons()
        {
            PointerDeviceState.ReleaseAllButtons(PointerId.mousePointerId);
            try
            {
                var original = new Event { type = EventType.KeyUp, keyCode = KeyCode.Escape };
                original.Use();
                using var key = UIElementsRuntimeUtility.CreateEvent(original);
                Assert.IsType<IMGUIEvent>(key);
                using var down = UIElementsRuntimeUtility.CreateEvent(new Event { type = EventType.MouseDown, button = 2 });
                Assert.IsType<PointerDownEvent>(down);
                using var chord = UIElementsRuntimeUtility.CreateEvent(new Event { type = EventType.MouseDown, button = 1 });
                Assert.IsType<PointerMoveEvent>(chord);
                using var leave = UIElementsRuntimeUtility.CreateEvent(new Event { type = EventType.MouseLeaveWindow });
                Assert.Equal(0, Assert.IsType<MouseLeaveWindowEvent>(leave).pressedButtons);
                using var up = UIElementsRuntimeUtility.CreateEvent(new Event { type = EventType.MouseUp, button = 1 });
                Assert.IsType<PointerUpEvent>(up);
            }
            finally { PointerDeviceState.ReleaseAllButtons(PointerId.mousePointerId); }
        }

        [Fact]
        public void PanelIsCachedBeforeNotificationAndDisposedPanelIsCreatedAgain()
        {
            var owner = ScriptableObject.CreateInstance<PanelSettings>();
            int count = 0;
            void Created(BaseRuntimePanel panel)
            {
                count++;
                Assert.Same(panel, UIElementsRuntimeUtility.FindOrCreateRuntimePanel(owner, _ => throw new Exception("already cached")));
            }
            UIElementsRuntimeUtility.onCreatePanel += Created;
            try
            {
                var first = UIElementsRuntimeUtility.FindOrCreateRuntimePanel(owner, RuntimePanel.Create);
                Assert.Equal(1, count);
                Assert.Same(first, UIElementsRuntimeUtility.FindOrCreateRuntimePanel(owner, RuntimePanel.Create));
                UIElementsRuntimeUtility.DisposeRuntimePanel(owner);
                Assert.NotSame(first, UIElementsRuntimeUtility.FindOrCreateRuntimePanel(owner, RuntimePanel.Create));
                Assert.Equal(2, count);
            }
            finally
            {
                UIElementsRuntimeUtility.onCreatePanel -= Created;
                UIElementsRuntimeUtility.DisposeRuntimePanel(owner);
                Object.DestroyImmediate(owner);
            }
        }
    }
}
