using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Unbound
{
    /// <summary>
    /// The names enum values print with under Unity's Mono, which picks another of several names
    /// that share a value than .NET does (<c>CameraClearFlags.SolidColor</c> prints "SolidColor"
    /// where .NET prints "Color"). Mono takes the constants in declaration order, sorts them by
    /// their value as an unsigned 64-bit number unless they are already in order (an introspective
    /// sort: stable up to two elements, not in general), and then names a value by a binary search
    /// of the sorted values, whose first hit among equal values is the one it prints. A [Flags]
    /// enum, and the "F" format, take names from the highest sorted value down, each one whose bits
    /// are all left; a value they do not cover prints as a number. Matches the Unity 6000.0.71f1
    /// Editor, tests/projects/enumtext. Game code reaches this through
    /// <see cref="MonoNumberFormat"/>'s redirected calls.
    /// </summary>
    static class MonoEnumNames
    {
        /// <summary>An enum's constants sorted as Mono sorts them.</summary>
        internal sealed class Info
        {
            internal readonly ulong[] Values;
            internal readonly string[] Names;
            internal readonly bool Flags;
            internal readonly TypeCode Code;

            internal Info(Type type)
            {
                Code = Type.GetTypeCode(Enum.GetUnderlyingType(type));
                Flags = type.IsDefined(typeof(FlagsAttribute), false);
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken)); // declaration order
                int count = 0;
                foreach (var field in fields)
                    if (field.IsLiteral) count++;
                Values = new ulong[count];
                Names = new string[count];
                bool sorted = true;
                int i = 0;
                foreach (var field in fields)
                {
                    if (!field.IsLiteral) continue;
                    Values[i] = Bits(field.GetRawConstantValue());
                    Names[i] = field.Name;
                    if (i > 0 && Values[i - 1] > Values[i]) sorted = false;
                    i++;
                }
                if (!sorted) IntroSort.Sort(Values, Names);
            }

            /// <summary>The index Mono's binary search finds for <paramref name="value"/>, or -1.</summary>
            internal int Find(ulong value)
            {
                int lo = 0, hi = Values.Length - 1;
                while (lo <= hi)
                {
                    int mid = lo + ((hi - lo) >> 1);
                    if (Values[mid] == value) return mid;
                    if (Values[mid] < value) lo = mid + 1;
                    else hi = mid - 1;
                }
                return -1;
            }
        }

        static readonly ConcurrentDictionary<Type, Info> s_Infos = new();
        static readonly Func<Type, Info> s_Create = type => new Info(type);

        internal static Info Of(Type type) => s_Infos.GetOrAdd(type, s_Create);

        /// <summary>What a type argument of <see cref="ToString{T}(ref T)"/> is.</summary>
        static class Kind<T>
        {
            internal static readonly Info Enum = typeof(T).IsEnum ? Of(typeof(T)) : null;
            /// <summary>A nullable enum, float or double: printed through its boxed value.</summary>
            internal static readonly bool NullableMono = Nullable.GetUnderlyingType(typeof(T)) is { } inner
                && (inner.IsEnum || inner == typeof(float) || inner == typeof(double));
        }

        /// <summary>A constant's value as Mono orders it: sign-extended to 64 bits, then unsigned.</summary>
        static ulong Bits(object value) => value switch
        {
            sbyte v => (ulong)(long)v,
            short v => (ulong)(long)v,
            int v => (ulong)(long)v,
            long v => (ulong)v,
            byte v => v,
            ushort v => v,
            uint v => v,
            ulong v => v,
            char v => v,
            bool v => v ? 1UL : 0UL,
            _ => throw new ArgumentException("not an integral value: " + value.GetType()),
        };

        static ulong Bits<T>(ref T value, TypeCode code) => code switch
        {
            TypeCode.SByte => (ulong)(long)Unsafe.As<T, sbyte>(ref value),
            TypeCode.Int16 => (ulong)(long)Unsafe.As<T, short>(ref value),
            TypeCode.Int32 => (ulong)(long)Unsafe.As<T, int>(ref value),
            TypeCode.Int64 => (ulong)Unsafe.As<T, long>(ref value),
            TypeCode.Byte or TypeCode.Boolean => Unsafe.As<T, byte>(ref value),
            TypeCode.UInt16 or TypeCode.Char => Unsafe.As<T, ushort>(ref value),
            TypeCode.UInt32 => Unsafe.As<T, uint>(ref value),
            _ => Unsafe.As<T, ulong>(ref value),
        };

        /// <summary>value.ToString() called on a value held directly whose type is an enum, a
        /// nullable or a generic parameter (no boxing for a named enum value); floats and doubles
        /// are <see cref="MonoNumberFormat.ConstrainedToString{T}"/>'s.</summary>
        internal static string ToString<T>(ref T value)
        {
            var info = Kind<T>.Enum;
            if (info != null)
            {
                if (!info.Flags)
                {
                    int index = info.Find(Bits(ref value, info.Code));
                    if (index >= 0) return info.Names[index];
                }
                return ToString((Enum)(object)value, null);
            }
            if (Kind<T>.NullableMono) return (object)value is { } boxed ? MonoNumberFormat.ObjectToString(boxed) : "";
            // A reference type parameter may hold a boxed enum, float or exception (null throws, as
            // the call it replaces does).
            if (!typeof(T).IsValueType) return value is null ? throw new NullReferenceException() : MonoNumberFormat.ObjectToString(value);
            return value.ToString();
        }

        /// <summary>value.ToString(format): names in the "G" (default) and "F" formats, the rest as .NET.</summary>
        internal static string ToString(Enum value, string format)
        {
            if (string.IsNullOrEmpty(format) || format is "G" or "g")
            {
                var info = Of(value.GetType());
                if (info.Flags) return FlagsText(info, value);
                int index = info.Find(Bits(value));
                return index >= 0 ? info.Names[index] : value.ToString("D");
            }
            if (format is "F" or "f") return FlagsText(Of(value.GetType()), value);
            return value.ToString(format);
        }

        static ulong Bits(Enum value)
        {
            var info = Of(value.GetType());
            object boxed = value;
            return info.Code switch
            {
                TypeCode.SByte => (ulong)(long)(sbyte)boxed,
                TypeCode.Int16 => (ulong)(long)(short)boxed,
                TypeCode.Int32 => (ulong)(long)(int)boxed,
                TypeCode.Int64 => (ulong)(long)boxed,
                TypeCode.Byte => (byte)boxed,
                TypeCode.UInt16 => (ushort)boxed,
                TypeCode.UInt32 => (uint)boxed,
                TypeCode.UInt64 => (ulong)boxed,
                _ => Bits(Convert.ChangeType(boxed, Enum.GetUnderlyingType(value.GetType()))),
            };
        }

        static string FlagsText(Info info, Enum value)
        {
            ulong bits = Bits(value), left = bits;
            var values = info.Values;
            StringBuilder text = null;
            string single = null;
            for (int i = values.Length - 1; i >= 0; i--)
            {
                if (i == 0 && values[i] == 0) break;
                if ((left & values[i]) != values[i]) continue;
                left -= values[i];
                if (single == null && text == null) single = info.Names[i];
                else
                {
                    text ??= new StringBuilder(single);
                    text.Insert(0, ", ").Insert(0, info.Names[i]);
                }
            }
            if (left != 0) return value.ToString("D");
            if (bits == 0) return values.Length > 0 && values[0] == 0 ? info.Names[0] : "0";
            return text?.ToString() ?? single;
        }

        /// <summary>Enum.GetName / Type.GetEnumName: the binary search's name, or null.</summary>
        internal static string GetName(Type enumType, object value)
        {
            if (enumType == null || value == null || !enumType.IsEnum || !IsIntegral(value)) return Enum.GetName(enumType, value);
            int index = Of(enumType).Find(value is Enum e ? Bits(e) : Bits(value));
            return index >= 0 ? Of(enumType).Names[index] : null;
        }

        /// <summary>Enum.Format: names in the "G" and "F" formats, the rest as .NET.</summary>
        internal static string Format(Type enumType, object value, string format)
        {
            // (A value of another enum, or of another integral type than the enum's, throws as there.)
            if (enumType == null || value == null || format == null || !enumType.IsEnum || !IsIntegral(value)
                || value.GetType() != enumType && value.GetType() != Enum.GetUnderlyingType(enumType) || format is not ("G" or "g" or "F" or "f"))
                return Enum.Format(enumType, value, format);
            return ToString((Enum)Enum.ToObject(enumType, value), format);
        }

        static bool IsIntegral(object value) =>
            value is Enum or sbyte or short or int or long or byte or ushort or uint or ulong;

        /// <summary>The introspective sort of Mono's class library (the .NET Framework 4.5 one) on
        /// values with their names: equal values may change places, as they do there.</summary>
        static class IntroSort
        {
            const int InsertionSortThreshold = 16;

            internal static void Sort(ulong[] keys, string[] items)
            {
                if (keys.Length >= 2) Sort(keys, items, 0, keys.Length - 1, 2 * FloorLog2PlusOne(keys.Length));
            }

            static int FloorLog2PlusOne(int n)
            {
                int result = 0;
                for (; n >= 1; n /= 2) result++;
                return result;
            }

            static void Sort(ulong[] keys, string[] items, int lo, int hi, int depthLimit)
            {
                while (hi > lo)
                {
                    int size = hi - lo + 1;
                    if (size <= InsertionSortThreshold)
                    {
                        if (size == 2) SwapIfGreater(keys, items, lo, hi);
                        else if (size == 3)
                        {
                            SwapIfGreater(keys, items, lo, hi - 1);
                            SwapIfGreater(keys, items, lo, hi);
                            SwapIfGreater(keys, items, hi - 1, hi);
                        }
                        else InsertionSort(keys, items, lo, hi);
                        return;
                    }
                    if (depthLimit == 0)
                    {
                        HeapSort(keys, items, lo, hi);
                        return;
                    }
                    depthLimit--;
                    int pivot = PickPivotAndPartition(keys, items, lo, hi);
                    Sort(keys, items, pivot + 1, hi, depthLimit);
                    hi = pivot - 1;
                }
            }

            static void SwapIfGreater(ulong[] keys, string[] items, int a, int b)
            {
                if (a != b && keys[a] > keys[b]) Swap(keys, items, a, b);
            }

            static void Swap(ulong[] keys, string[] items, int i, int j)
            {
                if (i == j) return;
                (keys[i], keys[j]) = (keys[j], keys[i]);
                (items[i], items[j]) = (items[j], items[i]);
            }

            static int PickPivotAndPartition(ulong[] keys, string[] items, int lo, int hi)
            {
                int middle = lo + (hi - lo) / 2;
                SwapIfGreater(keys, items, lo, middle);
                SwapIfGreater(keys, items, lo, hi);
                SwapIfGreater(keys, items, middle, hi);
                ulong pivot = keys[middle];
                Swap(keys, items, middle, hi - 1);
                int left = lo, right = hi - 1;
                while (left < right)
                {
                    while (pivot > keys[++left]) { }
                    while (pivot < keys[--right]) { }
                    if (left >= right) break;
                    Swap(keys, items, left, right);
                }
                Swap(keys, items, left, hi - 1);
                return left;
            }

            static void HeapSort(ulong[] keys, string[] items, int lo, int hi)
            {
                int n = hi - lo + 1;
                for (int i = n / 2; i >= 1; i--) DownHeap(keys, items, i, n, lo);
                for (int i = n; i > 1; i--)
                {
                    Swap(keys, items, lo, lo + i - 1);
                    DownHeap(keys, items, 1, i - 1, lo);
                }
            }

            static void DownHeap(ulong[] keys, string[] items, int i, int n, int lo)
            {
                ulong key = keys[lo + i - 1];
                string item = items[lo + i - 1];
                while (i <= n / 2)
                {
                    int child = 2 * i;
                    if (child < n && keys[lo + child - 1] < keys[lo + child]) child++;
                    if (keys[lo + child - 1] < key) break;
                    keys[lo + i - 1] = keys[lo + child - 1];
                    items[lo + i - 1] = items[lo + child - 1];
                    i = child;
                }
                keys[lo + i - 1] = key;
                items[lo + i - 1] = item;
            }

            static void InsertionSort(ulong[] keys, string[] items, int lo, int hi)
            {
                for (int i = lo; i < hi; i++)
                {
                    int j = i;
                    ulong key = keys[i + 1];
                    string item = items[i + 1];
                    while (j >= lo && key < keys[j])
                    {
                        keys[j + 1] = keys[j];
                        items[j + 1] = items[j];
                        j--;
                    }
                    keys[j + 1] = key;
                    items[j + 1] = item;
                }
            }
        }
    }
}
