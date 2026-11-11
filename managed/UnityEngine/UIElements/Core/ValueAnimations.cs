using System;
using System.Collections.Generic;
using UnityEngine.UIElements.Experimental;
using UnityEngine.UIElements.StyleSheets;

// Transition animations (VisualElement.experimental.animation): ValueAnimation<T> runs from a value
// (given, or read from the element when the animation first needs it) to another over durationMs
// on the panel clock, eased (OutQuad by default) and interpolated, and calls valueUpdated with the
// element and the value on every panel update until it completes; then onAnimationCompleted, and
// unless kept alive it returns to a pool (a recycled animation refuses use). Animations tick in the
// panel update after styles and layout, only while their element is in a panel. StyleValues animates a set of
// style properties of the element through its inline style. Documented (Unity 6000.0 Scripting API
// "ValueAnimation<T0>", "ITransitionAnimations", "Easing", "StyleValues"); the flexShrink setter of
// StyleValues sets flexGrow, as Unity's does.
namespace UnityEngine.UIElements.Experimental
{
    internal partial interface IValueAnimationUpdate
    {
        void Tick(long currentTimeMs);
    }

    public partial interface IValueAnimation
    {
        bool isRunning { get; }

        int durationMs { get; set; }

        void Start();

        void Stop();

        void Recycle();
    }

    public partial interface ITransitionAnimations
    {
        ValueAnimation<float> Start(float from, float to, int durationMs, Action<VisualElement, float> onValueChanged);

        ValueAnimation<Rect> Start(Rect from, Rect to, int durationMs, Action<VisualElement, Rect> onValueChanged);

        ValueAnimation<Color> Start(Color from, Color to, int durationMs, Action<VisualElement, Color> onValueChanged);

        ValueAnimation<Vector3> Start(Vector3 from, Vector3 to, int durationMs, Action<VisualElement, Vector3> onValueChanged);

        ValueAnimation<Vector2> Start(Vector2 from, Vector2 to, int durationMs, Action<VisualElement, Vector2> onValueChanged);

        ValueAnimation<Quaternion> Start(Quaternion from, Quaternion to, int durationMs, Action<VisualElement, Quaternion> onValueChanged);

        ValueAnimation<StyleValues> Start(StyleValues from, StyleValues to, int durationMs);

        ValueAnimation<StyleValues> Start(StyleValues to, int durationMs);

        ValueAnimation<float> Start(Func<VisualElement, float> fromValueGetter, float to, int durationMs, Action<VisualElement, float> onValueChanged);

        ValueAnimation<Rect> Start(Func<VisualElement, Rect> fromValueGetter, Rect to, int durationMs, Action<VisualElement, Rect> onValueChanged);

        ValueAnimation<Color> Start(Func<VisualElement, Color> fromValueGetter, Color to, int durationMs, Action<VisualElement, Color> onValueChanged);

        ValueAnimation<Vector3> Start(Func<VisualElement, Vector3> fromValueGetter, Vector3 to, int durationMs, Action<VisualElement, Vector3> onValueChanged);

        ValueAnimation<Vector2> Start(Func<VisualElement, Vector2> fromValueGetter, Vector2 to, int durationMs, Action<VisualElement, Vector2> onValueChanged);

        ValueAnimation<Quaternion> Start(Func<VisualElement, Quaternion> fromValueGetter, Quaternion to, int durationMs, Action<VisualElement, Quaternion> onValueChanged);

        ValueAnimation<Rect> Layout(Rect to, int durationMs);

        ValueAnimation<Vector2> TopLeft(Vector2 to, int durationMs);

        ValueAnimation<Vector2> Size(Vector2 to, int durationMs);

        ValueAnimation<float> Scale(float to, int duration);

        ValueAnimation<Vector3> Position(Vector3 to, int duration);

        ValueAnimation<Quaternion> Rotation(Quaternion to, int duration);
    }

    /// <summary>One running transition of an element's value.</summary>
    public sealed partial class ValueAnimation<T> : IValueAnimationUpdate, IValueAnimation
    {
        // Unity's default duration and pool size (the pool itself is UnityEngine.Pool's, so which
        // recycled instance comes back may differ; nothing observable depends on it).
        const int k_DefaultDurationMs = 400;
        const int k_DefaultMaxPoolSize = 100;

        static readonly UnityEngine.Pool.ObjectPool<ValueAnimation<T>> s_ObjectPool =
            new UnityEngine.Pool.ObjectPool<ValueAnimation<T>>(() => new ValueAnimation<T>(), null, null, null, true, 10, k_DefaultMaxPoolSize);

        long m_StartTimeMs;
        int m_DurationMs;
        T m_From;
        bool m_FromValueSet;

        /// <summary>At least 1 ms.</summary>
        public int durationMs
        {
            get => m_DurationMs;
            set => m_DurationMs = value < 1 ? 1 : value;
        }

        public Func<float, float> easingCurve { get; set; }

        public bool isRunning { get; private set; }

        public Action onAnimationCompleted { get; set; }

        public bool autoRecycle { get; set; }

        bool recycled { get; set; }

        VisualElement owner { get; set; }

        public Action<VisualElement, T> valueUpdated { get; set; }

        public Func<VisualElement, T> initialValue { get; set; }

        public Func<T, T, float, T> interpolator { get; set; }

        /// <summary>The start value: set, else read once from the element through initialValue.</summary>
        public T from
        {
            get
            {
                if (!m_FromValueSet && initialValue != null) from = initialValue(owner);
                return m_From;
            }
            set
            {
                m_FromValueSet = true;
                m_From = value;
            }
        }

        public T to { get; set; }

        public ValueAnimation() => SetDefaultValues();

        /// <summary>Starts on the panel clock (only with an owner element).</summary>
        public void Start()
        {
            CheckNotRecycled();
            if (owner == null) return;
            m_StartTimeMs = Panel.TimeSinceStartupMs();
            Register();
            isRunning = true;
        }

        public void Stop()
        {
            CheckNotRecycled();
            if (!isRunning) return;
            Unregister();
            isRunning = false;
            onAnimationCompleted?.Invoke();
            if (autoRecycle && !recycled) Recycle();
        }

        public void Recycle()
        {
            CheckNotRecycled();
            if (isRunning)
            {
                if (autoRecycle)
                {
                    // Stopping recycles it.
                    Stop();
                    return;
                }
                Stop();
            }
            SetDefaultValues();
            recycled = true;
            s_ObjectPool.Release(this);
        }

        void IValueAnimationUpdate.Tick(long currentTimeMs)
        {
            CheckNotRecycled();
            float progress = (currentTimeMs - m_StartTimeMs) / (float)durationMs;
            bool done = false;
            if (progress >= 1f)
            {
                progress = 1f;
                done = true;
            }
            progress = easingCurve?.Invoke(progress) ?? progress;
            if (interpolator != null) valueUpdated?.Invoke(owner, interpolator(from, to, progress));
            if (done) Stop();
        }

        void SetDefaultValues()
        {
            m_DurationMs = k_DefaultDurationMs;
            autoRecycle = true;
            owner = null;
            m_StartTimeMs = 0;
            onAnimationCompleted = null;
            valueUpdated = null;
            initialValue = null;
            interpolator = null;
            to = default;
            from = default;
            m_FromValueSet = false;
            easingCurve = Easing.OutQuad;
        }

        void Unregister() => owner?.RegisterAnimationUpdate(this, false);

        void Register() => owner?.RegisterAnimationUpdate(this, true);

        internal void SetOwner(VisualElement e)
        {
            if (isRunning) Unregister();
            owner = e;
            if (isRunning) Register();
        }

        void CheckNotRecycled()
        {
            if (recycled)
                throw new InvalidOperationException("Animation object has been recycled. Use KeepAlive() to keep a reference to an animation after it has been stopped.");
        }

        public static ValueAnimation<T> Create(VisualElement e, Func<T, T, float, T> interpolator)
        {
            var animation = s_ObjectPool.Get();
            animation.recycled = false;
            animation.SetOwner(e);
            animation.interpolator = interpolator;
            return animation;
        }

        public ValueAnimation<T> Ease(Func<float, float> easing)
        {
            easingCurve = easing;
            return this;
        }

        public ValueAnimation<T> OnCompleted(Action callback)
        {
            onAnimationCompleted = callback;
            return this;
        }

        public ValueAnimation<T> KeepAlive()
        {
            autoRecycle = false;
            return this;
        }
    }

    /// <summary>The easing curves of transition animations (Robert Penner's equations): progress in
    /// [0, 1] to eased progress. Intermediate values are kept in double precision and rounded to float
    /// only where a value is stored, passed as a float argument or returned, as Unity's Mono evaluates
    /// float expressions (tests/projects/uitkcollections animations/easing-curves).</summary>
    public static partial class Easing
    {
        const float HalfPi = Mathf.PI / 2f;
        const float TwoPi = Mathf.PI * 2f;
        // The overshoot of the Back curves.
        const float k_BackOvershoot = 1.70158f;
        // The period of the Elastic curves, and its quarter (the phase shift).
        const float k_ElasticPeriod = 0.3f;
        const float k_ElasticShift = k_ElasticPeriod / 4f;
        // The bounce segment ends and middles of OutBounce, in elevenths.
        const float k_Bounce1 = 4f / 11f, k_Bounce2 = 8f / 11f, k_Bounce3 = 10f / 11f;
        const float k_BounceMid2 = 6f / 11f, k_BounceMid3 = 9f / 11f, k_BounceMid4 = 21f / 22f;
        const double k_BounceScale = 7.5625;

        static float F(double value) => (float)value;

        static float Sin(double x) => (float)Math.Sin(F(x));

        static float Pow(double x, double p) => (float)Math.Pow(F(x), F(p));

        static float Sqrt(double x) => (float)Math.Sqrt(F(x));

        public static float Step(float t) => t < 0.5f ? 0f : 1f;

        public static float Linear(float t) => t;

        public static float InSine(float t) => F(Sin((double)HalfPi * ((double)t - 1.0)) + 1.0);

        public static float OutSine(float t) => Sin((double)t * HalfPi);

        public static float InOutSine(float t) => F((Sin((double)Mathf.PI * ((double)t - 0.5)) + 1.0) * 0.5);

        public static float InQuad(float t) => F((double)t * t);

        public static float OutQuad(float t) => F((double)t * (2.0 - t));

        public static float InOutQuad(float t)
        {
            t = F(t * 2.0);
            if (t < 1f) return F((double)t * t * 0.5);
            return F(-0.5 * (((double)t - 1.0) * ((double)t - 3.0) - 1.0));
        }

        public static float InCubic(float t) => InPower(t, 3);

        public static float OutCubic(float t) => OutPower(t, 3);

        public static float InOutCubic(float t) => InOutPower(t, 3);

        public static float InPower(float t, int power) => Pow(t, power);

        public static float OutPower(float t, int power)
        {
            int sign = power % 2 != 0 ? 1 : -1;
            return F(sign * ((double)Pow((double)t - 1.0, power) + sign));
        }

        public static float InOutPower(float t, int power)
        {
            t = F(t * 2.0);
            if (t < 1f) return F(InPower(t, power) * 0.5);
            int sign = power % 2 != 0 ? 1 : -1;
            return F(sign * 0.5 * ((double)Pow((double)t - 2.0, power) + sign * 2));
        }

        public static float InBounce(float t) => F(1.0 - OutBounce(F(1.0 - t)));

        public static float OutBounce(float t)
        {
            if (t < k_Bounce1) return F(k_BounceScale * t * t);
            if (t < k_Bounce2)
            {
                t = F((double)t - k_BounceMid2);
                return F(k_BounceScale * t * t + 0.75);
            }
            if (t < k_Bounce3)
            {
                t = F((double)t - k_BounceMid3);
                return F(k_BounceScale * t * t + 0.9375);
            }
            t = F((double)t - k_BounceMid4);
            return F(k_BounceScale * t * t + 63.0 / 64.0);
        }

        public static float InOutBounce(float t) =>
            t < 0.5f ? F(InBounce(F(t * 2.0)) * 0.5) : F(OutBounce(F(((double)t - 0.5) * 2.0)) * 0.5 + 0.5);

        public static float InElastic(float t)
        {
            if (t == 0f) return 0f;
            if (t == 1f) return 1f;
            t = F(t - 1.0);
            float amplitude = Pow(2.0, 10.0 * t);
            return F(-(amplitude * (double)Sin(((double)t - k_ElasticShift) * TwoPi / k_ElasticPeriod)));
        }

        public static float OutElastic(float t)
        {
            if (t == 0f) return 0f;
            if (t == 1f) return 1f;
            return F((double)Pow(2.0, -10.0 * t) * Sin(((double)t - k_ElasticShift) * TwoPi / k_ElasticPeriod) + 1.0);
        }

        public static float InOutElastic(float t) =>
            t < 0.5f ? F(InElastic(F(t * 2.0)) * 0.5) : F(OutElastic(F(((double)t - 0.5) * 2.0)) * 0.5 + 0.5);

        public static float InBack(float t) => InBack(t, k_BackOvershoot);

        public static float OutBack(float t) => F(1.0 - InBack(F(1.0 - t)));

        public static float InOutBack(float t) =>
            t < 0.5f ? F(InBack(F(t * 2.0)) * 0.5) : F(OutBack(F(((double)t - 0.5) * 2.0)) * 0.5 + 0.5);

        public static float InBack(float t, float s) => F((double)t * t * (((double)s + 1.0) * t - s));

        public static float OutBack(float t, float s) => F(1.0 - InBack(F(1.0 - t), s));

        public static float InOutBack(float t, float s) =>
            t < 0.5f ? F(InBack(F(t * 2.0), s) * 0.5) : F(OutBack(F(((double)t - 0.5) * 2.0), s) * 0.5 + 0.5);

        public static float InCirc(float t) => F(-((double)Sqrt(1.0 - (double)t * t) - 1.0));

        public static float OutCirc(float t)
        {
            t = F(t - 1.0);
            return Sqrt(1.0 - (double)t * t);
        }

        public static float InOutCirc(float t)
        {
            t = F(t * 2.0);
            if (t < 1f) return F(-0.5 * ((double)Sqrt(1.0 - (double)t * t) - 1.0));
            t = F(t - 2.0);
            return F(0.5 * ((double)Sqrt(1.0 - (double)t * t) + 1.0));
        }
    }

    /// <summary>Unclamped interpolation of animated values.</summary>
    internal static partial class Lerp
    {
        public static float Interpolate(float start, float end, float ratio) => Mathf.LerpUnclamped(start, end, ratio);

        public static int Interpolate(int start, int end, float ratio) => Mathf.RoundToInt(Mathf.LerpUnclamped(start, end, ratio));

        public static Rect Interpolate(Rect r1, Rect r2, float ratio) =>
            new Rect(Mathf.LerpUnclamped(r1.x, r2.x, ratio), Mathf.LerpUnclamped(r1.y, r2.y, ratio),
                Mathf.LerpUnclamped(r1.width, r2.width, ratio), Mathf.LerpUnclamped(r1.height, r2.height, ratio));

        public static Color Interpolate(Color start, Color end, float ratio) => Color.LerpUnclamped(start, end, ratio);

        public static Vector2 Interpolate(Vector2 start, Vector2 end, float ratio) => Vector2.LerpUnclamped(start, end, ratio);

        public static Vector3 Interpolate(Vector3 start, Vector3 end, float ratio) => Vector3.LerpUnclamped(start, end, ratio);

        public static Quaternion Interpolate(Quaternion start, Quaternion end, float ratio) => Quaternion.SlerpUnclamped(start, end, ratio);

        /// <summary>Each property of the end values interpolated from the start's (which must have it).
        /// The result reuses one buffer per end value set: it holds until the next interpolation.</summary>
        internal static StyleValues Interpolate(StyleValues start, StyleValues end, float ratio)
        {
            var result = default(StyleValues);
            if (end.m_UaValues == null) return result;
            var values = end.m_UaValues.InterpolationBuffer();
            result.m_UaValues = values;
            foreach (var value in end.m_UaValues.items)
            {
                if (start.m_UaValues == null || !start.m_UaValues.TryGet(value.id, out var startValue))
                    throw new ArgumentException("Start StyleValues must contain the same values as end values. Missing property:" + value.id);
                if (UaStyleValueList.IsColor(value.id))
                    values.Set(value.id, 0f, Interpolate(startValue.color, value.color, ratio));
                else if (UaStyleValueList.IsNumber(value.id))
                    values.Set(value.id, Interpolate(startValue.number, value.number, ratio), default);
                else
                    throw new ArgumentException("Style Value can't be animated");
            }
            return result;
        }
    }

    /// <summary>A set of animatable style values.</summary>
    public partial struct StyleValues
    {
        internal UaStyleValueList m_UaValues;

        public float top { get => Number(StylePropertyId.Top); set => SetValue(StylePropertyId.Top, value); }
        public float left { get => Number(StylePropertyId.Left); set => SetValue(StylePropertyId.Left, value); }
        public float width { get => Number(StylePropertyId.Width); set => SetValue(StylePropertyId.Width, value); }
        public float height { get => Number(StylePropertyId.Height); set => SetValue(StylePropertyId.Height, value); }
        public float right { get => Number(StylePropertyId.Right); set => SetValue(StylePropertyId.Right, value); }
        public float bottom { get => Number(StylePropertyId.Bottom); set => SetValue(StylePropertyId.Bottom, value); }
        public Color color { get => ColorOf(StylePropertyId.Color); set => SetValue(StylePropertyId.Color, value); }
        public Color backgroundColor { get => ColorOf(StylePropertyId.BackgroundColor); set => SetValue(StylePropertyId.BackgroundColor, value); }
        public Color unityBackgroundImageTintColor { get => ColorOf(StylePropertyId.UnityBackgroundImageTintColor); set => SetValue(StylePropertyId.UnityBackgroundImageTintColor, value); }
        public Color borderColor { get => ColorOf(StylePropertyId.BorderColor); set => SetValue(StylePropertyId.BorderColor, value); }
        public float marginLeft { get => Number(StylePropertyId.MarginLeft); set => SetValue(StylePropertyId.MarginLeft, value); }
        public float marginTop { get => Number(StylePropertyId.MarginTop); set => SetValue(StylePropertyId.MarginTop, value); }
        public float marginRight { get => Number(StylePropertyId.MarginRight); set => SetValue(StylePropertyId.MarginRight, value); }
        public float marginBottom { get => Number(StylePropertyId.MarginBottom); set => SetValue(StylePropertyId.MarginBottom, value); }
        public float paddingLeft { get => Number(StylePropertyId.PaddingLeft); set => SetValue(StylePropertyId.PaddingLeft, value); }
        public float paddingTop { get => Number(StylePropertyId.PaddingTop); set => SetValue(StylePropertyId.PaddingTop, value); }
        public float paddingRight { get => Number(StylePropertyId.PaddingRight); set => SetValue(StylePropertyId.PaddingRight, value); }
        public float paddingBottom { get => Number(StylePropertyId.PaddingBottom); set => SetValue(StylePropertyId.PaddingBottom, value); }
        public float borderLeftWidth { get => Number(StylePropertyId.BorderLeftWidth); set => SetValue(StylePropertyId.BorderLeftWidth, value); }
        public float borderRightWidth { get => Number(StylePropertyId.BorderRightWidth); set => SetValue(StylePropertyId.BorderRightWidth, value); }
        public float borderTopWidth { get => Number(StylePropertyId.BorderTopWidth); set => SetValue(StylePropertyId.BorderTopWidth, value); }
        public float borderBottomWidth { get => Number(StylePropertyId.BorderBottomWidth); set => SetValue(StylePropertyId.BorderBottomWidth, value); }
        public float borderTopLeftRadius { get => Number(StylePropertyId.BorderTopLeftRadius); set => SetValue(StylePropertyId.BorderTopLeftRadius, value); }
        public float borderTopRightRadius { get => Number(StylePropertyId.BorderTopRightRadius); set => SetValue(StylePropertyId.BorderTopRightRadius, value); }
        public float borderBottomLeftRadius { get => Number(StylePropertyId.BorderBottomLeftRadius); set => SetValue(StylePropertyId.BorderBottomLeftRadius, value); }
        public float borderBottomRightRadius { get => Number(StylePropertyId.BorderBottomRightRadius); set => SetValue(StylePropertyId.BorderBottomRightRadius, value); }
        public float opacity { get => Number(StylePropertyId.Opacity); set => SetValue(StylePropertyId.Opacity, value); }
        public float flexGrow { get => Number(StylePropertyId.FlexGrow); set => SetValue(StylePropertyId.FlexGrow, value); }

        // As in Unity, setting flexShrink sets flexGrow.
        public float flexShrink { get => Number(StylePropertyId.FlexShrink); set => SetValue(StylePropertyId.FlexGrow, value); }

        float Number(StylePropertyId id) => UaValues().TryGet(id, out var v) ? v.number : 0f;

        Color ColorOf(StylePropertyId id) => UaValues().TryGet(id, out var v) ? v.color : default;

        internal void SetValue(StylePropertyId id, float value) => UaValues().Set(id, value, default);

        internal void SetValue(StylePropertyId id, Color value) => UaValues().Set(id, 0f, value);

        internal UaStyleValueList UaValues() => m_UaValues ??= new UaStyleValueList();
    }

    /// <summary>The values of a StyleValues set, in the order they were first set.</summary>
    internal sealed class UaStyleValueList
    {
        internal struct Item
        {
            public StylePropertyId id;
            public float number;
            public Color color;
        }

        internal readonly List<Item> items = new List<Item>();
        UaStyleValueList m_InterpolationBuffer;

        public bool TryGet(StylePropertyId id, out Item item)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].id != id) continue;
                item = items[i];
                return true;
            }
            item = default;
            return false;
        }

        public void Set(StylePropertyId id, float number, Color color)
        {
            var item = new Item { id = id, number = number, color = color };
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].id != id) continue;
                items[i] = item;
                return;
            }
            items.Add(item);
        }

        /// <summary>A list this set's interpolations write into, emptied.</summary>
        public UaStyleValueList InterpolationBuffer()
        {
            m_InterpolationBuffer ??= new UaStyleValueList();
            m_InterpolationBuffer.items.Clear();
            return m_InterpolationBuffer;
        }

        public static bool IsColor(StylePropertyId id) =>
            id == StylePropertyId.Color || id == StylePropertyId.UnityBackgroundImageTintColor
            || id == StylePropertyId.BorderColor || id == StylePropertyId.BackgroundColor;

        public static bool IsNumber(StylePropertyId id)
        {
            switch (id)
            {
                case StylePropertyId.FontSize:
                case StylePropertyId.BorderBottomWidth:
                case StylePropertyId.BorderLeftWidth:
                case StylePropertyId.BorderRightWidth:
                case StylePropertyId.BorderTopWidth:
                case StylePropertyId.Bottom:
                case StylePropertyId.FlexBasis:
                case StylePropertyId.FlexGrow:
                case StylePropertyId.FlexShrink:
                case StylePropertyId.Height:
                case StylePropertyId.Left:
                case StylePropertyId.MarginBottom:
                case StylePropertyId.MarginLeft:
                case StylePropertyId.MarginRight:
                case StylePropertyId.MarginTop:
                case StylePropertyId.MaxHeight:
                case StylePropertyId.MaxWidth:
                case StylePropertyId.MinHeight:
                case StylePropertyId.MinWidth:
                case StylePropertyId.PaddingBottom:
                case StylePropertyId.PaddingLeft:
                case StylePropertyId.PaddingRight:
                case StylePropertyId.PaddingTop:
                case StylePropertyId.Right:
                case StylePropertyId.Top:
                case StylePropertyId.Width:
                case StylePropertyId.BorderBottomLeftRadius:
                case StylePropertyId.BorderBottomRightRadius:
                case StylePropertyId.BorderTopLeftRadius:
                case StylePropertyId.BorderTopRightRadius:
                case StylePropertyId.Opacity:
                    return true;
                default:
                    return false;
            }
        }
    }
}

namespace UnityEngine.UIElements
{
    public partial interface IExperimentalFeatures
    {
        ITransitionAnimations animation { get; }
    }

    /// <summary>The value animations of a panel, ticked once per panel update (and on the update
    /// after new ones start) after styles and layout.</summary>
    internal sealed class UaValueAnimationSystem
    {
        readonly HashSet<IValueAnimationUpdate> m_Animations = new HashSet<IValueAnimationUpdate>();
        readonly List<IValueAnimationUpdate> m_IterationList = new List<IValueAnimationUpdate>();
        bool m_HasNewAnimations;
        bool m_IterationListDirty;
        long m_LastUpdate;

        public void Register(IValueAnimationUpdate animation)
        {
            m_Animations.Add(animation);
            m_HasNewAnimations = true;
            m_IterationListDirty = true;
        }

        public void Unregister(IValueAnimationUpdate animation)
        {
            m_Animations.Remove(animation);
            m_IterationListDirty = true;
        }

        public void Update()
        {
            long now = Panel.TimeSinceStartupMs();
            if (m_IterationListDirty)
            {
                m_IterationList.Clear();
                foreach (var animation in m_Animations) m_IterationList.Add(animation);
                m_IterationListDirty = false;
            }
            if (!m_HasNewAnimations && m_LastUpdate == now) return;
            // Animations stopped by an earlier one in this tick still tick, as in Unity.
            for (int i = 0; i < m_IterationList.Count; i++) m_IterationList[i].Tick(now);
            m_HasNewAnimations = false;
            m_LastUpdate = now;
        }
    }

    public partial class VisualElement : ITransitionAnimations, IExperimentalFeatures
    {
        List<IValueAnimationUpdate> m_RunningAnimations;

        public IExperimentalFeatures experimental => this;

        ITransitionAnimations IExperimentalFeatures.animation => this;

        /// <summary>Adds or removes a running animation of this element (and of its panel's
        /// system while in a panel).</summary>
        internal void RegisterAnimationUpdate(IValueAnimationUpdate animation, bool running)
        {
            if (running)
            {
                (m_RunningAnimations ??= new List<IValueAnimationUpdate>()).Add(animation);
                elementPanel?.uaValueAnimations.Register(animation);
            }
            else
            {
                m_RunningAnimations?.Remove(animation);
                elementPanel?.uaValueAnimations.Unregister(animation);
            }
        }

        // Running animations follow the element from panel to panel.
        void UaUnregisterRunningAnimations()
        {
            if (elementPanel == null || m_RunningAnimations == null) return;
            for (int i = 0; i < m_RunningAnimations.Count; i++) elementPanel.uaValueAnimations.Unregister(m_RunningAnimations[i]);
        }

        void UaRegisterRunningAnimations()
        {
            if (elementPanel == null || m_RunningAnimations == null) return;
            for (int i = 0; i < m_RunningAnimations.Count; i++) elementPanel.uaValueAnimations.Register(m_RunningAnimations[i]);
        }

        ValueAnimation<float> ITransitionAnimations.Start(float from, float to, int durationMs, Action<VisualElement, float> onValueChanged) =>
            experimental.animation.Start(e => from, to, durationMs, onValueChanged);

        ValueAnimation<Rect> ITransitionAnimations.Start(Rect from, Rect to, int durationMs, Action<VisualElement, Rect> onValueChanged) =>
            experimental.animation.Start(e => from, to, durationMs, onValueChanged);

        ValueAnimation<Color> ITransitionAnimations.Start(Color from, Color to, int durationMs, Action<VisualElement, Color> onValueChanged) =>
            experimental.animation.Start(e => from, to, durationMs, onValueChanged);

        ValueAnimation<Vector3> ITransitionAnimations.Start(Vector3 from, Vector3 to, int durationMs, Action<VisualElement, Vector3> onValueChanged) =>
            experimental.animation.Start(e => from, to, durationMs, onValueChanged);

        ValueAnimation<Vector2> ITransitionAnimations.Start(Vector2 from, Vector2 to, int durationMs, Action<VisualElement, Vector2> onValueChanged) =>
            experimental.animation.Start(e => from, to, durationMs, onValueChanged);

        ValueAnimation<Quaternion> ITransitionAnimations.Start(Quaternion from, Quaternion to, int durationMs, Action<VisualElement, Quaternion> onValueChanged) =>
            experimental.animation.Start(e => from, to, durationMs, onValueChanged);

        ValueAnimation<StyleValues> ITransitionAnimations.Start(StyleValues from, StyleValues to, int durationMs)
        {
            from.UaValues();
            to.UaValues();
            return StartStyleAnimation(e => from, to, durationMs);
        }

        ValueAnimation<StyleValues> ITransitionAnimations.Start(StyleValues to, int durationMs)
        {
            to.UaValues();
            return StartStyleAnimation(e => ReadCurrentValues(e, to), to, durationMs);
        }

        ValueAnimation<float> ITransitionAnimations.Start(Func<VisualElement, float> fromValueGetter, float to, int durationMs, Action<VisualElement, float> onValueChanged) =>
            StartAnimation(ValueAnimation<float>.Create(this, Lerp.Interpolate), fromValueGetter, to, durationMs, onValueChanged);

        ValueAnimation<Rect> ITransitionAnimations.Start(Func<VisualElement, Rect> fromValueGetter, Rect to, int durationMs, Action<VisualElement, Rect> onValueChanged) =>
            StartAnimation(ValueAnimation<Rect>.Create(this, Lerp.Interpolate), fromValueGetter, to, durationMs, onValueChanged);

        ValueAnimation<Color> ITransitionAnimations.Start(Func<VisualElement, Color> fromValueGetter, Color to, int durationMs, Action<VisualElement, Color> onValueChanged) =>
            StartAnimation(ValueAnimation<Color>.Create(this, Lerp.Interpolate), fromValueGetter, to, durationMs, onValueChanged);

        ValueAnimation<Vector3> ITransitionAnimations.Start(Func<VisualElement, Vector3> fromValueGetter, Vector3 to, int durationMs, Action<VisualElement, Vector3> onValueChanged) =>
            StartAnimation(ValueAnimation<Vector3>.Create(this, Lerp.Interpolate), fromValueGetter, to, durationMs, onValueChanged);

        ValueAnimation<Vector2> ITransitionAnimations.Start(Func<VisualElement, Vector2> fromValueGetter, Vector2 to, int durationMs, Action<VisualElement, Vector2> onValueChanged) =>
            StartAnimation(ValueAnimation<Vector2>.Create(this, Lerp.Interpolate), fromValueGetter, to, durationMs, onValueChanged);

        ValueAnimation<Quaternion> ITransitionAnimations.Start(Func<VisualElement, Quaternion> fromValueGetter, Quaternion to, int durationMs, Action<VisualElement, Quaternion> onValueChanged) =>
            StartAnimation(ValueAnimation<Quaternion>.Create(this, Lerp.Interpolate), fromValueGetter, to, durationMs, onValueChanged);

        ValueAnimation<StyleValues> StartStyleAnimation(Func<VisualElement, StyleValues> fromValueGetter, StyleValues to, int durationMs) =>
            StartAnimation(ValueAnimation<StyleValues>.Create(this, Lerp.Interpolate), fromValueGetter, to, durationMs, AssignStyleValues);

        static ValueAnimation<T> StartAnimation<T>(ValueAnimation<T> animation, Func<VisualElement, T> fromValueGetter, T to, int durationMs, Action<VisualElement, T> onValueChanged)
        {
            animation.initialValue = fromValueGetter;
            animation.to = to;
            animation.durationMs = durationMs;
            animation.valueUpdated = onValueChanged;
            animation.Start();
            return animation;
        }

        ValueAnimation<Rect> ITransitionAnimations.Layout(Rect to, int durationMs) =>
            experimental.animation.Start(e => new Rect(e.resolvedStyle.left, e.resolvedStyle.top, e.resolvedStyle.width, e.resolvedStyle.height), to, durationMs, (e, c) =>
            {
                e.style.left = c.x;
                e.style.top = c.y;
                e.style.width = c.width;
                e.style.height = c.height;
            });

        ValueAnimation<Vector2> ITransitionAnimations.TopLeft(Vector2 to, int durationMs) =>
            experimental.animation.Start(e => new Vector2(e.resolvedStyle.left, e.resolvedStyle.top), to, durationMs, (e, c) =>
            {
                e.style.left = c.x;
                e.style.top = c.y;
            });

        ValueAnimation<Vector2> ITransitionAnimations.Size(Vector2 to, int durationMs) =>
            experimental.animation.Start(e => e.layout.size, to, durationMs, (e, c) =>
            {
                e.style.width = c.x;
                e.style.height = c.y;
            });

        ValueAnimation<float> ITransitionAnimations.Scale(float to, int durationMs) =>
            experimental.animation.Start(e => e.transform.scale.x, to, durationMs, (e, c) => e.transform.scale = new Vector3(c, c, c));

        ValueAnimation<Vector3> ITransitionAnimations.Position(Vector3 to, int durationMs) =>
            experimental.animation.Start(e => e.transform.position, to, durationMs, (e, c) => e.transform.position = c);

        ValueAnimation<Quaternion> ITransitionAnimations.Rotation(Quaternion to, int durationMs) =>
            experimental.animation.Start(e => e.transform.rotation, to, durationMs, (e, c) => e.transform.rotation = c);

        /// <summary>Sets each value of the set as the element's inline style (a border color on all
        /// four sides).</summary>
        static void AssignStyleValues(VisualElement ve, StyleValues src)
        {
            if (src.m_UaValues == null) return;
            var style = ve.style;
            foreach (var value in src.m_UaValues.items)
            {
                switch (value.id)
                {
                    case StylePropertyId.MarginLeft: style.marginLeft = value.number; break;
                    case StylePropertyId.MarginTop: style.marginTop = value.number; break;
                    case StylePropertyId.MarginRight: style.marginRight = value.number; break;
                    case StylePropertyId.MarginBottom: style.marginBottom = value.number; break;
                    case StylePropertyId.PaddingLeft: style.paddingLeft = value.number; break;
                    case StylePropertyId.PaddingTop: style.paddingTop = value.number; break;
                    case StylePropertyId.PaddingRight: style.paddingRight = value.number; break;
                    case StylePropertyId.PaddingBottom: style.paddingBottom = value.number; break;
                    case StylePropertyId.Left: style.left = value.number; break;
                    case StylePropertyId.Top: style.top = value.number; break;
                    case StylePropertyId.Right: style.right = value.number; break;
                    case StylePropertyId.Bottom: style.bottom = value.number; break;
                    case StylePropertyId.Width: style.width = value.number; break;
                    case StylePropertyId.Height: style.height = value.number; break;
                    case StylePropertyId.FlexGrow: style.flexGrow = value.number; break;
                    case StylePropertyId.FlexShrink: style.flexShrink = value.number; break;
                    case StylePropertyId.BorderLeftWidth: style.borderLeftWidth = value.number; break;
                    case StylePropertyId.BorderTopWidth: style.borderTopWidth = value.number; break;
                    case StylePropertyId.BorderRightWidth: style.borderRightWidth = value.number; break;
                    case StylePropertyId.BorderBottomWidth: style.borderBottomWidth = value.number; break;
                    case StylePropertyId.BorderTopLeftRadius: style.borderTopLeftRadius = value.number; break;
                    case StylePropertyId.BorderTopRightRadius: style.borderTopRightRadius = value.number; break;
                    case StylePropertyId.BorderBottomRightRadius: style.borderBottomRightRadius = value.number; break;
                    case StylePropertyId.BorderBottomLeftRadius: style.borderBottomLeftRadius = value.number; break;
                    case StylePropertyId.FontSize: style.fontSize = value.number; break;
                    case StylePropertyId.Color: style.color = value.color; break;
                    case StylePropertyId.BackgroundColor: style.backgroundColor = value.color; break;
                    case StylePropertyId.BorderColor:
                        style.borderLeftColor = value.color;
                        style.borderTopColor = value.color;
                        style.borderRightColor = value.color;
                        style.borderBottomColor = value.color;
                        break;
                    case StylePropertyId.UnityBackgroundImageTintColor: style.unityBackgroundImageTintColor = value.color; break;
                    case StylePropertyId.Opacity: style.opacity = value.number; break;
                }
            }
        }

        /// <summary>The element's resolved values of the properties in the set.</summary>
        static StyleValues ReadCurrentValues(VisualElement ve, StyleValues targetValuesToRead)
        {
            var result = default(StyleValues);
            if (targetValuesToRead.m_UaValues == null) return result;
            var resolved = ve.resolvedStyle;
            foreach (var value in targetValuesToRead.m_UaValues.items)
            {
                switch (value.id)
                {
                    case StylePropertyId.MarginLeft: result.marginLeft = resolved.marginLeft; break;
                    case StylePropertyId.MarginTop: result.marginTop = resolved.marginTop; break;
                    case StylePropertyId.MarginRight: result.marginRight = resolved.marginRight; break;
                    case StylePropertyId.MarginBottom: result.marginBottom = resolved.marginBottom; break;
                    case StylePropertyId.PaddingLeft: result.paddingLeft = resolved.paddingLeft; break;
                    case StylePropertyId.PaddingTop: result.paddingTop = resolved.paddingTop; break;
                    case StylePropertyId.PaddingRight: result.paddingRight = resolved.paddingRight; break;
                    case StylePropertyId.PaddingBottom: result.paddingBottom = resolved.paddingBottom; break;
                    case StylePropertyId.Left: result.left = resolved.left; break;
                    case StylePropertyId.Top: result.top = resolved.top; break;
                    case StylePropertyId.Right: result.right = resolved.right; break;
                    case StylePropertyId.Bottom: result.bottom = resolved.bottom; break;
                    case StylePropertyId.Width: result.width = resolved.width; break;
                    case StylePropertyId.Height: result.height = resolved.height; break;
                    case StylePropertyId.FlexGrow: result.flexGrow = resolved.flexGrow; break;
                    case StylePropertyId.FlexShrink: result.flexShrink = resolved.flexShrink; break;
                    case StylePropertyId.BorderLeftWidth: result.borderLeftWidth = resolved.borderLeftWidth; break;
                    case StylePropertyId.BorderTopWidth: result.borderTopWidth = resolved.borderTopWidth; break;
                    case StylePropertyId.BorderRightWidth: result.borderRightWidth = resolved.borderRightWidth; break;
                    case StylePropertyId.BorderBottomWidth: result.borderBottomWidth = resolved.borderBottomWidth; break;
                    case StylePropertyId.BorderTopLeftRadius: result.borderTopLeftRadius = resolved.borderTopLeftRadius; break;
                    case StylePropertyId.BorderTopRightRadius: result.borderTopRightRadius = resolved.borderTopRightRadius; break;
                    case StylePropertyId.BorderBottomRightRadius: result.borderBottomRightRadius = resolved.borderBottomRightRadius; break;
                    case StylePropertyId.BorderBottomLeftRadius: result.borderBottomLeftRadius = resolved.borderBottomLeftRadius; break;
                    case StylePropertyId.Color: result.color = resolved.color; break;
                    case StylePropertyId.BackgroundColor: result.backgroundColor = resolved.backgroundColor; break;
                    case StylePropertyId.BorderColor: result.borderColor = resolved.borderLeftColor; break;
                    case StylePropertyId.UnityBackgroundImageTintColor: result.unityBackgroundImageTintColor = resolved.unityBackgroundImageTintColor; break;
                    case StylePropertyId.Opacity: result.opacity = resolved.opacity; break;
                }
            }
            return result;
        }
    }
}
