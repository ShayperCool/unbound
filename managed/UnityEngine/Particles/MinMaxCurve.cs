using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class ParticleSystem
    {
        /// <summary>
        /// A value that is a constant, a curve, or random between two constants or two curves.
        /// Like Unity's, the struct keeps all six values apart: the mode, the curve multiplier, both
        /// curves and both constants; script fields serialize them as <c>m_Mode</c> ...
        /// <c>m_ConstantMax</c>. A module property keeps one stored scalar instead
        /// (<see cref="ModuleCurve"/>), so what a module returns is not always what was assigned.
        /// </summary>
        [Serializable]
        public partial struct MinMaxCurve
        {
            [SerializeField] internal ParticleSystemCurveMode m_Mode;
            [SerializeField] internal float m_CurveMultiplier;
            [SerializeField] internal AnimationCurve m_CurveMin;
            [SerializeField] internal AnimationCurve m_CurveMax;
            [SerializeField] internal float m_ConstantMin;
            [SerializeField] internal float m_ConstantMax;

            public MinMaxCurve(float constant)
            {
                m_Mode = ParticleSystemCurveMode.Constant;
                m_CurveMultiplier = 0f;
                m_CurveMin = null;
                m_CurveMax = null;
                m_ConstantMin = 0f;
                m_ConstantMax = constant;
            }

            public MinMaxCurve(float multiplier, AnimationCurve curve)
            {
                m_Mode = ParticleSystemCurveMode.Curve;
                m_CurveMultiplier = multiplier;
                m_CurveMin = null;
                m_CurveMax = curve;
                m_ConstantMin = 0f;
                m_ConstantMax = 0f;
            }

            public MinMaxCurve(float multiplier, AnimationCurve min, AnimationCurve max)
            {
                m_Mode = ParticleSystemCurveMode.TwoCurves;
                m_CurveMultiplier = multiplier;
                m_CurveMin = min;
                m_CurveMax = max;
                m_ConstantMin = 0f;
                m_ConstantMax = 0f;
            }

            public MinMaxCurve(float min, float max)
            {
                m_Mode = ParticleSystemCurveMode.TwoConstants;
                m_CurveMultiplier = 0f;
                m_CurveMin = null;
                m_CurveMax = null;
                m_ConstantMin = min;
                m_ConstantMax = max;
            }

            public ParticleSystemCurveMode mode { get => m_Mode; set => m_Mode = value; }

            [Obsolete("Please use MinMaxCurve.curveMultiplier instead. (UnityUpgradable) -> UnityEngine.ParticleSystem/MinMaxCurve.curveMultiplier", false)]
            public float curveScalar { get => m_CurveMultiplier; set => m_CurveMultiplier = value; }

            public float curveMultiplier { get => m_CurveMultiplier; set => m_CurveMultiplier = value; }
            public AnimationCurve curveMax { get => m_CurveMax; set => m_CurveMax = value; }
            public AnimationCurve curveMin { get => m_CurveMin; set => m_CurveMin = value; }
            public float constantMax { get => m_ConstantMax; set => m_ConstantMax = value; }
            public float constantMin { get => m_ConstantMin; set => m_ConstantMin = value; }
            public float constant { get => m_ConstantMax; set => m_ConstantMax = value; }
            public AnimationCurve curve { get => m_CurveMax; set => m_CurveMax = value; }

            public float Evaluate(float time) => Evaluate(time, 1f);

            /// <summary>The value in the current mode; a curve mode with a missing curve throws, as
            /// in Unity. The lerp factor is clamped to [0, 1].</summary>
            public float Evaluate(float time, float lerpFactor)
            {
                switch (m_Mode)
                {
                    case ParticleSystemCurveMode.Constant:
                        return m_ConstantMax;
                    case ParticleSystemCurveMode.TwoConstants:
                        return Mathf.Lerp(m_ConstantMin, m_ConstantMax, lerpFactor);
                    case ParticleSystemCurveMode.TwoCurves:
                        return Mathf.Lerp(m_CurveMin.Evaluate(time), m_CurveMax.Evaluate(time), lerpFactor) * m_CurveMultiplier;
                    default:
                        return m_CurveMax.Evaluate(time) * m_CurveMultiplier;
                }
            }

            public static implicit operator MinMaxCurve(float constant) => new MinMaxCurve(constant);
        }

        /// <summary>The bounds a module property keeps its stored scalars in (6000.0.71f1,
        /// tests/projects/particlecurves: every property in the probe is in one of these).</summary>
        internal enum CurveRange : byte
        {
            /// <summary>No bound.</summary>
            Any,
            /// <summary>[0, +inf): sizes, rates, delays, limits, drag, light range and intensity,
            /// trail width, the external-forces multiplier and burst counts.</summary>
            NonNegative,
            /// <summary>[<see cref="ModuleCurve.MinimumStartLifetime"/>, +inf): the start lifetime.</summary>
            StartLifetime,
            /// <summary>[0, 1]: collision dampen and lifetime loss, the trail lifetime.</summary>
            Unit,
            /// <summary>[0, 2]: collision bounce.</summary>
            Bounce,
            /// <summary>[0, <see cref="ModuleCurve.LastFrame"/>]: the normalized texture-sheet frames.</summary>
            Frame,
        }

        /// <summary>
        /// A MinMaxCurve as a module property stores it (6000.0.71f1, tests/projects/particlecurves):
        /// one <see cref="scalar"/> that is the constant, the upper constant or the curve multiplier
        /// depending on the mode, a lower constant <see cref="minScalar"/> and the curves. Every way
        /// in (the property setter, its <c>*Multiplier</c> shortcut and scene data) keeps both scalars
        /// within the property's <see cref="range"/>; the range does not reorder them. Reading the
        /// property gives back only what the mode uses: constants with a curve multiplier of 1 and no
        /// curves, or curves with constants of 0. A curve mode stores a missing curve as the constant
        /// curve 1.
        /// </summary>
        internal struct ModuleCurve
        {
            /// <summary>The shortest start lifetime a module keeps, in seconds.</summary>
            internal const float MinimumStartLifetime = 0.0001f;

            /// <summary>The largest normalized texture-sheet frame, just below the last frame's end.</summary>
            internal const float LastFrame = 0.9999f;

            internal ParticleSystemCurveMode mode;
            internal float scalar;
            internal float minScalar;
            internal AnimationCurve maxCurve;
            internal AnimationCurve minCurve;
            internal CurveRange range;

            internal static ModuleCurve Constant(float value, CurveRange range = CurveRange.Any)
            {
                var c = new ModuleCurve { range = range };
                c.scalar = c.Bounded(value);
                c.minScalar = c.Bounded(0f);
                return c;
            }

            internal static ModuleCurve Curve(float multiplier, AnimationCurve curve, CurveRange range = CurveRange.Any)
            {
                var c = Constant(multiplier, range);
                c.mode = ParticleSystemCurveMode.Curve;
                c.maxCurve = curve;
                return c;
            }

            internal bool IsCurve => mode == ParticleSystemCurveMode.Curve || mode == ParticleSystemCurveMode.TwoCurves;

            /// <summary>True when the value can differ between particles (random modes).</summary>
            internal bool IsRandom => mode == ParticleSystemCurveMode.TwoConstants || mode == ParticleSystemCurveMode.TwoCurves;

            internal float Evaluate(float time) => Evaluate(time, 1f);

            internal float Evaluate(float time, float lerpFactor)
            {
                switch (mode)
                {
                    case ParticleSystemCurveMode.Constant:
                        return scalar;
                    case ParticleSystemCurveMode.TwoConstants:
                        return Mathf.Lerp(minScalar, scalar, lerpFactor);
                    case ParticleSystemCurveMode.TwoCurves:
                        return Mathf.Lerp(Sample(minCurve, time), Sample(maxCurve, time), lerpFactor) * scalar;
                    default:
                        return Sample(maxCurve, time) * scalar;
                }
            }

            // A constant-mode value evaluated in a curve mode (the force module reads every axis in
            // the first axis' mode) has no curve: 0.
            static float Sample(AnimationCurve curve, float time) => curve != null ? curve.Evaluate(time) : 0f;

            /// <summary>The largest value the curve can take (constant or sampled), for bounds and limits.</summary>
            internal float MaxValue
            {
                get
                {
                    switch (mode)
                    {
                        case ParticleSystemCurveMode.Constant: return scalar;
                        case ParticleSystemCurveMode.TwoConstants: return Mathf.Max(scalar, minScalar);
                        default:
                        {
                            float max = float.NegativeInfinity;
                            for (int i = 0; i <= 8; i++)
                            {
                                max = Mathf.Max(max, Evaluate(i / 8f, 0f));
                                max = Mathf.Max(max, Evaluate(i / 8f, 1f));
                            }
                            return max;
                        }
                    }
                }
            }

            float Bounded(float value)
            {
                switch (range)
                {
                    case CurveRange.NonNegative: return Mathf.Max(value, 0f);
                    case CurveRange.StartLifetime: return Mathf.Max(value, MinimumStartLifetime);
                    case CurveRange.Unit: return Mathf.Clamp(value, 0f, 1f);
                    case CurveRange.Bounce: return Mathf.Clamp(value, 0f, 2f);
                    case CurveRange.Frame: return Mathf.Clamp(value, 0f, LastFrame);
                    default: return value;
                }
            }

            /// <summary>The property getter: what the mode uses, with copies of the curves.</summary>
            internal MinMaxCurve Get()
            {
                switch (mode)
                {
                    case ParticleSystemCurveMode.Constant:
                        return new MinMaxCurve(scalar) { curveMultiplier = 1f };
                    case ParticleSystemCurveMode.TwoConstants:
                        return new MinMaxCurve(minScalar, scalar) { curveMultiplier = 1f };
                    case ParticleSystemCurveMode.TwoCurves:
                        return new MinMaxCurve(scalar, ParticleCopies.Curve(minCurve), ParticleCopies.Curve(maxCurve));
                    default:
                        return new MinMaxCurve(scalar, ParticleCopies.Curve(maxCurve)) { mode = mode };
                }
            }

            /// <summary>The property setter: the mode's scalars within the range, curves copied
            /// (a missing one becomes the constant curve 1). The range stays the property's.</summary>
            internal void Set(in MinMaxCurve value)
            {
                mode = value.mode;
                bool curves = IsCurve;
                scalar = Bounded(curves ? value.curveMultiplier : value.constantMax);
                minScalar = mode == ParticleSystemCurveMode.TwoConstants ? Bounded(value.constantMin) : Bounded(0f);
                maxCurve = curves ? CurveOrOne(value.curveMax) : null;
                minCurve = mode == ParticleSystemCurveMode.TwoCurves ? CurveOrOne(value.curveMin) : null;
            }

            /// <summary>The <c>*Multiplier</c> shortcut: the stored scalar in every mode.</summary>
            internal float Multiplier
            {
                get => scalar;
                set => scalar = Bounded(value);
            }

            static AnimationCurve CurveOrOne(AnimationCurve curve) => curve != null ? ParticleCopies.Curve(curve) : One();

            /// <summary>The curve a curve mode stores when it is given none: the constant 1.</summary>
            static AnimationCurve One() => new AnimationCurve(new Keyframe(0f, 1f));

            /// <summary>The serialized form `{minMaxState, scalar, minScalar, maxCurve, minCurve}`
            /// over this value (the property's default and range); the scalars are kept within the
            /// range as when set. Data written before `minScalar` existed kept the lower constant as
            /// the min curve's first key times the scalar.</summary>
            internal ModuleCurve Read(YNode node)
            {
                if (node == null || node.Kind != YKind.Map) return this;
                var c = this;
                c.mode = (ParticleSystemCurveMode)node.Int("minMaxState");
                c.scalar = c.Bounded(node.Float("scalar", scalar));
                c.maxCurve = node.Get("maxCurve") is YNode max && max.Kind == YKind.Map ? BuiltinReaders.AnimationCurve(max) : null;
                c.minCurve = node.Get("minCurve") is YNode min && min.Kind == YKind.Map ? BuiltinReaders.AnimationCurve(min) : null;
                float lower = node.Get("minScalar") != null ? node.Float("minScalar")
                    : c.minCurve != null && c.minCurve.length > 0 ? c.minCurve[0].value * node.Float("scalar", scalar) : 0f;
                c.minScalar = c.Bounded(lower);
                if (!c.IsCurve)
                    c.maxCurve = c.minCurve = null;
                else
                {
                    c.maxCurve ??= One();
                    c.minCurve = c.mode == ParticleSystemCurveMode.TwoCurves ? c.minCurve ?? One() : null;
                }
                return c;
            }
        }

        /// <summary>A color that is a constant, a gradient, random between two colors or two
        /// gradients, or a random color from a gradient. Script fields serialize it as <c>m_Mode</c>,
        /// <c>m_GradientMin</c>, <c>m_GradientMax</c>, <c>m_ColorMin</c>, <c>m_ColorMax</c>; a module's
        /// serialized value uses the module form <see cref="Read"/> reads (6000.0.71f1,
        /// tests/projects/particlecurves gradient/fields).</summary>
        [Serializable]
        public partial struct MinMaxGradient
        {
            [SerializeField] internal ParticleSystemGradientMode m_Mode;
            [SerializeField] internal Gradient m_GradientMin;
            [SerializeField] internal Gradient m_GradientMax;
            [SerializeField] internal Color m_ColorMin;
            [SerializeField] internal Color m_ColorMax;

            public MinMaxGradient(Color color)
            {
                m_Mode = ParticleSystemGradientMode.Color;
                m_ColorMin = Color.black;
                m_ColorMax = color;
                m_GradientMax = null;
                m_GradientMin = null;
            }

            public MinMaxGradient(Gradient gradient)
            {
                m_Mode = ParticleSystemGradientMode.Gradient;
                m_ColorMin = Color.black;
                m_ColorMax = Color.black;
                m_GradientMax = gradient;
                m_GradientMin = null;
            }

            public MinMaxGradient(Color min, Color max)
            {
                m_Mode = ParticleSystemGradientMode.TwoColors;
                m_ColorMin = min;
                m_ColorMax = max;
                m_GradientMax = null;
                m_GradientMin = null;
            }

            public MinMaxGradient(Gradient min, Gradient max)
            {
                m_Mode = ParticleSystemGradientMode.TwoGradients;
                m_ColorMin = Color.black;
                m_ColorMax = Color.black;
                m_GradientMax = max;
                m_GradientMin = min;
            }

            public ParticleSystemGradientMode mode { get => m_Mode; set => m_Mode = value; }
            public Gradient gradientMax { get => m_GradientMax; set => m_GradientMax = value; }
            public Gradient gradientMin { get => m_GradientMin; set => m_GradientMin = value; }
            public Color colorMax { get => m_ColorMax; set => m_ColorMax = value; }
            public Color colorMin { get => m_ColorMin; set => m_ColorMin = value; }
            public Color color { get => m_ColorMax; set => m_ColorMax = value; }
            public Gradient gradient { get => m_GradientMax; set => m_GradientMax = value; }

            public Color Evaluate(float time) => Evaluate(time, 1f);

            /// <summary>The colour by mode; a gradient mode without its gradient throws (measured,
            /// 6000.0.71f1, tests/projects/particlecurves gradient/struct).</summary>
            public Color Evaluate(float time, float lerpFactor)
            {
                switch (m_Mode)
                {
                    case ParticleSystemGradientMode.Color:
                        return m_ColorMax;
                    case ParticleSystemGradientMode.Gradient:
                        return Required(m_GradientMax).Evaluate(time);
                    case ParticleSystemGradientMode.TwoColors:
                        return Color.Lerp(m_ColorMin, m_ColorMax, lerpFactor);
                    case ParticleSystemGradientMode.TwoGradients:
                        return Color.Lerp(Required(m_GradientMin).Evaluate(time), Required(m_GradientMax).Evaluate(time), lerpFactor);
                    case ParticleSystemGradientMode.RandomColor:
                        return Required(m_GradientMax).Evaluate(lerpFactor);
                    default:
                        return m_ColorMax;
                }
            }

            static Gradient Required(Gradient gradient) => gradient ?? throw new NullReferenceException("Object reference not set to an instance of an object.");

            /// <summary>The colour a particle takes from this value, as bytes (see
            /// <see cref="Unbound.Runtime.ParticleColors"/>): a gradient mode evaluates its gradients
            /// and rounds them to bytes first; two gradients blend those bytes. A missing gradient
            /// counts as white.</summary>
            internal Color32 EvaluateParticle(float time, float lerpFactor)
            {
                switch (m_Mode)
                {
                    case ParticleSystemGradientMode.Gradient:
                        return Unbound.Runtime.ParticleColors.ToBytes(m_GradientMax?.Evaluate(time) ?? Color.white);
                    case ParticleSystemGradientMode.TwoColors:
                        return Unbound.Runtime.ParticleColors.ToBytes(Color.Lerp(m_ColorMin, m_ColorMax, lerpFactor));
                    case ParticleSystemGradientMode.TwoGradients:
                        return Unbound.Runtime.ParticleColors.Lerp(Unbound.Runtime.ParticleColors.ToBytes(m_GradientMin?.Evaluate(time) ?? Color.white),
                            Unbound.Runtime.ParticleColors.ToBytes(m_GradientMax?.Evaluate(time) ?? Color.white), lerpFactor);
                    case ParticleSystemGradientMode.RandomColor:
                        return Unbound.Runtime.ParticleColors.ToBytes(m_GradientMax?.Evaluate(lerpFactor) ?? Color.white);
                    default:
                        return Unbound.Runtime.ParticleColors.ToBytes(m_ColorMax);
                }
            }

            public static implicit operator MinMaxGradient(Color color) => new MinMaxGradient(color);

            public static implicit operator MinMaxGradient(Gradient gradient) => new MinMaxGradient(gradient);

            internal MinMaxGradient Clone()
            {
                var g = this;
                g.m_GradientMax = Unbound.Runtime.ParticleCopies.Gradient(m_GradientMax);
                g.m_GradientMin = Unbound.Runtime.ParticleCopies.Gradient(m_GradientMin);
                return g;
            }

            /// <summary>The copy a module keeps when it is set: a missing gradient becomes the
            /// default white one (6000.0.71f1, tests/projects/particlecurves gradient/module/*/nullgradient).</summary>
            internal MinMaxGradient ForModule()
            {
                var g = Clone();
                g.m_GradientMax ??= new Gradient();
                g.m_GradientMin ??= new Gradient();
                return g;
            }

            /// <summary>A module's serialized form: `{minMaxState, minColor, maxColor, maxGradient, minGradient}`.</summary>
            internal static MinMaxGradient Read(Unbound.Runtime.YNode node)
            {
                if (node == null || node.Kind != Unbound.Runtime.YKind.Map)
                    return new MinMaxGradient(Color.white).ForModule();
                var g = new MinMaxGradient
                {
                    m_Mode = (ParticleSystemGradientMode)node.Int("minMaxState"),
                    m_ColorMin = node.Color("minColor", Color.white),
                    m_ColorMax = node.Color("maxColor", Color.white),
                };
                if (node.Get("maxGradient") is Unbound.Runtime.YNode max && max.Kind == Unbound.Runtime.YKind.Map)
                    g.m_GradientMax = Unbound.Runtime.BuiltinReaders.Gradient(max);
                if (node.Get("minGradient") is Unbound.Runtime.YNode min && min.Kind == Unbound.Runtime.YKind.Map)
                    g.m_GradientMin = Unbound.Runtime.BuiltinReaders.Gradient(min);
                // A module always holds both gradients (a missing node reads as the default white one).
                g.m_GradientMax ??= new Gradient();
                g.m_GradientMin ??= new Gradient();
                return g;
            }
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>Copies of the reference-typed parts of particle values.</summary>
    internal static class ParticleCopies
    {
        internal static AnimationCurve Curve(AnimationCurve source)
        {
            if (source == null) return null;
            var c = new AnimationCurve();
            c.CopyFrom(source);
            return c;
        }

        internal static Gradient Gradient(Gradient source)
        {
            if (source == null) return null;
            var g = new Gradient { mode = source.mode, colorSpace = source.colorSpace };
            g.SetKeys(source.colorKeys, source.alphaKeys);
            return g;
        }
    }

    /// <summary>Stable per-particle random numbers: a hash of the particle's seed and a salt per
    /// property, so a property keeps its random value over the particle's life.</summary>
    internal static class ParticleRandom
    {
        internal static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return x;
        }

        /// <summary>A value in [0, 1).</summary>
        internal static float Value(uint seed, uint salt) => (Hash(seed ^ Hash(salt * 0x9e3779b9U + 0x632be5abU)) >> 8) * (1f / 16777216f);

        // Salts of the per-particle random values.
        internal const uint Lifetime = 1, Speed = 2, SizeX = 3, SizeY = 4, SizeZ = 5, RotX = 6, RotY = 7, RotZ = 8, Color = 9,
            Flip = 10, Gravity = 11, VelX = 12, VelY = 13, VelZ = 14, SizeLife = 15, ColorLife = 16, RotLife = 17,
            Frame = 18, StartFrame = 19, Row = 20, FlipU = 21, FlipV = 22, Noise = 23, Limit = 24, Drag = 25,
            SizeSpeed = 26, ColorSpeed = 27, RotSpeed = 28, Orbital = 29, Radial = 30, SpeedMod = 31, Inherit = 32,
            MeshIndex = 33, Force = 34, Custom = 35, SubEmit = 36, NoiseStrength = 37, Collision = 40, Light = 48;
    }
}
