using System;

namespace UnityEngine
{
    public sealed partial class ParticleSystem
    {
        /// <summary>
        /// A value that is a constant, a curve, or random between two constants or two curves.
        /// It is stored the way Unity serializes it (and the way a value reads back from a particle
        /// system): one scalar is the constant, the upper constant and the curve multiplier at once.
        /// Storing it that way lets script fields of this type read from scenes and prefabs as-is.
        /// </summary>
        [Serializable]
        public partial struct MinMaxCurve
        {
            [SerializeField] ParticleSystemCurveMode minMaxState;
            [SerializeField] float scalar;
            [SerializeField] float minScalar;
            [SerializeField] AnimationCurve maxCurve;
            [SerializeField] AnimationCurve minCurve;

            public MinMaxCurve(float constant)
            {
                minMaxState = ParticleSystemCurveMode.Constant;
                scalar = constant;
                minScalar = 0f;
                maxCurve = null;
                minCurve = null;
            }

            public MinMaxCurve(float multiplier, AnimationCurve curve)
            {
                minMaxState = ParticleSystemCurveMode.Curve;
                scalar = multiplier;
                minScalar = 0f;
                maxCurve = curve;
                minCurve = null;
            }

            public MinMaxCurve(float multiplier, AnimationCurve min, AnimationCurve max)
            {
                minMaxState = ParticleSystemCurveMode.TwoCurves;
                scalar = multiplier;
                minScalar = 0f;
                maxCurve = max;
                minCurve = min;
            }

            public MinMaxCurve(float min, float max)
            {
                minMaxState = ParticleSystemCurveMode.TwoConstants;
                scalar = max;
                minScalar = min;
                maxCurve = null;
                minCurve = null;
            }

            public ParticleSystemCurveMode mode { get => minMaxState; set => minMaxState = value; }

            [Obsolete("Please use MinMaxCurve.curveMultiplier instead. (UnityUpgradable) -> UnityEngine.ParticleSystem/MinMaxCurve.curveMultiplier", false)]
            public float curveScalar { get => scalar; set => scalar = value; }

            public float curveMultiplier { get => scalar; set => scalar = value; }
            public AnimationCurve curveMax { get => maxCurve; set => maxCurve = value; }
            public AnimationCurve curveMin { get => minCurve; set => minCurve = value; }
            public float constantMax { get => scalar; set => scalar = value; }
            public float constantMin { get => minScalar; set => minScalar = value; }
            public float constant { get => scalar; set => scalar = value; }
            public AnimationCurve curve { get => maxCurve; set => maxCurve = value; }

            public float Evaluate(float time) => Evaluate(time, 1f);

            public float Evaluate(float time, float lerpFactor)
            {
                switch (minMaxState)
                {
                    case ParticleSystemCurveMode.Constant:
                        return scalar;
                    case ParticleSystemCurveMode.TwoConstants:
                        return Mathf.Lerp(minScalar, scalar, lerpFactor);
                    case ParticleSystemCurveMode.Curve:
                        return (maxCurve != null ? maxCurve.Evaluate(time) : 0f) * scalar;
                    case ParticleSystemCurveMode.TwoCurves:
                    {
                        float min = minCurve != null ? minCurve.Evaluate(time) : 0f;
                        float max = maxCurve != null ? maxCurve.Evaluate(time) : 0f;
                        return Mathf.Lerp(min, max, lerpFactor) * scalar;
                    }
                    default:
                        return scalar;
                }
            }

            public static implicit operator MinMaxCurve(float constant) => new MinMaxCurve(constant);

            /// <summary>True when the value can differ between particles (random modes).</summary>
            internal bool IsRandom => minMaxState == ParticleSystemCurveMode.TwoConstants || minMaxState == ParticleSystemCurveMode.TwoCurves;

            /// <summary>The largest value the curve can take (constant or sampled), for bounds and limits.</summary>
            internal float MaxValue
            {
                get
                {
                    switch (minMaxState)
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

            /// <summary>A copy that owns its curves (Unity hands out fresh curves on every read).</summary>
            internal MinMaxCurve Clone()
            {
                var c = this;
                c.maxCurve = Unbound.Runtime.ParticleCopies.Curve(maxCurve);
                c.minCurve = Unbound.Runtime.ParticleCopies.Curve(minCurve);
                return c;
            }

            /// <summary>The serialized form: `{minMaxState, scalar, minScalar, maxCurve, minCurve}`.
            /// Data written before `minScalar` existed kept the lower constant as the min curve's
            /// first key times the scalar.</summary>
            internal static MinMaxCurve Read(Unbound.Runtime.YNode node, float fallback)
            {
                if (node == null || node.Kind != Unbound.Runtime.YKind.Map)
                    return new MinMaxCurve(fallback);
                var c = new MinMaxCurve
                {
                    minMaxState = (ParticleSystemCurveMode)node.Int("minMaxState"),
                    scalar = node.Float("scalar", fallback),
                };
                if (node.Get("maxCurve") is Unbound.Runtime.YNode max && max.Kind == Unbound.Runtime.YKind.Map)
                    c.maxCurve = Unbound.Runtime.BuiltinReaders.AnimationCurve(max);
                if (node.Get("minCurve") is Unbound.Runtime.YNode min && min.Kind == Unbound.Runtime.YKind.Map)
                    c.minCurve = Unbound.Runtime.BuiltinReaders.AnimationCurve(min);
                if (node.Get("minScalar") != null)
                    c.minScalar = node.Float("minScalar");
                else if (c.minCurve != null && c.minCurve.length > 0)
                    c.minScalar = c.minCurve[0].value * c.scalar;
                return c;
            }
        }

        /// <summary>A color that is a constant, a gradient, random between two colors or two
        /// gradients, or a random color from a gradient. Stored the way Unity serializes it.</summary>
        [Serializable]
        public partial struct MinMaxGradient
        {
            [SerializeField] ParticleSystemGradientMode minMaxState;
            [SerializeField] Color minColor;
            [SerializeField] Color maxColor;
            [SerializeField] Gradient maxGradient;
            [SerializeField] Gradient minGradient;

            public MinMaxGradient(Color color)
            {
                minMaxState = ParticleSystemGradientMode.Color;
                minColor = Color.black;
                maxColor = color;
                maxGradient = null;
                minGradient = null;
            }

            public MinMaxGradient(Gradient gradient)
            {
                minMaxState = ParticleSystemGradientMode.Gradient;
                minColor = Color.black;
                maxColor = Color.black;
                maxGradient = gradient;
                minGradient = null;
            }

            public MinMaxGradient(Color min, Color max)
            {
                minMaxState = ParticleSystemGradientMode.TwoColors;
                minColor = min;
                maxColor = max;
                maxGradient = null;
                minGradient = null;
            }

            public MinMaxGradient(Gradient min, Gradient max)
            {
                minMaxState = ParticleSystemGradientMode.TwoGradients;
                minColor = Color.black;
                maxColor = Color.black;
                maxGradient = max;
                minGradient = min;
            }

            public ParticleSystemGradientMode mode { get => minMaxState; set => minMaxState = value; }
            public Gradient gradientMax { get => maxGradient; set => maxGradient = value; }
            public Gradient gradientMin { get => minGradient; set => minGradient = value; }
            public Color colorMax { get => maxColor; set => maxColor = value; }
            public Color colorMin { get => minColor; set => minColor = value; }
            public Color color { get => maxColor; set => maxColor = value; }
            public Gradient gradient { get => maxGradient; set => maxGradient = value; }

            public Color Evaluate(float time) => Evaluate(time, 1f);

            public Color Evaluate(float time, float lerpFactor)
            {
                switch (minMaxState)
                {
                    case ParticleSystemGradientMode.Color:
                        return maxColor;
                    case ParticleSystemGradientMode.Gradient:
                        return maxGradient != null ? maxGradient.Evaluate(time) : Color.white;
                    case ParticleSystemGradientMode.TwoColors:
                        return Color.Lerp(minColor, maxColor, lerpFactor);
                    case ParticleSystemGradientMode.TwoGradients:
                    {
                        var min = minGradient != null ? minGradient.Evaluate(time) : Color.white;
                        var max = maxGradient != null ? maxGradient.Evaluate(time) : Color.white;
                        return Color.Lerp(min, max, lerpFactor);
                    }
                    case ParticleSystemGradientMode.RandomColor:
                        return maxGradient != null ? maxGradient.Evaluate(lerpFactor) : Color.white;
                    default:
                        return maxColor;
                }
            }

            public static implicit operator MinMaxGradient(Color color) => new MinMaxGradient(color);

            public static implicit operator MinMaxGradient(Gradient gradient) => new MinMaxGradient(gradient);

            internal MinMaxGradient Clone()
            {
                var g = this;
                g.maxGradient = Unbound.Runtime.ParticleCopies.Gradient(maxGradient);
                g.minGradient = Unbound.Runtime.ParticleCopies.Gradient(minGradient);
                return g;
            }

            /// <summary>The serialized form: `{minMaxState, minColor, maxColor, maxGradient, minGradient}`.</summary>
            internal static MinMaxGradient Read(Unbound.Runtime.YNode node)
            {
                if (node == null || node.Kind != Unbound.Runtime.YKind.Map)
                    return new MinMaxGradient(Color.white);
                var g = new MinMaxGradient
                {
                    minMaxState = (ParticleSystemGradientMode)node.Int("minMaxState"),
                    minColor = node.Color("minColor", Color.white),
                    maxColor = node.Color("maxColor", Color.white),
                };
                if (node.Get("maxGradient") is Unbound.Runtime.YNode max && max.Kind == Unbound.Runtime.YKind.Map)
                    g.maxGradient = Unbound.Runtime.BuiltinReaders.Gradient(max);
                if (node.Get("minGradient") is Unbound.Runtime.YNode min && min.Kind == Unbound.Runtime.YKind.Map)
                    g.minGradient = Unbound.Runtime.BuiltinReaders.Gradient(min);
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
            MeshIndex = 33, Force = 34, Custom = 35, SubEmit = 36, NoiseStrength = 37, Collision = 40, Trail = 44;
    }
}
