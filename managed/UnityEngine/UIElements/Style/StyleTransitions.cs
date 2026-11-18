using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;

namespace Unbound.UIElements
{
    // Target values stay separate from the presented style. Restyling an ancestor must not restart
    // a child's transition, and interruption starts at the last presented value, not its old target.
    // How each kind of value interpolates, which size a length converts its unit against and the
    // usage hint a starting transition adds match the Unity 6000.0.71f1 Editor
    // (tests/projects/uitransitionkinds).
    internal sealed class StyleTransitions
    {
        /// <summary>What a length's percentage is of, when a transition goes between pixels and percent.</summary>
        enum LengthBasis
        {
            /// <summary>The value never converts: a transition between units does not run.</summary>
            None,
            ParentWidth,
            ParentHeight,
            /// <summary>The parent's height when it lays out in a column, else its width (flex-basis).</summary>
            ParentMainAxis,
            OwnWidth,
            /// <summary>The element's own width for x, its height for y (translate, transform-origin).</summary>
            OwnSize,
        }
        sealed class Property
        {
            internal StylePropertyId id;
            internal string name;
            internal Type type;
            internal Func<ComputedValues, object> get;
            internal Action<ComputedValues, object> set;
            internal Func<object, object, float, object> interpolate;
            /// <summary>Whether two values count as the same: floats, lengths and colours within
            /// Mathf.Approximately, everything else by equality.</summary>
            internal Func<object, object, bool> same;
            internal LengthBasis basis;
            /// <summary>Added to the element's usage hints when a transition of this property starts.</summary>
            internal UsageHints hint;
        }
        sealed class Animation
        {
            internal Property property;
            /// <summary>The property's own interpolation, or the one of the value kind it was started with.</summary>
            internal Func<object, object, float, object> interpolate;
            internal object from, to, reversingStart, current;
            internal long start, sampled;
            internal int duration, delay;
            internal float reversingFactor = 1, progress;
            internal Func<float, float> easing;
            internal bool started;
        }
        /// <summary>What a start did: refused it, kept the running animation, started or retargeted
        /// one, or cancelled the running one.</summary>
        enum Outcome { Refused, Kept, Started, Cancelled }
        readonly VisualElement owner;
        readonly Dictionary<StylePropertyId, Animation> running = new Dictionary<StylePropertyId, Animation>();
        /// <summary>The end values of the animations that completed, until a start, a cancel or a
        /// transition list without them removes them.</summary>
        readonly Dictionary<StylePropertyId, object> completed = new Dictionary<StylePropertyId, object>();
        readonly Dictionary<StylePropertyId, List<EventBase>> pending = new Dictionary<StylePropertyId, List<EventBase>>();
        readonly List<EventBase> dispatching = new List<EventBase>();

        internal ComputedValues target;
        internal bool active => running.Count > 0 || pending.Count > 0;
        internal StyleTransitions(VisualElement owner) { this.owner = owner; }
        IStylePropertyAnimations counts => owner;

        static Property P<T>(string name, Func<ComputedValues, T> get, Action<ComputedValues, T> set, Func<T, T, float, T> lerp,
            LengthBasis basis = LengthBasis.None, UsageHints hint = UsageHints.None)
            => new Property { name = name, type = typeof(T), id = StyleProperties.IdOf(name), get = s => get(s), set = (s, v) => set(s, (T)v),
                interpolate = (a, b, t) => lerp((T)a, (T)b, t), same = Same<T>(), basis = basis, hint = hint };
        static Func<object, object, bool> Same<T>()
        {
            if (typeof(T) == typeof(float)) return (a, b) => Mathf.Approximately((float)a, (float)b);
            if (typeof(T) == typeof(Length)) return (a, b) => ((Length)a).unit == ((Length)b).unit && Mathf.Approximately(((Length)a).value, ((Length)b).value);
            if (typeof(T) == typeof(Color)) return (a, b) => { Color c = (Color)a, d = (Color)b; return Mathf.Approximately(c.r, d.r) && Mathf.Approximately(c.g, d.g) && Mathf.Approximately(c.b, d.b) && Mathf.Approximately(c.a, d.a); };
            return Equals;
        }
        const LengthBasis PW = LengthBasis.ParentWidth, PH = LengthBasis.ParentHeight, OW = LengthBasis.OwnWidth, OS = LengthBasis.OwnSize;
        const UsageHints DynamicColor = UsageHints.DynamicColor, DynamicTransform = UsageHints.DynamicTransform;
        // The release interpolates with Mathf.LerpUnclamped, whose expression the Editor evaluates in
        // double before the result rounds to float (tests/projects/styleanimations restyle.*.750).
        static float Lerp(float a, float b, float t) => (float)(a + ((double)b - a) * t);
        static Length Lerp(Length a, Length b, float t) => new Length(Lerp(a.value, b.value, t), b.unit);
        static Color Lerp(Color a, Color b, float t) => new Color(Lerp(a.r, b.r, t), Lerp(a.g, b.g, t), Lerp(a.b, b.b, t), Lerp(a.a, b.a, t));
        static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new Vector3(Lerp(a.x, b.x, t), Lerp(a.y, b.y, t), Lerp(a.z, b.z, t));
        static T Discrete<T>(T a, T b, float t) => t < .5f ? a : b;
        static readonly Property[] properties = CreateProperties();
        static Property[] CreateProperties()
        {
            var result = new[]
        {
            P<Align>("align-content", s => s.alignContent, (s, v) => s.alignContent = v, Discrete),
            P<Align>("align-items", s => s.alignItems, (s, v) => s.alignItems = v, Discrete),
            P<Align>("align-self", s => s.alignSelf, (s, v) => s.alignSelf = v, Discrete),
            P<Color>("background-color", s => s.backgroundColor, (s, v) => s.backgroundColor = v, Lerp, hint: DynamicColor),
            P<Background>("background-image", s => s.backgroundImage, (s, v) => s.backgroundImage = v, Discrete),
            P<BackgroundPosition>("background-position-x", s => s.backgroundPositionX, (s, v) => s.backgroundPositionX = v, Discrete),
            P<BackgroundPosition>("background-position-y", s => s.backgroundPositionY, (s, v) => s.backgroundPositionY = v, Discrete),
            P<BackgroundRepeat>("background-repeat", s => s.backgroundRepeat, (s, v) => s.backgroundRepeat = v, Discrete),
            P<BackgroundSize>("background-size", s => s.backgroundSize, (s, v) => s.backgroundSize = v, (a, b, t) => new BackgroundSize(Lerp(a.x, b.x, t), Lerp(a.y, b.y, t))),
            P<Color>("border-bottom-color", s => s.borderBottomColor, (s, v) => s.borderBottomColor = v, Lerp, hint: DynamicColor),
            P<Length>("border-bottom-left-radius", s => s.borderBottomLeftRadius, (s, v) => s.borderBottomLeftRadius = v, Lerp, OW),
            P<Length>("border-bottom-right-radius", s => s.borderBottomRightRadius, (s, v) => s.borderBottomRightRadius = v, Lerp, OW),
            P<float>("border-bottom-width", s => s.borderBottomWidth, (s, v) => s.borderBottomWidth = v, Lerp),
            P<Color>("border-left-color", s => s.borderLeftColor, (s, v) => s.borderLeftColor = v, Lerp, hint: DynamicColor),
            P<float>("border-left-width", s => s.borderLeftWidth, (s, v) => s.borderLeftWidth = v, Lerp),
            P<Color>("border-right-color", s => s.borderRightColor, (s, v) => s.borderRightColor = v, Lerp, hint: DynamicColor),
            P<float>("border-right-width", s => s.borderRightWidth, (s, v) => s.borderRightWidth = v, Lerp),
            P<Color>("border-top-color", s => s.borderTopColor, (s, v) => s.borderTopColor = v, Lerp, hint: DynamicColor),
            P<Length>("border-top-left-radius", s => s.borderTopLeftRadius, (s, v) => s.borderTopLeftRadius = v, Lerp, OW),
            P<Length>("border-top-right-radius", s => s.borderTopRightRadius, (s, v) => s.borderTopRightRadius = v, Lerp, OW),
            P<float>("border-top-width", s => s.borderTopWidth, (s, v) => s.borderTopWidth = v, Lerp),
            P<Length>("bottom", s => s.bottom, (s, v) => s.bottom = v, Lerp, PH),
            P<Color>("color", s => s.color, (s, v) => s.color = v, Lerp, hint: DynamicColor),
            P<Length>("flex-basis", s => s.flexBasis, (s, v) => s.flexBasis = v, Lerp, LengthBasis.ParentMainAxis),
            P<FlexDirection>("flex-direction", s => s.flexDirection, (s, v) => s.flexDirection = v, Discrete),
            P<float>("flex-grow", s => s.flexGrow, (s, v) => s.flexGrow = v, Lerp),
            P<float>("flex-shrink", s => s.flexShrink, (s, v) => s.flexShrink = v, Lerp),
            P<Wrap>("flex-wrap", s => s.flexWrap, (s, v) => s.flexWrap = v, Discrete),
            P<Length>("font-size", s => s.fontSize, (s, v) => s.fontSize = v, Lerp),
            P<Length>("height", s => s.height, (s, v) => s.height = v, Lerp, PH),
            P<Justify>("justify-content", s => s.justifyContent, (s, v) => s.justifyContent = v, Discrete),
            P<Length>("left", s => s.left, (s, v) => s.left = v, Lerp, PW),
            P<Length>("letter-spacing", s => s.letterSpacing, (s, v) => s.letterSpacing = v, Lerp),
            P<Length>("margin-bottom", s => s.marginBottom, (s, v) => s.marginBottom = v, Lerp, PW),
            P<Length>("margin-left", s => s.marginLeft, (s, v) => s.marginLeft = v, Lerp, PW),
            P<Length>("margin-right", s => s.marginRight, (s, v) => s.marginRight = v, Lerp, PW),
            P<Length>("margin-top", s => s.marginTop, (s, v) => s.marginTop = v, Lerp, PW),
            P<Length>("max-height", s => s.maxHeight, (s, v) => s.maxHeight = v, Lerp, PH),
            P<Length>("max-width", s => s.maxWidth, (s, v) => s.maxWidth = v, Lerp, PW),
            P<Length>("min-height", s => s.minHeight, (s, v) => s.minHeight = v, Lerp, PH),
            P<Length>("min-width", s => s.minWidth, (s, v) => s.minWidth = v, Lerp, PW),
            P<float>("opacity", s => s.opacity, (s, v) => s.opacity = v, Lerp),
            P<OverflowInternal>("overflow", s => s.overflow, (s, v) => s.overflow = v, Discrete),
            P<Length>("padding-bottom", s => s.paddingBottom, (s, v) => s.paddingBottom = v, Lerp, PW),
            P<Length>("padding-left", s => s.paddingLeft, (s, v) => s.paddingLeft = v, Lerp, PW),
            P<Length>("padding-right", s => s.paddingRight, (s, v) => s.paddingRight = v, Lerp, PW),
            P<Length>("padding-top", s => s.paddingTop, (s, v) => s.paddingTop = v, Lerp, PW),
            P<Position>("position", s => s.position, (s, v) => s.position = v, Discrete),
            P<Length>("right", s => s.right, (s, v) => s.right = v, Lerp, PW),
            P<Rotate>("rotate", s => s.rotate, (s, v) => s.rotate = v, (a, b, t) => new Rotate(new Angle(Lerp(a.angle.ToDegrees(), b.angle.ToDegrees(), t))), hint: DynamicTransform),
            P<Scale>("scale", s => s.scale, (s, v) => s.scale = v, (a, b, t) => new Scale(Lerp(a.value, b.value, t)), hint: DynamicTransform),
            P<TextShadow>("text-shadow", s => s.textShadow, (s, v) => s.textShadow = v, (a, b, t) => new TextShadow { offset = Vector2.LerpUnclamped(a.offset, b.offset, t), blurRadius = Lerp(a.blurRadius, b.blurRadius, t), color = Lerp(a.color, b.color, t) }),
            P<TextOverflow>("text-overflow", s => s.textOverflow, (s, v) => s.textOverflow = v, Discrete),
            P<Length>("top", s => s.top, (s, v) => s.top = v, Lerp, PH),
            P<TransformOrigin>("transform-origin", s => s.transformOrigin, (s, v) => s.transformOrigin = v, (a, b, t) => new TransformOrigin(Lerp(a.x, b.x, t), Lerp(a.y, b.y, t), Mathf.Lerp(a.z, b.z, t)), OS, DynamicTransform),
            P<Translate>("translate", s => s.translate, (s, v) => s.translate = v, (a, b, t) => new Translate(Lerp(a.x, b.x, t), Lerp(a.y, b.y, t), Mathf.Lerp(a.z, b.z, t)), OS, DynamicTransform),
            P<Color>("-unity-background-image-tint-color", s => s.unityBackgroundImageTintColor, (s, v) => s.unityBackgroundImageTintColor = v, Lerp, hint: DynamicColor),
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
            P<WhiteSpace>("white-space", s => s.whiteSpace, (s, v) => s.whiteSpace = v, Discrete),
            P<Length>("width", s => s.width, (s, v) => s.width = v, Lerp, PW),
            P<Length>("word-spacing", s => s.wordSpacing, (s, v) => s.wordSpacing = v, Lerp),
        };
            Array.Sort(result, (a, b) => a.id.CompareTo(b.id));
            return result;
        }
        static readonly Dictionary<StylePropertyId, Property> byId = CreateIndex();
        static Dictionary<StylePropertyId, Property> CreateIndex()
        {
            var result = new Dictionary<StylePropertyId, Property>();
            foreach (var p in properties) result[p.id] = p;
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
        /// <summary>A transition for <paramref name="p"/> in <paramref name="style"/>: its index in the
        /// transition list and its timing; false when it has none or its combined duration is not positive.</summary>
        static bool TransitionOf(ComputedValues style, Property p, out int index, out int duration, out int delay)
        {
            index = Match(style, p);
            duration = index < 0 ? 0 : Milliseconds(At(style.transitionDuration, index, new TimeValue(0)));
            delay = index < 0 ? 0 : Milliseconds(At(style.transitionDelay, index, new TimeValue(0)));
            return index >= 0 && Math.Max(duration, 0) + delay > 0;
        }
        static bool SameList<T>(List<T> a, List<T> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
            return true;
        }
        static bool SameTransitions(ComputedValues a, ComputedValues b) =>
            SameList(a.transitionProperty, b.transitionProperty) && SameList(a.transitionDuration, b.transitionDuration) && SameList(a.transitionDelay, b.transitionDelay);
        static bool HasTransitions(ComputedValues style)
        {
            foreach (var p in properties) if (TransitionOf(style, p, out _, out _, out _)) return true;
            return false;
        }
        static bool IsTransitionTiming(StylePropertyId id) => id == StylePropertyId.TransitionProperty || id == StylePropertyId.TransitionDuration
            || id == StylePropertyId.TransitionDelay || id == StylePropertyId.TransitionTimingFunction || id == StylePropertyId.Transition;

        internal ComputedValues Reconcile(ComputedValues next, StylePropertyId? inlineProperty = null)
        {
            var oldTarget = target;
            target = next.Clone();
            if (!owner.styleInitialized || owner.elementPanel == null) return next;
            long now = Panel.TimeSinceStartupMs();
            var presented = owner.computedValues;
            // A changed transition list cancels the animations it no longer has a transition for:
            // on a style pass while one runs, on an inline transition change while one runs or has
            // completed. An unchanged list keeps them, started through the animation interface too.
            bool listChanged = oldTarget == null || !SameTransitions(oldTarget, next);
            bool inlineTiming = inlineProperty.HasValue && IsTransitionTiming(inlineProperty.Value);
            if (listChanged && (counts.runningAnimationCount > 0 || inlineTiming && counts.completedAnimationCount > 0))
                CancelUntransitioned(next, inlineProperty.HasValue ? now : (long?)null);
            var ordered = new List<Property>();
            var selected = new HashSet<StylePropertyId>();
            if (next.transitionProperty != null)
                for (int i = next.transitionProperty.Count - 1; i >= 0; i--)
                    foreach (var p in properties)
                        if (Matches(next.transitionProperty[i].id, p) && selected.Add(p.id)) ordered.Add(p);
            foreach (var a in running.Values) if (selected.Add(a.property.id)) ordered.Add(a.property);
            bool? hasTransitions = null;
            foreach (var p in ordered)
            {
                running.TryGetValue(p.id, out var previous);
                if (!TransitionOf(next, p, out int index, out int duration, out int delay))
                {
                    // An animation without a transition here (one the animation interface started)
                    // shows its value again only while the style has transitions; otherwise the
                    // style's own value shows until the next animation update.
                    if (previous != null && (hasTransitions ??= HasTransitions(next))) p.set(next, previous.current);
                    continue;
                }
                var end = p.get(next);
                // An unchanged target on a later style pass does not create a second animation.
                if (previous == null && oldTarget != null && p.same(p.get(oldTarget), end)) continue;
                var easing = TransitionEasing.Curve(At(next.transitionTimingFunction, index, new EasingFunction(EasingMode.Ease)).mode);
                long cancelTime = inlineProperty.HasValue || previous == null ? now : previous.sampled;
                var outcome = Start(p, p.interpolate, p.get(presented), end, duration, delay, easing, now, cancelTime);
                if (outcome == Outcome.Started)
                {
                    owner.usageHints |= p.hint;
                    p.set(next, running[p.id].current);
                }
                // Native inline writes of the current destination apply that value even when the
                // existing animation continues. Unrelated inline properties keep it sampled.
                else if (outcome == Outcome.Kept) { if (inlineProperty != p.id) p.set(next, p.get(presented)); }
                else if (running.TryGetValue(p.id, out var kept)) p.set(next, kept.current);
            }
            if (active)
                foreach (var p in properties)
                    if (pending.ContainsKey(p.id)) owner.elementPanel.UaRegisterTransition(this, p.type);
            return next;
        }

        /// <summary>Cancels the running and completed animations <paramref name="next"/> has no
        /// transition for. Each Cancel event counts from the animation's last sample, or from
        /// <paramref name="cancelTime"/> when given.</summary>
        void CancelUntransitioned(ComputedValues next, long? cancelTime)
        {
            List<StylePropertyId> cancelled = null;
            foreach (var a in running.Values)
                if (!TransitionOf(next, a.property, out _, out _, out _))
                {
                    Cancel(a, cancelTime ?? a.sampled);
                    (cancelled ??= new List<StylePropertyId>()).Add(a.property.id);
                }
            if (cancelled != null) foreach (var id in cancelled) RemoveRunning(id);
            cancelled?.Clear();
            foreach (var id in completed.Keys)
                if (!TransitionOf(next, byId[id], out _, out _, out _)) (cancelled ??= new List<StylePropertyId>()).Add(id);
            if (cancelled != null) foreach (var id in cancelled) RemoveCompleted(id);
        }

        // Starts or retargets an animation (the CSS Transitions starting rules), for style changes and
        // the animation interface alike; matches the Unity 6000.0.71f1 Editor (tests/projects/styleanimations).
        Outcome Start(Property p, Func<object, object, float, object> interpolate, object start, object end,
            int duration, int delay, Func<float, float> easing, long now, long cancelTime)
        {
            int combined = Math.Max(duration, 0) + delay;
            if (!ConvertUnits(p, ref start, end)) return Outcome.Refused;
            if (completed.TryGetValue(p.id, out var done))
            {
                if (p.same(end, done) || combined <= 0) return Outcome.Refused;
                RemoveCompleted(p.id);
            }
            if (running.TryGetValue(p.id, out var previous))
            {
                if (p.same(end, previous.to)) return Outcome.Kept;
                var current = previous.current;
                if (p.same(end, current) || combined <= 0 || !ConvertUnits(p, ref current, end))
                {
                    Cancel(previous, cancelTime);
                    RemoveRunning(p.id);
                    return Outcome.Cancelled;
                }
                var retargeted = NewAnimation(p, interpolate, current, end, duration, delay, easing, now);
                if (p.same(end, previous.reversingStart))
                {
                    // Reversing shortens the duration and a negative delay by the progress travelled.
                    retargeted.reversingFactor = Mathf.Clamp01(Mathf.Abs(previous.progress * previous.reversingFactor + 1 - previous.reversingFactor));
                    retargeted.duration = (int)Math.Round(duration * retargeted.reversingFactor);
                    if (delay < 0) retargeted.start = now + (int)Math.Round(delay * retargeted.reversingFactor);
                    retargeted.reversingStart = previous.to;
                }
                Cancel(previous, cancelTime, replaced: true);
                running[p.id] = retargeted;
                Register(retargeted);
                return Outcome.Started;
            }
            if (combined <= 0 || p.same(start, end)) return Outcome.Refused;
            var created = NewAnimation(p, interpolate, start, end, duration, delay, easing, now);
            running[p.id] = created;
            counts.runningAnimationCount++;
            Register(created);
            return Outcome.Started;
        }

        static Animation NewAnimation(Property p, Func<object, object, float, object> interpolate, object start, object end,
            int duration, int delay, Func<float, float> easing, long now) =>
            new Animation { property = p, interpolate = interpolate, from = start, to = end, reversingStart = start, current = start,
                start = now + delay, sampled = now, duration = duration, delay = delay, easing = easing };

        void Register(Animation a)
        {
            owner.elementPanel.UaRegisterTransition(this, a.property.type);
            Queue<TransitionRunEvent>(a, Math.Min(Math.Max(-a.delay, 0), Math.Max(0, a.duration)) / 1000d);
        }

        void RemoveRunning(StylePropertyId id)
        {
            running.Remove(id);
            counts.runningAnimationCount--;
        }

        void RemoveCompleted(StylePropertyId id)
        {
            if (completed.Remove(id)) counts.completedAnimationCount--;
        }

        /// <summary>The size a percentage of <paramref name="basis"/> is of (component 1 is y for
        /// translate and transform-origin), or null when there is none.</summary>
        float? BasisSize(LengthBasis basis, int component)
        {
            var parent = owner.hierarchy.parent;
            switch (basis)
            {
                case LengthBasis.ParentWidth: return parent?.resolvedStyle.width;
                case LengthBasis.ParentHeight: return parent?.resolvedStyle.height;
                case LengthBasis.ParentMainAxis:
                    if (parent == null) return null;
                    var direction = parent.resolvedStyle.flexDirection;
                    return direction == FlexDirection.Column || direction == FlexDirection.ColumnReverse ? parent.resolvedStyle.height : parent.resolvedStyle.width;
                case LengthBasis.OwnWidth: return owner.resolvedStyle.width;
                case LengthBasis.OwnSize: return component == 0 ? owner.resolvedStyle.width : owner.resolvedStyle.height;
                default: return null;
            }
        }

        /// <summary>Gives <paramref name="from"/> the unit of <paramref name="to"/>; false when the
        /// transition cannot run (auto, none, NaN, or no size to convert against).</summary>
        bool ConvertLength(ref Length from, Length to, LengthBasis basis, int component = 0)
        {
            if (from.IsAuto() || from.IsNone() || to.IsAuto() || to.IsNone()) return false;
            if (float.IsNaN(from.value) || float.IsNaN(to.value)) return false;
            if (from.unit == to.unit) return true;
            if (to.unit == LengthUnit.Pixel)
            {
                if (Mathf.Approximately(from.value, 0))
                {
                    from = new Length(0, LengthUnit.Pixel);
                    return true;
                }
                var size = BasisSize(basis, component);
                if (!size.HasValue || !(size.Value >= 0)) return false;
                from = new Length(from.value * size.Value / 100, LengthUnit.Pixel);
                return true;
            }
            var percentOf = BasisSize(basis, component);
            if (!percentOf.HasValue || !(percentOf.Value > 0)) return false;
            from = new Length(from.value * 100 / percentOf.Value, LengthUnit.Percent);
            return true;
        }

        bool ConvertUnits(Property p, ref object from, object to)
        {
            if (from is Length a && to is Length b)
            {
                if (!ConvertLength(ref a, b, p.basis)) return false;
                from = a;
            }
            else if (from is Translate ta && to is Translate tb)
            {
                var x = ta.x; var y = ta.y;
                if (!ConvertLength(ref x, tb.x, p.basis, 0) || !ConvertLength(ref y, tb.y, p.basis, 1)) return false;
                from = new Translate(x, y, ta.z);
            }
            else if (from is TransformOrigin oa && to is TransformOrigin ob)
            {
                var x = oa.x; var y = oa.y;
                if (!ConvertLength(ref x, ob.x, p.basis, 0) || !ConvertLength(ref y, ob.y, p.basis, 1)) return false;
                from = new TransformOrigin(x, y, oa.z);
            }
            else if (from is BackgroundSize sa && to is BackgroundSize sb)
            {
                var x = sa.x; var y = sa.y;
                if (!ConvertLength(ref x, sb.x, p.basis, 0) || !ConvertLength(ref y, sb.y, p.basis, 1)) return false;
                from = new BackgroundSize(x, y);
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
        /// <summary>Cancels every animation and forgets the completed ones; the values take their
        /// destinations. Cancel events go out at once during an animation update or when
        /// <paramref name="sendEvents"/> asks for it (the animation interface); otherwise (leaving
        /// the panel) they and the queued events are dropped.</summary>
        internal void CancelAll(bool sendEvents = false)
        {
            var panel = owner.elementPanel;
            bool send = sendEvents || panel?.uaUpdatingTransitions == true;
            if (!sendEvents) panel?.uaTransitionUpdates.Remove(this);
            var style = owner.computedValues.Clone();
            foreach (var a in running.Values)
            {
                a.property.set(style, a.to);
                if (send && panel != null)
                {
                    using var e = TransitionCancelEvent.GetPooled(new StylePropertyName(a.property.name), Math.Max(a.sampled - a.start, 0) / 1000d);
                    e.target = owner;
                    panel.SendEvent(e);
                }
            }
            counts.runningAnimationCount -= running.Count;
            counts.completedAnimationCount -= completed.Count;
            running.Clear();
            completed.Clear();
            owner.UaInstallAnimatedStyle(style);
            if (send) return;
            foreach (var e in dispatching) e.Dispose();
            dispatching.Clear();
            foreach (var events in pending.Values) foreach (var e in events) e.Dispose();
            pending.Clear();
        }

        /// <summary>Cancels one property's animation (its value takes the destination; Cancel goes
        /// out with the next update) and forgets its completed one.</summary>
        internal void Cancel(StylePropertyId id)
        {
            if (running.TryGetValue(id, out var a))
            {
                Cancel(a, a.sampled);
                RemoveRunning(id);
                Present(a.property, a.to);
            }
            RemoveCompleted(id);
        }

        internal bool IsRunning(StylePropertyId id) => running.ContainsKey(id);

        /// <summary>Shows a running animation's current value now.</summary>
        internal void Present(StylePropertyId id)
        {
            if (running.TryGetValue(id, out var a)) Present(a.property, a.current);
        }

        void Present(Property p, object value)
        {
            var style = owner.computedValues.Clone();
            p.set(style, value);
            owner.UaInstallAnimatedStyle(style);
        }

        /// <summary>Adds the running, then the completed animations' properties.</summary>
        internal void GetAll(List<StylePropertyId> ids)
        {
            ids.AddRange(running.Keys);
            ids.AddRange(completed.Keys);
        }

        /// <summary>The value kinds the animation interface starts with (its Start overloads).</summary>
        internal enum Kind { Float, Int, Enum, Value }

        /// <summary>Starts an animation for the animation interface: like a style change but with
        /// the given values, timing and easing curve, and no usage hints.</summary>
        internal bool StartAnimation(StylePropertyId id, Kind kind, Type valueType, string typeName, object from, object to,
            int durationMs, int delayMs, Func<float, float> easing)
        {
            if (easing == null) throw new ArgumentNullException("easingCurve");
            // The release accepts any id and fails when the value is applied; Unbound refuses it at once.
            if (!byId.TryGetValue(id, out var p) || !Accepts(p, kind, valueType))
                throw new ArgumentException("Invalid animation property id. Can't apply value of type '" + typeName + "' to property '" + id + "'. Please make sure that this property is animatable.", "id");
            var interpolate = p.interpolate;
            if (kind == Kind.Int || kind == Kind.Enum)
            {
                from = p.type.IsEnum ? Enum.ToObject(p.type, (int)from) : from;
                to = p.type.IsEnum ? Enum.ToObject(p.type, (int)to) : to;
                // Integers round to even; the enum start is discrete, whichever the property.
                interpolate = kind == Kind.Int ? RoundedInterpolation(p.type) : Discrete<object>;
            }
            long now = Panel.TimeSinceStartupMs();
            long cancelTime = running.TryGetValue(id, out var previous) ? previous.sampled : now;
            return Start(p, interpolate, from, to, durationMs, delayMs, easing, now, cancelTime) == Outcome.Started;
        }

        static bool Accepts(Property p, Kind kind, Type valueType) => kind switch
        {
            Kind.Float => p.type == typeof(float),
            Kind.Int or Kind.Enum => p.type == typeof(int) || p.type.IsEnum,
            _ => p.type == valueType,
        };

        static readonly Dictionary<Type, Func<object, object, float, object>> rounded = new Dictionary<Type, Func<object, object, float, object>>();
        static Func<object, object, float, object> RoundedInterpolation(Type type)
        {
            lock (rounded)
            {
                if (!rounded.TryGetValue(type, out var result))
                {
                    result = (a, b, t) =>
                    {
                        int value = (int)Math.Round(Lerp(Convert.ToInt32(a), Convert.ToInt32(b), t));
                        return type.IsEnum ? Enum.ToObject(type, value) : value;
                    };
                    rounded[type] = result;
                }
                return result;
            }
        }

        internal void Update(long now, Type type)
        {
            if (running.Count > 0)
            {
                var style = owner.computedValues.Clone();
                List<Animation> finished = null;
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
                    a.progress = a.easing(t);
                    a.current = t >= 1 ? a.to : a.interpolate(a.from, a.to, a.progress);
                    a.property.set(style, a.current);
                    if (t >= 1) { Queue<TransitionEndEvent>(a, a.duration / 1000d); (finished ??= new List<Animation>()).Add(a); }
                }
                if (finished != null)
                    foreach (var a in finished)
                    {
                        RemoveRunning(a.property.id);
                        if (!completed.ContainsKey(a.property.id)) counts.completedAnimationCount++;
                        completed[a.property.id] = a.to;
                    }
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
                if (byId[id].type != type) continue;
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
