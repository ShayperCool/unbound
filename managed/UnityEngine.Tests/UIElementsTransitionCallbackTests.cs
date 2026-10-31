using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    // Expected sequences and callback-visible values come from the clock-driven 6000.0.71f1
    // oracle in tests/projects/uitransitioncallbacks, not an assumed internal Unity mechanism.
    public class UIElementsTransitionCallbackTests
    {
        sealed class ClockPanel : IDisposable
        {
            static readonly BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            static readonly Type Type = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel");
            readonly PropertyInfo clockProperty = Type.GetProperty("TimeSinceStartup", F);
            readonly object saved, panel;
            long now = 10000;
            public readonly VisualElement root, a, b;
            public ClockPanel()
            {
                saved = clockProperty.GetValue(null);
                clockProperty.SetValue(null, Delegate.CreateDelegate(clockProperty.PropertyType, this, typeof(ClockPanel).GetMethod(nameof(Clock), F)));
                var d = typeof(EventDispatcher).GetMethod("CreateDefault", F).Invoke(null, null);
                panel = Type.GetConstructors(F).First(c => c.GetParameters().Length == 4).Invoke(new object[] { null, ContextType.Player, d, null });
                root = ((IPanel)panel).visualTree; root.style.width = 800; root.style.height = 800;
                a = Element("a"); b = Element("b"); root.Add(a); root.Add(b); Tick(0);
            }
            long Clock() => now;
            static VisualElement Element(string name)
            {
                var e = new VisualElement { name = name };
                e.style.width = 100; e.style.height = 50; e.style.opacity = 1; return e;
            }
            public void Configure(VisualElement e, string properties)
            {
                e.style.transitionProperty = properties.Split(',').Select(p => new StylePropertyName(p.Trim())).ToList();
                e.style.transitionDuration = new List<TimeValue> { new TimeValue(1000, TimeUnit.Millisecond) };
                e.style.transitionTimingFunction = new List<EasingFunction> { EasingMode.Linear };
            }
            public void Tick(long ms)
            {
                now = 10000 + ms;
                Type.GetMethod("ApplyStyles", F).Invoke(panel, null);
                Type.GetMethod("UpdateAnimations", F).Invoke(panel, null);
                Type.GetMethod("ValidateLayout", F).Invoke(panel, null);
            }
            public void Dispose() { ((IDisposable)panel).Dispose(); clockProperty.SetValue(null, saved); }
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CallbackRetargetsALaterTypeWithinTheSameTick(bool onStart)
        {
            using var p = new ClockPanel();
            p.Configure(p.a, "opacity"); p.Configure(p.b, "width"); p.Tick(0);
            if (onStart) p.a.RegisterCallback<TransitionStartEvent>(e => p.b.style.width = 200);
            else p.a.RegisterCallback<TransitionRunEvent>(e => p.b.style.width = 200);
            p.a.style.opacity = 0; p.b.style.width = 300; p.Tick(0); p.Tick(100);
            Assert.Equal(110, p.b.resolvedStyle.width);
        }
        [Fact]
        public void CallbackCanRegisterAndUnregisterSubsequentEndObservers()
        {
            using var p = new ClockPanel(); p.Configure(p.a, "opacity"); p.Tick(0);
            var events = new List<string>();
            EventCallback<TransitionEndEvent> old = e => events.Add("old");
            p.a.RegisterCallback(old);
            p.a.RegisterCallback<TransitionRunEvent>(e => {
                p.a.UnregisterCallback(old); p.a.RegisterCallback<TransitionEndEvent>(x => events.Add("new"));
            });
            p.a.style.opacity = 0; p.Tick(0); p.Tick(1000);
            Assert.Equal(new[] { "new" }, events);
        }
        [Fact]
        public void DetachingDuringRunSuppressesPendingStart()
        {
            using var p = new ClockPanel(); p.Configure(p.a, "opacity"); p.Tick(0);
            var events = new List<string>();
            p.a.RegisterCallback<TransitionRunEvent>(e => { events.Add("run"); p.a.RemoveFromHierarchy(); });
            p.a.RegisterCallback<TransitionStartEvent>(e => events.Add("start"));
            p.a.style.opacity = 0; p.Tick(0); p.Tick(1000);
            Assert.Equal(new[] { "run" }, events);
        }
        [Fact]
        public void CurrentTypeIsSampledAcrossOwnersBeforeEndCallbacks()
        {
            using var p = new ClockPanel();
            p.Configure(p.a, "width, opacity"); p.Configure(p.b, "width, opacity"); p.Tick(0);
            p.a.style.opacity = 0; p.a.style.width = 300;
            p.b.style.opacity = 0; p.b.style.width = 300;
            p.Tick(0); p.Tick(500);
            var values = new List<float>();
            p.a.RegisterCallback<TransitionEndEvent>(e => {
                if (e.AffectsProperty("opacity")) values.AddRange(new[] { p.b.resolvedStyle.opacity, p.b.resolvedStyle.width });
            });
            p.Tick(1000);
            Assert.Equal(new[] { 0f, 200f }, values);
        }
        [Fact]
        public void EachTypeKeepsItsOwnOwnerStartOrder()
        {
            using var p = new ClockPanel();
            p.Configure(p.a, "width, opacity"); p.Configure(p.b, "width, opacity"); p.Tick(0);
            p.a.style.width = 300; p.Tick(0);
            p.b.style.opacity = 0; p.Tick(0);
            p.a.style.opacity = 0; p.b.style.width = 300; p.Tick(0); p.Tick(500);
            var events = new List<string>();
            foreach (var owner in new[] { p.a, p.b })
                owner.RegisterCallback<TransitionEndEvent>(e => events.Add(owner.name + "/" + e.stylePropertyNames.First()));
            p.Tick(1000);
            Assert.Equal(new[] { "a/width", "b/width", "b/opacity", "a/opacity" }, events);
        }
        [Fact]
        public void RemovingAPropertyInRunPreservesTheCurrentBatch()
        {
            using var p = new ClockPanel(); p.Configure(p.a, "width, opacity"); p.Tick(0);
            var events = new List<string>();
            p.a.RegisterCallback<TransitionRunEvent>(e => {
                events.Add("run/" + e.stylePropertyNames.First());
                if (e.AffectsProperty("opacity")) p.a.style.transitionProperty = new List<StylePropertyName> { "opacity" };
            });
            p.a.RegisterCallback<TransitionStartEvent>(e => events.Add("start/" + e.stylePropertyNames.First()));
            p.a.RegisterCallback<TransitionCancelEvent>(e => events.Add("cancel/" + e.stylePropertyNames.First()));
            p.a.style.opacity = 0; p.a.style.width = 300; p.Tick(0);
            Assert.Equal(new[] { "run/opacity", "start/opacity", "run/width", "start/width" }, events);
            p.Tick(100);
            Assert.Equal("cancel/width", events.Last());
        }
        [Fact]
        public void RetargetingAnotherOwnerDuringCancelWaitsForTheNextBatch()
        {
            using var p = new ClockPanel(); p.Configure(p.a, "opacity"); p.Configure(p.b, "opacity"); p.Tick(0);
            p.a.style.opacity = 0; p.b.style.opacity = 0; p.Tick(0); p.Tick(250);
            var events = new List<string>();
            p.a.RegisterCallback<TransitionCancelEvent>(e => { events.Add("a"); p.b.style.opacity = .5f; });
            p.b.RegisterCallback<TransitionCancelEvent>(e => events.Add("b"));
            p.a.style.opacity = .25f; p.Tick(250);
            Assert.Equal(new[] { "a" }, events);
            p.Tick(500);
            Assert.Equal(new[] { "a", "b" }, events);
        }
        [Fact]
        public void PickingDuringRunDoesNotAdvanceALaterType()
        {
            using var p = new ClockPanel(); p.Configure(p.a, "opacity"); p.Configure(p.b, "width"); p.Tick(0);
            float width = -1;
            p.a.RegisterCallback<TransitionRunEvent>(e => {
                p.b.style.width = 200; p.root.panel.Pick(new Vector2(10, 10)); width = p.b.resolvedStyle.width;
            });
            p.a.style.opacity = 0; p.b.style.width = 300; p.Tick(0);
            Assert.Equal(100, width);
            p.Tick(100); Assert.Equal(110, p.b.resolvedStyle.width);
        }
        [Fact]
        public void DetachingInstallsTheDestinationButPreservesTheLastLayout()
        {
            using var p = new ClockPanel(); p.Configure(p.a, "width, opacity"); p.Tick(0);
            p.a.RegisterCallback<TransitionRunEvent>(e => p.a.RemoveFromHierarchy());
            p.a.style.opacity = 0; p.a.style.width = 300; p.Tick(0);
            Assert.Null(p.a.panel);
            Assert.Equal(0, p.a.resolvedStyle.opacity);
            Assert.Equal(100, p.a.resolvedStyle.width);
        }
    }
}
