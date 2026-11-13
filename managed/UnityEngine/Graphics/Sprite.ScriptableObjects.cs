using System;
using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>The scriptable objects a Sprite keeps (saved as m_ScriptableObjects): a list without
    /// repeats, in the order they were added (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/spriteassets).</summary>
    public sealed partial class Sprite
    {
        /// <summary>Null when none was ever added; a destroyed entry reads as null.</summary>
        internal List<ScriptableObject> m_ScriptableObjects;

        public uint GetScriptableObjectsCount() => (uint)(m_ScriptableObjects?.Count ?? 0);

        /// <summary>Fills the array from the start with as many entries as both hold.</summary>
        public uint GetScriptableObjects(ScriptableObject[] scriptableObjects)
        {
            if (scriptableObjects == null) throw new ArgumentNullException(nameof(scriptableObjects));
            int count = Math.Min(scriptableObjects.Length, m_ScriptableObjects?.Count ?? 0);
            for (int i = 0; i < count; i++)
            {
                var value = m_ScriptableObjects[i];
                scriptableObjects[i] = value != null ? value : null;
            }
            return (uint)count;
        }

        /// <summary>Appends the object unless the list holds it already.</summary>
        public bool AddScriptableObject(ScriptableObject obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            m_ScriptableObjects ??= new List<ScriptableObject>();
            if (Holds(obj)) return false;
            m_ScriptableObjects.Add(obj);
            return true;
        }

        public bool RemoveScriptableObjectAt(uint i)
        {
            if (m_ScriptableObjects == null || i >= (uint)m_ScriptableObjects.Count) return false;
            m_ScriptableObjects.RemoveAt((int)i);
            return true;
        }

        /// <summary>Replaces an entry, unless the list holds the object already (also at that
        /// index).</summary>
        public bool SetScriptableObjectAt(ScriptableObject obj, uint i)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (m_ScriptableObjects == null || i >= (uint)m_ScriptableObjects.Count || Holds(obj)) return false;
            m_ScriptableObjects[(int)i] = obj;
            return true;
        }

        bool Holds(ScriptableObject obj)
        {
            foreach (var value in m_ScriptableObjects)
                if (ReferenceEquals(value, obj)) return true;
            return false;
        }
    }
}
