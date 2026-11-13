using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Unbound.Runtime
{
    /// <summary>List lookups by object identity. Engine bookkeeping must not go through
    /// UnityEngine.Object.Equals or GetHashCode: scripts may override them with members that need
    /// a live object (the render pipeline core's ProbeVolume hashes its transform).</summary>
    internal static class IdentityLists
    {
        internal static int IndexOf<T>(List<T> list, T item) where T : class
        {
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], item)) return i;
            return -1;
        }

        internal static bool Contains<T>(List<T> list, T item) where T : class => IndexOf(list, item) >= 0;

        internal static bool Remove<T>(List<T> list, T item) where T : class
        {
            int index = IndexOf(list, item);
            if (index < 0) return false;
            list.RemoveAt(index);
            return true;
        }
    }

    /// <summary>Keys of an object and a name, the object compared by identity.</summary>
    internal sealed class IdentityKeyComparer<T> : IEqualityComparer<(T, string)> where T : class
    {
        internal static readonly IdentityKeyComparer<T> Instance = new IdentityKeyComparer<T>();

        public bool Equals((T, string) a, (T, string) b) => ReferenceEquals(a.Item1, b.Item1) && a.Item2 == b.Item2;

        public int GetHashCode((T, string) key) =>
            System.HashCode.Combine(RuntimeHelpers.GetHashCode(key.Item1), key.Item2);
    }
}
