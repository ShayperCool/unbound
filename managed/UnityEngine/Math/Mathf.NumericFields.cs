using System;

namespace UnityEngine
{
    // Range limits and rounding the numeric UI fields use for typed and dragged values (matches the
    // Unity 6000.0.71f1 Editor, tests/projects/numericfields).
    public partial struct Mathf
    {
        /// <summary>The most decimals a rounded value keeps (System.Math.Round accepts 15).</summary>
        internal const int kMaxDecimals = 15;

        /// <summary>The nearest float: infinities stay, finite values beyond the float range saturate.</summary>
        internal static float ClampToFloat(double value)
        {
            if (double.IsPositiveInfinity(value)) return float.PositiveInfinity;
            if (double.IsNegativeInfinity(value)) return float.NegativeInfinity;
            if (value < float.MinValue) return float.MinValue;
            if (value > float.MaxValue) return float.MaxValue;
            return (float)value;
        }

        internal static int ClampToInt(long value) =>
            value < int.MinValue ? int.MinValue : value > int.MaxValue ? int.MaxValue : (int)value;

        internal static uint ClampToUInt(long value) =>
            value < uint.MinValue ? uint.MinValue : value > uint.MaxValue ? uint.MaxValue : (uint)value;

        /// <summary>Decimals that resolve steps of <paramref name="minDifference"/>, at most kMaxDecimals.</summary>
        internal static int GetNumberOfDecimalsForMinimumDifference(float minDifference) =>
            Clamp(-FloorToInt(Log10(Abs(minDifference))), 0, kMaxDecimals);

        internal static int GetNumberOfDecimalsForMinimumDifference(double minDifference) =>
            (int)Math.Max(0.0, -Math.Floor(Math.Log10(Math.Abs(minDifference))));

        /// <summary>The value rounded (half away from zero) to the decimals a step of
        /// <paramref name="minDifference"/> shows; a zero step keeps about six significant digits.</summary>
        internal static float RoundBasedOnMinimumDifference(float valueToRound, float minDifference)
        {
            if (minDifference == 0) return DiscardLeastSignificantDecimal(valueToRound);
            return (float)Math.Round(valueToRound, GetNumberOfDecimalsForMinimumDifference(minDifference), MidpointRounding.AwayFromZero);
        }

        internal static double RoundBasedOnMinimumDifference(double valueToRound, double minDifference)
        {
            if (minDifference == 0) return DiscardLeastSignificantDecimal(valueToRound);
            return Math.Round(valueToRound, GetNumberOfDecimalsForMinimumDifference(minDifference), MidpointRounding.AwayFromZero);
        }

        internal static float DiscardLeastSignificantDecimal(float v)
        {
            int decimals = Clamp((int)(5 - Log10(Abs(v))), 0, kMaxDecimals);
            return (float)Math.Round(v, decimals, MidpointRounding.AwayFromZero);
        }

        /// <summary>The double counterpart rounds half to even and gives 0 where more than kMaxDecimals
        /// decimals would be needed.</summary>
        internal static double DiscardLeastSignificantDecimal(double v)
        {
            int decimals = Math.Max(0, (int)(5 - Math.Log10(Math.Abs(v))));
            return decimals > kMaxDecimals ? 0 : Math.Round(v, decimals);
        }
    }
}
