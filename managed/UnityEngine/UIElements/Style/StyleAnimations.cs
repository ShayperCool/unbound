using System;
using System.Collections.Generic;
using Unbound.UIElements;
using UnityEngine.UIElements.StyleSheets;

namespace UnityEngine.UIElements
{
    // The release's internal style-property animation interface. Declarations follow the selected
    // release's assemblies; the behaviour matches the Unity 6000.0.71f1 Editor
    // (tests/projects/styleanimations): typed starts with an easing curve, the running and completed
    // counts, cancellation and the animated property list, on the same animations USS transitions run.
    internal partial interface IStylePropertyAnimations
    {
        bool Start(StylePropertyId id, float from, float to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, int from, int to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, Length from, Length to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, Color from, Color to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool StartEnum(StylePropertyId id, int from, int to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, Background from, Background to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, FontDefinition from, FontDefinition to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, Font from, Font to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, TextShadow from, TextShadow to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, Scale from, Scale to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, Translate from, Translate to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, Rotate from, Rotate to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, TransformOrigin from, TransformOrigin to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, BackgroundPosition from, BackgroundPosition to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, BackgroundRepeat from, BackgroundRepeat to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool Start(StylePropertyId id, BackgroundSize from, BackgroundSize to, int durationMs, int delayMs, Func<float, float> easingCurve);
        bool HasRunningAnimation(StylePropertyId id);
        void UpdateAnimation(StylePropertyId id);
        void GetAllAnimations(List<StylePropertyId> outPropertyIds);
        void CancelAnimation(StylePropertyId id);
        void CancelAllAnimations();
        int runningAnimationCount { get; set; }
        int completedAnimationCount { get; set; }
    }

    public partial class VisualElement : IStylePropertyAnimations
    {
        // Plain values the animations add to and take from: setting one changes only what the gates
        // below see (tests/projects/styleanimations counts.*).
        int IStylePropertyAnimations.runningAnimationCount { get; set; }
        int IStylePropertyAnimations.completedAnimationCount { get; set; }

        internal IStylePropertyAnimations styleAnimation => this;
        internal bool hasRunningAnimations => styleAnimation.runningAnimationCount > 0;
        internal bool hasCompletedAnimations => styleAnimation.completedAnimationCount > 0;

        /// <summary>This element's animations; outside a panel there are none to start or update
        /// (the release throws NullReferenceException there).</summary>
        StyleTransitions AnimationsInPanel()
        {
            if (elementPanel == null) throw new NullReferenceException();
            return uaTransitions ??= new StyleTransitions(this);
        }

        bool StartStyleAnimation<T>(StylePropertyId id, StyleTransitions.Kind kind, string typeName, T from, T to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            AnimationsInPanel().StartAnimation(id, kind, typeof(T), typeName, from, to, durationMs, delayMs, easingCurve);

        bool IStylePropertyAnimations.Start(StylePropertyId id, float from, float to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Float, "float", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, int from, int to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Int, "int", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, Length from, Length to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "Length", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, Color from, Color to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "Color", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.StartEnum(StylePropertyId id, int from, int to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Enum, "int", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, Background from, Background to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "Background", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, FontDefinition from, FontDefinition to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "FontDefinition", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, Font from, Font to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "Font", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, TextShadow from, TextShadow to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "TextShadow", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, Scale from, Scale to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "Scale", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, Translate from, Translate to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "Translate", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, Rotate from, Rotate to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "Rotate", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, TransformOrigin from, TransformOrigin to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "TransformOrigin", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, BackgroundPosition from, BackgroundPosition to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "BackgroundPosition", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, BackgroundRepeat from, BackgroundRepeat to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "BackgroundRepeat", from, to, durationMs, delayMs, easingCurve);
        bool IStylePropertyAnimations.Start(StylePropertyId id, BackgroundSize from, BackgroundSize to, int durationMs, int delayMs, Func<float, float> easingCurve) =>
            StartStyleAnimation(id, StyleTransitions.Kind.Value, "BackgroundSize", from, to, durationMs, delayMs, easingCurve);

        void IStylePropertyAnimations.CancelAnimation(StylePropertyId id)
        {
            if (elementPanel != null) uaTransitions?.Cancel(id);
        }

        void IStylePropertyAnimations.CancelAllAnimations()
        {
            if ((hasRunningAnimations || hasCompletedAnimations) && elementPanel != null) uaTransitions?.CancelAll(sendEvents: true);
        }

        bool IStylePropertyAnimations.HasRunningAnimation(StylePropertyId id) =>
            hasRunningAnimations && AnimationsInPanel().IsRunning(id);

        void IStylePropertyAnimations.UpdateAnimation(StylePropertyId id) => AnimationsInPanel().Present(id);

        void IStylePropertyAnimations.GetAllAnimations(List<StylePropertyId> outPropertyIds)
        {
            if (hasRunningAnimations || hasCompletedAnimations) AnimationsInPanel().GetAll(outPropertyIds);
        }
    }
}
