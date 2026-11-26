using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public partial class Object
    {
        // A managed wrapper without an engine object has no identity, unlike an object
        // that was created and later destroyed (defaultstate, 6000.0.71f1).
        internal void MarkUncreated()
        {
            if (m_InstanceID != 0) ObjectRegistry.Unregister(this);
            m_InstanceID = 0;
            m_Destroyed = true;
        }

        static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> s_NoEngineClass =
            new System.Collections.Concurrent.ConcurrentDictionary<Type, bool>();

        /// <summary>Whether the nearest engine class of <paramref name="type"/> is Object itself.</summary>
        internal static bool HasNoEngineClass(Type type)
        {
            if (type == typeof(Object)) return true;
            if (type.Assembly == typeof(Object).Assembly) return false;
            return s_NoEngineClass.GetOrAdd(type, t =>
            {
                while (t.Assembly != typeof(Object).Assembly) t = t.BaseType;
                return t == typeof(Object);
            });
        }

        /// <summary>Whether the wrapper has no engine object behind it (and never had one).</summary>
        internal bool IsUncreated => m_InstanceID == 0 && m_Destroyed && Compat.UncreatedObjectWrappersMeasured;

        /// <summary>A wrapper without an engine object throws as Unity's bindings do: its error
        /// string's exception for an Editor placeholder, else a NullReferenceException.</summary>
        internal void ThrowIfUncreated()
        {
            if (IsUncreated) throw ObjectErrors.Receiver(this);
        }

        internal void PreserveCreationIdentity()
        {
            if (m_InstanceID == 0) m_InstanceID = ObjectRegistry.Register(this);
            m_Destroyed = false;
            if (m_Name == null) m_Name = "";
        }
    }
}
