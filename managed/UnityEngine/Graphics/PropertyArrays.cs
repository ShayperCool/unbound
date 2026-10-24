using System;
using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>Array properties (materials and property blocks) as Unity keeps them: a copy of
    /// the values, whose length is fixed by the first set (longer arrays are cut to it, shorter ones
    /// leave the rest as it was).</summary>
    internal static class PropertyArrays
    {
        internal static void Set<T>(Dictionary<int, object> store, int nameID, IReadOnlyList<T> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Count == 0) throw new ArgumentException("Zero-sized array is not allowed.");
            T[] array;
            if (store.TryGetValue(nameID, out var existing) && existing is T[] same)
                array = same;
            else
                store[nameID] = array = new T[values.Count];
            for (int i = 0; i < Math.Min(array.Length, values.Count); i++) array[i] = values[i];
        }

        internal static T[] Get<T>(Dictionary<int, object> store, int nameID) =>
            store.TryGetValue(nameID, out var v) && v is T[] array ? (T[])array.Clone() : null;

        internal static void Get<T>(Dictionary<int, object> store, int nameID, List<T> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            values.Clear();
            if (store.TryGetValue(nameID, out var v) && v is T[] array) values.AddRange(array);
        }
    }
}
