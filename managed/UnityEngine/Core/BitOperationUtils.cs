using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    /// <summary>Bit-mask helpers shared by rendering layer mask controls.</summary>
    internal static partial class BitOperationUtils
    {
        internal static int CountBits(int mask) => CountBits((uint)mask);

        internal static int CountBits(uint mask)
        {
            int count = 0;
            while (mask != 0)
            {
                count += (int)(mask & 1u);
                mask >>= 1;
            }
            return count;
        }

        internal static uint ModifyMaskByValuesArrayAndBitCount(uint mask, IEnumerable<int> values, int bitCount = 32)
        {
            AssertBitCount(bitCount);
            uint result = 0u;
            int maxIndex = BitCountToIndex(bitCount);
            foreach (int raw in values)
            {
                uint bit = (uint)raw;
                if (AnyBitMatch(mask, bit) && IsValueSmallerOrEqualThanIndex(bit, maxIndex))
                    result += bit;
            }
            return result;
        }

        internal static bool AreAllBitsSetForValues(uint mask, IEnumerable<int> values, int bitCount = 32)
        {
            AssertBitCount(bitCount);
            int maxIndex = BitCountToIndex(bitCount);
            foreach (int raw in values)
            {
                uint bit = (uint)raw;
                if (!AnyBitMatch(mask, bit) || IsValueBiggerOrEqualThanIndex(bit, maxIndex))
                    return false;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint IndexToValue(int index)
        {
            AssertIndex(index);
            return 1u << index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsValueSmallerThanIndex(uint value, int index)
        {
            AssertIndex(index);
            return value < IndexToValue(index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsValueBiggerThanIndex(uint value, int index)
        {
            AssertIndex(index);
            return value > IndexToValue(index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsValueSmallerOrEqualThanIndex(uint value, int index)
        {
            AssertIndex(index);
            return value <= IndexToValue(index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsValueBiggerOrEqualThanIndex(uint value, int index)
        {
            AssertIndex(index);
            return value >= IndexToValue(index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool AnyBitMatch(uint mask, uint value) => (mask & value) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int BitCountToIndex(int bitCount)
        {
            AssertBitCount(bitCount);
            return bitCount - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AssertBitCount(int bitCount) =>
            Debug.Assert(bitCount >= 1 && bitCount <= 32, "Bit count must be between 1 and 32.");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AssertIndex(int index) =>
            Debug.Assert(index >= 0 && index <= 31, "Index must be between 0 and 31.");
    }
}
