using System;
using System.Runtime.InteropServices;
using Unity.IntegerTime;
using UnityEngine;

namespace UnityEngine
{
    public partial class Time
    {
        /// <summary>The frame's time at the default tick rate, truncated (as the Editor reports it).</summary>
        public static RationalTime timeAsRational => RationalTime.FromDouble(timeAsDouble, RationalTime.TicksPerSecond.DefaultTicksPerSecond);
    }
}

namespace Unity.IntegerTime
{
    /// <summary>Time as a count of ticks of 1/141120000 s (a rate every common frame and sample
    /// rate divides).</summary>
    [Serializable]
    public partial struct DiscreteTime : IEquatable<DiscreteTime>, IFormattable, IComparable<DiscreteTime>
    {
        [StructLayout(LayoutKind.Explicit)]
        struct LongDoubleUnion
        {
            [FieldOffset(0)] public long longValue;
            [FieldOffset(0)] public double doubleValue;
        }

        [SerializeField] public long Value;

        public static readonly DiscreteTime Zero = default;
        public static readonly DiscreteTime MinValue = new DiscreteTime(long.MinValue, 0);
        public static readonly DiscreteTime MaxValue = new DiscreteTime(long.MaxValue, 0);

        const uint NonPow2Tps = 275625u;
        static readonly int TicksPerSecondBits = (int)Mathf.Ceil(Mathf.Log(141120000f, 2f));
        static readonly int NonPow2TpsBits = (int)Mathf.Ceil(Mathf.Log(275625f, 2f));

        public const uint TicksPerSecond = 141120000u;
        public const double Tick = 7.0861678004535145E-09;
        public const long MaxValueSeconds = 65358361939L;
        public const long MinValueSeconds = -65358361939L;
        public const uint Tick5Fps = 28224000u;
        public const uint Tick10Fps = 14112000u;
        public const uint Tick12Fps = 11760000u;
        public const uint Tick15Fps = 9408000u;
        public const uint Tick2397Fps = 5885880u;
        public const uint Tick24Fps = 5880000u;
        public const uint Tick25Fps = 5644800u;
        public const uint Tick2997Fps = 4708704u;
        public const uint Tick30Fps = 4704000u;
        public const uint Tick48Fps = 2940000u;
        public const uint Tick50Fps = 2822400u;
        public const uint Tick5995Fps = 2354352u;
        public const uint Tick60Fps = 2352000u;
        public const uint Tick90Fps = 1568000u;
        public const uint Tick11988Fps = 1177176u;
        public const uint Tick120Fps = 1176000u;
        public const uint Tick240Fps = 588000u;
        public const uint Tick1000Fps = 141120u;
        public const uint Tick8Khz = 17640u;
        public const uint Tick16Khz = 8820u;
        public const uint Tick22Khz = 6400u;
        public const uint Tick44Khz = 3200u;
        public const uint Tick48Khz = 2940u;
        public const uint Tick88Khz = 1600u;
        public const uint Tick96Khz = 1470u;
        public const uint Tick192Khz = 735u;

        public DiscreteTime(DiscreteTime x) { Value = x.Value; }
        /// <summary>Seconds.</summary>
        public DiscreteTime(float v) { Value = (long)Math.Round((double)v * TicksPerSecond); }
        /// <summary>Seconds.</summary>
        public DiscreteTime(double v) { Value = (long)Math.Round(v * TicksPerSecond); }
        /// <summary>Whole seconds.</summary>
        public DiscreteTime(long v) { Value = v * TicksPerSecond; }
        /// <summary>Whole seconds.</summary>
        public DiscreteTime(int v) { Value = (long)v * TicksPerSecond; }
        DiscreteTime(long ticks, int _) { Value = ticks; }

        public static DiscreteTime FromTicks(long v) => new DiscreteTime(v, 0);

        public static explicit operator DiscreteTime(float v) => new DiscreteTime(v);
        public static explicit operator DiscreteTime(double v) => new DiscreteTime(v);
        public static explicit operator float(DiscreteTime d) => (float)((double)d.Value / TicksPerSecond);
        public static explicit operator double(DiscreteTime d) => (double)d.Value / TicksPerSecond;
        public static implicit operator RationalTime(DiscreteTime t) => new RationalTime(t.Value, RationalTime.TicksPerSecond.DiscreteTimeRate);

        public static bool operator ==(DiscreteTime lhs, DiscreteTime rhs) => lhs.Value == rhs.Value;
        public static bool operator !=(DiscreteTime lhs, DiscreteTime rhs) => lhs.Value != rhs.Value;
        public static bool operator <(DiscreteTime lhs, DiscreteTime rhs) => lhs.Value < rhs.Value;
        public static bool operator >(DiscreteTime lhs, DiscreteTime rhs) => lhs.Value > rhs.Value;
        public static bool operator <=(DiscreteTime lhs, DiscreteTime rhs) => lhs.Value <= rhs.Value;
        public static bool operator >=(DiscreteTime lhs, DiscreteTime rhs) => lhs.Value >= rhs.Value;
        public static DiscreteTime operator +(DiscreteTime lhs, DiscreteTime rhs) => FromTicks(lhs.Value + rhs.Value);
        public static DiscreteTime operator -(DiscreteTime lhs, DiscreteTime rhs) => FromTicks(lhs.Value - rhs.Value);
        public static DiscreteTime operator *(DiscreteTime lhs, long rhs) => FromTicks(lhs.Value * rhs);

        /// <summary>Scales by the integer part exactly and by the fraction through the largest
        /// power-of-two multiple of the tick rate's factors that fits, as Unity does.</summary>
        public static DiscreteTime operator *(DiscreteTime lhs, double s)
        {
            double fraction = Modf(s, out var whole);
            long ticks = lhs.Value * (long)whole;
            if (Math.Abs(fraction) >= Tick)
            {
                int bits = Lzcnt(Math.Abs(lhs.Value)) - 1;
                long unit = 1 << bits;
                if (bits >= TicksPerSecondBits)
                    unit = TicksPerSecond;
                else if (bits >= NonPow2TpsBits)
                    unit = (uint)(NonPow2Tps << (bits - NonPow2TpsBits));
                long divisor = (long)Math.Round(unit / fraction);
                ticks += lhs.Value * unit / divisor;
            }
            return FromTicks(ticks);
        }

        static double Modf(double x, out double i)
        {
            i = Math.Truncate(x);
            return x - i;
        }

        static int Lzcnt(long x)
        {
            if (x == 0) return 64;
            uint high = (uint)(x >> 32);
            uint word = high != 0 ? high : (uint)x;
            int bias = high != 0 ? 1054 : 1086;
            LongDoubleUnion u = default;
            u.doubleValue = 0.0;
            u.longValue = 4841369599423283200L + word;
            u.doubleValue -= 4503599627370496.0;
            return bias - (int)(u.longValue >> 52);
        }

        public static DiscreteTime operator *(DiscreteTime lhs, float s) => lhs * (double)s;
        public static DiscreteTime operator /(DiscreteTime lhs, double s) => lhs * (1.0 / s);
        public static DiscreteTime operator /(DiscreteTime lhs, long s) => FromTicks(lhs.Value / s);
        public static DiscreteTime operator %(DiscreteTime lhs, DiscreteTime rhs) => FromTicks(lhs.Value % rhs.Value);
        public static DiscreteTime operator -(DiscreteTime lhs) => FromTicks(-lhs.Value);

        public readonly bool Equals(DiscreteTime rhs) => Value == rhs.Value;
        public override readonly bool Equals(object o) => Equals((DiscreteTime)o);
        public override readonly int GetHashCode() => Value.GetHashCode();
        public override readonly string ToString() => Unbound.MonoNumberFormat.ToString((double)this);
        public readonly string ToString(string format, IFormatProvider formatProvider) => Unbound.MonoNumberFormat.VirtualToString((double)this, format, formatProvider);
        public readonly int CompareTo(DiscreteTime other) => Value.CompareTo(other.Value);
    }

    public static partial class DiscreteTimeTimeExtensions
    {
        public static DiscreteTime Abs(this DiscreteTime lhs) => DiscreteTime.FromTicks(Math.Abs(lhs.Value));
        public static DiscreteTime Min(this DiscreteTime lhs, DiscreteTime rhs) => DiscreteTime.FromTicks(Math.Min(lhs.Value, rhs.Value));
        public static DiscreteTime Max(this DiscreteTime lhs, DiscreteTime rhs) => DiscreteTime.FromTicks(Math.Max(lhs.Value, rhs.Value));
        public static DiscreteTime Clamp(this DiscreteTime x, DiscreteTime a, DiscreteTime b) => a.Max(b.Min(x));
        public static DiscreteTime Floor(this DiscreteTime x) => (DiscreteTime)Math.Floor((double)x);
        public static DiscreteTime Select(this DiscreteTime a, DiscreteTime b, bool c) => DiscreteTime.FromTicks(c ? b.Value : a.Value);
    }

    /// <summary>A count of ticks at a rational rate. The arithmetic is native in Unity; its rules
    /// here were measured in the Editor (tests/projects/input/Reference/integertime-6000.0.71f1.jsonl).</summary>
    [Serializable]
    public partial struct RationalTime
    {
        [Serializable]
        public partial struct TicksPerSecond : IEquatable<TicksPerSecond>
        {
            [SerializeField] uint m_Numerator;
            [SerializeField] uint m_Denominator;

            public static readonly TicksPerSecond DefaultTicksPerSecond = new TicksPerSecond(141120000u);
            public static readonly TicksPerSecond TicksPerSecond24 = new TicksPerSecond(24u);
            public static readonly TicksPerSecond TicksPerSecond25 = new TicksPerSecond(25u);
            public static readonly TicksPerSecond TicksPerSecond30 = new TicksPerSecond(30u);
            public static readonly TicksPerSecond TicksPerSecond50 = new TicksPerSecond(50u);
            public static readonly TicksPerSecond TicksPerSecond60 = new TicksPerSecond(60u);
            public static readonly TicksPerSecond TicksPerSecond120 = new TicksPerSecond(120u);
            public static readonly TicksPerSecond TicksPerSecond2397 = new TicksPerSecond(24000u, 1001u);
            public static readonly TicksPerSecond TicksPerSecond2425 = new TicksPerSecond(25000u, 1001u);
            public static readonly TicksPerSecond TicksPerSecond2997 = new TicksPerSecond(30000u, 1001u);
            public static readonly TicksPerSecond TicksPerSecond5994 = new TicksPerSecond(60000u, 1001u);
            public static readonly TicksPerSecond TicksPerSecond11988 = new TicksPerSecond(120000u, 1001u);
            internal static readonly TicksPerSecond DiscreteTimeRate = new TicksPerSecond(141120000u);

            public TicksPerSecond(uint num, uint den = 1u)
            {
                m_Numerator = num;
                m_Denominator = den;
                Simplify(ref m_Numerator, ref m_Denominator);
            }

            public readonly uint Numerator => m_Numerator;
            public readonly uint Denominator => m_Denominator;
            /// <summary>Measured: any numerator (0 too) over a nonzero denominator.</summary>
            public readonly bool Valid => m_Denominator != 0;

            public readonly bool Equals(TicksPerSecond rhs) => m_Numerator == rhs.m_Numerator && m_Denominator == rhs.m_Denominator;
            public override readonly bool Equals(object rhs) => rhs is TicksPerSecond other && Equals(other);
            public override readonly int GetHashCode() => HashCode.Combine(m_Numerator, m_Denominator);

            static void Simplify(ref uint num, ref uint den)
            {
                if (den > 1 && num != 0)
                {
                    uint gcd = Gcd(num, den);
                    num /= gcd;
                    den /= gcd;
                }
            }

            static uint Gcd(uint a, uint b)
            {
                while (true)
                {
                    if (a == 0) return b;
                    b %= a;
                    if (b == 0) return a;
                    a %= b;
                }
            }
        }

        [SerializeField] long m_Count;
        [SerializeField] TicksPerSecond m_TicksPerSecond;

        public RationalTime(long count, TicksPerSecond ticks)
        {
            m_Count = count;
            m_TicksPerSecond = ticks;
        }

        public long Count => m_Count;
        public TicksPerSecond Ticks => m_TicksPerSecond;

        /// <summary>Measured: the count is truncated toward zero.</summary>
        public static RationalTime FromDouble(double t, TicksPerSecond ticksPerSecond)
        {
            if (!ticksPerSecond.Valid) throw new ArgumentException("Invalid ticks per second");
            return new RationalTime((long)(t * ticksPerSecond.Numerator / ticksPerSecond.Denominator), ticksPerSecond);
        }

        public static explicit operator DiscreteTime(RationalTime t) => DiscreteTime.FromTicks(t.Convert(TicksPerSecond.DiscreteTimeRate).Count);
    }

    public static partial class RationalTimeExtensions
    {
        public static double ToDouble(this RationalTime value)
        {
            var rate = value.Ticks;
            if (!rate.Valid) throw new ArgumentException("Invalid ticks per second");
            return value.Count * ((double)rate.Denominator / rate.Numerator);
        }

        public static bool IsValid(this RationalTime value) => value.Ticks.Valid;

        /// <summary>Measured: to a finer rate the count rounds away from zero, to a coarser one it
        /// is truncated toward zero.</summary>
        public static RationalTime Convert(this RationalTime time, RationalTime.TicksPerSecond rate)
        {
            var from = time.Ticks;
            if (!from.Valid || !rate.Valid) throw new ArgumentException("Invalid ticks per second");
            if (from.Equals(rate)) return time;
            // count * (rate / from) = count * rate.num * from.den / (rate.den * from.num)
            Int128 numerator = (Int128)time.Count * rate.Numerator * from.Denominator;
            Int128 denominator = (Int128)rate.Denominator * from.Numerator;
            if (denominator == 0) throw new ArgumentException("Invalid ticks per second");
            Int128 quotient = numerator / denominator;
            Int128 remainder = numerator % denominator;
            bool finer = (ulong)rate.Numerator * from.Denominator > (ulong)from.Numerator * rate.Denominator;
            if (finer && remainder != 0) quotient += numerator < 0 ? -1 : 1;
            return new RationalTime((long)quotient, rate);
        }

        static RationalTime.TicksPerSecond SameRate(RationalTime lhs, RationalTime rhs)
        {
            if (!lhs.Ticks.Equals(rhs.Ticks)) throw new ArgumentException("Mixed rate operations are not supported");
            return lhs.Ticks;
        }

        public static RationalTime Add(this RationalTime lhs, RationalTime rhs) => new RationalTime(lhs.Count + rhs.Count, SameRate(lhs, rhs));
        public static RationalTime Subtract(this RationalTime lhs, RationalTime rhs) => new RationalTime(lhs.Count - rhs.Count, SameRate(lhs, rhs));
        public static RationalTime Multiply(this RationalTime lhs, RationalTime rhs) => new RationalTime(lhs.Count * rhs.Count, SameRate(lhs, rhs));
        public static RationalTime Divide(this RationalTime lhs, RationalTime rhs) => new RationalTime(lhs.Count / rhs.Count, SameRate(lhs, rhs));
    }
}
