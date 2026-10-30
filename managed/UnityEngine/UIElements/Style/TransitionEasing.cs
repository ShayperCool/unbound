using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    internal static class TransitionEasing
    {
        internal static float Evaluate(EasingMode mode, float t)
        {
            switch (mode)
            {
                case EasingMode.Linear: return t;
                case EasingMode.Ease: return 3 * (1-t) * (1-t) * t * .6f + 3 * (1-t) * t * t + t * t * t;
                case EasingMode.EaseIn: return t * t;
                case EasingMode.EaseOut: return t * (2-t);
                case EasingMode.EaseInOut: return t < .5f ? 2*t*t : 1-2*(1-t)*(1-t);
                case EasingMode.EaseInSine: return 1 - Mathf.Cos(t * Mathf.PI * .5f);
                case EasingMode.EaseOutSine: return Mathf.Sin(t * Mathf.PI * .5f);
                case EasingMode.EaseInOutSine: return .5f * (1 - Mathf.Cos(t * Mathf.PI));
                case EasingMode.EaseInCubic: return t * t * t;
                case EasingMode.EaseOutCubic: return 1 - (1-t) * (1-t) * (1-t);
                case EasingMode.EaseInOutCubic: return t < .5f ? 4*t*t*t : 1 - 4*(1-t)*(1-t)*(1-t);
                case EasingMode.EaseInCirc: return 1 - Mathf.Sqrt(1-t*t);
                case EasingMode.EaseOutCirc: return Mathf.Sqrt(1-(t-1)*(t-1));
                case EasingMode.EaseInOutCirc: return t < .5f ? (1-Mathf.Sqrt(1-4*t*t))*.5f : (Mathf.Sqrt(1-4*(1-t)*(1-t))+1)*.5f;
                case EasingMode.EaseInElastic: return ElasticIn(t);
                case EasingMode.EaseOutElastic: return 1-ElasticIn(1-t);
                case EasingMode.EaseInOutElastic: return t < .5f ? ElasticIn(t*2)*.5f : 1-ElasticIn((1-t)*2)*.5f;
                case EasingMode.EaseInBack: return BackIn(t);
                case EasingMode.EaseOutBack: return 1-BackIn(1-t);
                case EasingMode.EaseInOutBack: return t < .5f ? BackIn(t*2)*.5f : 1-BackIn((1-t)*2)*.5f;
                case EasingMode.EaseInBounce: return 1-BounceOut(1-t);
                case EasingMode.EaseOutBounce: return BounceOut(t);
                case EasingMode.EaseInOutBounce: return t < .5f ? (1-BounceOut(1-t*2))*.5f : (1+BounceOut(t*2-1))*.5f;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
        static float BackIn(float t) => t*t*(2.70158f*t-1.70158f);
        static float ElasticIn(float t) => t == 0 || t == 1 ? t : -Mathf.Pow(2,10*(t-1))*Mathf.Sin((t-1.075f)*2*Mathf.PI/.3f);
        static float BounceOut(float t)
        {
            if (t < 1/2.75f) return 7.5625f*t*t;
            if (t < 2/2.75f) { t -= 1.5f/2.75f; return 7.5625f*t*t+.75f; }
            if (t < 2.5f/2.75f) { t -= 2.25f/2.75f; return 7.5625f*t*t+.9375f; }
            t -= 2.625f/2.75f; return 7.5625f*t*t+.984375f;
        }
    }
}
