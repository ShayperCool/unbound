using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Unbound
{
    /// <summary>
    /// Marks a generated compatibility stub (Generated/&lt;release&gt;/*.g.cs): a type or member that
    /// exists so game code binds, but has no implementation yet. The API gap report counts stubs as
    /// missing.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    internal sealed class StubAttribute : Attribute
    {
    }
}

namespace Unbound.Runtime
{
    /// <summary>What the bodies of generated stubs call.</summary>
    internal static class Stubs
    {
        static readonly ConcurrentDictionary<string, int> Hits = new(StringComparer.Ordinal);

        /// <summary>Records a call of an unimplemented member; the first call of each logs a
        /// warning.</summary>
        public static void NotImplemented(string member)
        {
            // Counted before logging, so a stub reached while logging does not log again.
            if (Hits.AddOrUpdate(member, 1, (_, n) => n + 1) == 1)
                UnityEngine.Debug.LogWarning("Unbound: " + member + " is not implemented yet");
        }

        /// <summary>The stubs called so far and how often, most called first.</summary>
        public static IReadOnlyList<KeyValuePair<string, int>> HitReport() =>
            Hits.OrderByDescending(h => h.Value).ThenBy(h => h.Key, StringComparer.Ordinal).ToList();

        /// <summary>A writable location for stubs that return by reference.</summary>
        public static ref T Ref<T>() => ref Box<T>.Value;

        static class Box<T>
        {
            public static T Value;
        }
    }
}
