using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;

namespace Unbound.UIElements
{
    // Target values stay separate from the presented style. Restyling an ancestor must not restart
    // a child's transition, and interruption starts at the last presented value, not its old target.
    internal sealed class StyleTransitions
    {
        sealed class Property
        {
            internal StylePropertyId id;
            internal string name;
            internal Type type;
            internal Func<ComputedValues, object> get;
            internal Action<ComputedValues, object> set;
            internal Func<object, object, float, object> interpolate;
        }
        sealed class Animation
        {
            internal Property property;
            internal object from, to, reversingStart;
            internal long start, sampled;
            internal int duration, delay;
            internal float reversingFactor = 1, progress;
            internal EasingMode easing;
            internal bool started;
        }
        readonly VisualElement owner;
        readonly Dictionary<StylePropertyId, Animation> running = new Dictionary<StylePropertyId, Animation>();
        readonly Dictionary<StylePropertyId, List<EventBase>> pending = new Dictionary<StylePropertyId, List<EventBase>>();
        readonly List<EventBase> dispatching = new List<EventBase>();

        internal ComputedValues target;
        internal bool active => running.Count > 0 || pending.Count > 0;
        internal StyleTransitions(VisualElement owner) { this.owner = owner; }

        static Property P<T>(string name, Func<ComputedValues, T> get, Action<ComputedValues, T> set, Func<T, T, float, T> lerp)
            => new Property { name = name, type = typeof(T), id = StyleProperties.IdOf(name), get = s => get(s), set = (s, v) => set(s, (T)v), interpolate = (a, b, t) => lerp((T)a, (T)b, t) };
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static Length Lerp(Length a, Length b, float t) => new Length(Lerp(a.value, b.value, t), b.unit);
        static Color Lerp(Color a, Color b, float t) => new Color(Lerp(a.r, b.r, t), Lerp(a.g, b.g, t), Lerp(a.b, b.b, t), Lerp(a.a, b.a, t));
        static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
        static T Discrete<T>(T a, T b, float t) => t < .5f ? a : b;
        static readonly Property[] properties = CreateProperties();
        static Property[] CreateProperties()
        {
            var result = new[]
        {
            P<Align>("align-content", s => s.alignContent, (s, v) => s.alignContent = v, Discrete),
            P<Align>("align-items", s => s.alignItems, (s, v) => s.alignItems = v, Discrete),
            P<Align>("align-self", s => s.alignSelf, (s, v) => s.alignSelf = v, Discrete),
            P<Color>("background-color", s => s.backgroundColor, (s, v) => s.backgroundColor = v, Lerp),
            P<Background>("background-image", s => s.backgroundImage, (s, v) => s.backgroundImage = v, Discrete),
            P<BackgroundPosition>("background-position-x", s => s.backgroundPositionX, (s, v) => s.backgroundPositionX = v, (a, b, t) => new BackgroundPosition(t < .5f ? a.keyword : b.keyword, Lerp(a.offset, b.offset, t))),
            P<BackgroundPosition>("background-position-y", s => s.backgroundPositionY, (s, v) => s.backgroundPositionY = v, (a, b, t) => new BackgroundPosition(t < .5f ? a.keyword : b.keyword, Lerp(a.offset, b.offset, t))),
            P<BackgroundRepeat>("background-repeat", s => s.backgroundRepeat, (s, v) => s.backgroundRepeat = v, Discrete),
            P<BackgroundSize>("background-size", s => s.backgroundSize, (s, v) => s.backgroundSize = v, (a, b, t) => new BackgroundSize(Lerp(a.x, b.x, t), Lerp(a.y, b.y, t))),
            P<Color>("border-bottom-color", s => s.borderBottomColor, (s, v) => s.borderBottomColor = v, Lerp),
            P<Length>("border-bottom-left-radius", s => s.borderBottomLeftRadius, (s, v) => s.borderBottomLeftRadius = v, Lerp),
            P<Length>("border-bottom-right-radius", s => s.borderBottomRightRadius, (s, v) => s.borderBottomRightRadius = v, Lerp),
            P<float>("border-bottom-width", s => s.borderBottomWidth, (s, v) => s.borderBottomWidth = v, Lerp),
            P<Color>("border-left-color", s => s.borderLeftColor, (s, v) => s.borderLeftColor = v, Lerp),
            P<float>("border-left-width", s => s.borderLeftWidth, (s, v) => s.borderLeftWidth = v, Lerp),
            P<Color>("border-right-color", s => s.borderRightColor, (s, v) => s.borderRightColor = v, Lerp),
            P<float>("border-right-width", s => s.borderRightWidth, (s, v) => s.borderRightWidth = v, Lerp),
            P<Color>("border-top-color", s => s.borderTopColor, (s, v) => s.borderTopColor = v, Lerp),
            P<Length>("border-top-left-radius", s => s.borderTopLeftRadius, (s, v) => s.borderTopLeftRadius = v, Lerp),
            P<Length>("border-top-right-radius", s => s.borderTopRightRadius, (s, v) => s.borderTopRightRadius = v, Lerp),
            P<float>("border-top-width", s => s.borderTopWidth, (s, v) => s.borderTopWidth = v, Lerp),
            P<Length>("bottom", s => s.bottom, (s, v) => s.bottom = v, Lerp),
            P<Color>("color", s => s.color, (s, v) => s.color = v, Lerp),
            P<Length>("flex-basis", s => s.flexBasis, (s, v) => s.flexBasis = v, Lerp),
            P<FlexDirection>("flex-direction", s => s.flexDirection, (s, v) => s.flexDirection = v, Discrete),
            P<float>("flex-grow", s => s.flexGrow, (s, v) => s.flexGrow = v, Lerp),
            P<float>("flex-shrink", s => s.flexShrink, (s, v) => s.flexShrink = v, Lerp),
            P<Wrap>("flex-wrap", s => s.flexWrap, (s, v) => s.flexWrap = v, Discrete),
            P<Length>("font-size", s => s.fontSize, (s, v) => s.fontSize = v, Lerp),
            P<Length>("height", s => s.height, (s, v) => s.height = v, Lerp),
            P<Justify>("justify-content", s => s.justifyContent, (s, v) => s.justifyContent = v, Discrete),
            P<Length>("left", s => s.left, (s, v) => s.left = v, Lerp),
            P<Length>("letter-spacing", s => s.letterSpacing, (s, v) => s.letterSpacing = v, Lerp),
            P<Length>("margin-bottom", s => s.marginBottom, (s, v) => s.marginBottom = v, Lerp),
            P<Length>("margin-left", s => s.marginLeft, (s, v) => s.marginLeft = v, Lerp),
            P<Length>("margin-right", s => s.marginRight, (s, v) => s.marginRight = v, Lerp),
            P<Length>("margin-top", s => s.marginTop, (s, v) => s.marginTop = v, Lerp),
            P<Length>("max-height", s => s.maxHeight, (s, v) => s.maxHeight = v, Lerp),
            P<Length>("max-width", s => s.maxWidth, (s, v) => s.maxWidth = v, Lerp),
            P<Length>("min-height", s => s.minHeight, (s, v) => s.minHeight = v, Lerp),
            P<Length>("min-width", s => s.minWidth, (s, v) => s.minWidth = v, Lerp),
            P<float>("opacity", s => s.opacity, (s, v) => s.opacity = v, Lerp),
            P<OverflowInternal>("overflow", s => s.overflow, (s, v) => s.overflow = v, Discrete),
            P<Length>("padding-bottom", s => s.paddingBottom, (s, v) => s.paddingBottom = v, Lerp),
            P<Length>("padding-left", s => s.paddingLeft, (s, v) => s.paddingLeft = v, Lerp),
            P<Length>("padding-right", s => s.paddingRight, (s, v) => s.paddingRight = v, Lerp),
            P<Length>("padding-top", s => s.paddingTop, (s, v) => s.paddingTop = v, Lerp),
            P<Position>("position", s => s.position, (s, v) => s.position = v, Discrete),
            P<Length>("right", s => s.right, (s, v) => s.right = v, Lerp),
            P<Rotate>("rotate", s => s.rotate, (s, v) => s.rotate = v, (a, b, t) => new Rotate(new Angle(Lerp(a.angle.ToDegrees(), b.angle.ToDegrees(), t)), Lerp(a.axis, b.axis, t))),
            P<Scale>("scale", s => s.scale, (s, v) => s.scale = v, (a, b, t) => new Scale(Lerp(a.value, b.value, t))),
            P<TextShadow>("text-shadow", s => s.textShadow, (s, v) => s.textShadow = v, (a, b, t) => new TextShadow { offset = Vector2.LerpUnclamped(a.offset, b.offset, t), blurRadius = Lerp(a.blurRadius, b.blurRadius, t), color = Lerp(a.color, b.color, t) }),
            P<Length>("top", s => s.top, (s, v) => s.top = v, Lerp),
            P<TransformOrigin>("transform-origin", s => s.transformOrigin, (s, v) => s.transformOrigin = v, (a, b, t) => new TransformOrigin(Lerp(a.x, b.x, t), Lerp(a.y, b.y, t), Lerp(a.z, b.z, t))),
            P<Translate>("translate", s => s.translate, (s, v) => s.translate = v, (a, b, t) => new Translate(Lerp(a.x, b.x, t), Lerp(a.y, b.y, t), Lerp(a.z, b.z, t))),
            P<Color>("-unity-background-image-tint-color", s => s.unityBackgroundImageTintColor, (s, v) => s.unityBackgroundImageTintColor = v, Lerp),
            P<Font>("-unity-font", s => s.unityFont, (s, v) => s.unityFont = v, Discrete),
            P<FontDefinition>("-unity-font-definition", s => s.unityFontDefinition, (s, v) => s.unityFontDefinition = v, Discrete),
            P<FontStyle>("-unity-font-style", s => s.unityFontStyleAndWeight, (s, v) => s.unityFontStyleAndWeight = v, Discrete),
            P<OverflowClipBox>("-unity-overflow-clip-box", s => s.unityOverflowClipBox, (s, v) => s.unityOverflowClipBox = v, Discrete),
            P<Length>("-unity-paragraph-spacing", s => s.unityParagraphSpacing, (s, v) => s.unityParagraphSpacing = v, Lerp),
            P<int>("-unity-slice-bottom", s => s.unitySliceBottom, (s, v) => s.unitySliceBottom = v, (a, b, t) => (int)Math.Round(Lerp(a, b, t))),
            P<int>("-unity-slice-left", s => s.unitySliceLeft, (s, v) => s.unitySliceLeft = v, (a, b, t) => (int)Math.Round(Lerp(a, b, t))),
            P<int>("-unity-slice-right", s => s.unitySliceRight, (s, v) => s.unitySliceRight = v, (a, b, t) => (int)Math.Round(Lerp(a, b, t))),
            P<float>("-unity-slice-scale", s => s.unitySliceScale, (s, v) => s.unitySliceScale = v, Lerp),
            P<int>("-unity-slice-top", s => s.unitySliceTop, (s, v) => s.unitySliceTop = v, (a, b, t) => (int)Math.Round(Lerp(a, b, t))),
            P<SliceType>("-unity-slice-type", s => s.unitySliceType, (s, v) => s.unitySliceType = v, Discrete),
            P<TextAnchor>("-unity-text-align", s => s.unityTextAlign, (s, v) => s.unityTextAlign = v, Discrete),
            P<Color>("-unity-text-outline-color", s => s.unityTextOutlineColor, (s, v) => s.unityTextOutlineColor = v, Lerp),
            P<float>("-unity-text-outline-width", s => s.unityTextOutlineWidth, (s, v) => s.unityTextOutlineWidth = v, Lerp),
            P<TextOverflowPosition>("-unity-text-overflow-position", s => s.unityTextOverflowPosition, (s, v) => s.unityTextOverflowPosition = v, Discrete),
            P<Visibility>("visibility", s => s.visibility, (s, v) => s.visibility = v, Discrete),
            P<Length>("width", s => s.width, (s, v) => s.width = v, Lerp),
            P<Length>("word-spacing", s => s.wordSpacing, (s, v) => s.wordSpacing = v, Lerp),
        };
            Array.Sort(result, (a, b) => a.id.CompareTo(b.id));
            return result;
        }
        static int Milliseconds(TimeValue value) => (int)Math.Round(value.value * (value.unit == TimeUnit.Second ? 1000 : 1));
        static T At<T>(List<T> values, int index, T fallback) => values != null && values.Count > 0 ? values[index % values.Count] : fallback;
        static bool Matches(StylePropertyId match, Property p)
        {
            if (match == StylePropertyId.All || match == p.id) return true;
            switch (match)
            {
                case StylePropertyId.Margin: return p.name.StartsWith("margin-");
                case StylePropertyId.Padding: return p.name.StartsWith("padding-");
                case StylePropertyId.BorderColor: return p.name.StartsWith("border-") && p.name.EndsWith("-color");
                case StylePropertyId.BorderWidth: return p.name.StartsWith("border-") && p.name.EndsWith("-width");
                case StylePropertyId.BorderRadius: return p.name.StartsWith("border-") && p.name.EndsWith("-radius");
                case StylePropertyId.Flex: return p.id == StylePropertyId.FlexGrow || p.id == StylePropertyId.FlexShrink || p.id == StylePropertyId.FlexBasis;
                case StylePropertyId.UnityTextOutline: return p.id == StylePropertyId.UnityTextOutlineColor || p.id == StylePropertyId.UnityTextOutlineWidth;
                case StylePropertyId.BackgroundPosition: return p.id == StylePropertyId.BackgroundPositionX || p.id == StylePropertyId.BackgroundPositionY;
                default: return false;
            }
        }
        static int Match(ComputedValues style, Property p)
        {
            var list = style.transitionProperty;
            if (list != null)
                for (int i = list.Count - 1; i >= 0; i--) if (Matches(list[i].id, p)) return i;
            return -1;
        }
        internal ComputedValues Reconcile(ComputedValues next, StylePropertyId? inlineProperty = null)
        {
            var oldTarget = target;
            target = next.Clone();
            if (!owner.styleInitialized || owner.elementPanel == null) return next;
            long now = Panel.TimeSinceStartupMs();
            var presented = owner.computedValues;
            var ordered = new List<Property>();
            var selected = new HashSet<StylePropertyId>();
            if (next.transitionProperty != null)
                for (int i = next.transitionProperty.Count - 1; i >= 0; i--)
                    foreach (var p in properties)
                        if (Matches(next.transitionProperty[i].id, p) && selected.Add(p.id)) ordered.Add(p);
            foreach (var a in running.Values) if (selected.Add(a.property.id)) ordered.Add(a.property);
            foreach (var p in ordered)
            {
                running.TryGetValue(p.id, out var previous);
                var end = p.get(next);
                int index = Match(next, p);
                if (previous != null && Equals(previous.to, end) && index >= 0)
                {
                    // Native inline writes of the current destination apply that value even when
                    // the existing animation continues. Unrelated inline properties keep it sampled.
                    if (inlineProperty != p.id) p.set(next, p.get(presented));
                    continue;
                }
                int duration = index < 0 ? 0 : Milliseconds(At(next.transitionDuration, index, new TimeValue(0)));
                int delay = index < 0 ? 0 : Milliseconds(At(next.transitionDelay, index, new TimeValue(0)));
                var start = p.get(presented);
                if (previous != null) { Cancel(previous, inlineProperty.HasValue ? now : previous.sampled, index >= 0 && !Equals(start, end)); running.Remove(p.id); }
                if (index < 0 || Math.Max(duration, 0) + delay <= 0 || Equals(start, end)) continue;
                // An unchanged target on a later style pass does not create a second animation.
                if (previous == null && oldTarget != null && Equals(p.get(oldTarget), end)) continue;
                if (!ConvertUnits(p, ref start, end)) continue;
                var a = new Animation { property = p, from = start, to = end, reversingStart = start,
                    start = now + delay, sampled = now, duration = duration, delay = delay,
                    easing = At(next.transitionTimingFunction, index, new EasingFunction(EasingMode.Ease)).mode };
                if (previous != null && Equals(previous.reversingStart, end))
                {
                    a.reversingFactor = Mathf.Clamp01(Mathf.Abs(previous.progress * previous.reversingFactor + 1 - previous.reversingFactor));
                    a.duration = (int)Math.Round(duration * a.reversingFactor);
                    if (delay < 0) a.start = now + (int)Math.Round(delay * a.reversingFactor);
                    a.reversingStart = previous.to;
                }
                running[p.id] = a;
                owner.elementPanel.UaRegisterTransition(this, p.type);
                Queue<TransitionRunEvent>(a, Math.Min(Math.Max(-a.delay, 0), Math.Max(0, a.duration)) / 1000d);
                p.set(next, start);
            }
            if (active)
                foreach (var p in properties)
                    if (pending.ContainsKey(p.id)) owner.elementPanel.UaRegisterTransition(this, p.type);
            return next;
        }
        static bool ConvertLength(ref Length a, Length b, float size)
        {
            if (a.IsAuto() || a.IsNone() || b.IsAuto() || b.IsNone()) return false;
            if (a.unit == b.unit) return true;
            if (float.IsNaN(size) || size == 0 && a.value != 0) return false;
            a = new Length(a.value == 0 ? 0 : b.unit == LengthUnit.Percent ? a.value / size * 100 : a.value * size / 100, b.unit);
            return true;
        }
        bool ConvertUnits(Property p, ref object from, object to)
        {
            if (from is Length a && to is Length b)
            {
                bool vertical = p.name.Contains("height") || p.id == StylePropertyId.Top || p.id == StylePropertyId.Bottom;
                float size = vertical ? owner.parent?.resolvedStyle.height ?? 0 : owner.parent?.resolvedStyle.width ?? 0;
                if (!ConvertLength(ref a, b, size)) return false;
                from = a;
            }
            else if (from is Translate ta && to is Translate tb)
            {
                var x = ta.x; var y = ta.y;
                if (!ConvertLength(ref x, tb.x, owner.resolvedStyle.width) || !ConvertLength(ref y, tb.y, owner.resolvedStyle.height)) return false;
                from = new Translate(x, y, ta.z);
            }
            else if (from is TransformOrigin oa && to is TransformOrigin ob)
            {
                var x = oa.x; var y = oa.y;
                if (!ConvertLength(ref x, ob.x, owner.resolvedStyle.width) || !ConvertLength(ref y, ob.y, owner.resolvedStyle.height)) return false;
                from = new TransformOrigin(x, y, oa.z);
            }
            return true;
        }
        void Queue<T>(Animation a, double elapsed) where T : TransitionEventBase<T>, new()
        {
            if (typeof(T) == typeof(TransitionCancelEvent) && pending.TryGetValue(a.property.id, out var queued))
            {
                int run = queued.FindLastIndex(e => e is TransitionRunEvent);
                if (run >= 0)
                {
                    for (int i = queued.Count - 1; i >= run; i--) { queued[i].Dispose(); queued.RemoveAt(i); }
                    if (queued.Count == 0) pending.Remove(a.property.id);
                    return;
                }
            }
            var e = TransitionEventBase<T>.GetPooled(new StylePropertyName(a.property.name), elapsed);
            e.target = owner;
            if (!pending.TryGetValue(a.property.id, out var events)) pending[a.property.id] = events = new List<EventBase>();
            events.Add(e);
            owner.elementPanel?.UaQueueTransition(this, a.property.type);
        }
        void Cancel(Animation a, long now, bool replaced = false) => Queue<TransitionCancelEvent>(a, (replaced ? Math.Max(-a.delay, 0) : Math.Max(now - a.start, 0)) / 1000d);
        internal void CancelAll()
        {
            var panel = owner.elementPanel;
            panel?.uaTransitionUpdates.Remove(this);
            var style = owner.computedValues.Clone();
            foreach (var a in running.Values)
            {
                a.property.set(style, a.to);
                if (panel?.uaUpdatingTransitions == true)
                {
                    using var e = TransitionCancelEvent.GetPooled(new StylePropertyName(a.property.name), Math.Max(a.sampled - a.start, 0) / 1000d);
                    e.target = owner;
                    panel.SendEvent(e);
                }
            }
            running.Clear();
            owner.UaInstallAnimatedStyle(style);
            if (panel?.uaUpdatingTransitions == true) return;
            foreach (var e in dispatching) e.Dispose();
            dispatching.Clear();
            foreach (var events in pending.Values) foreach (var e in events) e.Dispose();
            pending.Clear();
        }
        internal void Update(long now, Type type)
        {
            if (running.Count > 0)
            {
                var style = owner.computedValues.Clone();
                var finished = new List<StylePropertyId>();
                foreach (var a in running.Values)
                {
                    if (a.property.type != type) continue;
                    a.sampled = now;
                    bool ended = now >= a.start + Math.Max(0, a.duration);
                    if (!ended && now >= a.start && !a.started)
                    {
                        a.started = true;
                        Queue<TransitionStartEvent>(a, Math.Min(Math.Max(-a.delay, 0), Math.Max(0, a.duration)) / 1000d);
                    }
                    float t = ended ? 1 : now < a.start ? 0 : a.duration <= 0 ? 1 : Mathf.Clamp01((now - a.start) / (float)a.duration);
                    a.progress = TransitionEasing.Evaluate(a.easing, t);
                    a.property.set(style, t >= 1 ? a.to : a.property.interpolate(a.from, a.to, a.progress));
                    if (t >= 1) { Queue<TransitionEndEvent>(a, a.duration / 1000d); finished.Add(a.property.id); }
                }
                foreach (var id in finished) running.Remove(id);
                owner.UaInstallAnimatedStyle(style);
            }
        }
        internal static bool InheritedChanged(ComputedValues a, ComputedValues b)
        {
            foreach (var p in properties)
                if (StyleProperties.Inherited.Contains(p.id) && !Equals(p.get(a), p.get(b))) return true;
            return false;
        }
        internal void FreezeEvents(Type type)
        {
            if (pending.Count == 0) return;
            var ids = new List<StylePropertyId>(pending.Keys);
            foreach (var id in ids)
            {
                var property = Array.Find(properties, p => p.id == id);
                if (property.type != type) continue;
                dispatching.AddRange(pending[id]);
                pending.Remove(id);
            }
            // Reset the dictionary insertion slots once drained. Reusing removed slots in reverse
            // order would reverse simultaneous End events after the Run/Start queue is consumed.
            if (pending.Count == 0) pending.Clear();
        }
        internal void Dispatch()
        {
            while (dispatching.Count > 0)
            {
                var e = dispatching[0];
                dispatching.RemoveAt(0);
                using (e) owner.SendEvent(e);
            }
        }
    }
}
