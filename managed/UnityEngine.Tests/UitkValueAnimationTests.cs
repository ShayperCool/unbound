using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;
using Xunit;

// Transition animations (VisualElement.experimental.animation) on a panel whose clock the tests
// drive. [doc] Unity 6000.0 Scripting API "ValueAnimation<T0>", "ITransitionAnimations", "Easing":
// durationMs, easingCurve (OutQuad by default), onAnimationCompleted, autoRecycle/KeepAlive,
// Recycle. The probe (tests/projects/uitkcollections, animations section) records the Editor's
// completion, recycling and StyleValues behaviour; these tests check the timing in between.
namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UitkValueAnimationTests
    {
        sealed class ClockedPanel : IDisposable
        {
            static readonly PropertyInfo s_Clock = typeof(Panel).GetProperty("TimeSinceStartup", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            readonly object m_Saved;
            public long now;
            public readonly Panel panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);

            public ClockedPanel()
            {
                m_Saved = s_Clock.GetValue(null);
                s_Clock.SetValue(null, Delegate.CreateDelegate(s_Clock.PropertyType, this, typeof(ClockedPanel).GetMethod(nameof(Clock), BindingFlags.Instance | BindingFlags.NonPublic)));
                panel.visualTree.style.width = 200;
                panel.visualTree.style.height = 200;
            }

            long Clock() => now;

            public void Update()
            {
                panel.uaValueAnimations.Update();
                panel.ApplyStyles();
                panel.ValidateLayout();
            }

            public void Dispose() => s_Clock.SetValue(null, m_Saved);
        }

        [Fact]
        public void ValuesFollowTheEasedProgressOnThePanelClock()
        {
            using var c = new ClockedPanel { now = 1000 };
            var box = new VisualElement();
            c.panel.visualTree.Add(box);
            var values = new List<float>();
            int completed = 0;
            var anim = box.experimental.animation.Start(0f, 100f, 400, (e, v) => values.Add(v)).OnCompleted(() => completed++);
            c.Update();
            c.now = 1200;
            c.Update();
            c.Update(); // the same time: no new value
            c.now = 1400;
            c.Update();
            Assert.Equal(new[] { 0f, 75f, 100f }, values);
            Assert.Equal(1, completed);
            Assert.False(anim.isRunning);
            // Auto-recycled: the object refuses further use.
            Assert.Throws<InvalidOperationException>(() => anim.Start());
        }

        [Fact]
        public void KeptAnimationsRestartAndCustomEasingApplies()
        {
            using var c = new ClockedPanel { now = 0 };
            var box = new VisualElement();
            c.panel.visualTree.Add(box);
            var values = new List<float>();
            var anim = box.experimental.animation.Start(0f, 10f, 100, (e, v) => values.Add(v)).Ease(Easing.Linear).KeepAlive();
            c.now = 50;
            c.Update();
            c.now = 100;
            c.Update();
            Assert.False(anim.isRunning);
            anim.Start();
            c.now = 150;
            c.Update();
            Assert.Equal(new[] { 5f, 10f, 5f }, values);
            anim.Stop();
            anim.Recycle();
            Assert.Throws<InvalidOperationException>(() => anim.Stop());
        }

        [Fact]
        public void AnimationsTickOnlyWhileTheirElementIsInAPanel()
        {
            using var c = new ClockedPanel { now = 0 };
            var box = new VisualElement();
            var values = new List<float>();
            var anim = box.experimental.animation.Start(0f, 10f, 100, (e, v) => values.Add(v)).Ease(Easing.Linear);
            c.now = 50;
            c.Update();
            Assert.Empty(values);
            Assert.True(anim.isRunning);
            c.panel.visualTree.Add(box);
            c.Update();
            box.RemoveFromHierarchy();
            c.now = 80;
            c.Update();
            Assert.Equal(new[] { 5f }, values);
        }

        [Fact]
        public void StyleValuesAnimateFromTheResolvedStyle()
        {
            using var c = new ClockedPanel { now = 0 };
            var box = new VisualElement();
            box.style.paddingTop = 4;
            c.panel.visualTree.Add(box);
            c.Update();
            box.experimental.animation.Start(new StyleValues { paddingTop = 20, opacity = 0f }, 100).Ease(Easing.Linear);
            c.Update();
            c.now = 50;
            c.Update();
            Assert.Equal(12f, box.style.paddingTop.value.value);
            Assert.Equal(0.5f, box.style.opacity.value);
            c.now = 100;
            c.Update();
            Assert.Equal(20f, box.style.paddingTop.value.value);
            Assert.Equal(0f, box.style.opacity.value);
        }

        [Fact]
        public void StyleValuesMustStartWithTheEndProperties()
        {
            using var c = new ClockedPanel { now = 0 };
            var box = new VisualElement();
            c.panel.visualTree.Add(box);
            var anim = box.experimental.animation.Start(new StyleValues { top = 1 }, new StyleValues { top = 2, left = 3 }, 10);
            Assert.Throws<ArgumentException>(() => c.Update());
            anim.Stop();
        }

        [Fact]
        public void EasingCurvesMatchTheirEquations()
        {
            Assert.Equal(0.75f, Easing.OutQuad(0.5f));
            Assert.Equal(0.125f, Easing.InOutQuad(0.25f));
            Assert.Equal(0.875f, Easing.OutCubic(0.5f), 5);
            Assert.Equal(0.765625f, Easing.OutBounce(0.5f), 5);
            Assert.Equal(1f, Easing.Step(0.5f));
            Assert.Equal(0f, Easing.Step(0.49f));
            Assert.Equal(0f, Easing.InElastic(0f));
            Assert.Equal(1f, Easing.OutElastic(1f));
            Assert.Equal(-0.0876975f, Easing.InBack(0.5f), 5);
        }
    }
}
