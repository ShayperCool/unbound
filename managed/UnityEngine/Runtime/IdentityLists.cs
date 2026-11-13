using System.Collections.Generic;

namespace Unbound.Runtime
{
    /// <summary>List lookups by object identity. Engine bookkeeping must not go through
    /// UnityEngine.Object.Equals: scripts may override it with members that need a live object,
    /// and two destroyed objects compare equal under Unity's own override.</summary>
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
}
