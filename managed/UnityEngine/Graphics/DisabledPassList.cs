using System;
using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>
    /// A material's disabled passes (<c>disabledShaderPasses</c>), by LightMode tag, compared
    /// case-insensitively (measured: tests/projects/builtinrp passes.*). Kept as the Editor keeps
    /// them (matches the Unity 6000.0.71f1 Editor, tests/projects/urpwrites exit.*): the saved
    /// entries in their order and spelling; a pass disabled is added at the end, spelled in upper
    /// case when it is one of the engine's own pass names, else as given; a pass enabled again
    /// leaves its place to the last entry. A list nothing changed is written as it was read.
    /// </summary>
    internal sealed class DisabledPassList
    {
        /// <summary>The pass names the Editor stores in upper case (measured; other names, such
        /// as <c>Vertex</c> and the render pipelines' own, keep the spelling given).</summary>
        static readonly string[] s_UpperCaseNames =
        {
            "Always", "ForwardBase", "ForwardAdd", "Deferred", "ShadowCaster", "MotionVectors", "Meta", "GrabPass",
            "SRPDefaultUnlit",
        };

        readonly List<string> m_Names = new List<string>();

        internal int Count => m_Names.Count;

        internal string this[int index] => m_Names[index];

        internal bool Contains(string name) => IndexOf(name) >= 0;

        int IndexOf(string name)
        {
            for (int i = 0; i < m_Names.Count; i++)
                if (string.Equals(m_Names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>A saved entry, as the file spells it.</summary>
        internal void AddAsRead(string name)
        {
            if (!Contains(name)) m_Names.Add(name);
        }

        /// <summary>Disables a pass: false when it was disabled already.</summary>
        internal bool Add(string name)
        {
            if (Contains(name)) return false;
            foreach (var upper in s_UpperCaseNames)
                if (string.Equals(upper, name, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.ToUpperInvariant();
                    break;
                }
            m_Names.Add(name);
            return true;
        }

        /// <summary>Enables a pass again: the last entry takes its place. False when it was not disabled.</summary>
        internal bool Remove(string name)
        {
            int index = IndexOf(name);
            if (index < 0) return false;
            int last = m_Names.Count - 1;
            m_Names[index] = m_Names[last];
            m_Names.RemoveAt(last);
            return true;
        }

        internal void CopyFrom(DisabledPassList source)
        {
            m_Names.Clear();
            m_Names.AddRange(source.m_Names);
        }
    }
}
