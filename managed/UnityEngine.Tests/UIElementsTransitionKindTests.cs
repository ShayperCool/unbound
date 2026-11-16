using System.Collections.Generic;
using UnityEngine.UIElements;
using Xunit;
using ClockPanel = UnityEngine.Tests.UIElementsTransitionCallbackTests.ClockPanel;

namespace UnityEngine.Tests
{
    // How transitions treat each kind of value, measured on the Unity 6000.0.71f1 Editor in
    // tests/projects/uitransitionkinds; these tests apply the same rules to cases the capture
    // does not take (other properties, other easings, a refused conversion).
    [Collection("UXML custom controls")]
    public class UIElementsTransitionKindTests
    {
        static void Linear(VisualElement e, string property, EasingMode easing = EasingMode.Linear)
        {
            e.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName(property) };
            e.style.transitionDuration = new List<TimeValue> { new TimeValue(1000, TimeUnit.Millisecond) };
            e.style.transitionTimingFunction = new List<EasingFunction> { easing };
        }

        static List<string> Runs(VisualElement e)
        {
            var runs = new List<string>();
            e.RegisterCallback<TransitionRunEvent>(t => { foreach (var n in t.stylePropertyNames) runs.Add(n.ToString()); });
            return runs;
        }

        // A min-height percentage is of the parent's height (here 200, not its width 400): 50px is
        // 25%, so halfway to 50% is 37.5% = 75px.
        [Fact]
        public void MinHeightConvertsAgainstTheParentsHeight()
        {
            using var p = new ClockPanel();
            var parent = new VisualElement();
            parent.style.width = 400;
            parent.style.height = 200;
            var child = new VisualElement();
            child.style.minHeight = 50;
            child.style.flexShrink = 0;
            parent.Add(child);
            p.root.Add(parent);
            Linear(child, "min-height");
            p.Tick(0);
            child.style.minHeight = Length.Percent(50);
            p.Tick(0);
            p.Tick(500);
            Assert.Equal(75, child.resolvedStyle.minHeight.value);
        }

        // A translate whose percentage has nothing to be of (an element of no width) does not run:
        // the value is taken at once and no usage hint is added. A started one adds DynamicTransform.
        [Fact]
        public void ARefusedConversionStartsNothingAndAddsNoHint()
        {
            using var p = new ClockPanel();
            p.a.style.width = 0;
            Linear(p.a, "translate");
            var runs = Runs(p.a);
            p.Tick(0);
            p.a.style.translate = new Translate(Length.Percent(50), 0);
            p.Tick(0);
            Assert.Empty(runs);
            Assert.Equal(UsageHints.None, p.a.usageHints);
            Linear(p.b, "translate");
            p.b.usageHints = UsageHints.MaskContainer;
            var started = Runs(p.b);
            p.Tick(0);
            p.b.style.translate = new Translate(Length.Percent(50), 0);
            p.Tick(0);
            Assert.Equal(new[] { "translate" }, started);
            Assert.Equal(UsageHints.MaskContainer | UsageHints.DynamicTransform, p.b.usageHints);
        }

        // A discrete value switches when the eased progress reaches one half: with ease-in that is
        // after the halfway time.
        [Fact]
        public void DiscreteValuesSwitchAtHalfTheEasedProgress()
        {
            using var p = new ClockPanel();
            p.a.style.whiteSpace = WhiteSpace.Normal;
            Linear(p.a, "white-space", EasingMode.EaseIn);
            p.Tick(0);
            p.a.style.whiteSpace = WhiteSpace.NoWrap;
            p.Tick(0);
            p.Tick(500);
            Assert.Equal(WhiteSpace.Normal, p.a.resolvedStyle.whiteSpace);
            p.Tick(1000);
            Assert.Equal(WhiteSpace.NoWrap, p.a.resolvedStyle.whiteSpace);
        }

        // A keyword background size's components are 0px, so contain to pixels runs from 0
        // (bgSizeCover*: cover to pixels and to contain); one with an auto component never runs.
        [Fact]
        public void KeywordBackgroundSizesTransitionFromZero()
        {
            using var p = new ClockPanel();
            p.a.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            Linear(p.a, "background-size");
            var runs = Runs(p.a);
            p.Tick(0);
            p.a.style.backgroundSize = new BackgroundSize(80, 60);
            p.Tick(0);
            p.Tick(250);
            Assert.Equal(new[] { "background-size" }, runs);
            Assert.Equal(20, p.a.resolvedStyle.backgroundSize.x.value);
            Assert.Equal(15, p.a.resolvedStyle.backgroundSize.y.value);
            Linear(p.b, "background-size");
            p.b.style.backgroundSize = new BackgroundSize(40, Length.Auto());
            var autoRuns = Runs(p.b);
            p.Tick(250);
            p.b.style.backgroundSize = new BackgroundSize(80, Length.Auto());
            p.Tick(250);
            Assert.Empty(autoRuns);
            Assert.Equal(80, p.b.resolvedStyle.backgroundSize.x.value);
        }

        // An inline percent font size keeps its number; a child inherits the percentage and its own
        // style pass takes it of that number (fontPercentChildren: 50% gives the child 25), also
        // after another inline value replaced it and was set back.
        [Fact]
        public void InlinePercentFontSizesKeepTheirNumber()
        {
            using var p = new ClockPanel();
            p.root.style.fontSize = 20;
            var child = new VisualElement();
            p.a.Add(child);
            p.a.style.fontSize = Length.Percent(30);
            p.Tick(0);
            Assert.Equal(30, p.a.resolvedStyle.fontSize);
            Assert.Equal(9, child.resolvedStyle.fontSize);
            p.a.style.fontSize = 12;
            p.Tick(0);
            Assert.Equal(12, child.resolvedStyle.fontSize);
            p.a.style.fontSize = Length.Percent(30);
            p.Tick(0);
            Assert.Equal(30, p.a.resolvedStyle.fontSize);
            Assert.Equal(9, child.resolvedStyle.fontSize);
        }
    }
}
