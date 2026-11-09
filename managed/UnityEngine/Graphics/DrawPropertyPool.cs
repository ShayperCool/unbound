using System;
using System.Collections.Generic;

namespace Unbound.Runtime
{
    /// <summary>
    /// The dictionaries that hold a draw's merged properties (its own, its lighting, probes and
    /// per-object lights) while draws are emitted: a renderer list or a single draw opens a scope,
    /// and the dictionaries handed out in it are reused once it ends. The command
    /// stream copies a draw's properties when the draw is written, so nothing keeps them past
    /// their scope.
    /// </summary>
    internal static class DrawPropertyPool
    {
        static readonly List<Dictionary<int, object>> s_Pool = new List<Dictionary<int, object>>();
        static int s_Used, s_Depth;

        internal readonly struct Scope : IDisposable
        {
            readonly int m_Start;

            internal Scope(int start) => m_Start = start;

            /// <summary>The copies handed out in the scope go back (those of the scopes around it
            /// stay theirs).</summary>
            public void Dispose()
            {
                s_Depth--;
                for (int i = m_Start; i < s_Used; i++) s_Pool[i].Clear();
                s_Used = m_Start;
            }
        }

        internal static Scope Begin()
        {
            s_Depth++;
            return new Scope(s_Used);
        }

        /// <summary>A copy of <paramref name="properties"/> (empty for none), in their order, for
        /// the caller to change until the scope ends; a new dictionary outside any scope.</summary>
        internal static Dictionary<int, object> Copy(Dictionary<int, object> properties)
        {
            if (s_Depth == 0) return properties != null ? new Dictionary<int, object>(properties) : new Dictionary<int, object>();
            Dictionary<int, object> copy;
            if (s_Used < s_Pool.Count) copy = s_Pool[s_Used];
            else s_Pool.Add(copy = new Dictionary<int, object>());
            s_Used++;
            if (properties != null)
                foreach (var pair in properties) copy.Add(pair.Key, pair.Value);
            return copy;
        }
    }

    /// <summary>Vectors a draw property holds as objects, boxed again only when one changes (a
    /// box is never changed, so a draw that kept one sees the value it was given): what
    /// renderers keep for the values they pass every frame.</summary>
    internal sealed class BoxedVectors
    {
        readonly UnityEngine.Vector4[] m_Values;
        readonly object[] m_Boxes;

        internal BoxedVectors(int count)
        {
            m_Values = new UnityEngine.Vector4[count];
            m_Boxes = new object[count];
        }

        /// <summary>A value as a draw property: kept boxed in <paramref name="boxes"/>, boxed
        /// anew without them.</summary>
        internal static object Of(BoxedVectors boxes, int slot, UnityEngine.Vector4 value) => boxes != null ? boxes.Box(slot, value) : value;

        internal object Box(int slot, UnityEngine.Vector4 value)
        {
            var box = m_Boxes[slot];
            if (box == null || !FloatBits.Same(m_Values[slot], value))
            {
                m_Values[slot] = value;
                m_Boxes[slot] = box = value;
            }
            return box;
        }
    }

    /// <summary>Whether values are the same bit for bit (-0 is not 0, a NaN is itself): what a
    /// cache of values passed on unchanged compares.</summary>
    internal static class FloatBits
    {
        internal static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        internal static bool Same(UnityEngine.Vector2 a, UnityEngine.Vector2 b) => Same(a.x, b.x) && Same(a.y, b.y);

        internal static bool Same(UnityEngine.Vector3 a, UnityEngine.Vector3 b) => Same(a.x, b.x) && Same(a.y, b.y) && Same(a.z, b.z);

        internal static bool Same(UnityEngine.Vector4 a, UnityEngine.Vector4 b) =>
            Same(a.x, b.x) && Same(a.y, b.y) && Same(a.z, b.z) && Same(a.w, b.w);
    }
}
