using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;
using Xunit;
using ClockPanel = UnityEngine.Tests.UIElementsTransitionCallbackTests.ClockPanel;

namespace UnityEngine.Tests
{
    // The internal style-property animation interface, measured on the Unity 6000.0.71f1 Editor in
    // tests/projects/styleanimations; these tests apply its rules to cases the capture does not
    // take: USS and interface animations of one property, other value kinds, refused property ids,
    // inline restyles and leaving the panel.
    [Collection("UXML custom controls")]
    public class UitkStyleAnimationTests
    {
        static readonly Func<float, float> Linear = t => t;
        static IStylePropertyAnimations A(VisualElement e) => e;

        static List<string> Events(VisualElement e)
        {
            var events = new List<string>();
            e.RegisterCallback<TransitionRunEvent>(t => events.Add("run"));
            e.RegisterCallback<TransitionEndEvent>(t => events.Add("end"));
            e.RegisterCallback<TransitionCancelEvent>(t => events.Add("cancel"));
            return events;
        }

        // A USS transition and an interface start share one animation per property: reversing a
        // running USS transition through the interface shortens it by the progress travelled
        // (1 -> 0 halfway, back to 1 in 500 ms), as a USS reversal does.
        [Fact]
        public void TheInterfaceRetargetsARunningUssTransition()
        {
            using var p = new ClockPanel();
            p.Configure(p.a, "opacity");
            p.Tick(0);
            p.a.style.opacity = 0;
            p.Tick(0);
            p.Tick(500);
            Assert.Equal(1, A(p.a).runningAnimationCount);
            Assert.True(A(p.a).Start(StylePropertyId.Opacity, 0.2f, 1f, 1000, 0, Linear));
            Assert.Equal(1, A(p.a).runningAnimationCount);
            p.Tick(750);
            Assert.Equal(0.75f, p.a.resolvedStyle.opacity);
            p.Tick(1000);
            Assert.Equal(0, A(p.a).runningAnimationCount);
            Assert.Equal(1, A(p.a).completedAnimationCount);
        }

        // A completed USS transition refuses an interface start to its own end value and gives way
        // to one with another end.
        [Fact]
        public void ACompletedTransitionRefusesItsOwnEndValue()
        {
            using var p = new ClockPanel();
            p.Configure(p.a, "opacity");
            p.Tick(0);
            p.a.style.opacity = 0.25f;
            p.Tick(0);
            p.Tick(1000);
            Assert.Equal(1, A(p.a).completedAnimationCount);
            Assert.False(A(p.a).Start(StylePropertyId.Opacity, 1f, 0.25f, 1000, 0, Linear));
            Assert.True(A(p.a).Start(StylePropertyId.Opacity, 1f, 0.5f, 1000, 0, Linear));
            Assert.Equal(0, A(p.a).completedAnimationCount);
            Assert.Equal(1, A(p.a).runningAnimationCount);
        }

        // The enum start is discrete for an integer property too; the integer start rounds to even.
        [Fact]
        public void TheEnumStartIsDiscreteForIntegers()
        {
            using var p = new ClockPanel();
            Assert.True(A(p.a).StartEnum(StylePropertyId.UnitySliceTop, 0, 6, 1000, 0, Linear));
            Assert.True(A(p.b).Start(StylePropertyId.UnitySliceTop, 0, 5, 1000, 0, Linear));
            p.Tick(0);
            p.Tick(499);
            Assert.Equal(0, p.a.resolvedStyle.unitySliceTop);
            Assert.Equal(2, p.b.resolvedStyle.unitySliceTop);
            p.Tick(500);
            Assert.Equal(6, p.a.resolvedStyle.unitySliceTop);
            Assert.Equal(2, p.b.resolvedStyle.unitySliceTop);
            p.Tick(700);
            Assert.Equal(4, p.b.resolvedStyle.unitySliceTop);
        }

        // A value the property cannot take, or a property with no animation, is refused at once.
        [Fact]
        public void AValueOfAnotherKindIsRefused()
        {
            using var p = new ClockPanel();
            var wrongKind = Assert.Throws<ArgumentException>(() => A(p.a).Start(StylePropertyId.Width, 1f, 2f, 1000, 0, Linear));
            Assert.StartsWith("Invalid animation property id. Can't apply value of type 'float' to property 'Width'.", wrongKind.Message);
            Assert.Throws<ArgumentException>(() => A(p.a).Start(StylePropertyId.Display, 0, 1, 1000, 0, Linear));
            Assert.Throws<ArgumentException>(() => A(p.a).Start(StylePropertyId.Opacity, new Length(1), new Length(2), 1000, 0, Linear));
            Assert.Throws<ArgumentNullException>(() => A(p.a).Start(StylePropertyId.Opacity, 1f, 0f, 1000, 0, null));
            Assert.Equal(0, A(p.a).runningAnimationCount);
        }

        // An inline change of another property is no style pass over the transition list: the
        // interface animation keeps running and showing its value.
        [Fact]
        public void AnInlineChangeKeepsAnUnlistedAnimation()
        {
            using var p = new ClockPanel();
            var events = Events(p.a);
            Assert.True(A(p.a).Start(StylePropertyId.Opacity, 1f, 0f, 1000, 0, Linear));
            p.Tick(0);
            p.Tick(500);
            p.a.style.width = 120;
            Assert.True(A(p.a).HasRunningAnimation(StylePropertyId.Opacity));
            p.Tick(750);
            Assert.Equal(0.25f, p.a.resolvedStyle.opacity);
            Assert.Equal(new[] { "run" }, events);
        }

        // Leaving the panel cancels the element's animations and forgets the completed ones, without
        // a Cancel event (as for USS transitions, tests/projects/uitransitions); the other element keeps its own.
        [Fact]
        public void LeavingThePanelClearsTheCounts()
        {
            using var p = new ClockPanel();
            var events = Events(p.a);
            A(p.a).Start(StylePropertyId.Opacity, 1f, 0f, 1000, 0, Linear);
            A(p.a).Start(StylePropertyId.BackgroundColor, Color.red, Color.blue, 100, 0, Linear);
            A(p.b).Start(StylePropertyId.Opacity, 1f, 0f, 1000, 0, Linear);
            p.Tick(0);
            p.Tick(200);
            Assert.Equal(1, A(p.a).runningAnimationCount);
            Assert.Equal(1, A(p.a).completedAnimationCount);
            p.a.RemoveFromHierarchy();
            Assert.Equal(0, A(p.a).runningAnimationCount);
            Assert.Equal(0, A(p.a).completedAnimationCount);
            Assert.Equal(0f, p.a.resolvedStyle.opacity);
            Assert.Equal(1, A(p.b).runningAnimationCount);
            Assert.DoesNotContain("cancel", events);
            Assert.Throws<NullReferenceException>(() => A(p.a).Start(StylePropertyId.Opacity, 1f, 0f, 1000, 0, Linear));
        }
    }
}
