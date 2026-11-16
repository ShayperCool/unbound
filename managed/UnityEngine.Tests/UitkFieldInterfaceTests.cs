using System;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>BaseField's IMixedValueSupport, IPrefixLabel and IEditableElement (Unity 6000.0.71f1,
    /// tests/projects/fieldinterfaces). Controls outside the probe: an integer slider and an enum
    /// field, and a field whose handlers are set before it is first attached.</summary>
    public sealed class UitkFieldInterfaceTests : IDisposable
    {
        readonly Panel m_Panel;

        public UitkFieldInterfaceTests()
        {
            m_Panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            m_Panel.visualTree.style.width = 400;
            m_Panel.visualTree.style.height = 300;
        }

        public void Dispose() => m_Panel.Dispose();

        void Tick()
        {
            m_Panel.uaScheduler.UpdateScheduledEvents();
            m_Panel.ApplyStyles();
            m_Panel.ValidateLayout();
        }

        static int[] Hook(IEditableElement field)
        {
            var counts = new int[2];
            field.editingStarted = () => counts[0]++;
            field.editingEnded = () => counts[1]++;
            return counts;
        }

        static void Pointer(VisualElement target, EventType type)
        {
            var imgui = new Event { type = type, mousePosition = target.worldBound.center, button = 0, clickCount = 1 };
            using (EventBase e = type == EventType.MouseDown ? PointerDownEvent.GetPooled(imgui) : (EventBase)PointerUpEvent.GetPooled(imgui))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        [Fact]
        public void FieldsDeclareTheThreeInterfaces()
        {
            foreach (var type in new[] { typeof(TextField), typeof(Toggle), typeof(Slider), typeof(SliderInt), typeof(MinMaxSlider), typeof(EnumField) })
            {
                Assert.True(typeof(IMixedValueSupport).IsAssignableFrom(type), type.Name);
                Assert.True(typeof(IPrefixLabel).IsAssignableFrom(type), type.Name);
                Assert.True(typeof(IEditableElement).IsAssignableFrom(type), type.Name);
            }
            Assert.False(typeof(IMixedValueSupport).IsAssignableFrom(typeof(Label)));
        }

        [Fact]
        public void MixedValueAndPrefixLabelAreTheFieldsOwnState()
        {
            var field = new TextField("Name") { value = "abc" };
            m_Panel.visualTree.Add(field);
            IMixedValueSupport mixed = field;
            mixed.showMixedValue = true;
            Assert.True(field.showMixedValue);
            Assert.Equal("—", field.text);
            mixed.showMixedValue = false;
            Assert.Equal("abc", field.text);
            IPrefixLabel prefix = field;
            Assert.Equal("Name", prefix.label);
            Assert.Same(field.labelElement, prefix.labelElement);
            field.label = "Renamed";
            Assert.Equal("Renamed", prefix.label);
            IPrefixLabel plain = new TextField();
            Assert.Equal("", plain.label);
        }

        [Fact]
        public void ATextFieldStartsOnFocusAndEndsOnBlurOncePerAttachment()
        {
            var field = new TextField("Name");
            // Handlers set before the first attachment are kept.
            var counts = Hook(field);
            var other = new TextField("Other");
            m_Panel.visualTree.Add(field);
            m_Panel.visualTree.Add(other);
            Tick();
            field.Focus();
            Tick();
            Assert.Equal(new[] { 1, 0 }, counts);
            other.Focus();
            Tick();
            Assert.Equal(new[] { 1, 1 }, counts);
            field.RemoveFromHierarchy();
            m_Panel.visualTree.Add(field);
            field.RemoveFromHierarchy();
            m_Panel.visualTree.Add(field);
            Tick();
            field.Focus();
            Tick();
            Assert.Equal(new[] { 2, 1 }, counts);
            // Removing the focused field ends no edit and leaves nothing focused.
            var focus = field.focusController;
            field.RemoveFromHierarchy();
            Tick();
            Assert.Equal(new[] { 2, 1 }, counts);
            Assert.Null(focus.focusedElement);
        }

        [Fact]
        public void PressFieldsStartAndEndOnTheirPointerEvents()
        {
            var toggle = new Toggle("Flag");
            var slider = new SliderInt("Count", 0, 10);
            var choice = new EnumField("Mode", TextAnchor.UpperLeft);
            foreach (var e in new VisualElement[] { toggle, slider, choice }) m_Panel.visualTree.Add(e);
            Tick();
            var toggleCounts = Hook(toggle);
            var sliderCounts = Hook(slider);
            var choiceCounts = Hook(choice);
            toggle.Focus();
            Tick();
            Assert.Equal(new[] { 0, 0 }, toggleCounts);
            Pointer(toggle, EventType.MouseUp);
            Assert.Equal(new[] { 1, 0 }, toggleCounts);
            slider.Focus();
            Tick();
            Assert.Equal(new[] { 1, 1 }, toggleCounts);
            Assert.Equal(new[] { 0, 0 }, sliderCounts);
            Pointer(slider.labelElement, EventType.MouseDown);
            Pointer(slider.labelElement, EventType.MouseUp);
            Assert.Equal(new[] { 1, 0 }, sliderCounts);
            var drag = slider.Q(className: "unity-base-slider__drag-container");
            Pointer(drag, EventType.MouseDown);
            Pointer(drag, EventType.MouseUp);
            Assert.Equal(new[] { 2, 1 }, sliderCounts);
            // An enum field uses the base field's focus notifications.
            choice.Focus();
            Tick();
            Assert.Equal(new[] { 1, 0 }, choiceCounts);
        }
    }
}
