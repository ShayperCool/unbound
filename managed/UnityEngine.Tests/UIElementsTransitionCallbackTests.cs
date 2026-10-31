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
    }
}
